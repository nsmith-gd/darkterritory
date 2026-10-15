using Ballast;
using Ballast.Render;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// Bringing them home at the crosshair and in the corner (note 723): "HAUL UP : HOLD [E]" at a friend running beside the moving
/// car, "CARRY : HOLD [E]" at one in front, and over the shoulder or carrying one, the corner's keys.
/// </summary>
public class RescueHudTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly PlayerTuning P = DataFile.Load<PlayerTuning>(Path.Combine(Content, PlayerTuning.File));
    static readonly TrainTuning T = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));

    sealed class Seat(World world, int id, PlayerState me, IReadOnlyList<(int Id, PlayerState State)> crew) : IPlaySession
    {
        public TrainOnLine Train => world.Train;
        public World World => world;
        public Sim.Route.Route? Route => null;
        public PlayerState Player => me;
        public TrainControls Controls => default;
        public long Tick => 0;
        public PlayerTuning PlayerTuning => P;
        public int PlayerId => id;
        public int Watching => -1;
        public IReadOnlyList<(int Id, PlayerState State)> CrewStates(double alpha) => [(id, me), .. crew];
        public string Status() => "";
        public void Step(in PlayerIntent intent) { }
        public IReadOnlyList<CarFrame> InterpolatedFrames(double alpha) => [];
        public Camera EyeCamera(IReadOnlyList<CarFrame> frames, double alpha, double pendingYaw, double pendingPitch) => default;
        public IReadOnlyList<Crewmate> Crew(IReadOnlyList<CarFrame> frames, double alpha) => [];
    }

    static World Night(double speed)
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(40_000)]));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 4, 1)), line, 2_000);
        train.Dynamics.Velocity = speed;
        return new World(train) { Movement = P };
    }

    [Fact]
    public void AFriendBesideTheMovingCarIsOfferedTheHandUp()
    {
        var world = Night(5);
        var train = world.Train;
        int car = Staging.SideDoorCar(train);
        train.Vehicles[car].DoorsOpen |= 1 << 3;
        var frame = train.Frames[car];
        double w = frame.Shape.HalfWidth, floor = T.Geometry.Interior!.FloorHeight;
        var me = PlayerMotor.StandUp(train, car, new Double3(w - 0.3, floor, 0), train.Cars[car].FrontDistance, P) with { Yaw = -Math.PI / 2, Pitch = -0.85 };
        var spot = frame.ToWorld(new Double3(w + T.Geometry.Interior.StepWidth + 0.35, 0, 0.2));
        var runner = PlayerMotor.SpawnOnGround(spot, train.Line, train.Cars[car].FrontDistance, P) with { Velocity = frame.Velocity };
        Assert.Equal("HAUL UP : HOLD [E]", Hud.Prompt(new Seat(world, 1, me, [(2, runner)])));
        // Looked away from her, it's the door's again.
        Assert.NotEqual("HAUL UP : HOLD [E]", Hud.Prompt(new Seat(world, 1, me with { Yaw = 0, Pitch = 0 }, [(2, runner)])));
    }

    [Fact]
    public void AFriendInFrontIsOfferedTheCarryAndTheCornerSaysHowToBeRidOfThem()
    {
        var world = Night(0);
        var train = world.Train;
        var spot = train.Frames[1].ToWorld(new Double3(train.Frames[1].Shape.HalfWidth + 4, 0, 0));
        var me = PlayerMotor.SpawnOnGround(spot, train.Line, train.Cars[1].FrontDistance, P) with { Yaw = 0 };
        var friend = PlayerMotor.SpawnOnGround(spot + new Double3(0, 0, -0.8), train.Line, train.Cars[1].FrontDistance, P);
        Assert.Equal("CARRY : HOLD [E]   SHOVE : [LEFT ALT]", Hud.Prompt(new Seat(world, 1, me, [(2, friend)])));
        world.CrewMischief = false;
        Assert.Equal("CARRY : HOLD [E]", Hud.Prompt(new Seat(world, 1, me, [(2, friend)])));
        // Carrying her: nothing at the crosshair; the corner has the keys, the throw only with the switch on.
        var carrying = me with { Flags = PlayerFlags.Carrying | PlayerFlags.Heavy };
        Assert.Null(Hud.Prompt(new Seat(world, 1, carrying, [(2, friend)])));
        Assert.Equal(["PUT DOWN : [E]"], Hud.Hints(new Seat(world, 1, carrying, [(2, friend)])).Lines);
        world.CrewMischief = true;
        Assert.Equal(["PUT DOWN : [E]", "THROW : [T]"], Hud.Hints(new Seat(world, 1, carrying, [(2, friend)])).Lines);
        // Carried: the way off the shoulder.
        var carried = friend with { Flags = PlayerFlags.Carried };
        var (head, lines) = Hud.Hints(new Seat(world, 2, carried, [(1, carrying)]));
        Assert.Equal("CARRIED", head);
        Assert.Equal(["WRIGGLE FREE : [SPACE]"], lines);
        Assert.Null(Hud.Prompt(new Seat(world, 2, carried, [(1, carrying)])));
    }
}
