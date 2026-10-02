using Ballast;

namespace Ballast.Audio;

/// <summary>
/// Every sound definition in a directory (<c>content/audio/sounds/*.json</c>), named by file. Reloads a file
/// when it changes on disk, so a designer can retune a howl while it plays; new instances pick it up.
/// Sample layers' takes come from <see cref="Samples"/>, by default the directory's sibling <c>samples</c>
/// (<c>content/audio/samples</c>).
/// </summary>
public sealed class SoundBank
{
    readonly Dictionary<string, SoundDef> _sounds = new();
    readonly Dictionary<string, DateTime> _stamps = new();

    /// <param name="samples">The samples root, if not <paramref name="directory"/>'s sibling <c>samples</c>.</param>
    public SoundBank(string? directory = null, string? samples = null)
    {
        Directory = directory;
        samples ??= directory is null ? null : Path.Combine(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)))!, "samples");
        Samples = new SampleLibrary(samples, problem => LastError = problem);
        if (directory is not null)
            Refresh();
    }

    public string? Directory { get; }
    public SampleLibrary Samples { get; }
    public IReadOnlyCollection<string> Names => _sounds.Keys;
    /// <summary>The last problem: a file that wouldn't load, a sample that isn't there, a take that wouldn't decode.</summary>
    public string? LastError { get; private set; }

    public SoundDef? Get(string name) => _sounds.GetValueOrDefault(name);

    public void Add(string name, SoundDef def)
    {
        _sounds[name] = def;
        CheckSamples(name, def);
    }

    /// <summary>
    /// Decodes every take a sound's sample layers could pick now, rather than on its first play. Takes decode at about
    /// fifty times real time (managed Opus), so a long loop decoded as it starts would stall that frame. A sound edited
    /// on disk afterwards decodes lazily again.
    /// </summary>
    public void Preload(string name)
    {
        foreach (var layer in Get(name)?.Layers ?? [])
            if (layer.Source == SourceKind.Sample)
                foreach (var take in Samples.Takes(layer.Sample))
                    Samples.Clip(take);
    }

    /// <summary>Loads new and changed files. A malformed edit keeps the previous definition.</summary>
    public bool Refresh()
    {
        if (Directory is null || !System.IO.Directory.Exists(Directory))
            return false;
        bool changed = false;
        foreach (var path in System.IO.Directory.EnumerateFiles(Directory, "*.json"))
        {
            var stamp = File.GetLastWriteTimeUtc(path);
            if (_stamps.TryGetValue(path, out var seen) && seen == stamp)
                continue;
            _stamps[path] = stamp;
            try
            {
                _sounds[Path.GetFileNameWithoutExtension(path)] = DataFile.Load<SoundDef>(path);
                LastError = null;
                changed = true;
            }
            catch (Exception e) when (e is System.Text.Json.JsonException or IOException or InvalidDataException)
            {
                LastError = $"{Path.GetFileName(path)}: {e.Message}";
            }
        }
        if (changed)
        {
            // A sound saved may name takes that have been added or rebuilt since: look for them, and decode them, afresh.
            // Checked after every file has loaded, so a later file's clean load doesn't hide a missing take.
            Samples.Clear();
            foreach (var (name, def) in _sounds)
                CheckSamples(name, def);
        }
        return changed;
    }

    /// <summary>A sample layer naming nothing under the samples root plays silence; say so, as a bad file is.</summary>
    void CheckSamples(string name, SoundDef def)
    {
        foreach (var layer in def.Layers)
            if (layer.Source == SourceKind.Sample && Samples.Takes(layer.Sample).Count == 0)
                LastError = $"{name}.json: sample '{layer.Sample}' isn't a folder of {SampleLibrary.Extension} takes or a file under {Samples.Root ?? "(no samples root)"}";
    }
}
