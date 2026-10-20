namespace Ballast.Online;

/// <summary>
/// Hosting or joining through a platform lobby. The lobby is only the meeting place and the invite list: a joiner
/// reads who owns it and connects an <see cref="OnlineTransport"/> to them, and the game runs over that. Staying
/// in the lobby while playing is what lets friends "Join Game" from the friends list, and lets the host's
/// platform accept the joiner's datagrams.
/// </summary>
public sealed class Lobby : IDisposable
{
    public const string GameKey = "game", ProtocolKey = "protocol", HostKey = "host";
    /// <summary>"1" on a listed lobby (<see cref="LobbyVisibility.Public"/>): a search can ask for these only.</summary>
    public const string PublicKey = "public";
    /// <summary>The owner's <see cref="IOnlineBackend.LocalPingLocation"/>, for a browser's ping estimate.</summary>
    public const string PingKey = "ping";

    public enum State : byte { Creating, Joining, Open, Failed, Closed }

    readonly IOnlineBackend _online;
    readonly string _game;
    readonly int _protocol;
    readonly List<OnlineEvent> _events = new();
    readonly IReadOnlyDictionary<string, string> _data;
    readonly LobbyVisibility _visibility;
    LobbyId? _target;
    bool _pingPublished;

    Lobby(IOnlineBackend online, string game, int protocol, bool host, LobbyId? target, LobbyVisibility visibility = LobbyVisibility.FriendsOnly,
        IReadOnlyDictionary<string, string>? data = null)
    {
        _visibility = visibility;
        _data = data ?? new Dictionary<string, string>();
        _online = online;
        _game = game;
        _protocol = protocol;
        IsHost = host;
        _target = target;
        Status = host ? State.Creating : State.Joining;
    }

    /// <summary>Creates a lobby for a game we host: friends-only, or listed for anyone to find.</summary>
    /// <param name="data">The game's own values to set on it once it exists (a name, a tier), beside the game and protocol.</param>
    public static Lobby Host(IOnlineBackend online, string game, int protocol, int maxMembers, LobbyVisibility visibility = LobbyVisibility.FriendsOnly,
        IReadOnlyDictionary<string, string>? data = null)
    {
        var lobby = new Lobby(online, game, protocol, host: true, target: null, visibility, data) { MemberLimit = maxMembers };
        online.CreateLobby(maxMembers, visibility);
        return lobby;
    }

    public LobbyVisibility Visibility => _visibility;

    /// <summary>Joins a friend's lobby (from an invite, "Join Game", or <c>+connect_lobby</c>).</summary>
    public static Lobby Join(IOnlineBackend online, LobbyId id, string game, int protocol)
    {
        var lobby = new Lobby(online, game, protocol, host: false, target: id);
        online.JoinLobby(id);
        return lobby;
    }

    /// <summary>
    /// Joins a friend's lobby by the code they read out (EOS, note 896): the platform finds it, then it's as
    /// <see cref="Join"/>. Fails at once on a platform without codes (Steam has its invites).
    /// </summary>
    public static Lobby JoinByCode(IOnlineBackend online, string code, string game, int protocol)
    {
        var lobby = new Lobby(online, game, protocol, host: false, target: null) { _byCode = true };
        if (online is IJoinCodes codes)
            codes.JoinByCode(code);
        else
            lobby.Fail($"{online.Platform} has no join codes");
        return lobby;
    }

    bool _byCode;

    /// <summary>The code friends type to join this lobby, on a platform with codes (EOS); "" otherwise or until it's made.</summary>
    public string Code => Status == State.Open && _online is IJoinCodes codes ? codes.CodeOf(Id) : "";

    public IOnlineBackend Online => _online;
    public bool IsHost { get; }
    public State Status { get; private set; }
    public LobbyId Id { get; private set; }
    /// <summary>Who hosts the game: the lobby's owner when we arrived.</summary>
    public UserId Owner { get; private set; }
    public string? Error { get; private set; }
    public IReadOnlyList<UserId> Members => Status == State.Open ? _online.MembersOf(Id) : [];
    /// <summary>The host is no longer in the lobby (the platform may have handed it to someone else; the game is over).</summary>
    public bool HostLeft => Status == State.Open && !IsHost && !Members.Contains(Owner);

