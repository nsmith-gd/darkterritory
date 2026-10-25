using DarkTerritory.Sim.Run;

namespace DarkTerritory.Sim.Towns;

/// <summary>
/// The towns' houses with floors (queue #725, ARCHITECTURE §8 note 1022; #710's villages carried to the towns, note 1005; the
/// director, 23 Oct 2026: "more complex houses that have multiple levels, including basements. Those basements should get
/// pretty dark. They should have windows that you can escape from ... It's going to make looking in a house feel a bit more
/// scary"). An open house (its household at home, or a boarded one left as it was, <see cref="TownHouse.Left"/>) and a
/// burnt one, in its own frame (u along its front from its middle, v in from its front; heights over its ground floor's
/// boards, which are the town's ground at its middle):
/// <list type="bullet">
/// <item>An upstairs (a two-storey house's, <see cref="TownHouse.Upstairs"/>): a straight flight of <see cref="Rises"/>
/// against the parlour's end wall, climbing from near the front toward the back, each rise a step the motor takes; the upper
/// floor a slab with the well open over the flight, railed; a partition over the one downstairs, its doorway near the back
/// (the flight comes up at the back); the bedrooms furnished (<see cref="Furniture"/>); and in the kitchen's bedroom's
/// back wall a window that opens, a ladder's height over the floor, onto the lean-to porch roof under it, and off that
/// it's a drop to the yard (under player.json's fallHurtFrom: no knock).</item>
/// <item>A cellar (<see cref="TownHouse.Cellar"/>): the ground floor a slab, a hatch let into it in the parlour behind the
/// armchair (a burnt house's floor burnt through in the middle), a ladder down its front edge to the cellar's floor
/// <see cref="Cellar"/> under the boards, its walls stone. Inside the footprint the earth isn't stood on
/// (<see cref="StopWalls.Under"/>): the boards are, and the cellar's floor. Dark (the art's dark room): a hand lamp's what
/// you see by. Its only way out is the hatch: a town's houses stand on the ground, not a village's plinth, so there's no
/// window to put in its walls over the ground outside (note 1022's decision, the director's to change).</item>
/// </list>
/// Not design numbers: a Maritime house's, fitted to the art's storey (MaritimeKit, 2.7 m) and the layout's ground floor.
/// Which houses have them is towns.json <c>floors</c>.
/// </summary>
public static class TownFloors
{
    /// <summary>Floor to floor (m: the art's storey), the upper floor's slab (its underside the ground floor's ceiling).</summary>
    public const double Storey = 2.7, Slab = HouseLayout.Wall + 0.05;

    /// <summary>The cellar's floor under the ground floor's boards (m).</summary>
    public const double Cellar = 2.4;

    /// <summary>The flight up: its rises (the last onto the upper floor), each tread's depth, its width (m), and how far its foot stands in from the front wall.</summary>
    public const int Rises = 10;

    /// <inheritdoc cref="Rises"/>
    public const double Tread = 0.4, StairWidth = 0.85, StairFoot = 0.75;

    /// <summary>A rail's height over the upper floor and half its thickness (m).</summary>
    public const double RailHeight = 1.0, RailHalf = 0.04;

    /// <summary>The hatch down (square, m), and its ladder's step off over the top (into the room).</summary>
    public const double HatchSize = 0.9, HatchOnto = 0.5;

    /// <summary>A burnt house's floor burnt through (square, m).</summary>
    public const double Burnt = 1.3;

    /// <summary>A doorway's head over its floor (m): the lintel over the front door and the upstairs partition's doorway.</summary>
    public const double DoorHead = 2.1;

    /// <summary>The upstairs window that opens (m): its width, its sill and head over the upper floor (the art's upstairs panes'), its step off outside.</summary>
    public const double WindowWidth = 0.8, WindowSill = 1.0, WindowHead = 2.2, WindowOnto = 0.75;

    /// <summary>The porch roof under it: half its run along the back wall, how far out it stands, and its top under the sill (m).</summary>
    public const double PorchHalf = 1.3, PorchOut = 1.6, PorchUnder = 0.45, PorchThick = 0.15;

    /// <summary>How high a burnt house's walls stand (m): knee-high, as it stood shut.</summary>
    public const double BurntWalls = 1.0;

    /// <summary>The rooms of a house that has them: its household's, or a boarded one's left as they were.</summary>
    public static HouseLayout? Rooms(TownHouse h) => h.Layout ?? h.Left;

