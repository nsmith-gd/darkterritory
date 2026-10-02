using Ballast;
using Ballast.Audio;

namespace Ballast.Audio.Tests;

/// <summary>GDD v1.4 App. E.6's machinery in the engine: recorded clips, the music bus, its duck and the game's low-pass, and LUFS.</summary>
public class MusicBusTests
{
    static readonly MusicBusDef Bus = new(LevelDb: 0, DuckDb: -6, DuckUnder: ["dead"], DuckAttack: 0.05, DuckRelease: 0.4,
        LowpassHz: 1200, LowpassTiers: [1, 3, 4, 5, 6], LowpassSeconds: 0.05);

    static readonly MixDef Mix = new(
        [new DuckRule(1, [2, 3, 4, 5, 6], -9), new DuckRule(2, [5], -6)],
        DuckAttack: 0.02, DuckRelease: 0.3, MaxVoices: 64, TellOcclusionFloorDb: -6, OcclusionDb: -12, OcclusionLowpass: 900, MasterDb: 0, Music: Bus);

    static readonly SoundDef Music = new(Mixer.MusicTier, [new LayerDef(SourceKind.Sample, 1)], Flat: true, MaxInstances: 1);

    static Mixer Make(params (string Name, SoundDef Def)[] sounds)
    {
        var bank = new SoundBank();
        bank.Add("music", Music);
        foreach (var (name, def) in sounds)
            bank.Add(name, def);
        return new Mixer(bank, Mix) { Listener = Listener.At(Double3.Zero, 0) };
    }

    static float[] Render(Mixer m, double seconds)
    {
        var buffer = new float[(int)(seconds * Audio.SampleRate / Audio.Block) * Audio.Block * 2];
        m.Render(buffer);
        return buffer;
    }

    static AudioClip Sine(double hz, double seconds, int rate = 22050, float amplitude = 0.5f) =>
        new([.. Enumerable.Range(0, (int)(seconds * rate)).Select(i => amplitude * (float)Math.Sin(2 * Math.PI * hz * i / rate))], rate);

    [Fact]
    public void AOneKilohertzToneReadsItsLevelInLufs()
    {
        // BS.1770: a 997 Hz sine at −20 dBFS peak on one channel is −23 LUFS (its RMS, −23 dB, and K-weighting's ~0 dB there).
        var tone = Sine(997, 5, 48000, 0.1f);
        Assert.InRange(Loudness.Integrated(tone.Samples, 48000), -23.2, -22.8);
        // The same at 22.05 kHz, where the music lives.
        Assert.InRange(Loudness.Integrated(Sine(997, 5, 22050, 0.1f).Samples, 22050), -23.2, -22.8);
        Assert.Equal(double.NegativeInfinity, Loudness.Integrated(new float[48000], 48000));
    }

