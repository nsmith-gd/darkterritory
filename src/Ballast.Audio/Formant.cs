namespace Ballast.Audio;

/// <summary>
/// A formant shift on a sound (spec A.6 "formant-shifted crew voice: Soot Children"): its spectral envelope (the throat's
/// resonances, what makes a voice whose) moved up or down by <see cref="Shift"/>, its pitch and timing kept. Up by a fifth
/// or so, a grown man's voice comes out of a smaller throat: still his words, his pitch, not quite him.
/// </summary>
/// <param name="Shift">How far the formants move: 1.2 is a fifth of an octave up (a smaller throat), below 1 a bigger one.</param>
/// <param name="MaxGainDb">The most any bin is raised, so a valley moved under a formant doesn't bring its noise up with it.</param>
public sealed record FormantDef(double Shift = 1.2, double MaxGainDb = 24);

/// <summary>
/// One voice's formant shifter: a short-time spectrum (<see cref="Size"/> points, a quarter-window hop), each frame's
/// envelope found by cepstral smoothing and the frame reshaped by the envelope at f/shift over the envelope at f, so the
/// harmonics (the pitch) and the phases stay where they are. Streams a block at a time, <see cref="Size"/> samples late.
/// </summary>
internal sealed class FormantShifter
{
    /// <summary>The window: 43 ms at 48 kHz, long enough to resolve a low man's harmonics (100 Hz is four bins apart).</summary>
    public const int Size = 2048;
    const int Hop = Size / 4, Bits = 11;
    // The envelope's cepstral cutoff: quefrencies under 2.9 ms, so it resolves a formant to about 340 Hz and stops short of
    // a grown voice's pitch period (3.9 ms and up, under 255 Hz), which would put the harmonics into the envelope.
    const int Lifter = 140;
    static readonly float[] Window = Enumerable.Range(0, Size).Select(i => (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / Size))).ToArray();
    // Hann analysis and synthesis at a quarter hop overlap-add to 1.5.
    const float Gain = 1 / 1.5f;

    readonly FormantDef _def;
    readonly float[] _in = new float[Size], _out = new float[Size + Hop];
    readonly float[] _re = new float[Size], _im = new float[Size], _cre = new float[Size], _cim = new float[Size], _env = new float[Size / 2 + 1];
    readonly Queue<float> _ready = new();
    int _filled;

    public FormantShifter(FormantDef def)
    {
        _def = def;
        // Start a window late, in silence, so the first frame has a past.
        for (int i = 0; i < Size; i++)
            _ready.Enqueue(0);
    }

    /// <summary>Shifts a block in place (a window late).</summary>
    public void Process(Span<float> block)
    {
        for (int i = 0; i < block.Length; i++)
        {
            // Slide the input along and take a frame every hop.
            _in[_filled++ % Size] = block[i];
            if (_filled % Hop == 0 && _filled >= Size)
                Frame();
            block[i] = _ready.Count > 0 ? _ready.Dequeue() : 0;
        }
    }

    void Frame()
    {
        // The last Size samples, oldest first, windowed.
        int start = _filled % Size;
        for (int i = 0; i < Size; i++)
        {
            _re[i] = _in[(start + i) % Size] * Window[i];
            _im[i] = 0;
        }
        Transform(_re, _im, inverse: false);
        // The envelope: the log magnitude's cepstrum, the low quefrencies kept, back to a spectrum.
        for (int k = 0; k < Size; k++)
        {
            _cre[k] = (float)Math.Log(Math.Sqrt(_re[k] * _re[k] + _im[k] * _im[k]) + 1e-9);
            _cim[k] = 0;
        }
        Transform(_cre, _cim, inverse: true);
        for (int q = Lifter; q <= Size - Lifter; q++)
            _cre[q] = _cim[q] = 0;
        Transform(_cre, _cim, inverse: false);
        for (int k = 0; k <= Size / 2; k++)
            _env[k] = (float)Math.Exp(_cre[k]);
        // Each bin times the envelope as it'd be there with the throat shifted, over the envelope as it is.
        double shift = Math.Max(0.25, _def.Shift);
        float most = Audio.DbToGain(_def.MaxGainDb);
        for (int k = 1; k < Size / 2; k++)
        {
            double from = k / shift;
            int a = (int)from;
            float u = (float)(from - a);
            float moved = a + 1 <= Size / 2 ? _env[a] + (_env[a + 1] - _env[a]) * u : 0;
            float g = Math.Min(most, moved / Math.Max(_env[k], 1e-9f));
            _re[k] *= g;
            _im[k] *= g;
            // The other half's the mirror (a real signal).
            _re[Size - k] = _re[k];
            _im[Size - k] = -_im[k];
        }
        Transform(_re, _im, inverse: true);
        // Overlap-add, and the hop that's now whole is ready.
        for (int i = 0; i < Size; i++)
            _out[i] += _re[i] * Window[i] * Gain;
        for (int i = 0; i < Hop; i++)
            _ready.Enqueue(_out[i]);
        Array.Copy(_out, Hop, _out, 0, Size);
        Array.Clear(_out, Size, Hop);
    }

    static readonly int[] Reverse = Enumerable.Range(0, Size).Select(i =>
    {
        int r = 0;
        for (int b = 0; b < Bits; b++)
            r |= (i >> b & 1) << (Bits - 1 - b);
        return r;
    }).ToArray();
    static readonly float[] Cos = Enumerable.Range(0, Size / 2).Select(i => (float)Math.Cos(2 * Math.PI * i / Size)).ToArray();
    static readonly float[] Sin = Enumerable.Range(0, Size / 2).Select(i => (float)Math.Sin(2 * Math.PI * i / Size)).ToArray();

    /// <summary>In place, radix 2, as the reverb's (Fft) at this size: forward e^−iωt, inverse scaled by 1/N.</summary>
    static void Transform(float[] re, float[] im, bool inverse)
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
