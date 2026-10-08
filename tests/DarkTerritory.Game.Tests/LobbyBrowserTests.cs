using System.Diagnostics;
using System.Net;
using Ballast;
using Ballast.Net;
using Ballast.Online;
using DarkTerritory.Game;
using DarkTerritory.Sim;

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
        Assert.Equal(("Frontier", 1, NetPlaySession.CrewCap(Content)), (game.Tier, game.Aboard, game.Max));
        // A real round trip to the host's machine and back: on loopback, well under a frame.
        Assert.NotNull(game.PingMs);
        Assert.InRange(game.PingMs.Value, 0.0, 200.0);
        Assert.True(open.Link!.Value is { Listed: true, Locked: false });
        Assert.False(shut.Link!.Value.Listed);
    }

    /// <summary>A host's cap made small (as a mod's would be large), and a held place that runs out in a second.</summary>
    static void Cap(NetPlaySession host, int cap) =>
        host.Host!.PlayerTuning = host.Host.PlayerTuning with
        {
            Crew = host.Host.PlayerTuning.Crew with { Cap = cap },
            Rejoin = host.Host.PlayerTuning.Rejoin with { ReserveSeconds = 1 },
        };

    [Fact]
    public void AFullCrewOnTheNetworkIsListedFullAndOpensWhenItsHeldPlaceRunsOut()
    {
        // Note 254: the host and a bot, at a cap of two. Listed 2/2 FULL (bots hold crewmates), a joiner's told so, a place
        // held for a dropped bot still counts, and once it runs out the game's listed open again.
        const int port = 27472;
        using var browser = new LobbyBrowser(new LanBrowser(port), online: null);
        using var host = NetPlaySession.HostGame(Content, Night, port: 0, listed: true, bots: 1, beacon: new LanBeacon(port, [IPAddress.Loopback]));
        Cap(host, 2);
        Browse(browser, games => games is [{ Full: true }], null, host);
        var full = Assert.Single(browser.Games);
        Assert.Equal((2, 2, true), (full.Aboard, full.Max, full.Full));
        Assert.True(host.Link!.Value.Full);

        var e = Assert.Throws<JoinRefusedException>(() => NetPlaySession.Join(Content, new IPEndPoint(IPAddress.Loopback, host.Port), () => host.Step(default)));
        Assert.Equal("CREW FULL (2/2)", e.Message);
        Assert.Equal(1, host.Host!.Refusals);

        byte bot = host.BotCrew!.Bots[0].Session.PlayerId!.Value;
        host.Host.Drop(bot);
        for (int i = 0; i < 10; i++)
            host.Step(default);
        // Held for it (a second, here): still full, and what the beacon says is the places taken, the held one with them.
        Assert.True(host.Host.IsReserved(bot));
        Assert.Equal(2, host.Host.Occupied);
        Assert.True(host.Link!.Value.Full);

        Browse(browser, games => games is [{ Full: false }], null, host);
        var open = Assert.Single(browser.Games);
        Assert.Equal((1, 2, false), (open.Aboard, open.Max, open.Full));
        Assert.Equal(1, host.Host.ReservesExpired);
    }

    [Fact]
    public void AFullCrewsPlatformLobbyIsShutAndOpensWhenAPlayerLeaves()
    {
        // Note 254 on the fake Steam: at the cap, the lobby's values say so, its member limit is the cap, and it's shut, so
        // the search doesn't find it and a friend joining it is told CREW FULL. A player quitting frees their place at once.
        var cloud = new FakeOnline();
        var alice = cloud.SignIn("alice", (12, 16));
        var bob = cloud.SignIn("bob");
        var me = cloud.SignIn("me");
        using var host = NetPlaySession.HostGame(Content, Night, port: null, online: alice, listed: true, bots: 1);
        Cap(host, 2);
        for (int i = 0; i < 5; i++)
            host.Step(default);
        var id = host.Lobby!.Id;
        Assert.Equal(Lobby.State.Open, host.Lobby.Status);
        Assert.Equal((2, false), cloud.Doors(id));
        Assert.Equal(("2", "2", "1"), (alice.LobbyData(id, NetPlaySession.AboardKey), alice.LobbyData(id, NetPlaySession.MaxKey), alice.LobbyData(id, NetPlaySession.FullKey)));
        using (var browser = new LobbyBrowser(lan: null, me))
        {
            Browse(browser, _ => false, me, host);
            Assert.Empty(browser.Games);
        }
        var e = Assert.Throws<JoinRefusedException>(() => NetPlaySession.JoinLobby(Content, bob, id, () => host.Step(default)));
        Assert.Equal("CREW FULL (2/2)", e.Message);

        // The bot quits (as a joiner's game does when it's left): its place is given up, not held.
        host.BotCrew!.Bots[0].Session.Leave();
        for (int i = 0; i < 5; i++)
            host.Step(default);
        Assert.Equal(1, host.Host!.Leaves);
        Assert.Equal(0, host.Host.Reserved);
        Assert.Equal((2, true), cloud.Doors(id));
        Assert.Equal("0", alice.LobbyData(id, NetPlaySession.FullKey));
        using (var browser = new LobbyBrowser(lan: null, me))
        {
            Browse(browser, games => games.Count > 0, me, host);
            var listed = Assert.Single(browser.Games);
            Assert.Equal((1, 2, false), (listed.Aboard, listed.Max, listed.Full));
        }
        using var joiner = NetPlaySession.JoinLobby(Content, bob, id, () => host.Step(default));
        for (int t = 0; t < SimConstants.TickRate && !joiner.Client.Connected; t++)
        {
            host.Step(default);
            joiner.Step(default);
        }
        Assert.True(joiner.Client.Connected);
        Assert.True(host.Host.Full);
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

    /// <summary>Steps a host until a joiner's aboard, or a second's gone.</summary>
    static void Board(NetPlaySession host, NetPlaySession joiner)
    {
        for (int t = 0; t < SimConstants.TickRate && !joiner.Client.Connected; t++)
        {
            host.Step(default);
            joiner.Step(default);
        }
    }

    [Fact]
    public void APrivateRunOnThePlatformIsListedLockedAndLetsInThePasswordOrAFriend()
    {
        // Note 450 (the director, 8 Oct 2026: "Private matches should be password gated"; listed with a lock): on the fake
        // Steam, a private run is in the search with its lock and its mood; a stranger without the password is turned away
        // WRONG PASSWORD and takes no place; with it, they're aboard; the host's friend needs none.
        var cloud = new FakeOnline();
        var alice = cloud.SignIn("alice", (12, 16));
        var bob = cloud.SignIn("bob");
        var carol = cloud.SignIn("carol");
        var me = cloud.SignIn("me");
        cloud.Befriend(alice, carol);
        using var host = NetPlaySession.HostGame(Content, Night, port: null, online: alice, listed: true, lobbyName: "NIGHT OWLS",
            password: "lantern", mood: RunMood.Laughs);
        Assert.True(host.Locked);
        using (var browser = new LobbyBrowser(lan: null, me))
        {
            Browse(browser, games => games.Count > 0, me, host);
            var game = Assert.Single(browser.Games);
            Assert.Equal(("NIGHT OWLS", true, RunMood.Laughs, 20.0), (game.Name, game.Locked, game.Mood, game.PingMs));
        }
        var id = host.Lobby!.Id;
        // The words themselves are nowhere in the lobby's values, where anyone searching can read them.
        Assert.DoesNotContain(new[] { NetPlaySession.NameKey, NetPlaySession.LockedKey, NetPlaySession.MoodKey, NetPlaySession.RunKey },
            k => alice.LobbyData(id, k).Contains("LANTERN", StringComparison.OrdinalIgnoreCase));

        var e = Assert.Throws<JoinRefusedException>(() => NetPlaySession.JoinLobby(Content, bob, id, () => host.Step(default)));
        Assert.Equal(("WRONG PASSWORD", Sim.Net.RefusalReason.Password), (e.Message, e.Refusal.Reason));
        Assert.Equal(1, host.Host!.WrongPasswords);
        Assert.Equal(1, host.Host.Occupied);
        e = Assert.Throws<JoinRefusedException>(() => NetPlaySession.JoinLobby(Content, bob, id, () => host.Step(default), password: "candle"));
        Assert.Equal(2, host.Host.WrongPasswords);

        using var withIt = NetPlaySession.JoinLobby(Content, bob, id, () => host.Step(default), password: "Lantern");
        Board(host, withIt);
        Assert.True(withIt.Client.Connected);
        using var friend = NetPlaySession.JoinLobby(Content, carol, id, () => host.Step(default));
        Board(host, friend);
        Assert.True(friend.Client.Connected);
        Assert.Equal(3, host.Host.Occupied);
        Assert.Equal(2, host.Host.WrongPasswords);
    }

    [Fact]
    public void APrivateRunOnTheNetworkIsListedLockedAndAskedForItsPassword()
    {
        // Note 450 on the local network: the beacon says locked and the mood; a joiner by address needs the password too
        // (nobody there is anyone's friend).
        const int port = 27473;
        using var browser = new LobbyBrowser(new LanBrowser(port), online: null);
        using var host = NetPlaySession.HostGame(Content, Night, port: 0, listed: true, lobbyName: "NIGHT OWLS", beacon: new LanBeacon(port, [IPAddress.Loopback]),
            password: "lantern", mood: RunMood.Competitive);
        Browse(browser, games => games is [{ PingMs: not null }], null, host);
        var game = Assert.Single(browser.Games);
        Assert.Equal((true, RunMood.Competitive), (game.Locked, game.Mood));
        // The host's lobby panel says it's private, listed with a lock (not "your game's listed").
        Assert.True(host.Link!.Value is { Listed: true, Locked: true });
        var at = new IPEndPoint(IPAddress.Loopback, host.Port);
        var e = Assert.Throws<JoinRefusedException>(() => NetPlaySession.Join(Content, at, () => host.Step(default)));
        Assert.Equal("WRONG PASSWORD", e.Message);
        using var joiner = NetPlaySession.Join(Content, at, () => host.Step(default), password: "LANTERN");
        Board(host, joiner);
        Assert.True(joiner.Client.Connected);
    }
}
