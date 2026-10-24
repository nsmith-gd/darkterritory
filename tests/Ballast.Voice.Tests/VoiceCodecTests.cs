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
    public void ASpeakerUnheardForHalfTheSequencesWrapIsHeardAgainAtOnce()
    {
        // The director, 24 Oct: a guest's voice gone after they'd been out of earshot. The sequence runs on through
        // silence (50 a second), so after about 11 minutes unheard a speaker's next number reads as behind what was played.
        var enc = new VoiceEncoder();
        var dec = new VoiceDecoder();
        var input = SyntheticSpeech.Generate(0.1);
        var buffer = new byte[VoiceFormat.MaxPacket];
        var packet = buffer.AsSpan(0, enc.Encode(input.AsSpan(0, VoiceFormat.FrameSamples), buffer)).ToArray();
        var order = new JitterBuffer<ushort>();
        var played = new List<ushort>();
        int samples = 0;
        void Out(ReadOnlySpan<float> pcm) => samples += pcm.Length;
        void Hear(ushort seq, double now)
        {
            order.Push(seq, seq, now);
            order.Drain(now, (s, _) => { played.Add(s); dec.Decode(s, packet, Out); });
        }
        Hear(100, 0);
        Hear(101, 0.02);
        // 12 minutes unheard: 36000 frames on, which is 29536 "behind" 102.
        ushort back = unchecked((ushort)(101 + 36000));
        Hear(back, 720);
        Hear(unchecked((ushort)(back + 1)), 720.02);
        Assert.Equal([100, 101, back, unchecked((ushort)(back + 1))], played);
        Assert.Equal(4 * VoiceFormat.FrameSamples, samples);
        Assert.Equal(0, dec.Dropped);
        // A packet a few frames late is still dropped, as ever.
        Hear(unchecked((ushort)(back - 3)), 720.04);
        Assert.Equal(4, played.Count);
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
