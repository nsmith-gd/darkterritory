using Ballast;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;
using RunState = DarkTerritory.Sim.Run.Run;

namespace DarkTerritory.Sim.Tests;

/// <summary>GDD §9 run structure, spec B.8 dawn and F.1 pay: a night as a game.</summary>
public class RunTests
{
    static readonly TrainTuning T = Tuning.Train;
    static readonly PlayerTuning P = Tuning.Player;
    static readonly RunTuning R = Tuning.Run;
    static readonly Route.Route Frontier = RouteGenerator.Generate(Tuning.Route, RouteTier.Frontier, 7);
    static readonly RouteFeature Tower = Frontier.Of(FeatureKind.Facility).First(f => f.Facility == FacilityKind.CoalingTower);

    sealed class Night
    {
        public readonly World World;
        public readonly RunState Run;
        public PlayerState Player;
        // Standing on its brake unless a test drives it: with steam driving (T97) an unbraked engine pulls away.
        public TrainControls Controls = new() { Reverser = 1, Brake = 1 };

        public Night(double front, int cars = 6, Route.Route? route = null, double yard = 600)
        {
            route ??= Frontier;
            var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, cars, 1)), route.Build(), front, Tuning.Boiler);
            World = new World(train, Tuning.Combat);
            World.EnableRun(R, route, yard, authority: true);
            Run = World.Run!;
            Player = PlayerMotor.SpawnInCab(train, P);
        }

        public TrainOnLine Train => World.Train;

        public void Step(double seconds, Func<PlayerIntent>? intent = null, double? holdSpeed = null)
        {
            for (int i = 0; i < seconds * SimConstants.TickRate; i++)
            {
                if (holdSpeed is { } v)
                    Train.Dynamics.Velocity = v;
                var it = intent?.Invoke() ?? default;
                World.BeginTick();
                World.CrewAct(ref Player, it, 1);
                World.Step(Controls);
                World.ApplyDamage(id => Player, (_, s) => Player = s, [1]);
                PlayerMotor.Step(ref Player, it, Train, P, T, SimConstants.TickSeconds, applyLook: false);
                World.StepRun([Player]);
            }
        }
    }

    static double TenderUnderSpout(RunState run, RailLine line) =>
        run.ChuteAt(Tower, line).SpoutAlong + (T.Geometry.EngineLength - T.Geometry.Engine.TenderLength / 2);

    [Fact]
    public void TheRunAndTheDawnClockStartAtTheGates()
    {
        var n = new Night(front: 300);
        n.Step(5, holdSpeed: 10);
        Assert.Equal(RunPhase.Yard, n.Run.Phase);
        Assert.Equal(0, n.Run.Seconds);
        n.Step(40, holdSpeed: 10);
        Assert.Equal(RunPhase.Underway, n.Run.Phase);
        Assert.InRange(n.Run.Seconds, 1, 40);
    }

    /// <summary>Stopped with the tender under the coaling spout, one player on the ground at the lever.</summary>
    static Night AtTheTower(double tender, double offset = 0)
    {
        var probe = new Night(front: Tower.Start + 50);
        var n = new Night(front: TenderUnderSpout(probe.Run, probe.Train.Line) + offset);
        n.Train.Boiler.Tender = tender;
        n.Step(0.2);
        Assert.Equal(RunPhase.AtFacility, n.Run.Phase);
        Assert.Equal(FacilityKind.CoalingTower, n.Run.FacilityFeature!.Facility);
        var (_, lever) = n.Run.ChuteAt(Tower, n.Train.Line);
        n.Player = PlayerMotor.SpawnOnGround(lever - Double3.Up * 0.9, n.Train.Line, Tower.Start, P);
        return n;
    }

    static readonly Func<PlayerIntent> HoldUse = () => new PlayerIntent { Buttons = PlayerButtons.Use };

    [Fact]
    public void PullTheLeverUnderTheSpoutAndTheTenderFills()
    {
        var n = AtTheTower(tender: 100);
        n.Step(R.Chute.LeverSeconds + 0.1, HoldUse);
        Assert.True(n.Run.ChuteOpen);
        n.Step(R.Chute.Capacity / R.Chute.PourPerSecond + 1);
        Assert.False(n.Run.ChuteOpen);
        Assert.Equal(0, n.Run.ChuteLeft(n.Run.Facility), 6);
        Assert.InRange(n.Train.Boiler.Tender, 100 + R.Chute.Capacity - 5, 100 + R.Chute.Capacity);
        Assert.Equal(1, n.Train.Vehicles[0].Integrity);
    }

    [Fact]
    public void CoalThatMissesTheTenderIsLost()
    {
        var n = AtTheTower(tender: 100, offset: 12);
        n.Step(R.Chute.LeverSeconds + 0.1, HoldUse);
        n.Step(R.Chute.Capacity / R.Chute.PourPerSecond + 1);
        Assert.Equal(0, n.Run.ChuteLeft(n.Run.Facility), 6);
        Assert.InRange(n.Train.Boiler.Tender, 99, 100);
    }

    [Fact]
    public void ItFillsWhetherYouAreReadyOrNot()
    {
        // GDD §18: "Fast, deafening, fills whether you're ready or not." Nobody shuts it; the engine pays.
        var n = AtTheTower(tender: Tuning.Boiler.TenderCapacity - 50);
        n.Step(R.Chute.LeverSeconds + 0.1, HoldUse);
        n.Step(R.Chute.Capacity / R.Chute.PourPerSecond + 1);
        Assert.Equal(Tuning.Boiler.TenderCapacity, n.Train.Boiler.Tender, 3);
        Assert.True(n.Train.Vehicles[0].Integrity < 0.8);
    }

    [Fact]
    public void StoppedAtTheTerminusTheCargoIsDeliveredAndPaid()
    {
        var n = new Night(front: Frontier.Length - 150);
        n.Step(3, holdSpeed: 0);
        Assert.Equal(RunPhase.Arrived, n.Run.Phase);
        var report = n.Run.Report!;
        Assert.Equal(RunEnd.Delivered, report.End);
        int cargoCars = n.Train.Vehicles.Count(v => v.Kind == VehicleKind.Cargo);
        Assert.Equal(cargoCars, report.CarsDelivered);
        Assert.Equal(R.Economy.PerCar["frontier"] * cargoCars, report.Gross);
        Assert.Equal(1, report.CrewHome);
    }

    [Fact]
    public void OnlyWhatIsStillAttachedToTheLocomotiveCounts()
    {
        var n = new Night(front: Frontier.Length - 150);
        n.Train.Uncouple(2);
        n.Step(3, holdSpeed: 0);
        var report = n.Run.Report!;
        Assert.Equal(2, report.CarsDelivered);
        Assert.Equal(n.Train.Vehicles.Count(v => v.Kind == VehicleKind.Cargo) - 2, report.CarsLost);
    }

    [Fact]
    public void DerailingEndsTheNight()
    {
        var n = new Night(front: 5000);
        n.Step(1, holdSpeed: 10);
        n.World.Derail();
        n.Step(0.5);
        Assert.Equal(RunEnd.Derailed, n.Run.End);
        Assert.Equal(0, n.Run.Report!.Gross);
    }

    [Fact]
    public void StillOutWhenTheLineGoesLiveLosesTheNight()
    {
        // GDD §8: dawn reopens the main line. A short dawn clock stands in for a long night.
        var n = new Night(front: 5000, route: Frontier with { DawnSeconds = 3 });
        n.Step(2, holdSpeed: 10);
        Assert.False(n.Run.LineLive);
        n.Step(2, holdSpeed: 10);
        Assert.True(n.Run.LineLive);
        Assert.False(n.Run.Over);
        n.Step(R.DawnGraceSeconds, holdSpeed: 10);
        Assert.Equal(RunEnd.DawnMissed, n.Run.End);
    }

    [Fact]
    public void ClientsSeeTheRun()
    {
        var n = AtTheTower(tender: 100);
        n.Step(R.Chute.LeverSeconds + 0.1, HoldUse);
        var controls = new TrainControls();
        var records = WorldRecords.Capture(n.World, controls, []);
        var client = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 6, 1)), Frontier.Build(), 1000, Tuning.Boiler));
        client.EnableRun(R, Frontier, 600, authority: false);
        WorldRecords.Apply(records, client, ref controls, []);
        Assert.Equal(RunPhase.AtFacility, client.Run!.Phase);
        Assert.True(client.Run.ChuteOpen);
        Assert.Equal(n.Run.Seconds, client.Run.Seconds, 3);
    }
}
