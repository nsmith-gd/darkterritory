using System.Diagnostics;
using Ballast.Net;
using Ballast.Online.Eos;

namespace Ballast.Online.Tests;

/// <summary>EOS as the third online platform (note 896): sign-in, lobbies by join code, and P2P over the relays, on the fake EOS.</summary>
public class EosTests
{
    const string Game = "darkterritory";
    static readonly DatagramOptions Fast = new() { TimeoutSeconds = 2, ConnectSeconds = 1 };

    static void Until(Func<bool> done, Action pump, double seconds = 5)
    {
        var clock = Stopwatch.StartNew();
        while (!done())
        {
            Assert.True(clock.Elapsed.TotalSeconds < seconds, "timed out");
            pump();
            Thread.Sleep(1);
        }
    }

    static EosBackend SignIn(FakeEos cloud, string name, EosNat nat = EosNat.Moderate, EosRelay relay = EosRelay.AllowRelays) =>
        EosBackend.TryStart(cloud.Machine(nat), name, relay, out var error) ?? throw new InvalidOperationException(error);

    static Lobby Hosted(IOnlineBackend host, int max = 8, LobbyVisibility visibility = LobbyVisibility.FriendsOnly)
    {
        var lobby = Lobby.Host(host, Game, 1, max, visibility);
        Until(() => lobby.Status != Lobby.State.Creating, lobby.Poll);
        Assert.Equal(Lobby.State.Open, lobby.Status);
        // Its values (the code among them) go up with the next poll's one update.
        lobby.Poll();
        lobby.Poll();
        return lobby;
    }

    static Lobby ByCode(IOnlineBackend who, string code, int protocol = 1)
    {
        var lobby = Lobby.JoinByCode(who, code, Game, protocol);
        Until(() => lobby.Status != Lobby.State.Joining, lobby.Poll);
        return lobby;
    }

    /// <summary>A host and a friend connected over EOS P2P; the pump polls both lobbies and both transports.</summary>
    static (OnlineTransport Host, OnlineTransport Client, PeerId Peer, Action Pump, List<TransportEvent> HostEvents) Connected(Lobby hostLobby, Lobby friendLobby)
    {
        var host = OnlineTransport.Host(hostLobby.Online, Fast);
        var client = OnlineTransport.Connect(friendLobby.Online, friendLobby.Owner, Fast);
        var hostEvents = new List<TransportEvent>();
        var clientEvents = new List<TransportEvent>();
        void Pump()
        {
            hostLobby.Poll();
            friendLobby.Poll();
            host.Poll(hostEvents);
            client.Poll(clientEvents);
        }
        Until(() => client.IsConnected && hostEvents.Any(e => e.Kind == TransportEventKind.Connected), Pump);
        return (host, client, hostEvents.First(e => e.Kind == TransportEventKind.Connected).Peer, Pump, hostEvents);
    }

    [Fact]
    public void AFriendTypesTheCodeAndConnectsToTheHost()
    {
        var cloud = new FakeEos();
        using var alice = SignIn(cloud, "alice");
        using var bob = SignIn(cloud, "bob");
        using var hostLobby = Hosted(alice);
        string code = hostLobby.Code;
        Assert.NotNull(JoinCode.Normalize(code));
        Assert.Equal("alice", alice.LobbyData(hostLobby.Id, Lobby.HostKey));

        // Read out as "K7Q-M2X", typed in lower case: the same code.
        using var bobLobby = ByCode(bob, JoinCode.Show(code).ToLowerInvariant());
        Assert.Equal(Lobby.State.Open, bobLobby.Status);
        Assert.Equal(hostLobby.Id, bobLobby.Id);
        Assert.Equal(alice.Me, bobLobby.Owner);
        Assert.Equal([alice.Me, bob.Me], hostLobby.Members);
        // Each member's name is their own member attribute: device-id users have none the platform shows.
        Until(() => alice.NameOf(bob.Me) == "bob", () => { hostLobby.Poll(); bobLobby.Poll(); });
        Assert.Equal("alice", bob.NameOf(alice.Me));

        var (host, client, peer, pump, hostEvents) = Connected(hostLobby, bobLobby);
        using var _ = host;
        using var __ = client;
        Assert.True(host.TryGetAddress(peer, out var who) && who == bob.Me);
        client.Send(PeerId.Host, [1, 2, 3], Delivery.Unreliable);
        Until(() => hostEvents.Any(e => e.Kind == TransportEventKind.Data), pump);
        Assert.Equal([1, 2, 3], hostEvents.Single(e => e.Kind == TransportEventKind.Data).Payload);
        // Two moderate NATs meet directly: nothing went through the relays.
        Assert.Equal(new CarrierLink("EOS", Relayed: false), alice.Carrier.Describe(bob.Me));
        Assert.Equal(0, cloud.Relayed);
        Assert.True(cloud.Direct > 0);
    }

