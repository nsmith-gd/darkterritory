using System.Text.Json;
using Ballast;

namespace DarkTerritory.Game;

/// <summary>
/// The player's profile (GDD v1.4 App. D.12 "where it's kept: the player profile, not the character"; note 180): the tally of
/// commendations they've been given, which survives every death and every campaign. Social only. A small text file in the
/// user's app data beside the settings, written whole and moved into place.
/// </summary>
public sealed class PlayerProfile(string path)
{
    public sealed record Data
    {
        /// <summary>Times given each of D.12's starter set, by its name.</summary>
        public Dictionary<string, int> Commendations { get; init; } = [];
    }

    public string Path { get; } = path;

    public static string DefaultPath =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DarkTerritory", "profile.json");

    public Data Load()
    {
        try
        {
            return File.Exists(Path) ? JsonSerializer.Deserialize<Data>(File.ReadAllText(Path), DataFile.Options) ?? new() : new();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return new();
        }
    }

    /// <summary>Adds the night's commendations given to <paramref name="me"/> to the tally, and saves it.</summary>
    public Data Record(IEnumerable<(int From, int To, byte Which)> night, int me)
    {
        var data = Load();
        foreach (var (_, to, which) in night)
            if (to == me && which < Sim.Run.Commendations.StarterSet.Length)
            {
                string name = Sim.Run.Commendations.StarterSet[which];
                data.Commendations[name] = data.Commendations.GetValueOrDefault(name) + 1;
            }
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!);
        var temp = Path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(data, DataFile.Options) + "\n");
        File.Move(temp, Path, overwrite: true);
        return data;
    }
}
