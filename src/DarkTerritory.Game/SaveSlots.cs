using System.Text.Json;
using Ballast;
using DarkTerritory.Sim.Campaign;

namespace DarkTerritory.Game;

/// <summary>
/// Spec E: "3 per host. Slot must be selected when hosting." One text file a slot (JSON, readable, diffable), written
/// whole to a temporary file and moved into place, so a crash mid-write leaves the last good save.
/// <para>And the crews this player has ridden with (GDD §11; ARCHITECTURE §8 note 724): each crew's save as its host last sent
/// it, one file a crew under <c>crews/</c>, never in a slot. A crew's copy is <see cref="CampaignState.Slot"/> 0, and hosting
/// it saves back there.</para>
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

    /// <summary>A slot's save, or (with <paramref name="crew"/>) a crew's ridden with (note 724).</summary>
    public CampaignState? Load(int slot, string? crew) => crew is null ? Load(slot) : LoadCrew(crew);

    /// <summary>Into its slot; a crew's copy (slot 0, note 724) into the crews ridden with.</summary>
    public void Save(CampaignState state) => Write(state.Slot == 0 ? CrewPath(state.CrewId) : PathOf(state.Slot), state);

    static void Write(string path, CampaignState state)
    {
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, DataFile.Options) + "\n");
        File.Move(temp, path, overwrite: true);
    }

    public void Delete(int slot) => File.Delete(PathOf(slot));

    public IReadOnlyList<(int Slot, CampaignState? State)> List() => [.. Enumerable.Range(1, Count).Select(i => (i, Load(i)))];

    // ---- The crews ridden with (note 724) ----

    /// <summary>Where the crews ridden with are kept: beside the slots.</summary>
    public string CrewsDirectory => Path.Combine(Directory, "crews");

    /// <summary>A crew's file: its id, kept to the characters a file name can always have.</summary>
    public string CrewPath(string crew)
    {
        var safe = new string([.. crew.ToLowerInvariant().Where(c => c is >= '0' and <= '9' or >= 'a' and <= 'z' or '-')]);
        return Path.Combine(CrewsDirectory, (safe.Length > 0 ? safe : "crew") + ".json");
    }

    public CampaignState? LoadCrew(string crew)
    {
        var path = CrewPath(crew);
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<CampaignState>(File.ReadAllText(path), DataFile.Options) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Every crew ridden with, the one last settled first.</summary>
    public IReadOnlyList<CampaignState> Crews()
    {
        if (!System.IO.Directory.Exists(CrewsDirectory))
            return [];
        var all = new List<CampaignState>();
        foreach (var file in System.IO.Directory.GetFiles(CrewsDirectory, "*.json").Order(StringComparer.Ordinal))
        {
            try
            {
                if (JsonSerializer.Deserialize<CampaignState>(File.ReadAllText(file), DataFile.Options) is { } s)
                    all.Add(s);
            }
            catch (JsonException)
            {
                // A file that doesn't read is left alone, and not listed.
            }
        }
        return [.. all.OrderByDescending(s => s.SettledAt).ThenBy(s => s.CrewId, StringComparer.Ordinal)];
    }

    /// <summary>
    /// The crew's save as a host sent it (note 724): kept under the crew's id, never in one of this player's slots. Of two
    /// copies of a crew, the one with more nights settled stays (<see cref="Campaign.Prefer"/>). What's kept, or null for bytes
    /// that aren't a save.
    /// </summary>
    public CampaignState? Keep(byte[] save)
    {
        CampaignState? incoming;
        try
        {
            incoming = JsonSerializer.Deserialize<CampaignState>(save, DataFile.Options);
        }
        catch (JsonException)
        {
            return null;
        }
        return incoming is null ? null : Keep(incoming);
    }

    /// <summary>A crew's save kept among the crews ridden with (note 724), the better of it and the copy held.</summary>
    public CampaignState Keep(CampaignState incoming)
    {
        incoming = incoming with { Slot = 0, Crew = incoming.CrewId };
        var kept = LoadCrew(incoming.CrewId) is { } held ? Campaign.Prefer(held, incoming) : incoming;
        Save(kept);
        return kept;
    }

    /// <summary>The crew's save as it goes to the crew (note 724): its JSON, as this machine writes it.</summary>
    public static byte[] Bytes(CampaignState state) => JsonSerializer.SerializeToUtf8Bytes(state, DataFile.Options);
}
