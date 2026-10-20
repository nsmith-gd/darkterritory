#if EOS_SDK
using Epic.OnlineServices;
using Epic.OnlineServices.Connect;
using Epic.OnlineServices.Lobby;
using Epic.OnlineServices.P2P;
using Epic.OnlineServices.Platform;

namespace Ballast.Online.Eos;

/// <summary>
/// <see cref="IEosSdk"/> over Epic's own C# wrapper (the EOS SDK's <c>SDK/Source</c>, compiled in from <c>external/eos/</c>
/// with <c>EOS_SDK</c> defined; external/eos/README.md). A thin mapping, as <see cref="Steam.SteamBackend"/> is onto Steam:
/// the rules the game relies on are <see cref="FakeEos"/>'s, and the tests run against that.
/// <para>
/// Note 896: written against the SDK's documented C# API without the SDK to compile it in this repository's CI; the first
/// build with <c>external/eos/</c> filled is where it's first compiled, and the first real evening (note 896's handoff)
/// where it's first run.
/// </para>
/// </summary>
sealed class EpicSdk : IEosSdk
{
    const string Socket = "dt";
    readonly PlatformInterface _platform;
    readonly ConnectInterface _connect;
    readonly P2PInterface _p2p;
    readonly LobbyInterface _lobby;
    readonly Dictionary<string, EosConnection> _connections = new(StringComparer.Ordinal);
    ProductUserId? _me;
    EosNat _nat;
    ulong _requests, _established, _closed, _invites, _members;
    bool _disposed;

    EpicSdk(PlatformInterface platform)
    {
        _platform = platform;
        _connect = platform.GetConnectInterface();
        _p2p = platform.GetP2PInterface();
        _lobby = platform.GetLobbyInterface();
    }

    public static IEosSdk? TryCreate(EosSettings s, out string? error)
    {
        try
        {
            var init = new InitializeOptions { ProductName = "Dark Territory", ProductVersion = "1" };
            var r = PlatformInterface.Initialize(ref init);
            if (r != Result.Success && r.ToString() != "AlreadyConfigured")
            {
                error = $"EOS didn't start ({r})";
                return null;
            }
            var options = new Options
            {
                ProductId = s.ProductId,
                SandboxId = s.SandboxId,
                DeploymentId = s.DeploymentId,
                ClientCredentials = new ClientCredentials { ClientId = s.ClientId, ClientSecret = s.ClientSecret },
                // No Epic accounts, so nothing for the overlay to show.
                Flags = PlatformFlags.DisableOverlay,
                CacheDirectory = Path.Combine(Path.GetTempPath(), "darkterritory-eos"),
            };
            Directory.CreateDirectory(options.CacheDirectory);
            var platform = PlatformInterface.Create(ref options);
            if (platform is null)
            {
                error = "EOS didn't start (its platform wasn't made: check the credentials' ids)";
                return null;
            }
            error = null;
            return new EpicSdk(platform);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            error = $"Epic's library ({(OperatingSystem.IsWindows() ? "EOSSDK-Win64-Shipping.dll" : "libEOSSDK-Linux-Shipping.so")}) isn't next to the game; see external/eos/README.md";
            return null;
        }
    }

    public event Action<string>? ConnectionRequested;
    public event Action<string, string>? InviteReceived;
    public event Action<string, string, EosMemberStatus>? MemberStatus;

    public EosNat Nat => _nat;

    public void Tick() => _platform.Tick();

    static EosResult Map(Result r) => r.ToString() switch
    {
        "Success" => EosResult.Success,
        "NotFound" or "LobbyLobbyNotFound" => EosResult.NotFound,
        "LobbyTooManyPlayers" => EosResult.LobbyFull,
        "LobbyNotAllowed" or "LobbyInviteOnly" or "LobbyNoPermission" => EosResult.NotAllowed,
        "NoConnection" or "TimedOut" => EosResult.NoConnection,
        "InvalidCredentials" or "InvalidAuth" or "InvalidProductUserID" => EosResult.InvalidCredentials,
        _ => EosResult.Other,
    };

