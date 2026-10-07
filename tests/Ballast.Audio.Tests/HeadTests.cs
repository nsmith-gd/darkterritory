using Ballast;
using Ballast.Audio;

namespace Ballast.Audio.Tests;

/// <summary>
/// The listener's head (spec A.4: a tell is "directional to within ~30°. Players must be able to say 'car four, left
/// side'"): side by the delay and the shadow, front from back by the pinna, the roof from the floor by its height cue.
/// </summary>
public class HeadTests
{
    static readonly HeadDef Head = new();
    static readonly MixDef Mix = new([], DuckAttack: 0.02, DuckRelease: 0.3, MaxVoices: 64, TellOcclusionFloorDb: -6,
        OcclusionDb: -12, OcclusionLowpass: 900, MasterDb: 0, Head: Head);

    /// <summary>A second of noise from <paramref name="at"/> (2 m out, inside the rolloff), heard by a listener at the origin facing −Z.</summary>
    static (float[] Left, float[] Right) Hear(Double3 at, int tier = 4)
    {
        var bank = new SoundBank();
        bank.Add("hiss", new SoundDef(tier, [new LayerDef(SourceKind.Noise, 0.2)], Loop: true, MinDistance: 3, MaxDistance: 100));
        var mixer = new Mixer(bank, Mix) { Listener = Listener.At(Double3.Zero, 0) };
        mixer.Play("hiss", at);
        var stereo = new float[Audio.SampleRate / Audio.Block * Audio.Block * 2];
        mixer.Render(stereo);
        // From a tenth of a second in: the delays and filters settled.
        int from = Audio.SampleRate / 10, n = stereo.Length / 2 - from;
        float[] left = new float[n], right = new float[n];
        for (int i = 0; i < n; i++)
        {
            left[i] = stereo[(from + i) * 2];
            right[i] = stereo[(from + i) * 2 + 1];
        }
        return (left, right);
    }

    /// <summary>How many samples later the left ear hears it than the right (negative: earlier), by cross-correlation.</summary>
    static int LeftLate(float[] left, float[] right)
    {
        int best = 0;
        double bestSum = double.NegativeInfinity;
        for (int lag = -48; lag <= 48; lag++)
        {
            double sum = 0;
            for (int i = 48; i < left.Length - 48; i++)
                sum += left[i] * right[i - lag];
            if (sum > bestSum)
                (bestSum, best) = (sum, lag);
        }
        return best;
    }

    static double Band(float[] x, FilterType type, double hz, double q = 0.707)
    {
        var f = new Biquad();
        f.Set(type, hz, q);
        double sum = 0;
        foreach (float s in x)
        {
            float y = f.Process(s);
            sum += y * y;
        }
        return Audio.GainToDb(Math.Sqrt(sum / x.Length));
    }

    static Double3 Around(double degrees, double distance = 2) =>
        new(distance * Math.Sin(degrees * Math.PI / 180), 0, -distance * Math.Cos(degrees * Math.PI / 180));

    [Fact]
    public void StraightAheadBothEarsHearTheSame()
    {
        var (left, right) = Hear(Around(0));
        for (int i = 0; i < left.Length; i++)
            Assert.Equal(left[i], right[i], 5);
    }

    [Fact]
    public void FromTheRightTheLeftEarHearsItLateAndDuller()
    {
        var (left, right) = Hear(Around(90));
        // Woodworth's delay for an 8.75 cm head at the side: 0.66 ms, 31.5 samples.
        Assert.InRange(LeftLate(left, right), 29, 34);
        // Louder on the right, and more so in the highs: the head's shadow on top of the pan.
        double lows = Band(right, FilterType.LowPass, 400) - Band(left, FilterType.LowPass, 400);
        double highs = Band(right, FilterType.HighPass, 5000) - Band(left, FilterType.HighPass, 5000);
        Assert.InRange(lows, 3, 14);
        Assert.True(highs - lows > 7, $"the shadow takes {highs - lows:0.0} dB more off the far ear's highs");
        // And the mirror image from the left.
        var (l2, r2) = Hear(Around(-90));
        Assert.InRange(LeftLate(l2, r2), -34, -29);
    }

