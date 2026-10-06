using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The forts are safe (the director's decision of 2026-10-06, GDD App. F.1 and §9; note 266): nothing is sent inside one,
/// nothing follows anyone in, and the director spends nothing while the train's there.
/// </summary>
public class FortTests
{
    static readonly PlayerTuning P = Tuning.Player;

    static (World World, TrainOnLine Train, Route.Route Route, double Gate) Home(double trainAt, bool director)
    {
        var route = RouteGenerator.Generate(Tuning.Route, RouteTier.Frontier, 1);
        double gate = route.GateOr(Tuning.Route.YardLength);
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 4, 1)), route.Build(), trainAt, Tuning.Boiler);
        var world = new World(train, Tuning.Combat);
        world.EnableBodies();
        world.EnableEnemies(Tuning.Enemies, route, 3, 2, authority: true);
        if (!director)
            world.Insist = [];
        return (world, train, route, gate);
    }

    static Double3 Beside(TrainOnLine train, double along, double lateral)
    {
        var t = train.Line.Sample(along);
        var at = t.Position + Double3.Cross(t.Tangent, Double3.Up).Normalized * lateral;
        double hint = along;
        return at with { Y = PlayerMotor.GroundAt(at, train.Line, ref hint) };
    }

    [Fact]
    public void TheFortsAreTheHomeYardAndTheTerminusInsideTheirWalls()
    {
        var (world, train, route, gate) = Home(3_000, director: false);
        var forts = world.Forts!;
        Assert.Equal(2, forts.All.Count);
        Assert.True(forts.Inside(Beside(train, gate - 50, 0)));
        Assert.True(forts.Inside(Beside(train, gate - 50, 12)));
        Assert.False(forts.Inside(Beside(train, gate - 50, 25)), "outside the walls");
        Assert.False(forts.Inside(Beside(train, gate + 20, 0)), "out past the gate");
        Assert.True(forts.Inside(Beside(train, route.Length - 100, 0)), "the terminus");
        Assert.False(forts.Inside(Beside(train, route.Length / 2, 0)));
        // Put back out of one: past the gate.
        var out_ = forts.Outside(Beside(train, gate - 3, 0));
        Assert.False(forts.Inside(out_));
    }

    [Fact]
    public void AnEnemyChasingAPlayerIntoAFortStopsAtTheGate()
    {
        var (world, train, _, gate) = Home(3_000, director: false);
        var forts = world.Forts!;
        // On foot out past the home gate, running for it, a pack close behind.
        var s = PlayerMotor.SpawnOnGround(Beside(train, gate + 25, 2), train.Line, gate + 25, P);
        var toward = train.Line.Sample(gate).Tangent * -1;
        s.Yaw = Math.Atan2(-toward.X, -toward.Z);
        int pack = world.NextEnemyId;
        for (int i = 0; i < 3; i++)
        {
            var at = Beside(train, gate + 34 + i, 2 + i);
            world.AddEnemy(id => Ribbit.At(id, pack, at, Tuning.Enemies.Ribbits));
        }
        bool inside = false, grabbed = false;
        double deepest = 0;
        for (int t = 0; t < 30 * SimConstants.TickRate; t++)
        {
            world.BeginTick();
            var intent = new PlayerIntent { MoveZ = 1, Buttons = PlayerButtons.Run };
            world.CrewAct(ref s, intent, 1);
            world.Step(new TrainControls { Reverser = 1 });
            world.ApplyDamage(id => s, (_, x) => s = x, [1]);
            PlayerMotor.Step(ref s, intent, train, P, Tuning.Train, SimConstants.TickSeconds, applyLook: false);
            // In, they stop running 40 m inside.
            double hint = gate;
            var (_, along) = train.Line.Nearest(PlayerMotor.WorldPosition(s, train), ref hint);
            if (along < gate - 40)
                s = s with { Velocity = default };
            deepest = Math.Max(deepest, gate - along);
            foreach (var e in world.ActiveEnemies.Where(e => !e.Gone && e.Kind == EnemyKind.Ribbit))
                inside |= forts.Inside(e.WorldPosition(train));
            grabbed |= s.Has(PlayerFlags.Held);
        }
        Assert.True(deepest > 20, "they got in");
        Assert.False(inside, "a Ribbit got inside the walls");
        Assert.False(grabbed);
        // At the walls, they lost interest and went; any that fell further back sit there in the dark, after nobody.
        Assert.All(world.ActiveEnemies.OfType<Ribbit>().Where(e => !e.Gone), e => Assert.Null(e.Target));
    }

    [Fact]
    public void SomethingPutInsideIsPutBackOutAndGoes()
    {
        var (world, train, _, gate) = Home(3_000, director: false);
        var forts = world.Forts!;
        var at = Beside(train, gate - 30, 3);
        var r = world.AddEnemy(id => Ribbit.At(id, id, at, Tuning.Enemies.Ribbits));
        world.BeginTick();
        world.Step(new TrainControls { Reverser = 1 });
        Assert.False(forts.Inside(r.WorldPosition(train)));
        for (int t = 0; t < 6 * SimConstants.TickRate; t++)
        {
            world.BeginTick();
            world.Step(new TrainControls { Reverser = 1 });
        }
        Assert.True(r.Gone, "it lost interest at the walls");
    }

    [Fact]
    public void TheDirectorSpendsNothingWhileTheTrainIsInAFort()
    {
        // Standing in the home yard well past the grace: nothing sent, held for the fort.
        var (world, train, _, gate) = Home(300, director: true);
        Assert.True(world.EngineInFort);
        var s = PlayerMotor.SpawnInCab(train, P);
        for (int t = 0; t < 120 * SimConstants.TickRate; t++)
        {
            world.BeginTick();
            world.CrewAct(ref s, default, 1);
            world.Step(new TrainControls { Reverser = 1, Brake = 1 });
            train.Dynamics.Velocity = 0;
        }
        Assert.Empty(world.Director!.Log);
        Assert.Equal("fort", world.Director.HeldBecause);
        Assert.Equal(0, world.Director.Spent);
    }

    [Fact]
    public void NothingIsInsideAFortOverAHarnessNightIntoTheTerminus()
    {
        // The night's run in to the terminus with a crew of bots and the director's enemies, and hounds and climbers insisted
        // on the whole way: whatever's after the train, nothing is ever inside a fort's walls.
        var route = LineGen.Routes.Generate(DataFile.FindContentRoot(), "frontier:7", 6);
        var line = route.Build();
        int inside = 0, ticks = 0;
        string? what = null;
        var report = Harness.Run(line, Tuning.Train, Tuning.Player, new HarnessOptions
        {
            Bots = 3,
            Cars = 6,
            Seconds = 240,
            Seed = 1,
            Link = new Ballast.Net.LinkConditions(0, 0, 0),
            StartDistance = (route.Plan?.Terminus.GateM ?? route.Length - 600) - 2_000,
            Combat = Tuning.Combat,
            Enemies = Tuning.Enemies,
            Route = route,
            Run = Tuning.Run,
            YardLength = route.GateOr(Tuning.Route.YardLength),
            Insist = [EnemyKind.CinderHound, EnemyKind.Climber],
            InsistEvery = 10,
            Observe = (_, _, world) =>
            {
                ticks++;
                if (world.Forts is not { } forts)
                    return;
                foreach (var e in world.ActiveEnemies)
                    if (!e.Gone && e.Kind != EnemyKind.CarFire && forts.Inside(e.WorldPosition(world.Train)))
                    {
                        inside++;
                        what ??= $"{e.Kind} {e.Phase} at tick {world.Tick}";
                    }
            },
            Until = world => world.Run?.Over == true,
        }, Tuning.Boiler);
        Assert.True(ticks > 0);
        Assert.True(inside == 0, $"inside a fort {inside} times, first {what}");
    }
}
