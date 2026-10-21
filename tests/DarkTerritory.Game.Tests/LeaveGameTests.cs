using System.Net;
using Ballast;
using Ballast.Online;
using DarkTerritory.Sim;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Towns;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// Leaving a game leaves it (queue #640, ARCHITECTURE §8 note 937; the director's playtest of 20 Oct evening, line 27: "So I
/// escape and leave, and I've been put into automatically, like, another lobby ... you can't actually leave a game"). A
/// night left on purpose, hosted or joined, goes back to the front end's title. Nothing is joined, hosted or followed.
/// </summary>
public class LeaveGameTests
{
    static readonly string Content = DataFile.FindContentRoot();

    static AfterNight Left(bool campaign = true, bool crew = false, bool refitting = false, NetPlaySession.Following? following = null,
        LobbyId? invited = null) =>
        AfterNight.Decide(left: true, kicked: false, invited, refitting, following, fromCommandLine: false, campaign, crew);

    static AfterNight Over(bool campaign = true, bool crew = false, bool refitting = false, NetPlaySession.Following? following = null) =>
        AfterNight.Decide(left: false, kicked: false, invited: null, refitting, following, fromCommandLine: false, campaign, crew);

    static NetPlaySession.Following Going() =>
        new(() => throw new InvalidOperationException("nothing's dialled"), null, "frontier:7", new TownAt("Ashgrove", "", 1));

    [Fact]
    public void ALeftNightGoesToTheTitleWhateverItWas()
    {
        // The director's night: a campaign night hosted with a bot. Left, it's the title, not the fortress board.
        Assert.Equal(new AfterNight.Menu(MenuAfter.Title), Left());
        Assert.Equal(new AfterNight.Menu(MenuAfter.Title), Left(crew: true));
        Assert.Equal(new AfterNight.Menu(MenuAfter.Title), Left(campaign: false));
        // Nothing of the night carries on: not the blacksmith's refit, not following a host that went on.
        Assert.Equal(new AfterNight.Menu(MenuAfter.Title), Left(refitting: true));
        Assert.Equal(new AfterNight.Menu(MenuAfter.Title), Left(following: Going()));
    }

    [Fact]
    public void ANightSeenToItsEndGoesOnAsBefore()
    {
        // Enter on the end screen: the fortress, the crew's board, the refit, the host's next night.
        Assert.Equal(new AfterNight.Menu(MenuAfter.Fortress), Over());
        Assert.Equal(new AfterNight.Menu(MenuAfter.Crew), Over(crew: true));
        Assert.Equal(new AfterNight.Menu(MenuAfter.Title), Over(campaign: false));
        Assert.IsType<AfterNight.Refit>(Over(refitting: true));
        Assert.IsType<AfterNight.Follow>(Over(following: Going()));
        Assert.Equal(new AfterNight.Menu(MenuAfter.PutOff),
            AfterNight.Decide(left: false, kicked: true, null, false, null, fromCommandLine: false, campaign: false, crew: false));
        Assert.IsType<AfterNight.Quit>(AfterNight.Decide(left: true, kicked: false, null, false, null, fromCommandLine: true, campaign: true, crew: false));
    }

    [Fact]
    public void AnInviteAcceptedInTheNightIsStillTaken()
    {
        // Asked for by the player (queue #272): the one lobby a left night goes to.
        var lobby = new LobbyId(7);
        Assert.Equal(new AfterNight.Relaunch(lobby), Left(invited: lobby));
    }

