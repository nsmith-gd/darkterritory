using System.Numerics;
using System.Runtime.InteropServices;

namespace Ballast.Audio;

/// <summary>
/// A space's impulse response (spec A.6: "convolution with cheap impulse responses"), synthesised from its
/// <see cref="ReverbDef"/> rather than recorded: early reflections where the walls are, then a diffuse tail of decaying
/// noise whose highs die faster than its lows. Stereo (each ear its own noise), deterministic from the seed, and held
/// partitioned in the frequency domain, ready for a <see cref="Convolver"/>.
/// </summary>
public sealed class ImpulseResponse
{
    /// <summary>Bins kept per partition: 257 of a 512-point FFT, padded so a vector loop needs no tail.</summary>
    internal const int Bins = 272;
    /// <summary>Blocks of response: the cost, a complex multiply-add over each one a block.</summary>
    public int Partitions { get; }
    // Per ear, partition p's spectrum at [p * Bins + k].
    internal readonly float[][] Re, Im;

    /// <summary>The response itself, left and right, for looking at (and the tests).</summary>
    public float[] Left { get; }
    public float[] Right { get; }
    public double Seconds => (double)Left.Length / Audio.SampleRate;

    ImpulseResponse(float[] left, float[] right)
    {
        Left = left;
        Right = right;
        Partitions = Math.Max(1, (left.Length + Audio.Block - 1) / Audio.Block);
        Re = [new float[Partitions * Bins], new float[Partitions * Bins]];
        Im = [new float[Partitions * Bins], new float[Partitions * Bins]];
        var re = new float[Fft.Size];
        var im = new float[Fft.Size];
        float[][] ears = [left, right];
        for (int c = 0; c < 2; c++)
            for (int p = 0; p < Partitions; p++)
            {
                Array.Clear(re);
                Array.Clear(im);
                int from = p * Audio.Block, n = Math.Min(Audio.Block, ears[c].Length - from);
                ears[c].AsSpan(from, n).CopyTo(re);
                Fft.Transform(re, im, inverse: false);
                re.AsSpan(0, Fft.Size / 2 + 1).CopyTo(Re[c].AsSpan(p * Bins));
                im.AsSpan(0, Fft.Size / 2 + 1).CopyTo(Im[c].AsSpan(p * Bins));
            }
    }

    public static ImpulseResponse Synthesize(ReverbDef def)
    {
        int n = Math.Max(Audio.Block, (int)(Math.Clamp(def.Length, 0.01, 4) * Audio.SampleRate));
        var ears = new float[2][];
        // The diffuse tail: noise, split at the crossover, each band decaying at its own rate (60 dB in Decay for the
        // lows, Decay x HighDecay for the highs), starting at the predelay with a 5 ms swell rather than a step.
        double lowRt = Math.Max(0.01, def.Decay), highRt = Math.Max(0.01, def.Decay * def.HighDecay);
        int start = (int)(Math.Max(0, def.Predelay) * Audio.SampleRate), swell = Audio.SampleRate / 200;
        float split = (float)(1 - Math.Exp(-2 * Math.PI * def.Crossover / Audio.SampleRate));
        for (int c = 0; c < 2; c++)
        {
            var x = ears[c] = new float[n];
            var noise = new Noise(def.Seed * 2654435761u + (uint)c * 40503u + 1);
            float low = 0;
            for (int i = start; i < n; i++)
            {
                float w = noise.Next();
                low += split * (w - low);
                double t = (double)(i - start) / Audio.SampleRate;
                double rise = Math.Min(1, (i - start) / (double)swell);
                x[i] = (float)(rise * (low * Math.Exp(-6.9078 * t / lowRt) + (w - low) * Math.Exp(-6.9078 * t / highRt)));
            }
        }
        // Width: 0 is the same tail in both ears (a narrow bore), 1 each its own (a room around you).
        float width = (float)Math.Clamp(def.Width, 0, 1);
        for (int i = 0; i < n; i++)
            ears[1][i] = width * ears[1][i] + (1 - width) * ears[0][i];
        // The tail at unit energy, so its level is WetDb (and the send) whatever its length.
        for (int c = 0; c < 2; c++)
        {
            double energy = 0;
            foreach (float v in ears[c])
                energy += v * v;
            float scale = energy > 0 ? (float)(1 / Math.Sqrt(energy)) : 0;
            for (int i = 0; i < n; i++)
                ears[c][i] *= scale;
        }
        // Early reflections: each wall once, [seconds, gain against the sound itself], the right ear's a touch later or
        // sooner by turns so they don't image dead centre.
        int k = 0;
        foreach (var e in def.Early ?? [])
        {
            if (e.Length < 2)
                continue;
            double skew = 1 + 0.04 * (k++ % 2 == 0 ? 1 : -1) * width;
            int l = (int)(e[0] * Audio.SampleRate), r = (int)(e[0] * skew * Audio.SampleRate);
            if (l >= 0 && l < n)
                ears[0][l] += (float)e[1];
            if (r >= 0 && r < n)
                ears[1][r] += (float)e[1];
        }
        // The low end out (a boiler's rumble in a steel box is mud, not room), the whole at its level, and the last 30%
        // eased out (a raised cosine), so a long decay cut short at its length dies away rather than stopping.
        float wet = Audio.DbToGain(def.WetDb);
        int fade = Math.Max(1, n * 3 / 10);
        for (int c = 0; c < 2; c++)
        {
            var hp = new Biquad();
            hp.Set(FilterType.HighPass, Math.Max(10, def.Lowcut), 0.707);
            hp.Process(ears[c]);
            for (int i = 0; i < n; i++)
                ears[c][i] *= wet * (n - i >= fade ? 1f : 0.5f - 0.5f * MathF.Cos(MathF.PI * (n - i) / fade));
        }
        return new ImpulseResponse(ears[0], ears[1]);
    }
}

