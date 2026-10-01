using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// A roof walker pacing a standing train jumps its coupling gaps as it does a moving one (T102's walk aboard at the gate
/// showed it going over the side at every gap, down to the ballast and back up the next car's ladder).
/// </summary>
public class RoofWalkTests
{
    static readonly PlayerTuning P = Tuning.Player;

    [Theory]
    [InlineData(0)]
    [InlineData(12)]
    public void PacingTheRoofsItJumpsTheGapsAndStaysUpThere(double speed)
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(40_000)]));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), line, 5_000);
        train.Dynamics.Velocity = speed;
        var world = new World(train);
        var walker = new RoofWalkerBot(3);
        var s = PlayerMotor.SpawnOnRoof(train, 2, 0, P);
        int down = 0, cars = 0, was = s.Parent;
        var path = new System.Text.StringBuilder();
        string last = "";
        for (int i = 0; i < 60 * SimConstants.TickRate; i++)
        {
            var intent = walker.Decide(s, world, world.Tick, out _);
            world.BeginTick();
            world.Step(new TrainControls { Reverser = 1 });
            train.Dynamics.Velocity = speed;
            bool before = s.Parent == PlayerState.World;
            PlayerMotor.Step(ref s, intent, train, P, Tuning.Train, SimConstants.TickSeconds, applyLook: true);
            if (!before && s.Parent == PlayerState.World && s.Surface == Surface.Ground)
                down++;
            if (s.Parent > 0 && s.Parent != was)
                cars++;
            if (s.Parent >= 0)
                was = s.Parent;
            string now = $"{s.Parent}{s.Surface.ToString()[0]}";
            if (now != last)
                path.Append($" {i / 30.0:0.0}:{now}@{s.Position.Z:0.0}");
            last = now;
        }
        Assert.True(cars >= 3, $"it went from car to car ({cars}):{path}");
        Assert.True(down == 0, $"it went down onto the ballast {down} times");
    }
}
