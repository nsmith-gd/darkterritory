using Ballast.Net;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Net;

/// <summary>
/// Sessions (GDD §33.1; ARCHITECTURE §8 note 724; the director, 14 Oct 2026, App. F.6: friend groups break on friction, uneven
/// attendance, slow joins and strangers in public games): the crew's save sent to everyone aboard, a friend taking a bot's
/// seat, kick and the public lobby's vote to put someone off, and the lobby's crew mischief switch. None of it is sim state:
/// a joiner, a kick and a vote are the session's, and the switch reaches the sim through <see cref="World.CrewMischief"/>.
/// </summary>
public sealed partial class HostSession
{
    // ---- The crew's save travels (GDD §11, note 724) ----

    /// <summary>Saves sent to the crew (one a night's settling), for tests and the log.</summary>
    public int SavesShared { get; private set; }

    /// <summary>
    /// Sends the crew's save (the host machine's file, as bytes; the session never reads it) to every human aboard or waiting
    /// at a stop, as the night's settled into it (note 724): each keeps it among the crews it's ridden with. Bots are skipped.
    /// </summary>
    public void ShareSave(byte[] save)
    {
        var messages = Messages.CrewSaveMessages(save);
        foreach (var peer in HumanPeers())
            foreach (var m in messages)
                _transport.Send(peer, m, Delivery.ReliableOrdered);
        SavesShared++;
    }

    IEnumerable<PeerId> HumanPeers() =>
        _crew.Where(c => !_bots.Contains(c.Id)).Select(c => c.Peer).Concat(_waiting.Where(w => !_bots.Contains(w.Id)).Select(w => w.Peer)).Distinct();

    IEnumerable<PeerId> AllPeers() => _crew.Select(c => c.Peer).Concat(_waiting.Select(w => w.Peer)).Distinct();

    // ---- A friend takes a bot's seat (GDD §33.1, note 724) ----

    /// <summary>Joiners who took a bot's seat.</summary>
    public int BotSeatsTaken { get; private set; }

    /// <summary>
    /// Told when a bot leaves the crew (note 724): its seat taken by a joiner, or put off by the host. The bot's machine (the
    /// host's own, <c>BotCrew</c>) stops running it; its link is already hung up.
    /// </summary>
    public Action<byte>? BotLeft { get; set; }

    /// <summary>Whether this player said in their Hello they're a bot (note 574).</summary>
    public bool IsBot(byte id) => _bots.Contains(id);

    /// <summary>
    /// A new joiner who'd not walk straight aboard alive (note 724): the run's under way with Holdouts (D.3's queue), the train's
    /// between stops (spec E's wait), or the crew's full. With player.json <c>crew.joinTakesBotSeat</c>.
    /// </summary>
    bool WouldWait => PlayerTuning.Crew.JoinTakesBotSeat && BotSeatsOpen && (Full || Lobbying || CanBoard is { } can && !can());

    /// <summary>
    /// Whether a joiner can take a bot's seat yet (note 724). The game shuts it while it brings the night's own crew aboard
    /// (its bots, then the host's own player), so a resumed night's host doesn't take its own driver's seat. Shut, those
    /// aboard aren't lobbied past the gate either (note 937): they're the night's crew, not joiners.
    /// </summary>
    public bool BotSeatsOpen { get; set; } = true;

    /// <summary>
    /// The bot whose seat a joiner takes (note 724): a living one, the director's census's least busy (fewest threats on it,
    /// then the most slack), else the newest; the one at the cab's controls last, so the train isn't left undriven. A crew
    /// that's full takes a dead bot's seat (the queue's) before it turns a friend away. Null: no bot to take.
    /// </summary>
    Crew? BotSeat()
    {
        var bots = _crew.Where(c => _bots.Contains(c.Id) && c.State.Alive && !World.Wrecked(c.State)).ToList();
        if (bots.Count == 0 && Full)
            bots = [.. _crew.Where(c => _bots.Contains(c.Id))];
        if (bots.Count == 0)
            return null;
        var census = World.Director?.Posts.Entries.ToDictionary(e => e.Player) ?? new Dictionary<int, Enemies.CensusEntry>();
        return bots
            .OrderBy(c => c.Id == World.ControlsHolder ? 1 : 0)
            .ThenBy(c => census.TryGetValue(c.Id, out var e) ? e.On : 0)
            .ThenByDescending(c => census.TryGetValue(c.Id, out var e) ? e.Slack : 0)
            .ThenByDescending(c => c.Id)
            .First();
    }

