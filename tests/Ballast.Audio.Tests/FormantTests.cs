using Ballast;
using Ballast.Audio;

namespace Ballast.Audio.Tests;

/// <summary>
/// Spec A.6's formant shift (the Soot Children's mimicry): a voice's resonances move, its pitch doesn't. Heard on an "ah"
/// made the way a throat makes one: a 120 Hz buzz through resonances at 720 Hz and 1200 Hz.
/// </summary>
public class FormantTests
{
    static readonly MixDef Mix = new([], DuckAttack: 0.02, DuckRelease: 0.3, MaxVoices: 64, TellOcclusionFloorDb: -6,
        OcclusionDb: -12, OcclusionLowpass: 900, MasterDb: 0);

    /// <summary>Two seconds of the vowel, mono, from half a second in (past the shifter's window), shifted by <paramref name="shift"/> or dry.</summary>
    static float[] Vowel(double? shift)
    {
        LayerDef Formant(double hz, double q, double gain) =>
            new(SourceKind.Impulse, gain, 120, Filters: [new FilterDef(FilterType.BandPass, hz, q)]);
        var bank = new SoundBank();
        bank.Add("ah", new SoundDef(2, [Formant(720, 6, 1), Formant(1200, 7, 0.5)], Loop: true, Flat: true,
            Formant: shift is { } s ? new FormantDef(s) : null));
        var mixer = new Mixer(bank, Mix);
        mixer.Play("ah");
        var stereo = new float[(int)(2.5 * Audio.SampleRate / Audio.Block) * Audio.Block * 2];
        mixer.Render(stereo);
        int from = Audio.SampleRate / 2;
        return [.. Enumerable.Range(from, stereo.Length / 2 - from).Select(i => stereo[i * 2])];
    }

    static double BandDb(float[] x, double hz, double q)
    {
        var f = new Biquad();
        f.Set(FilterType.BandPass, hz, q);
        double sum = 0;
        foreach (float s in x)
        {
            float y = f.Process(s);
            sum += y * y;
        }
        return 10 * Math.Log10(sum / x.Length + 1e-20);
    }

    /// <summary>Where the first resonance is: the loudest narrow band between 400 Hz and 1 kHz.</summary>
    static double FirstFormant(float[] x) =>
        Enumerable.Range(0, 61).Select(i => 400 + i * 10.0).MaxBy(hz => BandDb(x, hz, 8));

    /// <summary>The buzz's pitch, by the autocorrelation's peak between 60 and 400 Hz.</summary>
    static double Pitch(float[] x)
    {
        int best = 0;
        double bestSum = double.NegativeInfinity;
        for (int lag = Audio.SampleRate / 400; lag <= Audio.SampleRate / 60; lag++)
        {
            double sum = 0;
            for (int i = 0; i + lag < x.Length; i += 2)
                sum += x[i] * x[i + lag];
            if (sum > bestSum)
                (bestSum, best) = (sum, lag);
        }
        return (double)Audio.SampleRate / best;
    }

    [Fact]
    public void ShiftedUpTheResonancesRiseAndThePitchStays()
    {
        var dry = Vowel(null);
        var shifted = Vowel(1.2);
        // The throat a fifth smaller: its first resonance from 720 Hz to about 860 Hz (the harmonic nearest is 840 Hz).
        Assert.InRange(FirstFormant(dry), 680, 760);
        Assert.InRange(FirstFormant(shifted), 800, 920);
        // The same man's pitch, the same loudness, near enough.
        Assert.InRange(Pitch(shifted), Pitch(dry) - 3, Pitch(dry) + 3);
        double level(float[] x) => 10 * Math.Log10(x.Average(s => (double)s * s));
        Assert.InRange(level(shifted) - level(dry), -4, 4);
    }

    [Fact]
    public void UnshiftedItsTheSameVoiceAWindowLate()
    {
        // A shift of 1 leaves the spectrum as it is: the overlap-add gives back the input, FormantShifter.Size samples late.
        var dry = Vowel(null);
        var same = Vowel(1.0);
        int late = 2048;
        double err = 0, sum = 0;
        for (int i = late; i < dry.Length; i++)
        {
            double d = same[i] - dry[i - late];
            err += d * d;
            sum += (double)dry[i - late] * dry[i - late];
        }
        Assert.True(10 * Math.Log10(err / sum) < -40, $"residual {10 * Math.Log10(err / sum):0.0} dB");
    }
}
