using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 605 (queue #337; the director, 9 Oct 2026: "plan a little extra time at the end of the run to stop the train, clear the
/// monsters, and then bring it in"): a bot crew coming in on a town's gate with a hound aboard stops short of it, clears the
/// train and brings it in with nothing aboard. With dawn about to break and no stand that puts its rear past the Wakers' wall
/// (they stop short of the gate), the driver runs straight in; a stand inside the wall is safe at dawn, and it stands.
/// </summary>
public class GateClearTests
{
    sealed record Seen(bool Stood, bool HoundPlaced, double FrontAtEnd, World? World);

    /// <summary>
    /// frontier:7's train <paramref name="out_"/> m short of its town's gate, a bot crew of four, and three hounds put aboard car 2
    /// as it comes within <see cref="BoardAt"/> m of the gate (late: a crew of four kills one put aboard any earlier on the way
    /// in). With <paramref name="dawnIn"/>, the run's clock is wound on to that many seconds short of dawn as they board.
    /// </summary>
    static (HarnessReport Report, Seen Seen) Night(double out_, double? dawnIn = null, double seconds = 480, int cars = 4)
    {
        var route = LineGen.Routes.Generate(DataFile.FindContentRoot(), "frontier:7", cars);
        double gate = route.Plan!.Terminus.GateM;
        bool placed = false, stood = false;
        World? last = null;
        var report = CrewOfTwoTests.Night("frontier:7", cars, seconds, null, start: gate - out_, bots: 4, enemies: HoundRunTests.Quiet,
            each: world =>
            {
                last = world;
                var train = world.Train;
                double ahead = gate - train.Dynamics.Distance;
                if (!placed && ahead < BoardAt && world.Run is { Phase: RunPhase.Underway } run)
                {
                    placed = true;
                    if (dawnIn is { } early)
                        run.Resume(run.Route.DawnSeconds - early, -1, train.Boiler.Tender, 0);
                    int car = train.Dynamics.Consist.Vehicles[2].Id;
                    var shape = train.Frames[car].Shape;
                    for (int i = 0; i < 3; i++)
                    {
                        var h = world.AddEnemy(e => new CinderHound(e, 900) { Health = Tough });
                        h.Restore(SpinePhase.Commit, 0, Tough, car,
                            new Double3(i % 2 == 0 ? 0.6 : -0.6, shape.RoofHeight, shape.HalfLength - 2 - i * 1.5), 0, 0, 0.6, 900, 0);
                    }
                }
                stood |= placed && train.Dynamics.Speed < 0.05 && ahead > 0 && ahead < 200;
            });
        return (report, new Seen(stood, placed, last?.Train.Dynamics.Distance - gate ?? double.NaN, last));
    }

    /// <summary>How near the gate (m) the hounds board.</summary>
    const double BoardAt = 100;
    /// <summary>Their health: a pack that takes a minute or so to clear, not one killed before the train's down to the stand.</summary>
    const int Tough = 12;

    [Fact]
    public void WithTimeInHandTheCrewClearsTheTrainShortOfTheGateAndBringsItInClean()
    {
        var (report, seen) = Night(900);
        Assert.True(seen.HoundPlaced);
        var run = Assert.IsType<RunReport>(report.Run);
        Assert.True(seen.Stood, $"never stood short of the gate (front {seen.FrontAtEnd:0} m past it)");
        Assert.Equal(RunEnd.Delivered, run.End);
        Assert.Equal(0, run.BroughtIn);
        Assert.Equal(0, run.BroughtInFees);
    }

    [Fact]
    public void WithDawnAboutToBreakAndNoStandPastTheWakersWallTheDriverRunsStraightIn()
    {
        // Eighteen cars (some 300 m): stood short of the gate, the rear's short of the Wakers' wall (wakers.stopShort, 300 m
        // short of it), and dawn's 20 s off. They'd have it standing: no stand, and the town's brigade gets the hounds.
        var (report, seen) = Night(900, dawnIn: 20, cars: 18);
        Assert.True(seen.HoundPlaced);
        var run = Assert.IsType<RunReport>(report.Run);
        Assert.False(seen.Stood, "stood short of the gate with dawn breaking");
        Assert.Equal(RunEnd.Delivered, run.End);
        Assert.True(run.BroughtIn > 0, "nothing aboard at the gate: the test's hounds were cleared before it");
    }

    [Fact]
    public void AStandWithItsRearPastTheWakersWallHasTimeInHandAtDawn()
    {
        // Four cars stood 30 m short of the gate are inside the Wakers' wall: they stop short of it, so dawn's no reason to run.
        var (_, seen) = Night(900, dawnIn: 20);
        Assert.True(seen.HoundPlaced);
        Assert.True(seen.Stood, $"never stood short of the gate (front {seen.FrontAtEnd:0} m past it)");
    }
}