    static ProductUserId User(string puid) => ProductUserId.FromString(puid);

    public void Login(string displayName, Action<EosResult, string?> done)
    {
        // A device id first (one per machine, kept by EOS); "already there" is as good as made.
        var create = new CreateDeviceIdOptions { DeviceModel = "PC" };
        _connect.CreateDeviceId(ref create, null, (ref CreateDeviceIdCallbackInfo made) =>
        {
            if (made.ResultCode != Result.Success && made.ResultCode.ToString() != "DuplicateNotAllowed")
            {
                done(Map(made.ResultCode), null);
                return;
            }
            var login = new Epic.OnlineServices.Connect.LoginOptions
            {
                Credentials = new Epic.OnlineServices.Connect.Credentials { Type = ExternalCredentialType.DeviceidAccessToken, Token = null },
                UserLoginInfo = new UserLoginInfo { DisplayName = displayName.Length > 32 ? displayName[..32] : displayName },
            };
            _connect.Login(ref login, null, (ref Epic.OnlineServices.Connect.LoginCallbackInfo info) =>
            {
                if (info.ResultCode == Result.Success)
                {
                    SignedIn(info.LocalUserId);
                    done(EosResult.Success, _me!.ToString());
                    return;
                }
                if (info.ResultCode.ToString() != "InvalidUser")
                {
                    done(Map(info.ResultCode), null);
                    return;
                }
                // A first sign-in on this machine: the product user is made for it.
                var user = new CreateUserOptions { ContinuanceToken = info.ContinuanceToken };
                _connect.CreateUser(ref user, null, (ref CreateUserCallbackInfo created) =>
                {
                    if (created.ResultCode != Result.Success)
                    {
                        done(Map(created.ResultCode), null);
                        return;
                    }
                    SignedIn(created.LocalUserId);
                    done(EosResult.Success, _me!.ToString());
                });
            });
        });
    }

    void SignedIn(ProductUserId me)
    {
        _me = me;
        var socket = new SocketId { SocketName = Socket };
        var request = new AddNotifyPeerConnectionRequestOptions { LocalUserId = me, SocketId = socket };
        _requests = _p2p.AddNotifyPeerConnectionRequest(ref request, null, (ref OnIncomingConnectionRequestInfo d) =>
            ConnectionRequested?.Invoke(d.RemoteUserId.ToString()));
        var established = new AddNotifyPeerConnectionEstablishedOptions { LocalUserId = me, SocketId = socket };
        _established = _p2p.AddNotifyPeerConnectionEstablished(ref established, null, (ref OnPeerConnectionEstablishedInfo d) =>
            _connections[d.RemoteUserId.ToString()] = new EosConnection(Relayed: d.NetworkType == NetworkConnectionType.RelayedConnection));
        var closed = new AddNotifyPeerConnectionClosedOptions { LocalUserId = me, SocketId = socket };
        _closed = _p2p.AddNotifyPeerConnectionClosed(ref closed, null, (ref OnRemoteConnectionClosedInfo d) => _connections.Remove(d.RemoteUserId.ToString()));
        var invites = new AddNotifyLobbyInviteReceivedOptions();
        _invites = _lobby.AddNotifyLobbyInviteReceived(ref invites, null, (ref LobbyInviteReceivedCallbackInfo d) =>
        {
            var copy = new CopyLobbyDetailsHandleByInviteIdOptions { InviteId = d.InviteId };
            if (_lobby.CopyLobbyDetailsHandleByInviteId(ref copy, out var details) != Result.Success || details is null)
                return;
            var lobby = Describe(details);
            details.Release();
            if (lobby is not null)
                InviteReceived?.Invoke(lobby.Id, d.TargetUserId.ToString());
        });
        var members = new AddNotifyLobbyMemberStatusReceivedOptions();
        _members = _lobby.AddNotifyLobbyMemberStatusReceived(ref members, null, (ref LobbyMemberStatusReceivedCallbackInfo d) =>
            MemberStatus?.Invoke(d.LobbyId.ToString(), d.TargetUserId.ToString(), d.CurrentStatus.ToString() switch
            {
                "Joined" => EosMemberStatus.Joined,
                "Left" => EosMemberStatus.Left,
                "Disconnected" => EosMemberStatus.Disconnected,
                "Kicked" => EosMemberStatus.Kicked,
                "Promoted" => EosMemberStatus.Promoted,
                _ => EosMemberStatus.Closed,
            }));
        var nat = new QueryNATTypeOptions();
        _p2p.QueryNATType(ref nat, null, (ref OnQueryNATTypeCompleteInfo d) => _nat = d.NATType.ToString() switch
        {
            "Open" => EosNat.Open,
            "Moderate" => EosNat.Moderate,
            "Strict" => EosNat.Strict,
            _ => EosNat.Unknown,
        });
    }

