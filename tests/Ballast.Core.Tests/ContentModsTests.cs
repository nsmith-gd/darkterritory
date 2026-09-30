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

    /// <summary>A Thunderstore package as a mod manager installs it: a Namespace-Name folder with manifest, README, icon, content/.</summary>
    string Package(string folder, string name, string version, string[] dependencies, params (string Rel, string Text)[] files)
    {
        var dir = Path.Combine(ModsDir, folder);
        Write(dir, "manifest.json", $$"""
            { "name": "{{name}}", "version_number": "{{version}}", "website_url": "", "description": "a test package",
              "dependencies": [{{string.Join(", ", dependencies.Select(d => $"\"{d}\""))}}] }
            """);
        Write(dir, "README.md", "# " + name);
        Png(Path.Combine(dir, "icon.png"), 256, 256);
        foreach (var (rel, text) in files)
            Write(Path.Combine(dir, "content"), rel, text);
        return dir;
    }

    /// <summary>Enough of a PNG for its size to be read: the signature and the IHDR chunk.</summary>
    static void Png(string path, int width, int height)
    {
        var b = new byte[33];
        new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' }.CopyTo(b, 0);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(16), width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(20), height);
        File.WriteAllBytes(path, b);
    }

    [Fact]
    public void AThunderstorePackageIsLaidOverTheContentFromItsContentFolder()
    {
        Package("Ace-HardEdges", "HardEdges", "1.0.0", [], ("tuning/train.json", """{ "cars": 20 }"""));
        var mod = Assert.Single(ContentMods.Find(ModsDir));
        Assert.True(mod.Thunderstore);
        Assert.Equal("Ace-HardEdges", mod.Id);
        Assert.Equal("1.0.0", mod.Version);
        var mounted = ContentMods.Mount(Content, [mod], Into);
        Assert.Contains("20", File.ReadAllText(Path.Combine(mounted, "tuning/train.json")));
        // The package's own files aren't content.
        Assert.False(File.Exists(Path.Combine(mounted, "manifest.json")));
        Assert.False(File.Exists(Path.Combine(mounted, "icon.png")));
        Assert.Equal(["Ace-HardEdges 1.0.0"], ContentMods.MountedIn(mounted));
    }

    [Fact]
    public void DependenciesLoadFirstWhateverTheOrderSays()
    {
        // Zed's package patches the file Ace's adds: Ace has to go first, though "order" and the name would put it last.
        Package("Zed-Base", "Base", "1.2.0", [], ("tuning/new.json", """{ "a": 1 }"""));
        Package("Ace-OnTop", "OnTop", "1.0.0", ["Zed-Base-1.1.0"], ("tuning/new.json", """{ "$patch": true, "b": 2 }"""));
        var scan = ContentMods.Scan(ModsDir);
        Assert.Empty(scan.Problems);
        Assert.Equal(["Zed-Base", "Ace-OnTop"], scan.Mods.Select(m => m.Id));
        var mounted = ContentMods.Mount(Content, scan.Mods, Into);
        var merged = JsonNode.Parse(File.ReadAllText(Path.Combine(mounted, "tuning/new.json")))!;
        Assert.Equal(1, (int)merged["a"]!);
        Assert.Equal(2, (int)merged["b"]!);
    }

    [Fact]
    public void AMissingOrTooOldDependencyOrACircleLeavesTheModOutAndSaysWhy()
    {
        Package("Ace-NeedsGhost", "NeedsGhost", "1.0.0", ["Nobody-Ghost-1.0.0"], ("a.json", "{}"));
        Package("Ace-Old", "Old", "1.0.0", [], ("b.json", "{}"));
        Package("Ace-NeedsNewer", "NeedsNewer", "1.0.0", ["Ace-Old-2.0.0"], ("c.json", "{}"));
        Package("Ace-Chicken", "Chicken", "1.0.0", ["Ace-Egg-1.0.0"], ("d.json", "{}"));
        Package("Ace-Egg", "Egg", "1.0.0", ["Ace-Chicken-1.0.0"], ("e.json", "{}"));
        var scan = ContentMods.Scan(ModsDir);
        Assert.Equal(["Ace-Old"], scan.Mods.Select(m => m.Id));
        Assert.Contains(scan.Problems, p => p.Contains("Ace-NeedsGhost") && p.Contains("isn't installed"));
        Assert.Contains(scan.Problems, p => p.Contains("Ace-NeedsNewer") && p.Contains("older"));
        Assert.Contains(scan.Problems, p => p.Contains("Ace-Chicken") && p.Contains("circle"));
    }

    [Fact]
    public void PackMakesAThunderstoreZipOrSaysWhatTheSiteWouldRefuse()
    {
        var good = Package("Ace-HardEdges", "HardEdges", "1.0.0", [], ("tuning/train.json", """{ "cars": 20 }"""));
        var zip = ContentMods.Pack(good, Path.Combine(_root, "out"), out var problems);
        Assert.Empty(problems);
        Assert.Equal("HardEdges-1.0.0.zip", Path.GetFileName(zip));
        using (var archive = System.IO.Compression.ZipFile.OpenRead(zip!))
        {
            var names = archive.Entries.Select(e => e.FullName).ToList();
            Assert.Contains("manifest.json", names);
            Assert.Contains("README.md", names);
            Assert.Contains("icon.png", names);
            Assert.Contains("content/tuning/train.json", names);
        }

        var bad = Package("Ace-Bad", "Bad Name!", "1.0", ["not a dependency"]);
        File.Delete(Path.Combine(bad, "README.md"));
        Png(Path.Combine(bad, "icon.png"), 128, 128);
        Assert.Null(ContentMods.Pack(bad, Path.Combine(_root, "out"), out var refused));
        Assert.Contains(refused, p => p.StartsWith("name"));
        Assert.Contains(refused, p => p.StartsWith("version_number"));
        Assert.Contains(refused, p => p.Contains("Namespace-Name-1.2.3"));
        Assert.Contains(refused, p => p.Contains("README"));
        Assert.Contains(refused, p => p.Contains("256x256"));
        Assert.Contains(refused, p => p.Contains("content"));
    }

    [Fact]
    public void AZipDroppedIntoTheModsFolderIsUnpackedAndLoaded()
    {
        var made = Package("Ace-HardEdges", "HardEdges", "1.0.0", [], ("tuning/train.json", """{ "cars": 20 }"""));
        var zip = ContentMods.Pack(made, Path.Combine(_root, "downloads"), out _)!;
        Directory.Delete(made, recursive: true);
        // As downloaded from the site: Namespace-Name-version.zip.
        File.Copy(zip, Path.Combine(ModsDir, "Ace-HardEdges-1.0.0.zip"));
        var unpacked = Path.Combine(_root, "unpacked");
        ContentMods.Unpack(ModsDir, unpacked);
        var mod = Assert.Single(ContentMods.Find(ModsDir, unpacked));
        Assert.Equal("Ace-HardEdges", mod.Id);
        Assert.Contains("20", File.ReadAllText(Path.Combine(ContentMods.Mount(Content, [mod], Into), "tuning/train.json")));
        // Its zip deleted (uninstalled), the unpacked copy goes too.
        File.Delete(Path.Combine(ModsDir, "Ace-HardEdges-1.0.0.zip"));
        ContentMods.Prune(unpacked, ContentMods.Unpack(ModsDir, unpacked));
        Assert.Empty(ContentMods.Find(ModsDir, unpacked));
    }

    [Fact]
    public void TheExampleModIsAPackageThunderstoreWouldTake()
    {
        var example = Path.Combine(Path.GetDirectoryName(DataFile.FindContentRoot())!, "tools", "mods", "example");
        Assert.Empty(ContentMods.Validate(example));
        // And it patches a file the game has.
        var content = DataFile.FindContentRoot();
        Assert.All(ContentMods.Plan(content, ContentMods.Find(Path.GetDirectoryName(example)!)), f => Assert.Equal(ModChange.Patched, f.Change));
    }
}
