using System.Diagnostics.CodeAnalysis;
using Ballast.Net;

namespace Ballast.Online;

/// <summary>
/// An in-process stand-in for Steam's online services: accounts, lobbies, invites and relayed datagrams, for tests
/// and the harness (there's no Steam client in CI). It keeps the rules the game depends on:
/// <list type="bullet">
/// <item>Lobby results arrive on a later <see cref="IOnlineBackend.Poll"/>, never inside the call.</item>
/// <item>Lobbies have a member limit and can be closed; the owner leaving hands the lobby on.</item>
/// <item>Datagrams reach you only from users in a lobby with you, or users you've sent to first.</item>
/// <item>A lobby search finds only public, joinable lobbies that match its filter, as Steam's does.</item>
/// <item>Each account is somewhere (a point on a plane, in milliseconds), and the ping estimate between two is the distance.</item>
/// </list>
/// </summary>
public sealed class FakeOnline
{
    readonly Dictionary<UserId, User> _users = new();
    readonly Dictionary<LobbyId, Room> _lobbies = new();
    ulong _nextUser = 76561197960265729, _nextLobby = 109775240000000001;

    /// <summary>Datagrams dropped because the receiver hadn't accepted the sender.</summary>
    public int Refused { get; private set; }
    public int Delivered { get; private set; }
    public int Lobbies => _lobbies.Count;

    /// <param name="where">Where the account's machine is, for ping estimates: a point in milliseconds from anywhere.</param>
    public IOnlineBackend SignIn(string name, (double X, double Y) where = default)
    {
        var user = new User(this, new UserId(_nextUser++), name, where);
        _users[user.Me] = user;
        return user;
    }

    sealed class Room(UserId owner, int max, LobbyVisibility visibility)
    {
        public readonly LobbyVisibility Visibility = visibility;
        public UserId Owner = owner;
        public readonly List<UserId> Members = [owner];
        public readonly Dictionary<string, string> Data = new();
        public readonly int Max = max;
        public bool Joinable = true;
    }

    void Notify(Room room, LobbyId id, UserId changed)
    {
        foreach (var m in room.Members)
            if (m != changed && _users.TryGetValue(m, out var u))
                u.Pending.Enqueue(new OnlineEvent(OnlineEventKind.MembersChanged, id, changed));
    }

    sealed class User(FakeOnline cloud, UserId me, string name, (double X, double Y) where) : IOnlineBackend, IDatagramCarrier<UserId>
    {
        public readonly Queue<OnlineEvent> Pending = new();
        readonly Queue<(UserId From, byte[] Datagram)> _inbox = new();
        readonly HashSet<UserId> _sentTo = new();
        bool _signedOut;

        public string Platform => "Fake";
        public UserId Me => me;
        public string NameOf(UserId user) => cloud._users.TryGetValue(user, out var u) ? u.Name : "someone";
        public string Name => name;
        public IDatagramCarrier<UserId> Carrier => this;

        public void Poll(List<OnlineEvent> into)
        {
            while (Pending.TryDequeue(out var e))
                into.Add(e);
        }

        public void CreateLobby(int maxMembers, LobbyVisibility visibility)
        {
            var id = new LobbyId(cloud._nextLobby++);
            cloud._lobbies[id] = new Room(me, maxMembers, visibility);
            Pending.Enqueue(new OnlineEvent(OnlineEventKind.LobbyCreated, id));
        }

        public void JoinLobby(LobbyId lobby)
        {
            string? why = !cloud._lobbies.TryGetValue(lobby, out var room) ? "that lobby is gone"
                : !room.Joinable ? "the lobby is closed"
                : room.Members.Count >= room.Max ? "the lobby is full"
                : null;
            if (why is not null)
            {
                Pending.Enqueue(new OnlineEvent(OnlineEventKind.LobbyFailed, lobby, Error: why));
                return;
            }
            if (!room!.Members.Contains(me))
            {
                room.Members.Add(me);
                cloud.Notify(room, lobby, me);
            }
            Pending.Enqueue(new OnlineEvent(OnlineEventKind.LobbyEntered, lobby));
        }

        public void LeaveLobby(LobbyId lobby)
        {
            if (!cloud._lobbies.TryGetValue(lobby, out var room) || !room.Members.Remove(me))
                return;
            if (room.Members.Count == 0)
            {
                cloud._lobbies.Remove(lobby);
                return;
            }
            if (room.Owner == me)
                room.Owner = room.Members[0];
            cloud.Notify(room, lobby, me);
        }