    public void SetRelayControl(EosRelay relay)
    {
        var options = new SetRelayControlOptions
        {
            RelayControl = relay switch
            {
                EosRelay.ForceRelays => RelayControl.ForceRelays,
                EosRelay.NoRelays => RelayControl.NoRelays,
                _ => RelayControl.AllowRelays,
            },
        };
        _p2p.SetRelayControl(ref options);
    }

    public void SendPacket(string localUser, string to, ReadOnlySpan<byte> data)
    {
        var options = new SendPacketOptions
        {
            LocalUserId = User(localUser),
            RemoteUserId = User(to),
            SocketId = new SocketId { SocketName = Socket },
            Channel = 0,
            Data = new ArraySegment<byte>(data.ToArray()),
            // Held until the peer accepts, so a joiner's first packets aren't lost to the handshake.
            AllowDelayedDelivery = true,
            // The game's protocol does its own reliability, as over UDP and Steam.
            Reliability = PacketReliability.UnreliableUnordered,
        };
        _p2p.SendPacket(ref options);
    }

    public bool ReceivePacket(string localUser, Span<byte> buffer, out int length, out string from)
    {
        var options = new ReceivePacketOptions { LocalUserId = User(localUser), MaxDataSizeBytes = (uint)buffer.Length, RequestedChannel = 0 };
        ProductUserId peer = null!;
        var socket = default(SocketId);
        var data = new byte[buffer.Length];
        var r = _p2p.ReceivePacket(ref options, ref peer, ref socket, out _, new ArraySegment<byte>(data), out uint written);
        if (r != Result.Success || peer is null)
        {
            length = 0;
            from = "";
            return false;
        }
        length = (int)Math.Min(written, (uint)buffer.Length);
        data.AsSpan(0, length).CopyTo(buffer);
        from = peer.ToString();
        return true;
    }

    public void AcceptConnection(string localUser, string from)
    {
        var options = new AcceptConnectionOptions { LocalUserId = User(localUser), RemoteUserId = User(from), SocketId = new SocketId { SocketName = Socket } };
        _p2p.AcceptConnection(ref options);
    }

    public void CloseConnection(string localUser, string peer)
    {
        var options = new CloseConnectionOptions { LocalUserId = User(localUser), RemoteUserId = User(peer), SocketId = new SocketId { SocketName = Socket } };
        _p2p.CloseConnection(ref options);
        _connections.Remove(peer);
    }

    public EosConnection? ConnectionTo(string peer) => _connections.GetValueOrDefault(peer);

    public void CreateLobby(string localUser, int maxMembers, Action<EosResult, string?> done)
    {
        var options = new CreateLobbyOptions
        {
            LocalUserId = User(localUser),
            MaxLobbyMembers = (uint)maxMembers,
            // Advertised, so a join code (a search) finds it; the game's own "public" value keeps friends-only ones off its list.
            PermissionLevel = LobbyPermissionLevel.Publicadvertised,
            PresenceEnabled = false,
            AllowInvites = true,
            BucketId = "darkterritory",
            EnableJoinById = true,
        };
        _lobby.CreateLobby(ref options, null, (ref CreateLobbyCallbackInfo d) => done(Map(d.ResultCode), d.ResultCode == Result.Success ? d.LobbyId.ToString() : null));
    }

