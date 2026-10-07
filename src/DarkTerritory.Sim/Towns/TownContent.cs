using Ballast;
using DarkTerritory.Sim.LineGen;

namespace DarkTerritory.Sim.Towns;

/// <summary>Mirror of content/tuning/towns.json (GDD §3.1; ARCHITECTURE §8 note 278). Field docs live in that file.</summary>
public sealed record TownTuning
{
    public const string File = "tuning/towns.json";

    public bool Enabled { get; init; } = true;
    public required SquareTuning Square { get; init; }
    public required int[] Ring { get; init; }
    public int Street { get; init; } = 4;
    public required int[] LinesPerPerson { get; init; }
    public double ScrapShare { get; init; }
    public required int[] Quirks { get; init; }
    public required int[] Notices { get; init; }
    public required int[] LooseNotes { get; init; }
    public required TownReach Reach { get; init; }
    public double TypePerSecond { get; init; } = 45;
    public double LingerSeconds { get; init; } = 6;
}

public sealed record SquareTuning
{
    public int Side { get; init; } = -1;
    public required double[] FromGate { get; init; }
    public double WallOut { get; init; }
    public double BuildingDepth { get; init; }
    public double HallWidth { get; init; }
    public double OfficeWidth { get; init; }
}

public sealed record TownReach
{
    public double Talk { get; init; }
    public double Read { get; init; }
    public double LookDegrees { get; init; }
    public double CloseBeyond { get; init; }
}

/// <summary>Mirror of content/world/towns.json: the towns' words. Read by <see cref="TownGenerator"/>.</summary>
public sealed record TownWriting
{
    public const string File = "world/towns.json";

    public required string[] FirstNames { get; init; }
    public required string[] Founded { get; init; }
    /// <summary>What the gatekeeper says first: the town's law, as you come in.</summary>
    public string[] Welcome { get; init; } = [];
    public required TownCulture[] Cultures { get; init; }
    public required TownQuirk[] Quirks { get; init; }
    public required Dictionary<string, TownIndustry> Industries { get; init; }
    public required Dictionary<string, TownRole> Roles { get; init; }
    public required TownThread[] Threads { get; init; }
    public required TownText[] Notices { get; init; }
    /// <summary>What answers a knock at the square's three doors, by building ("hall", "office", "store").</summary>
    public Dictionary<string, string[]> Doors { get; init; } = [];
}

/// <summary>A paper: a notice, a letter, a page.</summary>
public sealed record TownText(string Title, string Text);

/// <summary>
/// A town's custom (GDD §3.1): the human answer to one creature's rule, kept harder than it needs keeping. <see
/// cref="Creature"/> is the creature by enemies.json's "costs" names, so an edition only gets the customs of what it fields.
/// </summary>
public sealed record TownCulture
{
    public required string Id { get; init; }
    public required string Creature { get; init; }
    public required string Law { get; init; }
    public required string Hall { get; init; }
    public required TownCentrepiece Centrepiece { get; init; }
    public required string[] Lines { get; init; }
    public required TownText[] Notes { get; init; }
}

public sealed record TownCentrepiece(string Kind, string Name, string Text);

public sealed record TownQuirk
{
    public required string Id { get; init; }
    public string Habit { get; init; } = "";
    public required string[] Lines { get; init; }
    public TownText[] Notes { get; init; } = [];
}

public sealed record TownIndustry
{
    public required string Name { get; init; }
    /// <summary>What one of the town's working people is called ("pit hand").</summary>
    public string Hand { get; init; } = "townsman";
    /// <summary>What's left on the market's stalls at night, looked at.</summary>
    public string[] Stalls { get; init; } = [];
    public required string[] Lines { get; init; }
    public TownText[] Notes { get; init; } = [];
}

public sealed record TownRole(string Title, string[] Lines);

public sealed record TownThread
{
    public required string Id { get; init; }
    public required string[] Lines { get; init; }
    public TownText[] Notes { get; init; } = [];
}

/// <summary>Everything a town is made from: its numbers, its words, and linegen's surnames for its people.</summary>
public sealed record TownContent(TownTuning Tuning, TownWriting Writing, IReadOnlyList<string> Surnames)
{
    /// <summary>The content's towns, or null where it has none (a mod that leaves them out, an old content folder).</summary>
    public static TownContent? Load(string content)
    {
        string tuning = Path.Combine(content, TownTuning.File), writing = Path.Combine(content, TownWriting.File);
        if (!System.IO.File.Exists(tuning) || !System.IO.File.Exists(writing))
            return null;
        var names = DataFile.Load<NamesFile>(Path.Combine(content, LineGenConfig.Directory, "names.json"));
        return new TownContent(DataFile.Load<TownTuning>(tuning), DataFile.Load<TownWriting>(writing), names.Surnames);
    }
}
