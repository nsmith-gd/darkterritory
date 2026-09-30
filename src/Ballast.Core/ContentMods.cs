using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ballast;

/// <summary>
/// A mod: a folder of content files laid over the base content, described by its <c>mod.json</c> (v1) or by a Thunderstore
/// package's <c>manifest.json</c> (T78: mods ship through Thunderstore).
/// </summary>
/// <param name="Order">Lower goes first among mods neither depends on; a later mod's file wins over an earlier one's.</param>
public sealed record Mod(string Name, string Version, string Description, int Order, bool Enabled, string Directory)
{
    /// <summary>Who it is, for dependencies and matching a host's mods: Thunderstore's <c>Namespace-Name</c>, else its name.</summary>
    public string Id { get; init; } = Name;
    /// <summary>What it needs loaded first, as Thunderstore writes them: <c>Namespace-Name-1.2.3</c>.</summary>
    public IReadOnlyList<string> Dependencies { get; init; } = [];
    /// <summary>Where its content files are: the folder itself (v1), or a package's <c>content/</c>.</summary>
    public string ContentDirectory { get; init; } = Directory;
    public bool Thunderstore { get; init; }
}

/// <summary>What finding the mods turned up: the ones to load, in order, and the ones that can't be, and why.</summary>
public sealed record ModScan(IReadOnlyList<Mod> Mods, IReadOnlyList<string> Problems);

/// <summary>What a mod does to one content file.</summary>
public enum ModChange { Added, Replaced, Patched }

public sealed record ModFile(string Path, string Mod, ModChange Change);

/// <summary>
/// Mods v1 (roadmap M7): folders laid over the base content in order (the base content is itself the first "mod", CLAUDE.md).
/// A mod's file at the same path replaces the base's, a new path adds one, and a JSON file marked <c>"$patch": true</c>
/// is merged into the one below it, key by key, so a mod can change one number without copying the file (arrays are
/// replaced whole). Everything downstream reads one content root as ever: the mounted copy.
/// </summary>
public static class ContentMods
{
    public const string Manifest = "mod.json";
    /// <summary>A Thunderstore package's manifest, and the files beside it that are the package's, not content.</summary>
    public const string PackageManifest = "manifest.json";
    static readonly string[] PackageFiles = [PackageManifest, "README.md", "CHANGELOG.md", "LICENSE", "LICENSE.md", "icon.png"];
    public const string PatchKey = "$patch";
    /// <summary>Written into a mounted copy: which mods it has, in order (name and version), so it describes itself.</summary>
    public const string Mounted = "mounted-mods.json";

    /// <summary>The mods a content root was mounted with ("name version"), or none for the base content.</summary>
    public static IReadOnlyList<string> MountedIn(string content)
    {
        var path = Path.Combine(content, Mounted);
        return File.Exists(path) ? JsonSerializer.Deserialize<string[]>(File.ReadAllText(path)) ?? [] : [];
    }

    /// <summary>The mods in these folders that can be loaded, in load order (<see cref="Scan"/>'s problems left out).</summary>
    public static IReadOnlyList<Mod> Find(params string[] folders) => Scan(folders).Mods;

