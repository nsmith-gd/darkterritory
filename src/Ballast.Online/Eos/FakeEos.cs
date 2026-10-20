namespace Ballast.Online.Eos;

/// <summary>
/// An in-process stand-in for Epic Online Services (note 896), as <see cref="FakeOnline"/> is for Steam: device-id sign-in,
/// lobbies, invites and P2P packets, for the tests and <c>dt harness --eos</c> (there's no EOS SDK in CI). It keeps the rules
/// <see cref="EosBackend"/> depends on:
/// <list type="bullet">
/// <item>Every request answers on a later <see cref="IEosSdk.Tick"/>, never inside the call; notifications too.</item>
/// <item>A packet from a peer we haven't accepted waits, and asks for a connection, once; accepting it lets what waited through
/// (EOS's delayed delivery), and sending to a peer accepts it. A peer never accepted never gets through.</item>
/// <item>Each machine is behind a NAT. Two can meet directly unless both are strict, or one strict and one moderate; then the
/// relays carry them, unless either side said no relays, in which case nothing gets through. Forced relays always relay.</item>
/// <item>Lobbies have a member limit and a permission level; a shut or full one refuses a join; the owner leaving promotes the next.</item>
/// <item>A search finds only advertised (open) lobbies whose attributes match, except a search by id, which finds the lobby as it is.</item>
/// </list>
/// </summary>
public sealed class FakeEos
{
    readonly Dictionary<string, Device> _users = new(StringComparer.Ordinal);
    readonly Dictionary<string, Room> _lobbies = new(StringComparer.Ordinal);
    int _nextUser = 1, _nextLobby = 1;

    /// <summary>Epic's servers answer (false: every request fails with <see cref="EosResult.NoConnection"/>).</summary>
    public bool Up { get; set; } = true;
    /// <summary>The game's credentials are right (false: sign-in fails with <see cref="EosResult.InvalidCredentials"/>).</summary>
    public bool CredentialsValid { get; set; } = true;

    /// <summary>Packets held because the receiver hadn't accepted the sender (yet).</summary>
    public int Refused { get; private set; }
    /// <summary>Packets dropped because no path joins the two (strict NATs with the relays refused).</summary>
    public int Unreachable { get; private set; }
    public int Direct { get; private set; }
    public int Relayed { get; private set; }
    /// <summary>EOS_Lobby_UpdateLobby calls made: EOS rate-limits them, so the backend should gather a frame's changes into one.</summary>
    public int Updates { get; private set; }
    public int Lobbies => _lobbies.Count;

    /// <summary>A machine with the game on it, behind <paramref name="nat"/>; it signs in through <see cref="IEosSdk.Login"/>.</summary>
    public IEosSdk Machine(EosNat nat = EosNat.Moderate) => new Device(this, nat);

    sealed class Room(string id, string owner, int max)
    {
        public readonly string Id = id;
        public string Owner = owner;
        public readonly List<string> Members = [owner];
        public readonly Dictionary<string, string> Attributes = new(StringComparer.Ordinal);
        public readonly Dictionary<string, Dictionary<string, string>> MemberAttributes = new(StringComparer.Ordinal) { [owner] = new(StringComparer.Ordinal) };
        public int Max = max;
        public bool Open = true;

        public EosLobby Snapshot() => new(Id, Owner, [.. Members], Max, Open, new Dictionary<string, string>(Attributes, StringComparer.Ordinal),
            MemberAttributes.ToDictionary(m => m.Key, m => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(m.Value, StringComparer.Ordinal),
                StringComparer.Ordinal));
    }

    static bool CanMeetDirectly(EosNat a, EosNat b) =>
        !(a == EosNat.Strict && b is EosNat.Strict or EosNat.Moderate || b == EosNat.Strict && a == EosNat.Moderate);

    void Notify(Room room, string user, EosMemberStatus status)
    {
        foreach (var m in room.Members)
            if (m != user && _users.TryGetValue(m, out var d))
                d.Later(() => d.RaiseMember(room.Id, user, status));
    }

    sealed class Device(FakeEos cloud, EosNat nat) : IEosSdk
    {
        // Callbacks and notifications due on the next Tick, in the order they were made.
        readonly Queue<Action> _due = new();
        readonly Queue<(string From, byte[] Data)> _inbox = new();
        readonly HashSet<string> _accepted = new(StringComparer.Ordinal), _asked = new(StringComparer.Ordinal);
        // Packets from peers not yet accepted, by sender: EOS holds a little of them, and lets them through on accepting.
        readonly Dictionary<string, Queue<byte[]>> _held = new(StringComparer.Ordinal);
        const int HoldAtMost = 64;
        readonly Dictionary<string, EosConnection> _connections = new(StringComparer.Ordinal);
        EosRelay _relay;
        string? _me;
        bool _gone;

