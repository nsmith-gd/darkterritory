namespace DarkTerritory.Sim.Towns;

/// <summary>
/// What a house in a town is now (the director, 7 Oct 2026: towns of 20 to 350, "fully interior modeled and explorable
/// for some of them with residents"; every town has lost people): lived in and shut, standing open with its household
/// inside, or nobody's (boarded, burnt, or left open).
/// </summary>
public enum HouseKind : byte { Lived, Open, Boarded, Burnt, Empty }

/// <summary>
/// A house down the yard's street (note 281): where it stands (its middle, S along the line and D out to the
/// <see cref="Side"/>), how big its main block is, what it is now, its <see cref="Design"/>, whose it is, what a knock or a
/// look at it tells you, and for an open one the rooms inside. It fronts the line.
/// </summary>
public sealed record TownHouse(int Id, double S, double D, int Side, double Width, double Depth, HouseKind Kind, HouseDesign Design,
    string Family, string Text, HouseLayout? Layout)
{
    /// <summary>
    /// What stands of it, in its own frame (u along its front from its middle, v in from the front): its main block (an
    /// open house's is its own walls, <see cref="Towns.Town"/>), a side wing, an enclosed porch out in front of the door.
    /// A burnt house stands knee-high, and its wing and porch went with it.
    /// </summary>
    public IEnumerable<(double U0, double U1, double V0, double V1, double Height)> Parts()
    {
        var d = Design;
        bool burnt = Kind == HouseKind.Burnt;
        if (Layout is null)
            yield return (-Width / 2, Width / 2, 0, Depth, burnt ? 1.0 : 9);
        if (burnt)
            yield break;
        if (d.Ell != 0)
        {
            double inner = d.Ell * Width / 2, outer = d.Ell * (Width / 2 + d.EllWidth);
            yield return (Math.Min(inner, outer), Math.Max(inner, outer), d.EllSetback, d.EllSetback + d.EllDepth, 6);
        }
        if (d.Porch == HousePorch.Vestibule)
            yield return (d.DoorU - HouseDesign.VestibuleHalf, d.DoorU + HouseDesign.VestibuleHalf, -HouseDesign.VestibuleDepth, 0, 3.5);
    }

    /// <summary>
    /// What stands in its yard (ARCHITECTURE §8 note 335, the director's references: the picket fence out front, the board
    /// fence at the back, the woodpile, the shed, the privy, the traps and the dory), in its own frame. Empty on the
    /// line's own street of a town that's still the yard (no room behind the houses).
    /// </summary>
    public IReadOnlyList<YardThing> Yard { get; init; } = [];
    /// <summary>Nicki's party is on in it (note 487): its people keep their places, dancing, and it's lit up.</summary>
    public bool Party { get; init; }

    /// <summary>Everything of it that stops you: <see cref="Parts"/>, and its yard's solid things.</summary>
    public IEnumerable<(double U0, double U1, double V0, double V1, double Height)> Solids() =>
        Parts().Concat(Yard.Where(y => y.Solid).Select(y => (y.U0, y.U1, y.V0, y.V1, y.Height)));

    /// <summary>How far its front door is out from its front wall: an enclosed porch's door, else the front's.</summary>
    public double DoorV => Design.Porch == HousePorch.Vestibule ? -HouseDesign.VestibuleDepth : 0;

    /// <summary>The front wall's line, out from the line: the side's offset less half the depth.</summary>
    public double FrontD => D - Side * Depth / 2;

    /// <summary>A point in the house's own frame (u along the line from its middle, v in from its front) in the rail frame.</summary>
    public (double S, double D) Rail(double u, double v) => (S + u, FrontD + Side * v);

    /// <summary>A direction in the house's frame (along, in) as the rail frame's (along the line, across it).</summary>
    public (double S, double D) Facing(double fu, double fv) => (fu, Side * fv);
}

/// <summary>What stands in a yard (houses.json characters' <c>yard</c>, note 335).</summary>
public enum YardKind : byte { Picket, Boards, Woodpile, Shed, Privy, Traps, Dory, Clothesline, Barrel }

/// <summary>
/// A thing in a house's yard, in the house's frame (u along its front from its middle, v in from its front): its footprint,
/// its height, and which of its kind it is. A picket fence's gate is the gap between two of them, in front of the door.
/// </summary>
public sealed record YardThing(YardKind Kind, double U0, double U1, double V0, double V1, double Height, int Variant = 0)
{
    /// <summary>Whether it stops you: everything but the clothesline (you walk under the washing).</summary>
    public bool Solid => Kind != YardKind.Clothesline;
}

