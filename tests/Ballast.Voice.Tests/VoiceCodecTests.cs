using Ballast.Voice;

namespace Ballast.Voice.Tests;

public class VoiceCodecTests
{
    static double Rms(ReadOnlySpan<float> x)
    {
        double sum = 0;
        foreach (float v in x)
            sum += v * v;
        return Math.Sqrt(sum / Math.Max(1, x.Length));
    }

    [Fact]
    public void SpeechSurvivesTheCodecAtAFewDozenBytesAFrame()
    {
        var enc = new VoiceEncoder();
        var dec = new VoiceDecoder();
        var input = SyntheticSpeech.Generate(1);
        var output = new List<float>();
        var packet = new byte[VoiceFormat.MaxPacket];
        int bytes = 0;
        ushort seq = 0;
        for (int f = 0; f + VoiceFormat.FrameSamples <= input.Length; f += VoiceFormat.FrameSamples)
        {
            int n = enc.Encode(input.AsSpan(f, VoiceFormat.FrameSamples), packet);
            bytes += n;
            dec.Decode(++seq, packet.AsSpan(0, n), pcm => output.AddRange(pcm.ToArray()));
        }
        Assert.Equal(input.Length, output.Count);
        // 24 kbit/s: ~60 bytes per 20 ms frame.
        Assert.InRange(bytes / 50.0, 20, 90);
        // Level preserved within a couple of dB (Opus isn't sample-exact, so compare energy).
        double ratio = Rms(output.ToArray().AsSpan(VoiceFormat.SampleRate / 4)) / Rms(input.AsSpan(VoiceFormat.SampleRate / 4));
        Assert.InRange(20 * Math.Log10(ratio), -2.5, 2.5);
    }

    [Fact]
    public void ALostFrameIsConcealedAndALateOneDropped()
    {
        var enc = new VoiceEncoder();
        var dec = new VoiceDecoder();
        var input = SyntheticSpeech.Generate(0.2);
        var packets = new List<byte[]>();
        var buffer = new byte[VoiceFormat.MaxPacket];
        for (int f = 0; f + VoiceFormat.FrameSamples <= input.Length; f += VoiceFormat.FrameSamples)
            packets.Add(buffer.AsSpan(0, enc.Encode(input.AsSpan(f, VoiceFormat.FrameSamples), buffer)).ToArray());
        int samples = 0;
        void Out(ReadOnlySpan<float> pcm) => samples += pcm.Length;
        dec.Decode(1, packets[0], Out);
        dec.Decode(2, packets[1], Out);
        dec.Decode(4, packets[3], Out); // 3 lost
        dec.Decode(3, packets[2], Out); // 3 arrives late: too late
        dec.Decode(5, packets[4], Out);
        Assert.Equal(1, dec.Concealed);
        Assert.Equal(1, dec.Dropped);
        Assert.Equal(5 * VoiceFormat.FrameSamples, samples);
    }

    [Fact]
    public void VoiceActivityHearsSpeechAndHoldsThroughTheGapsButNotSilence()
    {
        var vad = new VoiceActivity();
        var speech = SyntheticSpeech.Generate(1);
        int active = 0, frames = 0;
        for (int f = 0; f + VoiceFormat.FrameSamples <= speech.Length; f += VoiceFormat.FrameSamples, frames++)
            if (vad.Update(speech.AsSpan(f, VoiceFormat.FrameSamples)))
                active++;
        Assert.Equal(frames, active);
        var silence = new float[VoiceFormat.FrameSamples];
        for (int i = 0; i < 30; i++)
            vad.Update(silence);
        Assert.False(vad.Speaking);
    }
}
