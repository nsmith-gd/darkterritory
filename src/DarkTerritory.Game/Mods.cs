using Ballast;

namespace DarkTerritory.Game;

/// <summary>
/// Where the game looks for mods (T49, roadmap M7 "mod loader v1"): a <c>mods</c> folder beside the content (the repo's,
/// or a shipped build's beside the executable) and one in the user's app data, next to the saves. Every enabled mod is
/// laid over the content in order (<see cref="ContentMods"/>) into a copy in the app data, and the game runs on that.
/// </summary>
public static class Mods
{
    static string AppData => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DarkTerritory");

    public static string UserFolder => Path.Combine(AppData, "mods");

    public static string[] Folders(string content) => [Path.Combine(Path.GetDirectoryName(Path.GetFullPath(content))!, "mods"), UserFolder];

    /// <summary>
    /// The content to run on: the base content, or, with mods found, the mounted copy. <paramref name="enabled"/> false
    /// (<c>--no-mods</c>) plays the base game whatever's installed.
    /// </summary>
    public static string Mount(string content, bool enabled = true, string? into = null)
    {
        var mods = enabled ? ContentMods.Find(Folders(content)) : [];
        return ContentMods.Mount(content, mods, into ?? Path.Combine(AppData, "content-with-mods"));
    }
}
