using System.Text.Json;
using Ballast;
using DarkTerritory.Sim.Music;

namespace DarkTerritory.Game;

/// <summary>
/// The derailment's shuffle bag for quick nights (GDD v1.4 App. E.6 "Rotation"; ARCHITECTURE §8 note 174): a campaign
/// keeps its bag in its save slot, and a night hosted from the menu without one keeps this, a small text file in the
/// user's app data beside the settings, so the no-repeat rules hold across nights there too. Written whole and moved into
/// place, as the save slots are.
/// </summary>
public sealed class MusicBagFile(string path)
{
    public string Path { get; } = path;

    public static string DefaultPath =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DarkTerritory", "music-bag.json");

    /// <summary>The bag as last saved; null if there's none yet, or it can't be read (a fresh bag, then).</summary>
    public MusicBag? Load()
    {
        try
        {
            return File.Exists(Path) ? JsonSerializer.Deserialize<MusicBag>(File.ReadAllText(Path), DataFile.Options) : null;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save(MusicBag bag)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!);
        var temp = Path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(bag, DataFile.Options) + "\n");
        File.Move(temp, Path, overwrite: true);
    }
}
