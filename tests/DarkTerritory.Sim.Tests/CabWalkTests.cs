using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// T110 playtest ("I like being able to walk to the front of the train, have both sides be a gap we can get through"): out
/// of the cab's front past the boiler on either side, onto the engine's deck alongside it.
/// </summary>
public class CabWalkTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void FromTheCabYouWalkForwardPastTheBoilerOnEitherSide(int side)
    {
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 3, 1)), new RailLine(new LineDefinition("t", [new TrackSegment(2000)])), 500);
        var shape = train.Frames[0].Shape;
        var boiler = shape.Solids.First(s => s.Part == PartKind.Boiler).Box;
        double cabFront = shape.Cab!.Value.Min.Z, gangway = (boiler.Max.X + shape.HalfWidth - 0.1) / 2;
        var s = PlayerMotor.SpawnInCab(train, Tuning.Player, side * gangway);
        for (int i = 0; i < 4 * SimConstants.TickRate; i++)
            PlayerMotor.Step(ref s, new PlayerIntent { MoveZ = 1 }, train, Tuning.Player, Tuning.Train, SimConstants.TickSeconds, applyLook: false);
        Assert.Equal(0, s.Parent);
        Assert.Equal(Surface.Deck, s.Surface);
        Assert.True(s.Position.Z < cabFront - 1.5, $"stuck at z {s.Position.Z:0.00} (the cab's front is {cabFront:0.00})");
    }
}
