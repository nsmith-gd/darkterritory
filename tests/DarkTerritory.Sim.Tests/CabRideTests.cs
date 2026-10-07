using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// T107: the land beside an alternate's climb read a hand's breadth over the engine's cab floor (deepTerritory:1, Grieve
/// Junction, when the line was 37 km; at one 24 km length, note 270, its first alternate), and the motor, taking the earth for whatever's highest underfoot, set the driver and fireman down on it at
/// 13 m/s. Riding the train, it's the train underfoot while any of it is.
/// </summary>
public class CabRideTests
{
    [Fact]
    public void StandingInTheCabUpTheGrieveJunctionAlternateTheyStayInTheCab()
    {
        var route = LineGen.Routes.Generate(DataFile.FindContentRoot(), "deepTerritory:1", 10);
        var line = route.Build();
        int alt = Enumerable.Range(0, line.Branches.Count).First(i => line.Branches[i].Definition.Kind == Rail.BranchKind.Alternate);
        double toe = line.Branches[alt].Toe;
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 10, 1)), line, toe - 120);
        var world = new World(train);
        world.SetSwitch(alt, true);
        var cab = PlayerMotor.SpawnInCab(train, Tuning.Player);
        var roof = PlayerMotor.SpawnOnRoof(train, 3, 0, Tuning.Player);
        while (train.Dynamics.Distance < toe + 1180)
        {
            train.Dynamics.Velocity = 13;
            world.BeginTick();
            world.Step(new TrainControls { Reverser = 1 });
            train.Dynamics.Velocity = 13;
            PlayerMotor.Step(ref cab, default, train, Tuning.Player, Tuning.Train, SimConstants.TickSeconds);
            PlayerMotor.Step(ref roof, default, train, Tuning.Player, Tuning.Train, SimConstants.TickSeconds);
            Assert.True(cab.Parent == 0 && cab.Surface == Surface.Deck, $"set down at {train.Dynamics.Distance:0} on path {train.Dynamics.Path}: {cab.Parent} {cab.Surface} {cab.Position}");
            Assert.Equal(3, roof.Parent);
        }
        Assert.Equal(alt, train.Dynamics.Path);
    }

    /// <summary>
    /// T107: where frontier:3's alternate climbs up beside main to rejoin it, both rails' formations cover the alternate's
    /// rail, and the first edge's (main, 2.8 m higher) won it: the crew jumping the roof gaps there came down on that and
    /// were left behind, and the driver stood for them again and again till the dawn. The nearer rail's formation wins.
    /// </summary>
    [Fact]
    public void WhereAnAlternateClimbsBesideMainItsRailIsOnItsOwnFormation()
    {
        // Every alternate's run in to its rejoin (frontier:3's, at one 24 km length: note 270).
        var line = LineGen.Routes.Generate(DataFile.FindContentRoot(), "frontier:3", 10).Build();
        var alts = Enumerable.Range(0, line.Branches.Count).Where(i => line.Branches[i].Kind == Rail.BranchKind.Alternate).ToList();
        Assert.NotEmpty(alts);
        foreach (int alt in alts)
        {
            double end = line.Branches[alt].Toe + line.Branches[alt].Definition.Length;
            for (double d = end - 300; d <= end - 100; d += 5)
            {
                var rail = line.Sample(alt, d).Position;
                double hint = 0;
                Assert.True(Math.Abs(PlayerMotor.GroundAt(rail, line, ref hint) - rail.Y) < 0.05, $"ground over alternate {alt}'s rail at {d}");
            }
        }
    }
}
