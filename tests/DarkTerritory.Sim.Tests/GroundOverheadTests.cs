using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// T66: ground far overhead (a cutting or bore the terrain doesn't know about) never lifts someone off a train. On the
/// procedural line's alternates, a crew on the engine deck stood up on the hill and the train left them.
/// </summary>
public class GroundOverheadTests
{
    sealed class HillOverhead(double above) : ITrackConditions
    {
        public double Ground(Double3 world) => above;
        public double Adhesion(int path, double distance) => 1;
        public double Drag(int path, double distance, double speed) => 0;
    }

    static TrainOnLine Train(double groundY)
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(80_000)])) { Conditions = new HillOverhead(groundY) };
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 5, 1)), line, 2_000);
        train.Dynamics.Velocity = 12;
        return train;
    }

    static void Run(ref PlayerState s, TrainOnLine train, double seconds)
    {
        for (int i = 0; i < seconds * SimConstants.TickRate; i++)
        {
            train.Step(SimConstants.TickSeconds, new TrainControls { Reverser = 1, Throttle = 0.5 });
            PlayerMotor.Step(ref s, default, train, Tuning.Player, Tuning.Train, SimConstants.TickSeconds, applyLook: false);
        }
    }

    [Fact]
    public void OnTheEngineUnderAHillYouStayOnTheEngine()
    {
        var train = Train(groundY: 10);
        var s = PlayerMotor.SpawnInCab(train, Tuning.Player);
        Run(ref s, train, 5);
        Assert.Equal(0, s.Parent);
        Assert.Equal(Surface.Deck, s.Surface);
        Assert.True(s.Alive);
    }

    [Fact]
    public void OnARoofUnderAHillYouStayOnTheRoof()
    {
        var train = Train(groundY: 12);
        var s = PlayerMotor.SpawnOnRoof(train, 2, 0, Tuning.Player);
        Run(ref s, train, 5);
        Assert.Equal(2, s.Parent);
        Assert.Equal(Surface.Roof, s.Surface);
    }

    [Fact]
    public void OffTheTrainTheEarthStillCatchesYouHoweverFarUpItIs()
    {
        // Nothing of the train underfoot: nobody falls through the earth.
        var train = Train(groundY: 10);
        var s = PlayerMotor.SpawnOnGround(train.Frames[^1].Origin + new Double3(30, 0, 30), train.Line, 2_000, Tuning.Player);
        s.Position = s.Position with { Y = 5 };
        Run(ref s, train, 1);
        Assert.Equal(PlayerState.World, s.Parent);
        Assert.Equal(10, s.Position.Y, 3);
    }
}
