using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 484 (queue #221): the harness's packs aboard. Each pack that boarded, how long it stayed and how its hounds ended,
/// for measuring the bots against a pack that moves (D1's #208).
/// </summary>
public class PackReportTests
{
    [Fact]
    public void APacksHoundsAreCountedKilledCutLooseOffOrStillAboard()
    {
        var n = new Night(6, speed: 10);
        var train = n.Train;
        int rear = train.Dynamics.Consist.Vehicles[^1].Id;
        var health = Tuning.Enemies.CinderHounds.Health;
        CinderHound Hound(int id, SpinePhase phase, double hp, int car)
        {
            var h = new CinderHound(id, 900);
            h.Restore(phase, 0, hp, car, new Double3(0, 4, 0), 0, 0, 0, 900, 0);
            return h;
        }
        var killed = Hound(1, SpinePhase.Gone, 0, 2);
        var cut = Hound(2, SpinePhase.Gone, health, rear);
        var off = Hound(3, SpinePhase.Gone, health, -1);
        var aboard = Hound(4, SpinePhase.Commit, health, 2);
        Assert.True(train.Uncouple(train.VehicleAhead(rear)));
        var report = Harness.PackAboard(900, (30 * SimConstants.TickRate, 75 * SimConstants.TickRate, [killed, cut, off, aboard]), train);
        Assert.Equal(new PackAboardReport(900, 30, 45, 4, 1, 1, 1, 1), report);
    }
}
