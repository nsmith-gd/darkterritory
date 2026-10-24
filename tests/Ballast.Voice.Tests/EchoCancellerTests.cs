using Ballast.Voice;

namespace Ballast.Voice.Tests;

/// <summary>
/// Note 1019 (the director, 24 Oct: "My guest has a half second delay on their audio when they talk, they can hear their own
/// voice"): a crewmate on speakers with an open mic mustn't send the far crew back their own voice, nor lose their own words.
/// </summary>
public class EchoCancellerTests
{
    [Fact]
    public void TheFarCrewsVoiceIsTakenOutOfTheMicAndTheNearVoiceKept()
    {
        var r = EchoBench.Run();
        Assert.True(r.SuppressionDb >= 30, $"echo only {r.SuppressionDb} dB down");
        Assert.True(r.ConvergedSeconds <= 1, $"took {r.ConvergedSeconds} s to learn the room");
        Assert.True(r.NearKeptDb >= -0.5, $"B alone kept at {r.NearKeptDb} dB");
        Assert.True(r.DoubleTalkKeptDb >= -2, $"B over A kept at {r.DoubleTalkKeptDb} dB");
        Assert.True(r.AfterDoubleTalkDb >= 30, $"thrown off by the double talk: the echo after it only {r.AfterDoubleTalkDb} dB down");
        // The bug, and the gate: with nothing, the mic opened on the echo; now it doesn't, and still opens on B.
        Assert.True(r.OpenBefore > 0.5, $"the bench's echo should open a bare mic ({r.OpenBefore})");
        Assert.True(r.OpenOnEcho <= 0.03, $"the mic opened on {r.OpenOnEcho:P0} of the echo's frames");
        Assert.True(r.OpenOnNear >= r.OpenOnNearBefore - 0.02, $"the gate shut B out ({r.OpenOnNear} vs {r.OpenOnNearBefore})");
    }

    [Theory]
    [InlineData(0.02, 0.2, -6)]
    [InlineData(0.1, 0.3, -16)]
    public void ItHoldsInOtherRooms(double delay, double rt60, double coupling)
    {
        var r = EchoBench.Run(roomDelay: delay, rt60: rt60, couplingDb: coupling, seed: 3);
        Assert.True(r.SuppressionDb >= 25, $"echo only {r.SuppressionDb} dB down");
        Assert.True(r.DoubleTalkKeptDb >= -2, $"B over A kept at {r.DoubleTalkKeptDb} dB");
        Assert.True(r.AfterDoubleTalkDb >= 25, $"thrown off by the double talk: {r.AfterDoubleTalkDb} dB");
        Assert.True(r.OpenOnEcho <= 0.05, $"the mic opened on {r.OpenOnEcho:P0} of the echo's frames");
        Assert.InRange(r.RoomDelaySeconds, delay - 0.002, delay + 0.002);
    }

    [Fact]
    public void WithNothingPlayedTheMicComesThroughUntouchedButLate()
    {
        var c = new EchoCanceller(new EchoSettings());
        var voice = SyntheticSpeech.Talker(1, 5);
        var frame = (float[])voice.Clone();
        var silent = new float[VoiceFormat.FrameSamples];
        for (int f = 0; f + VoiceFormat.FrameSamples <= frame.Length; f += VoiceFormat.FrameSamples)
            c.Process(frame.AsSpan(f, VoiceFormat.FrameSamples), silent);
        for (int i = c.Latency; i < frame.Length; i++)
            Assert.Equal(voice[i - c.Latency], frame[i], 1e-4f);
    }

    [Fact]
    public void TheReferenceIsLinedUpWithWhatsPlayingAndSilentWhereItsNotHeld()
    {
        var played = new EchoReference(0.1);
        var block = new float[512];
        for (int i = 0; i < block.Length; i += 2)
            block[i] = block[i + 1] = i / 2 + 1;
        played.Write(block, queuedSeconds: 100.0 / VoiceFormat.SampleRate);
        Assert.Equal(156, played.Playing);
        var into = new float[4];
        played.Read(254, into);
        Assert.Equal([255f, 256f, 0f, 0f], into);
        played.Read(-2, into);
        Assert.Equal([0f, 0f, 1f, 2f], into);
    }
}
