using System.Net;
using Ballast;
using Ballast.Online;
using DarkTerritory.Sim;
using DarkTerritory.Sim.LineGen;
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
}
