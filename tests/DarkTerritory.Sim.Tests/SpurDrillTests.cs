using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>GDD §17's facility set piece, end to end on generated track (T28).</summary>
public class SpurDrillTests
{
    static readonly TrainTuning T = Tuning.Train;
    static readonly PlayerTuning P = Tuning.Player;
    static readonly FacilityTuning F = FacilityTests.F;

    /// <summary>A night whose facility has a winch (a crew can load there), and that facility's index.</summary>
    static (Route.Route Route, int Facility) WinchStop()
    {
        foreach (var tier in new[] { RouteTier.Frontier, RouteTier.DeadLines, RouteTier.Local })
            for (ulong seed = 1; seed < 200; seed++)
            {
                var route = RouteGenerator.Generate(Tuning.Route, tier, seed);
                var facilities = route.Of(FeatureKind.Facility).ToList();
                int i = facilities.FindIndex(f => f.Facility is { } k && F.ModulesOf(k).Contains(ModuleKind.Winch));
                if (i >= 0)
                    return (route, i);
            }
        throw new InvalidOperationException("no route has a winch");
    }

    sealed class Night
    {
        public readonly World World;
        public readonly SpurDrill Drill;
        public readonly List<PlayerState> Crew = [];
        public readonly HashSet<RunPhase> Phases = [];

        /// <summary>How many cars the facility's own siding takes with the engine (its length is generated: level-design P16).</summary>
        public static int Capacity()
        {
            var (route, facility) = WinchStop();
            var line = route.Build();
            return SpurDrill.Capacity(T.Geometry, line.Branches[new Run.Run(Tuning.Run, route).SpurOf(facility)], Tuning.Route.Junctions.PointsLength);
        }

