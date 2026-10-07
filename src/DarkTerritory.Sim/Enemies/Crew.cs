using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Enemies;

/// <summary>
/// Where the crew are, as the roster reads them (GDD v1.1 App. C.6): groups (players within 8 m of each other, the voice's
/// full-clarity radius), who's alone, who's standing idle, and who's in a coupling gap.
/// </summary>
public static class CrewSense
{
    /// <summary>
    /// The group a player is in: everyone reachable from them in steps of at most <paramref name="radius"/> (App. C.6
    /// "players within 8 m of each other count as a group"), them included.
    /// </summary>
    public static List<int> Group(EnemyContext ctx, int player, double radius)
    {
        var living = ctx.LivingCrew().Select(c => (c.Player.Id, c.World)).ToList();
        var group = new List<int>();
        if (living.All(l => l.Id != player))
            return group;
        var queue = new Queue<int>();
        queue.Enqueue(player);
        group.Add(player);
        while (queue.Count > 0)
        {
            int at = queue.Dequeue();
            var here = living.First(l => l.Id == at).World;
            foreach (var (id, w) in living)
                if (!group.Contains(id) && (w - here).Length <= radius)
                {
                    group.Add(id);
                    queue.Enqueue(id);
                }
        }
        return group;
    }

    /// <summary>Nobody else alive within <paramref name="radius"/> of them.</summary>
    public static bool Alone(EnemyContext ctx, int player, double radius)
    {
        var me = ctx.LivingCrew().FirstOrDefault(c => c.Player.Id == player);
        if (me.Player.State.Death != DeathCause.None || ctx.LivingCrew().All(c => c.Player.Id != player))
            return false;
        return ctx.LivingCrew().All(c => c.Player.Id == player || (c.World - me.World).Length > radius);
    }

    /// <summary>Living players within <paramref name="radius"/> of a point.</summary>
    public static int Near(EnemyContext ctx, Double3 at, double radius) => ctx.LivingCrew().Count(c => (c.World - at).Length <= radius);

    /// <summary>
    /// Whether a player is looking at a point: within <paramref name="degrees"/> of where they face, level (Tippy Toesie
    /// "visible to anyone facing it", the Whistler found in its gap).
    /// </summary>
    public static bool Facing(in PlayerState s, TrainOnLine train, Double3 at, double degrees)
    {
        var eye = PlayerMotor.WorldPosition(s, train);
        var to = (at - eye) with { Y = 0 };
        if (to.Length < 1e-6)
            return true;
        double yaw = PlayerMotor.WorldYaw(s, train);
        var facing = new Double3(-DMath.Sin(yaw), 0, -DMath.Cos(yaw));
        return Double3.Dot(facing, to.Normalized) >= DMath.Cos(degrees * Math.PI / 180);
    }

    /// <summary>The middle of the coupling gap behind a vehicle, in that vehicle's frame.</summary>
    public static Double3 GapLocal(TrainOnLine train, int car) =>
        new(0, 0.6, train.Frames[car].Shape.HalfLength + train.Dynamics.Tuning.Geometry.CouplingGap * 0.5);

    /// <summary>
    /// Whether a point is in the gap behind a vehicle: between the two cars' ends, no wider than the cars, below their roofs
    /// (over the roofs is the way round it). The ground between the cars at a stop counts; that's the gap too.
    /// </summary>
    public static bool InGap(Double3 world, TrainOnLine train, int car)
    {
        if (car < 0 || car >= train.Frames.Count)
            return false;
        var frame = train.Frames[car];
        var local = frame.ToLocal(world);
        double half = frame.Shape.HalfLength;
        double far = half + train.Dynamics.Tuning.Geometry.CouplingGap;
        return Math.Abs(local.X) <= frame.Shape.HalfWidth && local.Z >= half && local.Z <= far
            && local.Y < frame.Shape.RoofHeight - 0.5 && local.Y > -1.5;
    }

    /// <summary>The coupling gaps in the engine's rake, as the vehicle ahead of each, the engine's own left out (the cab's).</summary>
    public static List<int> Gaps(TrainOnLine train)
    {
        var rake = train.Dynamics.Consist.Vehicles;
        var gaps = new List<int>();
        for (int i = 1; i + 1 < rake.Count; i++)
            gaps.Add(rake[i].Id);
        return gaps;
    }

    /// <summary>A player on the ground (off the train: the yards, the facilities, beside a stopped train).</summary>
    public static bool OnGround(in PlayerState s) => s.Alive && s.Parent == PlayerState.World;

    /// <summary>Someone living is inside that car (standing in its room, doors shut or not).</summary>
    public static bool Occupied(EnemyContext ctx, int car) =>
        ctx.Crew.Any(c => c.Player.State is { Alive: true, Surface: Surface.Deck } s && s.Parent == car
            && ctx.Train.Frames[car].Shape.Interior is { } room && room.Contains(s.Position));
}