    /// <summary>Whether it's built with floors (note 1022).</summary>
    public static bool Floored(TownHouse h) => h.Upstairs || h.Cellar;

    /// <summary>The upper floor's boards over the ground floor's (m), and the cellar's under them.</summary>
    public static double FloorAt(int level) => level switch { < 0 => -Cellar, > 0 => Storey, _ => 0 };

    /// <summary>Whether a house can have an upstairs: its rooms, and two full storeys under its eaves (a storey and a half's upper floor is in the roof).</summary>
    public static bool CanGoUp(TownHouse h) => Rooms(h) is not null && h.Design.Storeys >= 2 && h.Design.Eaves >= Storey + 2.2 && h.Kind != HouseKind.Burnt;

    /// <summary>The side (±1 along u) the flight's end wall is: the parlour's, away from the kitchen.</summary>
    public static int StairSide(TownHouse h) => -(Rooms(h)?.Kitchen ?? 1);

    /// <summary>
    /// The flight up: u of its wall side and its open side, v of its first tread's front edge (it climbs toward the back,
    /// +v), and v of its top tread's back edge.
    /// </summary>
    public static (double U0, double U1, double V0, double V1) Stair(TownHouse h) => Stair(h.Width, StairSide(h));

    /// <inheritdoc cref="Stair(TownHouse)"/>
    public static (double U0, double U1, double V0, double V1) Stair(double width, int side)
    {
        double inner = width / 2 - HouseLayout.Wall, v0 = HouseLayout.Wall + StairFoot;
        return (side * inner, side * (inner - StairWidth), v0, v0 + (Rises - 1) * Tread);
    }

    /// <summary>The upstairs partition's doorway (v of its middle): near the back, where the flight comes up.</summary>
    public static double UpperPassV(TownHouse h) => h.Depth - 1.5;

    /// <summary>
    /// The hatch down, its middle; and its ladder's way off at the top (into the room). An open house's is in the parlour
    /// between the flight's top and the partition, behind the armchair, its ladder down its front edge; a burnt house's floor
    /// is burnt through in the middle, away from its door.
    /// </summary>
    public static (double U, double V, double InU, double InV) Hatch(TownHouse h)
    {
        if (Rooms(h) is null)
            return (h.Design.DoorU >= 0 ? -h.Width / 6 : h.Width / 6, h.Depth * 0.55, 0, -1);
        return HatchIn(h.Width, h.Depth, StairSide(h));
    }

    /// <summary>An open house's hatch (<see cref="Hatch"/>) by its size and the flight's side.</summary>
    public static (double U, double V, double InU, double InV) HatchIn(double width, double depth, int side) => (side * (width / 2 - 1.55), depth - 1.35, 0, -1);

    /// <summary>The hatch's size (m): a burnt floor's hole is bigger.</summary>
    public static double HatchOf(TownHouse h) => Rooms(h) is null ? Burnt : HatchSize;

    /// <summary>The upstairs window that opens (u of its middle, in the back wall): the kitchen's bedroom's, toward its end.</summary>
    public static double WindowU(TownHouse h) => -StairSide(h) * (h.Width / 2 - 1.0);

    /// <summary>How high its walls stand over its ground floor (m): to the eaves upstairs, else as an open house's always have.</summary>
    public static double WallTop(TownHouse h) => h.Kind == HouseKind.Burnt ? BurntWalls : h.Upstairs ? h.Design.Eaves : 4;

