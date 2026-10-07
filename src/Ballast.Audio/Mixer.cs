using Ballast;

namespace Ballast.Audio;

/// <summary>Where the ears are. Right and Forward are unit vectors in world space.</summary>
public struct Listener
{
    public Double3 Position;
    public Double3 Right;
    public Double3 Forward;

    public static Listener At(Double3 position, double yaw) => new()
    {
        Position = position,
        Forward = new Double3(-Math.Sin(yaw), 0, -Math.Cos(yaw)),
        Right = new Double3(Math.Cos(yaw), 0, -Math.Sin(yaw)),
    };
}

/// <summary>
/// The player's volumes (the settings screen; the audio checklist's mix-settings), 0..1 each, on top of the mix: everything,
/// the game's sounds (tiers 1 and 3-6), the crew's voices (tier 2) and the music (tier 7 and the opera's bus).
/// </summary>
public readonly record struct MixVolumes(float Master = 1, float Effects = 1, float Voice = 1, float Music = 1)
{
    public MixVolumes() : this(1, 1, 1, 1) { }

    /// <summary>The gain on a tier's bus.</summary>
    public float Of(int tier) => Math.Clamp(Master, 0, 1) * Math.Clamp(tier switch { 2 => Voice, Mixer.MusicTier or Mixer.Tiers => Music, _ => Effects }, 0, 1);
}

/// <summary>
/// Records the mix split into stems by sound name, post-gain and post-duck, so a test can ask whether a tell
/// cuts through everything else in its own band (spec A.3: "tier 1 is inviolable").
/// </summary>
public sealed class MeterTap(int frames)
{
    public int Frames { get; } = frames;
    public Dictionary<string, float[]> Stems { get; } = new();
    public float[] Total { get; } = new float[frames * 2];
    public int Written { get; internal set; }

    internal float[] Stem(string name)
    {
        if (!Stems.TryGetValue(name, out var stem))
            Stems[name] = stem = new float[Frames * 2];
        return stem;
    }

    /// <summary>The whole mix minus one stem.</summary>
    public float[] AllBut(string name)
    {
        var rest = (float[])Total.Clone();
        if (Stems.TryGetValue(name, out var stem))
            for (int i = 0; i < rest.Length; i++)
                rest[i] -= stem[i];
        return rest;
    }
}

