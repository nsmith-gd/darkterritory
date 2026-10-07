using Ballast;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// T81: the driver stands for a crewmate left on the ground (T96) by where they are along the train's own path. A LineHint
/// is along the main line, and on an alternate that's another count: deepTerritory:1's driver set back again and again on
/// the J6 alternate for crew a couple of hundred metres "behind" who were beside his own rear car.
/// </summary>
public class LeftBehindAlongTests
{
    [Fact]
    public void BesideTheRearCarOnAnAlternateIsBesideTheRearCar()
    {
        var line = LineGen.Routes.Generate(DataFile.FindContentRoot(), "deepTerritory:1", 10).Build();
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 10, 1)), line, 21_000);
        var world = new World(train);
        int alt = Enumerable.Range(0, line.Branches.Count).Single(i => Math.Abs(line.Branches[i].Toe - 21_670) < 1);
        world.SetSwitch(alt, true);
        // Its rear car where the alternate runs 250 m from the main line (note 278's bends moved where it's close).
        while (train.Dynamics.Distance < 23_563)
        {
            train.Dynamics.Velocity = 12;
            world.BeginTick();
            world.Step(new TrainControls { Reverser = 1 });
        }
        var d = train.Dynamics;
        Assert.Equal(alt, d.Path);
        var rail = line.Sample(d.Path, d.RearDistance + 12);
        var beside = rail.Position + Double3.Cross(rail.Tangent, Double3.Up).Normalized * 2.5;
        Assert.InRange(ConductorBot.AlongTheTrain(train, beside), d.RearDistance + 11, d.RearDistance + 13);
        double hint = 0;
        line.Nearest(beside, ref hint);
        Assert.True(Math.Abs(hint - (d.RearDistance + 12)) > 50, $"the main line's count is {hint:0} there, the train's {d.RearDistance + 12:0}");
    }
}
