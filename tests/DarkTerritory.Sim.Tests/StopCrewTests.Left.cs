using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Sim.Tests;

public partial class StopCrewTests
{
    [Fact]
    public void AtAStopsPointsTheTrainWaitsForSomeonePlayingStillOnTheGround()
    {
        // Note 995 (the director's playtest of 23 Oct, line 3: "I've thrown the spur. Oh my god, and the train does not stop and
        // wait for me to get on"): set for the spur and cut, the driver went in once its bots were aboard (calls.Riding is
        // theirs), whoever was playing still down at the points. It waits for them, as it does to leave a stop (note 935).
        var night = new Night(cars: 6, walkers: 1, modules: ModuleKind.Crates);
        var train = night.Train;
        var stops = night.Driver.Stops!;
        night.Until(() => stops.Doing == StopDriver.Leg.Held, 600);
        Assert.Equal(StopDriver.Leg.Held, stops.Doing);
        var shape = train.Frames[2].Shape;
        int person = night.Add(new Waiting(), PlayerMotor.SpawnOnGround(train.Frames[2].ToWorld(new Ballast.Double3(shape.HalfWidth + 1.2, 0, 0)),
            train.Line, train.Cars[2].FrontDistance, P)) - 1;
        bool inWithThemDown = false;
        night.Until(() => inWithThemDown, 240, () =>
        {
            night.Driver.Players = [night.Crew[person]];
            inWithThemDown |= stops.Doing == StopDriver.Leg.SpurIn;
        });
        Assert.False(inWithThemDown, "went in down the spur with someone playing still on the ground");
        // Aboard (up on a roof), and in it goes.
        night.Crew[person] = PlayerMotor.SpawnOnRoof(train, 1, 0, P);
        night.Until(() => stops.Doing == StopDriver.Leg.SpurIn, 180, () => night.Driver.Players = [night.Crew[person]]);
        Assert.Equal(StopDriver.Leg.SpurIn, stops.Doing);
    }
}