/// <summary>
/// The software mixer (ARCHITECTURE §6.4). Voices are synthesised, spatialised (distance rolloff,
/// equal-power pan, occlusion) and summed onto seven tier buses: spec A.3's six, and music's own under them (decided
/// 1 Oct). Tier ducking follows spec A.3 exactly, from <c>content/audio/mix.json</c>: while a tier is audible, the tiers
/// it ducks drop. The listener's <see cref="Space"/> (spec A.6) adds its reverb, fed from every positioned voice by its
/// tier's send, shuts out what it shuts out, and compresses voice where it says. The same code renders to the audio
/// device and offline to a buffer, so audibility is something tests can measure.
/// </summary>
public sealed class Mixer
{
    /// <summary>
    /// The tiers (spec A.3), tier 1 on top. Tier 7 is the work's music, under everything (decided 1 Oct: low, ambient,
    /// almost a drone, and every other tier ducks it).
    /// </summary>
    public const int Tiers = 7;
    /// <summary>
    /// The music bus (GDD v1.4 App. E.6): a sound with this tier is the derailment's opera, outside the tell tiers. It ducks
    /// nothing and the tiers' rules never duck it; <see cref="MusicBusDef"/> says what it does. The work's drone is tier 7,
    /// not this: the opera plays when the run's over, the drone while there's still a tell to hear.
    /// </summary>
    public const int MusicTier = 0;
    /// <summary>The <see cref="MeterTap"/> stem the reverb's return is metered as (all but the tells' own, while metering).</summary>
    public const string ReverbStem = "(reverb)";
    /// <summary>
    /// While a <see cref="MeterTap"/> is on: the tells' (tier 1's) own share of the reverb, convolved apart so an audit can
    /// count a tell's room as the tell's and not as the bed masking it. Offline only: it doubles the reverb's cost.
    /// </summary>
    public const string TellReverbStem = "(reverb) tells";
    /// <summary>How long what a space shuts out takes to go (a linear ramp to nothing), or to come back.</summary>
    public const double MuteSeconds = 0.3;
    readonly SoundBank _bank;
    readonly List<SoundInstance> _voices = new();
    readonly float[][] _buffers;
    readonly Smoothed[] _tierGain = new Smoothed[Tiers + 1];
    readonly bool[] _tierActive = new bool[Tiers + 1];
    readonly List<SoundInstance> _selected = new();
    readonly HashSet<string> _soundActive = new();
    readonly Dictionary<string, Smoothed> _soundGain = new();
    readonly Dictionary<string, double> _soundTarget = new();
    int _nextId = 1;
    uint _seed = 1;
    // The space as of the last block, its reverb, and the reverbs of spaces just left, ringing out.
    SpaceDef? _space;
    Convolver? _reverb;
    readonly List<Convolver> _tails = new();
    readonly Dictionary<ReverbDef, ImpulseResponse> _responses = new();
    readonly float[] _send = new float[Audio.Block], _wetLeft = new float[Audio.Block], _wetRight = new float[Audio.Block], _silence = new float[Audio.Block];
    // Metering only: the tells' send, convolved apart.
    Convolver? _tellReverb;
    readonly float[] _tellSend = new float[Audio.Block], _tellLeft = new float[Audio.Block], _tellRight = new float[Audio.Block];
    // A voice through the head, each ear.
    readonly float[] _earLeft = new float[Audio.Block], _earRight = new float[Audio.Block];
    // The tape's tiers summed (interleaved), its delay line, and where its wow and flutter are.
    readonly float[] _tapeBus = new float[Audio.Block * 2];
    readonly float[] _tapeLine = new float[TapeLine * 2];
    int _tapeWrite;
    double _wowPhase, _flutterPhase;
    const int TapeLine = 128;
    Smoothed _musicGain = new(1), _gameLowpass = new(0);

    public Mixer(SoundBank bank, MixDef mix)
    {
        _bank = bank;
        Mix = mix;
        _buffers = new float[Math.Max(1, mix.MaxVoices)][];
        for (int i = 0; i < _buffers.Length; i++)
            _buffers[i] = new float[Audio.Block];
        for (int t = 0; t <= Tiers; t++)
            _tierGain[t] = new Smoothed(1);
    }

    public MixDef Mix { get; set; }
    /// <summary>The player's volumes, over the mix's own levels.</summary>
    public MixVolumes Volumes { get; set; } = new();
    public Listener Listener;
    /// <summary>
    /// The space the listener's in (<c>content/audio/spaces.json</c>, chosen by the game): its reverb, what it shuts out,
    /// how voice carries in it. Null is dry and open. A change takes effect from the next block: the new reverb starts
    /// empty, the old one rings out what it had.
    /// </summary>
    public SpaceDef? Space { get; set; }
    /// <summary>The space's reverb (or the last one's tail) is sounding.</summary>
    public bool Reverberating => _reverb?.Ringing == true || _tails.Count > 0;

    /// <summary>Synthesises (once) and keeps a space's response, so entering the space later doesn't build it on the mixing thread.</summary>
    public ImpulseResponse Prepare(ReverbDef def)
    {
        if (!_responses.TryGetValue(def, out var ir))
            _responses[def] = ir = ImpulseResponse.Synthesize(def);
        return ir;
    }
    public IReadOnlyList<SoundInstance> Voices => _voices;
    public MeterTap? Tap { get; set; }
    /// <summary>Voices rendered in the last block (the rest were virtualised).</summary>
    public int RenderedVoices { get; private set; }

    /// <summary>The music bus's duck gain now (1 = untouched; App. E.6's −6 dB under the dead channel).</summary>
    public float MusicDuck => _musicGain.Value;
    /// <summary>How far the rest of the game is into the music's low-pass (0 = dry, 1 = fully muffled).</summary>
    public float GameLowpass => _gameLowpass.Value;
    /// <summary>How fast the low-passed game plays now (1 = as made; the music bus's GameRate under a full fade).</summary>
    public double GameRate => Mix.Music is { } bus ? 1 - (1 - bus.GameRate) * _gameLowpass.Value : 1;
    /// <summary>Music is sounding on the music bus.</summary>
    public bool MusicActive { get; private set; }

