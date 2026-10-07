using Ballast;
using Ballast.Net;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Net;

/// <summary>
/// A connected player (GDD §33): applies its own input immediately (prediction), sends it to the
/// host, and when a snapshot arrives snaps to the host's truth and replays inputs the host has not
/// yet applied (reconciliation). The train is re-simulated the same way: it is deterministic from
/// its state and controls, so the client predicts it too. Other players are shown interpolated
/// between snapshots, a little in the past.
/// </summary>
/// <param name="Source">For a <see cref="VoicePath.Mimic"/> frame, the enemy it comes from (T40); 0 otherwise.</param>
/// <param name="Gain">With <see cref="VoicePath.Fading"/>: how much of the speaker's voice is left, 0..1 (App. C.8).</param>
public readonly record struct VoiceFrame(byte Speaker, ushort Sequence, VoicePath Path, byte[] Opus, int Source = 0, double Gain = 1);

public sealed class ClientSession
{
    /// <summary>Remote players are drawn this many ticks behind the newest snapshot (100 ms at 30 Hz).</summary>
    public const int InterpolationTicks = 3;
    const int HistoryLength = 128;
    const int SnapshotBuffer = 32;

    ITransport _transport;
    readonly List<TransportEvent> _events = new();
    readonly List<PlayerSnapshot> _players = new();
    readonly NetWriter _writer = new();
    readonly (uint Seq, PlayerIntent Intent, PlayerState After)[] _history = new (uint, PlayerIntent, PlayerState)[HistoryLength];
    readonly InputFrame[] _redundant = new InputFrame[Messages.InputRedundancy];
    readonly List<(uint Tick, PlayerSnapshot[] Players)> _snapshots = new();

    uint _sequence;
    uint _newestSnapshotTick;
    bool _haveState;

    public ClientSession(ITransport transport, TrainOnLine train, TrainTuning trainTuning, PlayerTuning playerTuning, CombatTuning? combat = null)
        : this(transport, new World(train, combat), trainTuning, playerTuning)
    {
    }

    /// <summary>The name this player goes by, sent to the host once welcomed (the roster, the report).</summary>
    public string Name { get; set; } = "";

    /// <summary>The outfit this player comes in (note 298), sent with the name; <see cref="Messages.NoOutfit"/> for their id's.</summary>
    public byte Outfit { get; set; } = Messages.NoOutfit;

    /// <summary>
    /// Tries an outfit on (note 298): asked of the host, which takes it only in the yard; the crew see it once it's sent
    /// back. Kept for the next Hello, too (a rejoin comes back in it).
    /// </summary>
    public void Wear(byte outfit)
    {
        Outfit = outfit;
        if (!_helloSent)
            return;
        Messages.WriteWear(_writer, outfit);
        _transport.Send(PeerId.Host, _writer.Written, Delivery.ReliableOrdered);
    }

    readonly Dictionary<int, byte[]> _reportChunks = [];
    int _reportCount;
    readonly Dictionary<int, byte[]> _filmChunks = [];
    int _filmCount;

    public ClientSession(ITransport transport, World world, TrainTuning trainTuning, PlayerTuning playerTuning)
    {
        World = world;
        // How long the host keeps an emote is how long it's drawn (note 298).
        world.EmoteTuning = playerTuning.Emotes;
        // Clients mirror enemies; only the host simulates them.
        _transport = transport;

        TrainTuning = trainTuning;
        PlayerTuning = playerTuning;
        Controls = new TrainControls { Reverser = 1, Brake = world.Train.BoilerTuning?.SteamDrive == true ? 1 : 0 };
    }

    public World World { get; }
    public TrainOnLine Train => World.Train;
    public TrainTuning TrainTuning { get; set; }
    /// <summary>The player tuning; its hand tuning is the world's too (<see cref="World.Hand"/>), so hot reload reaches both.</summary>
    public PlayerTuning PlayerTuning
    {
        get => _playerTuning;
        set
        {
            _playerTuning = value;
            World.Hand = value.Hand;
        }
    }
    PlayerTuning _playerTuning = null!;
    public byte? PlayerId { get; private set; }
    /// <summary>Voice frames from others, as they arrived. The game drains and decodes these.</summary>
    public Queue<VoiceFrame> VoiceFrames { get; } = new();
    readonly NetWriter _voiceWriter = new();

