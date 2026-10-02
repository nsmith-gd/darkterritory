using Ballast;
using Ballast.Audio;

namespace Ballast.Audio.Tests;

/// <summary>
/// The listener's space (spec A.6: "convolution with cheap impulse responses"), music's own tier under the rest (decided
/// 1 Oct), and voice in a tunnel (GDD §22: compressed and close, no exterior reference).
/// </summary>
public class SpaceTests
{
    static readonly MixDef Mix = new(
        [new DuckRule(1, [2, 3, 4, 5, 6], -9), new DuckRule(2, [5], -6),
         new DuckRule(1, [7], -12), new DuckRule(2, [7], -9), new DuckRule(3, [7], -6), new DuckRule(4, [7], -3), new DuckRule(5, [7], -2), new DuckRule(6, [7], -2)],
        DuckAttack: 0.02, DuckRelease: 0.3, MaxVoices: 64, TellOcclusionFloorDb: -6, OcclusionDb: -12, OcclusionLowpass: 900, MasterDb: 0,
        TierDb: [0, 0, 0, 0, 0, 0, -6]);

    static readonly ReverbDef TunnelReverb = new(Decay: 2.6, Length: 1.8, HighDecay: 0.35, Crossover: 1200, Predelay: 0.012, Lowcut: 90,
        Early: [[0.018, 0.45], [0.036, 0.34], [0.054, 0.26]], WetDb: -6, Seed: 37);
    static readonly SpaceDef Tunnel = new(TunnelReverb, Sends: [0.45, 0.9, 0.6, 0.9, 0.6, 0.5, 0], Muted: ["wind"],
        Voice: new CompressorDef(-32, 4, 0.005, 0.2, 7));
    static readonly SpaceDef Cab = new(new ReverbDef(0.35, 0.3, 0.7, 2000, 0.004, 220, [[0.0042, 0.32], [0.0068, 0.26]], -12, 0.7, 11),
        Sends: [0.35, 0.7, 0.5, 0.7, 0.35, 0.3, 0]);

    static SoundDef Tone(int tier, double frequency = 700, bool flat = false) =>
        new(tier, [new LayerDef(SourceKind.Sine, 0.2, frequency)], Loop: true, MinDistance: 2, MaxDistance: 500, Flat: flat);

    static (Mixer Mixer, SoundBank Bank) Make(params (string Name, SoundDef Def)[] sounds)
    {
        var bank = new SoundBank();
        foreach (var (name, def) in sounds)
            bank.Add(name, def);
        return (new Mixer(bank, Mix) { Listener = Listener.At(Double3.Zero, 0) }, bank);
    }

    static float[] Render(Mixer m, double seconds)
    {
        var buffer = new float[(int)(seconds * Audio.SampleRate / Audio.Block) * Audio.Block * 2];
        m.Render(buffer);
        return buffer;
    }

    [Fact]
    public void TheConvolverIsTheDirectSumOfTheResponse()
    {
        // Partitioned overlap-save against convolution done the long way, in both ears, over a few blocks of noise.
        var ir = ImpulseResponse.Synthesize(new ReverbDef(0.2, 0.03, Early: [[0.002, 0.5]], Width: 1, Seed: 5));
        var c = new Convolver(ir);
        var noise = new Noise(9);
        int blocks = 8, n = blocks * Audio.Block;
        var x = new float[n];
        for (int i = 0; i < 3 * Audio.Block; i++)
            x[i] = noise.Next();
        var left = new float[n];
        var right = new float[n];
        for (int b = 0; b < blocks; b++)
            c.Process(x.AsSpan(b * Audio.Block, Audio.Block), left.AsSpan(b * Audio.Block, Audio.Block), right.AsSpan(b * Audio.Block, Audio.Block));
        double worst = 0, peak = 0;
        for (int i = 0; i < n; i++)
        {
            double l = 0, r = 0;
            for (int k = 0; k < ir.Left.Length && k <= i; k++)
            {
                l += x[i - k] * ir.Left[k];
                r += x[i - k] * ir.Right[k];
            }
            worst = Math.Max(worst, Math.Max(Math.Abs(l - left[i]), Math.Abs(r - right[i])));
            peak = Math.Max(peak, Math.Abs(l));
        }
        Assert.True(peak > 0.1);
        Assert.True(worst < peak * 1e-4, $"worst {worst} of a peak of {peak}");
        // Its input long gone, it stops ringing and costs nothing.
        for (int b = 0; b < ir.Partitions + 2; b++)
            c.Process(new float[Audio.Block], new float[Audio.Block], new float[Audio.Block]);
        Assert.False(c.Ringing);
    }

