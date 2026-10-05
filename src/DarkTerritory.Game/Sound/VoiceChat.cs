using Ballast;
using Ballast.Audio;
using Ballast.Voice;
using DarkTerritory.Sim.Net;

namespace DarkTerritory.Game.Sound;

/// <summary>
/// Proximity voice and the walkie-talkie (spec A.5), client side. Outgoing: microphone → voice activity (or
/// push-to-talk) → Opus in 20 ms frames → the host. Incoming: each speaker's frames are decoded once and fed to
/// the path the host said they came by: a positional voice at the speaker (26 m log falloff, occluded through
/// the cab walls), a flat band-limited radio, or the flat dead channel. All of it plays on tier 2 of the mixer,
/// so talking ducks the bed exactly as spec A.3 says. A dead player on their Holdout's Live Mic (GDD App. D.7) comes on
/// the positional path too, from the Holdout's door. In a tunnel the mixer's space compresses positional voice and gives
/// it the tunnel's reverb (GDD §22; GameAudio picks the space).
/// <para>
/// A Soot Child's call (T40) comes as its own stream, keyed by the thing and not the crewmate whose voice it's using:
/// the frames are replayed, so they'd be stale to that crewmate's own decoder. It plays from where the thing is, at one
/// loudness however far (voice-mimic.json): the tell.
/// </para>
/// </summary>
public sealed class VoiceChat
{
    readonly Mixer _mixer;
    readonly VoiceEncoder _encoder = new();
    readonly VoiceActivity _activity = new();
    readonly float[] _frame = new float[VoiceFormat.FrameSamples];
    readonly byte[] _packet = new byte[VoiceFormat.MaxPacket];
    readonly Dictionary<byte, Speaker> _speakers = new();
    readonly Dictionary<int, Speaker> _mimics = new();
    int _filled;
    ushort _sequence;
    double _clock;

    public VoiceChat(Mixer mixer) => _mixer = mixer;

    /// <summary>Talk only while <see cref="TalkHeld"/>; otherwise voice activity decides.</summary>
    public bool PushToTalk { get; set; }
    public bool TalkHeld { get; set; }
    /// <summary>Holding the radio's button: this also goes out over the walkie-talkie.</summary>
    public bool RadioHeld { get; set; }
    /// <summary>
    /// Something has hold of you (GDD v1.4 App. C.8, "radio broadcast of a GRAB"): the mic's keyed open for the whole of it,
    /// push to talk or not, and on the radio if you've one (the host checks), so everyone else on it hears you, with no
    /// way to tell which car it's coming from. It closes when you're let go, or with the hard-cut.
    /// </summary>
    public bool Grabbed { get; set; }
    /// <summary>What goes out over the radio: the button held, or a GRAB keying it.</summary>
    public bool OnRadio => RadioHeld || Grabbed;
    public bool Transmitting { get; private set; }
    public int FramesSent { get; private set; }

    sealed class Speaker(byte id)
    {
        public readonly byte Id = id;
        public readonly VoiceDecoder Decoder = new();
        public readonly JitterBuffer<VoiceFrame> Order = new();
        public readonly StreamBuffer Near = new(), Radio = new(), Dead = new(), Mimic = new();
        public SoundInstance? NearVoice, RadioVoice, DeadVoice, MimicVoice;
        public double RadioKeyed;
        /// <summary>Their radio as of last update: keyed (the click and the static), and when it last broke up.</summary>
        public bool Keyed;
        public SoundInstance? Static;
        public int RadioUnderruns;
        public double BrokeUp = double.NegativeInfinity;
        public double LastHeard = double.NegativeInfinity;
        /// <summary>
        /// Cut off by their death (<see cref="HardCut"/>): their voice in the air and on the radio stays dropped until the
        /// snapshots have shown them dead and then alive again (freed from a Holdout, App. D.8). The host's cut arrives before
        /// the snapshots do (they're interpolated a tenth of a second behind).
        /// </summary>
        public bool Cut, SeenDead;
        /// <summary>
        /// Waiting in a Holdout with the Live Mic on (GDD v1.4 App. D.7; note 179): the dead speaker's in the air again, from
        /// the Holdout, whatever the cut says.
        /// </summary>
        public Ballast.Double3? LiveMic;
    }

    public IEnumerable<byte> Speakers => _speakers.Keys;

    /// <summary>
    /// Seconds since a crewmate was last heard (near or on the radio), or null if never: the roster's speaking marks, which
    /// is how a crew makes everyone speak and sees who didn't (App. A.7).
    /// </summary>
    public double? SinceHeard(byte speaker) =>
        _speakers.TryGetValue(speaker, out var s) && !double.IsNegativeInfinity(s.LastHeard) ? _clock - s.LastHeard : null;
    public int Underruns(byte speaker) => _speakers.TryGetValue(speaker, out var s) ? s.Near.Underruns : 0;

