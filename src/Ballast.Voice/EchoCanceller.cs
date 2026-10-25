namespace Ballast.Voice;

/// <summary>The echo canceller's settings (the game's player.json <c>voices.echo</c>; note 1019).</summary>
public sealed record EchoSettings
{
    public bool On { get; init; } = true;
    /// <summary>How long an echo it learns, from the moment the sound left the speakers (s): the room's delay and its tail.</summary>
    public double TailSeconds { get; init; } = 0.3;
    /// <summary>The adaptive filter's step (0..1): faster to learn the room, noisier once it has.</summary>
    public double Step { get; init; } = 0.8;
    /// <summary>How hard what's left of the echo is suppressed after the filter: 1 takes a bin down by the echo's share of it, more errs on the side of quiet.</summary>
    public double Suppression { get; init; } = 3;
    /// <summary>The most a bin is taken down by that (dB, negative).</summary>
    public double FloorDb { get; init; } = -40;
    /// <summary>A frame this far under the echo the filter heard in it (dB) is the room's echo of the far crew: the mic doesn't open on it.</summary>
    public double GateDb { get; init; } = 15;
    /// <summary>The playback and the mic slipping this far apart (s: a hitch, a device lost) starts the room's learning again.</summary>
    public double ResyncSeconds { get; init; } = 0.25;
}

/// <summary>
/// What the speakers just played (note 1019), as the canceller's reference: the mixer's blocks as they're queued at the device,
/// mixed to mono, and how much the device still holds unplayed, so the mic is lined up with what's coming out of the speakers
/// now rather than what's been rendered ahead. Written from the audio thread, read from the game's.
/// </summary>
public sealed class EchoReference(double seconds = 2)
{
    readonly float[] _ring = new float[(int)(seconds * VoiceFormat.SampleRate)];
    readonly Lock _gate = new();
    long _written;
    long _queued;

    /// <summary>Interleaved stereo just queued at the device, and what the device holds unplayed now (s).</summary>
    public void Write(ReadOnlySpan<float> stereo, double queuedSeconds)
    {
        lock (_gate)
        {
            for (int i = 0; i + 1 < stereo.Length; i += 2)
                _ring[_written++ % _ring.Length] = 0.5f * (stereo[i] + stereo[i + 1]);
            _queued = (long)(Math.Max(0, queuedSeconds) * VoiceFormat.SampleRate);
        }
    }

    /// <summary>The sample the speakers are playing about now, counted from the first ever written.</summary>
    public long Playing
    {
        get
        {
            lock (_gate)
                return Math.Max(0, _written - _queued);
        }
    }

    /// <summary>Samples [<paramref name="from"/>, +length) of what was played; silence where it's not held (too old, or not yet).</summary>
    public void Read(long from, Span<float> into)
    {
        lock (_gate)
            for (int i = 0; i < into.Length; i++)
            {
                long at = from + i;
                into[i] = at >= 0 && at < _written && at >= _written - _ring.Length ? _ring[at % _ring.Length] : 0;
            }
    }
}