    /// <summary>Sends one encoded voice frame to the host, which forwards it to whoever it reaches.</summary>
    public void SendVoice(ushort sequence, bool radio, ReadOnlySpan<byte> opus)
    {
        if (PlayerId is null)
            return;
        Messages.WriteVoiceUp(_voiceWriter, sequence, radio, opus);
        _transport.Send(PeerId.Host, _voiceWriter.Written, Delivery.Unreliable);
    }
    /// <summary>
    /// GDD v1.4 App. D.12 (note 180): commends <paramref name="to"/> with the starter set's <paramref name="which"/>, on the
    /// run-end screen. The host decides whether it stands (one each, never yourself) and sends everyone the night's list.
    /// </summary>
    public void Commend(int to, byte which)
    {
        if (PlayerId is null)
            return;
        Messages.WriteCommend(_voiceWriter, to, which);
        _transport.Send(PeerId.Host, _voiceWriter.Written, Delivery.ReliableOrdered);
    }

    /// <summary>This dead player's creature vote (D.11), as the host offered it: the ballot and what they cast; null till offered.</summary>
    public (IReadOnlyList<Enemies.EnemyKind> Options, Enemies.EnemyKind? Cast)? Ballot { get; private set; }

    /// <summary>The host's tick (the newest snapshot's) when <see cref="Ballot"/> was first offered: a dead bot votes a while after (note 202).</summary>
    public uint BallotOfferedTick { get; private set; }

    /// <summary>D.11's cues to the dead, as they came: a creature they voted for is coming, and who called it. The game takes them.</summary>
    public List<(Enemies.EnemyKind Kind, List<int> Voters)> VoteCues { get; } = [];

    public string SessionInfo { get; private set; } = "";
    /// <summary>Set while welcomed but not yet aboard (spec E: drop-in at POIs), with the host's reason.</summary>
    public string? WaitingReason { get; private set; }
    public bool Waiting => WaitingReason is not null && !Connected;
    public bool Connected => PlayerId is not null && _haveState;
    /// <summary>The link to the host went after this client was welcomed: the night's over for it, unless it comes back (<see cref="Reconnect"/>).</summary>
    public bool Dropped { get; private set; }
    /// <summary>The host's token for this player's slot, from the Welcome: said back after a drop, it gets the slot back (note 253).</summary>
    public ulong Token { get; private set; }
    /// <summary>How many times this client has come back after a drop.</summary>
    public int Reconnects { get; private set; }
    /// <summary>The host turned this connection away instead of welcoming it (a full crew, note 254); null otherwise.</summary>
    public Refusal? Refused { get; private set; }

    /// <summary>
    /// Note 254: the player's quitting on purpose. Tells the host, so their place frees now rather than being held for a
    /// rejoin (note 253), then hangs up (the session's done: don't step it again). If the word's lost on the way, the host
    /// holds the place as for any drop.
    /// </summary>
    public void Leave()
    {
        if (Left)
            return;
        Left = true;
        Messages.WriteLeave(_writer);
        _transport.Send(PeerId.Host, _writer.Written, Delivery.ReliableOrdered);
        _transport.Disconnect(PeerId.Host);
        PlayerId = null;
        _haveState = false;
    }

    /// <summary>This player quit (<see cref="Leave"/>): the session's done.</summary>
    public bool Left { get; private set; }

    /// <summary>
    /// Note 253: after a drop, connects again on <paramref name="transport"/> (a new link to the same host: UDP, a lobby, any
    /// transport) and asks for this player's slot back with its token. The world stays as it was; the host's next full
    /// snapshot brings it up to date, and this player's own state with it, adopted as a placement.
    /// </summary>
    public void Reconnect(ITransport transport)
    {
        _transport = transport;
        Dropped = false;
        PlayerId = null;
        _haveState = false;
        WaitingReason = null;
        Refused = null;
        _helloSent = false;
        _decoded.Clear();
        _snapshots.Clear();
        Reconnects++;
    }

    bool _helloSent;

    /// <summary>The first word to the host (note 253): who this is, and the slot it's coming back to, if any.</summary>
    void Hello()
    {
        if (_helloSent)
            return;
        _helloSent = true;
        Messages.WriteHello(_writer, Name, Token, Outfit);
        _transport.Send(PeerId.Host, _writer.Written, Delivery.ReliableOrdered);
    }
    /// <summary>Who the host said this client is, kept past a drop (so the last snapshot's players still exclude it).</summary>
    byte? _was;
    /// <summary>This player as predicted locally: what the local camera shows.</summary>
    public PlayerState Predicted;
    public TrainControls Controls;