    /// <summary>Microphone samples (mono, 48 kHz), any amount; sends whole frames as they fill.</summary>
    public void Capture(ReadOnlySpan<float> samples, ClientSession client)
    {
        while (samples.Length > 0)
        {
            int n = Math.Min(samples.Length, _frame.Length - _filled);
            samples[..n].CopyTo(_frame.AsSpan(_filled));
            _filled += n;
            samples = samples[n..];
            if (_filled < _frame.Length)
                continue;
            _filled = 0;
            bool speaking = _activity.Update(_frame);
            Transmitting = Grabbed || (PushToTalk ? TalkHeld || RadioHeld : speaking || RadioHeld);
            // The sequence advances even in silence, so a receiver can tell loss (conceal) from a pause (don't).
            _sequence++;
            if (!Transmitting)
                continue;
            int bytes = _encoder.Encode(_frame, _packet);
            client.SendVoice(_sequence, OnRadio, _packet.AsSpan(0, bytes));
            FramesSent++;
        }
    }

    /// <summary>Decodes what's arrived and keeps each speaker's voice where they are.</summary>
    public void Update(ClientSession client, IReadOnlyList<Crewmate> crew, double dt)
    {
        bool inTunnel = InTunnel(client);
        _clock += dt;
        while (client.VoiceFrames.TryDequeue(out var f))
        {
            if (f.Path.HasFlag(VoicePath.Mimic))
            {
                if (!_mimics.TryGetValue(f.Source, out var m))
                    _mimics[f.Source] = m = new Speaker(f.Speaker);
                m.Order.Push(f.Sequence, f, _clock);
                continue;
            }
            if (!_speakers.TryGetValue(f.Speaker, out var s))
                _speakers[f.Speaker] = s = new Speaker(f.Speaker);
            if (f.Path.HasFlag(VoicePath.Cut))
            {
                HardCut(s);
                continue;
            }
            s.Order.Push(f.Sequence, f, _clock);
            s.LastHeard = _clock;
        }
        // GDD v1.4 App. D.2 and C.8, the hard-cut: on the tick a crewmate dies, their voice stops mid-word, near and on the
        // radio, with nothing buffered played out and no fade. What comes after is on the dead channel.
        foreach (var s in _speakers.Values)
            foreach (var c in crew)
                if (c.Id == s.Id)
                {
                    if (!c.Alive)
                    {
                        // The snapshots caught up (or the host's cut went missing): cut, if it isn't yet.
                        if (!s.Cut)
                            HardCut(s);
                        s.SeenDead = true;
                    }
                    else if (s.Cut && s.SeenDead)
                        s.Cut = s.SeenDead = false;
                }
        foreach (var s in _speakers.Values)
            s.LiveMic = client.World.Holdouts?.LiveMicOf(s.Id)?.Inside;
        foreach (var s in _speakers.Values)
            s.Order.Drain(_clock, (_, f) => Decode(s, f));
        foreach (var (source, m) in _mimics.ToList())
        {
            m.Order.Drain(_clock, (_, f) => Decode(m, f));
            m.MimicVoice ??= Stream("voice-mimic", m.Mimic);
            // From where it is; gone (or never seen here), it goes quiet.
            if (client.World.ActiveEnemies.FirstOrDefault(e => e.Id == source && !e.Gone) is { } thing && m.MimicVoice is not null)
                m.MimicVoice.Position = thing.WorldPosition(client.Train) + Double3.Up * 0.8;
            else if (!m.Mimic.Playing)
            {
                m.MimicVoice?.Stop();
                _mimics.Remove(source);
            }
        }
        foreach (var s in _speakers.Values)
        {
            s.NearVoice ??= Stream("voice", s.Near);
            // GDD §22 tunnels (note 183): in one, voices close in: compressed, boxy, no exterior.
            s.NearVoice?.Params.Set("tunnel", inTunnel ? 1 : 0);
            s.RadioVoice ??= Stream("voice-radio", s.Radio);
            s.DeadVoice ??= Stream("voice-dead", s.Dead);
            if (s.LiveMic is { } holdout && s.NearVoice is not null)
                s.NearVoice.Position = holdout + Double3.Up * 1.4;
            else
                foreach (var c in crew)
                    if (c.Id == s.Id && s.NearVoice is not null)
                        s.NearVoice.Position = c.Feet + Double3.Up * 1.6;
            s.RadioKeyed = Math.Max(0, s.RadioKeyed - dt);
            s.RadioVoice?.Params.Set("keyed", s.RadioKeyed > 0 || s.Radio.Playing ? 1 : 0);
            RadioSet(s);
        }
        // Whoever's left the session goes quiet.
        foreach (var id in _speakers.Keys.Where(id => client.RemoteIds.All(r => r != id)).ToList())
        {
            var s = _speakers[id];
            s.NearVoice?.Stop();
            s.RadioVoice?.Stop();
            s.DeadVoice?.Stop();
            s.Static?.Stop();
            _speakers.Remove(id);
        }
    }

