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
/// <para>
/// Decoding costs about 18 ms a second of audio (Concentus, managed), so a short take decodes where it's asked for
/// (<see cref="Request"/>, from the thread that mixes), and a long one (a file over <see cref="InlineBytes"/>: a 12 s loop
/// would stall that frame 0.2 s, a 96 s music loop 1.7 s) decodes on a worker while the layer waits for it in silence.
/// The game starts sounds and the device's thread renders them (note 667), so the library's own tables are locked; a
/// worker only ever decodes bytes into PCM, and what it made is collected (and any problem reported) by whoever asks next.
/// The game itself decodes nothing where it's asked for (<see cref="InlineBytes"/> 0): neither its frame nor the device
/// waits on a decode, and a sound's first play starts the few milliseconds late its decode takes.
/// </para>
/// </summary>
public sealed class SampleLibrary(string? root, Action<string>? report = null)
{
    public const string Extension = ".opus";
    readonly Dictionary<string, string[]> _takes = new(StringComparer.Ordinal);
    readonly Dictionary<string, SampleClip> _clips = new(StringComparer.Ordinal);
    readonly Dictionary<string, Task<Decoded>> _decoding = new(StringComparer.Ordinal);
    readonly object _gate = new();
    // Two at a time at most: a night's first minute can ask for a dozen loops, and the render thread shares the cores.
    static readonly TaskScheduler Workers = new ConcurrentExclusiveSchedulerPair(TaskScheduler.Default, maxConcurrencyLevel: 2).ConcurrentScheduler;

    /// <summary>What a worker made of one file: its PCM and gain, or why there's none.</summary>
    readonly record struct Decoded(short[] Pcm, float Gain, string? Error);

    public string? Root { get; } = root is null ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));

    /// <summary>
    /// A take whose file is bigger than this decodes on a worker (<see cref="Request"/>). The game's takes are 64 kbit/s
    /// Opus (tools/audio/install.py), about 8 KB a second plus a header, so the default is a take of a second or so: under
    /// 20 ms of decoding inline. <see cref="long.MaxValue"/> decodes everything inline, as an offline render wants (it
    /// plays every take the moment it's asked for, so a render is the same every time).
    /// </summary>
    public long InlineBytes { get; set; } = 10_000;

    /// <summary>Where workers' decodes run (two at a time on the thread pool unless a test steps them itself).</summary>
    public TaskScheduler Scheduler { get; set; } = Workers;

    /// <summary>Decoded takes held, and their PCM in bytes.</summary>
    public (int Clips, long Bytes) Held
    {
        get
        {
            lock (_gate)
                return (_clips.Count, _clips.Values.Sum(c => (long)c.Length * sizeof(short)));
        }
    }

    /// <summary>Takes being decoded on a worker (or decoded and not yet collected).</summary>
    public int Pending
    {
        get
        {
            lock (_gate)
                return _decoding.Count;
        }
    }

    /// <summary>
    /// The takes <paramref name="sample"/> stands for, as paths under the root with forward slashes: empty if it names
    /// nothing there (or somewhere outside the root: a mod's sound can't reach out of the samples).
    /// </summary>
    public IReadOnlyList<string> Takes(string? sample)
    {
        if (Root is null || string.IsNullOrWhiteSpace(sample))
            return [];
        lock (_gate)
            return TakesLocked(sample);
    }

    IReadOnlyList<string> TakesLocked(string sample)
    {
        if (_takes.TryGetValue(sample, out var takes))
            return takes;
        string full = Path.GetFullPath(Path.Combine(Root!, sample));
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

    /// <summary>A take (one of <see cref="Takes"/>), decoded now if it isn't already: waits for a worker that has it.</summary>
    public SampleClip Clip(string take)
    {
        lock (_gate)
            return ClipLocked(take);
    }

    SampleClip ClipLocked(string take)
    {
        if (_clips.TryGetValue(take, out var clip))
            return clip;
        if (_decoding.Remove(take, out var task))
        {
            task.Wait();
            return Keep(take, task.Result);
        }
        return Keep(take, Decode(Path.Combine(Root ?? "", take)));
    }

    /// <summary>
    /// A take if it's ready to play. A short one is decoded now; a long one (a file over <see cref="InlineBytes"/>) is
    /// handed to a worker the first time, and this is null until it's done: ask again each block.
    /// </summary>
    public SampleClip? Request(string take)
    {
        lock (_gate)
            return RequestLocked(take);
    }

    SampleClip? RequestLocked(string take)
    {
        if (_clips.TryGetValue(take, out var clip))
            return clip;
        // Its size looked up only when it decides (a file's size is a trip to the disk).
        bool inline = InlineBytes switch { <= 0 => false, long.MaxValue => true, _ => Bytes(take) <= InlineBytes };
        // Already on a worker (prefetched): taken if it's done, or waited for if it's one this would decode inline anyway,
        // so a short take, or any take offline, is always there the moment it's asked for.
        if (_decoding.TryGetValue(take, out var task))
            return task.IsCompleted || inline ? ClipLocked(take) : null;
        if (inline)
            return ClipLocked(take);
        Start(take);
        return null;
    }

    /// <summary>Starts a take decoding on a worker whatever its size (a loop the game will want), unless it's already held or under way.</summary>
    public void Prefetch(string take)
    {
        lock (_gate)
            if (!_clips.ContainsKey(take) && !_decoding.ContainsKey(take))
                Start(take);
    }

    /// <summary>Waits for every decode under way and keeps what they made.</summary>
    public void Settle()
    {
        lock (_gate)
            foreach (var take in _decoding.Keys.ToList())
                ClipLocked(take);
    }

    /// <summary>Forgets every listing and decoded take, so rebuilt or added takes are found and decoded afresh.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _takes.Clear();
            _clips.Clear();
            // A worker still decoding an old file finishes into nothing: nobody asks for its task again.
            _decoding.Clear();
        }
    }

    void Start(string take)
    {
        string path = Path.Combine(Root ?? "", take);
        _decoding[take] = Task.Factory.StartNew(() => Decode(path), CancellationToken.None, TaskCreationOptions.DenyChildAttach, Scheduler);
    }

    SampleClip Keep(string take, Decoded d)
    {
        if (d.Error is not null)
            report?.Invoke($"sample {take}: {d.Error}");
        return _clips[take] = new SampleClip(take, d.Pcm, d.Gain);
    }

    long Bytes(string take)
    {
        try
        {
            return new FileInfo(Path.Combine(Root ?? "", take)).Length;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return 0; // decoded inline, which reports the problem
        }
    }

    /// <summary>Reads and decodes one file. Touches nothing of the library's, so it can run on any thread.</summary>
    static Decoded Decode(string path)
    {
        try
        {
            var pcm = OggOpus.Decode(File.ReadAllBytes(path), out float gain);
            return new Decoded(pcm, gain, null);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return new Decoded([], 1, e.Message);
        }
    }

    string Relative(string path) => Path.GetRelativePath(Root!, path).Replace('\\', '/');
}
