using Ballast;
using Ballast.Net;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The link's loss (note 534; netcode-audit.md gap 3, spec E "ping visibility is load-bearing"): of the newest numbers in a
/// window, the share that never came or came stale, counted on the host's snapshots at a joiner and on a crewmate's inputs
/// at the host.
/// </summary>
public class LinkLossTests
{
    [Fact]
    public void EverythingArrivingIsNoLossAndNothingYetIsNoAnswer()
    {
        var loss = new LinkLoss(10);
        Assert.Null(loss.Loss);
        for (uint n = 1; n <= 25; n++)
            loss.Heard(n);
        Assert.Equal(0, loss.Loss);
        Assert.Equal(10, loss.Span);
    }

    [Fact]
    public void OneInFourMissingIsAQuarterOverTheWindow()
    {
        var loss = new LinkLoss(100);
        for (uint n = 1; n <= 400; n++)
            if (n % 4 != 0)
                loss.Heard(n);
        Assert.Equal(0.25, loss.Loss!.Value, 2);
    }

    [Fact]
    public void ALateArrivalIsLostAndADuplicateCountsOnce()
    {
        var loss = new LinkLoss(10);
        foreach (uint n in new uint[] { 1, 2, 3, 5, 4, 6, 6, 7, 8, 9, 10 })
            loss.Heard(n);
        // 4 came after 5: no use by then, so lost as far as the game's concerned.
        Assert.Equal(0.1, loss.Loss!.Value, 6);
    }

    [Fact]
    public void ASilenceLongerThanTheWindowIsAllLostAndItRecovers()
    {
        var loss = new LinkLoss(10);
        for (uint n = 1; n <= 10; n++)
            loss.Heard(n);
        loss.Heard(50);
        Assert.Equal(0.9, loss.Loss!.Value, 6);
        for (uint n = 51; n <= 60; n++)
            loss.Heard(n);
        Assert.Equal(0, loss.Loss);
    }

    [Fact]
    public void AStreamStartingAgainFromLowStartsTheCountAgain()
    {
        // A crewmate coming back on a new connection counts its inputs from 1 again.
        var loss = new LinkLoss(30);
        for (uint n = 1000; n <= 1100; n++)
            loss.Heard(n);
        loss.Heard(1);
        loss.Heard(2);
        Assert.Equal(2, loss.Span);
        Assert.Equal(0, loss.Loss);
    }

    [Fact]
    public void ItSettlesOnlyOnceASecondsBeenCounted()
    {
        var loss = new LinkLoss(10 * SimConstants.TickRate);
        loss.Heard(1);
        loss.Heard(3);
        Assert.NotNull(loss.Loss);
        Assert.Null(loss.Settled);
        for (uint n = 4; n <= SimConstants.TickRate; n++)
            loss.Heard(n);
        Assert.NotNull(loss.Settled);
    }

    static readonly TrainTuning T = Tuning.Train;
    static readonly PlayerTuning P = Tuning.Player;
    static readonly RailLine TestLoop = RailLine.Load(Path.Combine(DataFile.FindContentRoot(), "lines/test-loop.json"));
    static TrainOnLine Train() => new(new TrainDynamics(Consist.Uniform(T, 4, 1)), TestLoop, 600);

    static (double? Snapshots, double? Inputs) Measured(LinkConditions conditions)
    {
        var net = new LoopbackNetwork(seed: 3, conditions);
        var host = new HostSession(net.CreateHost(), Train(), T, P);
        var client = new ClientSession(net.CreateClient(), Train(), T, P);
        for (int t = 0; t < 20 * SimConstants.TickRate; t++)
        {
            net.Advance(SimConstants.TickSeconds);
            host.Step();
            client.Step(default);
        }
        return (client.SnapshotLoss.Settled, Assert.Single(host.Links).InputLoss);
    }

    [Fact]
    public void APerfectLinkLosesNothingEitherWay()
    {
        var (snapshots, inputs) = Measured(LinkConditions.Perfect);
        Assert.Equal(0, snapshots);
        Assert.Equal(0, inputs);
    }

    [Fact]
    public void ALossyLinkIsMeasuredBothWays()
    {
        // One datagram in ten dropped, each way: about a tenth of the snapshots at the joiner, and of the inputs at the host.
        var (snapshots, inputs) = Measured(new LinkConditions(0.05, 0, 0.10));
        Assert.InRange(snapshots!.Value, 0.06, 0.14);
        Assert.InRange(inputs!.Value, 0.06, 0.14);
    }

    [Fact]
    public void JitterThatReordersSnapshotsCountsTheStaleOnes()
    {
        // Nothing dropped, but jitter wider than a tick: snapshots overtake each other, and the ones that come after a newer
        // one are no use (the joiner skips them), so they're lost to the game.
        var (snapshots, _) = Measured(new LinkConditions(0.08, 0.04, 0));
        Assert.True(snapshots > 0.02, $"{snapshots}");
    }
}