    /// <summary>
    /// A friend's lobby the player chose to join while here (an accepted invite, "Join Game"). The app decides
    /// what to do about it; <see cref="TakeJoinRequest"/> clears it.
    /// </summary>
    public LobbyId? JoinRequest { get; private set; }

    public LobbyId? TakeJoinRequest()
    {
        var r = JoinRequest;
        JoinRequest = null;
        return r;
    }

    /// <summary>Pumps the platform. Call every frame.</summary>
    public void Poll()
    {
        _events.Clear();
        _online.Poll(_events);
        foreach (var e in _events)
        {
            switch (e.Kind)
            {
                case OnlineEventKind.LobbyCreated when Status == State.Creating:
                    Id = e.Lobby;
                    Owner = _online.Me;
                    _online.SetLobbyData(Id, GameKey, _game);
                    _online.SetLobbyData(Id, ProtocolKey, _protocol.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    _online.SetLobbyData(Id, HostKey, _online.NameOf(_online.Me));
                    _online.SetLobbyData(Id, PublicKey, _visibility == LobbyVisibility.Public ? "1" : "0");
                    foreach (var (key, value) in _data)
                        _online.SetLobbyData(Id, key, value);
                    Status = State.Open;
                    break;
                case OnlineEventKind.LobbyEntered when Status == State.Joining && (e.Lobby == _target || _byCode && _target is null):
                    Entered(e.Lobby);
                    break;
                case OnlineEventKind.LobbyFailed when Status is State.Creating or State.Joining:
                    // Joined by id, it's the one asked for; by code, the one the code found (its values say why, note 254).
                    Id = _target ?? e.Lobby;
                    Fail(e.Error ?? "the lobby couldn't be reached");
                    break;
                case OnlineEventKind.JoinRequested when e.Lobby != Id:
                    JoinRequest = e.Lobby;
                    break;
            }
        }
        // The platform measures where we are a few seconds after it starts; the lobby carries it once it has, so a
        // browser can estimate its ping to us.
        if (IsHost && Status == State.Open && !_pingPublished && _online.LocalPingLocation is { Length: > 0 } where)
        {
            _online.SetLobbyData(Id, PingKey, where);
            _pingPublished = true;
        }
    }

    /// <summary>Sets one of the lobby's values (the host's only: the crew aboard as it changes).</summary>
    public void SetData(string key, string value)
    {
        if (IsHost && Status == State.Open && _online.LobbyData(Id, key) != value)
            _online.SetLobbyData(Id, key, value);
    }

    void Entered(LobbyId id)
    {
        Id = id;
        string game = _online.LobbyData(id, GameKey);
        string protocol = _online.LobbyData(id, ProtocolKey);
        if (game != _game)
        {
            _online.LeaveLobby(id);
            Fail(game.Length == 0 ? "that isn't a game lobby" : $"that lobby is for {game}");
            return;
        }
        if (protocol != _protocol.ToString(System.Globalization.CultureInfo.InvariantCulture))
        {
            _online.LeaveLobby(id);
            Fail($"the host is on a different version (protocol {protocol}, you have {_protocol})");
            return;
        }
        Owner = _online.OwnerOf(id);
        Status = State.Open;
    }

    void Fail(string why)
    {
        Error = why;
        Status = State.Failed;
    }

    /// <summary>Closes the doors (a full crew) or opens them again. Only said to the platform when it changes.</summary>
    public void SetJoinable(bool joinable)
    {
        if (IsHost && Status == State.Open && Joinable != joinable)
        {
            _online.SetJoinable(Id, joinable);
            Joinable = joinable;
        }
    }

    /// <summary>Whether the doors are open, as this host last set them (a new lobby's are).</summary>
    public bool Joinable { get; private set; } = true;

    /// <summary>How many the platform lets in (the crew cap); only said to the platform when it changes.</summary>
    public void SetMemberLimit(int max)
    {
        if (IsHost && Status == State.Open && MemberLimit != max)
        {
            _online.SetMemberLimit(Id, max);
            MemberLimit = max;
        }
    }

    /// <summary>The member limit as this host created it or last set it (0 for a lobby joined).</summary>
    public int MemberLimit { get; private set; }

    public void ShowInviteDialog()
    {
        if (Status == State.Open)
            _online.ShowInviteDialog(Id);
    }

    public void Dispose()
    {
        if (Status == State.Open)
            _online.LeaveLobby(Id);
        Status = State.Closed;
    }
}
