using System.Diagnostics.CodeAnalysis;
using Ballast.Net;
using Steamworks;

namespace Ballast.Online.Steam;

/// <summary>
/// Steam's online services through Steamworks.NET: friends-only lobbies, overlay invites, and Steam Datagram Relay
/// P2P (ISteamNetworkingMessages), so nobody opens a port or sees anyone's IP address.
/// Needs Valve's native library next to the game (external/steam/README.md) and a running Steam client;
/// <see cref="TryStart"/> says plainly when either is missing and the game carries on without Steam.
/// This class is a thin mapping onto the Steam API. The rules the game relies on are the ones
/// <see cref="FakeOnline"/> reproduces, and the tests run against that.
/// </summary>
public sealed class SteamBackend : IOnlineBackend
{
    /// <summary>Valve's public test app ("Spacewar"), until Dark Territory has an app id of its own.</summary>
    public const uint DevAppId = 480;

    readonly List<OnlineEvent> _events = new();
    readonly HashSet<LobbyId> _in = new();
    readonly Dictionary<UserId, SteamNetworkingIdentity> _asking = new();
    readonly Callback<GameLobbyJoinRequested_t> _joinRequested;
    readonly Callback<LobbyChatUpdate_t> _membersChanged;
    readonly Callback<SteamNetworkingMessagesSessionRequest_t> _sessionRequest;
    readonly CallResult<LobbyCreated_t> _created;
    readonly CallResult<LobbyEnter_t> _entered;
    readonly SteamCarrier _carrier = new();
    bool _disposed;

    SteamBackend()
    {
        Me = new UserId(SteamUser.GetSteamID().m_SteamID);
        _joinRequested = Callback<GameLobbyJoinRequested_t>.Create(e =>
            _events.Add(new OnlineEvent(OnlineEventKind.JoinRequested, new LobbyId(e.m_steamIDLobby.m_SteamID), new UserId(e.m_steamIDFriend.m_SteamID))));
        _membersChanged = Callback<LobbyChatUpdate_t>.Create(e =>
            _events.Add(new OnlineEvent(OnlineEventKind.MembersChanged, new LobbyId(e.m_ulSteamIDLobby), new UserId(e.m_ulSteamIDUserChanged))));
        _sessionRequest = Callback<SteamNetworkingMessagesSessionRequest_t>.Create(e => _asking[new UserId(e.m_identityRemote.GetSteamID64())] = e.m_identityRemote);
        _created = CallResult<LobbyCreated_t>.Create(OnCreated);
        _entered = CallResult<LobbyEnter_t>.Create(OnEntered);
    }

