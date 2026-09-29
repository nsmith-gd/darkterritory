using Ballast;
using Ballast.Audio;

namespace Ballast.Audio.Tests;

public class MixerTests
{
    static readonly MixDef Mix = new(
        [new DuckRule(1, [2, 3, 4, 5, 6], -9), new DuckRule(2, [5], -6)],
        DuckAttack: 0.02, DuckRelease: 0.3, MaxVoices: 64, TellOcclusionFloorDb: -6, OcclusionDb: -12, OcclusionLowpass: 900, MasterDb: 0);

    static SoundDef Tone(int tier, double frequency = 1000, bool loop = true, int max = 8) =>
        new(tier, [new LayerDef(SourceKind.Sine, 0.2, frequency)], Loop: loop, Duration: 1, MaxInstances: max, MinDistance: 2, MaxDistance: 500);

    static (Mixer Mixer, SoundBank Bank) Make(params (string Name, SoundDef Def)[] sounds)
    {
        var bank = new SoundBank();
        foreach (var (name, def) in sounds)
            bank.Add(name, def);
        var mixer = new Mixer(bank, Mix) { Listener = Listener.At(Double3.Zero, 0) };
        return (mixer, bank);
    }

    static float[] Render(Mixer m, double seconds)
    {
        var buffer = new float[(int)(seconds * Audio.SampleRate / Audio.Block) * Audio.Block * 2];
        m.Render(buffer);
        return buffer;
    }

    [Fact]
    public void ABandPassPassesItsCentreAndRejectsFarAway()
    {
        static double Through(double frequency)
        {
            var f = new Biquad();
            f.Set(FilterType.BandPass, 1000, 2);
            var buffer = new float[Audio.SampleRate];
            for (int i = 0; i < buffer.Length; i++)
                buffer[i] = (float)Math.Sin(2 * Math.PI * frequency * i / Audio.SampleRate);
            f.Process(buffer);
            return Audio.GainToDb(Math.Sqrt(buffer.Skip(4800).Average(x => x * x)) * Math.Sqrt(2));
        }
        Assert.InRange(Through(1000), -0.5, 0.5);
        Assert.True(Through(100) < -15);
        Assert.True(Through(8000) < -15);
    }

    [Fact]
    public void ATellDucksEverythingBelowItByNineDecibels()
    {
        // Spec A.3: tier 1 "ducks all else -9 dB".
        var (m, _) = Make(("bed", Tone(5, 200)), ("tell", Tone(1, 3000)));
        m.Play("bed", new Double3(0, 0, -2));
        Render(m, 0.5);
        Assert.Equal(1, m.TierGain(5), 3);
        m.Play("tell", new Double3(0, 0, -30));
        Render(m, 0.5);
        Assert.True(m.TierActive(1));
        Assert.InRange(Audio.GainToDb(m.TierGain(5)), -9.2, -8.8);
        Assert.Equal(1, m.TierGain(1), 3);
    }

    [Fact]
    public void VoiceDucksOnlyTheBed()
    {
        var (m, _) = Make(("bed", Tone(5, 200)), ("action", Tone(4, 500)), ("voice", Tone(2, 800)));
        m.Play("bed");
        m.Play("action");
        m.Play("voice");
        Render(m, 1);
        Assert.InRange(Audio.GainToDb(m.TierGain(5)), -6.2, -5.8);
        Assert.Equal(1, m.TierGain(4), 3);
    }

    [Fact]
    public void TellsAreNeverOccludedPastSixDecibels()
    {
        static double Level(int tier, float occlusion)
        {
            var (m, _) = Make(("s", Tone(tier, 440)));
            var v = m.Play("s", new Double3(0, 0, -2))!;
            v.Occlusion = occlusion;
            return Meter.Db(Render(m, 1).AsSpan(Audio.SampleRate));
        }
        Assert.InRange(Level(1, 0) - Level(1, 1), 5.5, 6.5);
        Assert.True(Level(5, 0) - Level(5, 1) > 11.5);
    }

