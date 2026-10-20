using System.Diagnostics.CodeAnalysis;
using Ballast.Net;

namespace Ballast.Online.Eos;

/// <summary>
/// Epic Online Services as the game's third online platform (note 896; ARCHITECTURE §4, note 24), for players off Steam
/// (the itch.io build): a device-id sign-in (no Epic account), lobbies that friends reach by a join code, invites to people
/// met before, and P2P packets that go straight to the peer when NAT allows and through Epic's relays when it doesn't. Free
/// of charge, and no servers of ours.
/// <para>
/// What EOS doesn't have, without Epic accounts, and what stands in: no friends list (<see cref="IsFriend"/> is false, so a
/// private run asks everyone its password), no overlay (the host reads out <see cref="IJoinCodes.CodeOf"/>), no ping
/// estimates (the browser's ping column is blank). Every lobby is advertised to EOS, since a join code is a search, and
/// <see cref="Lobby.PublicKey"/> keeps the friends-only ones out of the game's list, as on Steam.
/// </para>
/// <para>
/// The EOS SDK isn't thread-safe: everything it's asked happens in <see cref="Poll"/>, on the game's thread. The carrier
/// only queues packets for it, so a transport's pump thread can use it.
/// </para>
/// </summary>
public sealed class EosBackend : IOnlineBackend, IJoinCodes
{
    /// <summary>The lobby attribute that holds its join code.</summary>
    public const string CodeKey = "code";
    /// <summary>The member attribute each member sets to their name (EOS's device-id users have no name anyone else can read).</summary>
    public const string NameKey = "name";

    readonly IEosSdk _sdk;
    readonly string _me;
    readonly string _name;
    readonly List<OnlineEvent> _events = new();
    readonly HashSet<string> _in = new(StringComparer.Ordinal);
    readonly Dictionary<ulong, string> _users = new(), _lobbies = new();
    // The last we saw of a lobby we're not in (a search's result, one we've left): for its values after a refused join.
    readonly Dictionary<string, EosLobby> _seen = new(StringComparer.Ordinal);
    // The owner's changes not yet sent (one UpdateLobby per lobby per poll), and those sent but not yet confirmed.
    readonly Dictionary<string, EosLobbyChange> _changes = new(StringComparer.Ordinal);
    readonly Dictionary<string, List<EosLobbyChange>> _sent = new(StringComparer.Ordinal);
    readonly HashSet<string> _asking = new(StringComparer.Ordinal);
    readonly EosCarrier _carrier;
    readonly byte[] _buffer = new byte[2048];
    bool _disposed;

    EosBackend(IEosSdk sdk, string me, string name)
    {
        _sdk = sdk;
        _me = me;
        _name = name;
        Me = User(me);
        _carrier = new EosCarrier(this);
        sdk.ConnectionRequested += peer => _asking.Add(peer);
        sdk.InviteReceived += (lobby, from) => _events.Add(new OnlineEvent(OnlineEventKind.JoinRequested, LobbyOf(lobby), User(from)));
        sdk.MemberStatus += (lobby, user, status) =>
        {
            if (!_in.Contains(lobby))
                return;
            if (status == EosMemberStatus.Closed)
                _in.Remove(lobby);
            _events.Add(new OnlineEvent(OnlineEventKind.MembersChanged, LobbyOf(lobby), User(user)));
        };
    }

    /// <summary>Signs this machine in to EOS, or says why it can't; the backend owns <paramref name="sdk"/> from here.</summary>
    /// <param name="whileWaiting">Called while the sign-in is on its way (a loading screen's frame).</param>
    public static EosBackend? TryStart(IEosSdk sdk, string displayName, EosRelay relay, out string? error, double timeoutSeconds = 15,
        Action? whileWaiting = null)
    {
        sdk.SetRelayControl(relay);
        EosResult? result = null;
        string? me = null;
        sdk.Login(displayName, (r, id) => (result, me) = (r, id));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (result is null && clock.Elapsed.TotalSeconds < timeoutSeconds)
        {
            sdk.Tick();
            if (result is null)
            {
                whileWaiting?.Invoke();
                Thread.Sleep(5);
            }
        }
        if (result != EosResult.Success || me is null)
        {
            error = result switch
            {
                null => "EOS didn't answer the sign-in",
                EosResult.NoConnection => "no connection to Epic Online Services",
                EosResult.InvalidCredentials => "the game's EOS credentials were refused (eos.json, or the DT_EOS_* variables)",
                _ => $"EOS refused the sign-in ({result})",
            };
            sdk.Dispose();
            return null;
        }
        error = null;
        return new EosBackend(sdk, me, displayName);
    }

