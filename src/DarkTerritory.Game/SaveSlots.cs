using System.Text.Json;
using Ballast;
using DarkTerritory.Sim.Campaign;

namespace DarkTerritory.Game;

/// <summary>
/// Spec E: "3 per host. Slot must be selected when hosting." One text file a slot (JSON, readable, diffable), written
/// whole to a temporary file and moved into place, so a crash mid-write leaves the last good save.
/// </summary>
public sealed class SaveSlots(string directory, int slots = 3)
{
    public string Directory { get; } = directory;
    public int Count { get; } = slots;

    /// <summary>Where saves live when nobody says otherwise: the user's local app data.</summary>
    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DarkTerritory", "saves");

    public string PathOf(int slot)
    {
        if (slot < 1 || slot > Count)
            throw new ArgumentOutOfRangeException(nameof(slot), $"slots are 1 to {Count}");
        return Path.Combine(Directory, $"slot{slot}.json");
    }

    public CampaignState? Load(int slot)
    {
        var path = PathOf(slot);
        return File.Exists(path) ? JsonSerializer.Deserialize<CampaignState>(File.ReadAllText(path), DataFile.Options) : null;
    }

    public void Save(CampaignState state)
    {
        var path = PathOf(state.Slot);
        System.IO.Directory.CreateDirectory(Directory);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, DataFile.Options) + "\n");
        File.Move(temp, path, overwrite: true);
    }

    public void Delete(int slot) => File.Delete(PathOf(slot));

    public IReadOnlyList<(int Slot, CampaignState? State)> List() => [.. Enumerable.Range(1, Count).Select(i => (i, Load(i)))];
}