    /// <summary>Current duck gain on a tier (1 = untouched).</summary>
    public float TierGain(int tier) => _tierGain[Math.Clamp(tier, 1, Tiers)].Value;
    public bool TierActive(int tier) => _tierActive[Math.Clamp(tier, 1, Tiers)];

    /// <summary>Starts a sound. Over its instance limit, the oldest instance of it is stolen.</summary>
    public SoundInstance? Play(string name, Double3 position = default, float volume = 1)
    {
        if (_bank.Get(name) is not { } def)
            return null;
        var same = _voices.Where(v => v.Name == name && !v.Finished).ToList();
        for (int i = 0; i <= same.Count - Math.Max(1, def.MaxInstances); i++)
            same[i].Stop();
        var voice = new SoundInstance(_nextId++, name, def, _seed++, _bank.Samples) { Position = position, Volume = volume };
        _voices.Add(voice);
        return voice;
    }

    public void StopAll()
    {
        foreach (var v in _voices)
            v.Stop();
    }

    /// <summary>Renders interleaved stereo. Length must be a whole number of blocks.</summary>
    public void Render(Span<float> stereo)
    {
        if (stereo.Length % (Audio.Block * 2) != 0)
            throw new ArgumentException($"render length must be a multiple of {Audio.Block * 2} floats");
        for (int offset = 0; offset < stereo.Length; offset += Audio.Block * 2)
            RenderBlock(stereo.Slice(offset, Audio.Block * 2));
    }

