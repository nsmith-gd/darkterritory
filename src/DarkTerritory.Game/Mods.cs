using Ballast;

namespace DarkTerritory.Game;

/// <summary>
/// Where the game looks for mods (T49, roadmap M7 "mod loader v1"): a <c>mods</c> folder beside the content (the repo's,
/// or a shipped build's beside the executable) and one in the user's app data, next to the saves. Every enabled mod is
/// laid over the content in order (<see cref="ContentMods"/>) into a copy in the app data, and the game runs on that.
/// <para>
/// Mods ship through Thunderstore (T78). A mod manager (r2modman, Thunderstore Mod Manager) keeps each profile's packages
/// in a folder of its own and launches the game with <c>--mods-dir &lt;that folder&gt;</c> (or sets
/// <c>DARKTERRITORY_MODS</c>); a package zip downloaded from the site can also just be dropped into a mods folder.
/// </para>
/// </summary>
public static class Mods
{
    static string AppData => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DarkTerritory");

    public static string UserFolder => Path.Combine(AppData, "mods");
    /// <summary>Where zipped packages found in the mods folders are unpacked to be read.</summary>
    public static string Unpacked => Path.Combine(AppData, "mods-unpacked");
    /// <summary>The environment variable a mod manager can set instead of passing <c>--mods-dir</c>.</summary>
    public const string ManagerVariable = "DARKTERRITORY_MODS";
    /// <summary>A mod manager's profile folder (<c>--mods-dir</c>), set from the command line.</summary>
    public static string? ManagerFolder { get; set; }

    /// <summary>An edition to lay over the content before any mod (<c>--edition demo</c>, T79): a folder in <c>editions/</c>.</summary>
    public static string? Edition { get; set; }

    /// <summary>
    /// Takes <c>--mods-dir &lt;folder&gt;</c> (into <see cref="ManagerFolder"/>) and <c>--edition &lt;name&gt;</c> (into
    /// <see cref="Edition"/>) out of the arguments and returns the rest.
    /// </summary>
    public static string[] TakeArgs(string[] args)
    {
        args = Take(args, "--mods-dir", v => ManagerFolder = v);
        return Take(args, "--edition", v => Edition = v);
    }

    static string[] Take(string[] args, string flag, Action<string> set)
    {
        int at = Array.IndexOf(args, flag);
        if (at < 0 || at + 1 >= args.Length)
            return args;
        set(args[at + 1]);
        return [.. args.Take(at), .. args.Skip(at + 2)];
    }

    /// <summary>The editions beside the content (the repo's <c>editions/</c>; a shipped build has its own baked in).</summary>
    public static string EditionsFolder(string content) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(content))!, "editions");

    /// <summary>The named edition's overlay, as a mod that loads before every other.</summary>
    public static Mod EditionMod(string content, string name)
    {
        var dir = Path.Combine(EditionsFolder(content), name);
        return ContentMods.Scan(EditionsFolder(content)).Mods.FirstOrDefault(m => Path.GetFullPath(m.Directory) == Path.GetFullPath(dir))
            ?? throw new DirectoryNotFoundException($"no edition \"{name}\" in {EditionsFolder(content)}");
    }

    /// <summary>The base content with an edition baked in, written to <paramref name="into"/> (<c>dt edition bake</c>).</summary>
    public static string Bake(string content, string edition, string into) => ContentMods.Mount(content, [EditionMod(content, edition)], into);

    /// <summary>The mods <see cref="Mount"/> found installed (laid over or not), in load order, and what couldn't load.</summary>
    public static ModScan Installed { get; private set; } = new([], []);

    /// <summary><see cref="Mount"/> was told <c>--no-mods</c>: whatever's installed, the base game is playing.</summary>
    public static bool Off { get; private set; }

    public static string[] Folders(string content)
    {
        var folders = new List<string> { Path.Combine(Path.GetDirectoryName(Path.GetFullPath(content))!, "mods"), UserFolder };
        if ((ManagerFolder ?? Environment.GetEnvironmentVariable(ManagerVariable)) is { Length: > 0 } manager)
            folders.Add(manager);
        // Zips dropped into any of them, unpacked as the managers would lay them out; and none left from a zip that's gone.
        ContentMods.Prune(Unpacked, [.. folders.ToList().SelectMany(f => ContentMods.Unpack(f, Unpacked))]);
        folders.Add(Unpacked);
        return [.. folders];
    }

    /// <summary>
    /// The content to run on: the base content, or, with mods found, the mounted copy. <paramref name="enabled"/> false
    /// (<c>--no-mods</c>) plays the base game whatever's installed. What can't be loaded (a missing dependency) is said on
    /// the console and left out; the rest still load.
    /// </summary>
    public static string Mount(string content, bool enabled = true, string? into = null)
    {
        // What's installed is scanned either way, for the MODS screen (note 323); with --no-mods none of it is laid over.
        Installed = ContentMods.Scan(Folders(content));
        Off = !enabled;
        var scan = enabled ? Installed : new ModScan([], []);
        foreach (var problem in scan.Problems)
            Console.Error.WriteLine($"mods: {problem}");
        IReadOnlyList<Mod> mods = Edition is { } edition ? [EditionMod(content, edition), .. scan.Mods] : scan.Mods;
        return ContentMods.Mount(content, mods, into ?? Path.Combine(AppData, Edition is null ? "content-with-mods" : $"content-{Edition}"));
    }
}