/// <summary>
/// Convolves a mono send with a stereo <see cref="ImpulseResponse"/>, a block (<see cref="Audio.Block"/>) at a time:
/// uniformly partitioned overlap-save, one 512-point FFT in and one out a block (both ears share them: left real, right
/// imaginary), and a complex multiply-add over the partitions' spectra, done on vectors. A 1.8 s tunnel is 338
/// partitions, about 50 µs a block. Fed silence for as long as its response, it goes quiet and costs nothing.
/// </summary>
public sealed class Convolver
{
    const int Bins = ImpulseResponse.Bins;
    readonly ImpulseResponse _ir;
    readonly float[] _history = new float[Audio.Block];
    // The input's spectra, newest at _head, oldest P - 1 behind it.
    readonly float[] _xRe, _xIm;
    readonly float[] _yRe0 = new float[Bins], _yIm0 = new float[Bins], _yRe1 = new float[Bins], _yIm1 = new float[Bins];
    readonly float[] _re = new float[Fft.Size], _im = new float[Fft.Size];
    int _head, _quietBlocks;

    public Convolver(ImpulseResponse ir)
    {
        _ir = ir;
        _xRe = new float[ir.Partitions * Bins];
        _xIm = new float[ir.Partitions * Bins];
        _quietBlocks = ir.Partitions + 1;
    }

    public ImpulseResponse Response => _ir;

    /// <summary>Something it was fed is still sounding.</summary>
    public bool Ringing => _quietBlocks <= _ir.Partitions;

