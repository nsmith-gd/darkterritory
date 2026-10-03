using Concentus;

namespace Ballast.Audio;

/// <summary>
/// Reads Ogg Opus (RFC 7845) into a mono clip at 48 kHz, for recorded music (GDD v1.4 App. E.6; ARCHITECTURE §8 note 194).
/// Real recordings of 30-45 s are about 2 MB each as WAV and a sixth of that as Opus, and the repo already carries a
/// managed Opus (Concentus, the voice codec), so this is just the Ogg framing around it: pages, packets, the OpusHead's
/// pre-skip and output gain, and the last page's granule position for where the audio really ends. Channel mapping
/// family 0 only (mono or stereo, which is all a music file needs); channels are averaged to mono.
/// </summary>
public static class OggOpus
{
    /// <summary>Opus always decodes at 48 kHz, whatever rate the file was made from.</summary>
    public const int SampleRate = 48000;
    /// <summary>The longest Opus packet: 120 ms.</summary>
    const int MaxFrame = 5760;

    public static bool IsOggOpus(ReadOnlySpan<byte> file) => file.Length >= 4 && file[..4].SequenceEqual("OggS"u8);

    public static AudioClip Read(ReadOnlySpan<byte> file)
    {
        var packets = Packets(file, out long lastGranule);
        if (packets.Count < 2 || packets[0].Length < 19 || !packets[0].AsSpan(0, 8).SequenceEqual("OpusHead"u8))
            throw new InvalidDataException("not an Ogg Opus stream (no OpusHead)");
        var head = packets[0];
        int channels = head[9];
        int preSkip = BitConverter.ToUInt16(head, 10);
        short gainQ8 = BitConverter.ToInt16(head, 16);
        int family = head[18];
        if (family != 0 || channels is < 1 or > 2)
            throw new InvalidDataException($"unsupported Opus channel mapping: family {family}, {channels} channels");
        var decoder = OpusCodecFactory.CreateDecoder(SampleRate, channels);
        var frame = new float[MaxFrame * channels];
        var mono = new List<float>(packets.Count * 960);
        // Packet 1 is OpusTags; the audio starts at packet 2.
        for (int p = 2; p < packets.Count; p++)
        {
            int n = decoder.Decode(packets[p], frame, MaxFrame, false);
            for (int i = 0; i < n; i++)
                mono.Add(channels == 1 ? frame[i] : 0.5f * (frame[i * 2] + frame[i * 2 + 1]));
        }
        // The first pre-skip samples are the encoder's warm-up; the last page's granule (minus pre-skip) is the true length.
        long end = lastGranule > 0 ? Math.Min(mono.Count, lastGranule) : mono.Count;
        int start = Math.Min(preSkip, (int)end);
        var samples = new float[end - start];
        float gain = MathF.Pow(10, gainQ8 / 256f / 20f);
        for (int i = 0; i < samples.Length; i++)
            samples[i] = mono[start + i] * gain;
        return new AudioClip(samples, SampleRate);
    }

    /// <summary>The logical stream's packets in order (the first stream only), and the granule position of its last page.</summary>
    static List<byte[]> Packets(ReadOnlySpan<byte> file, out long lastGranule)
    {
        var packets = new List<byte[]>();
        var partial = new List<byte>();
        lastGranule = -1;
        int? serial = null;
        for (int at = 0; at + 27 <= file.Length;)
        {
            if (!file.Slice(at, 4).SequenceEqual("OggS"u8))
                throw new InvalidDataException($"Ogg page expected at byte {at}");
            long granule = BitConverter.ToInt64(file.Slice(at + 6, 8));
            int pageSerial = BitConverter.ToInt32(file.Slice(at + 14, 4));
            int segments = file[at + 26];
            int body = at + 27 + segments;
            if (body > file.Length)
                throw new InvalidDataException("truncated Ogg page");
            int size = 0;
            for (int s = 0; s < segments; s++)
                size += file[at + 27 + s];
            if (body + size > file.Length)
                throw new InvalidDataException("truncated Ogg page");
            serial ??= pageSerial;
            if (pageSerial == serial)
            {
                int offset = body;
                for (int s = 0; s < segments; s++)
                {
                    int lace = file[at + 27 + s];
                    partial.AddRange(file.Slice(offset, lace));
                    offset += lace;
                    // A lacing value under 255 ends the packet; 255 carries on into the next segment (or page).
                    if (lace < 255)
                    {
                        packets.Add([.. partial]);
                        partial.Clear();
                    }
                }
                if (granule >= 0)
                    lastGranule = granule;
            }
            at = body + size;
        }
        return packets;
    }
}
