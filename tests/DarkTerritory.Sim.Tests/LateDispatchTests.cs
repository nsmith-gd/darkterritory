using System.Text.Json.Nodes;
using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 625 (queue #355): the example mod, Late Dispatch (tools/mods/example), held the main line ten minutes past dawn
/// (run.json dawnGraceSeconds), which only counts with the Wakers off (note 588: they're dawn now, and v1's line going live
/// is kept as a mod's <c>wakers.enabled: false</c>, GDD §8). It turns them off too, so it's the old dawn, held late: a little
/// more time, as it always meant. Mounted over the game's content as the game mounts it, and nothing else changed.
/// </summary>
public sealed class LateDispatchTests : IDisposable
{
    readonly string _into = Path.Combine(Path.GetTempPath(), "dt-latedispatch-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_into))
            Directory.Delete(_into, recursive: true);
    }

    [Fact]
    public void TheOldDawnHeldLateTheWakersAsleepAndTwelveMinutesGrace()
    {
        var content = DataFile.FindContentRoot();
        var mods = ContentMods.Scan(Path.GetFullPath(Path.Combine(content, "..", "tools", "mods")));
        Assert.Empty(mods.Problems);
        var late = Assert.Single(mods.Mods, m => m.Name == "LateDispatch");
        string mounted = ContentMods.Mount(content, [late], _into);

        // The game's own: the Wakers are dawn, and the grace is v1's two minutes.
        var enemies = DataFile.Load<EnemyTuning>(Path.Combine(content, EnemyTuning.File));
        Assert.True(enemies.Wakers.Enabled);
        // Mounted: asleep, and twelve minutes.
        var modded = DataFile.Load<EnemyTuning>(Path.Combine(mounted, EnemyTuning.File));
        var run = DataFile.Load<RunTuning>(Path.Combine(mounted, RunTuning.File));
        Assert.False(modded.Wakers.Enabled);
        Assert.Equal(720, run.DawnGraceSeconds);

        // Nothing else: the rest of the Wakers, and every other key of both files, as the game's.
        Assert.Equal(Wakers(Path.Combine(content, EnemyTuning.File)), Wakers(Path.Combine(mounted, EnemyTuning.File)));
        Assert.Equal(Without(Path.Combine(content, EnemyTuning.File), "wakers"), Without(Path.Combine(mounted, EnemyTuning.File), "wakers"));
        Assert.Equal(Without(Path.Combine(content, RunTuning.File), "dawnGraceSeconds"), Without(Path.Combine(mounted, RunTuning.File), "dawnGraceSeconds"));
    }

    static JsonObject Read(string path) =>
        (JsonObject)JsonNode.Parse(File.ReadAllText(path), documentOptions: new() { CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true })!;

    static string Without(string path, string key)
    {
        var node = Read(path);
        node.Remove(key);
        return node.ToJsonString();
    }

    /// <summary>The file's Wakers but for whether they're on.</summary>
    static string Wakers(string path)
    {
        var wakers = (JsonObject)Read(path)["wakers"]!;
        wakers.Remove("enabled");
        return wakers.ToJsonString();
    }
}
