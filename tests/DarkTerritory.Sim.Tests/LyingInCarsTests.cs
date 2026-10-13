using Ballast;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 659 (the director, 13 Oct 2026: "we want the loot we put in the cars to persist once in there so poor stacking becomes
/// an obstacle for players passing through. We want it lethal company style"): what's lying still in a car is solid. A crate
/// in the aisle is walked into and stops you; a small find is stepped over; a crate is stood on.
/// </summary>
public class LyingInCarsTests
{
    static readonly TrainTuning T = Tuning.Train;
    static readonly PlayerTuning P = Tuning.Player;
    const double Dt = SimConstants.TickSeconds;
    const int Car = 1;

    /// <summary>A standing train, its bodies on, and a thing of <paramref name="kind"/> put down in car 1's aisle, lain still.</summary>
    static (World World, Body Thing) WithAThingInTheAisle(BodyKind kind, double radius)
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(20_000)]));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 4, 0)), line, 1_000);
        var world = new World(train, Tuning.Combat);
        world.EnableBodies();
        double floor = T.Geometry.Interior!.FloorHeight;
        var thing = world.Bodies.Put(train, Car, kind, new Double3(0, floor + radius, 0), radius, 0.35, 0.05);
        for (int i = 0; i < 3 * SimConstants.TickRate; i++)
            Tick(world, []);
        Assert.True(thing.Pbd.Asleep, "never lay still");
        return (world, thing);
    }

    static void Tick(World world, IReadOnlyCollection<(int, PlayerState)> crew)
    {
        world.Train.Dynamics.Velocity = 0;
        world.Train.Step(Dt, default);
        world.StepBodies(crew);
    }

    /// <summary>Stood in car 1's aisle 3 m ahead of its middle, facing back down it (+Z), walking on for so long.</summary>
    static PlayerState WalkDownTheAisle(World world, double seconds)
    {
        double floor = T.Geometry.Interior!.FloorHeight;
        var s = new PlayerState
        {
            Parent = Car,
            Position = new Double3(0, floor, -3),
            Surface = Surface.Deck,
            Health = P.Health,
            Yaw = Math.PI,
            LineHint = world.Train.Cars[Car].FrontDistance,
        };
        var forward = new PlayerIntent { MoveZ = 1 };
        for (int i = 0; i < seconds * SimConstants.TickRate; i++)
        {
            Tick(world, [(1, s)]);
            PlayerMotor.Step(ref s, forward, world.Train, P, T, Dt);
        }
        return s;
    }

    [Fact]
    public void ACrateLyingInTheAisleIsWalkedIntoAndStopsYou()
    {
        var (world, crate) = WithAThingInTheAisle(BodyKind.Cargo, 0.45);
        var (car, box) = Assert.Single(world.Train.Lying);
        Assert.Equal(Car, car);
        Assert.Equal(0.9, box.Max.Y - box.Min.Y, 2);

        var s = WalkDownTheAisle(world, 4);
        Assert.Equal(Car, s.Parent);
        // Stopped against its near face, not through it.
        Assert.InRange(s.Position.Z, -0.45 - P.Radius - 0.05, -0.45 - P.Radius + 0.01);

        // Picked up, it's not in the way.
        crate.Carrier = 2;
        Tick(world, []);
        Assert.Empty(world.Train.Lying);
        crate.Carrier = -1;
    }

    [Fact]
    public void ASmallFindIsSteppedOverAndACrateStoodOn()
    {
        // A toy (0.3 m high, under player.json stepUp) is no wall: on down the aisle, over it.
        var (world, _) = WithAThingInTheAisle(BodyKind.Toy, 0.15);
        Assert.Single(world.Train.Lying);
        var s = WalkDownTheAisle(world, 4);
        Assert.Equal(Car, s.Parent);
        Assert.True(s.Position.Z > 1, $"held up by a toy at {s.Position}");

        // A crate's top is a deck: stood on it, you're on it, not sunk into it.
        var (onCrate, _) = WithAThingInTheAisle(BodyKind.Cargo, 0.45);
        double floor = T.Geometry.Interior!.FloorHeight;
        var up = new PlayerState { Parent = Car, Position = new Double3(0, floor + 0.95, 0), Surface = Surface.Air, Health = P.Health, LineHint = onCrate.Train.Cars[Car].FrontDistance };
        for (int i = 0; i < SimConstants.TickRate; i++)
        {
            Tick(onCrate, [(1, up)]);
            PlayerMotor.Step(ref up, default, onCrate.Train, P, T, Dt);
        }
        Assert.Equal(Surface.Deck, up.Surface);
        Assert.Equal(floor + 0.9, up.Position.Y, 2);
    }

    /// <summary>A bot in car 1's aisle 3 m ahead of its middle, steered for a point 3 m behind it, heeding what's lying there.</summary>
    static PlayerState BotDownTheAisle(World world, double seconds, out int stood)
    {
        double floor = T.Geometry.Interior!.FloorHeight;
        var s = new PlayerState
        {
            Parent = Car,
            Position = new Double3(0, floor, -3),
            Surface = Surface.Deck,
            Health = P.Health,
            Yaw = Math.PI,
            LineHint = world.Train.Cars[Car].FrontDistance
        };
        stood = 0;
        for (int i = 0; i < seconds * SimConstants.TickRate; i++)
        {
            Tick(world, [(1, s)]);
            var (step, _) = WarmUp.Steer(s, new Double3(0, floor, 3), Math.PI);
            var heeded = Heed.Lying(step, s, world, P);
            if (heeded.MoveX == 0 && heeded.MoveZ == 0)
                stood++;
            PlayerMotor.Step(ref s, heeded, world.Train, P, T, Dt);
        }
        return s;
    }

    [Fact]
    public void ABotGoesRoundACrateInTheAisleAndStandsWhereTheAisleIsStackedShut()
    {
        // One crate in the middle of the aisle: round it, on to where it was going.
        var (world, _) = WithAThingInTheAisle(BodyKind.Cargo, 0.45);
        var s = BotDownTheAisle(world, 8, out _);
        Assert.True(s.Position.Z > 2.5, $"never got past the crate: {s.Position}");

        // The aisle stacked shut, wall to wall: it stands short of it, not pushing at it.
        var (shut, _) = WithAThingInTheAisle(BodyKind.Cargo, 0.45);
        var room = shut.Train.Frames[Car].Shape.Interior!.Value;
        double floor = T.Geometry.Interior!.FloorHeight;
        for (double x = room.Min.X + 0.45; x < room.Max.X; x += 0.9)
            if (Math.Abs(x) > 0.5)
                shut.Bodies.Put(shut.Train, Car, BodyKind.Cargo, new Double3(x, floor + 0.45, 0), 0.45, 0.35, 0.05);
        for (int i = 0; i < 3 * SimConstants.TickRate; i++)
            Tick(shut, []);
        var blocked = BotDownTheAisle(shut, 6, out int stood);
        Assert.True(blocked.Position.Z < -0.5, $"through the stack: {blocked.Position}");
        Assert.True(stood > 3 * SimConstants.TickRate, $"stood only {stood} ticks of 6 s");
    }
}
