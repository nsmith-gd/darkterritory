using Ballast;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>Walk-in cars (GDD §10 "the back door", §26 protected versus exposed).</summary>
public class InteriorTests
{
    static readonly TrainTuning T = Tuning.Train;
    static readonly PlayerTuning P = Tuning.Player;
    static readonly InteriorLayout I = T.Geometry.Interior!;
    const double Dt = SimConstants.TickSeconds;

    static TrainOnLine Train(double speed = 8)
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(20_000)]));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 4, 1)), line, 1_000);
        train.Dynamics.Velocity = speed;
        return train;
    }

    static void Run(TrainOnLine train, ref PlayerState s, double seconds, Func<PlayerState, PlayerIntent> intent)
    {
        double speed = train.Dynamics.Velocity;
        for (int i = 0; i < seconds * SimConstants.TickRate; i++)
        {
            train.Dynamics.Velocity = speed;
            train.Step(Dt, default);
            var it = intent(s);
            CrewActions.Apply(ref s, it, train, Dt);
            PlayerMotor.Step(ref s, it, train, P, T, Dt);
        }
    }

    /// <summary>On the coupler plate in front of the guard van, lined up with its front door, facing it.</summary>
    static PlayerState OnPlateFacingGuard(TrainOnLine train)
    {
        int ahead = train.Dynamics.Consist.Vehicles[^2].Id;
        var shape = train.Frames[ahead].Shape;
        return new PlayerState
        {
            Parent = ahead,
            Position = new Double3(T.Geometry.PlateX, T.Geometry.CouplerHeight, shape.HalfLength + 0.5),
            Surface = Surface.Coupler,
            Health = P.Health,
            Yaw = Math.PI, // facing +Z, towards the back of the train
            LineHint = train.Cars[ahead].FrontDistance,
        };
    }

    static readonly PlayerIntent Forward = new() { MoveZ = 1 };
    static readonly PlayerIntent Use = new() { Buttons = PlayerButtons.Use };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ACargoCarsSideDoorTakesYouInOffItsSteps(bool carrying)
    {
        // Spec D.2: freight goes in from the ground, and you can't climb with it. So a cargo car has steps up its side to a
        // sliding door: walk up them (arms full or not), open it, and you're in.
        var train = Train(speed: 0);
        const int car = 1;
        var frame = train.Frames[car];
        double w = frame.Shape.Bounds.Max.X, sd = I.SideDoorWidth / 2;
        var foot = frame.ToWorld(new Double3(w + I.StepWidth / 2, 0, -sd - 4 * I.StepDepth - 0.3));
        var s = PlayerMotor.SpawnOnGround(foot, train.Line, train.Cars[car].FrontDistance, P);
        s.Yaw = Math.Atan2(-frame.Back.X, -frame.Back.Z); // facing along the car toward its middle
        if (carrying)
            s.Flags |= PlayerFlags.Heavy;
        Run(train, ref s, 4, x => x.Parent == car && x.Position.Z > -0.2 ? default : Forward);
        Assert.Equal(car, s.Parent);
        Assert.Equal(Surface.Deck, s.Surface);
        Assert.Equal(I.FloorHeight, s.Position.Y, 6);
        Assert.False(PlayerMotor.Indoors(s, train));

        // Turn to face the door (looking in across the car, −X), open it, and walk in.
        int door = frame.Shape.DoorList.Single(d => d.Box.Centre.X > 0 && Math.Abs(d.Box.Centre.Z) < 1).Index;
        Run(train, ref s, 1, x => new PlayerIntent { LookYaw = (float)Math.Clamp(Math.IEEERemainder(Math.PI / 2 - x.Yaw, 2 * Math.PI), -0.3, 0.3) });
        Run(train, ref s, I.DoorSeconds + 0.1, _ => Use);
        Assert.True(train.Vehicles[car].DoorOpen(door));
        Run(train, ref s, 1.5, _ => Forward);
        Assert.True(PlayerMotor.Indoors(s, train), $"{s.Surface} on {s.Parent} at {s.Position}");
        // A side door open lets the cold in like any other.
        Assert.Equal(PlayerMotor.Outside, PlayerMotor.Space(s, train));
    }

    /// <summary>
    /// The plate across a coupling gap bridges end door to end door: it spans both doorways, and the end ladders stand
    /// clear of its edge, so stepping off it is stepping through a door.
    /// </summary>
    /// <summary>
    /// One doorway (train.json doorway, ARCHITECTURE §8 note 110): every door on the train, end and side, on every kind of
    /// car, and the cab's doorways, are the one height over their floor, with a crewmate's head well under it.
    /// </summary>
    [Fact]
    public void EveryDoorwayIsTheStandardDoorway()
    {
        var g = T.Geometry;
        double h = g.Doorway.Height;
        Assert.True(h >= P.Height + 0.2, "a crewmate walks through upright");
        foreach (var kind in new[] { VehicleKind.Cargo, VehicleKind.Guard, VehicleKind.Utility })
            foreach (bool behind in new[] { true, false })
            {
                var shape = CarShape.Build(g, kind, behind);
                Assert.NotEmpty(shape.DoorList);
                foreach (var door in shape.DoorList)
                {
                    Assert.Equal(h, door.Box.Max.Y - door.Box.Min.Y, 6);
                    Assert.Equal(I.FloorHeight, door.Box.Min.Y, 6);
                    // The end doors are the standard's width; the side doors are wider, for crates.
                    double width = Math.Max(door.Box.Max.X - door.Box.Min.X, door.Box.Max.Z - door.Box.Min.Z);
                    Assert.True(Math.Abs(width - g.Doorway.Width) < 1e-6 || Math.Abs(width - I.SideDoorWidth) < 1e-6, $"{kind} door {door.Index}: {width} m");
                }
            }
        // The cab's doorways: open, but under the same lintel, and the cab roof well over it.
        var engine = CarShape.Build(g, VehicleKind.Engine, true);
        var lintels = engine.Solids.Where(s => s.Part == PartKind.CabWall && s.Box.Min.Y > g.Engine.DeckHeight + 1.5).ToList();
        Assert.Equal(2, lintels.Count);
        Assert.All(lintels, l => Assert.Equal(g.Engine.DeckHeight + h, l.Box.Min.Y, 6));
        // The standard's width (cab forward, note 267; before, the back corner pillar took 0.15 of it).
        Assert.All(lintels, l => Assert.Equal(g.Doorway.Width, l.Box.Max.Z - l.Box.Min.Z, 6));
    }

    [Fact]
    public void CouplerPlatesLineUpWithTheEndDoors()
    {
        var train = Train();
        for (int i = 0; i + 1 < train.Frames.Count; i++)
        {
            var (ahead, behind) = (train.Frames[i].Shape, train.Frames[i + 1].Shape);
            var plate = ahead.Solids.Single(x => x.Part == PartKind.Coupler).Box;
            // Every door into a gap: the rear one of the car ahead (the engine has none), the front one of the car behind.
            var doors = ahead.DoorList.Where(d => d.Box.Max.Z >= ahead.HalfLength - 1e-6)
                .Concat(behind.DoorList.Where(d => d.Box.Min.Z <= -behind.HalfLength + 1e-6)).ToList();
            Assert.NotEmpty(doors);
            foreach (var door in doors)
            {
                Assert.True(plate.Min.X <= door.Box.Min.X && plate.Max.X >= door.Box.Max.X, $"gap {i}: plate {plate.Min.X:0.00}..{plate.Max.X:0.00} doesn't span door {door.Box.Min.X:0.00}..{door.Box.Max.X:0.00}");
                Assert.Equal(door.Box.Centre.X, plate.Centre.X, 6);
            }
            foreach (var ladder in ahead.Ladders.Where(l => l.Foot.Z > ahead.HalfLength).Concat(behind.Ladders.Where(l => l.Foot.Z < -behind.HalfLength)))
                Assert.True(ladder.Foot.X - 0.2 > plate.Max.X, $"gap {i}: ladder at {ladder.Foot.X:0.00} stands on the plate");
        }
    }

    [Fact]
    public void AShutDoorStopsYouAndAnOpenOneLetsYouIn()
    {
        var train = Train();
        int guard = train.Dynamics.Consist.Vehicles[^1].Id;
        var s = OnPlateFacingGuard(train);
        Run(train, ref s, 2, _ => Forward);
        Assert.Equal(guard - 1, s.Parent);
        Assert.Equal(Surface.Coupler, s.Surface);

        // Open it from the plate (it's the next car's door), then walk through.
        Run(train, ref s, I.DoorSeconds + 0.1, _ => Use);
        Assert.True(train.Vehicles[guard].DoorOpen(0));
        Run(train, ref s, 1.5, _ => Forward);
        Assert.Equal(guard, s.Parent);
        Assert.Equal(Surface.Deck, s.Surface);
        Assert.True(PlayerMotor.Indoors(s, train));
        // With a door open, inside is still outside as far as the night is concerned.
        Assert.Equal(PlayerMotor.Outside, PlayerMotor.Space(s, train));
    }

    [Fact]
    public void ShutTheDoorBehindYouAndYouAreSheltered()
    {
        var train = Train();
        int guard = train.Dynamics.Consist.Vehicles[^1].Id;
        var s = OnPlateFacingGuard(train);
        Run(train, ref s, 1, _ => Forward);
        Run(train, ref s, I.DoorSeconds + 0.1, _ => Use);
        Run(train, ref s, 1.5, _ => Forward);
        // Turn round and shut it.
        s.Yaw = 0;
        Run(train, ref s, 2, x => x.Position.Z > -train.Frames[guard].Shape.HalfLength + 0.55 ? Forward : default);
        Run(train, ref s, I.DoorSeconds + 0.1, _ => Use);
        Assert.Equal(0, train.Vehicles[guard].DoorsOpen);
        Assert.True(guard == PlayerMotor.Space(s, train), $"parent {s.Parent} at {s.Position} on {s.Surface}, doors {train.Vehicles[guard].DoorsOpen}");
    }

    [Fact]
    public void JumpingIndoorsHitsTheCeilingNotTheRoof()
    {
        var train = Train(speed: 20);
        int guard = train.Dynamics.Consist.Vehicles[^1].Id;
        var s = new PlayerState { Parent = guard, Position = new Double3(I.DoorX, I.FloorHeight, 0), Surface = Surface.Deck, Health = P.Health, LineHint = train.Cars[guard].FrontDistance };
        double highest = 0;
        int tick = 0;
        Run(train, ref s, 4, x =>
        {
            highest = Math.Max(highest, train.Frames[guard].ToLocal(PlayerMotor.WorldPosition(x, train)).Y);
            // Keep jumping for three seconds, then land.
            return ++tick < 3 * SimConstants.TickRate ? new PlayerIntent { Buttons = PlayerButtons.Jump } : default;
        });
        Assert.Equal(guard, s.Parent);
        Assert.True(PlayerMotor.Indoors(s, train));
        Assert.True(highest + P.Height <= T.Geometry.CarHeight - I.RoofThickness + 1e-6, $"head reached {highest + P.Height}");
    }

    [Fact]
    public void TheRoofIsStillAWalkwayTheLengthOfTheCar()
    {
        var train = Train();
        var s = PlayerMotor.SpawnOnRoof(train, 2, -6, P);
        s.Yaw = Math.PI;
        Run(train, ref s, 2, _ => Forward);
        Assert.Equal(Surface.Roof, s.Surface);
        Assert.Equal(2, s.Parent);
        Assert.InRange(s.Position.Y, T.Geometry.CarHeight - 1e-6, T.Geometry.CarHeight + 1e-6);
    }

    [Fact]
    public void DoorsReplicate()
    {
        var host = new World(Train());
        host.Train.Vehicles[2].ToggleDoor(1);
        var controls = new TrainControls();
        var records = WorldRecords.Capture(host, controls, []);
        var client = new World(Train());
        WorldRecords.Apply(records, client, ref controls, []);
        Assert.True(client.Train.Vehicles[2].DoorOpen(1));
        Assert.False(client.Train.Vehicles[2].DoorOpen(0));
    }
}
