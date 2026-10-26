using Ballast;

namespace DarkTerritory.Sim.Run;

/// <summary>How a step between a storeyed house's floors is taken (note 1023): walked on a floor, up or down the flight, or the hatch's ladder.</summary>
public enum FloorLeg : byte { Walk, Stairs, Hatch }

/// <summary>
/// The next leg of a way between a storeyed house's floors (note 1023): what kind of going it is, where it ends (in the world,
/// on the floor or the flight's end it ends at), and the house (an index into <see cref="StopWalls.OpenHouses"/>).
/// </summary>
public readonly record struct FloorWay(FloorLeg Leg, Double3 To, int House);

/// <summary>
/// Getting about a house with floors (queue #726, ARCHITECTURE §8 note 1023; #710's note 1005 built them): which floor a point
/// is on, where the flight and the hatch's ladder are in the world, and the next leg from one point to another when they're on
/// different floors: to the flight's foot, up it, along the upper floor; to the hatch, down its ladder, across the basement;
/// and back. A crewmate upstairs is reached by the flight and nothing else, one in the basement by the hatch (the cellar
/// windows are the crew's way out, not a creature's way in). The house creatures and the creatures that follow a crewmate
/// indoors take these legs (<see cref="Enemies.HouseWays"/>), and so do the bots searching a village's houses (note 326,
/// <see cref="Bots.StopHand"/>). Read from the house's own <see cref="Storeys"/> geometry (<see cref="Stair"/>,
/// <see cref="Hatch"/>, <see cref="StoreyedLadders"/>), so any house built with it is walked the same.
/// </summary>
public sealed partial class StopWalls
{
    /// <summary>How near (flat, m) a leg's start counts as at it: the flight's foot or top, the hatch's ladder.</summary>
    public const double FloorWayAt = 0.1;

    /// <summary>Whether an open house is built with floors (note 1005): the house itself, not a pair's cottage.</summary>
    public static bool Storeyed(in OpenHouse h) => h.Part < 0 && Storeyed(h.B);

    /// <summary>Open house <paramref name="index"/> as the creatures see it (<see cref="OpenHouses"/>' entry), cheaply.</summary>
    public OpenHouse OpenHouseAt(int index)
    {
        var h = _houses[index];
        return new OpenHouse(index, h.Doors.Length > 0 ? (h.Doors[0] - 1) >> 12 : -1, h.Doors.Length > 0 ? ((h.Doors[0] - 1) >> 2) & 0x3FF : -1,
            h.B, h.Origin, h.Ex, h.Ey, h.Doors, h.Part);
    }

    /// <summary>The storeyed open house a world point stands in (any of its floors), or −1.</summary>
    public int StoreyedAt(Double3 p)
    {
        for (int i = 0; i < _houses.Count; i++)
            if (_houses[i].Part < 0 && Storeyed(_houses[i].B) && _houses[i].Holds(p))
                return i;
        return -1;
    }

    /// <summary>A storeyed house's floor's boards in the world (m): −1 its basement's, 0 its ground floor's, 1 upstairs.</summary>
    public static double FloorY(in OpenHouse h, int level) => h.Origin.Y + FloorAt(h.B, level);

    /// <summary>The floor of a storeyed house a height is on: the nearest of the floors it has (−1, 0, 1).</summary>
    public static int LevelOf(in OpenHouse h, double y)
    {
        int best = 0;
        double nearest = Math.Abs(y - FloorY(h, 0));
        foreach (int level in new[] { -1, 1 })
            if ((level < 0 ? h.B.Basement : h.B.Upstairs) && Math.Abs(y - FloorY(h, level)) < nearest)
                (best, nearest) = (level, Math.Abs(y - FloorY(h, level)));
        return best;
    }

    /// <summary>
    /// The flight's two ends in the world (note 1023): its foot, on the ground floor a step short of its first tread, and its
    /// top, on the upper floor a step past its last, both on its middle line; null in a house without an upstairs.
    /// </summary>
    public static (Double3 Foot, Double3 Top)? Flight(in OpenHouse h)
    {
        if (!h.B.Upstairs)
            return null;
        var (x0, dir, y0, y1) = Stair(h.B);
        double ym = (y0 + y1) / 2;
        return (h.World(x0 - dir * 0.5, ym) with { Y = FloorY(h, 0) }, h.World(x0 + dir * ((Rises - 1) * Tread + 0.5), ym) with { Y = FloorY(h, 1) });
    }