    public string Platform => "EOS";
    public UserId Me { get; }
    public IDatagramCarrier<UserId> Carrier => _carrier;
    /// <summary>The SDK underneath, for a test's look at it.</summary>
    public IEosSdk Sdk => _sdk;

    public string NameOf(UserId user)
    {
        if (user == Me)
            return _name;
        if (_users.TryGetValue(user.Value, out var puid))
            foreach (var lobby in _in.Select(id => _sdk.Lobby(_me, id)).Concat(_seen.Values))
                if (lobby?.MemberAttributes.GetValueOrDefault(puid)?.GetValueOrDefault(NameKey) is { Length: > 0 } name)
                    return name;
        return "someone";
    }

    /// <summary>Device-id users have no friends list: nobody's let into a private run without its password.</summary>
    public bool IsFriend(UserId user) => false;

    public void Poll(List<OnlineEvent> into)
    {
        _carrier.Flush(this);
        foreach (var (lobby, change) in _changes)
        {
            if (change.IsEmpty || !_in.Contains(lobby))
                continue;
            _sent.TryAdd(lobby, []);
            _sent[lobby].Add(change);
            _sdk.UpdateLobby(_me, lobby, change, _ =>
            {
                if (_sent.TryGetValue(lobby, out var waiting))
                    waiting.Remove(change);
            });
        }
        _changes.Clear();
        _sdk.Tick();
        // Only lobby-mates are let reach us. Someone can ask a moment before their arrival reaches us, so a request is
        // held and accepted once they're in, as on Steam.
        foreach (var peer in _asking.ToList())
            if (_in.Any(l => _sdk.Lobby(_me, l)?.Members.Contains(peer, StringComparer.Ordinal) == true))
            {
                _sdk.AcceptConnection(_me, peer);
                _asking.Remove(peer);
            }
        _carrier.Fill(this);
        into.AddRange(_events);
        _events.Clear();
    }

    public void CreateLobby(int maxMembers, LobbyVisibility visibility) => _sdk.CreateLobby(_me, maxMembers, (r, id) =>
    {
        if (r != EosResult.Success || id is null)
        {
            _events.Add(new OnlineEvent(OnlineEventKind.LobbyFailed, default, Error: $"EOS couldn't make a lobby ({Why(r)})"));
            return;
        }
        _in.Add(id);
        var change = Change(id);
        change.Attributes[CodeKey] = JoinCode.From(id);
        change.MemberAttributes[NameKey] = _name;
        _events.Add(new OnlineEvent(OnlineEventKind.LobbyCreated, LobbyOf(id)));
    });

    public void JoinLobby(LobbyId lobby)
    {
        if (_lobbies.TryGetValue(lobby.Value, out var id))
            Join(id, lobby);
        else
            _events.Add(new OnlineEvent(OnlineEventKind.LobbyFailed, lobby, Error: "that lobby is gone"));
    }

    // A search by id first, so a shut or full lobby says so, and its values are there to read when it does.
    void Join(string id, LobbyId lobby) => _sdk.SearchLobbies(_me, new EosSearch(new Dictionary<string, string>()) { LobbyId = id }, (r, found) =>
    {
        if (r != EosResult.Success)
            Fail(lobby, Why(r));
        else if (found.Count == 0)
            Fail(lobby, "that lobby is gone");
        else
            Enter(found[0]);
    });

    void Enter(EosLobby l)
    {
        _seen[l.Id] = l;
        var lobby = LobbyOf(l.Id);
        if (!l.Open)
            Fail(lobby, "the lobby is closed");
        else if (l.Members.Count >= l.MaxMembers && !l.Members.Contains(_me))
            Fail(lobby, "the lobby is full");
        else
            _sdk.JoinLobby(_me, l.Id, r =>
            {
                if (r != EosResult.Success)
                {
                    Fail(lobby, Why(r));
                    return;
                }
                _in.Add(l.Id);
                Change(l.Id).MemberAttributes[NameKey] = _name;
                _events.Add(new OnlineEvent(OnlineEventKind.LobbyEntered, lobby));
            });
    }