    [Fact]
    public void AJoinerWhoLeavesAHostsGameIsLetGoAndJoinsNothing()
    {
        // A night hosted with a bot, as the director's was, and a friend aboard.
        using var host = NetPlaySession.HostGame(Content, new SessionSetup(Route: "frontier:7", Cars: 4, Enemies: false), port: 0, bots: 1);
        var joiner = NetPlaySession.Join(Content, new IPEndPoint(IPAddress.Loopback, host.Port), () => host.Step(default));
        for (int t = 0; t < SimConstants.TickRate; t++)
        {
            host.Step(default);
            joiner.Step(default);
        }
        Assert.Equal(3, host.Host!.Players.Count());

        // The host goes on from its town as the joiner leaves: the joiner doesn't follow it.
        var town = Assert.IsType<TownAt>(TownAt.Terminus(host.Route!, LineGenContent.Cached(Content).Config.Tiers.Fortress.Identities));
        host.CarryOn(town);
        for (int t = 0; t < SimConstants.TickRate / 2; t++)
        {
            host.Step(default);
            joiner.Step(default);
        }
        Assert.NotNull(joiner.GoingOn);
        var next = AfterNight.Decide(left: true, joiner.Kicked, invited: null, refitting: false, joiner.GoingOn, fromCommandLine: false,
            campaign: false, crew: false);
        Assert.Equal(new AfterNight.Menu(MenuAfter.Title), next);
        joiner.Dispose();

        // Left on purpose (a Leave said): its place frees at once, and the host and its bot carry on.
        for (int t = 0; t < SimConstants.TickRate && host.Host.Players.Count() > 2; t++)
        {
            host.Step(default);
            Thread.Sleep(1);
        }
        Assert.Equal(2, host.Host.Players.Count());
        Assert.Single(host.BotCrew!.Bots);
    }

    [Fact]
    public void AHostWhoLeavesTellsNobodyToFollowAndTheJoinerGoesToTheTitle()
    {
        var host = NetPlaySession.HostGame(Content, new SessionSetup(Route: "frontier:7", Cars: 4, Enemies: false), port: 0, bots: 1);
        using var joiner = NetPlaySession.Join(Content, new IPEndPoint(IPAddress.Loopback, host.Port), () => host.Step(default));
        for (int t = 0; t < SimConstants.TickRate; t++)
        {
            host.Step(default);
            joiner.Step(default);
        }
        // The host's own LEAVE: no carrying on, nothing hosted after it.
        Assert.Equal(new AfterNight.Menu(MenuAfter.Title), Left());
        host.Dispose();
        for (int t = 0; t < SimConstants.TickRate * 30 && !joiner.Lost; t++)
        {
            joiner.Step(default);
            Thread.Sleep(1);
        }
        Assert.True(joiner.Lost);
        // The joiner isn't sent after a night nobody's hosting: back to the front end.
        Assert.Null(joiner.GoingOn);
        Assert.Equal(new AfterNight.Menu(MenuAfter.Title),
            AfterNight.Decide(left: false, joiner.Kicked, invited: null, refitting: false, joiner.GoingOn, fromCommandLine: false, campaign: false, crew: false));
    }

    [Fact]
    public void AResumedNightsHostBoardsAliveBesideItsBotNotAsAJoiner()
    {
        // The director's way back in (line 27: "and joining. What the hell am I joining?"): LEAVE's Enter, read again by the
        // fortress board's first frame, chose CARRY ON. The resumed night is past the gate; its bot boarded first, and the
        // host's own player after it came in as a mid-run joiner: dead, the card saying JOINING. The night's own crew boards
        // alive; a friend joining after is still a joiner.
        const string spec = "frontier:11";
        var route = Sim.LineGen.Routes.Generate(Content, spec, 4);
        var facility = route.Of(FeatureKind.Facility).First(f => f.Facility == FacilityKind.CoalingTower);
        Sim.Campaign.RunCheckpoint saved;
        using (var night = NetPlaySession.HostGame(Content, new SessionSetup(Route: spec, Cars: 4, Enemies: false) { Start = facility.End - 20 }, port: 0))
        {
            for (int i = 0; i < 20 * SimConstants.TickRate && night.Checkpoints == 0; i++)
            {
                night.Host!.Train.Dynamics.Velocity = 3;
                night.Step(default);
            }
            saved = Assert.IsType<Sim.Campaign.RunCheckpoint>(night.Checkpoint);
        }

        using var resumed = NetPlaySession.HostGame(Content, new SessionSetup(Route: spec, Cars: 4, Enemies: false), port: 0, resume: saved, bots: 1);
        Assert.NotEqual(RunPhase.Yard, resumed.Host!.World.Run!.Phase);
        Assert.NotNull(resumed.Host.World.Holdouts);
        for (int t = 0; t < SimConstants.TickRate / 2; t++)
            resumed.Step(default);
        foreach (var p in resumed.Host.Players)
        {
            Assert.True(p.State.Alive, $"player {p.Id}: {p.State.Death}");
            Assert.NotEqual(DeathCause.Waiting, p.State.Death);
        }
        Assert.Equal(2, resumed.Host.Players.Count());

        // A friend joining it now takes the bot's seat (note 724). On the night resumed without bots, they're a joiner past
        // the gate, and wait in the queue as before.
        using var alone = NetPlaySession.HostGame(Content, new SessionSetup(Route: spec, Cars: 4, Enemies: false), port: 0, resume: saved);
        using var friend = NetPlaySession.Join(Content, new IPEndPoint(IPAddress.Loopback, alone.Port), () => alone.Step(default));
        for (int t = 0; t < SimConstants.TickRate / 2; t++)
        {
            alone.Step(default);
            friend.Step(default);
        }
        Assert.True(alone.Host!.Players.Single(p => p.Id == alone.PlayerId).State.Alive);
        Assert.Equal(DeathCause.Waiting, alone.Host.Players.Single(p => p.Id == friend.PlayerId).State.Death);
    }

