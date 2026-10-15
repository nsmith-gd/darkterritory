using DarkTerritory.Sim.Net;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Queue #532, note 820 (AU1's audio audit, note 801's night): the bot driver held its speed by pulsing the full brake. The
/// brake's a held button, all or nothing, and with steam driving (T97) the engine pulls at full effort the moment it's off,
/// so let off as soon as the train was under the speed it wanted, it was back on within a tick or two: frontier:7 with 4 bots
/// and enemies applied it 1,471 times in 8 minutes (345 a minute moving, held 0.19 s each), every one heard as the shoes and
/// the slack running in. On a line plan a brake that's on is held now: about a move's top on its open stretch
/// (<see cref="Bots.StopDriver.ShuntBand"/>), about the open line's cruise, and on the curve to a mark till well under it
/// (<see cref="Bots.StopDriver.LetOffUnder"/>).
/// </summary>
public class DriverBrakeTests
{
    [Fact]
    public void ABotNightsBrakeIsHeldNotTapped()
    {
        // The open line at the cruise, then the first stop: run in on the curve, the set-back for the spur and the shunt.
        var report = CrewOfTwoTests.Night("frontier:7", 6, 300, null, bots: 4);
        var driving = report.Driving!;
        Assert.True(driving.Applications > 0, "the night never braked");
        Assert.True(driving.PerMinute <= 20, $"{driving.PerMinute} applications a minute moving ({driving.Applications} in {driving.MovingSeconds} s)");
        Assert.True(driving.Taps <= 2, $"{driving.Taps} of {driving.Applications} applications let off inside a second");
        Assert.True(driving.MeanSeconds >= 1.5, $"held {driving.MeanSeconds} s each");
    }
}