    [Fact]
    public void AResponseIsTheSameEveryTimeAndDecaysAtItsRate()
    {
        var a = ImpulseResponse.Synthesize(TunnelReverb);
        Assert.Equal(a.Left, ImpulseResponse.Synthesize(TunnelReverb).Left);
        Assert.Equal(1.8, a.Seconds, 2);
        // A 2.6 s decay: 60 dB in 2.6 s, so between 0.4 s and 1.0 s about 14 dB (the highs faster, so a little more).
        static double Db(float[] x, double from, double to) =>
            Audio.GainToDb(Math.Sqrt(x.AsSpan((int)(from * Audio.SampleRate), (int)((to - from) * Audio.SampleRate)).ToArray().Average(v => (double)v * v)));
        double drop = Db(a.Left, 0.35, 0.45) - Db(a.Left, 0.95, 1.05);
        Assert.InRange(drop, 12, 22);
        // The ears differ: a room around you, not a voice in your head.
        Assert.NotEqual(a.Left, a.Right);
    }

    /// <summary>Plays a 0.1 s burst at 5 m and says how long the mix stays within 60 dB of the burst's level after it ends.</summary>
    static double Tail(SpaceDef? space)
    {
        var (m, _) = Make(("clap", new SoundDef(4, [new LayerDef(SourceKind.Noise, 0.3)], Duration: 0.1, MaxDistance: 500)));
        m.Space = space;
        m.Play("clap", new Double3(0, 0, -5));
        var mix = Render(m, 3);
        double loud = Meter.Db(mix.AsSpan(0, (int)(0.1 * Audio.SampleRate) * 2));
        int window = Audio.SampleRate / 50;
        double last = 0.1;
        for (int at = (int)(0.1 * Audio.SampleRate); at + window <= mix.Length / 2; at += window)
            if (Meter.Db(mix.AsSpan(at * 2, window * 2)) > loud - 60)
                last = (double)(at + window) / Audio.SampleRate;
        return last - 0.1;
    }

    [Fact]
    public void ASoundInATunnelRingsOnLongAfterItsDoneOutside()
    {
        double open = Tail(null), cab = Tail(Cab), tunnel = Tail(Tunnel);
        Assert.True(open < 0.05, $"outside it rang {open} s");
        Assert.True(cab > open && cab < 0.4, $"the cab rang {cab} s");
        Assert.True(tunnel > 1.0, $"the tunnel rang {tunnel} s");
    }

    [Fact]
    public void FlatSoundsStayDryAndTheReverbIsItsOwnStem()
    {
        var (m, _) = Make(("ui", Tone(4, flat: true)), ("step", Tone(4)));
        m.Space = Tunnel;
        var tap = new MeterTap((int)(0.75 * Audio.SampleRate / Audio.Block) * Audio.Block);
        m.Tap = tap;
        m.Play("ui");
        Render(m, 0.5);
        Assert.False(m.Reverberating);
        m.Play("step", new Double3(0, 0, -3));
        Render(m, 0.25);
        Assert.True(m.Reverberating);
        Assert.True(Meter.Db(tap.Stems[Mixer.ReverbStem]) > -60);
        // The stems still add up to the mix.
        var sum = new float[tap.Total.Length];
        foreach (var stem in tap.Stems.Values)
            for (int i = 0; i < sum.Length; i++)
                sum[i] += stem[i];
        Assert.Equal(tap.Total, sum, (a, b) => Math.Abs(a - b) < 1e-5f);
    }