    /// <summary>A joiner told its host is going on from the town (note 606), the host gone: what it follows with.</summary>
    static NetPlaySession.Following GoneOn(out int port)
    {
        var first = NetPlaySession.HostGame(Content, new SessionSetup(Route: "frontier:7", Cars: 4, Enemies: false), port: 0);
        port = first.Port;
        using var joiner = NetPlaySession.Join(Content, new IPEndPoint(IPAddress.Loopback, port), () => first.Step(default));
        for (int t = 0; t < SimConstants.TickRate; t++)
        {
            first.Step(default);
            joiner.Step(default);
        }
        first.CarryOn(Assert.IsType<TownAt>(TownAt.Terminus(first.Route!, LineGenContent.Cached(Content).Config.Tiers.Fortress.Identities)));
        for (int t = 0; t < SimConstants.TickRate / 2; t++)
        {
            first.Step(default);
            joiner.Step(default);
        }
        first.Dispose();
        for (int t = 0; t < SimConstants.TickRate * 30 && !joiner.Lost; t++)
        {
            joiner.Step(default);
            Thread.Sleep(1);
        }
        return Assert.IsType<NetPlaySession.Following>(joiner.GoingOn);
    }

    [Fact]
    public void AFollowerLeavesTheWaitByHandBeforeTheHostsNextNightIsUp()
    {
        // Queue #648 (note 950): BACK TO THE MENU on the town's card, the host still picking its next night. The wait ends
        // when it's asked to, not after rejoin.followSeconds (ten minutes).
        var going = GoneOn(out _);
        int asked = 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Assert.Throws<OperationCanceledException>(() => NetPlaySession.Follow(Content, going, () => asked++, leaving: () => asked > 50));
        Assert.True(clock.Elapsed.TotalSeconds < 30, $"{clock.Elapsed.TotalSeconds:0} s");
    }

    [Fact]
    public void AFollowerWhoLeavesWhileTheNextNightAnswersJoinsNothing()
    {
        // Left while its dial was being answered (the host's next night up, its welcome not yet given): hung up, and the
        // night has nobody of theirs aboard.
        var going = GoneOn(out int port);
        using var next = NetPlaySession.HostGame(Content, new SessionSetup(Route: "frontier:3", Cars: 4, Enemies: false), port: port);
        bool left = false;
        Assert.Throws<OperationCanceledException>(() => NetPlaySession.Follow(Content, going, () => left = true, leaving: () => left));
        for (int t = 0; t < SimConstants.TickRate * 2; t++)
        {
            next.Step(default);
            Thread.Sleep(1);
        }
        Assert.Single(next.Host!.Players);
    }
}

