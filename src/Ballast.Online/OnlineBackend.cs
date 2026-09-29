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
}

public readonly record struct OnlineEvent(OnlineEventKind Kind, LobbyId Lobby, UserId User = default, string? Error = null);

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

    void CreateLobby(int maxMembers);
    void JoinLobby(LobbyId lobby);
    void LeaveLobby(LobbyId lobby);
    UserId OwnerOf(LobbyId lobby);
    IReadOnlyList<UserId> MembersOf(LobbyId lobby);
    /// <summary>A lobby value, or "" when it isn't set (the platforms' own convention).</summary>
    string LobbyData(LobbyId lobby, string key);
    void SetLobbyData(LobbyId lobby, string key, string value);
    void SetJoinable(LobbyId lobby, bool joinable);

    /// <summary>The platform's own invite picker (the Steam overlay).</summary>
    void ShowInviteDialog(LobbyId lobby);
    /// <summary>Invites one user directly.</summary>
    void Invite(LobbyId lobby, UserId user);
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