    void RenderBlock(Span<float> output)
    {
        output.Clear();
        _voices.RemoveAll(v => v.Finished);
        if (!ReferenceEquals(Space, _space))
            EnterSpace();
        Array.Clear(_send);
        Array.Clear(_tellSend);

        // Priority: tier 1 first (it's inviolable), then the loudest. Past the voice budget, voices go virtual.
        foreach (var v in _voices)
        {
            // What the space shuts out fades to nothing over MuteSeconds as you go into a tunnel, not as a cut, and comes back
            // as you come out.
            if (!ReferenceEquals(v.MutedFor, _space))
            {
                v.Muted = _space?.Mutes(v.Name) == true;
                v.MutedFor = _space;
            }
            v.SpaceGain = Math.Clamp(v.SpaceGain + (v.Muted ? -1 : 1) * (float)(Audio.Block / (MuteSeconds * Audio.SampleRate)), 0, 1);
            v.LastAudibleGain = Spatial(v, out _, out _);
        }
        // E.6 "half speed": while the opera plays, the game on the low-passed tiers slows with the low-pass's fade, a tape
        // winding down (GameRate at full fade); everything else at its own speed.
        double slowed = GameRate;
        foreach (var v in _voices)
            v.Rate = Mix.Music is { } bus && Array.IndexOf(bus.LowpassTiers, v.Def.Tier) >= 0 ? slowed : 1;
        _selected.Clear();
        _selected.AddRange(_voices.Where(v => v.LastAudibleGain > 1e-4f || v.Stream is not null)
            .OrderBy(v => v.Def.Tier is 1 or MusicTier ? 0 : 1).ThenByDescending(v => v.LastAudibleGain).Take(_buffers.Length));
        foreach (var v in _voices)
        {
            v.Virtual = !_selected.Contains(v);
            if (v.Virtual)
                v.Skip(Audio.Block);
        }
        RenderedVoices = _selected.Count;

        Array.Clear(_tierActive);
        _soundActive.Clear();
        MusicActive = false;
        for (int i = 0; i < _selected.Count; i++)
        {
            var v = _selected[i];
            var buffer = _buffers[i].AsSpan();
            v.Render(buffer);
            float occlusion = EffectiveOcclusion(v);
            // A tell through a wall is quieter (the floor, spec A.3) and never duller: it's known by its band (spec A.4 rule
            // 1, "car fire: crackle and pop through the boards"), and a 900 Hz wall would take the band away.
            if (occlusion > 0.001f && v.Def.Tier != 1)
            {
                v.OcclusionFilter.Set(FilterType.LowPass, Mix.OcclusionLowpass * Math.Pow(20000 / Mix.OcclusionLowpass, 1 - occlusion), 0.707);
                v.OcclusionFilter.Process(buffer);
            }
            double sum = 0;
            foreach (float s in buffer)
                sum += s * s;
            double level = Math.Sqrt(sum / buffer.Length) * v.LastAudibleGain;
            if (level > 1e-3) // −60 dBFS: something you could hear
            {
                if (v.Def.Tier == MusicTier)
                    MusicActive = true;
                else
                    _tierActive[Math.Clamp(v.Def.Tier, 1, Tiers)] = true;
                _soundActive.Add(v.Name);
            }
            Compress(v, level);
        }

        // The music bus (App. E.6): ducked under the dead channel, and while it plays, the rest of the game muffled.
        var musicBus = Mix.Music;
        if (musicBus is not null)
        {
            float duck = musicBus.DuckUnder.Any(_soundActive.Contains) ? Audio.DbToGain(musicBus.DuckDb) : 1;
            _musicGain.Step(duck, duck < _musicGain.Value ? musicBus.DuckAttack : musicBus.DuckRelease);
            _gameLowpass.Step(MusicActive ? 1 : 0, musicBus.LowpassSeconds);
            if (_gameLowpass.Value < 1e-3 && !MusicActive)
                _gameLowpass.Value = 0;
            if (_gameLowpass.Value > 0)
                for (int i = 0; i < _selected.Count; i++)
                    if (Array.IndexOf(musicBus.LowpassTiers, _selected[i].Def.Tier) >= 0)
                        Muffle(_selected[i], _buffers[i], musicBus.LowpassHz, _gameLowpass.Value);
        }

        // Tier ducking, smoothed: fast down, slow back up.
        Span<double> duckDb = stackalloc double[Tiers + 1];
        foreach (var rule in Mix.Ducking)
            if (_tierActive[Math.Clamp(rule.Tier, 1, Tiers)])
                foreach (int t in rule.Ducks)
                    if (t is >= 1 and <= Tiers)
                        duckDb[t] = Math.Min(duckDb[t], rule.Db);
        for (int t = 1; t <= Tiers; t++)
        {
            float target = Audio.DbToGain(duckDb[t]);
            _tierGain[t].Step(target, target < _tierGain[t].Value ? Mix.DuckAttack : Mix.DuckRelease);
        }

        // Sound-level ducking inside a tier, for tells whose bands collide.
        _soundTarget.Clear();
        foreach (var rule in Mix.SoundDucking ?? [])
            if (rule.When.Any(_soundActive.Contains))
                foreach (var name in rule.Ducks)
                    _soundTarget[name] = Math.Min(_soundTarget.GetValueOrDefault(name), rule.Db);
        foreach (var name in _soundGain.Keys.Union(_soundTarget.Keys).ToList())
        {
            var gain = _soundGain.GetValueOrDefault(name, new Smoothed(1));
            float target = Audio.DbToGain(_soundTarget.GetValueOrDefault(name));
            gain.Step(target, target < gain.Value ? Mix.DuckAttack : Mix.DuckRelease);
            _soundGain[name] = gain;
        }

        float master = Audio.DbToGain(Mix.MasterDb);
        var tap = Tap;
        var tape = Mix.Tape is { Tiers.Length: > 0 } onTape ? onTape : null;
        Array.Clear(_tapeBus);
        for (int i = 0; i < _selected.Count; i++)
        {
            var v = _selected[i];
            Spatial(v, out float left, out float right);
            int tier = Math.Clamp(v.Def.Tier, 1, Tiers);
            float bus = (v.Def.Tier == MusicTier ? MusicBus() : _tierGain[tier].Value * Audio.DbToGain(Mix.Fader(tier))) * master * v.CompressorGain
                * Volumes.Of(v.Def.Tier)
                * (_soundGain.TryGetValue(v.Name, out var sg) ? sg.Value : 1);
            float l0 = v.LeftGain.Value, r0 = v.RightGain.Value;
            float l1 = v.LeftGain.Step(left * bus, 0.01), r1 = v.RightGain.Step(right * bus, 0.01);
            var buffer = _buffers[i];
            // Through the head (spec A.4): each ear its own delay and shadow. Flat sounds aren't anywhere, so they skip it.
            float[] earLeft = buffer, earRight = buffer;
            if (Mix.Head is { } head && !v.Def.Flat)
            {
                (v.Head ??= new HeadState()).Process(buffer, _earLeft, _earRight, head, Direction(v), tell: v.Def.Tier == 1);
                (earLeft, earRight) = (_earLeft, _earRight);
            }
            float[]? stem = tap is not null && tap.Written + Audio.Block * 2 <= tap.Total.Length ? tap.Stem(v.Name) : null;
            // The tape's tiers go to its bus, and through it into the mix after (spec A.6); the rest straight in.
            var into = tape is not null && Array.IndexOf(tape.Tiers!, v.Def.Tier) >= 0 ? _tapeBus.AsSpan() : output;
            for (int s = 0; s < Audio.Block; s++)
            {
                // Ramp across the block so gain changes never click.
                float u = (s + 1f) / Audio.Block;
                float l = earLeft[s] * (l0 + (l1 - l0) * u), r = earRight[s] * (r0 + (r1 - r0) * u);
                into[s * 2] += l;
                into[s * 2 + 1] += r;
                if (stem is not null)
                {
                    stem[tap!.Written + s * 2] += l;
                    stem[tap.Written + s * 2 + 1] += r;
                }
            }
            // Into the room, post-fader and whatever the pan (the room's all round you). Flat sounds aren't in the world.
            float send = _reverb is null || v.Def.Flat ? 0 : (float)_space!.Send(tier);
            if (send > 0)
            {
                float s0 = MathF.Sqrt(l0 * l0 + r0 * r0) * send, s1 = MathF.Sqrt(l1 * l1 + r1 * r1) * send;
                bool tell = tap is not null && tier == 1;
                for (int s = 0; s < Audio.Block; s++)
                {
                    float x = buffer[s] * (s0 + (s1 - s0) * (s + 1f) / Audio.Block);
                    _send[s] += x;
                    if (tell)
                        _tellSend[s] += x;
                }
            }
        }
        if (tape is not null)
            Tape(tape, output);
        Reverberate(output, tap);
        if (tap is not null && tap.Written + Audio.Block * 2 <= tap.Total.Length)
        {
            // Pre-clip, so stems sum exactly to the total.
            for (int s = 0; s < output.Length; s++)
                tap.Total[tap.Written + s] = output[s];
            tap.Written += Audio.Block * 2;
        }

        // Soft clip rather than wrap: a swarm plus a derailment shouldn't crackle.
        for (int s = 0; s < output.Length; s++)
            output[s] = MathF.Tanh(output[s]);
    }