        public EosNat Nat => _me is null ? EosNat.Unknown : nat;
        public event Action<string>? ConnectionRequested;
        public event Action<string, string>? InviteReceived;
        public event Action<string, string, EosMemberStatus>? MemberStatus;

        public void Later(Action a)
        {
            lock (cloud)
                _due.Enqueue(a);
        }

        public void RaiseMember(string lobby, string user, EosMemberStatus status) => MemberStatus?.Invoke(lobby, user, status);

        public void Tick()
        {
            // What's due now; anything these queue waits for the next tick, as EOS's callbacks do.
            List<Action> now;
            lock (cloud)
            {
                now = [.. _due];
                _due.Clear();
            }
            foreach (var a in now)
                lock (cloud)
                    a();
        }

        public void Login(string displayName, Action<EosResult, string?> done) => Later(() =>
        {
            if (!cloud.Up || !cloud.CredentialsValid)
            {
                done(!cloud.Up ? EosResult.NoConnection : EosResult.InvalidCredentials, null);
                return;
            }
            // A product user id is 32 hex digits.
            _me ??= FormattableString.Invariant($"0002{cloud._nextUser++:x28}");
            cloud._users[_me] = this;
            done(EosResult.Success, _me);
        });

        public void SetRelayControl(EosRelay relay) => _relay = relay;

        public void SendPacket(string localUser, string to, ReadOnlySpan<byte> data)
        {
            lock (cloud)
                Carry(to, data);
        }

        void Carry(string to, ReadOnlySpan<byte> data)
        {
            if (_me is null || _gone)
                return;
            _accepted.Add(to);
            if (!cloud._users.TryGetValue(to, out var them) || them._gone)
                return;
            if (!them._accepted.Contains(_me))
            {
                cloud.Refused++;
                if (!them._held.TryGetValue(_me, out var held))
                    them._held[_me] = held = new Queue<byte[]>();
                if (held.Count < HoldAtMost)
                    held.Enqueue(data.ToArray());
                if (them._asked.Add(_me))
                {
                    string from = _me;
                    them._due.Enqueue(() => them.ConnectionRequested?.Invoke(from));
                }
                return;
            }
            bool forced = _relay == EosRelay.ForceRelays || them._relay == EosRelay.ForceRelays;
            bool relayed = forced || !CanMeetDirectly(nat, them.Nat);
            if (relayed && (_relay == EosRelay.NoRelays || them._relay == EosRelay.NoRelays))
            {
                cloud.Unreachable++;
                return;
            }
            var link = new EosConnection(relayed);
            _connections[to] = link;
            them._connections[_me] = link;
            if (relayed)
                cloud.Relayed++;
            else
                cloud.Direct++;
            them._inbox.Enqueue((_me, data.ToArray()));
        }

        public bool ReceivePacket(string localUser, Span<byte> buffer, out int length, out string from)
        {
            (string From, byte[] Data) p;
            bool got;
            lock (cloud)
                got = _inbox.TryDequeue(out p);
            if (!got)
            {
                length = 0;
                from = "";
                return false;
            }
            length = Math.Min(p.Data.Length, buffer.Length);
            p.Data.AsSpan(0, length).CopyTo(buffer);
            from = p.From;
            return true;
        }

        public void AcceptConnection(string localUser, string from)
        {
            lock (cloud)
            {
                _accepted.Add(from);
                _asked.Remove(from);
                if (_held.Remove(from, out var held) && cloud._users.TryGetValue(from, out var them) && _me is not null)
                    foreach (var data in held)
                        them.Carry(_me, data);
            }
        }

        public void CloseConnection(string localUser, string peer)
        {
            lock (cloud)
            {
                _accepted.Remove(peer);
                _connections.Remove(peer);
            }
        }

        public EosConnection? ConnectionTo(string peer)
        {
            lock (cloud)
                return _connections.GetValueOrDefault(peer);
        }

        public void CreateLobby(string localUser, int maxMembers, Action<EosResult, string?> done) => Later(() =>
        {
            if (!cloud.Up)
            {
                done(EosResult.NoConnection, null);
                return;
            }
            var room = new Room(FormattableString.Invariant($"lobby{cloud._nextLobby++:x8}"), localUser, maxMembers);
            cloud._lobbies[room.Id] = room;
            done(EosResult.Success, room.Id);
        });