    /// <summary>
    /// A floored house's solids (note 1022), in its own frame (u, v of their middles, half sizes, bottom and top over its ground
    /// floor's boards), each tagged with what it is: its outer walls with the door (and upstairs the window) cut out, the lintels,
    /// the inner partitions, the boards with the hatch let in, the cellar's walls and floor, the flight's treads, the upper
    /// floor round its well, the rails, the porch roof. The Sim stands them (<see cref="Town"/>), the art draws them.
    /// </summary>
    public static IEnumerable<ShellBox> Shell(TownHouse h)
    {
        const double t = HouseLayout.Wall;
        double w = h.Width / 2, dp = h.Depth, top = WallTop(h), low = h.Cellar ? -Cellar - 0.3 : -0.5;
        var rooms = Rooms(h);
        double doorU = rooms?.DoorU ?? h.Design.DoorU, door = HouseLayout.DoorWidth / 2;
        // The front: the door's gap, its lintel (upstairs, the wall over it is the bedroom's).
        yield return new(ShellPart.Wall, (-w + doorU - door) / 2, t / 2, (doorU - door + w) / 2, t / 2, low, top);
        yield return new(ShellPart.Wall, (doorU + door + w) / 2, t / 2, (w - doorU - door) / 2, t / 2, low, top);
        if (top > DoorHead)
            yield return new(ShellPart.Lintel, doorU, t / 2, door, t / 2, DoorHead, top);
        if (h.Cellar)
            yield return new(ShellPart.Wall, doorU, t / 2, door, t / 2, low, -Slab);
        // The back, the window that opens cut out of it upstairs.
        if (h.Upstairs)
        {
            double wu = WindowU(h), ww = WindowWidth / 2, sill = Storey + WindowSill, head = Storey + WindowHead;
            yield return new(ShellPart.Wall, (-w + wu - ww) / 2, dp - t / 2, (wu - ww + w) / 2, t / 2, low, top);
            yield return new(ShellPart.Wall, (wu + ww + w) / 2, dp - t / 2, (w - wu - ww) / 2, t / 2, low, top);
            yield return new(ShellPart.Wall, wu, dp - t / 2, ww, t / 2, low, sill);
            yield return new(ShellPart.Lintel, wu, dp - t / 2, ww, t / 2, head, top);
            // The porch roof under it, out over the yard: stood on (a lean-to's slope taken flat at its middle).
            double roof = sill - PorchUnder;
            yield return new(ShellPart.Roof, wu, dp + PorchOut / 2, PorchHalf, PorchOut / 2, roof - PorchThick, roof);
        }
        else
            yield return new(ShellPart.Wall, 0, dp - t / 2, w, t / 2, low, top);
        yield return new(ShellPart.Wall, -w + t / 2, dp / 2, t / 2, dp / 2, low, top);
        yield return new(ShellPart.Wall, w - t / 2, dp / 2, t / 2, dp / 2, low, top);
        double ix = w - t, iv0 = t, iv1 = dp - t;
        // The partition (an open house's), the doorway through it downstairs; upstairs its own, near the back.
        if (rooms is not null)
        {
            double pass = HouseLayout.PassWidth / 2, ceiling = h.Upstairs ? Storey - Slab : top;
            yield return new(ShellPart.Wall, 0, (iv0 + rooms.PassV - pass) / 2, t / 2, (rooms.PassV - pass - iv0) / 2, -Slab, ceiling);
            yield return new(ShellPart.Wall, 0, (rooms.PassV + pass + iv1) / 2, t / 2, (iv1 - rooms.PassV - pass) / 2, -Slab, ceiling);
            if (h.Upstairs)
            {
                yield return new(ShellPart.Lintel, 0, rooms.PassV, t / 2, pass, DoorHead, ceiling);
                double up = UpperPassV(h);
                yield return new(ShellPart.Wall, 0, (iv0 + up - pass) / 2, t / 2, (up - pass - iv0) / 2, Storey, top);
                yield return new(ShellPart.Wall, 0, (up + pass + iv1) / 2, t / 2, (iv1 - up - pass) / 2, Storey, top);
                yield return new(ShellPart.Lintel, 0, up, t / 2, pass, Storey + DoorHead, top);
            }
        }
        if (h.Cellar)
        {
            // The boards over the cellar, the hatch left open in them; the cellar's floor under.
            var (hu, hv, _, _) = Hatch(h);
            double hs = HatchOf(h) / 2;
            foreach (var (x, y, wx, wy) in Less(-ix, iv0, ix, iv1, hu - hs, hv - hs, hu + hs, hv + hs))
                yield return new(ShellPart.Floor, x, y, wx, wy, -Slab, 0);
            yield return new(ShellPart.Cellar, 0, dp / 2, ix, (iv1 - iv0) / 2, -Cellar - 0.3, -Cellar);
        }
        if (h.Upstairs)
        {
            var (u0, u1, v0, v1) = Stair(h);
            double um = (u0 + u1) / 2, uh = Math.Abs(u0 - u1) / 2;
            // Each tread a block from the floor to its top: the last rise is onto the upper floor.
            for (int i = 1; i < Rises; i++)
                yield return new(ShellPart.Tread, um, v0 + (i - 0.5) * Tread, uh, Tread / 2, -0.1, Storey * i / Rises);
            // The upper floor, open over the flight (from just short of its foot to its top tread's back edge).
            double w0 = v0 - 0.1;
            foreach (var (x, y, wx, wy) in Less(-ix, iv0, ix, iv1, Math.Min(u0, u1), w0, Math.Max(u0, u1), v1))
                yield return new(ShellPart.Upper, x, y, wx, wy, Storey - Slab, Storey);
            // Its rails: along the well's open side, and across its front end over the flight's foot.
            int s = Math.Sign(u0);
            yield return new(ShellPart.Rail, u1 - s * RailHalf, (w0 + v1) / 2, RailHalf, (v1 - w0) / 2, Storey - Slab, Storey + RailHeight);
            yield return new(ShellPart.Rail, um, w0 - RailHalf, uh, RailHalf, Storey - Slab, Storey + RailHeight);
        }
    }