    [Fact]
    public void OverItsLimitASoundStealsItsOldestInstance()
    {
        var (m, _) = Make(("s", Tone(1, max: 3)));
        var first = m.Play("s")!;
        for (int i = 0; i < 4; i++)
            m.Play("s");
        Render(m, 0.01);
        Assert.True(first.Finished);
        Assert.Equal(3, m.Voices.Count(v => !v.Finished));
    }

    [Fact]
    public void PastTheVoiceBudgetTheQuietestGoVirtualButTellsAlwaysPlay()
    {
        var (m, _) = Make(("bed", Tone(5, max: 100)), ("tell", Tone(1)));
        for (int i = 0; i < 70; i++)
            m.Play("bed", new Double3(0, 0, -2 - i));
        var tell = m.Play("tell", new Double3(0, 0, -400))!;
        Render(m, 0.02);
        Assert.Equal(64, m.RenderedVoices);
        Assert.False(tell.Virtual);
    }

    [Fact]
    public void ASoundOnTheRightIsLouderOnTheRight()
    {
        var (m, _) = Make(("s", Tone(4)));
        m.Play("s", new Double3(10, 0, 0));
        var b = Render(m, 0.5);
        double left = 0, right = 0;
        for (int i = 0; i < b.Length; i += 2)
        {
            left += b[i] * b[i];
            right += b[i + 1] * b[i + 1];
        }
        Assert.True(right > left * 10);
    }

    [Fact]
    public void DoublingTheDistanceCostsSixDecibels()
    {
        static double At(double d)
        {
            var (m, _) = Make(("s", Tone(4)));
            m.Play("s", new Double3(0, 0, -d));
            return Meter.Db(Render(m, 1).AsSpan(Audio.SampleRate));
        }
        Assert.InRange(At(10) - At(20), 5.7, 6.3);
    }

    [Fact]
    public void RendersAreDeterministic()
    {
        var noise = new SoundDef(1, [new LayerDef(SourceKind.Noise, 0.3, Filters: [new FilterDef(FilterType.BandPass, 900, 3)], Tremolo: new ModDef(7, 0.8, Jitter: 0.4))], Loop: true);
        float[] Once()
        {
            var (m, _) = Make(("n", noise));
            m.Play("n", new Double3(3, 0, -5));
            return Render(m, 0.5);
        }
        Assert.Equal(Once(), Once());
    }

    [Fact]
    public void AParameterCurveDrivesALayer()
    {
        // The bed "drives from sim state" (spec A.2): at zero speed the clank is silent.
        var def = new SoundDef(5, [new LayerDef(SourceKind.Sine, new Value(0, "speed", [[0, 0], [22, 0.5]]), 300)], Loop: true);
        var (m, _) = Make(("clank", def));
        var v = m.Play("clank")!;
        Assert.True(Meter.Db(Render(m, 0.3)) < -100);
        v.Params.Set("speed", 22);
        Assert.True(Meter.Db(Render(m, 0.3).AsSpan(Audio.SampleRate / 10)) > -20);
    }

    [Fact]
    public void SoundFilesParseNumbersAndCurves()
    {
        const string json = """
            // a comment
            { "tier": 5, "loop": true,
              "layers": [ { "source": "noise", "gain": { "param": "speed", "points": [[0, 0], [10, 1]] },
                            "filters": [ { "type": "bandPass", "frequency": 400, "q": 2 } ] } ] }
            """;
        var def = System.Text.Json.JsonSerializer.Deserialize<SoundDef>(json, DataFile.Options)!;
        var p = new ParamSet();
        p.Set("speed", 5);
        Assert.Equal(0.5, def.Layers[0].Gain.Evaluate(p), 6);
        Assert.Equal(400, def.Layers[0].Filters![0].Frequency.Evaluate(p));
    }
}
