using Ballast;
using Ballast.Audio;

namespace Ballast.Audio.Tests;

/// <summary>
/// Spec A.4 rule 4 ("non-repeating at short intervals") and A.6 ("tape saturation, light wow and flutter"): a loop with a
/// drift isn't the same twice round, and the tape's tiers wander in pitch while a tell holds still.
/// </summary>
public class TapeTests
{
    static readonly MixDef Mix = new([], DuckAttack: 0.02, DuckRelease: 0.3, MaxVoices: 64, TellOcclusionFloorDb: -6,
        OcclusionDb: -12, OcclusionLowpass: 900, MasterDb: 0);

    static float[] Left(Mixer m, double seconds)
    {
        var stereo = new float[(int)(seconds * Audio.SampleRate / Audio.Block) * Audio.Block * 2];
        m.Render(stereo);
        return [.. Enumerable.Range(0, stereo.Length / 2).Select(i => stereo[i * 2])];
    }

    /// <summary>A tone's frequency over a window, from its rising zero crossings (interpolated).</summary>
    static double Frequency(float[] x, int from, int length)
    {
        double first = -1, last = -1;
        int crossings = 0;
        for (int i = from + 1; i < from + length && i < x.Length; i++)
            if (x[i - 1] < 0 && x[i] >= 0)
            {
                double at = i - 1 + x[i - 1] / (x[i - 1] - x[i]);
                if (first < 0)
                    first = at;
                last = at;
                crossings++;
            }
        return (crossings - 1) * Audio.SampleRate / (last - first);
    }

    /// <summary>The tone's frequency in windows of 40 ms across the render, from a tenth of a second in.</summary>
    static double[] Track(float[] x) =>
        [.. Enumerable.Range(0, (x.Length - 4800) / 1920).Select(w => Frequency(x, 4800 + w * 1920, 1920))];

    [Fact]
    public void ALoopWithADriftIsNotTheSameTwiceRoundNorTwoOfItAlike()
    {
        var bank = new SoundBank();
        bank.Add("hum", new SoundDef(1, [new LayerDef(SourceKind.Sine, 0.2, 1000)], Loop: true, Flat: true, Drift: new DriftDef(0.5, 1.5, 0.4)));
        bank.Add("still", new SoundDef(1, [new LayerDef(SourceKind.Sine, 0.2, 1000)], Loop: true, Flat: true));
        double[] Heard(string sound, int skip)
        {
            var m = new Mixer(bank, Mix);
            // Another instance first takes the first seed: the second's is its own.
            for (int i = 0; i < skip; i++)
                m.Play(sound)!.Stop();
            m.Play(sound);
            return Track(Left(m, 2.5));
        }
        var one = Heard("hum", 0);
        var two = Heard("hum", 1);
        // It wanders: over the render the pitch moves by more than a tenth of a semitone (0.6%), within its half semitone.
        double spread = one.Max() / one.Min();
        Assert.InRange(spread, 1.006, Math.Pow(2, 1 / 12.0) + 0.002);
        // And no two instances wander alike.
        Assert.True(one.Zip(two).Max(p => Math.Abs(p.First - p.Second)) > 3, "two instances of a drifting loop sang the same");
        // Without one, it holds.
        var still = Heard("still", 0);
        Assert.True(still.Max() / still.Min() < 1.0005);
    }

    [Fact]
    public void ThroughTheTapeTheBedWandersAndTheTellHoldsStill()
    {
        var bank = new SoundBank();
        bank.Add("bed", new SoundDef(5, [new LayerDef(SourceKind.Sine, 0.1, 1000)], Loop: true, Flat: true));
        bank.Add("tell", new SoundDef(1, [new LayerDef(SourceKind.Sine, 0.1, 1000)], Loop: true, Flat: true));
        double[] Heard(string sound, TapeDef? tape)
        {
            var m = new Mixer(bank, Mix with { Tape = tape });
            m.Play(sound);
            return Track(Left(m, 2.5));
        }
        var tape = new TapeDef(Tiers: [3, 4, 5, 6, 7]);
        var bed = Heard("bed", tape);
        // The wow's ±0.12% and the flutter's ±0.03%: the pitch swings by more than a tenth of a percent, and not by more
        // than the two together.
        double swing = bed.Max() / bed.Min() - 1;
        Assert.InRange(swing, 0.001, 2 * (tape.WowDepth + tape.FlutterDepth) + 0.0005);
        // The tell isn't on the tape.
        var tell = Heard("tell", tape);
        Assert.True(tell.Max() / tell.Min() - 1 < 0.0002);
    }

    [Fact]
    public void AQuietSoundThroughTheTapeIsAsLoudAndALoudOneRounded()
    {
        var bank = new SoundBank();
        bank.Add("quiet", new SoundDef(5, [new LayerDef(SourceKind.Sine, 0.05, 440)], Loop: true, Flat: true));
        bank.Add("loud", new SoundDef(5, [new LayerDef(SourceKind.Sine, 1.2, 440)], Loop: true, Flat: true));
        double Db(string sound, TapeDef? tape)
        {
            var m = new Mixer(bank, Mix with { Tape = tape });
            m.Play(sound);
            var x = Left(m, 1).AsSpan(4800).ToArray();
            return 10 * Math.Log10(x.Average(s => (double)s * s));
        }
        var tape = new TapeDef(Tiers: [5]);
        Assert.InRange(Db("quiet", tape) - Db("quiet", null), -0.3, 0.3);
        // Saturated: a loud one comes out below what it went in at (the mixer's own soft clip is after both).
        Assert.True(Db("loud", tape) < Db("loud", null) - 0.3);
    }
}