    /// <summary>Convolves one block of <paramref name="input"/> and adds the result into <paramref name="left"/> and <paramref name="right"/>.</summary>
    public void Process(ReadOnlySpan<float> input, Span<float> left, Span<float> right)
    {
        bool silent = true;
        foreach (float s in input)
            if (s != 0)
            {
                silent = false;
                break;
            }
        _quietBlocks = silent ? _quietBlocks + 1 : 0;
        if (!Ringing)
        {
            input.CopyTo(_history);
            return;
        }

        // This block's spectrum, of the last two blocks of input (overlap-save).
        _head = (_head + 1) % _ir.Partitions;
        var xr = _xRe.AsSpan(_head * Bins, Bins);
        var xi = _xIm.AsSpan(_head * Bins, Bins);
        if (silent && IsZero(_history))
        {
            xr.Clear();
            xi.Clear();
        }
        else
        {
            _history.CopyTo(_re, 0);
            input.CopyTo(_re.AsSpan(Audio.Block));
            Array.Clear(_im);
            Fft.Transform(_re, _im, inverse: false);
            _re.AsSpan(0, Fft.Size / 2 + 1).CopyTo(xr);
            _im.AsSpan(0, Fft.Size / 2 + 1).CopyTo(xi);
        }
        input.CopyTo(_history);

        // Sum over partitions: the input p blocks ago through partition p of the response.
        Array.Clear(_yRe0);
        Array.Clear(_yIm0);
        Array.Clear(_yRe1);
        Array.Clear(_yIm1);
        var yr0 = MemoryMarshal.Cast<float, Vector<float>>(_yRe0.AsSpan());
        var yi0 = MemoryMarshal.Cast<float, Vector<float>>(_yIm0.AsSpan());
        var yr1 = MemoryMarshal.Cast<float, Vector<float>>(_yRe1.AsSpan());
        var yi1 = MemoryMarshal.Cast<float, Vector<float>>(_yIm1.AsSpan());
        for (int p = 0, slot = _head; p < _ir.Partitions; p++, slot = slot == 0 ? _ir.Partitions - 1 : slot - 1)
        {
            var ar = MemoryMarshal.Cast<float, Vector<float>>(_xRe.AsSpan(slot * Bins, Bins));
            var ai = MemoryMarshal.Cast<float, Vector<float>>(_xIm.AsSpan(slot * Bins, Bins));
            var hr0 = MemoryMarshal.Cast<float, Vector<float>>(_ir.Re[0].AsSpan(p * Bins, Bins));
            var hi0 = MemoryMarshal.Cast<float, Vector<float>>(_ir.Im[0].AsSpan(p * Bins, Bins));
            var hr1 = MemoryMarshal.Cast<float, Vector<float>>(_ir.Re[1].AsSpan(p * Bins, Bins));
            var hi1 = MemoryMarshal.Cast<float, Vector<float>>(_ir.Im[1].AsSpan(p * Bins, Bins));
            for (int j = 0; j < ar.Length; j++)
            {
                var a = ar[j];
                var b = ai[j];
                yr0[j] += a * hr0[j] - b * hi0[j];
                yi0[j] += a * hi0[j] + b * hr0[j];
                yr1[j] += a * hr1[j] - b * hi1[j];
                yi1[j] += a * hi1[j] + b * hr1[j];
            }
        }

        // Both ears out of one inverse FFT: Z = L + iR, each half mirrored from its own Hermitian spectrum.
        const int N = Fft.Size, H = N / 2;
        for (int k = 0; k <= H; k++)
        {
            _re[k] = _yRe0[k] - _yIm1[k];
            _im[k] = _yIm0[k] + _yRe1[k];
        }
        for (int k = 1; k < H; k++)
        {
            _re[N - k] = _yRe0[k] + _yIm1[k];
            _im[N - k] = _yRe1[k] - _yIm0[k];
        }
        Fft.Transform(_re, _im, inverse: true);
        for (int i = 0; i < Audio.Block; i++)
        {
            left[i] += _re[Audio.Block + i];
            right[i] += _im[Audio.Block + i];
        }
    }

    static bool IsZero(ReadOnlySpan<float> x)
    {
        foreach (float s in x)
            if (s != 0)
                return false;
        return true;
    }
}

/// <summary>An in-place radix-2 complex FFT of <see cref="Size"/> points (twice a mixer block), split real and imaginary.</summary>
static class Fft
{
    public const int Size = Audio.Block * 2;
    const int Bits = 9;
    static readonly int[] Reverse = Enumerable.Range(0, Size).Select(i => Rev(i)).ToArray();
    static readonly float[] Cos = Enumerable.Range(0, Size / 2).Select(i => (float)Math.Cos(2 * Math.PI * i / Size)).ToArray();
    static readonly float[] Sin = Enumerable.Range(0, Size / 2).Select(i => (float)Math.Sin(2 * Math.PI * i / Size)).ToArray();

    static int Rev(int i)
    {
        int r = 0;
        for (int b = 0; b < Bits; b++)
            r |= (i >> b & 1) << (Bits - 1 - b);
        return r;
    }

    /// <summary>Forward (e^−iωt) or inverse (scaled by 1/N, so the pair is the identity).</summary>
    public static void Transform(Span<float> re, Span<float> im, bool inverse)
    {
        for (int i = 0; i < Size; i++)
        {
            int j = Reverse[i];
            if (j > i)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }
        float sign = inverse ? 1 : -1;
        for (int len = 2; len <= Size; len <<= 1)
        {
            int half = len >> 1, step = Size / len;
            for (int i = 0; i < Size; i += len)
                for (int j = 0; j < half; j++)
                {
                    float wr = Cos[j * step], wi = sign * Sin[j * step];
                    int a = i + j, b = a + half;
                    float tr = re[b] * wr - im[b] * wi, ti = re[b] * wi + im[b] * wr;
                    re[b] = re[a] - tr;
                    im[b] = im[a] - ti;
                    re[a] += tr;
                    im[a] += ti;
                }
        }
        if (inverse)
        {
            float scale = 1f / Size;
            for (int i = 0; i < Size; i++)
            {
                re[i] *= scale;
                im[i] *= scale;
            }
        }
    }
}
