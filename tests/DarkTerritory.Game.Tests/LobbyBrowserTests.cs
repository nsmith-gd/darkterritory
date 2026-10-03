using System.Diagnostics;
using System.Net;
using Ballast;
using Ballast.Net;
using Ballast.Online;
using DarkTerritory.Game;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The join screen's lobby browser over real hosts (the user's playtest: "I see active lobbies I can join and then what my
/// ping is ... I can join it if its public. If it's a private lobby its not listed"), on the local network and the fake platform.
/// </summary>
public class LobbyBrowserTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly SessionSetup Night = new(Route: "frontier:7", Cars: 4, Enemies: false);

    /// <summary>Steps the hosts and polls the browser until <paramref name="done"/> or a few seconds have gone.</summary>
    static void Browse(LobbyBrowser browser, Func<IReadOnlyList<ListedGame>, bool> done, IOnlineBackend? online, params NetPlaySession[] hosts)
    {
        var clock = Stopwatch.StartNew();
        var events = new List<OnlineEvent>();
        while (!done(browser.Games) && clock.Elapsed.TotalSeconds < 4)
        {
            foreach (var h in hosts)
                h.Step(default);
            events.Clear();
            online?.Poll(events);
            browser.Poll(clock.Elapsed.TotalSeconds, events, search: true);
            Thread.Sleep(5);
        }
    }

    [Fact]
    public void APublicHostOnTheNetworkIsListedWithAMeasuredPingAndAPrivateOneIsnt()
    {
        const int port = 27471;
        using var browser = new LobbyBrowser(new LanBrowser(port), online: null);
        Assert.Null(browser.Error);
        using var open = NetPlaySession.HostGame(Content, Night, port: 0, listed: true, lobbyName: "NICK'S RUN", beacon: new LanBeacon(port, [IPAddress.Loopback]));
        using var shut = NetPlaySession.HostGame(Content, Night, port: 0, listed: false, lobbyName: "SECRET RUN", beacon: new LanBeacon(port, [IPAddress.Loopback]));
        Browse(browser, games => games is [{ PingMs: not null }, ..], null, open, shut);
        // Long enough for the private host to have beaconed too, had it been going to.
        Browse(browser, _ => false, null, open, shut);

        var game = Assert.Single(browser.Games);
        Assert.Equal("NICK'S RUN", game.Name);
        Assert.Equal(new Launch.Join($"127.0.0.1:{open.Port}"), game.Join);
        Assert.Equal(("Frontier", 1, NetPlaySession.MaxCrew), (game.Tier, game.Aboard, game.Max));
        // A real round trip to the host's machine and back: on loopback, well under a frame.
        Assert.NotNull(game.PingMs);
        Assert.InRange(game.PingMs.Value, 0.0, 200.0);
        Assert.True(open.Link!.Value.Listed);
        Assert.False(shut.Link!.Value.Listed);
    }

    [Fact]
    public void APublicLobbyOnThePlatformIsListedWithItsPingAndAPrivateOneIsnt()
    {
        var cloud = new FakeOnline();
        var alice = cloud.SignIn("alice", (12, 16));
        var carol = cloud.SignIn("carol", (100, 0));
        var me = cloud.SignIn("me");
        using var open = NetPlaySession.HostGame(Content, Night, port: null, online: alice, listed: true);
        using var shut = NetPlaySession.HostGame(Content, Night, port: null, online: carol, listed: false);
        using var browser = new LobbyBrowser(lan: null, me);
        Browse(browser, games => games.Count > 0, me, open, shut);

        var game = Assert.Single(browser.Games);
        Assert.Equal("alice's run", game.Name);
        Assert.Equal(new Launch.JoinLobby(open.Lobby!.Id), game.Join);
        Assert.Equal(20, game.PingMs);
        Assert.Equal("Frontier", game.Tier);
        Assert.Contains("hosted by alice", game.Where);
        Assert.Equal(LobbyVisibility.FriendsOnly, shut.Lobby!.Visibility);
    }
}
