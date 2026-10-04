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
        // Without a film to show (none shot yet), the orbit; with one, the film (note 177), and the sequence is as long as its cut.
        Assert.Equal(DerailBeat.Orbit, DerailSequence.Beat(tuning, tuning.FirstPersonSeconds + tuning.ReplaySeconds + 0.1));
        var film = WaitForFilm(night);
        Assert.Equal(DerailBeat.Film, DerailSequence.Beat(tuning, tuning.FirstPersonSeconds + tuning.ReplaySeconds + 0.1, film));
        Assert.Equal(tuning.FirstPersonSeconds + tuning.ReplaySeconds + film.CutLength, DerailSequence.Length(tuning, film), 9);
        for (int i = 0; i < DerailSequence.Length(tuning, film) * SimConstants.TickRate; i++)
            night.Step(default);
        Assert.False(night.WreckCinematic);
    }

    static WreckFilm WaitForFilm(NetPlaySession night)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (night.Film is null && clock.Elapsed.TotalSeconds < 60)
        {
            night.Step(default);
            Thread.Sleep(5);
        }
        return night.Film ?? throw new Xunit.Sdk.XunitException("no film");
    }

    [Fact]
    public void TheFilmIsTheCrewsAndTheHostCanSkipItToTheCauseCard()
    {
        // GDD v1.4 App. E.5 (note 177): the host's crew in it, a shot each, then the cause card; after the first player's shot
        // the host's vote alone skips to the cause card, never past it.
        using var night = NetPlaySession.HostGame(Content, new SessionSetup(Route: "frontier:7", Cars: 4, Enemies: false), port: 0);
        for (int i = 0; i < 20; i++)
            night.Step(default);
        night.Host!.World.Derail("took the 45 km/h bend at 72 km/h, 27 km/h too fast");
        var film = WaitForFilm(night);
        var t = night.World.WreckTuning;
        Assert.Single(film.Start.Players);
        Assert.Equal(night.PlayerId, film.Start.Players[0].Id);
        Assert.Equal([ShotKind.Player, ShotKind.Settle, ShotKind.Cause], film.Cut.Select(s => s.Kind));
        // Not skippable in the first person, the replay, or the first player's own shot.
        Assert.False(night.Skippable);
        // (The app sends the vote only once it counts: Skippable.)
        while (DerailSequence.FilmSeconds(t, night.WreckSeconds) < film.SkippableFrom - 0.1)
        {
            Assert.False(night.Skippable);
            night.Step(default);
        }
        while (DerailSequence.FilmSeconds(t, night.WreckSeconds) < film.SkippableFrom + 0.1)
            night.Step(default);
        Assert.True(night.Skippable);
        // The vote goes to the host and the skip comes back on a snapshot: over real sockets, so by the clock, not a step count
        // (a slow runner took more than 10 steps for the round trip).
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!night.World.FilmSkipped && clock.Elapsed.TotalSeconds < 10)
        {
            night.Step(new Sim.Player.PlayerIntent { Actions = night.Skippable ? Sim.Player.PlayerActions.Skip : 0 });
            Thread.Sleep(5);
        }
        Assert.True(night.World.FilmSkipped);
        night.Step(default);
        Assert.Equal(ShotKind.Cause, film.CutAt(DerailSequence.FilmSeconds(t, night.WreckSeconds))!.Value.Shot.Kind);
        Assert.False(night.Skippable);
    }

    [Fact]
    public void TheHostDrawsTheOperaAndItsClientPlaysTheSameTrack()
    {
        // GDD v1.4 App. E.6 (note 174): the host draws from the bag it brought, and the track goes out with the world.
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
