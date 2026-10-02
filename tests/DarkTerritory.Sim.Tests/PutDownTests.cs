using Ballast;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Things put down or thrown inside a car stay in it (ARCHITECTURE §8 note 173). The playtest's lost items: faced up to a
/// wall, your hands are past it (the reach is longer than your body is wide, and the wall's a tenth of a metre), and what
/// you set down there was pushed out of the wall's far side onto the ballast; a thrown one went through it between ticks.
/// Out through an open door is still out.
/// </summary>
public class PutDownTests
{
    static readonly PlayerTuning P = Tuning.Player;

    static World Train(RailLine? line = null, double start = 1_000)
    {
        line ??= new RailLine(new LineDefinition("t", [new TrackSegment(50_000)]));
        var world = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 4, 1)), line, start));
        world.EnableBodies();
        world.Stock();
        return world;
    }

    /// <summary>Car 2 (no lockers): standing at <paramref name="at"/> on its floor, facing <paramref name="yaw"/>, a lamp in hand.</summary>
    static (PlayerState S, Body Lamp) Holding(World world, Double3 at, double yaw, int car = 2)
    {
        var room = world.Train.Frames[car].Shape.Interior!.Value;
        var s = new PlayerState { Parent = car, Position = at with { Y = room.Min.Y + 0.1 }, Yaw = yaw, Surface = Surface.Deck, Health = P.Health };
        var lamp = world.Bodies.All.First(b => b.Kind == BodyKind.Lamp);
        lamp.Carrier = 1;
        world.StepBodies([(1, s)]);
        return (s, lamp);
    }

    /// <summary>A press of a button, then two seconds of the world going on (the train at <paramref name="speed"/>).</summary>
    static void PressAndWait(World world, ref PlayerState s, PlayerButtons button, double speed = 0, double seconds = 2)
    {
        for (int i = 0; i < seconds * SimConstants.TickRate; i++)
        {
            world.Train.Dynamics.Velocity = speed;
            world.BeginTick();
            world.CrewAct(ref s, i == 0 ? new PlayerIntent { Buttons = button } : default, 1);
            world.Step(new TrainControls { Reverser = 1 });
            world.StepBodies([(1, s)]);
        }
    }

    /// <summary>In the car's room, come to rest: on its floor, or (<paramref name="onTheLoad"/>) on the load down its right side.</summary>
    static void AssertInside(World world, Body b, int car, bool onTheLoad = false)
    {
        var shape = world.Train.Frames[car].Shape;
        var room = shape.Interior!.Value;
        Assert.Equal(car, b.Parent);
        Assert.True(room.Contains(b.Centre), $"{b.Kind} at {b.Centre}, outside {room.Min}..{room.Max}");
        double under = onTheLoad ? shape.TopAt(b.Centre.X, b.Centre.Z, b.Centre.Y)!.Value.Top : room.Min.Y + 0.1;
        Assert.True(b.Centre.Y - under is >= 0 and <= 0.3, $"{b.Kind} at {b.Centre}, {b.Centre.Y - under:0.###} over what's under it");
    }

    [Fact]
    public void PutDownFacingASideWallItStaysInTheCar()
    {
        var world = Train();
        var room = world.Train.Frames[2].Shape.Interior!.Value;
        // Up against the left wall behind the side door (a body's width from it), facing it.
        var (s, lamp) = Holding(world, new Double3(room.Min.X + P.Radius + 0.01, 0, 3.0), Math.PI / 2);
        PressAndWait(world, ref s, PlayerButtons.Use);
        Assert.Null(world.Bodies.CarriedBy(1));
        AssertInside(world, lamp, 2);
    }

    [Fact]
    public void PutDownAtACarsEndAgainstItsShutDoorItStaysInTheCar()
    {
        var world = Train();
        var room = world.Train.Frames[2].Shape.Interior!.Value;
        double doorX = Tuning.Train.Geometry.Interior!.DoorX;
        var (s, lamp) = Holding(world, new Double3(doorX, 0, room.Min.Z + P.Radius + 0.01), 0);
        PressAndWait(world, ref s, PlayerButtons.Use);
        AssertInside(world, lamp, 2);
        // And in the corner beside it.
        (s, lamp) = Holding(world, new Double3(room.Min.X + P.Radius + 0.01, 0, room.Min.Z + P.Radius + 0.01), Math.PI / 4);
        PressAndWait(world, ref s, PlayerButtons.Use);
        AssertInside(world, lamp, 2);
    }

    [Fact]
    public void ThrownAtAWallOnAMovingTrainRoundACurveItStaysInTheCar()
    {
        // A long left-hand curve (200 m radius) at 15 m/s: the car's frame turning under it every tick.
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(1_000), new TrackSegment(3_000, Radius: 200)]));
        var world = Train(line, 1_400);
        Assert.True(Math.Abs(world.Train.Line.Sample(world.Train.Cars[2].FrontDistance).Curvature) > 1e-3);
        var room = world.Train.Frames[2].Shape.Interior!.Value;
        foreach (var (at, yaw) in new[] { (new Double3(0, 0, 3.0), Math.PI / 2), (new Double3(0, 0, 3.0), -Math.PI / 2), (new Double3(-0.45, 0, room.Max.Z - 1.5), Math.PI) })
        {
            var (s, lamp) = Holding(world, at, yaw);
            s.Pitch = 0.1;
            PressAndWait(world, ref s, PlayerButtons.Throw, speed: 15);
            AssertInside(world, lamp, 2, onTheLoad: yaw < 0);
            // Set down against the wall as it goes, too.
            (s, lamp) = Holding(world, at with { X = yaw > 0 ? room.Min.X + P.Radius + 0.01 : at.X }, yaw);
            PressAndWait(world, ref s, PlayerButtons.Use, speed: 15);
            AssertInside(world, lamp, 2, onTheLoad: yaw < 0);
        }
    }

    [Fact]
    public void OutThroughAnOpenDoorItGoes()
    {
        var world = Train();
        var room = world.Train.Frames[2].Shape.Interior!.Value;
        world.Train.Vehicles[2].ToggleDoor(0);
        double doorX = Tuning.Train.Geometry.Interior!.DoorX;
        var (s, lamp) = Holding(world, new Double3(doorX, 0, room.Min.Z + P.Radius + 0.01), 0);
        PressAndWait(world, ref s, PlayerButtons.Throw);
        Assert.False(room.Contains(world.Train.Frames[2].ToLocal(Bodies.WorldCentre(lamp, world.Train))));
    }
}
