using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Sim.Enemies;

/// <summary>
/// Which of a stop's open houses something lives in (GDD §21; the director, 9 Oct 2026: "Every time I go into a house, it
/// should feel like there could be something lurking in there that wants to kill me, wants to chase me"; notes 583–586).
/// Once the train's within <c>placeWithin</c> of a stop, each of its open houses rolls its own dice (the route's seed, the
/// stop and the building, so nothing else's change): <c>share</c> of them by tier have something in them, never the Gaunt's
/// roost, what by <c>weights</c> (a Householder only where there's something to find). Put in asleep; gone once the train's
/// <c>forgetPast</c> past the stop. On the host, once a second. Not the director's: they cost it nothing.
/// </summary>
public sealed class Dwelling
{
    readonly SortedSet<int> _placed = [], _forgotten = [];

    public void Step(World world, EnemyTuning t, Director director, ref int nextId, List<Enemy> into)
    {
        var dt = t.Dwellings;
        if (world.Route is not { } route || world.Train.Walls is not { } walls)
            return;
        double front = world.Train.Dynamics.Distance;
        foreach (var byStop in walls.OpenHouses.Where(h => h.Feature >= 0 && h.Feature < route.Features.Count).GroupBy(h => h.Feature).OrderBy(g => g.Key))
        {
            var f = route.Features[byStop.Key];
            if (f.Stop is not { } stop)
                continue;
            if (!_placed.Contains(byStop.Key) && front >= f.Start - dt.PlaceWithin && front <= f.End + dt.ForgetPast)
            {
                _placed.Add(byStop.Key);
                // Nothing new on the run in to the terminus (note 912): a stop there keeps its houses empty.
                if (!dt.InHomeStretch && (world.InHomeStretch(f.Start) || world.InHomeStretch(f.End)))
                    continue;
                foreach (var house in byStop.OrderBy(h => h.Index))
                    // Not about tonight, or its night's cap reached (note 644; the director's Creature Tweaker): the house
                    // stands empty.
                    if (Lives(route, stop, house, dt, director, world.Run?.LockedAt(house.Feature) == house.Building) is { } kind && director.Tonight(kind, world))
                    {
                        into.Add(Put(kind, nextId++, house, walls, t));
                        director.Counted(kind);
                    }
            }
            if (_placed.Contains(byStop.Key) && !_forgotten.Contains(byStop.Key) && front > f.End + dt.ForgetPast)
            {
                _forgotten.Add(byStop.Key);
                var houses = byStop.Select(h => h.Index).ToHashSet();
                foreach (var e in into.Where(e => !e.Gone && e.Kind is EnemyKind.Lodger or EnemyKind.Householder or EnemyKind.HollowHouse or EnemyKind.Hanger
                    && houses.Contains((int)e.Extra2)))
                    e.Dismiss();
            }
        }
    }

    /// <summary>What lives in <paramref name="house"/>, from its own dice, or null for nothing.</summary>
    /// <param name="jackpot">It's the stop's jackpot house (note 719): its share's times <c>jackpotShare</c>, never past 1.</param>
    public static EnemyKind? Lives(Route.Route route, Stops.StopLayout stop, StopWalls.OpenHouse house, DwellingsTuning t, Director? director, bool jackpot = false)
    {
        if (stop.Lairs.Any(l => l.Kind == Stops.LairKind.GauntRoost && l.Building == house.Building))
            return null;
        var dice = new Pcg32(route.Seed ^ ((ulong)(uint)house.Feature << 20 | (uint)house.Building), 0x44_5745_4C4CUL);
        if (dice.NextDouble() >= Math.Min(1, t.ShareFor(route.Tier) * (jackpot ? t.JackpotShare : 1)))
            return null;
        bool finds = stop.Containers.Any(c => c.Building == house.Building);
        var kinds = new (EnemyKind Kind, string Key)[] { (EnemyKind.Lodger, "lodger"), (EnemyKind.Householder, "householder"), (EnemyKind.HollowHouse, "hollowHouse"), (EnemyKind.Hanger, "hanger") }
            .Where(k => director?.Allows(k.Kind) ?? true)
            .Where(k => k.Kind != EnemyKind.Householder || finds)
            .Select(k => (k.Kind, Weight: t.Weights.GetValueOrDefault(k.Key)))
            .Where(k => k.Weight > 0).ToList();
        double sum = kinds.Sum(k => k.Weight);
        if (sum <= 0)
            return null;
        double roll = dice.NextDouble() * sum;
        var drawn = kinds[^1].Kind;
        foreach (var (kind, weight) in kinds)
        {
            if (roll < weight)
            {
                drawn = kind;
                break;
            }
            roll -= weight;
        }
        // A house with floors (note 1023): the kinds that live on floors, as its dice drew them (each kind as often as in a house
        // of one floor); another drawn, it stands empty.
        return StopWalls.Storeyed(house) && !OnFloors(drawn, t) ? null : drawn;
    }

