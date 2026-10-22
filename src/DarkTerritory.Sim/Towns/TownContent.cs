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
    /// <summary>The share of walled towns with one of Dave's murals on a wall (note 570).</summary>
    public double DaveMural { get; init; }
    /// <summary>The share of towns with Nicki's party in an open house, and how many guests she has (note 571).</summary>
    public double Nicki { get; init; }
    public int[] NickiGuests { get; init; } = [2, 4];
    /// <summary>Nicki's wine (note 571): taken by holding Use by her, a glass a crewmate a night.</summary>
    public NickiWine Wine { get; init; } = new();
    /// <summary>The blacksmith's forge (towns.json <c>smithy</c>; note 697). Unset, a town has none.</summary>
    public SmithyTuning? Smithy { get; init; }
    public required int[] Quirks { get; init; }
    public required int[] Notices { get; init; }
    /// <summary>How many of the board's notices are the town's own (its culture's <c>board</c>; note 669).</summary>
    public int BoardOwn { get; init; }
    public required int[] LooseNotes { get; init; }
    public required TownReach Reach { get; init; }
    public double TypePerSecond { get; init; } = 45;
    /// <summary>Note 971: households that argue where the crew can overhear them (towns.json <c>quarrels</c>).</summary>
    public QuarrelTuning Quarrels { get; init; } = new();
    /// <summary>Note 989: what stands between the houses (towns.json <c>between</c>).</summary>
    public BetweenTuning Between { get; init; } = new();
    /// <summary>
    /// Note 971: what somebody says in place of the custom's line once it's been told in their town (towns.json <c>told</c>,
    /// by temperament; "plain" for anyone else).
    /// </summary>
    public Dictionary<string, string[]> Told { get; init; } = [];

    /// <summary>Note 963: how a temperament's lines type out on the talk card (towns.json <c>voices</c>); a temperament not named types plainly.</summary>
    public Dictionary<string, TalkVoice> Voices { get; init; } = [];

    /// <summary>The voice <paramref name="p"/>'s lines type in: their temperament's, or plain.</summary>
    public TalkVoice VoiceOf(Townsperson p) => p.Personality is { } who && Voices.TryGetValue(who.Temperament, out var v) ? v : TalkVoice.Plain;
    public double LingerSeconds { get; init; } = 6;
    /// <summary>Their rounds (note 353).</summary>
    public RoundTuning Rounds { get; init; } = new();
    /// <summary>The town types (note 692): see <see cref="FormTuning"/>.</summary>
    public FormTuning Forms { get; init; } = new();
}

/// <summary>
/// The town types (towns.json <c>forms</c>; queue #424, note 692; the director, 13 Oct 2026: "multiple different town
/// types ... that give the world a bit more flavor and a bit more meaning"): the yard every one keeps for the longest
/// consist, and each form's weight by the town's trade. Field docs live in towns.json.
/// </summary>
public sealed record FormTuning
{
    public bool Enabled { get; init; }
    /// <summary>Every town this form ("" the street town; null, each its own): for playing or looking at one type.</summary>
    public string? Force { get; init; }
    /// <summary>The longest consist's length (m), and how far short of the way out its engine stands (run.json departShortOfGateM).</summary>
    public double YardM { get; init; } = 330;
    public double ShortOfGate { get; init; } = 8;
    /// <summary>Each form's weight by trade (trade → form → weight; "" the street town).</summary>
    public Dictionary<string, Dictionary<string, double>> ByTrade { get; init; } = [];
    /// <summary>Each form's size and ground (form → shape).</summary>
    public Dictionary<string, FormShape> Shapes { get; init; } = [];
}

/// <summary>
/// One town type's size and ground (towns.json <c>forms.shapes</c>, note 692): its length along the line between its two
/// gates, how far its wall reaches either side, its lots' frontage and depth and how far a house stands back from its
/// way, its water's level and its terraces' rise (m against the yard's level), and a quarry's benches.
/// </summary>
public sealed record FormShape
{
    public double Length { get; init; } = 520;
    public double Left { get; init; } = 150;
    public double Right { get; init; } = 150;
    public double[] Lot { get; init; } = [12, 16];
    public double LotDepth { get; init; } = 9;
    public double Setback { get; init; } = 3;
    public double WaterLevel { get; init; } = -1.5;
    /// <summary>How deep its water is over its bed (m): past TownGround.Wade it's railed off; under it, waded.</summary>
    public double WaterDepth { get; init; } = 1.6;
    public double TerraceLevel { get; init; } = 5;
    public int Benches { get; init; } = 3;
    /// <summary>How far past the wall it shapes the land (m; its landform), and each street band's rise over the last (m).</summary>
    public double Margin { get; init; } = 120;
    public double Rise { get; init; }
    /// <summary>How far its landforms past the wall swell up and down (m, either way; note 692).</summary>
    public double Roll { get; init; }
    /// <summary>
    /// Its warren (note 692; the director, 14 Oct: "they don't look like the references"): its streets' width, how far
    /// they wander either way off straight (m), how far out from the line its first row backs onto the yard's wall, and
    /// whether its houses stand shoulder to shoulder, each as wide as its lot.
    /// </summary>
    public double StreetWidth { get; init; } = 4.5;
    public double Wander { get; init; } = 3;
    public double Near { get; init; } = 16.2;
    public bool Packed { get; init; }
    /// <summary>Whether its wall is a timber palisade (note 692: Timberholds).</summary>
    public bool Palisade { get; init; }
}