    /// <summary>
    /// The set in your hand as a crewmate's transmission comes and goes (voice-radio-sfx): the click as they key it, static
    /// under them that comes up while the signal breaks up (frames lost on the way: the stream running dry), and the click
    /// and squelch tail as they let go.
    /// </summary>
    void RadioSet(Speaker s)
    {
        bool keyed = s.RadioKeyed > 0 || s.Radio.Playing;
        if (s.Radio.Underruns != s.RadioUnderruns)
        {
            s.RadioUnderruns = s.Radio.Underruns;
            s.BrokeUp = _clock;
        }
        if (keyed && !s.Keyed)
        {
            _mixer.Play("voice-radio-sfx.key-down");
            s.Static ??= _mixer.Play("voice-radio-sfx.static");
        }
        else if (!keyed && s.Keyed)
        {
            _mixer.Play("voice-radio-sfx.key-up");
            _mixer.Play("voice-radio-sfx.squelch");
            s.Static?.Stop();
            s.Static = null;
        }
        s.Keyed = keyed;
        if (s.Static is not null)
            s.Static.Volume = _clock - s.BrokeUp < 0.6 ? 1f : 0.2f;
    }

    /// <summary>The voice stops here: whatever of it is waiting, in the air and on the radio, is dropped.</summary>
    static void HardCut(Speaker s)
    {
        s.Cut = true;
        // Whatever of theirs is still waiting in the jitter buffer goes too (Decode strips it of air and radio).
        s.Near.Clear();
        s.Radio.Clear();
        s.RadioKeyed = 0;
        // And the voice in the air itself, filters and all, so not even the tunnel filters' ring outlives the word (note 183);
        // a fresh one starts next update for the Live Mic or the freed.
        s.NearVoice?.Stop();
        s.NearVoice = null;
    }

    /// <summary>The cut's tests: whether this speaker's air and radio voices hold anything now.</summary>
    public (int Near, int Radio) Buffered(byte speaker) => _speakers.TryGetValue(speaker, out var s) ? (s.Near.Buffered, s.Radio.Buffered) : (0, 0);

    static void Decode(Speaker s, VoiceFrame f)
    {
        // Frames from before the death, arriving late, are cut with the rest: only the dead channel plays on.
        if (s.Cut)
            f = f with { Path = f.Path & ~(s.LiveMic is null ? VoicePath.Proximity | VoicePath.Radio : VoicePath.Radio) };
        if (f.Path.HasFlag(VoicePath.Radio))
            s.RadioKeyed = 0.15;
        if (s.NearVoice is not null)
        {
            // A hand over the mouth (Tippy Toesie, GDD v1.1 App. C.8) muffles like a wall; a Soot Child drinking fades the
            // cries for help, as the host says how much is left.
            bool muffled = f.Path.HasFlag(VoicePath.Muffled);
            s.NearVoice.Occlusion = f.Path.HasFlag(VoicePath.Occluded) || muffled ? 1 : 0;
            s.NearVoice.Volume = (float)((muffled ? 0.45 : 1) * (f.Path.HasFlag(VoicePath.Fading) ? f.Gain : 1));
        }
        if (s.MimicVoice is not null)
            s.MimicVoice.Occlusion = f.Path.HasFlag(VoicePath.Occluded) ? 1 : 0;
        s.Decoder.Decode(f.Sequence, f.Opus, pcm =>
        {
            if (f.Path.HasFlag(VoicePath.Mimic))
            {
                s.Mimic.Write(pcm);
                return;
            }
            if (f.Path.HasFlag(VoicePath.Proximity))
                s.Near.Write(pcm);
            if (f.Path.HasFlag(VoicePath.Radio))
                s.Radio.Write(pcm);
            if (f.Path.HasFlag(VoicePath.Dead))
                s.Dead.Write(pcm);
        });
    }

    /// <summary>Whether this listener's under a tunnel (the route's own, the same test the radio's death uses).</summary>
    static bool InTunnel(ClientSession client)
    {
        var me = client.Predicted;
        if (client.World.Route is not { } route)
            return false;
        double along = me.Parent >= 0 && me.Parent < client.Train.Cars.Count ? client.Train.Cars[me.Parent].FrontDistance : me.LineHint;
        return route.InTunnel(along);
    }

    SoundInstance? Stream(string sound, StreamBuffer buffer)
    {
        var v = _mixer.Play(sound);
        if (v is not null)
            v.Stream = buffer;
        return v;
    }
}
