using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The director's decision of 2026-10-06 (GDD App. F.1: "I clipped right through a mountain ... in the world we're always
/// going to have to be able to interact with basically everything"; note 266): what carries a player off, and every creature
/// on foot, keeps to the ground and the solids. Over a hilly stretch of a generated line, a Whistler's carry, a Ribbit pack's
/// chase and a hound pack's run never leave the ground or pass through anything.
/// </summary>
public class GroundCarryTests
{
    static readonly PlayerTuning P = Tuning.Player;
    const double Tolerance = 0.05;

    static RailLine? _line;
    static RailLine Line => _line ??= LineGen.Routes.Generate(DataFile.FindContentRoot(), "deepTerritory:1", 6).Build();

    static double Ground(RailLine line, Double3 at, double hint) => PlayerMotor.GroundAt(at, line, ref hint);

    /// <summary>A stretch where the land 15-40 m off the track rises or falls well away from the rail on at least one side.</summary>
    static double Hilly(RailLine line)
    {
        double best = 0, at = 3_000;
        for (double s = 3_000; s < line.Length - 3_000; s += 100)
        {
            var t = line.Sample(s);
            var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
            double relief = 0;
            foreach (int side in (int[])[-1, 1])
                for (double d = 15; d <= 40; d += 5)
                    relief = Math.Max(relief, Math.Abs(Ground(line, t.Position + right * side * d, s) - t.Position.Y));
            if (relief > best)
            {
                best = relief;
                at = s;
            }
        }
        Assert.True(best > 4, $"a hilly stretch to try it on ({best:0.0} m of relief at best)");
        return at;
    }

    /// <summary>On the ground, and out of every solid (pushing it out of them would move it).</summary>
    static void OnTheGround(TrainOnLine train, Double3 at, double over, string what)
    {
        double ground = Ground(train.Line, at, train.Dynamics.Distance);
        Assert.True(Math.Abs(at.Y - over - ground) <= Tolerance, $"{what} {at.Y - over - ground:0.00} m off the ground at {at}");
        var clear = PlayerMotor.Clear(at - Double3.Up * over, train, new PlayerMotor.Cylinder(0.3, 1.0, 0.35));
        Assert.True(((clear - (at - Double3.Up * over)) with { Y = 0 }).Length <= 0.15, $"{what} inside a solid at {at}");
    }

    [Fact]
    public void AWhistlersCarryOverHillyGroundKeepsToTheGroundAndEndsWhereARescuerCanFollow()
    {
        var line = Line;
        double s = Hilly(line);
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 4, 1)), line, s + 30, Tuning.Boiler);
        var world = new World(train, Tuning.Combat);
        world.EnableBodies();
        var route = LineGen.Routes.Generate(DataFile.FindContentRoot(), "deepTerritory:1", 6);
        world.EnableEnemies(Tuning.Enemies, route, 1, 2, authority: true);
        world.Insist = [];
        int gap = CrewSense.Gaps(train)[0];
        var w = world.AddEnemy(id => Whistler.InGap(id, train, gap, Tuning.Enemies.Whistler));
        // Standing in its gap, alone, on the ground under the couplers.
        var at = train.Frames[gap].ToWorld(CrewSense.GapLocal(train, gap));
        double hint = s;
        var them = PlayerMotor.SpawnOnGround(at, line, hint, P);
        bool grabbed = false;
        int carried = 0;
        double rail = line.Sample(s).Position.Y;
        for (int i = 0; i < 40 * SimConstants.TickRate && !w.Gone; i++)
        {
            world.BeginTick();
            world.CrewAct(ref them, default, 1);
            world.Step(new TrainControls { Reverser = 1, Brake = 1 });
            train.Dynamics.Velocity = 0;
            world.ApplyDamage(_ => them, (_, x) => them = x, [1]);
            PlayerMotor.Step(ref them, default, train, P, Tuning.Train, SimConstants.TickSeconds, applyLook: false);
            if (w.Phase == SpinePhase.Grab)
            {
                grabbed = true;
                carried++;
                OnTheGround(train, w.WorldPosition(train), 0, "the Whistler");
                OnTheGround(train, PlayerMotor.WorldPosition(them, train), 0, "who it carries");
            }
        }
        Assert.True(grabbed, "it snatched them");
        Assert.True(carried > SimConstants.TickRate * 5, "and carried them off");
        // Its den: where a rescuer can run (within its climb of the rail).
        Assert.True(Math.Abs(w.WorldPosition(train).Y - rail) <= Tuning.Enemies.Whistler.NestClimb + 0.5, $"the den {w.WorldPosition(train).Y - rail:0.0} m off the rail");
    }

    [Fact]
    public void ARibbitPackChasingOverHillyGroundAndAHoundPackRunningBesideTheLineKeepToTheGround()
    {
        var line = Line;
        double s = Hilly(line);
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 4, 1)), line, s - 200, Tuning.Boiler);
        var world = new World(train, Tuning.Combat);
        world.EnableBodies();
        var route = LineGen.Routes.Generate(DataFile.FindContentRoot(), "deepTerritory:1", 6);
        world.EnableEnemies(Tuning.Enemies, route, 1, 2, authority: true);
        world.Insist = [];
        // A lone player on the line at the hilly stretch, a pack 40 m off on whichever side has the most relief.
        var t = line.Sample(s);
        var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
        int side = Math.Abs(Ground(line, t.Position + right * 30, s) - t.Position.Y) >= Math.Abs(Ground(line, t.Position - right * 30, s) - t.Position.Y) ? 1 : -1;
        var them = PlayerMotor.SpawnOnGround(t.Position, line, s, P);
        int pack = world.NextEnemyId;
        for (int i = 0; i < 3; i++)
        {
            var spot = t.Position + right * side * (40 + i) + t.Tangent * i;
            world.AddEnemy(id => Ribbit.At(id, pack, spot, Tuning.Enemies.Ribbits));
        }
        int hounds = world.NextEnemyId;
        for (int i = 0; i < 3; i++)
        {
            int n = i;
            world.AddEnemy(id => new CinderHound(id, hounds) { LineDistance = train.Dynamics.RearDistance - 60 - n * 6, Lateral = (n % 2 == 0 ? 1 : -1) * 5, Height = 0.6, Health = 3 });
        }
        int checks = 0;
        for (int i = 0; i < 20 * SimConstants.TickRate; i++)
        {
            world.BeginTick();
            world.CrewAct(ref them, default, 1);
            world.Step(new TrainControls { Reverser = 1 });
            train.Dynamics.Velocity = 10;
            world.ApplyDamage(_ => them, (_, x) => them = x, [1]);
            PlayerMotor.Step(ref them, default, train, P, Tuning.Train, SimConstants.TickSeconds, applyLook: false);
            foreach (var e in world.ActiveEnemies.Where(e => !e.Gone))
            {
                if (e.Kind == EnemyKind.Ribbit)
                    OnTheGround(train, e.WorldPosition(train), 0, $"Ribbit {e.Id} ({e.Phase})");
                else if (e.Kind == EnemyKind.CinderHound && e.Attached < 0)
                    OnTheGround(train, e.WorldPosition(train), 0.6, $"hound {e.Id}");
                else
                    continue;
                checks++;
            }
        }
        Assert.True(checks > 1000, $"checked {checks}");
    }
}
