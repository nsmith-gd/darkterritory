using Ballast;

namespace DarkTerritory.Game;

/// <summary>
/// Which game this build is (T79, roadmap M6 "Steam demo build"): the full game, or the December demo, which GDD §21 and
/// §35 scope to the demo roster of five, one tier and two facilities. The demo is an overlay on the base content
/// (<c>editions/demo</c>, laid over like a mod): its <c>enemies.json</c> roster, its line generator's tier table, and this
/// file. <c>tools/package.sh --demo</c> bakes it into the demo's content; <c>--edition demo</c> plays it from the repo.
/// </summary>
public sealed record EditionTuning
{
    public const string File = "tuning/edition.json";

    /// <summary>Its name: "full", "demo".</summary>
    public string Name { get; init; } = "full";
    public bool Demo { get; init; }
    /// <summary>The tiers a quick night can be on (their camel-cased names); empty is all of them.</summary>
    public string[] Tiers { get; init; } = [];
    /// <summary>Whether the campaign (slots, the fortress, contracts) is in it.</summary>
    public bool Campaign { get; init; } = true;
    /// <summary>The longest train a quick night can take (0: the campaign's most).</summary>
    public int MaxCars { get; init; }
    /// <summary>Under the title ("DEMO"), and said once a night's over; empty for nothing.</summary>
    public string Tag { get; init; } = "";
    public string AfterNight { get; init; } = "";
    /// <summary>
    /// Note 434: the full game's Steam app, whose store page WISHLIST ON STEAM opens from the demo's title; 0 (no store page
    /// yet) hides it.
    /// </summary>
    public uint StoreAppId { get; init; }

    /// <summary>The store page's address, for a browser when the Steam overlay can't show it; null with no app.</summary>
    public string? StoreUrl => StoreAppId > 0 ? $"https://store.steampowered.com/app/{StoreAppId}/" : null;

    public static EditionTuning Load(string content)
    {
        var path = Path.Combine(content, File);
        return System.IO.File.Exists(path) ? DataFile.Load<EditionTuning>(path) : new();
    }

    public bool HasTier(DarkTerritory.Sim.Route.RouteTier tier) =>
        Tiers.Length == 0 || Tiers.Contains(char.ToLowerInvariant(tier.ToString()[0]) + tier.ToString()[1..]);
}
