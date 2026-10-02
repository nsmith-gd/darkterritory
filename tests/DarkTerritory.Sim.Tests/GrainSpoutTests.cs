using Ballast;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;
using RunState = DarkTerritory.Sim.Run.Run;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// GDD §18's grain elevator, "one spout, one car at a time" (run.json "grainSpout"): the coaling chute's lever and pour,
/// into the cargo car under the spout down the elevator's spur.
/// </summary>
public class GrainSpoutTests
{
    static readonly TrainTuning T = Tuning.Train;
    static readonly PlayerTuning P = Tuning.Player;
    static readonly RunTuning R = Tuning.Run;
    static readonly GrainSpoutTuning G = R.GrainSpout;
    static readonly PlayerIntent HoldUse = new() { Buttons = PlayerButtons.Use };
    static readonly Lazy<(Route.Route Route, RouteFeature Elevator)> Found = new(ElevatorRoute);

    /// <summary>A generated route with a grain elevator down a spur long enough for the spout, and that elevator.</summary>
    static (Route.Route Route, RouteFeature Elevator) ElevatorRoute()
    {
        foreach (var tier in new[] { RouteTier.Frontier, RouteTier.Local, RouteTier.DeadLines, RouteTier.DeepTerritory })
            for (ulong seed = 1; seed < 200; seed++)
            {
                var route = RouteGenerator.Generate(Tuning.Route, tier, seed);
                var line = route.Build();
                var f = route.Of(FeatureKind.Facility).FirstOrDefault(f => f.Facility == FacilityKind.GrainElevator
                    && line.Branches.FirstOrDefault(b => b.Kind == BranchKind.Spur && f.Contains(b.Toe)) is { } spur && spur.Local.Length > G.FromBuffer + 10);
                if (f is not null)
                    return (route, f);
            }
        throw new InvalidOperationException("no route has a grain elevator");
    }

    /// <summary>
    /// Stopped down the elevator's spur, four cargo cars and the guard van behind the engine, each half full (the
    /// fortress's load), someone on the ground at the spout's lever. <paramref name="back"/> is how far short of the
    /// buffer stop the engine's front stands, past the metre the driver leaves.
    /// </summary>
    sealed class Elevator
    {
        public readonly World World;
        public readonly RunState Run;
        public readonly int Index;
        public PlayerState Player;