    /// <summary>
    /// The joiner takes <paramref name="bot"/>'s place where it stands (note 724): its id, and with it its body, its slots and
    /// what it carries, its health and its post. The bot's link is hung up and its machine told (<see cref="BotLeft"/>). The
    /// joiner is welcomed into the id with a fresh token, and adopts the state as a placement.
    /// </summary>
    byte TakeSeat(Crew bot, PeerId peer)
    {
        var old = bot.Peer;
        bot.Peer = peer;
        bot.Pending.Clear();
        bot.PendingSeen.Clear();
        bot.Sent.Clear();
        (bot.Seen, bot.LastApplied, bot.AckedSnapshot, bot.MissedInputs, bot.ReportedLoss) = (0, 0, 0, 0, 0);
        (bot.LastIntent, bot.ThisTick, bot.Bookmarking) = (default, default, false);
        (bot.Thin, bot.ThinFor, bot.ThinHeld) = (false, 0, 0);
        bot.InputLoss = new(_lossWindow);
        bot.State.Placed++;
        // The bot's look (D.8) was its own; the joiner's comes with their name, or their id's.
        World.Looks.Remove(bot.Id);
        Botness(bot.Id, false);
        if (World.ControlsHolder == bot.Id)
            World.HoldControls(bot.Id, false);
        _tokens.Remove(bot.Id);
        ulong token = NewToken();
        _tokens[bot.Id] = token;
        _transport.Disconnect(old);
        BotSeatsTaken++;
        Welcome(bot.Id, peer, token);
        BotLeft?.Invoke(bot.Id);
        return bot.Id;
    }

    // ---- Kick and the public lobby's vote (GDD §33.1, note 724) ----

    /// <summary>
    /// A public lobby (listed, no password): anyone aboard can call a vote to put a player off. Off (friends-only, a private
    /// run, a night alone), only the host's own player can, and does so outright.
    /// </summary>
    public bool VoteKicks { get; set; }

    /// <summary>
    /// Who a link is beyond its connection (note 724): the platform's account, or a LAN joiner's address. Someone put off is
    /// barred by it; without one (null, or it returns null), by the name they gave.
    /// </summary>
    public Func<PeerId, string?>? Identity { get; set; }

    /// <summary>Players put off this run.</summary>
    public int Kicks { get; private set; }

    /// <summary>The vote to put someone off as it stands, or <see cref="KickVote.None"/>.</summary>
    public KickVote Vote { get; private set; }

    readonly HashSet<string> _barred = [];
    readonly HashSet<byte> _kickYes = [];
    bool _voteChanged, _mischiefChanged;

    /// <summary>The lobby's crew mischief switch (GDD §23.4, note 724): the world's, and every client's once it's sent.</summary>
    public bool Mischief
    {
        get => World.CrewMischief;
        set
        {
            if (World.CrewMischief == value)
                return;
            World.CrewMischief = value;
            _mischiefChanged = true;
        }
    }

    /// <summary>
    /// Puts a player off the run (note 724): told so, their crewmate dropped where they stood as for a Leave (their place not
    /// held), barred from coming back to this run, and hung up on once the word's had time to get there. Never the host's own
    /// player. False if there's nobody by that id.
    /// </summary>
    public bool Kick(byte id)
    {
        if (id == HostPlayer)
            return false;
        PeerId? found = _crew.Find(c => c.Id == id)?.Peer ?? _waiting.Where(w => w.Id == id).Select(w => (PeerId?)w.Peer).FirstOrDefault()
            ?? _reserved.Where(r => r.Id == id).Select(r => (PeerId?)r.Peer).FirstOrDefault();
        if (found is not { } peer)
            return false;
        bool bot = _bots.Contains(id);
        // A bot put off is gone, and nothing to bar (it would bar the host's own machine, where the bots are).
        if (!bot)
            Bar(peer, World.Names.GetValueOrDefault(id));
        Messages.WriteKicked(_writer);
        _transport.Send(peer, _writer.Written, Delivery.ReliableOrdered);
        Gone(peer, hold: false);
        foreach (var seat in _reserved.Where(r => r.Id == id).ToList())
        {
            _reserved.Remove(seat);
            Free(seat.Id);
        }
        _refused.Add((peer, Tick + (uint)TicksOf(PlayerTuning.Crew.RefuseLingerSeconds)));
        Kicks++;
        if (Vote.Target == id)
            CloseVote();
        if (bot)
        {
            Botness(id, false);
            BotLeft?.Invoke(id);
        }
        return true;
    }

    void Bar(PeerId peer, string? name)
    {
        if (Identity?.Invoke(peer) is { Length: > 0 } who)
            _barred.Add("id:" + who);
        else if (name is { Length: > 0 })
            _barred.Add("name:" + name.ToUpperInvariant());
    }

    bool Banned(PeerId peer, string name) =>
        _barred.Count > 0 && (Identity?.Invoke(peer) is { Length: > 0 } who && _barred.Contains("id:" + who)
            || name.Length > 0 && _barred.Contains("name:" + name.ToUpperInvariant()));

