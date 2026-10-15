using DarkTerritory.Sim.Campaign;

namespace DarkTerritory.Dev.Smithy;

/// <summary>
/// The blacksmith's unlock (ARCHITECTURE §8 note 747; the director, 14 Oct 2026: "add a dev unlock mode where I can just
/// infinitely buy whatever I want. From the Smith."): in a developer build, a row at the top of the catalogue turns it on, and
/// then every upgrade is free, whatever the tier, and as often as you like: one owned is taken off again, and one that won't
/// go with something fitted takes that off for it. The scrip is never touched. The refit hosts the yard again as for a sale.
/// </summary>
public sealed class DevUnlock
{
    /// <summary>On, the shelves are free (the row at the catalogue's top toggles it).</summary>
    public bool On { get; set; }

    /// <summary>The catalogue's top row: what it says, and the toggle.</summary>
    public (Game.MenuItem Item, Action Choose) Row() =>
        (new Game.MenuItem(On ? "DEV UNLOCK: ON" : "DEV UNLOCK: OFF",
            On ? "Everything free, any tier, as often as you like; choose an owned one to take it off." : "A developer build's: buy anything, free."),
         () => On = !On);

    /// <summary>
    /// The upgrade <paramref name="id"/> fitted free (or, owned, taken off), with anything it won't go with taken off for it.
    /// </summary>
    public static Purchase Buy(CampaignTuning t, CampaignState s, string id)
    {
        if (t.Upgrades.FirstOrDefault(u => u.Id == id) is not { } u)
            return new(s, $"no upgrade called '{id}'");
        if (s.Upgrades.Contains(id))
            return new(s with { Upgrades = [.. s.Upgrades.Where(x => x != id)] }, null);
        var kept = s.Upgrades.Where(other => t.Upgrades.FirstOrDefault(o => o.Id == other) is not { } o || !u.Excludes.Contains(other) && !o.Excludes.Contains(id));
        return new(s with { Upgrades = [.. kept, id] }, null);
    }
}