    /// <summary>
    /// The hatch's ladder in the world (note 1023): its top at the ground floor's boards and its foot on the basement's floor
    /// (where it stands), and the way off its top into the room with how far; null in a house without a basement.
    /// </summary>
    public static (Double3 Top, Double3 Foot, Double3 Inward, double Onto)? Hatchway(in OpenHouse h)
    {
        if (!h.B.Basement)
            return null;
        var (x, y, foot, top, inX, inY, onto) = StoreyedLadders(h.B).First();
        var at = h.World(x, y);
        var inward = (h.World(x + inX, y + inY) - at) with { Y = 0 };
        return (at with { Y = h.Origin.Y + top }, at with { Y = h.Origin.Y + foot }, inward.Normalized, onto);
    }

    /// <summary>
    /// Where a body on foot in a storeyed house stands (m, in the world): the boards of the floor it's on (by its height,
    /// <paramref name="y"/>); null outside one.
    /// </summary>
    public double? FloorUnder(Double3 p, double y)
    {
        int i = StoreyedAt(p with { Y = y });
        return i < 0 ? null : FloorY(OpenHouseAt(i), LevelOf(OpenHouseAt(i), y));
    }

    /// <summary>Whether two points are on different floors of a storeyed house either stands in (nothing reaches through its boards).</summary>
    public bool FloorsApart(Double3 a, Double3 b)
    {
        int i = StoreyedAt(a);
        if (i < 0)
            i = StoreyedAt(b);
        if (i < 0)
            return false;
        var h = OpenHouseAt(i);
        return LevelOf(h, a.Y) != LevelOf(h, b.Y);
    }

    /// <summary>
    /// The next leg from <paramref name="from"/> toward <paramref name="to"/> where floors are in the way (note 1023), or null
    /// where they aren't: both on one floor of a house, or <paramref name="from"/> outside any storeyed house, or on its
    /// ground floor going out (the doors are on it). From anywhere else in a storeyed house, the floor <paramref name="to"/>
    /// needs is its own in the same house, else the ground floor (to go out by a door); between floors the way is the flight
    /// (ground and upstairs) or the hatch (ground and basement), by the ground floor between the two. On the flight or in the
    /// hatch already, on to the end toward that floor.
    /// </summary>
    public FloorWay? Way(Double3 from, Double3 to)
    {
        int at = StoreyedAt(from);
        if (at < 0)
            return null;
        var h = OpenHouseAt(at);
        int goal = StoreyedAt(to) == at ? LevelOf(h, to.Y) : 0;
        static double Flat(Double3 v) => (v with { Y = 0 }).Length;
        // On the flight (over its treads, between the floors) or in the hatch: on to whichever end is toward the floor wanted.
        if (Flight(h) is var (foot, top) && OnFlight(h, from))
            return new FloorWay(FloorLeg.Stairs, goal >= 1 ? top : foot, at);
        if (Hatchway(h) is var (hTop, hFoot, _, _) && Flat(from - hFoot) < FloorWayAt && from.Y < hTop.Y - 0.1 && from.Y > hFoot.Y + 0.1)
            return new FloorWay(FloorLeg.Hatch, goal >= 0 ? hTop : hFoot, at);
        int level = LevelOf(h, from.Y);
        if (level == goal)
            return null;
        switch (level)
        {
            case 1:
                {
                    var (f, t) = Flight(h)!.Value;
                    return Flat(from - t) > FloorWayAt ? new FloorWay(FloorLeg.Walk, t, at) : new FloorWay(FloorLeg.Stairs, f, at);
                }
            case < 0:
                {
                    var (t, f, _, _) = Hatchway(h)!.Value;
                    return Flat(from - f) > FloorWayAt ? new FloorWay(FloorLeg.Walk, f, at) : new FloorWay(FloorLeg.Hatch, t, at);
                }
            default:
                if (goal > 0)
                {
                    var (f, t) = Flight(h)!.Value;
                    return Flat(from - f) > FloorWayAt ? new FloorWay(FloorLeg.Walk, f, at) : new FloorWay(FloorLeg.Stairs, t, at);
                }
                else
                {
                    var (t, f, _, _) = Hatchway(h)!.Value;
                    return Flat(from - t) > FloorWayAt ? new FloorWay(FloorLeg.Walk, t, at) : new FloorWay(FloorLeg.Hatch, f, at);
                }
        }
    }

    /// <summary>Whether a point is on a storeyed house's flight: over its treads, between its floors.</summary>
    public static bool OnFlight(in OpenHouse h, Double3 p)
    {
        if (!h.B.Upstairs)
            return false;
        var (x0, dir, y0, y1) = Stair(h.B);
        var d = p - h.Origin;
        double x = d.X * h.Ex.X + d.Z * h.Ex.Z, y = d.X * h.Ey.X + d.Z * h.Ey.Z, along = (x - x0) * dir;
        return along > -0.5 && along < (Rises - 1) * Tread + 0.5 && y >= Math.Min(y0, y1) - 0.1 && y <= Math.Max(y0, y1) + 0.1
            && p.Y > FloorY(h, 0) + 0.01 && p.Y < FloorY(h, 1) - 0.01;
    }
}
