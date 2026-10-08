using Ballast.Net;

namespace Ballast.Online;

/// <summary>A platform account (a SteamID, an EOS product user id).</summary>
public readonly record struct UserId(ulong Value)
{
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>A platform lobby: where friends meet before the game connects them to its host.</summary>
public readonly record struct LobbyId(ulong Value)
{
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public enum OnlineEventKind : byte
{
    /// <summary>A lobby we asked for exists, and we're in it as its owner.</summary>
    LobbyCreated,
    /// <summary>We're in a lobby we asked to join.</summary>
    LobbyEntered,
    /// <summary>Creating or joining failed (<see cref="OnlineEvent.Error"/> says why).</summary>
    LobbyFailed,
    /// <summary>Someone came into or left a lobby we're in.</summary>
    MembersChanged,
    /// <summary>The player accepted an invite or clicked "Join Game" on a friend (<see cref="OnlineEvent.User"/>).</summary>
    JoinRequested,
    /// <summary>The lobbies a <see cref="IOnlineBackend.RequestLobbyList"/> found (<see cref="OnlineEvent.Listings"/>).</summary>
    LobbyList,
}

/// <param name="Listings">With <see cref="OnlineEventKind.LobbyList"/>: what was found.</param>
public readonly record struct OnlineEvent(OnlineEventKind Kind, LobbyId Lobby, UserId User = default, string? Error = null,
    IReadOnlyList<LobbyListing>? Listings = null);

/// <summary>Who can find a lobby.</summary>
public enum LobbyVisibility : byte
{
    /// <summary>Listed: anyone running the game finds it in a lobby search.</summary>
    Public,
    /// <summary>Not listed: friends join it from the friends list, or by invite.</summary>
    FriendsOnly,
}

/// <summary>What a lobby search looks for: lobbies of this game, on this protocol (null: any), and only listed ones.</summary>
public sealed record LobbyFilter(string Game, int? Protocol)
{
    public bool PublicOnly { get; init; } = true;
}

/// <summary>A lobby a search found: its members, its limit, its data, and the estimated ping to its owner.</summary>
/// <param name="Data">Every value the owner set on it (<see cref="Lobby.GameKey"/> and the rest).</param>
/// <param name="PingMs">
/// The platform's estimate of the round trip to the owner from the location the owner published (<see cref="Lobby.PingKey"/>);
/// null when there's none yet.
/// </param>
public sealed record LobbyListing(LobbyId Id, int Members, int MaxMembers, IReadOnlyDictionary<string, string> Data, int? PingMs)
{
    public string Get(string key) => Data.GetValueOrDefault(key, "");
}

/// <summary>
/// A platform's online services: identity, lobbies, invites and relayed P2P datagrams (ARCHITECTURE §4, §8 note 24).
/// Implementations: <see cref="Steam.SteamBackend"/>, and <see cref="FakeOnline"/> for tests and the harness.
/// EOS (itch.io builds) will be the third. Everything asynchronous comes back as an <see cref="OnlineEvent"/> from
/// <see cref="Poll"/>, which also pumps the platform's callbacks, so call it every frame.
/// </summary>
public interface IOnlineBackend : IDisposable
{
    string Platform { get; }
    UserId Me { get; }
    string NameOf(UserId user);

    /// <summary>
    /// Datagrams to and from other users, relayed by the platform: no ports to open and no IP addresses shown.
    /// Datagrams are accepted only from users in a lobby with us, or users we've sent to first.
    /// </summary>
    IDatagramCarrier<UserId> Carrier { get; }

    void Poll(List<OnlineEvent> into);

    void CreateLobby(int maxMembers, LobbyVisibility visibility);
    void JoinLobby(LobbyId lobby);
    void LeaveLobby(LobbyId lobby);
    UserId OwnerOf(LobbyId lobby);
    IReadOnlyList<UserId> MembersOf(LobbyId lobby);
    /// <summary>A lobby value, or "" when it isn't set (the platforms' own convention).</summary>
    string LobbyData(LobbyId lobby, string key);
    void SetLobbyData(LobbyId lobby, string key, string value);
    void SetJoinable(LobbyId lobby, bool joinable);
    /// <summary>How many members the lobby takes (the owner's only, as on Steam).</summary>
    void SetMemberLimit(LobbyId lobby, int max);

    /// <summary>Searches for lobbies; the result comes back as a <see cref="OnlineEventKind.LobbyList"/> on a later poll.</summary>
    void RequestLobbyList(LobbyFilter filter);

    /// <summary>
    /// Where this machine is on the platform's network, as text a lobby can carry ("" until the platform has measured it):
    /// Steam's ping location, from its relays. Someone else estimates their ping to us from it with <see cref="EstimatePingMs"/>.
    /// </summary>
    string LocalPingLocation { get; }

    /// <summary>The estimated round trip from here to a machine at <paramref name="location"/>, in ms; null if it can't say.</summary>
    int? EstimatePingMs(string location);

    /// <summary>The platform's own invite picker (the Steam overlay).</summary>
    void ShowInviteDialog(LobbyId lobby);
    /// <summary>Invites one user directly.</summary>
    void Invite(LobbyId lobby, UserId user);
    /// <summary>
    /// The platform's store page for <paramref name="app"/> over the game (the Steam overlay's, with its wishlist button).
    /// False if it can't be shown there (the overlay's off), for the caller to open it some other way.
    /// </summary>
    bool ShowStorePage(uint app);
}

/// <summary>What a platform passes a game it launches to accept an invite.</summary>
public static class LaunchArgs
{
    /// <summary>Steam starts the game with <c>+connect_lobby &lt;id&gt;</c> when an invite is accepted while it isn't running.</summary>
    public static LobbyId? ConnectLobby(IReadOnlyList<string> args)
    {
        for (int i = 0; i + 1 < args.Count; i++)
            if (args[i] is "+connect_lobby" or "--join-lobby" && ulong.TryParse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture, out ulong id))
                return new LobbyId(id);
        return null;
    }
}
