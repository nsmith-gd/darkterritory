using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// T110 playtest ("I like being able to walk to the front of the train, have both sides be a gap we can get through"), cab
/// forward (ARCHITECTURE §8 note 276): the cab is the front of the train now, and the way out of it is either side, through
/// its doorway onto the running board and back along the boiler. Nothing of the engine stands ahead of the cab to walk to.
/// </summary>
public class CabWalkTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void FromTheCabYouWalkOutEitherSideAndBackAlongTheBoiler(int side)
    {
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 3, 1)), new RailLine(new LineDefinition("t", [new TrackSegment(2000)])), 500);
        var shape = train.Frames[0].Shape;
        var g = Tuning.Train.Geometry;
        var plan = EnginePlan.Of(g);
        double door = plan.DoorFront + g.Doorway.Width * 0.75, board = side * (shape.HalfWidth + g.Engine.RunningBoardWidth / 2);
        var s = PlayerMotor.SpawnInCab(train, Tuning.Player, side * 0.3);
        // Back across the footplate to the doorway, out of it, and back along the board.
        foreach (var (to, seconds) in new[] { (new Double3(side * 0.9, 0, plan.DoorFront - 0.3), 3.0), (new Double3(side * 0.9, 0, door), 1.5), (new Double3(board, 0, door), 1.5), (new Double3(board, 0, plan.CabBack + 4), 3.0) })
            for (int i = 0; i < seconds * SimConstants.TickRate; i++)
                PlayerMotor.Step(ref s, Toward(s, to), train, Tuning.Player, Tuning.Train, SimConstants.TickSeconds, applyLook: false);
        Assert.Equal(0, s.Parent);
        Assert.Equal(Surface.Deck, s.Surface);
        Assert.True(s.Position.Z > plan.CabBack + 3.5 && Math.Abs(s.Position.X - board) < 0.2, $"stuck at {s.Position} (the cab's back is {plan.CabBack:0.00})");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void AlongTheWalkwayBesideTheBoilerYouWalkStraightIntoTheCab(int side)
    {
        // Note 338 (the director, 7 Oct 2026: "cut open on both sides so players can walk straight to the cab"): the back
        // wall either side of the boiler is open under a lintel, so the walkway between the boiler and the cab's side runs
        // on into the cab, no turn out onto the board and in at the side doorway.
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 3, 1)), new RailLine(new LineDefinition("t", [new TrackSegment(2000)])), 500);
        var shape = train.Frames[0].Shape;
        var g = Tuning.Train.Geometry;
        var plan = EnginePlan.Of(g);
        double walk = side * (g.Engine.BoilerHalfWidth + (shape.HalfWidth - 0.1 - g.Engine.BoilerHalfWidth) / 2);
        var s = PlayerMotor.SpawnInCab(train, Tuning.Player, side * 0.3);
        // (From the deck behind the smokebox, in at the hood's back end and along the corridor beside the boiler.)
        s = s with { Position = new Double3(walk, s.Position.Y, shape.HalfLength - 0.2) };
        for (int i = 0; i < 1.5 * SimConstants.TickRate; i++)
            PlayerMotor.Step(ref s, default, train, Tuning.Player, Tuning.Train, SimConstants.TickSeconds, applyLook: false);
        Assert.Equal(Surface.Deck, s.Surface);
        // Forward along the walkway, through the back wall, into the cab.
        for (int i = 0; i < 9 * SimConstants.TickRate; i++)
            PlayerMotor.Step(ref s, Toward(s, new Double3(walk, 0, plan.CabBack - 1.2)), train, Tuning.Player, Tuning.Train, SimConstants.TickSeconds, applyLook: false);
        Assert.True(PlayerMotor.InCab(s, train), $"stuck at {s.Position} (the cab's back is {plan.CabBack:0.00})");
        // Through the wall and in (on the left the bunker stands a pace inside: in, and up against the coal).
        Assert.True(s.Position.Z < plan.CabBack - 0.6, $"at {s.Position}");
    }

    static PlayerIntent Toward(in PlayerState s, Double3 to)
    {
        var d = (to - s.Position) with { Y = 0 };
        double len = d.Length;
        if (len < 0.05)
            return default;
        double y = s.Yaw;
        double z = (d.X * -Math.Sin(y) + d.Z * -Math.Cos(y)) / len, x = (d.X * Math.Cos(y) + d.Z * -Math.Sin(y)) / len;
        return new PlayerIntent { MoveX = (float)x, MoveZ = (float)z };
    }
}
