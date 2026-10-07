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
        // The session's own sequence (its first person is this player's, to their death in the film), not the file's: they
        // differ, and the steps taken waiting on the film used to cover the difference, until a slower step didn't.
        for (int i = 0; i < DerailSequence.Length(night.SequenceTuning, film) * SimConstants.TickRate; i++)
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
    public void TheFilmIsTheCrewsAndEachPlayerSkipsTheirOwnToTheCauseCard()
    {
        // GDD v1.4 App. E.5 (note 177): the host's crew in it, a shot each, then the cause card. After E.12 question 2 (the
        // director, 7 Oct 2026: "players should be able to skip whenever they want. It's up to EACH player"; note 311):
        // each player skips their own, from the first frame, by holding the key; it lands on the cause card, never past it,
        // and skips nobody else's.
        using var night = NetPlaySession.HostGame(Content, new SessionSetup(Route: "frontier:7", Cars: 4, Enemies: false), port: 0);
        for (int i = 0; i < 20; i++)
            night.Step(default);
        night.Host!.World.Derail("took the 45 km/h bend at 72 km/h, 27 km/h too fast");
        night.Step(default);
        // Skippable in the first person already, the film not yet shot.
        Assert.True(night.Skippable);
        Assert.Equal(DerailBeat.FirstPerson, DerailSequence.Beat(night.SequenceTuning, night.WreckSeconds, night.Film));
        var skip = new Sim.Player.PlayerIntent { Actions = Sim.Player.PlayerActions.Skip };
        // A tap (a jump mashed as the train comes off) skips nothing, and letting go empties the hold.
        int hold = (int)Math.Ceiling(night.World.WreckTuning.Skip.HoldSeconds * SimConstants.TickRate);
        for (int i = 0; i < hold - 3; i++)
            night.Step(skip);
        Assert.True(night.SkipHold is > 0 and < 1);
        night.Step(default);
        Assert.Equal(0, night.SkipHold);
        Assert.True(night.Skippable);
        // Held: taken, and the jump waits for the film.
        for (int i = 0; i < hold + 1 && night.Skippable; i++)
            night.Step(skip);
        Assert.False(night.Skippable);
        var film = WaitForFilm(night);
        night.Step(default);
        var t = night.SequenceTuning;
        Assert.Equal(film.FirstPersonOf(night.PlayerId), t.FirstPersonSeconds);
        Assert.Single(film.Start.Players);
        Assert.Equal(night.PlayerId, film.Start.Players[0].Id);
        Assert.Equal([ShotKind.Player, ShotKind.Settle, ShotKind.Cause], film.Cut.Select(s => s.Kind));
        Assert.Equal(DerailBeat.Film, DerailSequence.Beat(t, night.WreckSeconds, film));
        Assert.Equal(ShotKind.Cause, film.CutAt(DerailSequence.FilmSeconds(t, night.WreckSeconds))!.Value.Shot.Kind);
        // The cause card plays out, and the skip went nowhere: the crew's film isn't voted off, and the host saw no vote.
        Assert.True(night.WreckCinematic);
        Assert.False(night.World.FilmSkipped);
        Assert.False(night.Host.World.FilmSkipped);
        Assert.Equal((0, 0), night.Host.World.FilmVotes);
    }

    [Fact]
    public void TheCrewRideTheWreckToTheirOwnDeathAndTheFirstPersonEndsOnIt()
    {
        // GDD v1.4 App. E.2 step 1 (the director's decision of 5 Oct 2026; note 258): nobody dies on the derail tick. The
        // player rides the wreck, alive and unable to move, till the hit that kills them lands in their first person; the
        // first person ends on it, and the run is already over, settled on the derail tick.
        using var night = NetPlaySession.HostGame(Content, new SessionSetup(Route: "frontier:7", Cars: 4, Enemies: false), port: 0);
        for (int i = 0; i < 20; i++)
            night.Step(default);
        var host = night.Host!;
        host.World.Derail("test");
        uint derail = host.World.DerailTick;
        uint dies = host.World.DoomedAt[night.PlayerId];
        Assert.True(dies > derail, "dead on the derail tick");
        var walk = new Sim.Player.PlayerIntent { MoveZ = 1, Buttons = Sim.Player.PlayerButtons.Run };
        var at = host.Players.First(p => p.Id == night.PlayerId).State.Position;
        while (host.World.Tick <= derail + 2)
            night.Step(walk);
        // Their death counted and in the log already (the settlement's fixed on the derail tick, E.7), but alive, and
        // pressing forward moves nothing: the wreck has them.
        Assert.Equal(1, host.World.Bodies.Deaths);
        Assert.Single(host.World.Attribution.Log, i => i.Kind == Sim.Run.IncidentKind.Death && i.Victim == night.PlayerId);
        var riding = host.Players.First(p => p.Id == night.PlayerId).State;
        Assert.True(riding.Alive);
        Assert.Equal(at, riding.Position);
        while (host.World.Tick < dies)
        {
            Assert.True(host.Players.First(p => p.Id == night.PlayerId).State.Alive);
            night.Step(walk);
        }
        var dead = host.Players.First(p => p.Id == night.PlayerId).State;
        Assert.False(dead.Alive);
        Assert.Equal(Sim.Player.DeathCause.Derailed, dead.Death);
        Assert.Equal(1, host.World.Bodies.Deaths);
        Assert.Single(host.World.Attribution.Log, i => i.Kind == Sim.Run.IncidentKind.Death);
        // The first person is as long as the film says, and the host's kill lands inside it, before its end.
        var film = WaitForFilm(night);
        var death = film.Deaths[night.PlayerId];
        Assert.Equal(film.Tuning.FirstPersonLength(death.At), night.SequenceTuning.FirstPersonSeconds, 9);
        Assert.True((dies - derail) * SimConstants.TickSeconds < night.SequenceTuning.FirstPersonSeconds);
    }

    [Fact]
    public void TheHostDrawsTheOperaAndItsClientPlaysTheSameTrack()
    {
        // GDD v1.4 App. E.6 (note 174): the host draws from the bag it brought, and the track goes out with the world.
        var bag = new Sim.Music.MusicBag { Left = ["doom-egmont-overture", "lament-funeral-march"], Last = "gallop-infernal-galop" };
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