    /// <summary>Whether a house creature lives in houses with floors (enemies.json dwellings.storeyed, note 1023).</summary>
    public static bool OnFloors(EnemyKind kind, DwellingsTuning t) => kind switch
    {
        EnemyKind.Lodger => t.Storeyed.Contains("lodger"),
        EnemyKind.Householder => t.Storeyed.Contains("householder"),
        EnemyKind.HollowHouse => t.Storeyed.Contains("hollowHouse"),
        EnemyKind.Hanger => t.Storeyed.Contains("hanger"),
        _ => false,
    };

    static Enemy Put(EnemyKind kind, int id, StopWalls.OpenHouse house, StopWalls walls, EnemyTuning t) => kind switch
    {
        EnemyKind.Lodger => Lodger.In(id, house, walls, t.Lodger),
        EnemyKind.Householder => Householder.In(id, house, walls, t.Householder),
        EnemyKind.HollowHouse => HollowHouse.In(id, house),
        _ => Hanger.In(id, house, t.Hanger),
    };
}

/// <summary>Getting about the houses for the creatures that live in them: by their doors, and what stands between.</summary>
public static class HouseWays
{
    /// <summary>
    /// Where to head for, from <paramref name="from"/> to get to <paramref name="to"/>: out of the house it's in by that house's
    /// nearest door (a step outside it), into the house <paramref name="to"/> is in by that house's nearest door (a step inside),
    /// else straight there.
    /// </summary>
    /// <remarks>
    /// In a house with floors (note 1023) the floors come first: from upstairs or the basement the way is to the flight or
    /// the hatch, and down or up it, before the doors (they're on the ground floor); and into one, in at its door, then to
    /// the floor <paramref name="to"/> is on (<see cref="StopWalls.Way"/>).
    /// </remarks>
    public static Double3 Toward(StopWalls walls, Double3 from, Double3 to)
    {
        if (walls.Way(from, to) is { } leg)
            return leg.To;
        int inside = walls.HouseAt(from), goal = walls.HouseAt(to);
        if (inside >= 0 && inside != goal && Door(walls, inside, from) is { } exit)
            return Flat(from - exit.At) > 0.4 ? exit.At - exit.Out * 0.3 : exit.At + exit.Out * 1.2;
        // Note 1023: just out of a door, on out clear of its step before turning (turned at once back past the house, it was in
        // again through the wall, and led out by the door again, for good: a Householder hunting its thing to a house behind its
        // own never got away from its door).
        if (inside < 0 && walls.DoorInReach(from, 1.3) is { } step && step.House != goal
            && Double3.Dot((from - step.At) with { Y = 0 }, step.Out) is > -0.05 and < 1.1)
            return step.At + step.Out * 1.2;
        var aim = goal >= 0 && goal != inside && Door(walls, goal, from) is { } entry
            // (In from its step once at it: 0.6 m out, give or take a rounding, had it stood there for good.)
            ? Flat(from - entry.At) > 0.7 ? entry.At + entry.Out * 0.6 : entry.At - entry.Out * 1.0
            : to;
        return inside < 0 ? Round(walls, from, aim, goal) : aim;
    }

    /// <summary>How far out round a house's corner (m) a creature on foot goes past it.</summary>
    const double RoundBy = 0.8;

    /// <summary>
    /// Out in the open, from <paramref name="from"/> toward <paramref name="aim"/> (note 1023): straight there, unless a house
    /// (not <paramref name="goal"/>) stands in the way; then to whichever of its corners, a little out from it, makes the
    /// shortest way round that's clear of it from here.
    /// </summary>
    static Double3 Round(StopWalls walls, Double3 from, Double3 aim, int goal)
    {
        if (InTheWay(walls, from, aim, goal) is not (var house and >= 0))
            return aim;
        var h = walls.OpenHouseAt(house);
        double hx = h.B.Length / 2 + RoundBy, hy = h.B.Width / 2 + RoundBy;
        Double3? best = null;
        double shortest = double.MaxValue;
        foreach (var (x, y) in new[] { (hx, hy), (hx, -hy), (-hx, -hy), (-hx, hy) })
        {
            var corner = h.World(x, y) with { Y = from.Y };
            double length = Flat(corner - from) + Flat(aim - corner);
            if (length < shortest && Flat(corner - from) > 0.2 && InTheWay(walls, from, corner, goal) != house)
                (best, shortest) = (corner, length);
        }
        return best ?? aim;
    }

