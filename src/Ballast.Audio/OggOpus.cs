using System.Buffers.Binary;
using Concentus;
using Concentus.Structs;

namespace Ballast.Audio;

/// <summary>
/// Reads Ogg Opus files (RFC 7845) as <c>ffmpeg -c:a libopus</c> writes them, for <see cref="SourceKind.Sample"/> takes:
/// the file's first logical stream, mono or stereo (mapping family 0), decoded to mono at 48 kHz by Concentus. The
/// header's pre-skip is decoded and dropped from the front, and the last page's granule position trims the encoder's
/// padding off the end, so a take is exactly as long as what was encoded (a loop's seam lands where it was cut).
/// Page CRCs aren't checked: the takes are built by our own tools and shipped, not streamed over anything lossy.
/// </summary>
public static class OggOpus
{
    /// <summary>The longest an Opus packet can be: 120 ms at 48 kHz.</summary>
    const int MaxFrame = 5760;

    /// <summary>
    /// Decodes a whole file to 16-bit mono PCM at <see cref="Audio.SampleRate"/> (Opus always decodes at 48 kHz, whatever
    /// rate the header says the source was). <paramref name="gain"/> is the header's output gain, as a linear factor.
    /// </summary>
    /// <exception cref="InvalidDataException">Not an Ogg Opus file this reads, or a packet that won't decode.</exception>
    public static short[] Decode(ReadOnlySpan<byte> file, out float gain)
    {
        var packets = Packets(file, out long granule);
        if (packets.Count == 0 || packets[0].Length < 19 || !packets[0].AsSpan().StartsWith("OpusHead"u8))
            throw new InvalidDataException("not an Ogg Opus file (no OpusHead)");
        var head = packets[0].AsSpan();
        if (head[8] >> 4 != 0)
            throw new InvalidDataException($"OpusHead version {head[8]} isn't one this reads");
        int channels = head[9], preSkip = BinaryPrimitives.ReadUInt16LittleEndian(head[10..]), family = head[18];
        if (family != 0 || channels is < 1 or > 2)
            throw new InvalidDataException($"{channels} channels in mapping family {family}: export takes mono");
        // Q7.8 dB.
        gain = (float)Math.Pow(10, BinaryPrimitives.ReadInt16LittleEndian(head[16..]) / 256.0 / 20);
        int first = packets.Count > 1 && packets[1].AsSpan().StartsWith("OpusTags"u8) ? 2 : 1;

        try
        {
            // Sized up front from the packets' own frame counts, then trimmed to the true end the granule position gives.
            long decodable = 0;
            for (int i = first; i < packets.Count; i++)
            {
                int n = OpusPacketInfo.GetNumSamples(packets[i], Audio.SampleRate);
                if (n <= 0)
                    throw new InvalidDataException($"packet {i} isn't Opus");
                decodable += n;
            }
            long length = decodable - preSkip;
            if (granule >= 0)
                length = Math.Min(length, granule - preSkip);
            var pcm = new short[Math.Max(0, length)];

            // Mono out whatever went in: Opus mixes a stereo stream down itself.
            var decoder = OpusCodecFactory.CreateDecoder(Audio.SampleRate, 1);
            var frame = new short[MaxFrame];
            long produced = 0; // counting the pre-skip
            for (int i = first; i < packets.Count; i++)
            {
                int n = decoder.Decode(packets[i], frame, MaxFrame, false);
                if (n < 0)
                    throw new InvalidDataException($"packet {i} won't decode ({n})");
                long from = Math.Max(produced, preSkip), to = Math.Min(produced + n, preSkip + pcm.Length);
                if (to > from)
                    frame.AsSpan((int)(from - produced), (int)(to - from)).CopyTo(pcm.AsSpan((int)(from - preSkip)));
                produced += n;
            }
            return pcm;
        }
        catch (Exception e) when (e is OpusException or ArgumentException or IndexOutOfRangeException)
        {
            throw new InvalidDataException($"Opus: {e.Message}", e);
        }
    }

    /// <summary>
    /// The packets of the file's first logical stream, reassembled across lacing and continued pages, and the last
    /// granule position that stream's pages give (−1 if none does). Another stream in the file (chained or multiplexed)
    /// is skipped. A packet cut off by a missing page is dropped rather than glued to the next.
    /// </summary>
    public static List<byte[]> Packets(ReadOnlySpan<byte> file, out long granule)
    {
        const int Header = 27;
        var packets = new List<byte[]>();
        var partial = new List<byte>();
        uint? serial = null;
        bool skipping = false;
        granule = -1;
        int at = 0;
        while (file.Length - at >= Header)
        {
            var page = file[at..];
            if (!page.StartsWith("OggS"u8) || page[4] != 0)
                throw new InvalidDataException($"no Ogg page at byte {at}");
            bool continued = (page[5] & 1) != 0;
            long position = BinaryPrimitives.ReadInt64LittleEndian(page[6..]);
            uint stream = BinaryPrimitives.ReadUInt32LittleEndian(page[14..]);
            int segments = page[26];
            if (Header + segments > page.Length)
                throw new InvalidDataException($"truncated Ogg page at byte {at}");
            var lacing = page.Slice(Header, segments);
            int size = 0;
            foreach (byte l in lacing)
                size += l;
            if (Header + segments + size > page.Length)
                throw new InvalidDataException($"truncated Ogg page at byte {at}");
            var body = page.Slice(Header + segments, size);
            at += Header + segments + size;

            serial ??= stream;
            if (stream != serial)
                continue;
            // A fresh page means whatever packet was unfinished lost its end; a continued one we have no start for, its start.
            if (!continued)
                partial.Clear();
            else if (partial.Count == 0)
                skipping = true;
            int offset = 0;
            foreach (byte l in lacing)
            {
                if (!skipping)
                    partial.AddRange(body.Slice(offset, l));
                offset += l;
                // A lacing value under 255 ends a packet; 255 means it goes on (onto the next page, if this was the last).
                if (l < 255)
                {
                    if (!skipping && partial.Count > 0)
                        packets.Add([.. partial]);
                    partial.Clear();
                    skipping = false;
                }
            }
            // −1: no packet ends on this page.
            if (position != -1)
                granule = position;
        }
        return packets;
    }
}
