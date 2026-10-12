using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 576 (queue #306; the director, 9 Oct 2026: "If players dont fix the ship in sea of thieves the ship goes down and you
/// lose everything. If players dont fix the train here seemingly nothing happens"): a battered car let go spills its freight
/// and comes apart; a battered engine let go loses its pull and brake and breaks down, and the crew are stranded.
/// </summary>
public class FailingTests
{
    static readonly TrainTuning T = Tuning.Train;
    static readonly FailingTuning F = Tuning.Train.Failing;

    static World World(int cars = 5)
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(80_000)]));
        var world = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(T, cars, 1)), line, 2_000, Tuning.Boiler));
        world.EnableBodies(); // the host
        return world;
    }

    /// <summary>Runs the world on at <paramref name="speed"/> (held), as the host.</summary>
    static void Run(World world, double seconds, double speed, double throttle = 1)
    {
        for (int i = 0; i < seconds * SimConstants.TickRate; i++)
        {
            world.Train.Dynamics.Velocity = speed;
            world.BeginTick();
            world.Step(new TrainControls { Reverser = 1, Throttle = throttle });
        }
    }

    [Fact]
    public void TheTuningSaysWhatTheDirectorAskedFor()
    {
        Assert.True(F.Enabled);
        Assert.True(F.BreakingBelow < F.Below && F.Below < T.Repair.DentedBelow);
        // From the failing line to nothing at line speed is minutes, not seconds: time to see it, hear it and mend it.
        double toNothing = F.Below / F.WorkPerSecond;
        Assert.InRange(toNothing, 120, 400);
        // And a full car's freight is gone in about that long.
        Assert.InRange(1 / F.SpillPerSecond, 120, 400);
        // The wrench brings a failing car back over the line in seconds.
        Assert.InRange(F.Below / T.Repair.IntegrityPerSecond, 5, 30);
    }

    [Fact]
    public void ABatteredCarHoldsUntilItsUnderTheLine()
    {
        var world = World();
        var car = world.Train.Vehicles[2];
        car.Integrity = F.Below + 0.01;
        Run(world, 20, F.AtSpeed);
        Assert.Equal(F.Below + 0.01, car.Integrity, 9);
        Assert.Equal(1, car.Load, 9);
        Assert.Equal(FailStage.Holding, Failing.Stage(F, car));
    }

    [Fact]
    public void ACarLetGoSpillsItsFreightAsItRunsAndNothingStanding()
    {
        var world = World();
        var car = world.Train.Vehicles[2];
        car.Integrity = 0.4;
        Assert.Equal(FailStage.Failing, Failing.Stage(F, car));
        // Standing, nothing comes of it (held at a stand, it creeps no more than a tick's pull).
        Run(world, 10, 0);
        Assert.Equal(0.4, car.Integrity, 3);
        Assert.Equal(1, car.Load, 3);
        // Running at the tuning's speed: its shell and its freight go by the second.
        Run(world, 20, F.AtSpeed);
        Assert.Equal(0.4 - 20 * F.WorkPerSecond, car.Integrity, 3);
        Assert.Equal(1 - 20 * F.SpillPerSecond, car.Load, 3);
        // Faster, faster (to mostFactor).
        double was = car.Integrity;
        Run(world, 10, 2 * F.AtSpeed);
        Assert.Equal(was - 10 * F.WorkPerSecond * F.MostFactor, car.Integrity, 2);
        // The other cars are untouched.
        Assert.All(world.Train.Vehicles.Where(v => v.Id != 2), v => Assert.Equal(1, v.Integrity));
    }

    [Fact]
    public void ACarLetGoComesApartAndWhatsBehindItIsLost()
    {
        var world = World(cars: 5);
        var train = world.Train;
        var car = train.Vehicles[2];
        car.Integrity = F.BreakingBelow - 0.01;
        Assert.Equal(FailStage.Breaking, Failing.Stage(F, car));
        Assert.Equal(6, train.Dynamics.Consist.Vehicles.Count); // the engine and five cars
        Run(world, (F.BreakingBelow - 0.01) / F.WorkPerSecond + 2, F.AtSpeed);
        // Off its rails and parted from the train ahead of it: the engine's rake is the engine and car 1.
        Assert.True(car.OffRails);
        Assert.Equal([0, 1], train.Dynamics.Consist.Vehicles.Select(v => v.Id));
        Assert.Equal(0, car.Load);
        Assert.True(world.Attribution.Apart(2));
        // Left on the line: it holds fast where it is, and what's behind it with it.
        var left = train.RakeOf(2);
        Assert.Equal([2, 3, 4, 5], left.Consist.Vehicles.Select(v => v.Id));
        double at = left.Distance;
        for (int i = 0; i < 2 * SimConstants.TickRate; i++)
        {
            world.BeginTick();
            world.Step(new TrainControls { Reverser = 1 });
        }
        Assert.True(left.Distance - at < 2 * F.AtSpeed * 0.5, $"rolled {left.Distance - at:0.0} m");
    }

    [Fact]
    public void ACarTakenToNothingAtOnceKeepsItsOwnEnd()
    {
        // The powder blast (note 182) leaves its car burnt out in the train: only a car worn through comes apart.
        var world = World();
        world.Train.Vehicles[2].Integrity = 0;
        Run(world, 5, F.AtSpeed);
        Assert.False(world.Train.Vehicles[2].OffRails);
        Assert.Equal(6, world.Train.Dynamics.Consist.Vehicles.Count);
    }

    [Fact]
    public void MendedWithTheWrenchItStopsFailing()
    {
        var world = World();
        var train = world.Train;
        train.Vehicles[2].Integrity = 0.3;
        Repairs.MendDent(train, 2, (F.Below - 0.3) / T.Repair.IntegrityPerSecond + 0.5);
        Assert.Equal(FailStage.Holding, Failing.Stage(train, 2));
        double was = train.Vehicles[2].Integrity;
        Run(world, 10, F.AtSpeed);
        Assert.Equal(was, train.Vehicles[2].Integrity, 9);
    }

    [Fact]
    public void TheYardIsSafe()
    {
        var world = World();
        var train = world.Train;
        train.Vehicles[2].Integrity = 0.3;
        train.HeldInYard = true;
        train.Dynamics.Velocity = F.AtSpeed;
        train.Step(SimConstants.TickSeconds * 60, new TrainControls { Reverser = 1, Throttle = 1 });
        Assert.Equal(0.3, train.Vehicles[2].Integrity, 9);
    }

    [Fact]
    public void AFailingEngineLosesItsPullAndBrakeThenBreaksDown()
    {
        var engine = Consist.Uniform(T, 3, 0).Vehicles[0];
        Assert.Equal(1, Failing.Power(F, engine));
        Assert.Equal(1, Failing.Brake(F, engine));
        engine.Integrity = F.Below / 2;
        Assert.Equal(F.PowerAtBreak + (1 - F.PowerAtBreak) / 2, Failing.Power(F, engine), 9);
        Assert.Equal(F.BrakeAtBreak + (1 - F.BrakeAtBreak) / 2, Failing.Brake(F, engine), 9);
        engine.Integrity = 0;
        Assert.Equal(0, Failing.Power(F, engine));
        Assert.Equal(F.BrakeAtBreak, Failing.Brake(F, engine));
    }

    [Fact]
    public void AFailingEngineCantHaulWhatAWholeOneCan()
    {
        // On a 1% climb from 5 m/s, a whole engine and one under the failing line, same train, a minute on full throttle.
        double Climb(double integrity)
        {
            var line = new RailLine(new LineDefinition("t", [new TrackSegment(80_000, GradePercent: 1)]));
            var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 6, 1)), line, 2_000, Tuning.Boiler);
            train.Vehicles[0].Integrity = integrity;
            train.Dynamics.Velocity = 5;
            for (int i = 0; i < 60 * SimConstants.TickRate; i++)
                train.Step(SimConstants.TickSeconds, new TrainControls { Reverser = 1, Throttle = 1 });
            return train.Dynamics.Speed;
        }
        double whole = Climb(1), failing = Climb(0.1), broken = Climb(0);
        Assert.True(failing < whole - 0.5, $"whole {whole:0.00} m/s, failing {failing:0.00} m/s");
        Assert.True(broken < failing, $"failing {failing:0.00} m/s, broken {broken:0.00} m/s");
    }

    [Fact]
    public void AnEngineLetGoBreaksDownAndAtAStandTheNightIsStranded()
    {
        var route = RouteGenerator.Generate(Tuning.Route, RouteTier.Frontier, 1);
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 4, 0)), route.Build(), 3_000, Tuning.Boiler);
        var world = new World(train, Tuning.Combat);
        world.EnableRun(Tuning.Run, route, 600, authority: true);
        world.Run!.Resume(900, -1, train.Boiler.Tender, 0);
        var crew = new[] { PlayerMotor.SpawnInCab(train, Tuning.Player) };
        train.Vehicles[0].Integrity = 0;
        Assert.True(Failing.BrokenDown(train));
        train.Dynamics.Velocity = 6;
        for (int i = 0; i < 120 * SimConstants.TickRate && !world.Run.Over; i++)
        {
            world.BeginTick();
            world.Step(new TrainControls { Reverser = 1, Throttle = 1, Brake = 1 });
            world.StepRun(crew);
        }
        Assert.Equal(RunEnd.Stranded, world.Run.End);
        Assert.Contains(world.Attribution.Of(IncidentKind.Stranded), i => i.Action.Contains("broke down"));
    }
}