    /// <summary>The first open house (not <paramref name="goal"/>) on the ground between two points, every 0.5 m along, or −1.</summary>
    static int InTheWay(StopWalls walls, Double3 a, Double3 b, int goal)
    {
        var d = (b - a) with { Y = 0 };
        int steps = (int)(d.Length / 0.5);
        for (int i = 1; i < steps; i++)
        {
            var p = a + d * ((double)i / steps) + Double3.Up * 0.5;
            if (walls.HouseAt(p) is var h and >= 0 && h != goal)
                return h;
        }
        return -1;
    }

    /// <summary>House <paramref name="house"/>'s door nearest <paramref name="at"/> (by key on a tie), or null for one with none.</summary>
    public static HouseDoor? Door(StopWalls walls, int house, Double3 at) =>
        walls.HouseDoors.Where(d => d.House == house).OrderBy(d => Flat(d.At - at)).ThenBy(d => d.Key).Select(d => (HouseDoor?)d).FirstOrDefault();

    /// <summary>
    /// Whether a wall (a shut door among them) stands between two points on the ground, checked every 0.1 m along; or, in a
    /// house with floors, a floor (note 1023: they're on different ones, and nothing reaches through the boards).
    /// </summary>
    public static bool Blocked(StopWalls walls, Double3 a, Double3 b)
    {
        if (walls.FloorsApart(a, b))
            return true;
        var d = (b - a) with { Y = 0 };
        double length = d.Length;
        int steps = Math.Max(1, (int)(length / 0.1));
        for (int i = 1; i < steps; i++)
        {
            var p = a + d * ((double)i / steps) + Double3.Up * 1.0;
            foreach (var w in walls.Near(p))
            {
                if (w.Fort || w.Trestle)
                    continue;
                var l = w.ToLocal(p);
                if (Math.Abs(l.X) <= w.HalfLength && Math.Abs(l.Z) <= w.HalfWidth && l.Y >= w.Bottom && l.Y <= w.Top)
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// A step along the ground toward <paramref name="to"/> at <paramref name="speed"/>; true once there. In a house with
    /// floors (note 1023) it walks the boards of the floor it's on, and a leg up or down the flight (at enemies.json
    /// dwellings.stairsPace of its pace) or the hatch's ladder (at hatchClimb m/s) it takes as it goes, never through a floor.
    /// </summary>
    public static bool Step(Enemy e, EnemyContext ctx, Double3 to, double speed, ref double lineHint)
    {
        bool between = false;
        if (ctx.Train.Walls is { } walls && walls.Way(e.Local, to) is { } first)
        {
            // To the flight or the hatch first, on this floor.
            between = true;
            if (first.Leg == FloorLeg.Walk)
                to = first.To;
        }
        if (between && ctx.Train.Walls!.Way(e.Local, to) is { Leg: not FloorLeg.Walk } leg)
        {
            // Up or down the flight, or the hatch: straight along it, from end to end.
            var dw = ctx.Tuning.Dwellings;
            var along = leg.To - e.Local;
            double go = (leg.Leg == FloorLeg.Hatch ? dw.HatchClimb : speed * dw.StairsPace) * SimConstants.TickSeconds;
            e.Local = along.Length <= go ? leg.To : e.Local + along.Normalized * go;
            if (Flat(along) > 1e-6)
                e.Lateral = Yaw(along);
            return false;
        }
        var way = (to - e.Local) with { Y = 0 };
        double step = speed * SimConstants.TickSeconds;
        var next = way.Length <= step ? to : e.Local + way.Normalized * step;
        e.Local = next with { Y = Afoot(ctx, next, e.Local.Y, ref lineHint) };
        if (way.Length > 1e-6)
            e.Lateral = Yaw(way);
        return !between && way.Length <= step && (ctx.Train.Walls is not { } w || !w.FloorsApart(e.Local, to));
    }

    /// <summary>
    /// The height a creature on foot at <paramref name="at"/> stands at: in a house with floors, the boards of the floor it's
    /// on (by <paramref name="y"/>, its height now; note 1023), else the ground.
    /// </summary>
    public static double Afoot(EnemyContext ctx, Double3 at, double y, ref double lineHint) =>
        ctx.Train.Walls?.FloorUnder(at, y) ?? PlayerMotor.GroundAt(at, ctx.Train.Line, ref lineHint);

    public static double Flat(Double3 v) => (v with { Y = 0 }).Length;

    /// <summary>A heading for a way along the ground, as a player's yaw (−Z forward).</summary>
    public static double Yaw(Double3 way) => DMath.Atan2(-way.X, -way.Z);
}
