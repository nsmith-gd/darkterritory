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
            Position = new Double3(-0.3, T.Geometry.CouplerHeight, shape.HalfLength + 0.5),
            Surface = Surface.Coupler,
            Health = P.Health,
            Yaw = Math.PI, // facing +Z, towards the back of the train
            LineHint = train.Cars[ahead].FrontDistance,
        };
    }

    static readonly PlayerIntent Forward = new() { MoveZ = 1 };
    static readonly PlayerIntent Use = new() { Buttons = PlayerButtons.Use };

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
