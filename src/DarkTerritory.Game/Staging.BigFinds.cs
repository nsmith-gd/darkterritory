using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Stops;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game;

/// <summary>
/// The big finds staged (queue #453, ARCHITECTURE §8 note 719): two crewmates carrying a safe off the lineside (<c>jackpot</c>);
/// a stop's jackpot house from its street, its better door locked and a lamp in its window, or inside it opened (<c>jackpothouse</c>,
/// <c>--inside</c>); and the finds put down in a car's middle, slid after a bend at speed (<c>looseloot</c>).
/// </summary>
public static partial class Staging
{
    /// <summary>The jackpot view's crewmates: either end of the safe.</summary>
    public const byte JackpotFront = 1, JackpotBack = 2;

    /// <summary>
    /// The loot with every stop of the night given a jackpot house and a jackpot in it (<paramref name="item"/>, or the tuning's
    /// own), for the views (the night's seed decides which house; every stop has one).
    /// </summary>
    public static LootTuning Jackpotted(LootTuning loot, Sim.Route.RouteTier tier, string? item = null) => loot with
    {
        JackpotHouse = loot.JackpotHouse is { } jh ? jh with { Chance = new() { [StopLoot.TierKey(tier)] = 1 } } : null,
        Jackpots = loot.Jackpots is { } jp ? jp with
        {
            House = 1,
            Items = item is not null && jp.Items.TryGetValue(item, out var only) ? new() { [item] = only } : jp.Items,
        } : null,
    };

    /// <summary>
    /// Two crewmates carrying a jackpot (note 719; the director: "Very big value things should take two to carry") on the
    /// ground off the line's left, ahead of the engine, walking it back toward the train: <paramref name="owner"/>'s find, a
    /// heavy crate's ball of <paramref name="radius"/>, between their hands. The camera's beside them.
    /// </summary>
    public static (Sim.Physics.Bodies Bodies, List<Crewmate> Crew, Camera Camera) Jackpot(TrainOnLine train, int owner, double radius)
    {
        var bodies = new Sim.Physics.Bodies();
        var middle = Lineside(train, 9, -4.2);
        var t = train.Line.Sample(train.Dynamics.Distance + 9);
        var along = new Double3(t.Tangent.X, 0, t.Tangent.Z).Normalized;
        var safe = bodies.SpawnCargo(middle + Double3.Up * 0.62, train.Dynamics.Distance + 9, radius);
        safe.Owner = owner;
        safe.Carrier = JackpotFront;
        safe.Second = JackpotBack;
        safe.Yaw = Math.Atan2(-along.X, -along.Z) + Math.PI / 2;
        safe.Pbd.Particles[0].Position = safe.Pbd.Particles[0].Previous = middle + Double3.Up * 0.95;
        // Each a step and a bit from its middle along the line, facing it, the one toward the train walking backwards.
        Crewmate End(byte id, int sign)
        {
            var feet = OnGround(train, middle + along * (sign * (radius + 0.55)));
            var toward = middle - feet;
            return new Crewmate(id, feet, Math.Atan2(-toward.X, -toward.Z), true, Act: Art.CrewPose.Carry);
        }
        List<Crewmate> crew = [End(JackpotFront, 1), End(JackpotBack, -1)];
        var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
        var camera = Camera.LookAt(middle - right * 4.6 + along * 1.2 + Double3.Up * 1.55, middle + Double3.Up * 0.85, 52);
        return (bodies, crew, camera);
    }

    /// <summary>
    /// The jackpot house (note 719) from its street: stood back from its door and a little to the side of it, its better door and
    /// the lamp in its window, the house's front in the frame; <paramref name="inside"/>, stood just in its doorway looking in
    /// at what it keeps.
    /// </summary>
    public static Camera JackpotHouse(Sim.Run.StopWalls walls, int door, bool inside)
    {
        var d = walls.HouseDoors.First(x => x.Key == door);
        var side = Double3.Cross(Double3.Up, d.Out);
        var house = walls.OpenHouses.First(h => h.Index == d.House);
        if (inside)
            return Camera.LookAt(d.At - d.Out * 0.6 + Double3.Up * 1.65, house.Middle + Double3.Up * 0.2, 75);
        return Camera.LookAt(d.At + d.Out * 7.5 + side * 2.2 + Double3.Up * 1.6, d.At + side * 0.9 + Double3.Up * 1.4, 55);
    }

    /// <summary>
    /// A bend the looseloot view rides round (note 719): the tightest stretch of <paramref name="line"/> between its first and last
    /// 2 km, judged by the least curving over 200 m of it, one way all along; the engine's front 60 m past its middle, so the
    /// cars behind it are on it.
    /// </summary>
    public static double Bend(RailLine line)
    {
        double best = line.Length / 2, tightest = 0;
        for (double s = 2000; s < line.Length - 2000; s += 20)
        {
            double sign = Math.Sign(line.Sample(s).Curvature), least = double.MaxValue;
            for (int i = 0; i <= 20 && least > tightest; i++)
                least = Math.Min(least, line.Sample(s - 100 + i * 10).Curvature * sign);
            if (sign != 0 && least > tightest)
                (best, tightest) = (s + 60, least);
        }
        return best;
    }
}
