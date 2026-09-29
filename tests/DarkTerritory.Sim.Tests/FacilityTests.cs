using Ballast;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>Spec D loading modules: manual crates and the capstan winch.</summary>
public class FacilityTests
{
    static readonly TrainTuning T = Tuning.Train;
    static readonly PlayerTuning P = Tuning.Player;
    static readonly FacilityTuning F = DataFile.Load<FacilityTuning>(Path.Combine(DataFile.FindContentRoot(), FacilityTuning.File));

    /// <summary>A route with a facility that has the module, and that facility.</summary>
    static (Route.Route Route, RouteFeature Facility) With(ModuleKind module)
    {
        foreach (var tier in new[] { RouteTier.Frontier, RouteTier.DeadLines, RouteTier.DeepTerritory, RouteTier.Local })
            for (ulong seed = 1; seed < 200; seed++)
            {
                var route = RouteGenerator.Generate(Tuning.Route, tier, seed);
                var f = route.Of(FeatureKind.Facility).FirstOrDefault(f => f.Facility is { } k && F.ModulesOf(k).Contains(module));
                if (f is not null)
                    return (route, f);
            }
        throw new InvalidOperationException($"no route has a {module}");
    }

    sealed class Stop
    {
        public readonly World World;
        public readonly List<PlayerState> Crew = [];
        public readonly Site Site;