    /// <summary>
    /// The mods in these folders: each subfolder with a <c>mod.json</c> or a Thunderstore <c>manifest.json</c>, enabled ones
    /// only. In load order: everything a mod depends on before it, and otherwise by <see cref="Mod.Order"/> then id. A mod
    /// missing a dependency (or with one older than it asks for), or in a dependency cycle, isn't loaded, and says why.
    /// </summary>
    public static ModScan Scan(params string[] folders)
    {
        var found = new List<Mod>();
        foreach (var folder in folders.Where(System.IO.Directory.Exists))
            foreach (var dir in System.IO.Directory.EnumerateDirectories(folder).Order(StringComparer.Ordinal))
                if (Read(dir) is { } mod)
                    found.Add(mod);
        var problems = new List<string>();
        var mods = found.Where(m => m.Enabled).GroupBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                // The same package twice (a manager's profile and the mods folder): the newest.
                var pick = g.OrderByDescending(m => Semver(m.Version)).First();
                if (g.Count() > 1)
                    problems.Add($"{pick.Id} is installed {g.Count()} times: using {pick.Version} from {pick.Directory}");
                return pick;
            }).ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
        // Dependencies first: a mod whose dependency can't be loaded can't be either, and so on down.
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var m in mods.Values.ToList())
                foreach (var dep in m.Dependencies)
                {
                    var (id, version) = SplitDependency(dep);
                    string? why = !mods.TryGetValue(id, out var have) ? $"needs {dep}, which isn't installed"
                        : Semver(have.Version).CompareTo(Semver(version)) < 0 ? $"needs {dep}, and {id} {have.Version} is older" : null;
                    if (why is null)
                        continue;
                    problems.Add($"{m.Id} isn't loaded: it {why}");
                    mods.Remove(m.Id);
                    changed = true;
                    break;
                }
        }
        var order = new List<Mod>();
        var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var left = mods.Values.OrderBy(m => m.Order).ThenBy(m => m.Id, StringComparer.Ordinal).ToList();
        while (left.Count > 0)
        {
            var next = left.FirstOrDefault(m => m.Dependencies.All(d => placed.Contains(SplitDependency(d).Id)));
            if (next is null)
            {
                foreach (var m in left)
                    problems.Add($"{m.Id} isn't loaded: its dependencies go round in a circle ({string.Join(", ", m.Dependencies)})");
                break;
            }
            order.Add(next);
            placed.Add(next.Id);
            left.Remove(next);
        }
        return new ModScan(order, problems);
    }

    static Mod? Read(string dir)
    {
        var v1 = Path.Combine(dir, Manifest);
        var package = Path.Combine(dir, PackageManifest);
        if (File.Exists(v1))
        {
            var m = JsonNode.Parse(File.ReadAllText(v1), documentOptions: Jsonc)!;
            string name = (string?)m["name"] ?? Path.GetFileName(dir);
            return new Mod(name, (string?)m["version"] ?? "0", (string?)m["description"] ?? "", (int?)m["order"] ?? 0, (bool?)m["enabled"] ?? true, dir)
            {
                Id = (string?)m["id"] ?? name,
                Dependencies = m["dependencies"] is JsonArray deps ? [.. deps.Select(d => (string)d!)] : [],
            };
        }
        if (!File.Exists(package))
            return null;
        // A Thunderstore package (manifest.json: name, version_number, website_url, description, dependencies). Its
        // namespace isn't in the manifest: the mod managers install a package into a folder named Namespace-Name.
        var t = JsonNode.Parse(File.ReadAllText(package), documentOptions: Jsonc)!;
        string pname = (string?)t["name"] ?? Path.GetFileName(dir);
        string folder = Path.GetFileName(dir);
        string id = folder.EndsWith("-" + pname, StringComparison.Ordinal) && folder.Length > pname.Length + 1 ? folder : pname;
        var content = Path.Combine(dir, "content");
        return new Mod(pname, (string?)t["version_number"] ?? "0.0.0", (string?)t["description"] ?? "", (int?)t["order"] ?? 0, true, dir)
        {
            Id = id,
            Dependencies = t["dependencies"] is JsonArray needs ? [.. needs.Select(d => (string)d!)] : [],
            ContentDirectory = System.IO.Directory.Exists(content) ? content : dir,
            Thunderstore = true,
        };
    }

    /// <summary>
    /// What Thunderstore would refuse in a package folder (its upload rules): the manifest's name (letters, digits and
    /// underscores), a <c>major.minor.patch</c> version, a description of 250 characters at most, a website URL (empty will
    /// do), dependencies as <c>Namespace-Name-1.2.3</c>; a README.md; a 256×256 icon.png. And content to lay over the game.
    /// </summary>
    public static IReadOnlyList<string> Validate(string dir)
    {
        var problems = new List<string>();
        var manifest = Path.Combine(dir, PackageManifest);
        if (!File.Exists(manifest))
            return [$"no {PackageManifest}"];
        JsonNode? m;
        try
        {
            // Thunderstore reads strict JSON: no comments.
            m = JsonNode.Parse(File.ReadAllText(manifest));
        }
        catch (JsonException e)
        {
            return [$"{PackageManifest} isn't JSON: {e.Message}"];
        }
        if ((string?)m?["name"] is not { } name || !System.Text.RegularExpressions.Regex.IsMatch(name, "^[a-zA-Z0-9_]+$"))
            problems.Add("name: letters, digits and underscores only (Thunderstore's rule)");
        if ((string?)m?["version_number"] is not { } version || !System.Text.RegularExpressions.Regex.IsMatch(version, @"^\d+\.\d+\.\d+$"))
            problems.Add("version_number: major.minor.patch, like 1.0.0");
        if (m?["website_url"] is not JsonValue url || url.GetValueKind() != JsonValueKind.String)
            problems.Add("website_url: a URL, or an empty string");
        if ((string?)m?["description"] is not { } description || description.Length > 250)
            problems.Add("description: up to 250 characters");
        if (m?["dependencies"] is not JsonArray deps)
            problems.Add("dependencies: a list (empty if none)");
        else
            foreach (var d in deps)
                if ((string?)d is not { } dep || !System.Text.RegularExpressions.Regex.IsMatch(dep, @"^[a-zA-Z0-9_]+-[a-zA-Z0-9_]+-\d+\.\d+\.\d+$"))
                    problems.Add($"dependency '{d}': Namespace-Name-1.2.3");
        if (!File.Exists(Path.Combine(dir, "README.md")))
            problems.Add("no README.md");
        var icon = Path.Combine(dir, "icon.png");
        if (!File.Exists(icon))
            problems.Add("no icon.png");
        else if (PngSize(icon) is not (256, 256))
            problems.Add($"icon.png is {PngSize(icon)?.ToString() ?? "not a PNG"}: it has to be 256x256");
        if (Read(dir) is { } mod && !ContentFiles(mod).Any())
            problems.Add("nothing in content/ to lay over the game");
        return problems;
    }

    /// <summary>
    /// A package folder zipped for Thunderstore (<c>Name-1.2.3.zip</c> in <paramref name="outDir"/>): the manifest, README,
    /// icon and the rest at the zip's root, as the site wants them. Null, with nothing written, if it wouldn't be accepted.
    /// </summary>
    public static string? Pack(string dir, string outDir, out IReadOnlyList<string> problems)
    {
        problems = Validate(dir);
        if (problems.Count > 0)
            return null;
        var m = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, PackageManifest)))!;
        System.IO.Directory.CreateDirectory(outDir);
        var zip = Path.Combine(outDir, $"{(string)m["name"]!}-{(string)m["version_number"]!}.zip");
        File.Delete(zip);
        using var archive = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create);
        foreach (var rel in Files(dir))
            System.IO.Compression.ZipFileExtensions.CreateEntryFromFile(archive, Path.Combine(dir, rel), rel);
        return zip;
    }

    /// <summary>
    /// Zipped packages dropped straight into a mods folder (as downloaded from Thunderstore, <c>Namespace-Name-1.2.3.zip</c>),
    /// unpacked into <paramref name="into"/> as the managers lay them out (a <c>Namespace-Name</c> folder each), so
    /// <see cref="Scan"/> finds them. Only what's changed is unpacked again. Returns the folders it keeps there.
    /// </summary>
    public static IReadOnlyList<string> Unpack(string folder, string into)
    {
        var kept = new List<string>();
        if (!System.IO.Directory.Exists(folder))
            return kept;
        foreach (var zip in System.IO.Directory.EnumerateFiles(folder, "*.zip").Order(StringComparer.Ordinal))
        {
            var (id, _) = SplitDependency(Path.GetFileNameWithoutExtension(zip));
            var target = Path.Combine(into, id);
            kept.Add(id);
            var stamp = Path.Combine(target, ".from-zip");
            string mark = $"{Path.GetFileName(zip)} {new FileInfo(zip).Length} {File.GetLastWriteTimeUtc(zip).Ticks}";
            if (File.Exists(stamp) && File.ReadAllText(stamp) == mark)
                continue;
            if (System.IO.Directory.Exists(target))
                System.IO.Directory.Delete(target, recursive: true);
            System.IO.Compression.ZipFile.ExtractToDirectory(zip, target);
            File.WriteAllText(stamp, mark);
        }
        return kept;
    }

    /// <summary>Unpacked packages whose zip is gone (uninstalled): removed, so they don't go on loading.</summary>
    public static void Prune(string into, IEnumerable<string> keep)
    {
        if (!System.IO.Directory.Exists(into))
            return;
        var wanted = keep.ToHashSet(StringComparer.Ordinal);
        foreach (var dir in System.IO.Directory.EnumerateDirectories(into).Where(d => !wanted.Contains(Path.GetFileName(d))).ToList())
            System.IO.Directory.Delete(dir, recursive: true);
    }

    static (int Width, int Height)? PngSize(string path)
    {
        var b = File.ReadAllBytes(path);
        if (b.Length < 24 || b[0] != 0x89 || b[1] != (byte)'P' || b[12] != (byte)'I' || b[13] != (byte)'H')
            return null;
        return (System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(16)), System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(20)));
    }

    /// <summary><c>Namespace-Name-1.2.3</c> into its id and version (a bare id wants any version).</summary>
    public static (string Id, string Version) SplitDependency(string dependency)
    {
        int dash = dependency.LastIndexOf('-');
        return dash > 0 && dash + 1 < dependency.Length && char.IsDigit(dependency[dash + 1]) ? (dependency[..dash], dependency[(dash + 1)..]) : (dependency, "0");
    }

    static Version Semver(string v) => System.Version.TryParse(v.Split('-', '+')[0], out var parsed) ? parsed : new Version(0, 0);

    /// <summary>What each mod does, file by file, in the order they're applied.</summary>
    public static IReadOnlyList<ModFile> Plan(string content, IReadOnlyList<Mod> mods)
    {
        var have = Files(content).ToHashSet(StringComparer.Ordinal);
        var plan = new List<ModFile>();
        foreach (var mod in mods)
            foreach (var rel in ContentFiles(mod))
            {
                var change = IsPatch(Path.Combine(mod.ContentDirectory, rel)) ? ModChange.Patched : have.Contains(rel) ? ModChange.Replaced : ModChange.Added;
                if (change == ModChange.Patched && !have.Contains(rel))
                    throw new InvalidDataException($"mod '{mod.Id}' patches {rel}, which nothing below it has");
                plan.Add(new ModFile(rel, mod.Id, change));
                have.Add(rel);
            }
        return plan;
    }

    /// <summary>
    /// The content with the mods laid over it, written to <paramref name="into"/> (only what's changed is rewritten, and
    /// what's no longer there is removed). With no mods, that's the base content itself, untouched.
    /// </summary>
    public static string Mount(string content, IReadOnlyList<Mod> mods, string into)
    {
        if (mods.Count == 0)
            return content;
        var plan = Plan(content, mods);
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var rel in Files(content))
            files[rel] = File.ReadAllBytes(Path.Combine(content, rel));
        files[Mounted] = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(mods.Select(m => $"{m.Id} {m.Version}").ToArray()) + "\n");
        foreach (var step in plan)
        {
            var source = Path.Combine(mods.First(m => m.Id == step.Mod).ContentDirectory, step.Path);
            files[step.Path] = step.Change == ModChange.Patched
                ? System.Text.Encoding.UTF8.GetBytes(Patch(files[step.Path], File.ReadAllBytes(source)))
                : File.ReadAllBytes(source);
        }
        System.IO.Directory.CreateDirectory(into);
        foreach (var (rel, bytes) in files)
        {
            var path = Path.Combine(into, rel);
            if (File.Exists(path) && new FileInfo(path).Length == bytes.Length && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
                continue;
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }
        foreach (var stale in Files(into).Where(r => !files.ContainsKey(r)).ToList())
            File.Delete(Path.Combine(into, stale));
        return into;
    }

    static readonly JsonDocumentOptions Jsonc = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    static bool IsPatch(string path)
    {
        if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return false;
        try
        {
            return JsonNode.Parse(File.ReadAllText(path), documentOptions: Jsonc) is JsonObject o && (bool?)o[PatchKey] == true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>A patch merged into a JSON file: objects key by key, anything else (numbers, strings, arrays) replaced.</summary>
    public static string Patch(byte[] below, byte[] patch)
    {
        var target = JsonNode.Parse(below, documentOptions: Jsonc) as JsonObject ?? throw new InvalidDataException("a patch needs a JSON object below it");
        var over = (JsonObject)JsonNode.Parse(patch, documentOptions: Jsonc)!;
        over.Remove(PatchKey);
        Merge(target, over);
        return target.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    static void Merge(JsonObject target, JsonObject over)
    {
        foreach (var (key, value) in over.ToList())
        {
            if (value is JsonObject child && target[key] is JsonObject existing)
                Merge(existing, child);
            else
                target[key] = value?.DeepClone();
        }
    }

    /// <summary>A mod's content files: all of a v1 folder but its manifest; a package's <c>content/</c>, or its root bar the package's own files.</summary>
    static IEnumerable<string> ContentFiles(Mod mod) =>
        Files(mod.ContentDirectory).Where(r => mod.Thunderstore && mod.ContentDirectory == mod.Directory ? !PackageFiles.Contains(r) : r != Manifest);

    static IEnumerable<string> Files(string root) =>
        System.IO.Directory.Exists(root)
            ? System.IO.Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/')).Order(StringComparer.Ordinal)
            : [];
}