    /// <summary>
    /// The tape (spec A.6, <see cref="TapeDef"/>): the tape bus through a delay that wanders, so its pitch does by the wow's
    /// and the flutter's depths, then a soft saturation, and into the mix. With a tape, the meter's total is the stems
    /// through it, not their plain sum.
    /// </summary>
    void Tape(TapeDef tape, Span<float> output)
    {
        // A delay swinging by A samples at f Hz moves the pitch by 2πfA/rate: so A from the depth.
        double wow = tape.WowDepth * Audio.SampleRate / (2 * Math.PI * Math.Max(0.01, tape.WowHz));
        double flutter = tape.FlutterDepth * Audio.SampleRate / (2 * Math.PI * Math.Max(0.01, tape.FlutterHz));
        double centre = Math.Min(TapeLine - 4, wow + flutter + 2);
        double drive = Math.Max(1e-3, tape.Drive);
        for (int s = 0; s < Audio.Block; s++)
        {
            _tapeLine[_tapeWrite * 2] = _tapeBus[s * 2];
            _tapeLine[_tapeWrite * 2 + 1] = _tapeBus[s * 2 + 1];
            _wowPhase = (_wowPhase + tape.WowHz / Audio.SampleRate) % 1;
            _flutterPhase = (_flutterPhase + tape.FlutterHz / Audio.SampleRate) % 1;
            double delay = Math.Clamp(centre + wow * Math.Sin(2 * Math.PI * _wowPhase) + flutter * Math.Sin(2 * Math.PI * _flutterPhase), 1, TapeLine - 2);
            int whole = (int)delay;
            float frac = (float)(delay - whole);
            int a = (_tapeWrite - whole + TapeLine) % TapeLine, b = (a - 1 + TapeLine) % TapeLine;
            for (int c = 0; c < 2; c++)
            {
                float x = _tapeLine[a * 2 + c] + (_tapeLine[b * 2 + c] - _tapeLine[a * 2 + c]) * frac;
                output[s * 2 + c] += (float)(Math.Tanh(drive * x) / drive);
            }
            _tapeWrite = (_tapeWrite + 1) % TapeLine;
        }
    }

