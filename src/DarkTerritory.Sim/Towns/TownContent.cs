using Ballast;
using DarkTerritory.Sim.LineGen;

namespace DarkTerritory.Sim.Towns;

/// <summary>Mirror of content/tuning/towns.json (GDD §3.1; ARCHITECTURE §8 note 281). Field docs live in that file.</summary>
public sealed record TownTuning
{
    public const string File = "tuning/towns.json";

    public bool Enabled { get; init; } = true;
    public required int[] Population { get; init; }
    public required int[] Household { get; init; }
    public required double[] Former { get; init; }
    public double Outdoors { get; init; } = 22;
    public required ExplorableTuning Explorable { get; init; }
    public required StreetTuning Houses { get; init; }
    public required SquareTuning Square { get; init; }
    public required int[] Ring { get; init; }
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

public sealed record ExplorableTuning
{
    public double Per { get; init; } = 70;
    public int Min { get; init; } = 2;
    public int Max { get; init; } = 5;
}

public sealed record StreetTuning
{
    public double Every { get; init; } = 15;
    public double Out { get; init; } = 10.9;
    public required double[] Width { get; init; }
    public required double[] Depth { get; init; }
    public double FromGate { get; init; } = 30;
    public double Burnt { get; init; }
    public double Open { get; init; }
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
    /// <summary>The townspeople's surnames (the province's: Gaelic, Acadian, Irish, Lunenburg); linegen's when unset.</summary>
    public string[] Surnames { get; init; } = [];
    /// <summary>Other towns, for {other}.</summary>
    public string[] Places { get; init; } = [];
    public required string[] Founded { get; init; }
    /// <summary>The plaque at the way in: the town, when it was walled, its people now and before.</summary>
    public string[] Plaque { get; init; } = [];
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
    /// <summary>Households behind an open door: what happened to them, what each says, and the thing that tells it.</summary>
    public TownHousehold[] Households { get; init; } = [];
    /// <summary>A knock at a shut house.</summary>
    public string[] HouseKnocks { get; init; } = [];
    /// <summary>A house nobody lives in now, looked at, by how it's left ("boarded", "burnt", "open").</summary>
    public Dictionary<string, string[]> EmptyHouses { get; init; } = [];
    /// <summary>What else there is to look at in an open house, by thing ("stairs", "stove", "photo").</summary>
    public Dictionary<string, string[]> Rooms { get; init; } = [];
}

/// <summary>
/// A household behind an open door (note 281): whether it lost somebody (<see cref="Absent"/>), its members' lines by
/// part ("elder", "parent", "child", "lodger"), and the thing in the house that tells it.
/// </summary>
public sealed record TownHousehold
{
    public required string Id { get; init; }
    public bool Absent { get; init; }
    public required Dictionary<string, string[]> Lines { get; init; }
    public required TownCentrepiece Object { get; init; }
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
    /// <summary>What the custom's building is: "church", "school", "shed" or a plain "hall".</summary>
    public string HallStyle { get; init; } = "hall";
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
        var words = DataFile.Load<TownWriting>(writing);
        // Its own people's surnames (the province's); else the line's, as the places are named.
        IReadOnlyList<string> surnames = words.Surnames.Length > 0 ? words.Surnames
            : DataFile.Load<NamesFile>(Path.Combine(content, LineGenConfig.Directory, "names.json")).Surnames;
        return new TownContent(DataFile.Load<TownTuning>(tuning), words, surnames);
    }
}
