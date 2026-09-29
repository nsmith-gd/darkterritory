using System.Text.Json.Nodes;
using Ballast;

namespace Ballast.Core.Tests;

/// <summary>Mods v1 (T49, roadmap M7): folders laid over the base content, in order, replacing, adding and patching files.</summary>
public sealed class ContentModsTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "dt-mods-" + Guid.NewGuid().ToString("N"));
    string Content => Path.Combine(_root, "content");
    string ModsDir => Path.Combine(_root, "mods");
    string Into => Path.Combine(_root, "mounted");

    public ContentModsTests()
    {
        Write(Content, "tuning/enemies.json", """
            // the base game
            { "draggers": { "grabRange": 1.0, "creepSpeed": 1.2 }, "costs": [1, 2, 3] }
            """);
        Write(Content, "tuning/train.json", """{ "cars": 6 }""");
        Write(Content, "audio/sounds/wind.json", """{ "gain": 1 }""");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    static void Write(string root, string rel, string text)
    {
        var path = Path.Combine(root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    void Mod(string name, int order, string manifestExtra = "", params (string Rel, string Text)[] files)
    {
        Write(ModsDir, $"{name}/mod.json", $$"""{ "name": "{{name}}", "version": "1.2", "order": {{order}} {{manifestExtra}} }""");
        foreach (var (rel, text) in files)
            Write(Path.Combine(ModsDir, name), rel, text);
    }

    [Fact]
    public void WithNoModsTheBaseContentIsUsedAsItIs()
    {
        Assert.Equal(Content, ContentMods.Mount(Content, ContentMods.Find(ModsDir), Into));
        Assert.False(Directory.Exists(Into));
        Assert.Empty(ContentMods.MountedIn(Content));
    }

    [Fact]
    public void ModsReplaceAddAndPatchInOrder()
    {
        // A patch that changes one number (and leaves the rest), a whole file replaced, a new file; and a later mod's patch
        // on top of the first's.
        Mod("hard-edges", order: 1, files: [
            ("tuning/enemies.json", """{ "$patch": true, "draggers": { "grabRange": 1.6 } }"""),
            ("tuning/train.json", """{ "cars": 12 }"""),
            ("audio/sounds/scream.json", """{ "gain": 2 }"""),
        ]);
        Mod("cheap-guns", order: 2, files: [("tuning/enemies.json", """{ "$patch": true, "costs": [9], "draggers": { "creepSpeed": 0.5 } }""")]);
        Mod("off", order: 0, manifestExtra: """, "enabled": false""", files: [("tuning/train.json", """{ "cars": 99 }""")]);

        var mods = ContentMods.Find(ModsDir);
        Assert.Equal(["hard-edges", "cheap-guns"], mods.Select(m => m.Name));
        var plan = ContentMods.Plan(Content, mods);
        Assert.Contains(new ModFile("tuning/enemies.json", "hard-edges", ModChange.Patched), plan);
        Assert.Contains(new ModFile("tuning/train.json", "hard-edges", ModChange.Replaced), plan);
        Assert.Contains(new ModFile("audio/sounds/scream.json", "hard-edges", ModChange.Added), plan);

        var root = ContentMods.Mount(Content, mods, Into);
        Assert.Equal(Into, root);
        var enemies = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "tuning/enemies.json")))!;
        Assert.Equal(1.6, (double)enemies["draggers"]!["grabRange"]!);
        Assert.Equal(0.5, (double)enemies["draggers"]!["creepSpeed"]!);
        Assert.Equal([9], enemies["costs"]!.AsArray().Select(n => (int)n!));
        Assert.Null(enemies["$patch"]);
        Assert.Equal("""{ "cars": 12 }""", File.ReadAllText(Path.Combine(root, "tuning/train.json")));
        Assert.True(File.Exists(Path.Combine(root, "audio/sounds/scream.json")));
        Assert.True(File.Exists(Path.Combine(root, "audio/sounds/wind.json")));
        Assert.Equal(["hard-edges 1.2", "cheap-guns 1.2"], ContentMods.MountedIn(root));
        // The base content isn't touched.
        Assert.Contains("\"grabRange\": 1.0", File.ReadAllText(Path.Combine(Content, "tuning/enemies.json")));
    }

    [Fact]
    public void TakingAModOutTakesItsFilesOut()
    {
        Mod("extra", order: 1, files: [("audio/sounds/scream.json", """{ "gain": 2 }""")]);
        Mod("other", order: 2, files: [("tuning/train.json", """{ "cars": 8 }""")]);
        ContentMods.Mount(Content, ContentMods.Find(ModsDir), Into);
        Directory.Delete(Path.Combine(ModsDir, "extra"), recursive: true);
        var root = ContentMods.Mount(Content, ContentMods.Find(ModsDir), Into);
        Assert.False(File.Exists(Path.Combine(root, "audio/sounds/scream.json")));
        Assert.Equal(["other 1.2"], ContentMods.MountedIn(root));
    }

    [Fact]
    public void APatchWithNothingUnderItIsRefused()
    {
        Mod("orphan", order: 1, files: [("tuning/nothing.json", """{ "$patch": true, "x": 1 }""")]);
        var e = Assert.Throws<InvalidDataException>(() => ContentMods.Plan(Content, ContentMods.Find(ModsDir)));
        Assert.Contains("orphan", e.Message);
        Assert.Contains("tuning/nothing.json", e.Message);
    }
}
