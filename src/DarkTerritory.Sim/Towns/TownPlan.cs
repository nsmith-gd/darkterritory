using Ballast;
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
    TownBounds? Bounds = null,
    TownGreen? Green = null,
    TownWorks? Works = null);

/// <summary>
/// A walled town's green (note 353): across the first street from the square, from <see cref="S0"/> to <see cref="S1"/>
/// along the line and <see cref="Near"/> to <see cref="Far"/> out from it on the square's <see cref="Side"/>, where the
/// houses would have been. The statue, the wall of names, the bandstand, the lamp garden and the trees stand on it.
/// </summary>
public sealed record TownGreen(double S0, double S1, double Near, double Far, int Side)
{
    public bool Holds(double s, double d, double pad = 0) =>
        s >= S0 - pad && s <= S1 + pad && Math.Sign(d) == Side && Math.Abs(d) >= Near - pad && Math.Abs(d) <= Far + pad;
}

/// <summary>
/// A walled town's works (queue #166, note 430): its <see cref="Trade"/> (towns.json industries: "coal", "farm",
/// "foundry") at work inside its wall, across the line from the green, between the far side's first and second streets:
/// from <see cref="S0"/> to <see cref="S1"/> along the line and <see cref="Near"/> to <see cref="Far"/> out from it on
/// <see cref="Side"/>, where the houses would have been. Its pieces are the plan's fixtures standing in it.
/// </summary>
public sealed record TownWorks(double S0, double S1, double Near, double Far, int Side, string Trade)
{
    public bool Holds(double s, double d, double pad = 0) =>
        s >= S0 - pad && s <= S1 + pad && Math.Sign(d) == Side && Math.Abs(d) >= Near - pad && Math.Abs(d) <= Far + pad;
}

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

/// <summary>
/// A street beside the line: its middle (D) where it runs straight, from <see cref="S0"/> to <see cref="S1"/> along it, its
/// width; and how it bends (note 353): out from the line by <see cref="Bend"/> times its side's <see cref="Wave"/>.
/// </summary>
public sealed record TownStreet(double D, double S0, double S1, double Width, double Bend = 0, TownWave? Wave = null)
{
    /// <summary>Its middle at <paramref name="s"/> along the line.</summary>
    public double At(double s) => D + Math.Sign(D) * Bend * (Wave?.At(s) ?? 0);
}

/// <summary>
/// The wave a side's streets bend on (note 353), −1 to 1: a sine of <see cref="Length"/> m from <see cref="Phase"/>, eased
/// to nothing within <see cref="Ease"/> m of the stretch from <see cref="Quiet0"/> to <see cref="Quiet1"/> (the square
/// and its green), where it's straight.
/// </summary>
public sealed record TownWave(double Phase, double Length, double Quiet0, double Quiet1, double Ease)
{
    public double At(double s)
    {
        double off = s < Quiet0 ? Quiet0 - s : s > Quiet1 ? s - Quiet1 : 0;
        double t = Math.Clamp(off / Math.Max(1, Ease), 0, 1);
        return DMath.Sin((s - Phase) / Length * 2 * Math.PI) * t * t * (3 - 2 * t);
    }
}

/// <summary>
/// A lane across the streets at <see cref="S"/> where it crosses the line, from <see cref="D0"/> to <see cref="D1"/>, its
/// width; and where it runs crooked (note 353), the <see cref="Kinks"/> it turns at, (D, S) in order out across the line
/// (one where it meets each street), straight between them and on past the last.
/// </summary>
public sealed record TownLane(double S, double D0, double D1, double Width, IReadOnlyList<(double D, double S)>? Kinks = null)
{
    /// <summary>Its middle (along the line) at <paramref name="d"/> across it.</summary>
    public double At(double d)
    {
        if (Kinks is not { Count: > 0 } k)
            return S;
        if (d <= k[0].D)
            return k[0].S;
        for (int i = 1; i < k.Count; i++)
            if (d <= k[i].D)
                return k[i - 1].S + (k[i].S - k[i - 1].S) * (d - k[i - 1].D) / Math.Max(1e-9, k[i].D - k[i - 1].D);
        return k[^1].S;
    }

    /// <summary>
    /// How far along the line its ground reaches either way anywhere from <paramref name="da"/> to <paramref name="db"/>
    /// across it: its middle's least and most there, out by its half-width (wider along the line where it runs at an angle).
    /// </summary>
    public (double Lo, double Hi) Span(double da, double db)
    {
        if (da > db)
            (da, db) = (db, da);
        double lo = Math.Min(At(da), At(db)), hi = Math.Max(At(da), At(db)), slope = 0;
        if (Kinks is { Count: > 0 } k)
            for (int i = 0; i < k.Count; i++)
            {
                if (k[i].D > da && k[i].D < db)
                    (lo, hi) = (Math.Min(lo, k[i].S), Math.Max(hi, k[i].S));
                // The steepest of the stretches that touch the band.
                if (i > 0 && k[i].D > da && k[i - 1].D < db)
                    slope = Math.Max(slope, Math.Abs((k[i].S - k[i - 1].S) / Math.Max(1e-9, k[i].D - k[i - 1].D)));
            }
        double half = Width / 2 * Math.Sqrt(1 + slope * slope);
        return (lo - half, hi + half);
    }
}

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
/// doors; <see cref="Pose"/> how they are ("idle", "lantern", "seated", "crouch"). <see cref="Gear"/>: what they breathe
/// through out of doors (<see cref="TownGear"/>, note 353).
/// </summary>
public sealed record Townsperson(int Id, string Name, string Title, string Role, double S, double D, double Up, double FaceS, double FaceD, int Look,
    IReadOnlyList<string> Lines, int House = -1, string Pose = "idle", string Gear = "respirator");

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