/// <summary>
/// Acoustic echo cancellation on the capture path (note 1019; the director, 24 Oct: "My guest has a half second delay on
/// their audio when they talk, they can hear their own voice"). A crewmate on speakers with an open mic sends back what their
/// speakers play: the far crew hear themselves, a network round trip late. This takes what the speakers played (the
/// reference) and learns the room between them and the mic, then takes its echo out of what the mic heard:
/// <list type="bullet">
/// <item>A partitioned frequency-domain adaptive filter (MDF: 10 ms blocks, 1024-point FFTs, <see cref="EchoSettings.TailSeconds"/>
/// long), normalised per bin, one partition's gradient constrained a block in turn.</item>
/// <item>Two of them for double talk: a background filter always learning, and the foreground filter the mic is cleaned with,
/// copied from the background only while it takes out more. The near voice speaking over the echo throws the background off,
/// never the foreground, so it can't learn to cancel the speaker's own words.</item>
/// <item>What the linear filter leaves is suppressed bin by bin by the echo's share of what's left (a 50% overlap, square-root
/// Hann, so the near voice alone comes through bit for bit, 10 ms late), down to <see cref="EchoSettings.FloorDb"/>.</item>
/// </list>
/// Managed, so it ships wherever the game does (no native AEC to license or build for each platform).
/// </summary>
public sealed class EchoCanceller
{
    /// <summary>The block it works in (10 ms); a capture frame is a whole number of them.</summary>
    public const int Block = 480;
    const int Size = 1024, Bins = Size / 2 + 1;
    /// <summary>An echo estimate this far under the bin's playback (−30 dB) is a bin not learned yet.</summary>
    const float Unlearned = 1e-3f;
    readonly int _parts;
    readonly float[][] _xr, _xi, _br, _bi, _fr, _fi;
    readonly float[] _x = new float[Size];
    readonly float[] _re = new float[Size], _im = new float[Size];
    readonly float[] _yb = new float[Block], _yf = new float[Block], _eb = new float[Block], _ef = new float[Block];
    readonly float[] _pxx = new float[Bins];
    readonly float[] _win = new float[2 * Block];
    readonly float[] _ePrev = new float[Block], _yPrev = new float[Block], _ola = new float[Block];
    readonly float[] _see = new float[Bins], _syy = new float[Bins];
    readonly float[] _sxx = new float[Bins];
    readonly float[] _cee = new float[Bins], _cyy = new float[Bins], _cer = new float[Bins], _cei = new float[Bins];
    readonly float[] _yr = new float[Size], _yi = new float[Size];
    readonly float[] _ref = new float[VoiceFormat.FrameSamples];
    readonly float _floor;
    int _head, _constrain, _better;
    bool _learned;
    double _sdd, _sbb, _sff;
    long _mic;
    long? _offset;

