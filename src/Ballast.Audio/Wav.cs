namespace Ballast.Audio;

/// <summary>Writes 16-bit PCM WAV, for listening to offline renders.</summary>
public static class Wav
{
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