        public void JoinLobby(string localUser, string lobby, Action<EosResult> done) => Later(() =>
        {
            cloud._lobbies.TryGetValue(lobby, out var room);
            var why = !cloud.Up ? EosResult.NoConnection
                : room is null ? EosResult.NotFound
                : room.Members.Contains(localUser) ? EosResult.Success
                : !room.Open ? EosResult.NotAllowed
                : room.Members.Count >= room.Max ? EosResult.LobbyFull
                : EosResult.Success;
            if (why == EosResult.Success && !room!.Members.Contains(localUser))
            {
                room.Members.Add(localUser);
                room.MemberAttributes[localUser] = new(StringComparer.Ordinal);
                cloud.Notify(room, localUser, EosMemberStatus.Joined);
            }
            done(why);
        });

        public void LeaveLobby(string localUser, string lobby, Action<EosResult> done) => Later(() =>
        {
            Leave(localUser, lobby);
            done(EosResult.Success);
        });

        void Leave(string user, string lobby)
        {
            if (!cloud._lobbies.TryGetValue(lobby, out var room) || !room.Members.Remove(user))
                return;
            room.MemberAttributes.Remove(user);
            if (room.Members.Count == 0)
            {
                cloud._lobbies.Remove(lobby);
                return;
            }
            cloud.Notify(room, user, EosMemberStatus.Left);
            if (room.Owner == user)
            {
                room.Owner = room.Members[0];
                cloud.Notify(room, user, EosMemberStatus.Promoted);
            }
        }

        public void UpdateLobby(string localUser, string lobby, EosLobbyChange change, Action<EosResult> done)
        {
            lock (cloud)
                cloud.Updates++;
            Later(() =>
            {
                if (!cloud._lobbies.TryGetValue(lobby, out var room) || !room.Members.Contains(localUser))
                {
                    done(EosResult.NotFound);
                    return;
                }
                bool owner = room.Owner == localUser;
                if (!owner && (change.Attributes.Count > 0 || change.Open is not null || change.MaxMembers is not null))
                {
                    done(EosResult.NotAllowed);
                    return;
                }
                foreach (var (k, v) in change.Attributes)
                    room.Attributes[k] = v;
                foreach (var (k, v) in change.MemberAttributes)
                    room.MemberAttributes[localUser][k] = v;
                if (change.Open is { } open)
                    room.Open = open;
                if (change.MaxMembers is { } max)
                    room.Max = max;
                done(EosResult.Success);
            });
        }

        public EosLobby? Lobby(string localUser, string lobby)
        {
            lock (cloud)
                return cloud._lobbies.TryGetValue(lobby, out var room) && room.Members.Contains(localUser) ? room.Snapshot() : null;
        }

        public void SearchLobbies(string localUser, EosSearch search, Action<EosResult, IReadOnlyList<EosLobby>> done) => Later(() =>
        {
            if (!cloud.Up)
            {
                done(EosResult.NoConnection, []);
                return;
            }
            var found = search.LobbyId is { } id
                ? cloud._lobbies.TryGetValue(id, out var one) ? [one.Snapshot()] : []
                : cloud._lobbies.Values
                    .Where(r => r.Open && search.Equal.All(e => r.Attributes.GetValueOrDefault(e.Key) == e.Value))
                    .OrderBy(r => r.Id, StringComparer.Ordinal)
                    .Take(search.MaxResults)
                    .Select(r => r.Snapshot())
                    .ToList();
            done(EosResult.Success, found);
        });

        public void SendInvite(string localUser, string lobby, string to, Action<EosResult> done) => Later(() =>
        {
            if (cloud._users.TryGetValue(to, out var them) && !them._gone)
                them._due.Enqueue(() => them.InviteReceived?.Invoke(lobby, localUser));
            done(cloud._users.ContainsKey(to) ? EosResult.Success : EosResult.NotFound);
        });

        public void Dispose()
        {
            lock (cloud)
            {
                if (_gone || _me is null)
                {
                    _gone = true;
                    return;
                }
                // Signing out of EOS leaves its lobbies at once, as a closed game drops from them on Epic's side.
                foreach (var id in cloud._lobbies.Where(l => l.Value.Members.Contains(_me)).Select(l => l.Key).ToList())
                    Leave(_me, id);
                _gone = true;
                _inbox.Clear();
            }
        }
    }
}