    /// <summary>Distance between what we predicted for an input and what the host computed for it.</summary>
    public double LastCorrection { get; private set; }
    public double MaxCorrection { get; private set; }
    public int Corrections { get; private set; }
    public int SnapshotsReceived { get; private set; }

    /// <summary>Clears the correction statistics (after a deliberate host-side teleport, for example).</summary>
    public void ResetStats()
    {
        LastCorrection = MaxCorrection = 0;
        Corrections = 0;
    }
    public uint NewestSnapshotTick => _newestSnapshotTick;

    public void Step(in PlayerIntent intent)
    {
        // Gone for good (Leave): a transport polled after hanging up would dial the host again as someone new.
        if (Left)
            return;
        Receive();
        if (!Connected)
            return;

        _sequence++;
        Predict(intent);
        _history[_sequence % HistoryLength] = (_sequence, intent, Predicted);
        SendInputs();
    }

    void Predict(in PlayerIntent given)
    {
        // Off the rails, the wreck has you till its hit kills you (App. E.2 step 1), as the host has it.
        var intent = World.Wrecked(Predicted) ? World.WreckedIntent(given) : given;
        World.BeginTick();
        // The host clears the brake every tick and re-applies whoever is holding it. If we're the one in
        // the cab it's almost certainly us, so do the same; otherwise assume whoever was braking still is.
        if (CabControls.CanDrive(Predicted, Train) && CabControls.Clears(Controls, Train, CabControls.ReleasesBrake(intent, Predicted, Train)))
            Controls.Brake = 0;
        CabControls.Apply(ref Controls, intent, Predicted, Train);
        World.CrewAct(ref Predicted, intent, PlayerId ?? 0);
        World.Step(Controls);
        if (!World.Wrecked(Predicted))
            PlayerMotor.Step(ref Predicted, intent, Train, PlayerTuning, TrainTuning, SimConstants.TickSeconds, applyLook: false);
        // The host snaps its world to the replication grid every tick; do the same so we match it exactly. The hand
        // isn't replicated (the next intent brings it), but this machine's HUD reads it between ticks, so it stays.
        _quantise.Clear();
        _quantise.Add(new PlayerSnapshot(PlayerId ?? 0, Predicted));
        WorldRecords.Quantise(World, ref Controls, _quantise);
        Predicted = _quantise[0].State with { Hand = Predicted.Hand, OtherHand = Predicted.OtherHand, Head = Predicted.Head };
    }

    readonly List<PlayerSnapshot> _quantise = new();
    readonly Dictionary<uint, List<WireRecord>> _decoded = new();
    const int DecodedHistory = 64;

    void SendInputs()
    {
        int n = (int)Math.Min(_sequence, (uint)_redundant.Length);
        for (int i = 0; i < n; i++)
        {
            uint seq = _sequence - (uint)(n - 1 - i);
            _redundant[i] = new InputFrame(seq, _history[seq % HistoryLength].Intent);
        }
        Messages.WriteInput(_writer, _redundant.AsSpan(0, n), _newestSnapshotTick);
        _transport.Send(PeerId.Host, _writer.Written, Delivery.Unreliable);
    }

