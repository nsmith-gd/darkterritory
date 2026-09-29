using Ballast;

namespace Ballast.Audio;

/// <summary>
/// Every sound definition in a directory (<c>content/audio/sounds/*.json</c>), named by file. Reloads a file
/// when it changes on disk, so a designer can retune a howl while it plays; new instances pick it up.
/// </summary>
public sealed class SoundBank
{
    readonly Dictionary<string, SoundDef> _sounds = new();
    readonly Dictionary<string, DateTime> _stamps = new();

    public SoundBank(string? directory = null)
    {
        Directory = directory;
        if (directory is not null)
            Refresh();
    }

    public string? Directory { get; }
    public IReadOnlyCollection<string> Names => _sounds.Keys;
    public string? LastError { get; private set; }

    public SoundDef? Get(string name) => _sounds.GetValueOrDefault(name);
    public void Add(string name, SoundDef def) => _sounds[name] = def;

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
        return changed;
    }
}