        /// <param name="carAt">Along-line distance to put the first cargo car's middle at (defaults to the facility's middle).</param>
        public Stop(ModuleKind module, double? carAt = null)
        {
            var (route, f) = With(module);
            var g = T.Geometry;
            double firstCar = carAt ?? (f.Start + f.End) / 2;
            double front = firstCar + g.EngineLength + g.CouplingGap + g.CarLength / 2;
            var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 6, 0)), route.Build(), front, Tuning.Boiler);
            World = new World(train, Tuning.Combat);
            World.EnableBodies();
            World.EnableRun(Tuning.Run, route, 600, authority: true, F);
            Step(0.2, []);
            Assert.Equal(RunPhase.AtFacility, World.Run!.Phase);
            Site = World.Run.CurrentSite!;
        }

        public TrainOnLine Train => World.Train;

        public void Step(double seconds, PlayerIntent[] intents)
        {
            for (int t = 0; t < seconds * SimConstants.TickRate; t++)
            {
                Train.Dynamics.Velocity = 0;
                World.BeginTick();
                for (int i = 0; i < Crew.Count; i++)
                {
                    var s = Crew[i];
                    World.CrewAct(ref s, intents[i], i + 1);
                    Crew[i] = s;
                }
                World.Step(new TrainControls { Reverser = 1, Brake = 1 });
                for (int i = 0; i < Crew.Count; i++)
                {
                    var s = Crew[i];
                    PlayerMotor.Step(ref s, intents[i], Train, P, T, SimConstants.TickSeconds, applyLook: false);
                    Crew[i] = s;
                }
                World.StepBodies([.. Crew.Select((c, i) => (i + 1, c))]);
                World.StepRun(Crew);
            }
        }

        /// <summary>Stands someone on the ground at a point, facing along the line.</summary>
        public PlayerState OnTheGround(Double3 at) => PlayerMotor.SpawnOnGround(at, Train.Line, Site.Feature.Start, P);
    }

    [Fact]
    public void EveryFacilityHasTheModulesItsKindOffers()
    {
        var route = RouteGenerator.Generate(Tuning.Route, RouteTier.Frontier, 7);
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 6, 1)), route.Build(), 900);
        var world = new World(train);
        world.EnableRun(Tuning.Run, route, 600, authority: true, F);
        var facilities = route.Of(FeatureKind.Facility).ToList();
        Assert.Equal(facilities.Count, world.Run!.Sites.Count);
        for (int i = 0; i < facilities.Count; i++)
        {
            var offered = F.ModulesOf(facilities[i].Facility!.Value);
            if (offered.Count == 0)
                Assert.Null(world.Run.Sites[i]);
            else
                Assert.Equal(offered, world.Run.Sites[i]!.Modules);
        }
        Assert.Empty(F.ModulesOf(FacilityKind.CoalingTower));
    }

    [Fact]
    public void CratesComeOutWhenTheTrainStopsAndACarTakesThemAsLoad()
    {
        var stop = new Stop(ModuleKind.Crates);
        var cargo = stop.World.Bodies.All.Where(b => b.Kind == BodyKind.Cargo).ToList();
        Assert.Equal(stop.Site.CrateCount, cargo.Count);
        Assert.InRange(cargo.Count, F.Crates.Count[0], F.Crates.Count[1]);
        Assert.True(stop.Site.Stocked);
        // Every crate stands on the ground by the track, not in the air.
        stop.Step(2, []);
        Assert.All(cargo, b => Assert.Equal(PlayerState.World, b.Parent));

        // Put one down inside the first cargo car: once it lies still there, it's loaded, and gone.
        var car = stop.Train.Vehicles.First(v => v.Kind == VehicleKind.Cargo);
        var room = stop.Train.Frames[car.Id].Shape.Interior!.Value;
        var crate = cargo[0];
        crate.Parent = car.Id;
        crate.Pbd.Particles[0].Position = new Double3(0, room.Min.Y + 0.5, 0);
        crate.Pbd.Particles[0].Previous = crate.Pbd.Particles[0].Position;
        stop.Step(F.Crates.SettleSeconds + 1, []);
        Assert.Equal(F.Crates.LoadPerCrate, car.Load, 6);
        Assert.DoesNotContain(crate, stop.World.Bodies.All);
    }

    [Fact]
    public void CarryingFreightIsSlowAndKeepsYouOffLadders()
    {
        var stop = new Stop(ModuleKind.Crates);
        stop.Step(2, []);
        var crate = stop.World.Bodies.All.First(b => b.Kind == BodyKind.Cargo);
        var at = crate.Pbd.Particles[0].Position;
        var s = stop.OnTheGround(at + new Double3(0, 0, 0.7));
        s.Yaw = 0; // facing −Z, towards the crate
        stop.Crew.Add(s);
        stop.Step(0.1, [default]);
        stop.Step(0.1, [new PlayerIntent { Buttons = PlayerButtons.Use }]);
        Assert.Equal(1, crate.Carrier);
        stop.Step(0.1, [default]);
        Assert.True(stop.Crew[0].Has(PlayerFlags.Heavy));
        var start = stop.Crew[0].Position;
        stop.Step(1, [new PlayerIntent { MoveZ = 1, Buttons = PlayerButtons.Run }]);
        double speed = (stop.Crew[0].Position - start).Length;
        Assert.InRange(speed, P.CarryHeavy * 0.8, P.CarryHeavy * 1.05);
        // Put it down and you're light again.
        stop.Step(0.1, [new PlayerIntent { Buttons = PlayerButtons.Use }]);
        stop.Step(0.1, [default]);
        Assert.False(stop.Crew[0].Has(PlayerFlags.Heavy));
    }

    [Fact]
    public void TheWinchNeedsTwoOnTheCapstan()
    {
        // Park the first cargo car by the winch, where the sled comes to rest.
        var (_, f) = With(ModuleKind.Winch);
        var stop = new Stop(ModuleKind.Winch, carAt: (f.Start + f.End) / 2 + F.Winch.Along);
        var site = stop.Site;
        var crank = new PlayerIntent { Buttons = PlayerButtons.Use };
        foreach (var handle in site.Handles)
            stop.Crew.Add(stop.OnTheGround(handle - Double3.Up * 0.9));

        // One alone: it doesn't move.
        stop.Step(5, [crank, default]);
        Assert.Equal(0, site.Progress);
        Assert.False(site.Turning);

        // Both: it hauls at the winch's speed.
        stop.Step(10, [crank, crank]);
        Assert.True(site.Turning);
        Assert.Equal(10 * F.Winch.Speed / F.Winch.HaulMetres, site.Progress, 2);

        // Haul it all the way and the car by the track takes the load.
        var car = stop.Train.Vehicles.First(v => v.Kind == VehicleKind.Cargo);
        stop.Step(F.Winch.HaulMetres / F.Winch.Speed, [crank, crank]);
        Assert.Equal(F.Winch.LoadPerSled, car.Load, 6);
        Assert.Equal(F.Winch.Sleds - 1, site.SledsLeft);
    }

    [Fact]
    public void AClientSeesTheSite()
    {
        var stop = new Stop(ModuleKind.Winch);
        var client = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 6, 0)), stop.Train.Line, 1000, Tuning.Boiler));
        client.EnableRun(Tuning.Run, stop.World.Run!.Route, 600, authority: false, F);
        var controls = new TrainControls();
        var records = WorldRecords.Capture(stop.World, controls, []);
        WorldRecords.Apply(records, client, ref controls, []);
        var mirrored = client.Run!.Sites[stop.Site.Index]!;
        Assert.Equal(stop.Site.SledsLeft, mirrored.SledsLeft);
        Assert.Equal(stop.Site.Stocked, mirrored.Stocked);
        Assert.Equal(stop.Site.Capstan, mirrored.Capstan);
    }
}