    [Theory]
    [InlineData(EosNat.Strict, EosNat.Strict)]
    [InlineData(EosNat.Strict, EosNat.Moderate)]
    public void WhenNatBlocksTheDirectPathTheRelayCarriesIt(EosNat hostNat, EosNat friendNat)
    {
        var cloud = new FakeEos();
        using var alice = SignIn(cloud, "alice", hostNat);
        using var bob = SignIn(cloud, "bob", friendNat);
        using var hostLobby = Hosted(alice);
        using var bobLobby = ByCode(bob, hostLobby.Code);
        var (host, client, _, _, _) = Connected(hostLobby, bobLobby);
        using var h = host;
        using var c = client;
        Assert.True(alice.Carrier.Describe(bob.Me)?.Relayed);
        Assert.True(bob.Carrier.Describe(alice.Me)?.Relayed);
        Assert.Equal(0, cloud.Direct);
        Assert.Equal(0, cloud.Unreachable);
    }

    [Fact]
    public void ForcedRelaysRelayEvenWhenADirectPathIsThere()
    {
        // Relay "force": nobody's address reaches anybody, as on Steam.
        var cloud = new FakeEos();
        using var alice = SignIn(cloud, "alice", EosNat.Open, EosRelay.ForceRelays);
        using var bob = SignIn(cloud, "bob", EosNat.Open);
        using var hostLobby = Hosted(alice);
        using var bobLobby = ByCode(bob, hostLobby.Code);
        var (host, client, _, _, _) = Connected(hostLobby, bobLobby);
        using var h = host;
        using var c = client;
        Assert.True(alice.Carrier.Describe(bob.Me)?.Relayed);
        Assert.Equal(0, cloud.Direct);
    }

    [Fact]
    public void WithoutTheRelaysTwoStrictNatsNeverMeet()
    {
        var cloud = new FakeEos();
        using var alice = SignIn(cloud, "alice", EosNat.Strict, EosRelay.NoRelays);
        using var bob = SignIn(cloud, "bob", EosNat.Strict);
        using var hostLobby = Hosted(alice);
        using var bobLobby = ByCode(bob, hostLobby.Code);
        using var host = OnlineTransport.Host(alice, Fast);
        using var client = OnlineTransport.Connect(bob, bobLobby.Owner, Fast);
        var clientEvents = new List<TransportEvent>();
        var hostEvents = new List<TransportEvent>();
        Until(() => clientEvents.Any(e => e.Kind == TransportEventKind.Disconnected), () =>
        {
            hostLobby.Poll();
            bobLobby.Poll();
            host.Poll(hostEvents);
            client.Poll(clientEvents);
        });
        Assert.DoesNotContain(hostEvents, e => e.Kind == TransportEventKind.Connected);
        Assert.True(cloud.Unreachable > 0);
    }

    [Fact]
    public void SomeoneOutsideTheLobbyCantReachTheHost()
    {
        var cloud = new FakeEos();
        using var alice = SignIn(cloud, "alice");
        using var mallory = SignIn(cloud, "mallory");
        using var lobby = Hosted(alice);
        using var host = OnlineTransport.Host(alice, Fast);
        // Mallory knows who Alice is (she was in a lobby with her once) but isn't in this one.
        using var met = Hosted(mallory);
        var aliceId = alice.Me;
        using var stranger = OnlineTransport.Connect(mallory, aliceId, Fast);
        var hostEvents = new List<TransportEvent>();
        var strangerEvents = new List<TransportEvent>();
        Until(() => strangerEvents.Any(e => e.Kind == TransportEventKind.Disconnected), () =>
        {
            lobby.Poll();
            met.Poll();
            host.Poll(hostEvents);
            stranger.Poll(strangerEvents);
        });
        Assert.Empty(hostEvents);
    }

