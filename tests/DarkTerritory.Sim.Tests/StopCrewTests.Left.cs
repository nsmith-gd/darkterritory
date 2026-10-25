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

    /// <summary>
    /// Note 1009 (the director's playtest of 23 Oct, line 6: "these guys blew through everything and we didn't stop to actually
    /// play the game"; line 3: "I've thrown the spur"): a stop the driver's reckoning turns down (the night's dawn set so it has
    /// a hundred seconds less than the stop's allowance as it comes up to it, found from the same night with no dawn to speak
    /// of), its spur thrown for it by the crew ahead of the train. The driver makes the stop. With facilities.json
    /// crew.crewsStops off, it's a switch set wrong, stood short of and set back, as before.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AStopsSpurThrownByTheCrewIsThatStopAskedForAndMade(bool crewsStops)
    {
        var open = new Night(cars: 6, walkers: 0, dawn: 1e6);
        var openStops = open.Driver.Stops!;
        double spare = double.NaN;
        open.Until(() => openStops.Doing != StopDriver.Leg.Cruise, 900, () => spare = openStops.Spare);
        Assert.Equal(StopDriver.Leg.Approach, openStops.Doing);
        var plan = openStops.Plan!;
        double allowance = StopDriver.Allowance(plan, open.World);
        var f = FacilityTests.F with { Crew = FacilityTests.F.Crew with { CrewsStops = crewsStops } };
        var night = new Night(cars: 6, walkers: 0, dawn: 1e6 - (spare - (allowance - 100)), facilities: f);
        var stops = night.Driver.Stops!;
        // Turned down by the reckoning, its spur not thrown: it runs on past.
        var passing = new Night(cars: 6, walkers: 0, dawn: 1e6 - (spare - (allowance - 100)), facilities: f);
        passing.Until(() => passing.Train.Dynamics.Distance > plan.Hold + 50 || passing.Driver.Stops!.Doing != StopDriver.Leg.Cruise, 300);
        Assert.Equal(StopDriver.Leg.Cruise, passing.Driver.Stops!.Doing);
        // Thrown for it by the crew, the train still 600 m short.
        Assert.True(night.World.SetSwitch(plan.Spur.Index, true));
        var legs = new HashSet<StopDriver.Leg>();
        night.Until(() => stops.Doing == StopDriver.Leg.SpurIn || stops.Doing == StopDriver.Leg.SetBack, 400, () => legs.Add(stops.Doing));
        if (crewsStops)
        {
            Assert.True(stops.Doing == StopDriver.Leg.SpurIn, $"{stops.Doing} (legs {string.Join(", ", legs)}), the shunter {((RoofWalkerBot)night.Bots[1]).Job!.Doing}");
            Assert.Equal(plan.Key, stops.Plan!.Key);
        }
        else
        {
            Assert.Equal(StopDriver.Leg.SetBack, stops.Doing);
            Assert.DoesNotContain(StopDriver.Leg.Approach, legs);
        }
    }
}