    /// <summary>
    /// A floored house's ladders, in its own frame: (foot u, v, its foot's and its top's heights, the way off over its top, how
    /// far): the hatch's, down its front edge (off its top onto the boards in front of it), and upstairs the window's (its foot
    /// on the bedroom floor against the wall under the sill, off its top out over the sill onto the porch roof).
    /// </summary>
    public static IEnumerable<(double U, double V, double Foot, double Top, double InU, double InV, double Onto)> Ladders(TownHouse h)
    {
        if (h.Cellar)
        {
            var (hu, hv, inU, inV) = Hatch(h);
            double r = HatchOf(h) / 2 - 0.15;
            yield return (hu + inU * r, hv + inV * r, -Cellar, 0, inU, inV, HatchOnto);
        }
        if (h.Upstairs)
            yield return (WindowU(h), h.Depth - HouseLayout.Wall - 0.15, Storey, Storey + WindowSill + 0.05, 0, 1, WindowOnto);
    }

    /// <summary>
    /// The bedrooms upstairs (note 1022; the director's "rooms furnished for whoever lives there"), heights over the upper
    /// floor: over the parlour a child's cot along the partition, over the kitchen the big bed against the end wall, a
    /// wardrobe against the partition, a washstand by the front corner with its lamp (lit where somebody's home), and the
    /// household's chest at the bed's foot. A house left boarded keeps them under dust sheets (the art's).
    /// </summary>
    public static IReadOnlyList<HouseThing> Furniture(TownHouse h)
    {
        if (!h.Upstairs)
            return [];
        int k = -StairSide(h);
        double w = h.Width / 2, dp = h.Depth;
        return
        [
            new("cot", -k * 0.6, 0.35 + 0.95, 0.42, 0.95, 0.6, true),
            new("bed", k * (w - 0.9), Math.Min(dp * 0.5, dp - 2.2), 0.75, 1.0, 0.65, true),
            new("chest", k * (w - 0.9), Math.Min(dp * 0.5, dp - 2.2) - 1.3, 0.45, 0.22, 0.5, true),
            new("wardrobe", k * 0.42, 1.0, 0.3, 0.5, 2.0, true),
            new("washstand", k * (w - 0.45), 0.5, 0.28, 0.25, 0.85, true),
        ];
    }

    /// <summary>A rectangle less a hole in it (corners), as up to four boxes (middles and half sizes).</summary>
    static IEnumerable<(double X, double Y, double HalfX, double HalfY)> Less(double x0, double y0, double x1, double y1, double hx0, double hy0, double hx1, double hy1)
    {
        hx0 = Math.Max(hx0, x0);
        hx1 = Math.Min(hx1, x1);
        hy0 = Math.Max(hy0, y0);
        hy1 = Math.Min(hy1, y1);
        static (double, double, double, double) Rect(double a, double b, double c, double d) => ((a + c) / 2, (b + d) / 2, (c - a) / 2, (d - b) / 2);
        if (hx0 - x0 > 0.01)
            yield return Rect(x0, y0, hx0, y1);
        if (x1 - hx1 > 0.01)
            yield return Rect(hx1, y0, x1, y1);
        if (hy0 - y0 > 0.01)
            yield return Rect(hx0, y0, hx1, hy0);
        if (y1 - hy1 > 0.01)
            yield return Rect(hx0, hy1, hx1, y1);
    }
}