    /// <summary>Starts Steam, or explains why it can't.</summary>
    /// <param name="appId">Used when the game wasn't launched by Steam and there's no steam_appid.txt.</param>
    public static SteamBackend? TryStart(uint appId, out string? error)
    {
        try
        {
            if (Environment.GetEnvironmentVariable("SteamAppId") is null && !File.Exists("steam_appid.txt"))
                Environment.SetEnvironmentVariable("SteamAppId", appId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var result = SteamAPI.InitEx(out string message);
            if (result != ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
            {
                error = result == ESteamAPIInitResult.k_ESteamAPIInitResult_NoSteamClient ? "Steam isn't running" : $"Steam didn't start: {message}";
                return null;
            }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            error = $"Steam's library ({(OperatingSystem.IsWindows() ? "steam_api64.dll" : "libsteam_api.so")}) isn't next to the game; see external/steam/README.md";
            return null;
        }
        // Fetch the relay network config now, so the first connection doesn't wait for it.
        SteamNetworkingUtils.InitRelayNetworkAccess();
        error = null;
        return new SteamBackend();
    }

    public string Platform => "Steam";
    public UserId Me { get; }
    public IDatagramCarrier<UserId> Carrier => _carrier;

    public string NameOf(UserId user) => user == Me ? SteamFriends.GetPersonaName() : SteamFriends.GetFriendPersonaName(Steam(user));

    public void Poll(List<OnlineEvent> into)
    {
        SteamAPI.RunCallbacks();
        // Only lobby-mates get a P2P session. Someone can ask a moment before their arrival reaches us,
        // so a refused request is held and accepted once they're in.
        foreach (var (user, identity) in _asking.ToList())
            if (_in.Any(l => MembersOf(l).Contains(user)))
            {
                var id = identity;
                SteamNetworkingMessages.AcceptSessionWithUser(ref id);
                _asking.Remove(user);
            }
        into.AddRange(_events);
        _events.Clear();
    }

    public void CreateLobby(int maxMembers) => _created.Set(SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, maxMembers));
    public void JoinLobby(LobbyId lobby) => _entered.Set(SteamMatchmaking.JoinLobby(Steam(lobby)));

    public void LeaveLobby(LobbyId lobby)
    {
        SteamMatchmaking.LeaveLobby(Steam(lobby));
        _in.Remove(lobby);
    }

    public UserId OwnerOf(LobbyId lobby) => new(SteamMatchmaking.GetLobbyOwner(Steam(lobby)).m_SteamID);

    public IReadOnlyList<UserId> MembersOf(LobbyId lobby)
    {
        var id = Steam(lobby);
        int n = SteamMatchmaking.GetNumLobbyMembers(id);
        var members = new List<UserId>(n);
        for (int i = 0; i < n; i++)
            members.Add(new UserId(SteamMatchmaking.GetLobbyMemberByIndex(id, i).m_SteamID));
        return members;
    }

    public string LobbyData(LobbyId lobby, string key) => SteamMatchmaking.GetLobbyData(Steam(lobby), key) ?? "";
    public void SetLobbyData(LobbyId lobby, string key, string value) => SteamMatchmaking.SetLobbyData(Steam(lobby), key, value);
    public void SetJoinable(LobbyId lobby, bool joinable) => SteamMatchmaking.SetLobbyJoinable(Steam(lobby), joinable);
    public void ShowInviteDialog(LobbyId lobby) => SteamFriends.ActivateGameOverlayInviteDialog(Steam(lobby));
    public void Invite(LobbyId lobby, UserId user) => SteamMatchmaking.InviteUserToLobby(Steam(lobby), Steam(user));

    void OnCreated(LobbyCreated_t e, bool ioFailure)
    {
        if (ioFailure || e.m_eResult != EResult.k_EResultOK)
        {
            _events.Add(new OnlineEvent(OnlineEventKind.LobbyFailed, default, Error: $"Steam couldn't make a lobby ({(ioFailure ? "no connection" : e.m_eResult)})"));
            return;
        }
        var id = new LobbyId(e.m_ulSteamIDLobby);
        _in.Add(id);
        _events.Add(new OnlineEvent(OnlineEventKind.LobbyCreated, id));
    }

    void OnEntered(LobbyEnter_t e, bool ioFailure)
    {
        var id = new LobbyId(e.m_ulSteamIDLobby);
        var response = (EChatRoomEnterResponse)e.m_EChatRoomEnterResponse;
        if (ioFailure || response != EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
        {
            string why = ioFailure ? "no connection to Steam" : response switch
            {
                EChatRoomEnterResponse.k_EChatRoomEnterResponseDoesntExist => "that lobby is gone",
                EChatRoomEnterResponse.k_EChatRoomEnterResponseFull => "the lobby is full",
                EChatRoomEnterResponse.k_EChatRoomEnterResponseNotAllowed => "the lobby is closed",
                _ => $"Steam refused ({response})",
            };
            _events.Add(new OnlineEvent(OnlineEventKind.LobbyFailed, id, Error: why));
            return;
        }
        _in.Add(id);
        _events.Add(new OnlineEvent(OnlineEventKind.LobbyEntered, id));
    }

    static CSteamID Steam(UserId user) => new(user.Value);
    static CSteamID Steam(LobbyId lobby) => new(lobby.Value);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (var lobby in _in.ToList())
            LeaveLobby(lobby);
        _joinRequested.Dispose();
        _membersChanged.Dispose();
        _sessionRequest.Dispose();
        _created.Dispose();
        _entered.Dispose();
        SteamAPI.Shutdown();
    }

    /// <summary>ISteamNetworkingMessages on channel 0: connectionless, relayed, addressed by SteamID.</summary>
    sealed unsafe class SteamCarrier : IDatagramCarrier<UserId>
    {
        // Unreliable (the protocol above does its own reliability), no Nagle delay, and quietly reopen a session
        // that broke, as a lost packet would be.
        const int Flags = Constants.k_nSteamNetworkingSend_UnreliableNoNagle | Constants.k_nSteamNetworkingSend_AutoRestartBrokenSession;
        readonly IntPtr[] _batch = new IntPtr[64];
        int _count, _next;

        public void Send(UserId to, ReadOnlySpan<byte> datagram)
        {
            var id = Identity(to);
            fixed (byte* p = datagram)
                SteamNetworkingMessages.SendMessageToUser(ref id, (IntPtr)p, (uint)datagram.Length, Flags, 0);
        }

        public bool TryReceive(Span<byte> buffer, out int length, [MaybeNullWhen(false)] out UserId from)
        {
            if (_next >= _count)
            {
                _next = 0;
                _count = Math.Max(0, SteamNetworkingMessages.ReceiveMessagesOnChannel(0, _batch, _batch.Length));
                if (_count == 0)
                {
                    length = 0;
                    from = default;
                    return false;
                }
            }
            var ptr = _batch[_next++];
            var message = SteamNetworkingMessage_t.FromIntPtr(ptr);
            length = Math.Min(message.m_cbSize, buffer.Length);
            new ReadOnlySpan<byte>((void*)message.m_pData, length).CopyTo(buffer);
            from = new UserId(message.m_identityPeer.GetSteamID64());
            SteamNetworkingMessage_t.Release(ptr);
            return true;
        }

        public void Forget(UserId peer)
        {
            var id = Identity(peer);
            SteamNetworkingMessages.CloseSessionWithUser(ref id);
        }

        static SteamNetworkingIdentity Identity(UserId user)
        {
            var id = new SteamNetworkingIdentity();
            id.SetSteamID64(user.Value);
            return id;
        }

        public void Dispose()
        {
        }
    }
}
