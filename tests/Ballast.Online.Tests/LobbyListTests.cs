using Ballast.Online;

namespace Ballast.Online.Tests;

/// <summary>
/// The lobby browser's platform half (the user's playtest: "I see active lobbies I can join and then what my ping is ... If
/// it's a private lobby its not listed"), against the fake platform, which keeps Steam's rules for a lobby search.
/// </summary>
public class LobbyListTests
{
    const string Game = "darkterritory";

    static Lobby Open(IOnlineBackend who, LobbyVisibility visibility, int protocol = 1, string game = Game, int max = 8)
    {
        var lobby = Lobby.Host(who, game, protocol, max, visibility, new Dictionary<string, string> { ["name"] = $"{who.NameOf(who.Me)}'s run" });
        for (int i = 0; i < 3 && lobby.Status == Lobby.State.Creating; i++)
            lobby.Poll();
        Assert.Equal(Lobby.State.Open, lobby.Status);
        return lobby;
    }

    static IReadOnlyList<LobbyListing> Search(IOnlineBackend who, LobbyFilter filter)
    {
        who.RequestLobbyList(filter);
        var events = new List<OnlineEvent>();
        who.Poll(events);
        return Assert.Single(events, e => e.Kind == OnlineEventKind.LobbyList).Listings!;
    }

    [Fact]
    public void ASearchFindsThePublicLobbiesOfThisGameAndProtocolWithTheirPing()
    {
        var cloud = new FakeOnline();
        var near = cloud.SignIn("near", (30, 40));
        var far = cloud.SignIn("far", (300, 400));
        var secret = cloud.SignIn("secret", (3, 4));
        var old = cloud.SignIn("old", (6, 8));
        var other = cloud.SignIn("other", (9, 12));
        var me = cloud.SignIn("me");
        using var a = Open(near, LobbyVisibility.Public);
        using var b = Open(far, LobbyVisibility.Public);
        using var c = Open(secret, LobbyVisibility.FriendsOnly);
        using var d = Open(old, LobbyVisibility.Public, protocol: 0);
        using var e = Open(other, LobbyVisibility.Public, game: "someothergame");

        var found = Search(me, new LobbyFilter(Game, 1));
        Assert.Equal([a.Id, b.Id], found.Select(l => l.Id));
        var first = found[0];
        Assert.Equal(("near's run", "1", "1", 1, 8), (first.Get("name"), first.Get(Lobby.ProtocolKey), first.Get(Lobby.PublicKey), first.Members, first.MaxMembers));
        // The host published where it is; the search estimates the round trip from here.
        Assert.Equal(near.LocalPingLocation, first.Get(Lobby.PingKey));
        Assert.Equal(50, first.PingMs);
        Assert.Equal(500, found[1].PingMs);
        // A private lobby is never listed, even asked for "any": Steam's search doesn't show friends-only lobbies.
        Assert.DoesNotContain(Search(me, new LobbyFilter(Game, null)), l => l.Id == c.Id);
    }

    [Fact]
    public void AClosedLobbyDropsOffTheList()
    {
        var cloud = new FakeOnline();
        var host = cloud.SignIn("host");
        var me = cloud.SignIn("me");
        using var lobby = Open(host, LobbyVisibility.Public);
        Assert.Single(Search(me, new LobbyFilter(Game, 1)));
        // Out of the yard, the doors close (spec E's drop-in rules): nobody can find it to join.
        lobby.SetJoinable(false);
        Assert.Empty(Search(me, new LobbyFilter(Game, 1)));
    }

    [Fact]
    public void TheHostKeepsItsCrewCountUpToDate()
    {
        var cloud = new FakeOnline();
        var host = cloud.SignIn("host");
        using var lobby = Open(host, LobbyVisibility.Public);
        lobby.SetData("aboard", "3");
        Assert.Equal("3", host.LobbyData(lobby.Id, "aboard"));
    }
}