/// <summary>A town type's words (world/towns.json <c>forms</c>, note 692): its name ("a harbour town") and the names its
/// quarters go by, by district id, one picked a town.</summary>
public sealed record TownFormWords
{
    public string Name { get; init; } = "";
    public Dictionary<string, string[]> Districts { get; init; } = [];
    /// <summary>What its landmarks are called and what looking at one tells you, by kind ("lighthouse", "chapel", "lift").</summary>
    public Dictionary<string, TownText> Landmarks { get; init; } = [];
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
    /// <summary>
    /// Note 963 (townsfolk.md "Not yet"): where somebody's temperament takes them on a slot away from their post, and how often
    /// (towns.json <c>rounds.temperaments</c>): "gate" (stood inside it looking out), "wall" (at the nearest of the town's
    /// walls, looking out), or a kind of the square's places ("garden", "vigil", "warm", "read", "work", "mend"), "lamps" (down their street to a lamp) or "talk"; more
    /// than one, comma-separated, tried in turn.
    /// </summary>
    public Dictionary<string, TemperamentRound> Temperaments { get; init; } = [];
}

/// <summary>
/// How somebody's lines type out on the talk card (note 963; docs/design/townsfolk.md "Not yet": "A frayed person could type in
/// bursts, a hollow one slowly"): <see cref="Rate"/> times the town's pace; a pause after a comma or a dash
/// (<see cref="Comma"/>, s) and after a sentence's end (<see cref="Stop"/>); and in bursts, <see cref="Burst"/> characters
/// at a time with <see cref="BurstPause"/> between (0: none). Presentation only, a pure function of the line and the time.
/// </summary>
public sealed record TalkVoice(double Rate = 1, double Comma = 0, double Stop = 0, int Burst = 0, double BurstPause = 0)
{
    public static readonly TalkVoice Plain = new();

    /// <summary>How long the whole of <paramref name="line"/> takes to type at <paramref name="perSecond"/> (the town's pace).</summary>
    public double Seconds(string line, double perSecond) => At(line, perSecond, double.PositiveInfinity).Seconds;

    /// <summary>How many characters of <paramref name="line"/> are out <paramref name="elapsed"/> seconds in.</summary>
    public int Typed(string line, double perSecond, double elapsed) => At(line, perSecond, elapsed).Typed;

    (int Typed, double Seconds) At(string line, double perSecond, double elapsed)
    {
        double each = 1 / Math.Max(1e-3, perSecond * Math.Max(0.05, Rate)), t = 0;
        for (int i = 0; i < line.Length; i++)
        {
            t += each;
            if (t > elapsed)
                return (i, t);
            char c = line[i];
            bool last = i == line.Length - 1;
            // (Pauses after a character that's out: the line's end has none.)
            if (!last)
            {
                if (c is ',' or ';' or ':' || c == '-' && i > 0 && line[i - 1] == ' ' || c == '\u2014')
                    t += Comma;
                else if (c is '.' or '?' or '!' && line[i + 1] == ' ')
                    t += Stop;
                if (Burst > 0 && (i + 1) % Burst == 0)
                    t += BurstPause;
            }
            if (t > elapsed)
                return (i + 1, t);
        }
        return (line.Length, t);
    }
}