    /// <summary>The listener's moved to another space: its reverb starts empty, the last one's rings out (two at most).</summary>
    void EnterSpace()
    {
        var was = _space?.Reverb;
        _space = Space;
        if (Equals(was, _space?.Reverb))
            return;
        if (_reverb is { Ringing: true })
            _tails.Add(_reverb);
        if (_tails.Count > 2)
            _tails.RemoveAt(0);
        _reverb = _space?.Reverb is { } def ? new Convolver(Prepare(def)) : null;
        _tellReverb = null;
    }

    /// <summary>The space's reverb on this block's sends, and the tails of the spaces just left, into the mix (and the meter).</summary>
    void Reverberate(Span<float> output, MeterTap? tap)
    {
        if (_reverb is null && _tails.Count == 0)
            return;
        Array.Clear(_wetLeft);
        Array.Clear(_wetRight);
        _reverb?.Process(_send, _wetLeft, _wetRight);
        foreach (var tail in _tails)
            tail.Process(_silence, _wetLeft, _wetRight);
        _tails.RemoveAll(t => !t.Ringing);
        bool metering = tap is not null && tap.Written + Audio.Block * 2 <= tap.Total.Length;
        float[]? stem = metering ? tap!.Stem(ReverbStem) : null;
        // The tells' share, the same response on their send alone (the reverb's linear, so the rest is the difference).
        Array.Clear(_tellLeft);
        Array.Clear(_tellRight);
        float[]? tells = null;
        if (metering && _reverb is not null)
        {
            _tellReverb ??= new Convolver(_reverb.Response);
            _tellReverb.Process(_tellSend, _tellLeft, _tellRight);
            tells = tap!.Stem(TellReverbStem);
        }
        for (int s = 0; s < Audio.Block; s++)
        {
            output[s * 2] += _wetLeft[s];
            output[s * 2 + 1] += _wetRight[s];
            if (stem is not null)
            {
                stem[tap!.Written + s * 2] += _wetLeft[s] - _tellLeft[s];
                stem[tap.Written + s * 2 + 1] += _wetRight[s] - _tellRight[s];
            }
            if (tells is not null)
            {
                tells[tap!.Written + s * 2] += _tellLeft[s];
                tells[tap.Written + s * 2 + 1] += _tellRight[s];
            }
        }
    }

    /// <summary>
    /// GDD §22: in a tunnel, proximity voice is compressed and close. The space's compressor works on a positioned voice's
    /// level at the ear (after distance), a block at a time: what's over the threshold comes down by the ratio, and the
    /// makeup brings it all up, so a crewmate at 20 m sounds nearly as close as one beside you. Elsewhere it's left alone.
    /// </summary>
    void Compress(SoundInstance v, double level)
    {
        if (_space?.Voice is not { } c || v.Def.Tier != 2 || v.Def.Flat)
        {
            v.CompressorGain = 1;
            v.CompressorEnvelopeDb = -120;
            return;
        }
        double db = Audio.GainToDb(level), seconds = (double)Audio.Block / Audio.SampleRate;
        double time = db > v.CompressorEnvelopeDb ? c.AttackSeconds : c.ReleaseSeconds;
        v.CompressorEnvelopeDb += (float)((db - v.CompressorEnvelopeDb) * (1 - Math.Exp(-seconds / Math.Max(1e-4, time))));
        double over = Math.Max(0, v.CompressorEnvelopeDb - c.ThresholdDb);
        v.CompressorGain = Audio.DbToGain(c.MakeupDb - over * (1 - 1 / Math.Max(1, c.Ratio)));
    }

