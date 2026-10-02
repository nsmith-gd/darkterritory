namespace Ballast.Audio;

/// <summary>Writes 16-bit PCM WAV, for listening to offline renders, and reads WAV back for recorded music (App. E.6).</summary>
public static class Wav
{
    /// <summary>Parses a WAV file's bytes into a mono clip: PCM 8/16/24/32-bit or 32-bit float, channels averaged.</summary>
    public static AudioClip Read(ReadOnlySpan<byte> file)
    {
        if (file.Length < 12 || !file[..4].SequenceEqual("RIFF"u8) || !file.Slice(8, 4).SequenceEqual("WAVE"u8))
            throw new InvalidDataException("not a RIFF WAVE file");
        int format = 0, channels = 0, rate = 0, bits = 0;
        for (int at = 12; at + 8 <= file.Length;)
        {
            var id = file.Slice(at, 4);
            int size = BitConverter.ToInt32(file.Slice(at + 4, 4));
            var body = file.Slice(at + 8, Math.Min(size, file.Length - at - 8));
            if (id.SequenceEqual("fmt "u8))
            {
                format = BitConverter.ToUInt16(body);
                channels = BitConverter.ToUInt16(body[2..]);
                rate = BitConverter.ToInt32(body[4..]);
                bits = BitConverter.ToUInt16(body[14..]);
                // WAVE_FORMAT_EXTENSIBLE: the real format is the sub-format GUID's first two bytes.
                if (format == 0xFFFE && body.Length >= 26)
                    format = BitConverter.ToUInt16(body[24..]);
            }
            else if (id.SequenceEqual("data"u8))
            {
                if (channels <= 0 || rate <= 0 || bits is not (8 or 16 or 24 or 32) || format is not (1 or 3) || format == 3 && bits != 32)
                    throw new InvalidDataException($"unsupported WAV: format {format}, {bits} bits, {channels} channels");
                int bytes = bits / 8, frames = body.Length / (bytes * channels);
                var samples = new float[frames];
                for (int f = 0; f < frames; f++)
                {
                    float sum = 0;
                    for (int c = 0; c < channels; c++)
                    {
                        var s = body.Slice((f * channels + c) * bytes, bytes);
                        sum += format == 3 ? BitConverter.ToSingle(s) : bits switch
                        {
                            8 => (s[0] - 128) / 128f,
                            16 => BitConverter.ToInt16(s) / 32768f,
                            24 => (s[0] | s[1] << 8 | (sbyte)s[2] << 16) / 8388608f,
                            _ => BitConverter.ToInt32(s) / 2147483648f,
                        };
                    }
                    samples[f] = sum / channels;
                }
                return new AudioClip(samples, rate);
            }
            at += 8 + size + (size & 1);
        }
        throw new InvalidDataException("WAV file has no data chunk");
    }

    public static void Write(string path, ReadOnlySpan<float> interleaved, int channels = 2, int sampleRate = Audio.SampleRate)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var w = new BinaryWriter(File.Create(path));
        int bytes = interleaved.Length * 2;
        w.Write("RIFF"u8);
        w.Write(36 + bytes);
        w.Write("WAVEfmt "u8);
        w.Write(16);
        w.Write((short)1);
        w.Write((short)channels);
        w.Write(sampleRate);
        w.Write(sampleRate * channels * 2);
        w.Write((short)(channels * 2));
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(bytes);
        foreach (float s in interleaved)
            w.Write((short)Math.Clamp(Math.Round(s * 32767), -32768, 32767));
    }
}

/// <summary>Band-limited loudness, for checking that a tell sits clear of the bed in its own band (spec A.4 rule 1).</summary>
public static class Meter
{
    /// <summary>RMS level in dBFS of a stereo buffer (mono-summed) between two frequencies.</summary>
    public static double BandDb(ReadOnlySpan<float> stereo, double low, double high)
    {
        Biquad hp1 = default, hp2 = default, lp1 = default, lp2 = default;
        hp1.Set(FilterType.HighPass, low, 0.707);
        hp2.Set(FilterType.HighPass, low, 0.707);
        lp1.Set(FilterType.LowPass, high, 0.707);
        lp2.Set(FilterType.LowPass, high, 0.707);
        double sum = 0;
        int n = stereo.Length / 2;
        for (int i = 0; i < n; i++)
        {
            float x = 0.5f * (stereo[i * 2] + stereo[i * 2 + 1]);
            x = lp2.Process(lp1.Process(hp2.Process(hp1.Process(x))));
            sum += x * x;
        }
        return Audio.GainToDb(Math.Sqrt(sum / Math.Max(1, n)));
    }

    public static double Db(ReadOnlySpan<float> stereo) => BandDb(stereo, 20, 20000);
}