/// <summary>
/// Households that argue (note 971; docs/design/townsfolk.md "Not yet": "a lapsed lodger in a zealous house could say so"): a
/// house whose two at most odds on one of <see cref="Lines"/>' traits are <see cref="Gap"/> or more apart has that trait's
/// quarrel, overheard within <see cref="Hear"/> m of its front for <see cref="Lasts"/> s of every <see cref="Every"/>, a line
/// every <see cref="LineSeconds"/> s. Each quarrel's lines go back and forth, the first said by the one higher on the trait;
/// "{other}" is the other one's name.
/// </summary>
public sealed record QuarrelTuning
{
    public double Gap { get; init; } = 1.1;
    public double Hear { get; init; } = 9;
    public double Every { get; init; } = 150;
    public double Lasts { get; init; } = 40;
    public double LineSeconds { get; init; } = 5;
    public Dictionary<string, string[][]> Lines { get; init; } = [];
}

/// <summary>A temperament's pull on somebody's round (note 963): where to (<see cref="To"/>), and the chance a slot away goes there.</summary>
public sealed record TemperamentRound(string To = "", double Chance = 0);

public sealed record SquareTuning
{
    public int Side { get; init; } = -1;
    public required double[] FromGate { get; init; }
    public double WallOut { get; init; }
    public double BuildingDepth { get; init; }
    public double HallWidth { get; init; }
    public double OfficeWidth { get; init; }
    /// <summary>A small town's green (note 490): in its square's rear end, from the end along it and out from the line
    /// (m), clear of the clerk's office, the stall and the first bench.</summary>
    public double[] GreenAlong { get; init; } = [5, 17];
    public double[] GreenOut { get; init; } = [7, 17];
    /// <summary>
    /// The night market (queue #552, note 825; the director's playtest of 14 Oct, line 3: "Still a bit basic"): how many
    /// stalls, the least and the most, one more for every <see cref="MarketPer"/> people; how far in front of the buildings'
    /// fronts its row stands, and how far apart along it its stalls may stand (m).
    /// </summary>
    public int[] Market { get; init; } = [4, 8];
    public double MarketPer { get; init; } = 400;
    public double MarketIn { get; init; } = 3.6;
    public double MarketEvery { get; init; } = 4.6;
    /// <summary>
    /// The market's second row (note 870; the director's playtest of 19 Oct, line 4: "Too many cut and paste stalls in a
    /// row"): how wide the aisle between its two rows of counters is, and how far each stall may stand off its place along
    /// and across (m).
    /// </summary>
    public double MarketAisle { get; init; } = 3.4;
    public double MarketJitter { get; init; } = 0.5;
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
    /// <summary>
    /// The most houses a town keeps on the line's own street once it has more than that (note 668): the rest stand on
    /// streets of its own, so a town of forty houses isn't one row down the track.
    /// </summary>
    public int LineRow { get; init; } = int.MaxValue;
    /// <summary>How long a run of one plan is down a company town's street (m; note 815).</summary>
    public double PlanRun { get; init; } = 45;
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
    /// <summary>How far along the line a lane turns, one way or the other, where it meets each street (m; note 353).</summary>
    public double[] LaneJog { get; init; } = [3, 10];
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
    /// <summary>How far a shut house on a street stands off true (degrees), and how many do (note 490); none when 0.</summary>
    public double[] Turn { get; init; } = [0, 0];
    public double TurnChance { get; init; }
    /// <summary>How far the row facing the green across its far street turns to it at the green's ends (degrees; note 490).</summary>
    public double Crescent { get; init; }
    /// <summary>The most streets a side.</summary>
    public int MaxStreets { get; init; } = 5;
    /// <summary>
    /// The town's works (queue #90, note 353), across the line from the green: how far before the square's rear end and
    /// past its gate end they run along the line, how far their pieces keep from their streets and the works' ends, and
    /// the room between pieces (m).
    /// </summary>
    public double WorksBefore { get; init; } = 30;
    public double WorksPast { get; init; } = 6;
    public double WorksMargin { get; init; } = 1.5;
    public double WorksGap { get; init; } = 3;
    /// <summary>How many of the town's hands are out at its works at night (note 353).</summary>
    public int WorksHands { get; init; } = 3;
    /// <summary>
    /// The town's rail yard (queue #239, note 502), behind the home platform: how far before the square's rear end it starts,
    /// how far out from the line its siding runs, how far its pieces and buffer stops keep in from its street and its ends,
    /// the room between pieces, how much stock stands in its siding, how far apart its lamps are, and how many porters
    /// work it at night (m, a count).
    /// </summary>
    /// <summary>The fewest people a town has its rail yard for (note 502): a smaller one's first street is all it has on that side.</summary>
    public int YardPeople { get; init; } = 400;
    public double YardBefore { get; init; } = 90;
    public double YardSiding { get; init; } = 14;
    public double YardMargin { get; init; } = 1.5;
    public double YardGap { get; init; } = 4;
    public int[] YardStock { get; init; } = [2, 4];
    public double YardLamps { get; init; } = 28;
    public int YardHands { get; init; } = 2;
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
    /// <summary>The town types' words (note 692): what each is called, and its quarters' names, by form.</summary>
    public Dictionary<string, TownFormWords> Forms { get; init; } = [];
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
    /// <summary>
    /// What a town's people believe, by its houses' character (<see cref="HouseCharacter.Creed"/>; note 822, the Flooders):
    /// never drawn as a habit is, only kept where the character keeps it.
    /// </summary>
    public TownQuirk[] Creeds { get; init; } = [];
    public required Dictionary<string, TownIndustry> Industries { get; init; }
    public required Dictionary<string, TownRole> Roles { get; init; }
    /// <summary>The forge (note 697): its name, and what its board says read close to.</summary>
    public TownText? Forge { get; init; }
    public required TownThread[] Threads { get; init; }
    public required TownText[] Notices { get; init; }
    /// <summary>The night market's stalls (note 825), each its name and what's on it, dealt with the town's trade's own stalls.</summary>
    public TownText[] Market { get; init; } = [];
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
    /// <summary>
    /// A walled town's works by its trade (queue #90, note 353; the industries' keys, and "any" for what every town's works
    /// have after its own): the pieces in the order they stand along the line, each with what looking at it tells you.
    /// </summary>
    public Dictionary<string, TownWork[]> Works { get; init; } = [];
    /// <summary>
    /// A walled town's rail yard (queue #239, note 502): its pieces in order from its gate end (<see cref="TownWork.After"/>:
    /// the room left beside one toward the yard's rear end, the weighbridge's plate), each fronting the siding
    /// (<see cref="TownWork.Across"/>: how much further back from it); and the stock that can be left standing in its siding.
    /// </summary>
    public TownWork[] Yard { get; init; } = [];
    public TownWork[] Stock { get; init; } = [];
    /// <summary>What the people of a walled town say of living inside it (note 353): what becomes of those who rarely leave.</summary>
    public string[] Walled { get; init; } = [];
    /// <summary>A town that's had a creature brought in through its gate, remembering it (note 608); none, it says nothing of it.</summary>
    public TownBroughtIn? BroughtIn { get; init; }
}

