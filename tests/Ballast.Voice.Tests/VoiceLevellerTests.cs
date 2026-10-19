using Ballast.Voice;

namespace Ballast.Voice.Tests;

/// <summary>Note 855: every microphone sent at one speech level, a quiet headset as a hot desktop mic.</summary>
public class VoiceLevellerTests
{
    /// <summary>The speech-band RMS (dBFS) of what the leveller sends over the last second of talk, from a mic at <paramref name="micDb"/>.</summary>
    static double Sent(double micDb, LevellerSettings settings, double seconds = 3)
    {
        var speech = SyntheticSpeech.Generate(seconds);
        float mic = (float)Math.Pow(10, micDb / 20);
        var leveller = new VoiceLeveller(settings);
        var activity = new VoiceActivity();
        var frame = new float[VoiceFormat.FrameSamples];
        double sum = 0;
        int counted = 0, last = speech.Length - VoiceFormat.SampleRate;
        for (int f = 0; f + frame.Length <= speech.Length; f += frame.Length)
        {
            for (int i = 0; i < frame.Length; i++)
                frame[i] = speech[f + i] * mic;
            leveller.Process(frame, activity.Update(frame));
            if (f < last)
                continue;
            foreach (float v in frame)
                sum += v * v;
            counted += frame.Length;
        }
        return 10 * Math.Log10(sum / counted);
    }

    [Fact]
    public void AQuietMicIsSentAsLoudAsAHotOne()
    {
        // The director's playtest of 16 Oct evening, line 1: the host's Windows mic "way too low" for the joiner, whose Linux one
        // carried. 20 dB apart at the mic, they're sent within a decibel of each other.
        var on = new LevellerSettings();
        double hot = Sent(0, on), quiet = Sent(-20, on), between = Sent(-10, on);
        Assert.InRange(hot - quiet, -1, 1);
        Assert.InRange(hot - between, -1, 1);
        // Off, each as it comes.
        var off = on with { On = false };
        Assert.InRange(Sent(0, off) - Sent(-20, off), 19.5, 20.5);
    }

    [Fact]
    public void ItLiftsNoFurtherThanItsLimitAndNeverPastTheCeiling()
    {
        var on = new LevellerSettings();
        // 20 dB down is more than a 10 dB limit lets it lift: it comes up 10 and no more.
        var limited = on with { MaxBoostDb = 10 };
        Assert.InRange(Sent(-20, limited) - Sent(-20, on with { On = false }), 9.5, 10.2);
        // A shout at full scale is cut, and nothing it sends is over the ceiling.
        var speech = SyntheticSpeech.Generate(2);
        var leveller = new VoiceLeveller(on);
        var activity = new VoiceActivity();
        var frame = new float[VoiceFormat.FrameSamples];
        float peak = 0;
        for (int f = 0; f + frame.Length <= speech.Length; f += frame.Length)
        {
            for (int i = 0; i < frame.Length; i++)
                frame[i] = Math.Clamp(speech[f + i] * 4, -1f, 1f);
            leveller.Process(frame, activity.Update(frame));
            foreach (float v in frame)
                peak = Math.Max(peak, Math.Abs(v));
        }
        Assert.True(peak <= on.Ceiling + 1e-3, $"peak {peak}");
        Assert.True(leveller.GainDb < 0, $"gain {leveller.GainDb} dB");
    }

    [Fact]
    public void SilenceBetweenWordsDoesntPumpTheRoomUp()
    {
        // The room's hiss between talk spurts (not speech to the voice activity) leaves the gain where the talk put it.
        var on = new LevellerSettings();
        var leveller = new VoiceLeveller(on);
        var activity = new VoiceActivity();
        var speech = SyntheticSpeech.Generate(2);
        var frame = new float[VoiceFormat.FrameSamples];
        for (int f = 0; f + frame.Length <= speech.Length; f += frame.Length)
        {
            speech.AsSpan(f, frame.Length).CopyTo(frame);
            leveller.Process(frame, activity.Update(frame));
        }
        double talked = leveller.GainDb;
        var rng = new Random(3);
        for (int k = 0; k < 150; k++)
        {
            for (int i = 0; i < frame.Length; i++)
                frame[i] = (float)((rng.NextDouble() - 0.5) * 0.002);
            leveller.Process(frame, speaking: false);
        }
        Assert.InRange(leveller.GainDb - talked, -0.5, 0.5);
    }
}