    void Fail(LobbyId lobby, string why) => _events.Add(new OnlineEvent(OnlineEventKind.LobbyFailed, lobby, Error: why));

    static string Why(EosResult r) => r switch
    {
        EosResult.NotFound => "that lobby is gone",
        EosResult.LobbyFull => "the lobby is full",
        EosResult.NotAllowed => "the lobby is closed",
        EosResult.NoConnection => "no connection to EOS",
        _ => $"EOS refused ({r})",
    };

    public void JoinByCode(string code)
    {
        if (JoinCode.Normalize(code) is not { } c)
        {
            Fail(default, "that isn't a join code");
            return;
        }
        _sdk.SearchLobbies(_me, new EosSearch(new Dictionary<string, string> { [CodeKey] = c }, MaxResults: 2), (r, found) =>
        {
            if (r != EosResult.Success)
                Fail(default, Why(r));
            else if (found.Count == 0)
                Fail(default, $"no open game has the code {JoinCode.Show(c)}");
            else
                Enter(found[0]);
        });
    }

    /// <summary>The code once EOS has it, so it's only shown when a friend's search can find it (not while it's on its way).</summary>
    public string CodeOf(LobbyId lobby) => Details(lobby)?.Get(CodeKey) ?? "";

    public void LeaveLobby(LobbyId lobby)
    {
        if (!_lobbies.TryGetValue(lobby.Value, out var id) || !_in.Contains(id))
            return;
        if (_sdk.Lobby(_me, id) is { } last)
            _seen[id] = last;
        _in.Remove(id);
        _changes.Remove(id);
        _sent.Remove(id);
        _sdk.LeaveLobby(_me, id, _ => { });
    }

    EosLobby? Details(LobbyId lobby) =>
        !_lobbies.TryGetValue(lobby.Value, out var id) ? null : (_in.Contains(id) ? _sdk.Lobby(_me, id) : null) ?? _seen.GetValueOrDefault(id);

    public UserId OwnerOf(LobbyId lobby) => Details(lobby) is { } l ? User(l.Owner) : default;
    public IReadOnlyList<UserId> MembersOf(LobbyId lobby) => Details(lobby) is { } l ? [.. l.Members.Select(User)] : [];

    /// <summary>The owner sees its own values at once, as on Steam: what's waiting to go and what's on its way come first.</summary>
    public string LobbyData(LobbyId lobby, string key)
    {
        if (_lobbies.TryGetValue(lobby.Value, out var id))
        {
            if (_changes.TryGetValue(id, out var change) && change.Attributes.TryGetValue(key, out var waiting))
                return waiting;
            if (_sent.TryGetValue(id, out var sent))
                for (int i = sent.Count - 1; i >= 0; i--)
                    if (sent[i].Attributes.TryGetValue(key, out var going))
                        return going;
        }
        return Details(lobby)?.Get(key) ?? "";
    }

    bool Owns(LobbyId lobby, [NotNullWhen(true)] out string? id) =>
        _lobbies.TryGetValue(lobby.Value, out id) && _in.Contains(id) && _sdk.Lobby(_me, id)?.Owner == _me;

    EosLobbyChange Change(string id)
    {
        if (!_changes.TryGetValue(id, out var c))
            _changes[id] = c = new EosLobbyChange();
        return c;
    }

    // As on Steam, only the owner can.
    public void SetLobbyData(LobbyId lobby, string key, string value)
    {
        if (Owns(lobby, out var id))
            Change(id).Attributes[key] = value;
    }

    public void SetJoinable(LobbyId lobby, bool joinable)
    {
        if (Owns(lobby, out var id))
            Change(id).Open = joinable;
    }

    public void SetMemberLimit(LobbyId lobby, int max)
    {
        if (Owns(lobby, out var id))
            Change(id).MaxMembers = max;
    }

    public void RequestLobbyList(LobbyFilter filter)
    {
        var equal = new Dictionary<string, string> { [Lobby.GameKey] = filter.Game };
        if (filter.Protocol is { } protocol)
            equal[Lobby.ProtocolKey] = protocol.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (filter.PublicOnly)
            equal[Lobby.PublicKey] = "1";
        _sdk.SearchLobbies(_me, new EosSearch(equal), (r, found) =>
        {
            foreach (var l in found)
                _seen[l.Id] = l;
            var listings = found.Select(l => new LobbyListing(LobbyOf(l.Id), l.Members.Count, l.MaxMembers, l.Attributes, PingMs: null)).ToList();
            _events.Add(new OnlineEvent(OnlineEventKind.LobbyList, default, Error: r == EosResult.Success ? null : Why(r), Listings: listings));
        });
    }

