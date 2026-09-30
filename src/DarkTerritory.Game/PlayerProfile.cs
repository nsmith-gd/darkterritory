using System.Text.Json;
using Ballast;

namespace DarkTerritory.Game;

/// <summary>
/// The player's profile (GDD App. D.12): who they are across sessions and hosts, and the commendations the crews they've
/// run with have given them. One small text file in the user's app data, beside the settings. The character a player is
/// (App. D.8) isn't here: it's the host's campaign's, kept against this profile's id.
/// </summary>
public sealed record PlayerProfile
{
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DarkTerritory", "profile.json");

    /// <summary>A stable id for this player, made once (a host's campaign keeps their character against it).</summary>
    public string Id { get; init; } = "";
    /// <summary>D.12: every commendation this player has been given, by award. Social only.</summary>
    public IReadOnlyDictionary<string, int> Commendations { get; init; } = new Dictionary<string, int>();

    /// <summary>The file's profile, made (with a fresh id) and saved if there isn't one.</summary>
    public static PlayerProfile Load(string path)
    {
        PlayerProfile? p = null;
        try
        {
            if (File.Exists(path))
                p = JsonSerializer.Deserialize<PlayerProfile>(File.ReadAllText(path), DataFile.Options);
        }
        catch (JsonException)
        {
        }
        if (p is { Id.Length: > 0 })
            return p;
        p = (p ?? new PlayerProfile()) with { Id = Guid.NewGuid().ToString("N") };
        p.Save(path);
        return p;
    }

    /// <summary>A commendation received at a run's end (D.12), tallied.</summary>
    public PlayerProfile Commended(string award)
    {
        var tally = new SortedDictionary<string, int>(Commendations.ToDictionary(), StringComparer.Ordinal);
        tally[award] = tally.GetValueOrDefault(award) + 1;
        return this with { Commendations = tally };
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, DataFile.Options) + "\n");
        File.Move(temp, path, overwrite: true);
    }
}
