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
