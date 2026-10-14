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
        var line = LineGen.Routes.Generate(DataFile.FindContentRoot(), "local:17", 10).Build();
        // Its last alternate (deepTerritory:1's J6 when the line was 37 km; at one 24 km length, note 270, J4, 1,173 m up it;
        // on the 7 km nights of note 554, where alternates are few and short, local:17's, about 800 m long), most of the way
        // up it.
        int alt = Enumerable.Range(0, line.Branches.Count).Last(i => line.Branches[i].Definition.Kind == Rail.BranchKind.Alternate);
        double toe = line.Branches[alt].Toe, up = Math.Min(1_173, line.Branches[alt].Definition.Length - 150);
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 10, 1)), line, toe - 670);
        var world = new World(train);
        world.SetSwitch(alt, true);
        while (train.Dynamics.Distance < toe + up)
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
        // The main line's count there is another (by 50 m and more up a 24 km night's alternates; up a 7 km night's, under a
        // kilometre long, by a car's length or so): more than half a car out, as a LineHint would have it.
        var g = Tuning.Train.Geometry;
        Assert.True(Math.Abs(hint - (d.RearDistance + 12)) > g.CarLength / 2, $"the main line's count is {hint:0} there, the train's {d.RearDistance + 12:0}");
    }
}
