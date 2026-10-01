using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// T107: the land beside an alternate's climb read a hand's breadth over the engine's cab floor (deepTerritory:1, Grieve
/// Junction), and the motor, taking the earth for whatever's highest underfoot, set the driver and fireman down on it at
/// 13 m/s. Riding the train, it's the train underfoot while any of it is.
/// </summary>
public class CabRideTests
{
    [Fact]
    public void StandingInTheCabUpTheGrieveJunctionAlternateTheyStayInTheCab()
    {
        var route = LineGen.Routes.Generate(DataFile.FindContentRoot(), "deepTerritory:1", 10);
        var line = route.Build();
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 10, 1)), line, 9200);
        var world = new World(train);
        int alt = Enumerable.Range(0, line.Branches.Count).Single(i => Math.Abs(line.Branches[i].Toe - 9320) < 1);
        world.SetSwitch(alt, true);
        var cab = PlayerMotor.SpawnInCab(train, Tuning.Player);
        var roof = PlayerMotor.SpawnOnRoof(train, 3, 0, Tuning.Player);
        while (train.Dynamics.Distance < 10500)
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
}