/// <summary>
/// What a town says of the creatures brought in through its gate (note 608), once it's had one: a house it burnt, looked at
/// ({family}), and what its gatekeeper and its guards say first ({town}).
/// </summary>
public sealed record TownBroughtIn
{
    public string[] Houses { get; init; } = [];
    public string[] Gate { get; init; } = [];
    public string[] Guard { get; init; } = [];
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
/// Where the blacksmith works (towns.json <c>smithy</c>; ARCHITECTURE §8 note 697): the forge <see cref="FromGate"/> m along
/// the line from the gate (back into the yard, beside the engine as it stands there), <see cref="Out"/> m out on the town's
/// side; the smith <see cref="Stand"/> m in front of it, toward the line. Use pressed on them opens the
/// catalogue (note 904).
/// </summary>
public sealed record SmithyTuning(double FromGate = -18, double Out = 8.5, double Stand = 2.2);

/// <summary>
/// A piece of a town's works (note 353): its <see cref="Kind"/> (its art and its size, <see cref="TownFixtures.Size"/>), what
/// it's called and what looking at it tells you; <see cref="After"/>, where it must stand that far along the line from the
/// piece before it (the winding house from its headframe, its ropes run between them), else packed after it; and
/// <see cref="Across"/>, how far from the works' middle it stands toward their far street (negative: toward the line).
/// </summary>
public sealed record TownWork(string Kind, string Title, string Text, double After = 0, double Across = 0);

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
    /// <summary>What only this town pins on its board, in its own voice (note 669): a few each night, after its order.</summary>
    public TownText[] Board { get; init; } = [];
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

/// <summary>
/// tuning/towns.json <c>wine</c> (note 571; the director, 9 Oct: "when Nicki offers wine to the players they should get extra
/// health for the next run if they take it"). She's only ever in the departure town, before the night sets out, so the run
/// it's for is the one about to start: <see cref="Health"/> over full, until it's knocked off them like any other.
/// </summary>
public sealed record NickiWine
{
    /// <summary>How close to her (m), and how long Use is held by her to take a glass (s).</summary>
    public double Reach { get; init; } = 2.6;
    public double HoldSeconds { get; init; } = 0.8;
    /// <summary>The health it's worth, on top of whatever they have.</summary>
    public int Health { get; init; } = 25;
}
