using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// T121 playtest: "we appear to have derailed at a very low speed. Something doesn't seem to be functioning correctly with
/// derailment", and "if derailment happens people should know they took the corner too hard and by how much". A derailing
/// Switchman throws the points under a train at any speed it's moving, which at a crawl splits them instead (the engine's
/// wrenched and brought up short); over that it's off the rails, and the cause says at what speed.
/// </summary>
public class DerailCauseTests
{
    static (World World, TrainOnLine Train, int Branch) AtBlackwellJunction(double speed)
    {
        var line = LineGen.Routes.Generate(DataFile.FindContentRoot(), "frontier:7", 4).Build();
        int branch = Enumerable.Range(0, line.Branches.Count).OrderBy(i => Math.Abs(line.Branches[i].Toe - 6850)).First();
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 4, 1)), line, line.Branches[branch].Toe - 600);
        var world = new World(train, Tuning.Combat);
        var quiet = Tuning.Enemies with { Director = Tuning.Enemies.Director with { GraceSeconds = 1e9 } };
        world.EnableEnemies(quiet, route: null, 1, crew: 2, authority: true);
        world.AddEnemy(i => Switchman.At(i, line.Branches[branch], quiet.Switchman, 2.6, derail: true));
        train.Dynamics.Velocity = speed;
        return (world, train, branch);
    }

    static void RunPast(World world, TrainOnLine train, int branch, double speed)
    {
        double toe = train.Line.Branches[branch].Toe;
        for (int i = 0; i < 200 * SimConstants.TickRate && !world.Derailed && train.Dynamics.RearDistance < toe + 5; i++)
        {
            if (train.Dynamics.Velocity > 0.01)
                train.Dynamics.Velocity = speed;
            world.BeginTick();
            world.Step(new TrainControls { Reverser = 1 });
            if (train.Dynamics.Velocity < 0.01 && train.Dynamics.Distance > toe)
                break;
        }
    }

    [Fact]
    public void PointsThrownUnderACrawlingTrainSplitAndDoNotDerailIt()
    {
        double crawl = Tuning.Enemies.Switchman.DerailAbove - 1.5;
        var (world, train, branch) = AtBlackwellJunction(crawl);
        double before = train.Vehicles[0].Integrity;
        RunPast(world, train, branch, crawl);
        Assert.False(world.Derailed, world.DerailCause);
        Assert.True(train.Vehicles[0].Integrity < before, "the engine takes the split points");
    }

    [Fact]
    public void PointsThrownUnderAFastTrainDerailItAndSayHowFast()
    {
        double fast = Tuning.Enemies.Switchman.DerailAbove + 4;
        var (world, train, branch) = AtBlackwellJunction(fast);
        RunPast(world, train, branch, fast);
        Assert.True(world.Derailed);
        Assert.Matches(@"^the Switchman threw the points under it at \d+ km/h \(over \d+ km/h they throw a train off\)$", world.DerailCause);
    }
}