/// <summary>
/// The ground floor of an open house (note 281): a kitchen and a parlour either side of a partition, the front door into
/// the kitchen, a doorway through to the parlour, a boxed stair in the parlour's back corner with its door shut, and the
/// furniture. <see cref="Kitchen"/> is the side (+1 or −1 along u) the kitchen's on. Everything in the house's frame (u, v).
/// </summary>
public sealed record HouseLayout(int Kitchen, double DoorU, double PassV, IReadOnlyList<HouseThing> Things, IReadOnlyList<HouseSpot> Spots)
{
    /// <summary>The front door's width, the doorway's through the partition, the walls' thickness, the ceiling's height (m).</summary>
    public const double DoorWidth = 1.0, PassWidth = 0.95, Wall = 0.15, Ceiling = 2.5;

    /// <summary>
    /// The layout of a house <paramref name="width"/> along the street by <paramref name="depth"/> deep, the kitchen to the
    /// <paramref name="kitchen"/> side. Not design numbers: where a stove, a table and a stair go in a Maritime kitchen and
    /// parlour of this size, clear of the doors and each other.
    /// </summary>
    public static HouseLayout For(double width, double depth, int kitchen)
    {
        double w = width / 2, k = kitchen;
        var things = new List<HouseThing>
        {
            // The kitchen: the range against the side wall at the back, the table, the dresser against the back wall.
            new("stove", k * (w - 0.55), depth - 1.1, 0.42, 0.35, 0.9, true),
            new("table", k * width / 4, TableV(depth), 0.7, 0.45, 0.75, true),
            new("chair", k * width / 4, TableV(depth) + 0.75, 0.22, 0.22, 0.9, false),
            new("dresser", k * 0.85, depth - 0.3, 0.6, 0.22, 1.9, true),
            // The parlour: the stair boxed in its back corner (its door toward the partition), a cabinet against the back
            // wall, a chair with the parlour's lamp on a stand beside it, a photograph on the partition.
            new("stairs", -k * (w - 0.5), depth - 1.4, 0.5, 1.4, 2.5, true),
            new("cabinet", -k * (w - 1.75), depth - 0.28, 0.5, 0.22, 1.1, true),
            new("armchair", -k * width / 4, depth * 0.4, 0.35, 0.35, 0.9, false),
            new("lampstand", -k * (width / 4 - 0.45), depth * 0.4 - 0.75, 0.2, 0.2, 1.0, true),
            new("photo", -k * 0.1, depth * 0.72, 0.2, 0.02, 1.6, false),
        };
        var spots = new List<HouseSpot>
        {
            new("stove", k * (w - 1.35), depth - 1.1, k, 0, "crouch"),
            new("table", k * width / 4, TableV(depth) + 0.75, 0, -1, "seated"),
            new("chair", -k * width / 4, depth * 0.4, 0, -1, "seated"),
            new("window", -k * (width / 4 + 0.4), 0.8, 0, -1, "idle"),
            new("stairs", -k * (w - 1.55), depth - 1.4, -k, 0, "idle"),
            new("door", k * (width / 4 + 0.9), 1.2, 0, -1, "idle"),
        };
        return new HouseLayout(kitchen, k * width / 4, depth * 0.35, things, spots);
    }

    /// <summary>
    /// How far in the kitchen table stands: three fifths of the way back, but in a short house forward of that, so its chair
    /// (behind it) leaves room for whoever's crouched at the range (the director's 8 Oct shots: in a 5 m house the range's
    /// place was on the table's chair, the one at the table sat beside it; note 353).
    /// </summary>
    public static double TableV(double depth) => Math.Min(depth * 0.6, depth - 2.5);

    /// <summary>Where the household's own thing goes, by its kind: on the table, by the stairs, on the cabinet, by the door.</summary>
    public (double U, double V, double H) Place(string kind, double width, double depth)
    {
        double w = width / 2, k = Kitchen;
        return kind switch
        {
            "table" or "letters" or "wine" => (k * width / 4 + 0.2, TableV(depth), 0.8),
            "anklebell" => (-k * (w - 1.2), depth - 0.5, 0.9),
            "timetable" => (-k * width / 4, Wall + 0.03, 1.6),
            "boots" => (DoorU + k * 0.75, 0.45, 0.1),
            "boards" => (k * width / 4, depth - Wall - 0.03, 1.5),
            "suitcase" => (-k * (w - 1.3), depth - 0.35, 0.3),
            "cradle" => (-k * (width / 4 - 0.6), depth * 0.62, 0.7),
            _ => (-k * (w - 1.75), depth - 0.28, 1.15),
        };
    }
}

/// <summary>A thing in an open house: its kind, its middle (u, v), half its size each way, its height, and whether it's solid.</summary>
public sealed record HouseThing(string Kind, double U, double V, double HalfU, double HalfV, double Height, bool Solid);

/// <summary>Where somebody in an open house is (u, v), which way they face (along u, in v), and how ("idle", "seated", "crouch").</summary>
public sealed record HouseSpot(string Name, double U, double V, double FaceU, double FaceV, string Pose);