    /// <summary>
    /// A crewmate's Kick (note 724). The host's own player's puts them off outright. In a public lobby anyone else living and
    /// playing calls the vote on them, or says yes to the one open on them; one vote at a time, never on the host or yourself.
    /// </summary>
    void CallKick(Crew from, byte target)
    {
        if (from.Id == HostPlayer)
        {
            Kick(target);
            return;
        }
        if (!VoteKicks || _bots.Contains(from.Id) || !from.State.Alive || target == from.Id || target == HostPlayer || target == 0)
            return;
        if (!_crew.Any(c => c.Id == target) && !_waiting.Any(w => w.Id == target))
            return;
        if (Vote.Open && Vote.Target != target)
            return;
        if (!Vote.Open)
        {
            _kickYes.Clear();
            Vote = new KickVote(target, 0, 0, Tick + (uint)TicksOf(PlayerTuning.Crew.Kick.VoteSeconds));
        }
        _kickYes.Add(from.Id);
        CountVote();
    }

    /// <summary>
    /// The vote counted (note 724): the living humans aboard but the one voted on are its electorate, and more than
    /// crew.kick.majority of them saying yes puts them off. It closes at its time, or when they've gone anyway.
    /// </summary>
    void CountVote()
    {
        if (!Vote.Open)
            return;
        byte target = Vote.Target;
        var electorate = _crew.Where(c => !_bots.Contains(c.Id) && c.State.Alive && c.Id != target).Select(c => c.Id).ToList();
        int yes = electorate.Count(_kickYes.Contains);
        int needed = (int)Math.Floor(electorate.Count * PlayerTuning.Crew.Kick.Majority) + 1;
        if (electorate.Count > 0 && yes >= needed)
        {
            CloseVote();
            Kick(target);
            return;
        }
        if (Tick >= Vote.Until || !_crew.Any(c => c.Id == target) && !_waiting.Any(w => w.Id == target))
        {
            CloseVote();
            return;
        }
        var now = Vote with { Yes = yes, Needed = needed };
        if (now != Vote)
        {
            Vote = now;
            _voteChanged = true;
        }
    }

    void CloseVote()
    {
        Vote = KickVote.None;
        _kickYes.Clear();
        _voteChanged = true;
    }

    /// <summary>Each tick, before anyone acts: the vote counted, and the vote and the switch sent to everyone when they change.</summary>
    void Sessions()
    {
        CountVote();
        if (_voteChanged)
        {
            _voteChanged = false;
            Messages.WriteKickVote(_writer, Vote);
            foreach (var p in AllPeers())
                _transport.Send(p, _writer.Written, Delivery.ReliableOrdered);
        }
        if (_mischiefChanged)
        {
            _mischiefChanged = false;
            Messages.WriteMischief(_writer, World.CrewMischief);
            foreach (var p in AllPeers())
                _transport.Send(p, _writer.Written, Delivery.ReliableOrdered);
        }
    }

    /// <summary>What a joiner's told as they're welcomed (note 724): the switch as it stands now, and any vote open.</summary>
    void TellSession(PeerId peer)
    {
        Messages.WriteMischief(_writer, World.CrewMischief);
        _transport.Send(peer, _writer.Written, Delivery.ReliableOrdered);
        if (Vote.Open)
        {
            Messages.WriteKickVote(_writer, Vote);
            _transport.Send(peer, _writer.Written, Delivery.ReliableOrdered);
        }
    }

    // ---- A joiner buys at the forge (GDD §3.1, §11; note 1054) ----

    readonly List<ForgeOrder> _orders = [];

    /// <summary>
    /// The joiners' orders at the blacksmith since the last take, in the order they came in (note 1054). The crew's scrip isn't
    /// the session's (it's the host machine's campaign), so the game buys each in turn and answers it with
    /// <see cref="AnswerOrder"/>: two orders for the last of something are bought first come, and the second refused.
    /// </summary>
    public IReadOnlyList<ForgeOrder> TakeOrders()
    {
        if (_orders.Count == 0)
            return [];
        var taken = _orders.ToList();
        _orders.Clear();
        return taken;
    }

    /// <summary>Answers a joiner's order (note 1054), to that joiner only. False if they're not aboard any more.</summary>
    public bool AnswerOrder(byte to, OrderAnswer answer)
    {
        if (_crew.Find(c => c.Id == to) is not { } c || _bots.Contains(to))
            return false;
        Messages.WriteOrderAnswer(_writer, answer);
        _transport.Send(c.Peer, _writer.Written, Delivery.ReliableOrdered);
        return true;
    }

    /// <summary>A crewmate's Order (note 1054): kept for the game to buy. A bot's isn't (bots don't shop; it'd be the host's own machine).</summary>
    void Ordered(Crew from, ForgeOrder order)
    {
        if (!_bots.Contains(from.Id))
            _orders.Add(order);
    }
}