    [Fact]
    public void ThirtyDegreesApartSoundApart()
    {
        // Spec A.4's ~30°: each step round to the side moves the delay by over 0.1 ms, which ears resolve to a few degrees.
        int[] lags = [.. new[] { 0, 30, 60, 90 }.Select(a => { var (l, r) = Hear(Around(a)); return LeftLate(l, r); })];
        Assert.Equal(0, lags[0]);
        for (int i = 1; i < lags.Length; i++)
            Assert.True(lags[i] - lags[i - 1] >= 6, $"{30 * (i - 1)}° to {30 * i}° moved the delay {lags[i] - lags[i - 1]} samples");
    }

    [Fact]
    public void BehindIsDullerThanInFrontAtTheSameAngleToTheSide()
    {
        // Front and back at 30° off the median plane have the same delay and shadow (the cone of confusion); the pinna
        // tells them apart: the highs from behind come down, the lows don't.
        var (fl, fr) = Hear(Around(30));
        var (bl, br) = Hear(Around(150));
        Assert.Equal(LeftLate(fl, fr), LeftLate(bl, br));
        double highs = Band(fr, FilterType.HighPass, 6000) - Band(br, FilterType.HighPass, 6000);
        double lows = Band(fr, FilterType.LowPass, 400) - Band(br, FilterType.LowPass, 400);
        Assert.InRange(lows, -0.5, 0.5);
        Assert.True(highs > 3.5, $"behind is {highs:0.0} dB down in the highs");
    }

    [Fact]
    public void OverheadSoundsUnlikeUnderfoot()
    {
        // The roof from the floor: the pinna's cue at 8 kHz, up above, a notch below.
        var (al, _) = Hear(new Double3(0, 2, 0));
        var (bl, _) = Hear(new Double3(0, -2, 0));
        double cue = Band(al, FilterType.BandPass, 8000, 2) - Band(bl, FilterType.BandPass, 8000, 2);
        double lows = Band(al, FilterType.LowPass, 400) - Band(bl, FilterType.LowPass, 400);
        Assert.True(cue > 5, $"overhead is {cue:0.0} dB over underfoot at 8 kHz");
        Assert.InRange(lows, -0.5, 0.5);
    }

    [Fact]
    public void ATellLosesNoMoreThanItsFloorToTheHead()
    {
        // Spec A.3's tier 1 is never masked: from straight behind and below, where the head cuts most, a tell's highs come
        // down by its floor (3 dB) at most, where anything else's come down by the pinna's whole shelf.
        var (front, _) = Hear(Around(0), tier: 1);
        var (tell, _) = Hear(new Double3(0, -1.2, 1.6), tier: 1);
        var (other, _) = Hear(new Double3(0, -1.2, 1.6));
        double tellLoss = Band(front, FilterType.HighPass, 6000) - Band(tell, FilterType.HighPass, 6000);
        double otherLoss = Band(front, FilterType.HighPass, 6000) - Band(other, FilterType.HighPass, 6000);
        Assert.InRange(tellLoss, 0.5, 3.6);
        Assert.True(otherLoss > tellLoss + 2, $"the pinna took {otherLoss:0.0} dB off a sound behind, {tellLoss:0.0} off a tell");
    }

    [Fact]
    public void WithoutAHeadItsTheSpeakerPan()
    {
        // MixDef.Head null: no delay, the level pan alone, all of it in the near ear at the side.
        var bank = new SoundBank();
        bank.Add("hiss", new SoundDef(4, [new LayerDef(SourceKind.Noise, 0.2)], Loop: true, MinDistance: 3, MaxDistance: 100));
        var mixer = new Mixer(bank, Mix with { Head = null }) { Listener = Listener.At(Double3.Zero, 0) };
        mixer.Play("hiss", Around(90));
        var stereo = new float[Audio.Block * 40 * 2];
        mixer.Render(stereo);
        double left = 0;
        for (int i = 0; i < stereo.Length; i += 2)
            left += Math.Abs(stereo[i]);
        Assert.True(left < 1e-3);
    }
}
