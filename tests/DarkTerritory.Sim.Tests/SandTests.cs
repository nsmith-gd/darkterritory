using Ballast;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Grease's counter (T72, App. A.2): "sanding from the running boards restores traction over ~8s". The engine has running
/// boards out past the cab sides and a sandbox on each; held there, the grip comes back.
/// </summary>
public class SandTests
{
    static readonly TrainTuning T = Tuning.Train;
    static readonly PlayerTuning P = Tuning.Player;
    static readonly SightTuning S = DataFile.Load<SightTuning>(Path.Combine(DataFile.FindContentRoot(), SightTuning.File));

    /// <summary>A long straight with a long greased stretch on it, the engine this far in.</summary>
    static World Greased(double front, double greaseFrom = 400, double greaseTo = 900)
    {
        var route = new Route.Route("test", RouteTier.Frontier, 1, new LineDefinition("test", [new TrackSegment(6000)]),
            [new RouteFeature(FeatureKind.Grease, greaseFrom, greaseTo)], new RouteWeather(0.01, false, 0, 0), 3600);
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 3, 1)), route.Build(), front);
        var world = new World(train);
        world.EnableLineside(S, route);
        return world;
    }

    /// <summary>Stood on the right-hand running board at its sandbox.</summary>
    static PlayerState AtSandbox(TrainOnLine train)
    {
        var box = train.Frames[0].Shape.Interactables.First(i => i.Kind == InteractableKind.Sandbox && i.Position.X > 0).Position;
        var s = PlayerMotor.SpawnInCab(train, P);
        s.Position = box;
        s.Surface = Surface.Deck;
        return s;
    }

    static void Tick(World world, ref PlayerState s, in PlayerIntent intent)
    {
        world.BeginTick();
        world.CrewAct(ref s, intent, 1);
        world.Step(new TrainControls { Reverser = 1 });
        PlayerMotor.Step(ref s, intent, world.Train, P, T, SimConstants.TickSeconds, applyLook: false);
    }

    [Fact]
    public void TheEngineHasRunningBoardsWithASandboxOnEach()
    {
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 3, 1)), new RailLine(new LineDefinition("t", [new TrackSegment(2000)])), 500);
        var shape = train.Frames[0].Shape;
        var boxes = shape.Interactables.Where(i => i.Kind == InteractableKind.Sandbox).ToList();
        Assert.Equal(2, boxes.Count);
        foreach (var box in boxes)
        {
            // Out past the cab side, on a board at deck height, ahead of the cab.
            Assert.True(Math.Abs(box.Position.X) > shape.HalfWidth);
            var top = shape.TopAt(box.Position.X, box.Position.Z);
            Assert.Equal((T.Geometry.Engine.DeckHeight, SurfaceKind.Deck), top);
            Assert.True(box.Position.Z < shape.Cab!.Value.Min.Z);
        }
    }

    [Fact]
    public void HeldAtTheSandboxTheGripComesBackOverEightSecondsAndGoesWhenItStops()
    {
        var world = Greased(front: 600);
        var s = AtSandbox(world.Train);
        Tick(world, ref s, default);
        Assert.Equal(S.GreaseTraction, world.Train.Traction, 6);
        var use = new PlayerIntent { Buttons = PlayerButtons.Use };
        for (int i = 0; i < S.SandSeconds / 2 * SimConstants.TickRate; i++)
            Tick(world, ref s, use);
        // Halfway there after half the time.
        Assert.Equal(S.GreaseTraction + (1 - S.GreaseTraction) / 2, world.Train.Traction, 1);
        for (int i = 0; i < S.SandSeconds / 2 * SimConstants.TickRate + 2; i++)
            Tick(world, ref s, use);
        Assert.Equal(1, world.Train.Traction, 3);
        // Let go, and the wheels roll on off the sand.
        for (int i = 0; i < S.SandFadeSeconds * SimConstants.TickRate + 2; i++)
            Tick(world, ref s, default);
        Assert.Equal(S.GreaseTraction, world.Train.Traction, 3);
    }

    [Fact]
    public void UseInTheCabDoesntSand()
    {
        var world = Greased(front: 600);
        var s = PlayerMotor.SpawnInCab(world.Train, P);
        for (int i = 0; i < 3 * SimConstants.TickRate; i++)
            Tick(world, ref s, new PlayerIntent { Buttons = PlayerButtons.Use });
        Assert.Equal(0, world.Train.Sand);
        Assert.Equal(S.GreaseTraction, world.Train.Traction, 6);
    }

    [Fact]
    public void TheDriverGoesOutOnTheBoardToSandItThroughAndComesBackIn()
    {
        // Rolling onto a long greased stretch: out it goes and sands, and once through, back into the cab.
        var world = Greased(front: 380, greaseFrom: 400, greaseTo: 900);
        var train = world.Train;
        train.Dynamics.Velocity = 10;
        var bot = new ConductorBot();
        var s = PlayerMotor.SpawnInCab(train, P);
        double best = 0;
        bool outside = false;
        for (uint tick = 0; tick < 90 * SimConstants.TickRate && train.Dynamics.Distance < 1000; tick++)
        {
            var intent = bot.Decide(s, world, tick, out _);
            Tick(world, ref s, intent);
            if (world.Lineside!.OnGrease(train))
                best = Math.Max(best, train.Traction);
            outside |= s.Parent == 0 && !PlayerMotor.InCab(s, train);
        }
        Assert.True(outside);
        Assert.True(best > 0.9, $"best grip on the grease {best:0.00}");
        Assert.True(s.Alive);
        // Back at the controls within a few seconds of the end of it.
        for (uint tick = 0; tick < 10 * SimConstants.TickRate && !PlayerMotor.InCab(s, train); tick++)
            Tick(world, ref s, bot.Decide(s, world, tick, out _));
        Assert.True(PlayerMotor.InCab(s, train));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WhileTheDriversOutSandingTheFiremanHoldsTheTrainToTheBoardsAndWatchesTheRoad(bool sleepers)
    {
        // T105: the brake holds only while someone in the cab holds it, so with the driver out on the board steam pulled the
        // train on unchecked (frontier:11 ran on so onto the Sleepers, and derailed). Just past the grease, a weak bridge's
        // board, or the Sleepers across the rail.
        var route = new Route.Route("test", RouteTier.Frontier, 1, new LineDefinition("test", [new TrackSegment(6000)]),
            [new RouteFeature(FeatureKind.Grease, 400, sleepers ? 820 : 700), sleepers ? new RouteFeature(FeatureKind.Sleepers, 830, 830) : new RouteFeature(FeatureKind.Bridge, 800, 860, MaxCars: 10)],
            new RouteWeather(0.01, false, 0, 0), 3600);
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 3, 1)), route.Build(), 420, Tuning.Boiler);
        var world = new World(train);
        world.EnableLineside(S, route);
        // The host's enemies: the route's Sleepers, laid where it puts them (brought back as a mod would: they're off by
        // default since the director's decision of 2026-10-06, note 265).
        if (sleepers)
            world.EnableEnemies(Tuning.Enemies with { Sleepers = Tuning.Enemies.Sleepers with { Enabled = true } }, route, 1, 2, authority: true);
        train.Dynamics.Velocity = 8;
        var calls = new CrewCalls();
        var driver = new ConductorBot(calls, 0);
        var fireman = new ConductorBot(calls, 1) { Fireman = true };
        var d = PlayerMotor.SpawnInCab(train, P);
        var f = PlayerMotor.SpawnInCab(train, P) with { Position = PlayerMotor.SpawnInCab(train, P).Position + new Double3(-1.2, 0, 0) };
        var c = new TrainControls { Reverser = 1 };
        bool outside = false;
        double overBridge = 0;
        for (uint tick = 0; tick < 150 * SimConstants.TickRate && train.Dynamics.RearDistance < 870; tick++)
        {
            var di = driver.Decide(d, world, tick, out _);
            var fi = fireman.Decide(f, world, tick, out _);
            if (CabControls.Clears(c, train, CabControls.ReleasesBrake(di, d, train) || CabControls.ReleasesBrake(fi, f, train)))
                c.Brake = 0;
            CabControls.Apply(ref c, di, d, train);
            CabControls.Apply(ref c, fi, f, train);
            world.BeginTick();
            world.CrewAct(ref d, di, 1);
            world.CrewAct(ref f, fi, 2);
            world.Step(c);
            PlayerMotor.Step(ref d, di, train, P, T, SimConstants.TickSeconds, applyLook: false);
            PlayerMotor.Step(ref f, fi, train, P, T, SimConstants.TickSeconds, applyLook: false);
            outside |= d.Parent == 0 && !PlayerMotor.InCab(d, train);
            // On the span; or the engine at the Sleepers.
            if (sleepers ? Math.Abs(train.Dynamics.Distance - 830) < 1 : train.Dynamics.Distance > 800 && train.Dynamics.RearDistance < 860)
                overBridge = Math.Max(overBridge, train.Dynamics.Speed);
        }
        Assert.True(outside, "the driver went out to sand");
        Assert.False(world.Derailed, world.DerailCause);
        // Under the board, as the driver holds it (Posted: the strain starts lurchOver past it); or under the Sleepers'
        // derailing speed.
        Assert.InRange(overBridge, 0, sleepers ? Tuning.Enemies.Sleepers.DerailAbove : S.WeakBridgeLimit + S.LurchOver);
    }

    [Fact]
    public void AloneWithSteamDrivingTheDriverIsOutOnTheBoardOnlyWhileTheTrainIsSlow()
    {
        // T107: nobody to mind the controls, and sanded wheels let steam run the train on up to its speed with the driver out
        // on the board (deepTerritory:1 ran onto the Sleepers at 11.8 m/s so). Up a greased climb, slow, it goes out and sands;
        // once the train's going it comes back in.
        var route = new Route.Route("test", RouteTier.Frontier, 1, new LineDefinition("test", [new TrackSegment(300), new TrackSegment(2500, 0, 1.5), new TrackSegment(2000)]),
            [new RouteFeature(FeatureKind.Grease, 350, 2600)], new RouteWeather(0.01, false, 0, 0), 3600);
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 6, 1)), route.Build(), 400, Tuning.Boiler);
        var world = new World(train);
        world.EnableLineside(S, route);
        train.Dynamics.Velocity = 4;
        var driver = new ConductorBot(new CrewCalls(), 0);
        var d = PlayerMotor.SpawnInCab(train, P);
        var c = new TrainControls { Reverser = 1 };
        bool outside = false;
        double fastestOut = 0;
        for (uint tick = 0; tick < 180 * SimConstants.TickRate && train.Dynamics.Distance < 2600; tick++)
        {
            var di = driver.Decide(d, world, tick, out _);
            if (CabControls.Clears(c, train, CabControls.ReleasesBrake(di, d, train)))
                c.Brake = 0;
            CabControls.Apply(ref c, di, d, train);
            world.BeginTick();
            world.CrewAct(ref d, di, 1);
            world.Step(c);
            PlayerMotor.Step(ref d, di, train, P, T, SimConstants.TickSeconds, applyLook: false);
            if (d.Parent == 0 && !PlayerMotor.InCab(d, train))
            {
                outside = true;
                fastestOut = Math.Max(fastestOut, train.Dynamics.Speed);
            }
        }
        Assert.True(outside, "it went out to sand");
        Assert.True(fastestOut < 8, $"out on the board at {fastestOut:0.0} m/s");
    }

    /// <summary>A driver alone, standing up a greased climb with steam driving, the fire door as given.</summary>
    static (World world, ConductorBot driver, PlayerState d) StandingAlone(bool doorOpen, double pressure)
    {
        var route = new Route.Route("test", RouteTier.Frontier, 1, new LineDefinition("test", [new TrackSegment(300), new TrackSegment(2500, 0, 1.5), new TrackSegment(2000)]),
            [new RouteFeature(FeatureKind.Grease, 350, 2600)], new RouteWeather(0.01, false, 0, 0), 3600);
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 6, 1)), route.Build(), 400, Tuning.Boiler);
        var world = new World(train);
        world.EnableLineside(S, route);
        train.Boiler.FireDoorOpen = doorOpen;
        train.Boiler.Pressure = pressure;
        return (world, new ConductorBot(new CrewCalls(), 0), PlayerMotor.SpawnInCab(train, P));
    }

    /// <summary>Ticks of the driver deciding and moving; true if it was ever out of the cab.</summary>
    static bool Out(World world, ConductorBot driver, ref PlayerState d, double seconds)
    {
        var train = world.Train;
        var c = new TrainControls { Reverser = 1, Brake = 1 };
        bool outside = false;
        for (uint tick = 0; tick < seconds * SimConstants.TickRate; tick++)
        {
            var di = driver.Decide(d, world, tick, out _);
            CabControls.Apply(ref c, di, d, train);
            train.Dynamics.Velocity = 0;
            PlayerMotor.Step(ref d, di, train, P, T, SimConstants.TickSeconds, applyLook: false);
            outside |= d.Parent == 0 && !PlayerMotor.InCab(d, train);
        }
        return outside;
    }

    [Fact]
    public void AloneTheDriverShutsTheFireDoorBeforeGoingOutToSand()
    {
        // T81 (deepTerritory:2, a crew of two): only someone in the cab shuts the door after a shovelful, and an open door at
        // a stand lets the Stoker in (App. A.5). Out to sand with it open, the driver sanded on while the fire was put out.
        var (world, driver, d) = StandingAlone(doorOpen: true, pressure: 80);
        Assert.False(Out(world, driver, ref d, 10), "out of the cab with the fire door open");
        world.Train.Boiler.FireDoorOpen = false;
        Assert.True(Out(world, driver, ref d, 10), "out to sand once it's shut");
    }

    [Fact]
    public void WithNoSteamToPullOnTheSandTheDriverStaysInToFireIt()
    {
        var (world, driver, d) = StandingAlone(doorOpen: false, pressure: Tuning.Boiler.PowerFloor);
        Assert.False(Out(world, driver, ref d, 10), "out to sand with the gauge under the power floor");
    }

    [Fact]
    public void AClientHasTheHostsSand()
    {
        var host = Greased(front: 600);
        host.Train.Sand = 0.6;
        var client = Greased(front: 600);
        var controls = new TrainControls();
        WorldRecords.Apply(WorldRecords.Capture(host, controls, []), client, ref controls, []);
        Assert.Equal(0.6, client.Train.Sand, 3);
    }
}