    [Fact]
    public void AFullAClosedOrAWrongCodeSaysSo()
    {
        var cloud = new FakeEos();
        using var alice = SignIn(cloud, "alice");
        using var lobby = Hosted(alice, max: 2);
        string code = lobby.Code;
        var bob = ByCode(SignIn(cloud, "bob"), code);
        Assert.Equal(Lobby.State.Open, bob.Status);

        using var carolMachine = SignIn(cloud, "carol");
        using var carol = ByCode(carolMachine, code);
        Assert.Equal(Lobby.State.Failed, carol.Status);
        Assert.Equal("the lobby is full", carol.Error);

        bob.Online.Dispose();
        bob.Dispose();
        lobby.SetJoinable(false);
        lobby.Poll();
        lobby.Poll();
        using var dave = ByCode(SignIn(cloud, "dave"), code);
        Assert.Equal($"no open game has the code {JoinCode.Show(code)}", dave.Error);
        // By its id (Carol's machine has seen it), the lobby says it's shut.
        using var again = Lobby.Join(carolMachine, lobby.Id, Game, 1);
        Until(() => again.Status != Lobby.State.Joining, again.Poll);
        Assert.Equal("the lobby is closed", again.Error);
        // An id this machine never saw means nothing to it.
        using var erin = SignIn(cloud, "erin");
        using var unknown = Lobby.Join(erin, lobby.Id, Game, 1);
        Until(() => unknown.Status != Lobby.State.Joining, unknown.Poll);
        Assert.Equal("that lobby is gone", unknown.Error);

        using var typo = ByCode(SignIn(cloud, "frank"), "not a code");
        Assert.Equal("that isn't a join code", typo.Error);
    }

    [Fact]
    public void AHostOnAnotherVersionIsRefusedBeforeConnecting()
    {
        var cloud = new FakeEos();
        using var alice = SignIn(cloud, "alice");
        using var lobby = Hosted(alice);
        using var bob = SignIn(cloud, "bob");
        using var old = ByCode(bob, lobby.Code, protocol: 2);
        Assert.Equal(Lobby.State.Failed, old.Status);
        Assert.Contains("different version", old.Error);
        Until(() => !lobby.Members.Contains(bob.Me), () => { lobby.Poll(); bob.Poll([]); });
    }

    [Fact]
    public void TheHostsValuesGoUpInOneUpdateAFrame()
    {
        // EOS limits how often a lobby is updated: the half-dozen values a new lobby is given are one update, and the owner
        // reads them back at once.
        var cloud = new FakeEos();
        using var alice = SignIn(cloud, "alice");
        var lobby = Lobby.Host(alice, Game, 1, 8, LobbyVisibility.Public, new Dictionary<string, string> { ["name"] = "alice's run", ["tier"] = "frontier" });
        Until(() => lobby.Status == Lobby.State.Open, lobby.Poll);
        Assert.Equal("alice's run", alice.LobbyData(lobby.Id, "name"));
        lobby.Poll();
        Assert.Equal(1, cloud.Updates);
        lobby.SetData("aboard", "2");
        lobby.SetData("aboard", "2");
        lobby.Poll();
        Assert.Equal(2, cloud.Updates);
        lobby.Dispose();
    }

    [Fact]
    public void TheBrowserListsPublicLobbiesOnlyAndHasNoPing()
    {
        var cloud = new FakeEos();
        using var alice = SignIn(cloud, "alice");
        using var bob = SignIn(cloud, "bob");
        using var carol = SignIn(cloud, "carol");
        using var listed = Hosted(alice, visibility: LobbyVisibility.Public);
        using var friends = Hosted(bob, visibility: LobbyVisibility.FriendsOnly);
        carol.RequestLobbyList(new LobbyFilter(Game, 1));
        var events = new List<OnlineEvent>();
        Until(() => events.Any(e => e.Kind == OnlineEventKind.LobbyList), () => carol.Poll(events));
        var found = Assert.Single(events.Single(e => e.Kind == OnlineEventKind.LobbyList).Listings!);
        Assert.Equal(listed.Id, found.Id);
        Assert.Null(found.PingMs);
        // A lobby found in the list is joined by its id.
        using var joined = Lobby.Join(carol, found.Id, Game, 1);
        Until(() => joined.Status != Lobby.State.Joining, joined.Poll);
        Assert.Equal(Lobby.State.Open, joined.Status);
    }

