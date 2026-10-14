using System.Collections.Concurrent;

namespace Ballast;

/// <summary>
/// Whether content reloads itself when it changes on disk, and how a reload knows to look (note 687). A player's game never
/// edits <c>content/</c>, so it's off there and nothing asks the disk anything per frame; a dev build (DEVTOOLS) and a mod
/// author (<c>--hot-reload</c>) turn it on. On, a <see cref="FileWatch"/> per folder says when something changed, and
/// <see cref="HotCheck"/> reads the disk only then, or on a slow poll for what a watcher misses (a network drive, an editor
/// that saves some odd way). Never used by the Sim: the host and its clients take tuning once, from the same files.
/// </summary>
public static class HotReload
{
    /// <summary>Off unless the app turns it on: a shipped game loads its content once.</summary>
    public static bool Enabled { get; set; }

    /// <summary>How long a reload waits between looks at the disk with no word from the watcher, and with a watcher that failed.</summary>
    public const double PollSeconds = 5, FailedPollSeconds = 1;

    static readonly ConcurrentDictionary<string, FileWatch> s_watches = new(StringComparer.Ordinal);

    /// <summary>The one watch on <paramref name="directory"/> and everything under it, shared by everything reloading from there.</summary>
    public static FileWatch Watch(string directory) =>
        s_watches.GetOrAdd(Path.GetFullPath(directory), dir => new FileWatch(dir));
}

/// <summary>
/// A folder watched for changes (note 687): every change under it bumps <see cref="Generation"/>, on the watcher's own
/// thread. A watcher that can't start or errors (too many watches, the folder gone) is <see cref="Failed"/>, and the
/// reloads fall back to polling it often.
/// </summary>
public sealed class FileWatch : IDisposable
{
    readonly FileSystemWatcher? _watcher;
    long _generation;
    volatile bool _failed;

    internal FileWatch(string directory)
    {
        Directory = directory;
        try
        {
            _watcher = new FileSystemWatcher(directory)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size,
            };
            _watcher.Changed += Bump;
            _watcher.Created += Bump;
            _watcher.Deleted += Bump;
            _watcher.Renamed += Bump;
            _watcher.Error += (_, _) => { _failed = true; Interlocked.Increment(ref _generation); };
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception e) when (e is IOException or ArgumentException or PlatformNotSupportedException or UnauthorizedAccessException)
        {
            _watcher?.Dispose();
            _watcher = null;
            _failed = true;
        }
    }

    public string Directory { get; }
    /// <summary>Counts the changes seen under the folder; a reload that last saw a different number has something to read.</summary>
    public long Generation => Interlocked.Read(ref _generation);
    /// <summary>The watcher couldn't start or has errored: reload by polling.</summary>
    public bool Failed => _failed;

    void Bump(object? sender, FileSystemEventArgs e) => Interlocked.Increment(ref _generation);

    public void Dispose() => _watcher?.Dispose();
}

/// <summary>
/// One reload's look at a <see cref="FileWatch"/> (note 687): <see cref="Due"/> is true when hot reload's on and the folder
/// has changed since this reload last looked, or its slow poll has come round. Kept per reload, as a field.
/// </summary>
public struct HotCheck
{
    long _seen;
    long _nextPollMs;

    /// <summary>Whether to read the disk now. Takes the watcher's word, and polls every <see cref="HotReload.PollSeconds"/> anyway.</summary>
    public bool Due(FileWatch watch)
    {
        if (!HotReload.Enabled)
            return false;
        long now = Environment.TickCount64;
        long generation = watch.Generation;
        if (generation != _seen || now >= _nextPollMs)
        {
            _seen = generation;
            _nextPollMs = now + (long)((watch.Failed ? HotReload.FailedPollSeconds : HotReload.PollSeconds) * 1000);
            return true;
        }
        return false;
    }
}
