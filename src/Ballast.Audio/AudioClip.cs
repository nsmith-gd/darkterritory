namespace Ballast.Audio;

/// <summary>
/// Recorded sound held in memory: mono samples at the file's own rate, played through a <see cref="SourceKind.Sample"/>
/// layer and resampled to the mixer's rate as it plays (GDD v1.4 App. E.6, the derailment's opera). Loaded once, at
/// startup: the mixer never touches the disk.
/// </summary>
public sealed class AudioClip(float[] samples, int sampleRate)
{
    public float[] Samples { get; } = samples;
    public int SampleRate { get; } = sampleRate;
    public double Seconds => (double)Samples.Length / SampleRate;

    /// <summary>
    /// The sample at a fractional position, Catmull-Rom between its neighbours (cleaner than linear going from 22 kHz up
    /// to 48 kHz); silence outside the clip.
    /// </summary>
    public float At(double position)
    {
        int i = (int)Math.Floor(position);
        float t = (float)(position - i);
        float p0 = Get(i - 1), p1 = Get(i), p2 = Get(i + 1), p3 = Get(i + 2);
        return p1 + 0.5f * t * (p2 - p0 + t * (2 * p0 - 5 * p1 + 4 * p2 - p3 + t * (3 * (p1 - p2) + p3 - p0)));
    }

    float Get(int i) => (uint)i < (uint)Samples.Length ? Samples[i] : 0;

    /// <summary>A WAV file: PCM at 8, 16, 24 or 32 bits, or 32-bit float, any channel count (summed to mono).</summary>
    public static AudioClip LoadWav(string path) => Wav.Read(File.ReadAllBytes(path));

    /// <summary>A recording, by what the file holds: Ogg Opus (decoded to 48 kHz, <see cref="OggOpus"/>) or WAV.</summary>
    public static AudioClip Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return OggOpus.IsOggOpus(bytes) ? OggOpus.Read(bytes) : Wav.Read(bytes);
    }
}