        public Night(int cars)
        {
            var (route, facility) = WinchStop();
            var line = route.Build();
            var spur = line.Branches[new Run.Run(Tuning.Run, route).SpurOf(facility)];
            // No boiler model: steam without a fireman. Left alone through the loading, a real fire dies and the
            // regulator does nothing (the crew keeps it; this is about the shunting).
            var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, cars, 0)), line, spur.Toe - 120);
            World = new World(train, Tuning.Combat);
            World.EnableBodies();
            World.EnableSwitches(Tuning.Route.Junctions);
            World.EnableRun(Tuning.Run, route, 600, authority: true, F);
            Drill = new SpurDrill(World, facility);
        }

        public TrainOnLine Train => World.Train;

        /// <summary>Plays it out: at the facility, two of the crew go to the capstan and haul until the sleds are in.</summary>
        public void Play(double maxSeconds = 1200)
        {
            var crank = new PlayerIntent { Buttons = PlayerButtons.Use };
            for (int t = 0; t < maxSeconds * SimConstants.TickRate && Drill.Step != DrillStep.Done; t++)
            {
                if (Drill.Step == DrillStep.Loading && Crew.Count == 0 && World.Run!.CurrentSite is { } site)
                    foreach (var handle in site.Handles)
                        Crew.Add(PlayerMotor.SpawnOnGround(handle - Double3.Up * 0.9, Train.Line, site.Feature.Start, P));
                bool loading = Drill.Step == DrillStep.Loading;
                // Done at the capstan, they climb aboard: left standing out there they'd freeze (spec B.2), and a
                // crew all dead ends the night before it can leave.
                if (!loading && Drill.Step > DrillStep.Loading)
                    Crew.Clear();
                var intents = Crew.Select(_ => loading ? crank : default).ToArray();
                World.BeginTick();
                for (int i = 0; i < Crew.Count; i++)
                {
                    var s = Crew[i];
                    World.CrewAct(ref s, intents[i], i + 1);
                    Crew[i] = s;
                }
                var controls = Drill.Tick(SimConstants.TickSeconds);
                World.Step(controls);
                for (int i = 0; i < Crew.Count; i++)
                {
                    var s = Crew[i];
                    PlayerMotor.Step(ref s, intents[i], Train, P, T, SimConstants.TickSeconds, applyLook: false);
                    Crew[i] = s;
                }
                World.StepBodies([.. Crew.Select((c, i) => (i + 1, c))]);
                World.StepRun(Crew);
                Phases.Add(World.Run!.Phase);
                if (loading && World.Run.CurrentSite is { SledsLeft: 0 })
                    Drill.Loaded = true;
            }
        }
    }

    [Fact]
    public void CutSpurInLoadBackOutRecoupleAndGo()
    {
        int fit = Night.Capacity();
        var night = new Night(cars: fit + 3);
        var train = night.Train;
        var order = train.Dynamics.Consist.Vehicles.Select(v => v.Id).ToArray();
        night.Play();

        Assert.True(night.Drill.Step == DrillStep.Done, $"stuck at {night.Drill.Step}: {string.Join(", ", night.Drill.Timeline)}; " +
            string.Join(" ; ", train.Rakes.Select(r => $"[{string.Join(",", r.Consist.Vehicles.Select(v => v.Id))}] path {r.Path} at {r.Distance:0.0}..{r.RearDistance:0.0} v {r.Velocity:0.00}")));
        Assert.Equal(Enum.GetValues<DrillStep>(), night.Drill.Timeline.Select(x => x.Step));
        // Three more cars than the siding takes: the engine and as many as fit went in, three waited on the main line.
        Assert.Equal(fit, night.Drill.TookIn.Count);
        Assert.Equal(3, night.Drill.LeftWaiting.Count);
        // One train again, in the order it arrived, on the main line, with the switch set back for it.
        Assert.Equal(1, train.TrainRakes);
        var rake = train.Dynamics;
        Assert.Equal(order, rake.Consist.Vehicles.Select(v => v.Id));
        Assert.Equal(RailLine.MainPath, train.Dynamics.Path);
        Assert.False(train.Diverging(night.World.Run!.SpurOf(night.World.Run.Departed)));
        // It stopped at the facility, the winch's two sleds went into the cars it took in (the sled rests between the
        // first two), and the night saw it leave.
        Assert.Contains(RunPhase.AtFacility, night.Phases);
        Assert.Equal(2 * F.Winch.LoadPerSled, night.Drill.TookIn.Take(2).Sum(id => train.Vehicles[id].Load), 6);
        Assert.All(night.Drill.LeftWaiting, id => Assert.Equal(0, train.Vehicles[id].Load));
        Assert.Equal(1, night.World.Run.Departures);
        Assert.Equal(RunPhase.Underway, night.World.Run.Phase);
        // And gently: nothing was damaged coupling back on.
        Assert.All(train.Vehicles.Take(train.OwnVehicles), v => Assert.Equal(1, v.Integrity));
    }

    [Fact]
    public void AShortTrainGoesInWhole()
    {
        int cars = Math.Min(3, Night.Capacity());
        var night = new Night(cars);
        night.Play();
        Assert.Equal(DrillStep.Done, night.Drill.Step);
        Assert.Equal(cars, night.Drill.TookIn.Count);
        Assert.Empty(night.Drill.LeftWaiting);
        Assert.Equal(1, night.Train.TrainRakes);
        Assert.Equal(1, night.World.Run!.Departures);
    }

    [Fact]
    public void StoppedOnTheMainLineBesideTheSpurIsntAtTheFacility()
    {
        // The machinery is down the spur: pulling up in the zone on the main line gets you nothing.
        var (route, facility) = WinchStop();
        var f = route.Of(FeatureKind.Facility).ElementAt(facility);
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 4, 0)), route.Build(), (f.Start + f.End) / 2, Tuning.Boiler);
        var world = new World(train, Tuning.Combat);
        world.EnableRun(Tuning.Run, route, 600, authority: true, F);
        for (int t = 0; t < SimConstants.TickRate; t++)
        {
            world.BeginTick();
            world.Step(new TrainControls { Brake = 1, Reverser = 1 });
            world.StepRun([]);
        }
        Assert.Equal(RunPhase.Underway, world.Run!.Phase);
        Assert.Equal(-1, world.Run.Facility);
    }
}