    public void JoinLobby(string localUser, string lobby, Action<EosResult> done)
    {
        var options = new JoinLobbyByIdOptions { LocalUserId = User(localUser), LobbyId = lobby, PresenceEnabled = false };
        _lobby.JoinLobbyById(ref options, null, (ref JoinLobbyByIdCallbackInfo d) => done(Map(d.ResultCode)));
    }

    public void LeaveLobby(string localUser, string lobby, Action<EosResult> done)
    {
        var options = new LeaveLobbyOptions { LocalUserId = User(localUser), LobbyId = lobby };
        _lobby.LeaveLobby(ref options, null, (ref LeaveLobbyCallbackInfo d) => done(Map(d.ResultCode)));
    }

    public void UpdateLobby(string localUser, string lobby, EosLobbyChange change, Action<EosResult> done)
    {
        var start = new UpdateLobbyModificationOptions { LocalUserId = User(localUser), LobbyId = lobby };
        if (_lobby.UpdateLobbyModification(ref start, out var mod) != Result.Success || mod is null)
        {
            done(EosResult.Other);
            return;
        }
        foreach (var (key, value) in change.Attributes)
        {
            var add = new LobbyModificationAddAttributeOptions
            {
                Attribute = new AttributeData { Key = key, Value = new AttributeDataValue { AsUtf8 = value } },
                Visibility = LobbyAttributeVisibility.Public,
            };
            mod.AddAttribute(ref add);
        }
        foreach (var (key, value) in change.MemberAttributes)
        {
            var add = new LobbyModificationAddMemberAttributeOptions
            {
                Attribute = new AttributeData { Key = key, Value = new AttributeDataValue { AsUtf8 = value } },
                Visibility = LobbyAttributeVisibility.Public,
            };
            mod.AddMemberAttribute(ref add);
        }
        if (change.Open is { } open)
        {
            var level = new LobbyModificationSetPermissionLevelOptions
            {
                PermissionLevel = open ? LobbyPermissionLevel.Publicadvertised : LobbyPermissionLevel.Inviteonly,
            };
            mod.SetPermissionLevel(ref level);
        }
        if (change.MaxMembers is { } max)
        {
            var limit = new LobbyModificationSetMaxMembersOptions { MaxMembers = (uint)max };
            mod.SetMaxMembers(ref limit);
        }
        var update = new UpdateLobbyOptions { LobbyModificationHandle = mod };
        _lobby.UpdateLobby(ref update, null, (ref UpdateLobbyCallbackInfo d) =>
        {
            mod.Release();
            done(Map(d.ResultCode));
        });
    }

    public EosLobby? Lobby(string localUser, string lobby)
    {
        var options = new CopyLobbyDetailsHandleOptions { LocalUserId = User(localUser), LobbyId = lobby };
        if (_lobby.CopyLobbyDetailsHandle(ref options, out var details) != Result.Success || details is null)
            return null;
        var described = Describe(details);
        details.Release();
        return described;
    }

    static string Text(AttributeData? data) => data?.Value.AsUtf8?.ToString() ?? "";