    [Fact]
    public void AWavReadsBackAsItWasWritten()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ballast-wav-{Guid.NewGuid():N}.wav");
        try
        {
            var stereo = new float[2000];
            for (int i = 0; i < 1000; i++)
                (stereo[i * 2], stereo[i * 2 + 1]) = (0.5f, -0.25f);
            Wav.Write(path, stereo, channels: 2, sampleRate: 22050);
            var clip = AudioClip.LoadWav(path);
            Assert.Equal(22050, clip.SampleRate);
            Assert.Equal(1000, clip.Samples.Length);
            Assert.All(clip.Samples, s => Assert.Equal(0.125f, s, 3));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AClipPlaysAtItsOwnPitchFromWhereItsStartedAndEndsWithIt()
    {
        // 440 Hz recorded at 22.05 kHz, resampled to the mixer's 48 kHz: still 440 Hz, and a 2 s clip started 1.5 s in
        // plays half a second, then the voice is done.
        var m = Make();
        var v = m.Play("music")!;
        v.Clip = Sine(440, 2);
        v.ClipSeconds = 1.5;
        var out_ = Render(m, 0.4);
        int crossings = 0;
        for (int i = 1; i < out_.Length / 2; i++)
            if (out_[(i - 1) * 2] < 0 && out_[i * 2] >= 0)
                crossings++;
        Assert.InRange(crossings / 0.4, 435, 445);
        Render(m, 0.2);
        Assert.True(v.Finished);
        Assert.Empty(m.Voices);
    }

    [Fact]
    public void TheMusicDucksSixDecibelsUnderTheDeadChannelAndComesBack()
    {
        var dead = new SoundDef(2, [new LayerDef(SourceKind.Sine, 0.2, 300)], Loop: true, Flat: true);
        var m = Make(("dead", dead));
        m.Play("music")!.Clip = Sine(440, 10);
        Render(m, 0.3);
        Assert.Equal(1, m.MusicDuck, 3);
        // The music ducks nothing: no tier rule fires for it (it's outside the tiers).
        Assert.All(Enumerable.Range(1, Mixer.Tiers), t => Assert.False(m.TierActive(t)));
        Assert.All(Enumerable.Range(1, Mixer.Tiers), t => Assert.Equal(1, m.TierGain(t), 3));
        var voice = m.Play("dead")!;
        // 50 ms attack: most of the way down in 0.1 s, all of it in 0.5.
        Render(m, 0.1);
        Assert.True(Audio.GainToDb(m.MusicDuck) < -4.5, $"{Audio.GainToDb(m.MusicDuck)} dB after 0.1 s");
        Render(m, 0.4);
        Assert.InRange(Audio.GainToDb(m.MusicDuck), -6.1, -5.9);
        voice.Stop();
        // 400 ms release: under halfway back after 0.2 s, home after 2.
        Render(m, 0.2);
        Assert.True(Audio.GainToDb(m.MusicDuck) < -2.5);
        Render(m, 2);
        Assert.InRange(Audio.GainToDb(m.MusicDuck), -0.1, 0);
    }

    [Fact]
    public void WhileMusicPlaysTheGameIsMuffledAboveTwelveHundredHertzButVoicesAreNot()
    {
        var bed = new SoundDef(5, [new LayerDef(SourceKind.Sine, 0.2, 5000)], Loop: true, Flat: true);
        var talk = new SoundDef(2, [new LayerDef(SourceKind.Sine, 0.2, 5000)], Loop: true, Flat: true);
        var m = Make(("bed", bed), ("talk", talk));
        var tap = new MeterTap(Audio.SampleRate * 2);
        m.Tap = tap;
        m.Play("bed");
        m.Play("talk");
        Render(m, 0.5);
        // Stems are interleaved stereo: 0.25-0.5 s, before the music; 0.75-1 s, with it.
        double before = Meter.Db(tap.Stems["bed"].AsSpan(Audio.SampleRate / 2, Audio.SampleRate / 2));
        double talking = Meter.Db(tap.Stems["talk"].AsSpan(Audio.SampleRate / 2, Audio.SampleRate / 2));
        m.Play("music")!.Clip = Sine(440, 10, amplitude: 0.05f);
        Render(m, 1);
        Assert.Equal(1, m.GameLowpass, 2);
        Assert.True(m.MusicActive);
        // A 5 kHz bed, two octaves past the corner on two 12 dB/octave stages: ~−48 dB.
        double after = Meter.Db(tap.Stems["bed"].AsSpan(Audio.SampleRate * 2 - Audio.SampleRate / 2, Audio.SampleRate / 2));
        Assert.True(after < before - 30, $"bed {before:0.0} dB before, {after:0.0} dB with music");
        double voice = Meter.Db(tap.Stems["talk"].AsSpan(Audio.SampleRate * 2 - Audio.SampleRate / 2, Audio.SampleRate / 2));
        Assert.InRange(voice, talking - 0.5, talking + 0.5);
    }
}
