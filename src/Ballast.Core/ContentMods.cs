using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ballast;

/// <summary>A mod: a folder of content files laid over the base content, described by its <c>mod.json</c>.</summary>
/// <param name="Order">Lower goes first; a later mod's file wins over an earlier one's.</param>
public sealed record Mod(string Name, string Version, string Description, int Order, bool Enabled, string Directory);

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
    public const string PatchKey = "$patch";
    /// <summary>Written into a mounted copy: which mods it has, in order (name and version), so it describes itself.</summary>
    public const string Mounted = "mounted-mods.json";

    /// <summary>The mods a content root was mounted with ("name version"), or none for the base content.</summary>
    public static IReadOnlyList<string> MountedIn(string content)
    {
        var path = Path.Combine(content, Mounted);
        return File.Exists(path) ? JsonSerializer.Deserialize<string[]>(File.ReadAllText(path)) ?? [] : [];
    }

    /// <summary>The mods in these folders (each subfolder with a <c>mod.json</c>), enabled ones only, in load order.</summary>
    public static IReadOnlyList<Mod> Find(params string[] folders)
    {
        var mods = new List<Mod>();
        foreach (var folder in folders.Where(System.IO.Directory.Exists))
            foreach (var dir in System.IO.Directory.EnumerateDirectories(folder).Order(StringComparer.Ordinal))
            {
                var manifest = Path.Combine(dir, Manifest);
                if (!File.Exists(manifest))
                    continue;
                var m = JsonNode.Parse(File.ReadAllText(manifest), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!;
                mods.Add(new Mod(
                    (string?)m["name"] ?? Path.GetFileName(dir),
                    (string?)m["version"] ?? "0",
                    (string?)m["description"] ?? "",
                    (int?)m["order"] ?? 0,
                    (bool?)m["enabled"] ?? true,
                    dir));
            }
        return [.. mods.Where(m => m.Enabled).OrderBy(m => m.Order).ThenBy(m => m.Name, StringComparer.Ordinal)];
    }

    /// <summary>What each mod does, file by file, in the order they're applied.</summary>
    public static IReadOnlyList<ModFile> Plan(string content, IReadOnlyList<Mod> mods)
    {
        var have = Files(content).ToHashSet(StringComparer.Ordinal);
        var plan = new List<ModFile>();
        foreach (var mod in mods)
            foreach (var rel in Files(mod.Directory).Where(r => r != Manifest))
            {
                var change = IsPatch(Path.Combine(mod.Directory, rel)) ? ModChange.Patched : have.Contains(rel) ? ModChange.Replaced : ModChange.Added;
                if (change == ModChange.Patched && !have.Contains(rel))
                    throw new InvalidDataException($"mod '{mod.Name}' patches {rel}, which nothing below it has");
                plan.Add(new ModFile(rel, mod.Name, change));
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
        files[Mounted] = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(mods.Select(m => $"{m.Name} {m.Version}").ToArray()) + "\n");
        foreach (var step in plan)
        {
            var source = Path.Combine(mods.First(m => m.Name == step.Mod).Directory, step.Path);
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

    static IEnumerable<string> Files(string root) =>
        System.IO.Directory.Exists(root)
            ? System.IO.Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/')).Order(StringComparer.Ordinal)
            : [];
}
