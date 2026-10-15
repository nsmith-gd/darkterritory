using Ballast;
using DarkTerritory.Game.Art;
using DarkTerritory.Game.Sound;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The whimsies' pieces and sounds (ARCHITECTURE §8 note 715): each piece built, the calliope's pipes at their notes, the
/// canary high and bright, and dt screenshot's staging of their state.
/// </summary>
public class WhimsyArtTests
{
    [Fact]
    public void EachPieceIsBuilt()
    {
        foreach (var piece in new[] { WhimsyKit.Gerald(null), WhimsyKit.Gnome(null, 1), WhimsyKit.Cage(null, false), WhimsyKit.Cage(null, true),
            WhimsyKit.Gramophone(null, 0.8f), WhimsyKit.Calliope(null), WhimsyKit.Label(null) })
            Assert.True(piece.Vertices.Length > 0, piece.Name);
    }

    /// <summary>The strongest period in <paramref name="x"/> between <paramref name="minHz"/> and <paramref name="maxHz"/> (Hz).</summary>
    static double Pitch(float[] x, double minHz, double maxHz)
    {
        int rate = Ballast.Audio.Audio.SampleRate, from = rate / 4, n = rate / 4;
        double best = double.MinValue;
        int bestLag = 0;
        for (int lag = (int)(rate / maxHz); lag <= (int)(rate / minHz); lag++)
        {
            double sum = 0;
            for (int i = from; i < from + n; i++)
                sum += x[i] * x[i + lag];
            if (sum > best)
                (best, bestLag) = (sum, lag);
        }
        return (double)rate / bestLag;
    }

    [Fact]
    public void TheCalliopesPipesSoundTheirNotes()
    {
        foreach (int pitch in BandSynth.CalliopeTune.Distinct())
        {
            double hz = 440 * Math.Pow(2, (pitch - 69) / 12.0);
            double heard = Pitch(BandSynth.Calliope(pitch, 1.0), hz * 0.8, hz * 1.25);
            Assert.InRange(heard, hz * 0.98, hz * 1.02);
        }
    }

    [Fact]
    public void TheCanarySingsHighAndEveryPhraseIsItsOwn()
    {
        var a = BandSynth.Canary(0);
        var b = BandSynth.Canary(1);
        Assert.NotEqual(a, b);
        // Its trills are kilohertz: well over a thousand crossings a second where it's singing.
        int crossings = 0;
        for (int i = 1; i < a.Length; i++)
            if (Math.Sign(a[i]) != Math.Sign(a[i - 1]) && Math.Abs(a[i]) + Math.Abs(a[i - 1]) > 1e-3f)
                crossings++;
        Assert.True(crossings / (a.Length / (double)Ballast.Audio.Audio.SampleRate) > 2_000, $"{crossings} crossings");
    }

    [Fact]
    public void TheScreenshotStagesTheWhimsies()
    {
        var w = Staging.Whimsy("taken:5,canary:dead,playing");
        Assert.True(w.IsTaken(0) && !w.IsTaken(1) && w.IsTaken(2));
        Assert.Equal(Sim.Train.CanaryMood.Dead, w.Canary);
        Assert.True(w.Playing);
        Assert.Equal(default, Staging.Whimsy(""));
    }
}
