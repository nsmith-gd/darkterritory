using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ballast.Audio;

/// <summary>
/// A number in a sound definition: either a constant, or a curve over one of the sound's live parameters
/// (<c>{ "param": "speed", "points": [[0, 20], [22, 140]] }</c>, piecewise linear, clamped at the ends).
/// This is how the train bed "drives from sim state" (spec A.2) without code per sound.
/// </summary>
[JsonConverter(typeof(ValueConverter))]
public readonly record struct Value(double Constant, string? Param = null, double[][]? Points = null)
{
    public static implicit operator Value(double constant) => new(constant);

    public double Evaluate(ParamSet p)
    {
        if (Param is null || Points is not { Length: > 0 } pts)
            return Constant;
        double x = p.Get(Param);
        if (x <= pts[0][0])
            return pts[0][1];
        for (int i = 1; i < pts.Length; i++)
            if (x <= pts[i][0])
            {
                double t = (x - pts[i - 1][0]) / Math.Max(1e-9, pts[i][0] - pts[i - 1][0]);
                return pts[i - 1][1] + t * (pts[i][1] - pts[i - 1][1]);
            }
        return pts[^1][1];
    }

    sealed class ValueConverter : JsonConverter<Value>
    {
        public override Value Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number)
                return new Value(reader.GetDouble());
            var curve = JsonSerializer.Deserialize<Curve>(ref reader, options) ?? throw new JsonException("expected a number or a curve");
            return new Value(0, curve.Param, curve.Points);
        }

        public override void Write(Utf8JsonWriter writer, Value value, JsonSerializerOptions options)
        {
            if (value.Param is null)
                writer.WriteNumberValue(value.Constant);
            else
                JsonSerializer.Serialize(writer, new Curve(value.Param, value.Points ?? []), options);
        }

        sealed record Curve(string Param, double[][] Points);
    }
}

/// <summary>Named live parameters for one playing sound (speed, pressure, drill progress...).</summary>
public sealed class ParamSet
{
    readonly Dictionary<string, double> _values = new();

    public double Get(string name) => _values.GetValueOrDefault(name);
    public void Set(string name, double value) => _values[name] = value;
}

public enum SourceKind : byte
{
    /// <summary>White noise, for steam, roar, wind and writhes once filtered.</summary>
    Noise,
    Sine,
    Saw,
    Square,
    /// <summary>One click per period: through resonant filters it's metal (rod clank, a drill bit, a coupling).</summary>
    Impulse,
    /// <summary>Samples pushed in from outside: voice chat, decoded as it arrives (<see cref="SoundInstance.Stream"/>).</summary>
    Stream,
    /// <summary>A recorded take from <c>content/audio/samples</c> (<see cref="LayerDef.Sample"/>, <see cref="SampleLibrary"/>).</summary>
    Sample,
}

/// <summary>How level falls with distance.</summary>
public enum RolloffCurve : byte
{
    /// <summary>Inverse distance (to the power Rolloff) beyond MinDistance, faded out over the last 10% of MaxDistance.</summary>
    Inverse,
    /// <summary>Spec A.5 proximity voice: full clarity to MinDistance, logarithmic falloff to nothing at MaxDistance.</summary>
    Voice,
}

public sealed record FilterDef(FilterType Type, Value Frequency, double Q = 0.707, double GainDb = 0);

/// <summary>Amplitude modulation: sine tremolo, or a hard rhythmic gate when <see cref="Duty"/> is set.</summary>
public sealed record ModDef(Value Rate, double Depth = 1, double Duty = 0, double Jitter = 0);

/// <summary>One layer: a source, a filter chain, a gain, and optional shaping over time.</summary>
public sealed record LayerDef(SourceKind Source, Value Gain, Value? Frequency = null, FilterDef[]? Filters = null,
    ModDef? Tremolo = null, ModDef? Vibrato = null,
    // (time s, value) breakpoints over the sound's life (one-shots) or each cycle of CycleSeconds (loops).
    double[][]? Envelope = null, double[][]? PitchEnvelope = null, double Delay = 0,
    // Sample layers: the take(s) to play, a path under content/audio/samples (a folder of takes, one picked per instance,
    // or one file; SampleLibrary). Rate is the playback-rate multiplier (2 = an octave up and twice as fast); the pitch
    // envelope and vibrato scale it as they would a frequency. A loop's take loops; a one-shot's plays once.
    string? Sample = null, Value? Rate = null,
    // Picked once per instance from its seed, uniform in ±: semitones on the pitch (a sample's rate, an oscillator's
    // frequency) and dB on the gain, so repeats of one sound aren't identical.
    double PitchJitter = 0, double GainJitter = 0);

/// <summary>Spec A.6's deliberate degradation: sample-rate reduction and bit crush, on non-tell layers.</summary>
public sealed record CrushDef(int Bits = 12, int Rate = 22050);

/// <summary>
/// A sound, as data (<c>content/audio/sounds/*.json</c>). Layers are summed, then the whole is spatialised
/// and mixed on its tier's bus (spec A.3).
/// </summary>
/// <param name="Duration">
/// How long a one-shot's synth layers sound (they have no end of their own): 1 s if not given. Sample layers end when
/// their takes do, so a one-shot with any lasts until every one has played out, and at least <c>duration</c> only if it
/// has synth layers too and gives one; an all-sample one-shot ignores it. See <see cref="SoundInstance.Finished"/>.
/// </param>
public sealed record SoundDef(int Tier, LayerDef[] Layers, bool Loop = false, double? Duration = null,
    // Loops can repeat their envelopes on a cycle.
    double CycleSeconds = 0,
    // Per-sound voice limit (spec A.7: a Choir swarm must not eat the voice budget).
    int MaxInstances = 8,
    // Full level inside MinDistance, inverse-distance rolloff to silence at MaxDistance.
    double MinDistance = 2, double MaxDistance = 150, double Rolloff = 1,
    // Headroom, applied last.
    double GainDb = 0,
    CrushDef? Crush = null,
    // Heard without position (UI, the listener's own wind).
    bool Flat = false,
    RolloffCurve Curve = RolloffCurve.Inverse);

/// <summary>Mix bus rules (<c>content/audio/mix.json</c>).</summary>
public sealed record MixDef(DuckRule[] Ducking, double DuckAttack, double DuckRelease, int MaxVoices,
    // Spec A.3: tier 1 is never occluded beyond this.
    double TellOcclusionFloorDb, double OcclusionDb, double OcclusionLowpass, double MasterDb,
    SoundDuckRule[]? SoundDucking = null)
{
    public const string File = "audio/mix.json";
}

/// <summary>While any sound on <see cref="Tier"/> is audible, the listed tiers drop by <see cref="Db"/>.</summary>
public sealed record DuckRule(int Tier, int[] Ducks, double Db);

/// <summary>Within a tier: while any of <see cref="When"/> is audible, the named sounds drop (tells whose bands collide).</summary>
public sealed record SoundDuckRule(string[] When, string[] Ducks, double Db);
