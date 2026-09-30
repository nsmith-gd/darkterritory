using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The pace on a generated line (T74): its grace stretch holds the director off only as long as the director's own grace
/// ("out of the gate in 20 s", after the playtest), not for however long the crew takes over its two kilometres.
/// </summary>
public class PacingTests
{
    static readonly string Content = DataFile.FindContentRoot();

    [Fact]
    public void TheLinesGraceBanGivesWayToTheDirectorsGrace()
    {
        var route = Routes.Generate(Content, "frontier:7", 6);
        double at = route.Plan!.GateM + 300;
        Assert.Contains("grace", route.Plan.Director.TagsAt(at));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), route.Build(), at);
        train.Dynamics.Velocity = 12;
        var world = new World(train, Tuning.Combat);
        world.EnableRun(Tuning.Run, route, route.GateOr(0), authority: true);
        world.EnableEnemies(Tuning.Enemies, route, 1, crew: 4, authority: true);
        var director = world.Director!;
        double grace = Tuning.Enemies.Director.LineGraceSeconds;

        // Just out of the gate: the stretch's ban holds.
        world.Run!.Resume(grace / 2, -1, train.Boiler.Tender, 0);
        Assert.Null(director.Decide(world, 100, [], 500));
        Assert.Equal("banned (grace)", director.HeldBecause);
        // The director's grace over: the stretch is still easy going, but it's no longer the director's to sit out.
        world.Run.Resume(grace + 10, -1, train.Boiler.Tender, 0);
        director.Decide(world, 101, [], 500);
        Assert.NotEqual("banned (grace)", director.HeldBecause);
    }
}
