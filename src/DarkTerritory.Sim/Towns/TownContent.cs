using Ballast;
using DarkTerritory.Sim.LineGen;

namespace DarkTerritory.Sim.Towns;

/// <summary>Mirror of content/tuning/towns.json (GDD §3.1; ARCHITECTURE §8 note 281). Field docs live in that file.</summary>
public sealed record TownTuning
{
    public const string File = "tuning/towns.json";

    public bool Enabled { get; init; } = true;
    public required int[] Population { get; init; }
    /// <summary>How the population is drawn across its range: 1 evenly, 2 or 3 for more small towns than big (u to that power).</summary>
    public int PopulationPower { get; init; } = 1;
    public required int[] Household { get; init; }
    public required double[] Former { get; init; }
    public double Outdoors { get; init; } = 22;
    /// <summary>The most people out of doors (a big town's street folk), so a town of thousands isn't a crowd.</summary>
    public int OutdoorsMax { get; init; } = 1000;
    /// <summary>The most houses of the lost, as a share of the households (a big town that lost two in three leaves the rest as empty lots).</summary>
    public double LostShare { get; init; } = 100;
    /// <summary>A walled town's streets (queue #74): see <see cref="WalledTuning"/>.</summary>
    public WalledTuning Walled { get; init; } = new();
    public required ExplorableTuning Explorable { get; init; }
    public required StreetTuning Houses { get; init; }
    public required SquareTuning Square { get; init; }
    public required int[] Ring { get; init; }
    public required int[] LinesPerPerson { get; init; }
    public double ScrapShare { get; init; }
    /// <summary>The share of walled towns with one of Dave's murals on a wall (note 487).</summary>
    public double DaveMural { get; init; }
    public required int[] Quirks { get; init; }
    public required int[] Notices { get; init; }
    public required int[] LooseNotes { get; init; }
    public required TownReach Reach { get; init; }
    public double TypePerSecond { get; init; } = 45;
    public double LingerSeconds { get; init; } = 6;
    /// <summary>Their rounds (note 353).</summary>
    public RoundTuning Rounds { get; init; } = new();
}

/// <summary>
/// Townspeople's rounds (towns.json <c>rounds</c>; note 353): out of doors a day of <see cref="Slots"/> slots of
/// <see cref="Slot"/> seconds, at home <see cref="HomeSlot"/> seconds at each of the rooms' places, walked between at
/// <see cref="Walk"/>; a pacer goes <see cref="Pace"/> either way of their post, a lamp-carrier on a street this far along it.
/// </summary>
public sealed record RoundTuning
{
    public double Slot { get; init; } = 45;
    public int Slots { get; init; } = 4;
    public double HomeSlot { get; init; } = 38;
    public double Walk { get; init; } = 1.2;
    public double Pace { get; init; } = 3.5;
    public double[] Street { get; init; } = [10, 24];
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
    /// <summary>A gable-front house's frontage (narrower: its gable end is to the street).</summary>
    public double[] GableWidth { get; init; } = [5.4, 7.0];
    public required double[] Depth { get; init; }
    public double FromGate { get; init; } = 30;
    public double Burnt { get; init; }
    public double Open { get; init; }
}

/// <summary>
/// A walled town's ground plan (the director, 7 Oct 2026: "fortresses aren't just some straight line around the railroad,
/// they should surround towns, towns should be explorable"; queue #74, ARCHITECTURE §8 note 335). Past the houses on
/// the line's own street, more streets run beside the line, each with a row of houses either side, as many as the town
/// needs; lanes cross between blocks; the wall goes round the lot.
/// </summary>
public sealed record WalledTuning
{
    /// <summary>The first street's middle from the line, and from one street's middle to the next (m).</summary>
    public double First { get; init; } = 34.5;
    public double Every { get; init; } = 30;
    /// <summary>A street's width, and how far a house's front stands back from its edge (m).</summary>
    public double Width { get; init; } = 6;
    public double Setback { get; init; } = 3.5;
    /// <summary>A lot's frontage along a street, between these (m).</summary>
    public double[] Lot { get; init; } = [13, 17];
    /// <summary>A lane across the streets every so often (m), this wide.</summary>
    public double[] LaneEvery { get; init; } = [90, 130];
    public double LaneWidth { get; init; } = 8;
    /// <summary>Where the street rows run along the line: from this far up from the yard's start to this far inside the gate (m).</summary>
    public double From { get; init; } = 22;
    public double ToGate { get; init; } = 22;
    /// <summary>The wall's distance past the last row's backs, and the rear wall's place along the line (m; behind the yard's start).</summary>
    public double Margin { get; init; } = 7;
    public double Rear { get; init; } = -8;
    /// <summary>
    /// How the streets bend (note 353's natural layout): each street a side swings out and back on one wave of
    /// <see cref="BendWavelength"/> m, the first by <see cref="BendBase"/> m and each further out by <see cref="BendStep"/>
    /// m more (so the rows between them keep their room), at most <see cref="BendMax"/>,
    /// straight within <see cref="BendClear"/> m of the square's ends (the square and the green keep their lines).
    /// </summary>
    public double BendBase { get; init; }
    public double BendStep { get; init; }
    public double BendMax { get; init; }
    public double[] BendWavelength { get; init; } = [220, 320];
    public double BendClear { get; init; } = 30;
    /// <summary>How much further back a house may stand than the setback, or nearer (m): the row steps in and out.</summary>
    public double[] SetbackJitter { get; init; } = [0, 0];
    /// <summary>The most streets a side.</summary>
    public int MaxStreets { get; init; } = 5;
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
    /// <summary>The council's ordinances (note 353): a town posts some of them by the clerk's door.</summary>
    public string[] Laws { get; init; } = [];
    /// <summary>The green's and the walls' pieces by kind (statue, memorial, bandstand, garden, tree, flag, mural): what each is called and what looking at it tells you.</summary>
    public Dictionary<string, TownText[]> Civic { get; init; } = [];
    /// <summary>What the people of a walled town say of living inside it (note 353): what becomes of those who rarely leave.</summary>
    public string[] Walled { get; init; } = [];
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
    /// <summary>How the towns' houses look (content/world/houses.json): their palettes and the towns' characters.</summary>
    public HouseLooks Looks { get; init; } = new();

    /// <summary>Who the townspeople are and what they're called (note 474: tuning and world townsfolk.json); null without them.</summary>
    public TownFolkContent? Folk { get; init; }

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
        // The peoples' surnames after them (note 474), so every family a house is given is one of the town's.
        var folk = TownFolkContent.Load(content);
        if (folk is not null)
            surnames = [.. surnames.Concat(folk.Surnames).Distinct(StringComparer.Ordinal)];
        return new TownContent(DataFile.Load<TownTuning>(tuning), words, surnames) { Looks = HouseLooks.Load(content), Folk = folk };
    }
}
