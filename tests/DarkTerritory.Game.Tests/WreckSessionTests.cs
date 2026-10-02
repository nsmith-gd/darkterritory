using Ballast;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>T117: the derailment reaches a client as poses, and the cinematic holds the run's end back while it plays.</summary>
public class WreckSessionTests
{
    static readonly string Content = DataFile.FindContentRoot();

    [Fact]
    public void ADerailmentPlaysItsCinematicThenTheRunEnds()
    {
        using var night = NetPlaySession.HostGame(Content, new SessionSetup(Route: "frontier:7", Cars: 4, Enemies: false), port: 0);
        for (int i = 0; i < 10; i++)
            night.Step(default);
        Assert.False(night.WreckCinematic);
        night.Host!.World.Derail("test");
        for (int i = 0; i < 10; i++)
            night.Step(default);
        // The client's train is the host's wreck's puppet, posed from the snapshots.
        Assert.NotNull(night.Train.Wreck);
        Assert.True(night.WreckCinematic);
        var tuning = DataFile.Load<WreckTuning>(Path.Combine(Content, WreckTuning.File));
        Assert.Equal(tuning.CinematicSeconds, night.World.WreckTuning.CinematicSeconds);
        for (int i = 0; i < tuning.CinematicSeconds * SimConstants.TickRate; i++)
            night.Step(default);
        Assert.False(night.WreckCinematic);
    }
}
