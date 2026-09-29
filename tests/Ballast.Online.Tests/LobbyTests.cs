using System.Diagnostics;
using System.Net;
using Ballast.Net;
using Ballast.Online;

namespace Ballast.Online.Tests;

/// <summary>The lobby flow and the game's protocol over relayed P2P, against the fake platform.</summary>
public class LobbyTests
{
    const string Game = "darkterritory";
    static readonly DatagramOptions Fast = new() { TimeoutSeconds = 2, ConnectSeconds = 1 };

    static void Until(Func<bool> done, Action pump)
    {
        var clock = Stopwatch.StartNew();
        while (!done())
        {
            Assert.True(clock.Elapsed.TotalSeconds < 5, "timed out");
            pump();
            Thread.Sleep(1);
        }
    }

    static Lobby Hosted(IOnlineBackend host, int max = 8)
    {
        var lobby = Lobby.Host(host, Game, 1, max);
        Assert.Equal(Lobby.State.Creating, lobby.Status);
        Until(() => lobby.Status != Lobby.State.Creating, lobby.Poll);
        Assert.Equal(Lobby.State.Open, lobby.Status);
        return lobby;
    }

    static Lobby Joined(IOnlineBackend who, LobbyId id, int protocol = 1)
    {
        var lobby = Lobby.Join(who, id, Game, protocol);
        Until(() => lobby.Status != Lobby.State.Joining, lobby.Poll);
        return lobby;
    }

    [Fact]
    public void AFriendAcceptsAnInviteAndConnectsToTheHost()
    {
        var cloud = new FakeOnline();
        var alice = cloud.SignIn("alice");
        var bob = cloud.SignIn("bob");
        using var hostLobby = Hosted(alice);
        Assert.Equal("alice", alice.LobbyData(hostLobby.Id, Lobby.HostKey));
        using var host = OnlineTransport.Host(alice, Fast);

        // Bob is playing on his own when the invite comes: the platform tells his game which lobby.
        alice.Invite(hostLobby.Id, bob.Me);
        var events = new List<OnlineEvent>();
        bob.Poll(events);
        var invite = Assert.Single(events);
        Assert.Equal(OnlineEventKind.JoinRequested, invite.Kind);
        Assert.Equal(alice.Me, invite.User);

        using var bobLobby = Joined(bob, invite.Lobby);
        Assert.Equal(Lobby.State.Open, bobLobby.Status);
        Assert.Equal(alice.Me, bobLobby.Owner);
        Assert.Equal([alice.Me, bob.Me], hostLobby.Members);

        using var client = OnlineTransport.Connect(bob, bobLobby.Owner, Fast);
        var hostEvents = new List<TransportEvent>();
        var clientEvents = new List<TransportEvent>();
        void Pump()
        {
            hostLobby.Poll();
            bobLobby.Poll();
            host.Poll(hostEvents);
            client.Poll(clientEvents);
        }
        Until(() => client.IsConnected && hostEvents.Any(e => e.Kind == TransportEventKind.Connected), Pump);
        var peer = hostEvents.Single(e => e.Kind == TransportEventKind.Connected).Peer;
        Assert.True(host.TryGetAddress(peer, out var who) && who == bob.Me);

        client.Send(PeerId.Host, [1, 2, 3], Delivery.Unreliable);
        host.Send(peer, [4, 5], Delivery.ReliableOrdered);
        Until(() => hostEvents.Any(e => e.Kind == TransportEventKind.Data) && clientEvents.Any(e => e.Kind == TransportEventKind.Data), Pump);
        Assert.Equal([1, 2, 3], hostEvents.Single(e => e.Kind == TransportEventKind.Data).Payload);
        Assert.Equal([4, 5], clientEvents.Single(e => e.Kind == TransportEventKind.Data).Payload);
        Assert.Equal(0, cloud.Refused);
    }

    [Fact]
    public void SomeoneOutsideTheLobbyCantReachTheHost()
    {
        var cloud = new FakeOnline();
        var alice = cloud.SignIn("alice");
        var mallory = cloud.SignIn("mallory");
        using var lobby = Hosted(alice);
        using var host = OnlineTransport.Host(alice, Fast);
        using var stranger = OnlineTransport.Connect(mallory, alice.Me, Fast);
        var hostEvents = new List<TransportEvent>();
        var strangerEvents = new List<TransportEvent>();
        Until(() => strangerEvents.Any(e => e.Kind == TransportEventKind.Disconnected), () =>
        {
            host.Poll(hostEvents);
            stranger.Poll(strangerEvents);
        });
        Assert.Empty(hostEvents);
        Assert.True(cloud.Refused > 0);
    }

    [Fact]
    public void AFullOrClosedLobbySaysSo()
    {
        var cloud = new FakeOnline();
        var alice = cloud.SignIn("alice");
        using var lobby = Hosted(alice, max: 2);
        using var bob = Joined(cloud.SignIn("bob"), lobby.Id);
        Assert.Equal(Lobby.State.Open, bob.Status);

        using var carol = Joined(cloud.SignIn("carol"), lobby.Id);
        Assert.Equal(Lobby.State.Failed, carol.Status);
        Assert.Equal("the lobby is full", carol.Error);

        bob.Dispose();
        lobby.SetJoinable(false);
        using var dave = Joined(cloud.SignIn("dave"), lobby.Id);
        Assert.Equal("the lobby is closed", dave.Error);

        using var gone = Joined(cloud.SignIn("erin"), new LobbyId(12345));
        Assert.Equal("that lobby is gone", gone.Error);
    }