    float MusicBus() => Mix.Music is { } m ? Audio.DbToGain(m.LevelDb) * _musicGain.Value : 1;

    /// <summary>The music's low-pass on one voice (App. E.6), crossfaded in by <paramref name="amount"/> so it never clicks on.</summary>
    static void Muffle(SoundInstance v, float[] buffer, double hz, float amount)
    {
        v.GameFilterA.Set(FilterType.LowPass, hz, 0.707);
        v.GameFilterB.Set(FilterType.LowPass, hz, 0.707);
        for (int s = 0; s < Audio.Block; s++)
        {
            float dry = buffer[s];
            float wet = v.GameFilterB.Process(v.GameFilterA.Process(dry));
            buffer[s] = dry + (wet - dry) * amount;
        }
    }

    /// <summary>Tier-1 tells are never occluded past the floor (spec A.3).</summary>
    float EffectiveOcclusion(SoundInstance v)
    {
        float occ = Math.Clamp(Math.Max(v.Occlusion, v.Walls), 0, 1);
        if (v.Def.Tier == 1 && Mix.OcclusionDb < 0)
            occ = Math.Min(occ, (float)(Mix.TellOcclusionFloorDb / Mix.OcclusionDb));
        return occ;
    }

    /// <summary>Distance, occlusion and pan for a voice. Returns the overall gain; outputs per-ear gains.</summary>
    float Spatial(SoundInstance v, out float left, out float right)
    {
        var def = v.Def;
        float gain = v.Volume * v.SpaceGain * Audio.DbToGain(def.GainDb) * Audio.DbToGain(EffectiveOcclusion(v) * Mix.OcclusionDb);
        if (def.Flat)
        {
            left = right = gain * 0.7071f;
            return gain;
        }
        var offset = v.Position - Listener.Position;
        double d = offset.Length;
        double attenuation;
        if (def.Curve == RolloffCurve.Voice)
            attenuation = d <= def.MinDistance ? 1 : d >= def.MaxDistance ? 0 : 1 - Math.Log(d / def.MinDistance) / Math.Log(def.MaxDistance / def.MinDistance);
        else
        {
            attenuation = d <= def.MinDistance ? 1 : Math.Pow(def.MinDistance / d, def.Rolloff);
            // Fade out over the last 10% of range instead of cutting off.
            attenuation *= Math.Clamp((def.MaxDistance - d) / (0.1 * def.MaxDistance), 0, 1);
        }
        gain *= (float)attenuation;

        var at = Direction(v);
        // Without a head, a touch quieter from behind: a cheap front/back cue. With one, the pinna's shelf does that.
        if (Mix.Head is null)
            gain *= Audio.DbToGain(-2 * at.Behind);
        double angle = (at.Lateral * (Mix.Head?.PanWidth ?? 1) + 1) * Math.PI / 4;
        left = gain * (float)Math.Cos(angle);
        right = gain * (float)Math.Sin(angle);
        return gain;
    }

    /// <summary>Where a voice is from the listener's head (all zero on top of it).</summary>
    HeadDirection Direction(SoundInstance v)
    {
        var offset = v.Position - Listener.Position;
        double d = offset.Length;
        if (d <= 1e-6)
            return default;
        var dir = offset * (1 / d);
        var up = Double3.Cross(Listener.Right, Listener.Forward);
        return new HeadDirection(Math.Clamp(Double3.Dot(dir, Listener.Right), -1, 1), Math.Max(0, -Double3.Dot(dir, Listener.Forward)),
            Math.Clamp(Double3.Dot(dir, up), -1, 1));
    }
}
