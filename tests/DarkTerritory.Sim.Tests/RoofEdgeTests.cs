using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The director's decision of 2026-10-06 (GDD App. F.1, build 1121: "way too easy to fall off the train"; note 266): walking
/// on a moving train never takes you off it. Falling comes from a jump, a hit, a grab, or walking off a side you're facing.
/// </summary>
public class RoofEdgeTests
{
    static readonly PlayerTuning P = Tuning.Player;

    /// <summary>Cruise speed round curves both ways: a long straight, then S-bends.</summary>
    static TrainOnLine Train(double speed)
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(3_000), new TrackSegment(400, Radius: 250), new TrackSegment(400, Radius: -250),
            new TrackSegment(400, Radius: 250), new TrackSegment(40_000)]));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), line, 2_900);
        train.Dynamics.Velocity = speed;
        return train;
    }

    static void Tick(World world, TrainOnLine train, ref PlayerState s, in PlayerIntent intent, double speed)
    {
        world.BeginTick();
        world.Step(new TrainControls { Reverser = 1 });
        train.Dynamics.Velocity = speed;
        PlayerMotor.Step(ref s, intent, train, P, Tuning.Train, SimConstants.TickSeconds, applyLook: true);
    }

    [Theory]
    [InlineData(12)]
    [InlineData(20)]
    public void WalkingTheRoofsAndGapsAtCruiseNobodyFallsOff(double speed)
    {
        var train = Train(speed);
        var world = new World(train);
        var rng = new Pcg32(7, 1);
        int falls = 0;
        // Each car in turn: wander its roof (a sidestep, a run, a turn about), and stand in its gap shuffling side to side.
        for (int car = 1; car < train.Frames.Count - 1; car++)
        {
            var s = PlayerMotor.SpawnOnRoof(train, car, 0, P);
            for (int i = 0; i < 20 * SimConstants.TickRate; i++)
            {
                double half = train.Frames[s.Parent].Shape.HalfLength;
                // Along the car and back, never jumping, drifting sideways as much as forward, sometimes at a run.
                bool back = (i / (3 * SimConstants.TickRate)) % 2 == 1;
                var intent = new PlayerIntent
                {
                    MoveZ = 1,
                    MoveX = (float)(Math.Sin(i * 0.07) * 0.9 + (rng.NextDouble() - 0.5) * 0.4),
                    Buttons = rng.NextDouble() < 0.3 ? PlayerButtons.Run : 0,
                };
                // Facing along the car (the way a walker goes), back the other way half the time, or crabbing past the end.
                s.Yaw = back ? Math.PI : 0;
                if (Math.Abs(s.Position.Z) > half + 0.2)
                    s.Yaw = s.Position.Z > 0 ? 0 : Math.PI;
                Tick(world, train, ref s, intent, speed);
                if (s.Parent == PlayerState.World || s.Surface == Surface.Air)
                {
                    falls++;
                    break;
                }
            }
        }
        Assert.Equal(0, falls);
    }

    [Fact]
    public void ASidestepInACouplingGapAtCruiseHolds()
    {
        const double speed = 15;
        var train = Train(speed);
        var world = new World(train);
        var g = Tuning.Train.Geometry;
        var shape = train.Frames[2].Shape;
        var s = PlayerMotor.SpawnOnRoof(train, 2, 0, P) with { Position = new Double3(g.PlateX, g.CouplerHeight, shape.HalfLength + g.CouplingGap / 2), Surface = Surface.Coupler };
        for (int i = 0; i < 10 * SimConstants.TickRate; i++)
        {
            var intent = new PlayerIntent { MoveX = (i / 45) % 2 == 0 ? 1 : -1 };
            Tick(world, train, ref s, intent, speed);
            Assert.True(s.Parent != PlayerState.World && s.Surface != Surface.Air, $"off the plate at {i / 30.0:0.0} s: {s.Position}");
        }
    }

    [Fact]
    public void FacingTheEdgeAndWalkingOffItStillGoesOver()
    {
        const double speed = 4;
        var train = Train(speed);
        var world = new World(train);
        // Facing the right side (+X in the car: yaw −90° looks along +X) and walking forward.
        var s = PlayerMotor.SpawnOnRoof(train, 2, 0, P) with { Yaw = -Math.PI / 2 };
        bool over = false;
        for (int i = 0; i < 5 * SimConstants.TickRate && !over; i++)
        {
            Tick(world, train, ref s, new PlayerIntent { MoveZ = 1 }, speed);
            over = s.Parent == PlayerState.World || s.Surface == Surface.Air;
        }
        Assert.True(over, "a deliberate step off the side goes over it");
    }

    [Fact]
    public void AJumpOffTheSideStillGoesOver()
    {
        const double speed = 4;
        var train = Train(speed);
        var world = new World(train);
        var s = PlayerMotor.SpawnOnRoof(train, 2, 0, P);
        bool over = false;
        for (int i = 0; i < 5 * SimConstants.TickRate && !over; i++)
        {
            Tick(world, train, ref s, new PlayerIntent { MoveX = 1, Buttons = PlayerButtons.Jump }, speed);
            over = s.Parent == PlayerState.World;
        }
        Assert.True(over, "a jump off the side goes over it");
    }
}
