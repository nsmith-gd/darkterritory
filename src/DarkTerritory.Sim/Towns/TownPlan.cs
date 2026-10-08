namespace DarkTerritory.Sim.Towns;

/// <summary>
/// A fortress town as generated (GDD §3.1; ARCHITECTURE §8 note 281): its name and custom, its square, its people and
/// their lines, its papers and the things in it to look at. Everything is placed in the main line's rail frame: S metres
/// along it, D metres out to its right (left is negative), as the stops are (level-design conventions). The same on every
/// machine: made from the route and the content alone.
/// </summary>
public sealed record TownPlan(
    string Name,
    int Population,
    int Former,
    string Culture,
    string Creature,
    string Law,
    string Hall,
    string Industry,
    IReadOnlyList<string> Quirks,
    TownSquare Square,
    IReadOnlyList<TownBuilding> Buildings,
    IReadOnlyList<TownHouse> Houses,
    IReadOnlyList<Townsperson> People,
    IReadOnlyList<TownPaper> Papers,
    IReadOnlyList<TownFixture> Fixtures,
    string Character = "",
    TownBounds? Bounds = null);

/// <summary>
/// A walled town's extent and streets (queue #74, note 335), in the rail frame: the wall along the line from
/// <see cref="Rear"/> to <see cref="Gate"/> and out to <see cref="Left"/> (the −D side) and <see cref="Right"/> (+D), the
/// streets beside the line and the lanes across them. Null on a town that's still the yard's corridor (its houses on the
/// line's own street only), whose walls are the fortress's two (T124).
/// </summary>
public sealed record TownBounds(double Rear, double Gate, double Left, double Right, IReadOnlyList<TownStreet> Streets, IReadOnlyList<TownLane> Lanes)
{
    /// <summary>Whether a point (along the line, across it) is inside the wall, give or take <paramref name="pad"/>.</summary>
    public bool Holds(double s, double d, double pad = 0) => s >= Rear - pad && s <= Gate + pad && d >= -Left - pad && d <= Right + pad;
}

/// <summary>A street beside the line: its middle (D), from <see cref="S0"/> to <see cref="S1"/> along it, its width.</summary>
public sealed record TownStreet(double D, double S0, double S1, double Width);

/// <summary>A lane across the streets at <see cref="S"/>, from <see cref="D0"/> to <see cref="D1"/>, its width.</summary>
public sealed record TownLane(double S, double D0, double D1, double Width);

/// <summary>Where the walls step back for the square: along the line from <see cref="S0"/> to <see cref="S1"/>, out to
/// <see cref="WallD"/> on <see cref="Side"/>.</summary>
public sealed record TownSquare(double S0, double S1, int Side, double WallD)
{
    /// <summary>Whether a point along the line, on a side, is inside the square (a fortress house wouldn't stand there).</summary>
    public bool Holds(double s, int side) => side == Side && s >= S0 && s <= S1;
}

/// <summary>One of the square's buildings, backed onto its far wall, its front to the line. <see cref="Kind"/>: hall,
/// office or store. Its footprint is <see cref="Length"/> along the line by <see cref="Depth"/> across it.
/// <see cref="Style"/>: the custom's building's look ("church", "school", "shed", "hall"); empty for the others.</summary>
public sealed record TownBuilding(string Kind, string Name, double S, double D, double Length, double Depth, string Knock, string Style = "");

/// <summary>
/// Somebody in the town: where they stand (feet, <see cref="Up"/> off the ground on a platform), which way they face
/// (along the line, across it), what they do, and what they say, one line a word with them and round again.
/// <see cref="Look"/> picks their clothes from the crew's. <see cref="House"/>: the open house they're in, or −1 out of
/// doors; <see cref="Pose"/> how they are ("idle", "lantern", "seated", "crouch").
/// </summary>
public sealed record Townsperson(int Id, string Name, string Title, string Role, double S, double D, double Up, double FaceS, double FaceD, int Look,
    IReadOnlyList<string> Lines, int House = -1, string Pose = "idle");

/// <summary>
/// A paper to read: a notice on the board (<see cref="OnBoard"/>, read in turn there), or a note left lying about at
/// (S, D) and <see cref="Height"/> off the ground.
/// </summary>
public sealed record TownPaper(int Id, string Title, string Text, bool OnBoard, double S, double D, double Height);

/// <summary>
/// A thing in the square to look at closely: the custom's centrepiece, the notice board, the plaque. <see cref="Kind"/>
/// names its art ("bell", "post", "board", "plaque", …); <see cref="Text"/> is what looking at it tells you.
/// <see cref="Solid"/>: half its size along and across the line, a box nobody walks through; zero for a thing on the ground.
/// <see cref="House"/>: the open house it's in (a household's thing, the stove, the stair door), or −1 in the square.
/// </summary>
public sealed record TownFixture(int Id, string Kind, string Name, string Text, double S, double D, double FaceS, double FaceD,
    double SolidS, double SolidD, double Height, int House = -1);