        public Elevator(RunTuning tuning, double back = 0, double load = 0.5)
        {
            var (route, f) = Found.Value;
            var line = route.Build();
            var spur = line.Branches.First(b => b.Kind == BranchKind.Spur && f.Contains(b.Toe));
            var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 5, load)), line, spur.End - 1 - back, Tuning.Boiler);
            var state = train.Capture();
            train.Restore(state with { Rakes = [state.Rakes[0] with { Path = spur.Index }] });
            World = new World(train, Tuning.Combat);
            World.EnableBodies();
            World.EnableRun(tuning, route, 600, authority: true, FacilityTests.F);
            Run = World.Run!;
            Index = route.Of(FeatureKind.Facility).ToList().IndexOf(f);
            var lever = Run.SpoutAt(Index, line).Lever;
            Player = PlayerMotor.SpawnOnGround(lever - Double3.Up * 0.9, line, f.Start, P);
            Step(0.2);
            Assert.Equal(RunPhase.AtFacility, Run.Phase);
            Assert.Equal(Index, Run.Facility);
        }

        public TrainOnLine Train => World.Train;
        public IEnumerable<Vehicle> Cargo => Train.Vehicles.Where(v => v.Kind == VehicleKind.Cargo);

        public void Step(double seconds, PlayerIntent intent = default, double speed = 0)
        {
            for (int t = 0; t < seconds * SimConstants.TickRate; t++)
            {
                Train.Dynamics.Velocity = speed;
                World.BeginTick();
                World.CrewAct(ref Player, intent, 1);
                World.Step(new TrainControls { Reverser = 1, Brake = 1 });
                PlayerMotor.Step(ref Player, intent, Train, P, T, SimConstants.TickSeconds, applyLook: false);
                World.StepBodies([(1, Player)]);
                World.StepRun([Player]);
            }
        }

        /// <summary>Holds the lever over (open, or shut again) and lets go.</summary>
        public void Pull()
        {
            Step(R.Chute.LeverSeconds + 0.1, HoldUse);
            Step(0.1);
        }
    }

    [Fact]
    public void TheSpoutFillsTheCarUnderItWithFood()
    {
        // The engine up at the buffer stop: the fourth car stands under the spout.
        var e = new Elevator(R);
        Assert.True(e.Run.HasSpout(e.Index));
        Assert.Equal(G.Capacity, e.Run.ChuteLeft(e.Index), 6);
        var under = e.Train.Vehicles[4];
        Assert.Equal(4, e.Run.CarUnderSpout(e.Train.Frames, e.Train.Vehicles, e.Run.SpoutAt(e.Index, e.Train.Line).Spout));
        Assert.True(e.Run.LeverInReach(e.Player, e.Train));
        e.Pull();
        Assert.True(e.Run.ChuteOpen);
        e.Step(10);
        double poured = G.Capacity - e.Run.ChuteLeft(e.Index);
        Assert.InRange(poured, G.PourPerSecond * 10, G.PourPerSecond * 11);
        Assert.Equal(0.5 + poured, under.Load, 6);
        Assert.Equal(CargoKind.Food, under.Cargo);
        Assert.Equal(1, under.Integrity);
        // One car at a time: the others are as they were.
        Assert.All(e.Cargo.Where(v => v != under), v => Assert.Equal(0.5, v.Load, 6));
        Assert.All(e.Cargo.Where(v => v != under), v => Assert.Equal(CargoKind.Goods, v.Cargo));

        // Shut again, it stops.
        e.Pull();
        Assert.False(e.Run.ChuteOpen);
        double load = under.Load, left = e.Run.ChuteLeft(e.Index);
        e.Step(5);
        Assert.Equal(load, under.Load, 9);
        Assert.Equal(left, e.Run.ChuteLeft(e.Index), 9);
    }

    [Fact]
    public void ThePouredLoadIsPaidForAtTheTerminus()
    {
        // Spec F.1 via Run.Tally: a car's load pays per car whatever went into it, the spout's grain like a crate's.
        var e = new Elevator(R);
        var before = e.Run.Tally(e.World, [e.Player]).CargoDelivered;
        e.Pull();
        e.Step(10);
        var after = e.Run.Tally(e.World, [e.Player]).CargoDelivered;
        Assert.InRange(after - before - (G.Capacity - e.Run.ChuteLeft(e.Index)), -0.011, 0.011);
    }

    [Fact]
    public void WithNoCarUnderItTheGrainIsLost()
    {
        // Half a car and a gap short: the spout's over the coupling between the third and fourth cars.
        var e = new Elevator(R, back: (T.Geometry.CarLength + T.Geometry.CouplingGap) / 2);
        Assert.Equal(-1, e.Run.CarUnderSpout(e.Train.Frames, e.Train.Vehicles, e.Run.SpoutAt(e.Index, e.Train.Line).Spout));
        e.Pull();
        e.Step(G.Capacity / G.PourPerSecond + 1);
        Assert.False(e.Run.ChuteOpen);
        Assert.Equal(0, e.Run.ChuteLeft(e.Index), 6);
        Assert.All(e.Cargo, v => Assert.Equal(0.5, v.Load, 6));
        Assert.All(e.Train.Vehicles, v => Assert.Equal(1, v.Integrity));
        // Spent, the lever does nothing.
        Assert.False(e.Run.LeverInReach(e.Player, e.Train));
    }

    [Fact]
    public void OverfillingSpillsOverTheCarAndDamagesIt()
    {
        // Spec D.2's gravity chute: "overfill damages car and spills". Nobody shuts it on a nearly full car.
        var e = new Elevator(R, load: 0.9);
        var under = e.Train.Vehicles[4];
        e.Pull();
        e.Step(10);
        double poured = G.Capacity - e.Run.ChuteLeft(e.Index);
        Assert.Equal(1, under.Load, 9);
        Assert.Equal(1 - (poured - 0.1) * G.OverfillDamagePerLoad, under.Integrity, 6);
    }

    [Fact]
    public void ItShutsWhenTheTrainMovesOff()
    {
        // As the coaling chute does: spotting the next car under it means pulling it again.
        var e = new Elevator(R);
        e.Pull();
        Assert.True(e.Run.ChuteOpen);
        e.Step(0.5, speed: -1);
        Assert.False(e.Run.ChuteOpen);
    }

    [Fact]
    public void ClientsSeeTheSpout()
    {
        var e = new Elevator(R);
        e.Pull();
        e.Step(2);
        var controls = new TrainControls();
        var records = WorldRecords.Capture(e.World, controls, []);
        var (route, _) = Found.Value;
        var client = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 5, 0.5)), route.Build(), 1000, Tuning.Boiler));
        client.EnableRun(R, route, 600, authority: false);
        WorldRecords.Apply(records, client, ref controls, []);
        Assert.True(client.Run!.ChuteOpen);
        Assert.Equal(e.Index, client.Run.Facility);
        Assert.Equal(e.Run.ChuteLeft(e.Index), client.Run.ChuteLeft(e.Index), 6);
        Assert.Equal(e.Train.Vehicles[4].Load, client.Train.Vehicles[4].Load, 6);
        Assert.Equal(CargoKind.Food, client.Train.Vehicles[4].Cargo);
    }

    [Fact]
    public void SwitchedOffTheElevatorIsCratesOnly()
    {
        // run.json "grainSpout.on": false is the elevator as it was before it had one.
        var off = R with { GrainSpout = G with { On = false } };
        var e = new Elevator(off);
        Assert.False(e.Run.HasSpout(e.Index));
        Assert.Equal(0, e.Run.ChuteLeft(e.Index));
        Assert.False(e.Run.LeverInReach(e.Player, e.Train));
        e.Step(R.Chute.LeverSeconds * 3, HoldUse);
        Assert.False(e.Run.ChuteOpen);
        Assert.All(e.Cargo, v => Assert.Equal(0.5, v.Load, 6));
        Assert.True(e.Run.CurrentSite!.Has(ModuleKind.Crates));
    }
}