    void Receive()
    {
        _events.Clear();
        _transport.Poll(_events);
        // Decode every snapshot (each may be the baseline for a later one); reconcile against the newest.
        List<WireRecord>? newest = null;
        uint newestAcked = 0;
        foreach (var e in _events)
        {
            if (e.Kind == TransportEventKind.Connected)
            {
                // Said before the host welcomes it: the host lets nobody in till they've said hello (or greetSeconds pass).
                if (PlayerId is null)
                    Hello();
                continue;
            }
            if (e.Kind == TransportEventKind.Disconnected)
            {
                // Once aboard, a drop is the night lost to this client, said as much (the 4 Oct rehearsal: a joiner whose
                // link timed out sat on "connecting…" and drew itself as a crewmate round its own eyes).
                Dropped |= PlayerId is not null || _was is not null;
                PlayerId = null;
                _haveState = false;
                continue;
            }
            if (e.Kind != TransportEventKind.Data || e.Payload is not { Length: > 0 } payload)
                continue;
            var r = new NetReader(payload);
            switch ((MessageType)r.U8())
            {
                case MessageType.Voice:
                    byte speaker = r.U8();
                    ushort vseq = r.U16();
                    var path = (VoicePath)r.U8();
                    int source = path.HasFlag(VoicePath.Mimic) ? r.I32() : 0;
                    double gain = path.HasFlag(VoicePath.Fading) ? r.U8() / 255.0 : 1;
                    VoiceFrames.Enqueue(new VoiceFrame(speaker, vseq, path, r.Rest().ToArray(), source, gain));
                    break;
                case MessageType.Wait:
                    WaitingReason = r.Str();
                    break;
                case MessageType.Refused:
                    // Note 254: turned away (a full crew). Kept to show; the host hangs up on this link in a moment.
                    Refused = Messages.ReadRefused(ref r);
                    break;
                case MessageType.Welcome:
                    {
                        (PlayerId, _, SessionInfo, ulong token) = Messages.ReadWelcome(ref r);
                        _was = PlayerId;
                        if (token != 0)
                            Token = token;
                        // Welcomed by a host whose Hello went before it was connected to: say it now (the name), once.
                        Hello();
                        break;
                    }
                case MessageType.Names:
                    Messages.ReadNames(ref r, World.Names);
                    break;
                case MessageType.Looks:
                    Messages.ReadLooks(ref r, World.Looks);
                    break;
                case MessageType.Outfits:
                    Messages.ReadOutfits(ref r, World.Outfits);
                    break;
                case MessageType.Ballot:
                    if (Ballot is null)
                        BallotOfferedTick = _newestSnapshotTick;
                    Ballot = Messages.ReadBallot(ref r);
                    break;
                case MessageType.VoteCue:
                    VoteCues.Add(Messages.ReadVoteCue(ref r));
                    break;
                case MessageType.Commendations:
                    World.Commendations.Clear();
                    World.Commendations.AddRange(Messages.ReadCommendations(ref r));
                    break;
                case MessageType.Bookmark:
                    if (Messages.ReadBookmark(ref r) is { } bookmark)
                        World.Bookmarks.Mirror(bookmark);
                    break;
                case MessageType.Report:
                    {
                        int index = r.U8();
                        _reportCount = r.U8();
                        _reportChunks[index] = r.Rest().ToArray();
                        if (Messages.ReadReport(_reportChunks, _reportCount) is { } report)
                            World.Run?.MirrorReport(report);
                        break;
                    }
                case MessageType.Film:
                    {
                        int index = r.U8();
                        _filmCount = r.U8();
                        _filmChunks[index] = r.Rest().ToArray();
                        if (World.Film is null && Messages.ReadFilm(_filmChunks, _filmCount) is { } film)
                            World.Film = film;
                        break;
                    }
                case MessageType.Snapshot:
                    uint tick = r.U32(), acked = r.U32(), baseTick = r.U32();
                    if (tick <= _newestSnapshotTick || _decoded.ContainsKey(tick))
                        continue;
                    var baseline = baseTick == 0 ? null : _decoded.GetValueOrDefault(baseTick);
                    if (baseTick != 0 && baseline is null)
                        continue; // we no longer have what it's relative to; the next one will be
                    List<WireRecord> records;
                    try { records = WorldRecords.ReadDelta(ref r, baseline); }
                    catch (Exception ex) when (ex is EndOfStreamException or InvalidDataException) { continue; }
                    SnapshotsReceived++;
                    _decoded[tick] = records;
                    if (_decoded.Count > DecodedHistory)
                        foreach (var old in _decoded.Keys.Where(k => k + DecodedHistory < tick).ToList())
                            _decoded.Remove(old);
                    Buffer(tick, records);
                    _newestSnapshotTick = tick;
                    newest = records;
                    newestAcked = acked;
                    break;
            }
        }
        if (newest is not null)
            Reconcile(newest, newestAcked);
    }

    void Buffer(uint tick, List<WireRecord> records)
    {
        var players = records.Where(r => r.Kind == RecordKind.Player).Select(r => PlayerFrom(r)).ToArray();
        _snapshots.Add((tick, players));
        if (_snapshots.Count > SnapshotBuffer)
            _snapshots.RemoveAt(0);
    }

    static PlayerSnapshot PlayerFrom(WireRecord r)
    {
        var list = new List<PlayerSnapshot>(1);
        WorldRecords.ApplyPlayers([r], list);
        return list[0];
    }

