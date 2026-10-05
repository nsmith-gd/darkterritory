using Concentus;
using Concentus.Enums;

namespace Ballast.Audio.Tests;

/// <summary>
/// The recorded music's Ogg Opus (GDD v1.4 App. E.6; ARCHITECTURE §8 note 194): a stream made here with Concentus's
/// encoder and RFC 7845's framing reads back at its length (pre-skip and the last granule honoured), its pitch and its level.
/// </summary>
public class OggOpusTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void AnOggOpusStreamReadsBackAtItsLengthPitchAndLevel(int channels)
    {
        const int rate = 48000, frame = 960, preSkip = 312;
        int length = rate + 4321; // not a whole number of frames: the last granule trims it
        var source = new float[length];
        for (int i = 0; i < length; i++)
            source[i] = 0.25f * MathF.Sin(2 * MathF.PI * 440 * i / rate);
        var bytes = Make(source, channels, preSkip, frame);
        Assert.True(OggOpus.IsOggOpus(bytes));
        var clip = OggOpus.Read(bytes);
        Assert.Equal(48000, clip.SampleRate);
        Assert.Equal(length, clip.Samples.Length);
        // 440 Hz: zero crossings over the middle second (away from the codec's attack).
        int crossings = 0;
        for (int i = 2401; i < 2400 + rate / 2; i++)
            if (clip.Samples[i - 1] < 0 != clip.Samples[i] < 0)
                crossings++;
        Assert.InRange(crossings, 435, 445);
        // The level survives the codec (RMS of a 0.25 sine is 0.177).
        double rms = Math.Sqrt(clip.Samples.Skip(4800).Take(rate / 2).Average(s => (double)s * s));
        Assert.InRange(rms, 0.16, 0.19);
    }

    [Fact]
    public void AWavIsStillAWav()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ballast-load-{Guid.NewGuid():N}.wav");
        try
        {
            Wav.Write(path, new float[200], channels: 2, sampleRate: 22050);
            Assert.Equal(22050, AudioClip.Load(path).SampleRate);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>An Ogg Opus file as an encoder writes one: OpusHead, OpusTags, then a packet per page for simplicity.</summary>
    static byte[] Make(float[] mono, int channels, int preSkip, int frame)
    {
        var encoder = OpusCodecFactory.CreateEncoder(48000, channels, OpusApplication.OPUS_APPLICATION_AUDIO);
        encoder.Bitrate = 96000;
        using var file = new MemoryStream();
        int sequence = 0;
        var head = new byte[19];
        "OpusHead"u8.CopyTo(head);
        head[8] = 1;
        head[9] = (byte)channels;
        BitConverter.GetBytes((ushort)preSkip).CopyTo(head, 10);
        BitConverter.GetBytes(48000u).CopyTo(head, 12);
        Page(file, head, 0, 2, sequence++);
        var tags = new byte[16];
        "OpusTags"u8.CopyTo(tags);
        Page(file, tags, 0, 0, sequence++);
        // Pre-skip samples of silence ahead (what the decoder will drop), then the signal, then padding to whole frames.
        int total = preSkip + mono.Length;
        int frames = (total + frame - 1) / frame;
        var pcm = new float[frames * frame * channels];
        for (int i = 0; i < mono.Length; i++)
            for (int c = 0; c < channels; c++)
                pcm[(preSkip + i) * channels + c] = mono[i];
        var packet = new byte[4000];
        for (int f = 0; f < frames; f++)
        {
            int n = encoder.Encode(pcm.AsSpan(f * frame * channels, frame * channels), frame, packet, packet.Length);
            bool last = f == frames - 1;
            long granule = last ? total : (long)(f + 1) * frame;
            Page(file, packet.AsSpan(0, n).ToArray(), granule, last ? 4 : 0, sequence++);
        }
        return file.ToArray();
    }

    static void Page(Stream s, byte[] packet, long granule, int flags, int sequence)
    {
        var lacing = new List<byte>();
        int left = packet.Length;
        while (left >= 255)
        {
            lacing.Add(255);
            left -= 255;
        }
        lacing.Add((byte)left);
        var page = new List<byte>();
        page.AddRange("OggS"u8.ToArray());
        page.Add(0);
        page.Add((byte)flags);
        page.AddRange(BitConverter.GetBytes(granule));
        page.AddRange(BitConverter.GetBytes(0x44540000));
        page.AddRange(BitConverter.GetBytes(sequence));
        page.AddRange(new byte[4]);
        page.Add((byte)lacing.Count);
        page.AddRange(lacing);
        page.AddRange(packet);
        var bytes = page.ToArray();
        BitConverter.GetBytes(Crc(bytes)).CopyTo(bytes, 22);
        s.Write(bytes);
    }

    /// <summary>Ogg's CRC-32: polynomial 0x04C11DB7, unreflected, from zero.</summary>
    static uint Crc(byte[] data)
    {
        uint crc = 0;
        foreach (byte b in data)
        {
            crc ^= (uint)b << 24;
            for (int k = 0; k < 8; k++)
                crc = (crc & 0x80000000) != 0 ? (crc << 1) ^ 0x04C11DB7 : crc << 1;
        }
        return crc;
    }
}
