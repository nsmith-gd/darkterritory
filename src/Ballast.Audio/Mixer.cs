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
/// equal-power pan, occlusion) and summed onto six tier buses. Tier ducking follows spec A.3 exactly, from
/// <c>content/audio/mix.json</c>: while a tier is audible, the tiers it ducks drop. The same code renders to
/// the audio device and offline to a buffer, so audibility is something tests can measure.
/// </summary>
public sealed class Mixer
{
    public const int Tiers = 6;
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
    public Listener Listener;
    public IReadOnlyList<SoundInstance> Voices => _voices;
    public MeterTap? Tap { get; set; }
    /// <summary>Voices rendered in the last block (the rest were virtualised).</summary>
    public int RenderedVoices { get; private set; }

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
        var voice = new SoundInstance(_nextId++, name, def, _seed++) { Position = position, Volume = volume };
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

        // Priority: tier 1 first (it's inviolable), then the loudest. Past the voice budget, voices go virtual.
        foreach (var v in _voices)
            v.LastAudibleGain = Spatial(v, out _, out _);
        _selected.Clear();
        _selected.AddRange(_voices.Where(v => v.LastAudibleGain > 1e-4f)
            .OrderBy(v => v.Def.Tier == 1 ? 0 : 1).ThenByDescending(v => v.LastAudibleGain).Take(_buffers.Length));
        foreach (var v in _voices)
        {
            v.Virtual = !_selected.Contains(v);
            if (v.Virtual)
                v.Skip(Audio.Block);
        }
        RenderedVoices = _selected.Count;

        Array.Clear(_tierActive);
        _soundActive.Clear();
        for (int i = 0; i < _selected.Count; i++)
        {
            var v = _selected[i];
            var buffer = _buffers[i].AsSpan();
            v.Render(buffer);
            float occlusion = EffectiveOcclusion(v);
            if (occlusion > 0.001f)
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
                _tierActive[Math.Clamp(v.Def.Tier, 1, Tiers)] = true;
                _soundActive.Add(v.Name);
            }
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
        for (int i = 0; i < _selected.Count; i++)
        {
            var v = _selected[i];
            Spatial(v, out float left, out float right);
            float bus = _tierGain[Math.Clamp(v.Def.Tier, 1, Tiers)].Value * master * (_soundGain.TryGetValue(v.Name, out var sg) ? sg.Value : 1);
            float l0 = v.LeftGain.Value, r0 = v.RightGain.Value;
            float l1 = v.LeftGain.Step(left * bus, 0.01), r1 = v.RightGain.Step(right * bus, 0.01);
            var buffer = _buffers[i];
            float[]? stem = tap is not null && tap.Written + Audio.Block * 2 <= tap.Total.Length ? tap.Stem(v.Name) : null;
            for (int s = 0; s < Audio.Block; s++)
            {
                // Ramp across the block so gain changes never click.
                float u = (s + 1f) / Audio.Block;
                float l = buffer[s] * (l0 + (l1 - l0) * u), r = buffer[s] * (r0 + (r1 - r0) * u);
                output[s * 2] += l;
                output[s * 2 + 1] += r;
                if (stem is not null)
                {
                    stem[tap!.Written + s * 2] += l;
                    stem[tap.Written + s * 2 + 1] += r;
                }
            }
        }
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

    /// <summary>Tier-1 tells are never occluded past the floor (spec A.3).</summary>
    float EffectiveOcclusion(SoundInstance v)
    {
        float occ = Math.Clamp(v.Occlusion, 0, 1);
        if (v.Def.Tier == 1 && Mix.OcclusionDb < 0)
            occ = Math.Min(occ, (float)(Mix.TellOcclusionFloorDb / Mix.OcclusionDb));
        return occ;
    }

    /// <summary>Distance, occlusion and pan for a voice. Returns the overall gain; outputs per-ear gains.</summary>
    float Spatial(SoundInstance v, out float left, out float right)
    {
        var def = v.Def;
        float gain = v.Volume * Audio.DbToGain(def.GainDb) * Audio.DbToGain(EffectiveOcclusion(v) * Mix.OcclusionDb);
        if (def.Flat)
        {
            left = right = gain * 0.7071f;
            return gain;
        }
        var offset = v.Position - Listener.Position;
        double d = offset.Length;
        double attenuation = d <= def.MinDistance ? 1 : Math.Pow(def.MinDistance / d, def.Rolloff);
        // Fade out over the last 10% of range instead of cutting off.
        attenuation *= Math.Clamp((def.MaxDistance - d) / (0.1 * def.MaxDistance), 0, 1);
        gain *= (float)attenuation;

        double pan = 0, behind = 0;
        if (d > 1e-6)
        {
            var dir = offset * (1 / d);
            pan = Math.Clamp(Double3.Dot(dir, Listener.Right), -1, 1);
            behind = Math.Max(0, -Double3.Dot(dir, Listener.Forward));
        }
        // A touch quieter from behind: a cheap front/back cue until HRTF (Steam Audio) is in.
        gain *= Audio.DbToGain(-2 * behind);
        double angle = (pan + 1) * Math.PI / 4;
        left = gain * (float)Math.Cos(angle);
        right = gain * (float)Math.Sin(angle);
        return gain;
    }
}