    void Reconcile(List<WireRecord> records, uint acked)
    {
        if (PlayerId is not { } id)
            return;
        WorldRecords.Apply(records, World, ref Controls, _players);
        int mine = _players.FindIndex(p => p.Id == id);
        if (mine < 0)
            return;
        var truth = _players[mine].State;

        if (!_haveState)
        {
            _haveState = true;
            Predicted = truth;
            _sequence = Math.Max(_sequence, acked);
            return;
        }

        var predicted = _history[acked % HistoryLength];
        // A host placement (respawn, revival) isn't a misprediction: adopt it without counting it.
        if (acked > 0 && predicted.Seq == acked && predicted.After.Placed == truth.Placed)
        {
            LastCorrection = Difference(predicted.After, truth);
            MaxCorrection = Math.Max(MaxCorrection, LastCorrection);
            if (LastCorrection > 1e-6)
                Corrections++;
        }

        // Rewind to the host's truth and replay everything it hasn't applied yet.
        Predicted = truth;
        for (uint seq = acked + 1; seq <= _sequence; seq++)
        {
            ref var h = ref _history[seq % HistoryLength];
            if (h.Seq != seq)
                continue;
            Predict(h.Intent);
            h.After = Predicted;
        }
    }

    /// <summary>Position error in metres, measured in the player's own frame; a frame change counts as large.</summary>
    /// <summary>
    /// How far the prediction was out. Across two frames (the ballast against a car's step, one roof against the next)
    /// it's measured in the world through the frames as they are now: exact at a stand, near enough on the move. It used to
    /// count any change of frame as 100 m, which hid how small those corrections are (a crate picked up on the host slows
    /// its carrier a round trip before their client knows).
    /// </summary>
    double Difference(in PlayerState a, in PlayerState b)
    {
        if (a.Parent == b.Parent)
            return (a.Position - b.Position).Length;
        if (a.Parent >= Train.Frames.Count || b.Parent >= Train.Frames.Count)
            return 100;
        return (PlayerMotor.WorldPosition(a, Train) - PlayerMotor.WorldPosition(b, Train)).Length;
    }

    /// <summary>
    /// Another player's state for rendering, interpolated <see cref="InterpolationTicks"/> behind the newest
    /// snapshot. Positions stay in their parent frame, so a remote player riding a car sits exactly on
    /// our (predicted) car even though their state is 100 ms old.
    /// </summary>
    public bool TryGetRemote(byte id, double alpha, out PlayerState state)
    {
        state = default;
        if (_snapshots.Count == 0)
            return false;
        double renderTick = _newestSnapshotTick - InterpolationTicks + alpha;
        int after = _snapshots.FindIndex(s => s.Tick >= renderTick);
        if (after < 0)
            return Find(_snapshots[^1].Players, id, out state);
        if (after == 0)
            return Find(_snapshots[0].Players, id, out state);
        var (t0, p0) = _snapshots[after - 1];
        var (t1, p1) = _snapshots[after];
        if (!Find(p0, id, out var a) || !Find(p1, id, out var b))
            return Find(p1, id, out state);
        double t = (renderTick - t0) / (t1 - t0);
        state = b;
        if (a.Parent == b.Parent)
        {
            state.Position = Double3.Lerp(a.Position, b.Position, t);
            state.Yaw = a.Yaw + (b.Yaw - a.Yaw) * t;
            // Their hands too, while they had them at both ends (T47).
            if (a.Hand != default && b.Hand != default)
                state.Hand = Double3.Lerp(a.Hand, b.Hand, t);
            if (a.OtherHand != default && b.OtherHand != default)
                state.OtherHand = Double3.Lerp(a.OtherHand, b.OtherHand, t);
            if (a.Head > 0 && b.Head > 0)
                state.Head = a.Head + (b.Head - a.Head) * t;
        }
        return true;
    }

    public IEnumerable<byte> RemoteIds =>
        _snapshots.Count == 0 ? [] : _snapshots[^1].Players.Select(p => p.Id).Where(i => i != (PlayerId ?? _was));

    static bool Find(PlayerSnapshot[] players, byte id, out PlayerState state)
    {
        foreach (var p in players)
            if (p.Id == id)
            {
                state = p.State;
                return true;
            }
        state = default;
        return false;
    }
}
