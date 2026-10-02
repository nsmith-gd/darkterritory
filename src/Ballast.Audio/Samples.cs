namespace Ballast.Audio;

/// <summary>One decoded take: mono 16-bit PCM at <see cref="Audio.SampleRate"/>.</summary>
public sealed class SampleClip(string name, short[] pcm, float gain = 1)
{
    public string Name { get; } = name;
    public short[] Pcm { get; } = pcm;
    /// <summary>Takes a PCM value to full scale ±1, with the file's own output gain folded in.</summary>
    public float Scale { get; } = gain / 32768f;
    public int Length => Pcm.Length;
    public double Seconds => (double)Pcm.Length / Audio.SampleRate;
}

/// <summary>
/// The recorded sounds <see cref="SourceKind.Sample"/> layers play: Ogg Opus files under one root
/// (<c>content/audio/samples</c>). A sample is named by its path under the root, with forward slashes: a folder, whose
/// <c>*.opus</c> files (in ordinal order) are its takes, or one file, with or without the <c>.opus</c>. A folder wins
/// over a file of the same name.
/// <para>
/// A take is decoded the first time something plays it and kept, as 16 bit: hundreds of short takes, so half the memory
/// of float, and Opus is lossy below 16 bit's floor anyway. A file that won't read or decode plays as silence, and
/// <paramref name="report"/> hears why (<see cref="SoundBank.LastError"/>), once; it's never a crash.
/// </para>
/// </summary>
public sealed class SampleLibrary(string? root, Action<string>? report = null)
{
    public const string Extension = ".opus";
    readonly Dictionary<string, string[]> _takes = new(StringComparer.Ordinal);
    readonly Dictionary<string, SampleClip> _clips = new(StringComparer.Ordinal);

    public string? Root { get; } = root is null ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));

    /// <summary>Decoded takes held, and their PCM in bytes.</summary>
    public (int Clips, long Bytes) Held => (_clips.Count, _clips.Values.Sum(c => (long)c.Length * sizeof(short)));

    /// <summary>
    /// The takes <paramref name="sample"/> stands for, as paths under the root with forward slashes: empty if it names
    /// nothing there (or somewhere outside the root: a mod's sound can't reach out of the samples).
    /// </summary>
    public IReadOnlyList<string> Takes(string? sample)
    {
        if (Root is null || string.IsNullOrWhiteSpace(sample))
            return [];
        if (_takes.TryGetValue(sample, out var takes))
            return takes;
        string full = Path.GetFullPath(Path.Combine(Root, sample));
        if (!full.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            takes = [];
        else if (Directory.Exists(full))
            takes = [.. Directory.EnumerateFiles(full, "*" + Extension).Select(Relative).Order(StringComparer.Ordinal)];
        else if (File.Exists(full))
            takes = [Relative(full)];
        else if (File.Exists(full + Extension))
            takes = [Relative(full + Extension)];
        else
            takes = [];
        _takes[sample] = takes;
        return takes;
    }

    /// <summary>A take (one of <see cref="Takes"/>), decoded once.</summary>
    public SampleClip Clip(string take)
    {
        if (_clips.TryGetValue(take, out var clip))
            return clip;
        try
        {
            var pcm = OggOpus.Decode(File.ReadAllBytes(Path.Combine(Root ?? "", take)), out float gain);
            clip = new SampleClip(take, pcm, gain);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            report?.Invoke($"sample {take}: {e.Message}");
            clip = new SampleClip(take, []);
        }
        _clips[take] = clip;
        return clip;
    }

    /// <summary>Forgets every listing and decoded take, so rebuilt or added takes are found and decoded afresh.</summary>
    public void Clear()
    {
        _takes.Clear();
        _clips.Clear();
    }

    string Relative(string path) => Path.GetRelativePath(Root!, path).Replace('\\', '/');
}