    public EchoCanceller(EchoSettings settings)
    {
        Settings = settings;
        _parts = Math.Max(1, (int)Math.Ceiling(settings.TailSeconds * VoiceFormat.SampleRate / Block));
        float[][] Make() => [.. Enumerable.Range(0, _parts).Select(_ => new float[Bins])];
        (_xr, _xi, _br, _bi, _fr, _fi) = (Make(), Make(), Make(), Make(), Make(), Make());
        for (int i = 0; i < _win.Length; i++)
            _win[i] = (float)Math.Sqrt(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / _win.Length));
        _floor = (float)Math.Pow(10, settings.FloorDb / 20);
    }

    public EchoSettings Settings { get; }
    /// <summary>How late the cleaned voice comes out (samples): the suppressor's overlap.</summary>
    public int Latency => Settings.On ? Block : 0;
    /// <summary>The echo the filter heard in the last frame (dBFS), and the level of what it let through (dBFS).</summary>
    public double EchoDb { get; private set; } = -120;
    public double OutDb { get; private set; } = -120;
    /// <summary>The last frame was mostly the room's echo of the far crew (<see cref="EchoSettings.GateDb"/>): an open mic stays shut on it.</summary>
    public bool MostlyEcho => EchoDb > -60 && OutDb < EchoDb - Settings.GateDb;
    /// <summary>Times the playback and the mic slipped apart and the room was learned again.</summary>
    public int Resyncs { get; private set; }

    /// <summary>
    /// Cleans one capture frame in place against what <paramref name="played"/> says the speakers were playing as it was heard.
    /// The two are lined up by sample count from the first frame on (both run at 48 kHz); a slip past
    /// <see cref="EchoSettings.ResyncSeconds"/> lines them up again.
    /// </summary>
    public void Capture(Span<float> frame, EchoReference played)
    {
        var reference = frame.Length <= _ref.Length ? _ref.AsSpan(0, frame.Length) : new float[frame.Length];
        long offset = played.Playing - frame.Length - _mic;
        if (_offset is not { } o || Math.Abs(offset - o) > Settings.ResyncSeconds * VoiceFormat.SampleRate)
        {
            if (_offset is not null)
                Resyncs++;
            _offset = offset;
            Relearn();
        }
        played.Read(_mic + _offset.Value, reference);
        _mic += frame.Length;
        Process(frame, reference);
    }

    /// <summary>Cleans <paramref name="mic"/> in place of the echo of <paramref name="reference"/> (the same length, a whole number of blocks).</summary>
    public void Process(Span<float> mic, ReadOnlySpan<float> reference)
    {
        if (mic.Length % Block != 0 || reference.Length != mic.Length)
            throw new ArgumentException($"the mic and its reference are the same whole number of {Block}-sample blocks");
        if (!Settings.On)
        {
            EchoDb = OutDb = -120;
            return;
        }
        double echo = 0, output = 0;
        for (int o = 0; o < mic.Length; o += Block)
        {
            var d = mic.Slice(o, Block);
            ProcessBlock(d, reference.Slice(o, Block));
            foreach (float v in _yf)
                echo += v * v;
            foreach (float v in d)
                output += v * v;
        }
        EchoDb = 10 * Math.Log10(Math.Max(echo / mic.Length, 1e-12));
        OutDb = 10 * Math.Log10(Math.Max(output / mic.Length, 1e-12));
    }

    /// <summary>Forgets the room (the reference has jumped): the filters and their history, not the voice in the overlap.</summary>
    void Relearn()
    {
        foreach (var a in (float[][])[.. _xr, .. _xi, .. _br, .. _bi, .. _fr, .. _fi])
            Array.Clear(a);
        Array.Clear(_x);
        Array.Clear(_syy);
        foreach (var a in (float[][])[_cee, _cyy, _cer, _cei, _sxx])
            Array.Clear(a);
        _sdd = _sbb = _sff = 0;
        _better = 0;
        _learned = false;
    }

    void ProcessBlock(Span<float> d, ReadOnlySpan<float> x)
    {
        // The reference's last Size samples, into the partitions' delay line.
        Array.Copy(_x, Block, _x, 0, Size - Block);
        x.CopyTo(_x.AsSpan(Size - Block));
        _head = (_head + 1) % _parts;
        _x.CopyTo(_re, 0);
        Array.Clear(_im);
        Fft.Transform(_re, _im, inverse: false);
        _re.AsSpan(0, Bins).CopyTo(_xr[_head]);
        _im.AsSpan(0, Bins).CopyTo(_xi[_head]);
        double far = 0;
        foreach (float v in x)
            far += v * v;
        for (int b = 0; b < Bins; b++)
            _sxx[b] = 0.5f * _sxx[b] + 0.5f * (_xr[_head][b] * _xr[_head][b] + _xi[_head][b] * _xi[_head][b]);
        Array.Clear(_pxx);
        for (int k = 0; k < _parts; k++)
            for (int b = 0; b < Bins; b++)
                _pxx[b] += _xr[k][b] * _xr[k][b] + _xi[k][b] * _xi[k][b];

        Estimate(_br, _bi, _yb);
        Estimate(_fr, _fi, _yf);
        double sdd = 0, sbb = 0, sff = 0;
        for (int i = 0; i < Block; i++)
        {
            _eb[i] = d[i] - _yb[i];
            _ef[i] = d[i] - _yf[i];
            sdd += d[i] * d[i];
            sbb += _eb[i] * _eb[i];
            sff += _ef[i] * _ef[i];
        }
        _sdd = 0.6 * _sdd + 0.4 * sdd;
        _sbb = 0.6 * _sbb + 0.4 * sbb;
        _sff = 0.6 * _sff + 0.4 * sff;
        const double Tiny = Block * 1e-9;
        // The background takes out more than the foreground, and more than nothing: it's learned the room better. Once the
        // room's learned, only while what the foreground leaves is mostly echo (it takes out at least half the mic), unless the
        // background's far better (the room changed): the near voice talking over the echo is what the foreground leaves
        // then, and a background that's caught a moment of it where its harmonics meet the far voice's mustn't be copied in.
        if (_sbb + Tiny < 0.7 * _sff && _sbb < _sdd && (!_learned || _sff < 0.5 * _sdd || _sbb < 0.2 * _sff))
        {
            if (++_better >= 3)
            {
                Copy(_br, _bi, _fr, _fi);
                _yb.CopyTo(_yf, 0);
                _eb.CopyTo(_ef, 0);
                _sff = _sbb;
                _better = 0;
                // Learned once it takes out three quarters of what the mic heard; till then every bin learns at the full step.
                _learned |= _sff < 0.25 * _sdd;
            }
        }
        else
            _better = 0;
        // Thrown off (the near voice over the echo, or the room changed under it): back to what was working.
        if (_sbb > 4 * _sff + Tiny && _sbb > _sdd)
        {
            Copy(_fr, _fi, _br, _bi);
            _sbb = _sff;
        }
        if (far > Block * 1e-7)
            Adapt();
        Suppress(d);
    }

    /// <summary>The echo a filter predicts in this block: Σ W_k·X_k, its last <see cref="Block"/> samples.</summary>
    void Estimate(float[][] wr, float[][] wi, float[] y)
    {
        Array.Clear(_re);
        Array.Clear(_im);
        for (int k = 0; k < _parts; k++)
        {
            int p = (_head - k + _parts) % _parts;
            float[] ar = wr[k], ai = wi[k], xr = _xr[p], xi = _xi[p];
            for (int b = 0; b < Bins; b++)
            {
                _re[b] += ar[b] * xr[b] - ai[b] * xi[b];
                _im[b] += ar[b] * xi[b] + ai[b] * xr[b];
            }
        }
        Mirror(_re, _im);
        Fft.Transform(_re, _im, inverse: true);
        _re.AsSpan(Size - Block, Block).CopyTo(y);
    }

    void Adapt()
    {
        Array.Clear(_re);
        Array.Clear(_im);
        _eb.CopyTo(_re, Size - Block);
        Fft.Transform(_re, _im, inverse: false);
        float mu = (float)Settings.Step, delta = _parts * Size * 1e-6f;
        for (int k = 0; k < _parts; k++)
        {
            int p = (_head - k + _parts) % _parts;
            float[] ar = _br[k], ai = _bi[k], xr = _xr[p], xi = _xi[p];
            for (int b = 0; b < Bins; b++)
            {
                // Double talk: once there's an echo estimate, each bin learns by the echo's share of what the foreground
                // leaves in it, so the near voice speaking over the echo barely moves it. The share is the echo estimate's
                // against the error, or the error's coherence with it (an error that's echo the filter's still short of is
                // coherent with its estimate; the near voice isn't), whichever's more.
                float coherence = (_cer[b] * _cer[b] + _cei[b] * _cei[b]) / (_cee[b] * _cyy[b] + 1e-20f);
                // A bin whose echo estimate is under what even a quiet room would give back (Unlearned) hasn't been learned
                // yet, or not since the voice moved into it: it learns at the full step, or it never would.
                float share = !_learned || _syy[b] < Unlearned * _sxx[b] ? 1 : Math.Max(coherence, _syy[b] / (_syy[b] + _see[b] + 1e-12f));
                float g = mu * share / (_pxx[b] + delta);
                ar[b] += g * (xr[b] * _re[b] + xi[b] * _im[b]);
                ai[b] += g * (xr[b] * _im[b] - xi[b] * _re[b]);
            }
        }
        // One partition's taps held to a block's length each time (the rest left unconstrained till their turn).
        var cr = _br[_constrain];
        var ci = _bi[_constrain];
        cr.CopyTo(_re, 0);
        ci.CopyTo(_im, 0);
        Mirror(_re, _im);
        Fft.Transform(_re, _im, inverse: true);
        Array.Clear(_re, Block, Size - Block);
        Array.Clear(_im);
        Fft.Transform(_re, _im, inverse: false);
        _re.AsSpan(0, Bins).CopyTo(cr);
        _im.AsSpan(0, Bins).CopyTo(ci);
        _constrain = (_constrain + 1) % _parts;
    }

    /// <summary>What the filter left of the echo, taken down bin by bin by its share; the block out is the one before this.</summary>
    void Suppress(Span<float> d)
    {
        Array.Clear(_re);
        Array.Clear(_im);
        Array.Clear(_yr);
        Array.Clear(_yi);
        for (int i = 0; i < 2 * Block; i++)
        {
            _re[i] = _win[i] * (i < Block ? _ePrev[i] : _ef[i - Block]);
            _yr[i] = _win[i] * (i < Block ? _yPrev[i] : _yf[i - Block]);
        }
        Fft.Transform(_re, _im, inverse: false);
        Fft.Transform(_yr, _yi, inverse: false);
        float s = (float)Settings.Suppression;
        for (int b = 0; b < Bins; b++)
        {
            float e2 = _re[b] * _re[b] + _im[b] * _im[b], y2 = _yr[b] * _yr[b] + _yi[b] * _yi[b];
            _see[b] = 0.5f * _see[b] + 0.5f * e2;
            _syy[b] = 0.5f * _syy[b] + 0.5f * y2;
            // Slower, for the coherence: a few frames' worth reads coherent by chance.
            _cee[b] = 0.85f * _cee[b] + 0.15f * e2;
            _cyy[b] = 0.85f * _cyy[b] + 0.15f * y2;
            _cer[b] = 0.85f * _cer[b] + 0.15f * (_re[b] * _yr[b] + _im[b] * _yi[b]);
            _cei[b] = 0.85f * _cei[b] + 0.15f * (_im[b] * _yr[b] - _re[b] * _yi[b]);
            float g = Math.Clamp(1 - s * _syy[b] / (_see[b] + 1e-12f), _floor, 1);
            _re[b] *= g;
            _im[b] *= g;
        }
        Mirror(_re, _im);
        Fft.Transform(_re, _im, inverse: true);
        for (int i = 0; i < Block; i++)
        {
            d[i] = _ola[i] + _re[i] * _win[i];
            _ola[i] = _re[Block + i] * _win[Block + i];
        }
        _ef.CopyTo(_ePrev, 0);
        _yf.CopyTo(_yPrev, 0);
    }

    static void Copy(float[][] fromR, float[][] fromI, float[][] toR, float[][] toI)
    {
        for (int k = 0; k < fromR.Length; k++)
        {
            fromR[k].CopyTo(toR[k], 0);
            fromI[k].CopyTo(toI[k], 0);
        }
    }

    /// <summary>A real signal's spectrum: bins past Nyquist from those below it.</summary>
    static void Mirror(float[] re, float[] im)
    {
        im[0] = 0;
        im[Size / 2] = 0;
        for (int b = 1; b < Size / 2; b++)
        {
            re[Size - b] = re[b];
            im[Size - b] = -im[b];
        }
    }

    static class Fft
    {
        const int Bits = 10;
        static readonly int[] Reverse = [.. Enumerable.Range(0, Size).Select(Rev)];
        static readonly float[] Cos = [.. Enumerable.Range(0, Size / 2).Select(i => (float)Math.Cos(2 * Math.PI * i / Size))];
        static readonly float[] Sin = [.. Enumerable.Range(0, Size / 2).Select(i => (float)Math.Sin(2 * Math.PI * i / Size))];

        static int Rev(int i)
        {
            int r = 0;
            for (int b = 0; b < Bits; b++)
                r |= (i >> b & 1) << (Bits - 1 - b);
            return r;
        }

        /// <summary>Forward (e^−iωt) or inverse (scaled by 1/N, so the pair is the identity).</summary>
        public static void Transform(float[] re, float[] im, bool inverse)
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
                for (int i = 0; i < Size; i++)
                {
                    re[i] /= Size;
                    im[i] /= Size;
                }
        }
    }
}
