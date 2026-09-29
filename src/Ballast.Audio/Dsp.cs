namespace Ballast.Audio;

public static class Audio
{
    public const int SampleRate = 48000;
    /// <summary>Samples per mixer block. Parameters and spatial gains update once a block (5.3 ms).</summary>
    public const int Block = 256;

    public static float DbToGain(double db) => (float)Math.Pow(10, db / 20);
    public static double GainToDb(double gain) => 20 * Math.Log10(Math.Max(gain, 1e-9));
}

public enum FilterType : byte { LowPass, HighPass, BandPass, Peak }

/// <summary>RBJ-cookbook biquad, transposed direct form II. Coefficients can be retuned per block.</summary>
public struct Biquad
{
    float _b0, _b1, _b2, _a1, _a2, _z1, _z2;

    public void Set(FilterType type, double frequency, double q, double gainDb = 0)
    {
        double w = 2 * Math.PI * Math.Clamp(frequency, 10, Audio.SampleRate * 0.45) / Audio.SampleRate;
        double cos = Math.Cos(w), alpha = Math.Sin(w) / (2 * Math.Max(q, 0.05));
        double b0, b1, b2, a0, a1, a2;
        switch (type)
        {
            case FilterType.LowPass:
                b0 = (1 - cos) / 2; b1 = 1 - cos; b2 = b0; a0 = 1 + alpha; a1 = -2 * cos; a2 = 1 - alpha;
                break;
            case FilterType.HighPass:
                b0 = (1 + cos) / 2; b1 = -(1 + cos); b2 = b0; a0 = 1 + alpha; a1 = -2 * cos; a2 = 1 - alpha;
                break;
            case FilterType.BandPass:
                // Constant 0 dB peak gain.
                b0 = alpha; b1 = 0; b2 = -alpha; a0 = 1 + alpha; a1 = -2 * cos; a2 = 1 - alpha;
                break;
            default:
                double a = Math.Pow(10, gainDb / 40);
                b0 = 1 + alpha * a; b1 = -2 * cos; b2 = 1 - alpha * a; a0 = 1 + alpha / a; a1 = -2 * cos; a2 = 1 - alpha / a;
                break;
        }
        _b0 = (float)(b0 / a0); _b1 = (float)(b1 / a0); _b2 = (float)(b2 / a0); _a1 = (float)(a1 / a0); _a2 = (float)(a2 / a0);
    }

    public float Process(float x)
    {
        float y = _b0 * x + _z1;
        _z1 = _b1 * x - _a1 * y + _z2;
        _z2 = _b2 * x - _a2 * y;
        return y;
    }

    public void Process(Span<float> buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
            buffer[i] = Process(buffer[i]);
    }
}

/// <summary>One-pole smoother for gains, so per-block changes don't click.</summary>
public struct Smoothed(float value)
{
    public float Value = value;

    /// <summary>Moves toward <paramref name="target"/> with time constant <paramref name="seconds"/> over one block.</summary>
    public float Step(float target, double seconds)
    {
        float k = seconds <= 0 ? 1 : (float)(1 - Math.Exp(-Audio.Block / (seconds * Audio.SampleRate)));
        Value += (target - Value) * k;
        return Value;
    }
}

/// <summary>Deterministic white noise (xorshift32), so offline renders are bit-identical run to run.</summary>
public struct Noise(uint seed)
{
    uint _state = seed == 0 ? 0x9E3779B9u : seed;

    public float Next()
    {
        uint x = _state;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        _state = x;
        return (x >> 8) * (2f / (1 << 24)) - 1f;
    }
}