    /// <summary>EOS has no ping estimates: the browser's ping column stays blank on this platform.</summary>
    public string LocalPingLocation => "";
    public int? EstimatePingMs(string location) => null;

    /// <summary>No overlay: the game shows the lobby's join code instead (<see cref="CodeOf"/>).</summary>
    public void ShowInviteDialog(LobbyId lobby)
    {
    }

    /// <summary>Invites someone met before (a crewmate from an earlier night): their game hears it as a join request.</summary>
    public void Invite(LobbyId lobby, UserId user)
    {
        if (_lobbies.TryGetValue(lobby.Value, out var id) && _users.TryGetValue(user.Value, out var puid))
            _sdk.SendInvite(_me, id, puid, _ => { });
    }

    public bool ShowStorePage(uint app) => false;

    UserId User(string puid)
    {
        var id = new UserId(JoinCode.Fnv(puid));
        _users[id.Value] = puid;
        return id;
    }

    LobbyId LobbyOf(string id)
    {
        var lobby = new LobbyId(JoinCode.Fnv(id));
        _lobbies[lobby.Value] = id;
        return lobby;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (var id in _in.ToList())
            _sdk.LeaveLobby(_me, id, _ => { });
        _in.Clear();
        _sdk.Dispose();
    }

    /// <summary>
    /// EOS P2P on one socket, channel 0, addressed by product user. Packets wait here for the backend's poll, on the game's
    /// thread (the SDK isn't thread-safe), both ways.
    /// </summary>
    sealed class EosCarrier(EosBackend owner) : IDatagramCarrier<UserId>
    {
        readonly Queue<(UserId To, byte[] Data)> _out = new();
        readonly Queue<(UserId From, byte[] Data)> _in = new();
        readonly List<UserId> _forget = new();
        readonly Dictionary<UserId, CarrierLink> _links = new();
        readonly HashSet<UserId> _peers = new();

        public void Send(UserId to, ReadOnlySpan<byte> datagram)
        {
            lock (this)
                _out.Enqueue((to, datagram.ToArray()));
        }

        public bool TryReceive(Span<byte> buffer, out int length, [MaybeNullWhen(false)] out UserId from)
        {
            (UserId From, byte[] Data) p;
            bool got;
            lock (this)
                got = _in.TryDequeue(out p);
            if (!got)
            {
                length = 0;
                from = default;
                return false;
            }
            length = Math.Min(p.Data.Length, buffer.Length);
            p.Data.AsSpan(0, length).CopyTo(buffer);
            from = p.From;
            return true;
        }

        public void Forget(UserId peer)
        {
            lock (this)
                _forget.Add(peer);
        }

        /// <summary>EOS's own word on the connection (netcode-audit.md gap 3): through Epic's relays or straight to the peer.</summary>
        public CarrierLink? Describe(UserId peer)
        {
            lock (this)
                return _links.GetValueOrDefault(peer);
        }

        public void Flush(EosBackend b)
        {
            lock (this)
            {
                while (_out.TryDequeue(out var p))
                    if (b._users.TryGetValue(p.To.Value, out var puid))
                    {
                        _peers.Add(p.To);
                        b._sdk.SendPacket(b._me, puid, p.Data);
                    }
                foreach (var peer in _forget)
                {
                    if (b._users.TryGetValue(peer.Value, out var puid))
                        b._sdk.CloseConnection(b._me, puid);
                    _peers.Remove(peer);
                    _links.Remove(peer);
                }
                _forget.Clear();
            }
        }

        public void Fill(EosBackend b)
        {
            lock (this)
            {
                while (b._sdk.ReceivePacket(b._me, b._buffer, out int length, out string from))
                {
                    var peer = b.User(from);
                    _peers.Add(peer);
                    _in.Enqueue((peer, b._buffer.AsSpan(0, length).ToArray()));
                }
                foreach (var peer in _peers)
                    if (b._users.TryGetValue(peer.Value, out var puid) && b._sdk.ConnectionTo(puid) is { } c)
                        _links[peer] = new CarrierLink(owner.Platform, c.Relayed);
                    else
                        _links.Remove(peer);
            }
        }

        public void Dispose()
        {
        }
    }
}