    [Fact]
    public void AnInviteToSomeoneMetBeforeArrivesAsAJoinRequest()
    {
        var cloud = new FakeEos();
        using var alice = SignIn(cloud, "alice");
        using var bob = SignIn(cloud, "bob");
        // Last night's crew: Bob was in Alice's lobby, so her machine knows him.
        using (var last = Hosted(alice))
        using (var was = ByCode(bob, last.Code))
            Until(() => last.Members.Count == 2, last.Poll);
        using var tonight = Hosted(alice);
        alice.Invite(tonight.Id, bob.Me);
        tonight.Poll();
        var events = new List<OnlineEvent>();
        Until(() => events.Any(e => e.Kind == OnlineEventKind.JoinRequested), () => bob.Poll(events));
        var invite = events.Single(e => e.Kind == OnlineEventKind.JoinRequested);
        Assert.Equal(alice.Me, invite.User);
        using var bobLobby = Lobby.Join(bob, invite.Lobby, Game, 1);
        Until(() => bobLobby.Status != Lobby.State.Joining, bobLobby.Poll);
        Assert.Equal(Lobby.State.Open, bobLobby.Status);
        Assert.False(alice.IsFriend(bob.Me)); // no friends list without Epic accounts: a private run asks its password
    }

    [Fact]
    public void TheHostLeavingIsNoticed()
    {
        var cloud = new FakeEos();
        using var alice = SignIn(cloud, "alice");
        var host = Hosted(alice);
        using var bob = SignIn(cloud, "bob");
        using var bobLobby = ByCode(bob, host.Code);
        Assert.False(bobLobby.HostLeft);
        host.Dispose();
        alice.Poll([]);
        Until(() => bobLobby.HostLeft, bobLobby.Poll);
    }

    [Fact]
    public void SigningInSaysPlainlyWhatsWrong()
    {
        var down = new FakeEos { Up = false };
        Assert.Null(EosBackend.TryStart(down.Machine(), "alice", EosRelay.AllowRelays, out var error));
        Assert.Equal("no connection to Epic Online Services", error);
        var wrong = new FakeEos { CredentialsValid = false };
        Assert.Null(EosBackend.TryStart(wrong.Machine(), "alice", EosRelay.AllowRelays, out error));
        Assert.Contains("credentials were refused", error);
    }

    [Fact]
    public void TheCredentialsComeFromEosJsonOrTheEnvironmentAndNeverAlone()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"eos-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            static string? None(string _) => null;
            Assert.Null(EosSettings.Load(dir, None));
            File.WriteAllText(Path.Combine(dir, EosSettings.File), """
                // written by the Release workflow
                { "productId": "p", "sandboxId": "s", "deploymentId": "d", "clientId": "c", "clientSecret": "x" }
                """);
            var s = EosSettings.Load(dir, None)!;
            Assert.True(s.Complete);
            Assert.Equal(EosRelay.AllowRelays, s.RelayControl);
            var env = EosSettings.Load(dir, name => name switch { "DT_EOS_SANDBOX_ID" => "dev", "DT_EOS_RELAY" => "force", _ => null })!;
            Assert.Equal("dev", env.SandboxId);
            Assert.Equal("p", env.ProductId);
            Assert.Equal(EosRelay.ForceRelays, env.RelayControl);
            File.Delete(Path.Combine(dir, EosSettings.File));
            var half = EosSettings.Load(dir, name => name == "DT_EOS_PRODUCT_ID" ? "p" : null)!;
            Assert.False(half.Complete);
            Assert.Null(EosSdk.TryCreate(half, out var why));
            Assert.Contains("incomplete", why);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData("K7Q-M2X", "K7QM2X")]
    [InlineData(" k7q m2x ", "K7QM2X")]
    [InlineData("K7QM2", null)]
    [InlineData("K0QM2X", null)] // no zero: it's read as an O
    [InlineData("192.168.1.4", null)]
    public void AJoinCodeIsSixLettersThatCantBeMisread(string typed, string? code) => Assert.Equal(code, JoinCode.Normalize(typed));

    [Fact]
    public void ALobbysCodeIsMadeFromItsIdAlone() =>
        Assert.Equal(JoinCode.From("lobby00000001"), JoinCode.From("lobby00000001"));
}
