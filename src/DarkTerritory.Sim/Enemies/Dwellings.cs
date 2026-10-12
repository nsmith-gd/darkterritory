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
                foreach (var house in byStop.OrderBy(h => h.Index))
                    if (Lives(route, stop, house, dt, director) is { } kind)
                        into.Add(Put(kind, nextId++, house, walls, t));
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
    public static EnemyKind? Lives(Route.Route route, Stops.StopLayout stop, StopWalls.OpenHouse house, DwellingsTuning t, Director? director)
    {
        if (stop.Lairs.Any(l => l.Kind == Stops.LairKind.GauntRoost && l.Building == house.Building))
            return null;
        var dice = new Pcg32(route.Seed ^ ((ulong)(uint)house.Feature << 20 | (uint)house.Building), 0x44_5745_4C4CUL);
        if (dice.NextDouble() >= t.ShareFor(route.Tier))
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
        foreach (var (kind, weight) in kinds)
        {
            if (roll < weight)
                return kind;
            roll -= weight;
        }
        return kinds[^1].Kind;
    }

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
    public static Double3 Toward(StopWalls walls, Double3 from, Double3 to)
    {
        int inside = walls.HouseAt(from), goal = walls.HouseAt(to);
        if (inside >= 0 && inside != goal && Door(walls, inside, from) is { } exit)
            return Flat(from - exit.At) > 0.4 ? exit.At - exit.Out * 0.3 : exit.At + exit.Out * 1.2;
        if (goal >= 0 && goal != inside && Door(walls, goal, from) is { } entry)
            return Flat(from - entry.At) > 0.6 ? entry.At + entry.Out * 0.6 : entry.At - entry.Out * 1.0;
        return to;
    }

    /// <summary>House <paramref name="house"/>'s door nearest <paramref name="at"/> (by key on a tie), or null for one with none.</summary>
    public static HouseDoor? Door(StopWalls walls, int house, Double3 at) =>
        walls.HouseDoors.Where(d => d.House == house).OrderBy(d => Flat(d.At - at)).ThenBy(d => d.Key).Select(d => (HouseDoor?)d).FirstOrDefault();

    /// <summary>Whether a wall (a shut door among them) stands between two points on the ground, checked every 0.1 m along.</summary>
    public static bool Blocked(StopWalls walls, Double3 a, Double3 b)
    {
        var d = (b - a) with { Y = 0 };
        double length = d.Length;
        int steps = Math.Max(1, (int)(length / 0.1));
        for (int i = 1; i < steps; i++)
        {
            var p = a + d * ((double)i / steps) + Double3.Up * 1.0;
            foreach (var w in walls.Near(p))
            {
                if (w.Fort)
                    continue;
                var l = w.ToLocal(p);
                if (Math.Abs(l.X) <= w.HalfLength && Math.Abs(l.Z) <= w.HalfWidth && l.Y >= w.Bottom && l.Y <= w.Top)
                    return true;
            }
        }
        return false;
    }

    /// <summary>A step along the ground toward <paramref name="to"/> at <paramref name="speed"/>; true once there.</summary>
    public static bool Step(Enemy e, EnemyContext ctx, Double3 to, double speed, ref double lineHint)
    {
        var way = (to - e.Local) with { Y = 0 };
        double step = speed * SimConstants.TickSeconds;
        var next = way.Length <= step ? to : e.Local + way.Normalized * step;
        e.Local = next with { Y = PlayerMotor.GroundAt(next, ctx.Train.Line, ref lineHint) };
        if (way.Length > 1e-6)
            e.Lateral = Yaw(way);
        return way.Length <= step;
    }

    public static double Flat(Double3 v) => (v with { Y = 0 }).Length;

    /// <summary>A heading for a way along the ground, as a player's yaw (−Z forward).</summary>
    public static double Yaw(Double3 way) => DMath.Atan2(-way.X, -way.Z);
}
