using Ballast;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// T117: the derailment reaches a client as poses, and the cinematic holds the run's end back while it plays. T121: the
/// sequence is first person, then the replay, then the orbit, and the run's end waits for all three.
/// </summary>
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
        Assert.Equal(tuning.SequenceSeconds, night.World.WreckTuning.SequenceSeconds);
        Assert.Equal(DerailBeat.FirstPerson, DerailSequence.Beat(tuning, night.WreckSeconds));
        Assert.Equal(DerailBeat.Replay, DerailSequence.Beat(tuning, tuning.FirstPersonSeconds + 0.1));
        Assert.Equal(DerailBeat.Orbit, DerailSequence.Beat(tuning, tuning.FirstPersonSeconds + tuning.ReplaySeconds + 0.1));
        for (int i = 0; i < tuning.SequenceSeconds * SimConstants.TickRate; i++)
            night.Step(default);
        Assert.False(night.WreckCinematic);
    }

    [Fact]
    public void TheHostDrawsTheOperaAndItsClientPlaysTheSameTrack()
    {
        // GDD v1.4 App. E.6 (note 173): the host draws from the bag it brought, and the track goes out with the world.
        var bag = new Sim.Music.MusicBag { Left = ["doom-dies-irae", "lament-vesti-la-giubba"], Last = "gallop-infernal-galop" };
        using var night = NetPlaySession.HostGame(Content, new SessionSetup(Route: "frontier:7", Cars: 4, Enemies: false) { MusicBag = bag }, port: 0);
        for (int i = 0; i < 10; i++)
            night.Step(default);
        Assert.Equal(0u, night.World.DerailMusic);
        night.Host!.World.Derail("test");
        for (int i = 0; i < 10; i++)
            night.Step(default);
        uint drawn = night.Host.World.DerailMusic;
        Assert.NotEqual(0u, drawn);
        Assert.Equal(drawn, night.World.DerailMusic);
        var track = Sim.Music.MusicManifest.Load(Content).ByKey(drawn)!;
        Assert.Contains(track.Id, bag.Left);
        Assert.Equal(track.Id, night.MusicBag!.Last);
        Assert.Single(night.MusicBag.Left);
    }
}
