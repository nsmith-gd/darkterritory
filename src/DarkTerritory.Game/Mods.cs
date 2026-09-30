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

    /// <summary>Takes <c>--mods-dir &lt;folder&gt;</c> out of the arguments (into <see cref="ManagerFolder"/>) and returns the rest.</summary>
    public static string[] TakeArgs(string[] args)
    {
        int at = Array.IndexOf(args, "--mods-dir");
        if (at < 0 || at + 1 >= args.Length)
            return args;
        ManagerFolder = args[at + 1];
        return [.. args.Take(at), .. args.Skip(at + 2)];
    }

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
        var scan = enabled ? ContentMods.Scan(Folders(content)) : new ModScan([], []);
        foreach (var problem in scan.Problems)
            Console.Error.WriteLine($"mods: {problem}");
        return ContentMods.Mount(content, scan.Mods, into ?? Path.Combine(AppData, "content-with-mods"));
    }
}