    static EosLobby? Describe(LobbyDetails d)
    {
        var infoOptions = new LobbyDetailsCopyInfoOptions();
        if (d.CopyInfo(ref infoOptions, out var copied) != Result.Success || copied is not { } info)
            return null;
        var count = new LobbyDetailsGetMemberCountOptions();
        uint n = d.GetMemberCount(ref count);
        var members = new List<string>((int)n);
        var memberAttributes = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        for (uint i = 0; i < n; i++)
        {
            var at = new LobbyDetailsGetMemberByIndexOptions { MemberIndex = i };
            var member = d.GetMemberByIndex(ref at);
            string puid = member.ToString();
            members.Add(puid);
            var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
            var howMany = new LobbyDetailsGetMemberAttributeCountOptions { TargetUserId = member };
            uint m = d.GetMemberAttributeCount(ref howMany);
            for (uint k = 0; k < m; k++)
            {
                var copy = new LobbyDetailsCopyMemberAttributeByIndexOptions { TargetUserId = member, AttrIndex = k };
                if (d.CopyMemberAttributeByIndex(ref copy, out var a) == Result.Success && a is { } attribute && attribute.Data is { } data)
                    attributes[data.Key.ToString()] = Text(data);
            }
            memberAttributes[puid] = attributes;
        }
        var lobbyAttributes = new Dictionary<string, string>(StringComparer.Ordinal);
        var attributeCount = new LobbyDetailsGetAttributeCountOptions();
        uint c = d.GetAttributeCount(ref attributeCount);
        for (uint k = 0; k < c; k++)
        {
            var copy = new LobbyDetailsCopyAttributeByIndexOptions { AttrIndex = k };
            if (d.CopyAttributeByIndex(ref copy, out var a) == Result.Success && a is { } attribute && attribute.Data is { } data)
                lobbyAttributes[data.Key.ToString()] = Text(data);
        }
        return new EosLobby(info.LobbyId.ToString(), info.LobbyOwnerUserId.ToString(), members, (int)info.MaxMembers,
            Open: info.PermissionLevel == LobbyPermissionLevel.Publicadvertised, lobbyAttributes, memberAttributes);
    }

    public void SearchLobbies(string localUser, EosSearch search, Action<EosResult, IReadOnlyList<EosLobby>> done)
    {
        var create = new CreateLobbySearchOptions { MaxResults = (uint)search.MaxResults };
        if (_lobby.CreateLobbySearch(ref create, out var s) != Result.Success || s is null)
        {
            done(EosResult.Other, []);
            return;
        }
        if (search.LobbyId is { } id)
        {
            var byId = new LobbySearchSetLobbyIdOptions { LobbyId = id };
            s.SetLobbyId(ref byId);
        }
        else
            foreach (var (key, value) in search.Equal)
            {
                var parameter = new LobbySearchSetParameterOptions
                {
                    Parameter = new AttributeData { Key = key, Value = new AttributeDataValue { AsUtf8 = value } },
                    ComparisonOp = ComparisonOp.Equal,
                };
                s.SetParameter(ref parameter);
            }
        var find = new LobbySearchFindOptions { LocalUserId = User(localUser) };
        s.Find(ref find, null, (ref LobbySearchFindCallbackInfo d) =>
        {
            var found = new List<EosLobby>();
            if (d.ResultCode == Result.Success)
            {
                var count = new LobbySearchGetSearchResultCountOptions();
                uint n = s.GetSearchResultCount(ref count);
                for (uint i = 0; i < n; i++)
                {
                    var copy = new LobbySearchCopySearchResultByIndexOptions { LobbyIndex = i };
                    if (s.CopySearchResultByIndex(ref copy, out var details) == Result.Success && details is not null)
                    {
                        if (Describe(details) is { } lobby)
                            found.Add(lobby);
                        details.Release();
                    }
                }
            }
            s.Release();
            // A search that finds nothing is EOS's NotFound; for the game it's an empty list.
            done(d.ResultCode == Result.Success || d.ResultCode.ToString() == "NotFound" ? EosResult.Success : Map(d.ResultCode), found);
        });
    }

    public void SendInvite(string localUser, string lobby, string to, Action<EosResult> done)
    {
        var options = new SendInviteOptions { LocalUserId = User(localUser), LobbyId = lobby, TargetUserId = User(to) };
        _lobby.SendInvite(ref options, null, (ref SendInviteCallbackInfo d) => done(Map(d.ResultCode)));
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        // The lobbies' leaving (asked by the backend) goes out on these last ticks.
        for (int i = 0; i < 10; i++)
        {
            _platform.Tick();
            Thread.Sleep(10);
        }
        _p2p.RemoveNotifyPeerConnectionRequest(_requests);
        _p2p.RemoveNotifyPeerConnectionEstablished(_established);
        _p2p.RemoveNotifyPeerConnectionClosed(_closed);
        _lobby.RemoveNotifyLobbyInviteReceived(_invites);
        _lobby.RemoveNotifyLobbyMemberStatusReceived(_members);
        _platform.Release();
        PlatformInterface.Shutdown();
    }
}
#endif