    [Fact]
    public void AHostOnAnotherVersionIsRefusedBeforeConnecting()
    {
        var cloud = new FakeOnline();
        using var lobby = Hosted(cloud.SignIn("alice"));
        var bob = cloud.SignIn("bob");
        using var old = Joined(bob, lobby.Id, protocol: 2);
        Assert.Equal(Lobby.State.Failed, old.Status);
        Assert.Contains("different version", old.Error);
        // And he's not left sitting in it.
        Assert.DoesNotContain(bob.Me, lobby.Members);
    }

    [Fact]
    public void AnInviteWhilePlayingIsHeldForTheGameToAnswer()
    {
        var cloud = new FakeOnline();
        var alice = cloud.SignIn("alice");
        var bob = cloud.SignIn("bob");
        using var mine = Hosted(bob);
        using var theirs = Hosted(alice);
        alice.Invite(theirs.Id, bob.Me);
        mine.Poll();
        Assert.Equal(theirs.Id, mine.TakeJoinRequest());
        Assert.Null(mine.JoinRequest);
    }

    [Fact]
    public void TheHostLeavingIsNoticed()
    {
        var cloud = new FakeOnline();
        var alice = cloud.SignIn("alice");
        var host = Hosted(alice);
        using var bob = Joined(cloud.SignIn("bob"), host.Id);
        Assert.False(bob.HostLeft);
        host.Dispose();
        bob.Poll();
        Assert.True(bob.HostLeft);
    }

    [Theory]
    [InlineData(new[] { "+connect_lobby", "109775240000000001" }, 109775240000000001UL)]
    [InlineData(new[] { "--route", "frontier:7", "--join-lobby", "42" }, 42UL)]
    public void SteamsLaunchArgumentNamesTheLobby(string[] args, ulong lobby) =>
        Assert.Equal(new LobbyId(lobby), LaunchArgs.ConnectLobby(args));

    [Fact]
    public void NoLaunchArgumentMeansNoLobby() => Assert.Null(LaunchArgs.ConnectLobby(["--host", "+connect_lobby"]));
}

/// <summary>One host on Steam and UDP at once.</summary>
public class HostGroupTests
{
    [Fact]
    public void PeersFromEveryTransportAreOneCrew()
    {
        var fast = new DatagramOptions { TimeoutSeconds = 2, ConnectSeconds = 2 };
        var cloud = new FakeOnline();
        var alice = cloud.SignIn("alice");
        var bob = cloud.SignIn("bob");
        using var lobby = Lobby.Host(alice, "darkterritory", 1, 8);
        var udp = UdpTransport.Host(0, fast, IPAddress.Loopback);
        using var group = new HostGroup(udp, OnlineTransport.Host(alice, fast));
        var events = new List<TransportEvent>();
        var clock = Stopwatch.StartNew();
        while (lobby.Status != Lobby.State.Open)
            lobby.Poll();
        using var bobLobby = Lobby.Join(bob, lobby.Id, "darkterritory", 1);
        while (bobLobby.Status != Lobby.State.Open)
            bobLobby.Poll();

        using var local = UdpTransport.Connect(new IPEndPoint(IPAddress.Loopback, udp.Port), fast);
        using var friend = OnlineTransport.Connect(bob, alice.Me, fast);
        var localEvents = new List<TransportEvent>();
        var friendEvents = new List<TransportEvent>();
        bool localGone = false;
        void Pump()
        {
            Assert.True(clock.Elapsed.TotalSeconds < 5, "timed out");
            group.Poll(events);
            if (!localGone)
                local.Poll(localEvents);
            friend.Poll(friendEvents);
            Thread.Sleep(1);
        }
        while (events.Count(e => e.Kind == TransportEventKind.Connected) < 2)
            Pump();
        var peers = events.Where(e => e.Kind == TransportEventKind.Connected).Select(e => e.Peer).ToList();
        Assert.Equal(2, peers.Distinct().Count());
        Assert.DoesNotContain(PeerId.Host, peers);

        // Each outer id routes to the right one, both ways.
        var toFriend = peers.Single(p => group.Route(p)!.Value.Transport is OnlineTransport);
        var toLocal = peers.Single(p => p != toFriend);
        group.Send(toFriend, [7], Delivery.ReliableOrdered);
        group.Send(toLocal, [8], Delivery.ReliableOrdered);
        friend.Send(PeerId.Host, [9], Delivery.Unreliable);
        while (!friendEvents.Any(e => e.Kind == TransportEventKind.Data) || !localEvents.Any(e => e.Kind == TransportEventKind.Data)
            || !events.Any(e => e.Kind == TransportEventKind.Data))
            Pump();
        Assert.Equal([7], friendEvents.Single(e => e.Kind == TransportEventKind.Data).Payload);
        Assert.Equal([8], localEvents.Single(e => e.Kind == TransportEventKind.Data).Payload);
        Assert.Equal(toFriend, events.Single(e => e.Kind == TransportEventKind.Data).Peer);
        Assert.True(group.RoundTrip(toLocal) > 0);

        // A leaver is reported under their outer id, and forgotten.
        local.Dispose();
        localGone = true;
        while (!events.Any(e => e.Kind == TransportEventKind.Disconnected))
            Pump();
        Assert.Equal(toLocal, events.Single(e => e.Kind == TransportEventKind.Disconnected).Peer);
        Assert.Null(group.Route(toLocal));
    }
}