        public UserId OwnerOf(LobbyId lobby) => cloud._lobbies.TryGetValue(lobby, out var r) ? r.Owner : default;
        public IReadOnlyList<UserId> MembersOf(LobbyId lobby) => cloud._lobbies.TryGetValue(lobby, out var r) ? [.. r.Members] : [];
        public string LobbyData(LobbyId lobby, string key) =>
            cloud._lobbies.TryGetValue(lobby, out var r) ? r.Data.GetValueOrDefault(key, "") : "";

        public void SetLobbyData(LobbyId lobby, string key, string value)
        {
            // As on Steam, only the owner can.
            if (cloud._lobbies.TryGetValue(lobby, out var r) && r.Owner == me)
                r.Data[key] = value;
        }

        public void SetJoinable(LobbyId lobby, bool joinable)
        {
            if (cloud._lobbies.TryGetValue(lobby, out var r) && r.Owner == me)
                r.Joinable = joinable;
        }

        public void RequestLobbyList(LobbyFilter filter)
        {
            string? protocol = filter.Protocol?.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var found = cloud._lobbies
                .Where(l => l.Value.Joinable && (!filter.PublicOnly || l.Value.Visibility == LobbyVisibility.Public)
                    && l.Value.Data.GetValueOrDefault(Lobby.GameKey) == filter.Game
                    && (protocol is null || l.Value.Data.GetValueOrDefault(Lobby.ProtocolKey) == protocol))
                .OrderBy(l => l.Key.Value)
                .Select(l => new LobbyListing(l.Key, l.Value.Members.Count, l.Value.Max, new Dictionary<string, string>(l.Value.Data),
                    EstimatePingMs(l.Value.Data.GetValueOrDefault(Lobby.PingKey, ""))))
                .ToList();
            Pending.Enqueue(new OnlineEvent(OnlineEventKind.LobbyList, default, Listings: found));
        }

        public string LocalPingLocation => Location(where);

        public int? EstimatePingMs(string location)
        {
            var parts = location.StartsWith("fake:", StringComparison.Ordinal) ? location[5..].Split(',') : [];
            if (parts.Length != 2 || !double.TryParse(parts[0], System.Globalization.CultureInfo.InvariantCulture, out double x)
                || !double.TryParse(parts[1], System.Globalization.CultureInfo.InvariantCulture, out double y))
                return null;
            return (int)Math.Round(Math.Sqrt((x - where.X) * (x - where.X) + (y - where.Y) * (y - where.Y)));
        }

        static string Location((double X, double Y) p) => FormattableString.Invariant($"fake:{p.X},{p.Y}");

        public int InviteDialogsShown { get; private set; }
        public void ShowInviteDialog(LobbyId lobby) => InviteDialogsShown++;

        /// <summary>The fake's friend accepts at once: they see what Steam shows after they click the invite.</summary>
        public void Invite(LobbyId lobby, UserId user)
        {
            if (cloud._users.TryGetValue(user, out var u) && !u._signedOut)
                u.Pending.Enqueue(new OnlineEvent(OnlineEventKind.JoinRequested, lobby, me));
        }

        bool Accepts(UserId from) =>
            _sentTo.Contains(from) || cloud._lobbies.Values.Any(r => r.Members.Contains(me) && r.Members.Contains(from));

        public void Send(UserId to, ReadOnlySpan<byte> datagram)
        {
            _sentTo.Add(to);
            if (!cloud._users.TryGetValue(to, out var u) || u._signedOut)
                return;
            if (!u.Accepts(me))
            {
                cloud.Refused++;
                return;
            }
            cloud.Delivered++;
            u._inbox.Enqueue((me, datagram.ToArray()));
        }

        public bool TryReceive(Span<byte> buffer, out int length, [MaybeNullWhen(false)] out UserId from)
        {
            if (!_inbox.TryDequeue(out var d))
            {
                length = 0;
                from = default;
                return false;
            }
            length = Math.Min(d.Datagram.Length, buffer.Length);
            d.Datagram.AsSpan(0, length).CopyTo(buffer);
            from = d.From;
            return true;
        }

        public void Dispose()
        {
            if (_signedOut)
                return;
            foreach (var id in cloud._lobbies.Where(l => l.Value.Members.Contains(me)).Select(l => l.Key).ToList())
                LeaveLobby(id);
            _signedOut = true;
            _inbox.Clear();
        }
    }
}