    [Fact]
    public void LeavingASpaceLetsItsTailRingOutAndTheNextStartsEmpty()
    {
        var (m, _) = Make(("clap", new SoundDef(4, [new LayerDef(SourceKind.Noise, 0.3)], Duration: 0.1, MaxDistance: 500)));
        m.Space = Tunnel;
        m.Play("clap", new Double3(0, 0, -5));
        Render(m, 0.2);
        m.Space = null;
        // Out of the tunnel's mouth: what was in there still dies away, not cut off.
        Assert.True(Meter.Db(Render(m, 0.3)) > -50);
        Assert.True(m.Reverberating);
        Render(m, 2);
        Assert.False(m.Reverberating);
    }

    [Fact]
    public void MusicGivesWayToEveryTierAndDucksNothing()
    {
        for (int tier = 1; tier <= 6; tier++)
        {
            var (m, _) = Make(("music", Tone(7, 110, flat: true)), ("other", Tone(tier, 900)));
            m.Play("music");
            Render(m, 0.5);
            Assert.Equal(1, m.TierGain(7), 3);
            m.Play("other", new Double3(0, 0, -3));
            Render(m, 0.5);
            Assert.True(Audio.GainToDb(m.TierGain(7)) < -1.5, $"tier {tier} left the music at {Audio.GainToDb(m.TierGain(7)):F1} dB");
            // ... and the music never ducks what it's under, voice least of all.
            Assert.Equal(1, m.TierGain(tier), 3);
        }
        // Its fader keeps it low: 6 dB under the same sound on another tier.
        static double Level(int tier)
        {
            var (m, _) = Make(("s", Tone(tier, 110, flat: true)));
            m.Play("s");
            return Meter.Db(Render(m, 1).AsSpan(Audio.SampleRate / 2));
        }
        Assert.InRange(Level(6) - Level(7), 5.5, 6.5);
    }

    [Fact]
    public void ATunnelShutsOutTheWorldOutsideButNotTheTrain()
    {
        var (m, _) = Make(("wind", Tone(6, 300, flat: true)), ("wheel-rail", Tone(5, 120)));
        var tap = new MeterTap((int)(3 * Audio.SampleRate / Audio.Block) * Audio.Block);
        m.Tap = tap;
        m.Play("wind");
        m.Play("wheel-rail", new Double3(0, -1, 0));
        Render(m, 1);
        m.Space = Tunnel;
        Render(m, 2);
        int second = Audio.SampleRate * 2;
        double windOut = Meter.Db(tap.Stems["wind"].AsSpan(0, second)), windIn = Meter.Db(tap.Stems["wind"].AsSpan(tap.Total.Length - second));
        double wheelsOut = Meter.Db(tap.Stems["wheel-rail"].AsSpan(0, second)), wheelsIn = Meter.Db(tap.Stems["wheel-rail"].AsSpan(tap.Total.Length - second));
        Assert.True(windOut - windIn > 60, $"wind {windOut} dB outside, {windIn} dB in the tunnel");
        Assert.InRange(wheelsOut - wheelsIn, -0.5, 0.5);
    }

    [Fact]
    public void InATunnelVoiceIsCompressedAndClose()
    {
        // Spec A.5's curve: full to 8 m, logarithmic to nothing at 26 m. At 6 m and 18 m it's about 9 dB apart in the open.
        var voice = new SoundDef(2, [new LayerDef(SourceKind.Sine, 0.2, 400)], Loop: true, MinDistance: 8, MaxDistance: 26, Curve: RolloffCurve.Voice);
        double At(double metres, SpaceDef? space)
        {
            var (m, _) = Make(("voice", voice));
            m.Space = space;
            var tap = new MeterTap(Audio.SampleRate);
            m.Tap = tap;
            m.Play("voice", new Double3(0, 0, -metres));
            Render(m, 1);
            return Meter.Db(tap.Stems["voice"].AsSpan(Audio.SampleRate));
        }
        double open = At(6, null) - At(18, null), tunnel = At(6, Tunnel) - At(18, Tunnel);
        Assert.InRange(open, 7, 11);
        Assert.True(tunnel < open - 4, $"6 m against 18 m: {open:F1} dB in the open, {tunnel:F1} dB in the tunnel");
        // Close: the far voice comes up, it isn't just the near one going down.
        Assert.True(At(18, Tunnel) > At(18, null) + 3);
        // The compressor is the tunnel's: in the cab, voice is as it always was.
        Assert.Equal(At(18, null), At(18, Cab), 1);
    }
}
