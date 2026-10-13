using Ballast;

namespace Ballast.Core.Tests;

/// <summary>
/// Note 687: a player's game reads its content once and asks the disk nothing after; a dev build's reloads an edited file,
/// told by the folder's watch. <see cref="HotReload.Enabled"/> is global, so this class's tests run one after another and
/// put it back.
/// </summary>
public sealed class HotReloadTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "dt-hot-" + Guid.NewGuid().ToString("N"));
    readonly bool _was = HotReload.Enabled;

    sealed record Tuning(int Speed);

    public HotReloadTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        HotReload.Enabled = _was;
        Directory.Delete(_dir, recursive: true);
    }

    string Write(int speed)
    {
        var path = Path.Combine(_dir, "train.json");
        File.WriteAllText(path, $"{{ \"speed\": {speed} }}");
        // A filesystem's times can be coarse: make each write a different one.
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(speed));
        return path;
    }

    [Fact]
    public void APlayersGameNeverRereadsAnEditedFile()
    {
        HotReload.Enabled = false;
        var hot = new HotData<Tuning>(Write(10));
        Write(20);
        Assert.False(hot.Refresh());
        Assert.Equal(10, hot.Value.Speed);
    }

    [Fact]
    public void ADevBuildRereadsAFileWhenItsFolderSaysItChanged()
    {
        HotReload.Enabled = true;
        var hot = new HotData<Tuning>(Write(10));
        var watch = HotReload.Watch(_dir);
        Assert.False(hot.Refresh()); // its first look: nothing's changed since it was read
        long before = watch.Generation;
        Write(20);
        // The watcher speaks on its own thread; give it a moment (a watcher that failed polls every second instead).
        var until = DateTime.UtcNow.AddSeconds(5);
        while (watch.Generation == before && !watch.Failed && DateTime.UtcNow < until)
            Thread.Sleep(20);
        bool reloaded = false;
        while (!reloaded && DateTime.UtcNow < until.AddSeconds(2))
        {
            reloaded = hot.Refresh();
            if (!reloaded)
                Thread.Sleep(50);
        }
        Assert.True(reloaded);
        Assert.Equal(20, hot.Value.Speed);
    }

    [Fact]
    public void AReloadLooksAtTheDiskOnlyWhenTheFolderChangedOrItsSlowPollComesRound()
    {
        HotReload.Enabled = true;
        var watch = HotReload.Watch(_dir);
        var check = new HotCheck();
        Assert.True(check.Due(watch)); // the first look
        if (watch.Failed)
            return; // no watcher here (a container out of inotify watches): polling every second is all there is
        Assert.False(check.Due(watch)); // nothing changed, and the poll is seconds off
        HotReload.Enabled = false;
        Write(30);
        Assert.False(check.Due(watch)); // off, whatever the folder says
    }
}
