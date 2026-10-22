namespace Ballast.Online.Eos;

/// <summary>
/// The part of the Epic Online Services SDK the game uses, in EOS's own terms (note 896): product user ids and lobby ids are
/// strings, and every request answers in a callback that fires inside a later <see cref="Tick"/>, never inside the call.
/// <see cref="EosBackend"/> maps it onto <see cref="IOnlineBackend"/>; <see cref="FakeEos"/> is the in-process stand-in the
/// tests and <c>dt harness --eos</c> run on; the real SDK's binding (<c>EpicSdk</c>) is compiled only when
/// <c>external/eos/</c> holds Epic's SDK (external/eos/README.md).
/// </summary>
public interface IEosSdk : IDisposable
{
    /// <summary>Pumps the SDK (EOS_Platform_Tick): the callbacks of earlier requests and the notifications fire in here.</summary>
    void Tick();

    // Connect, by device id: no Epic account, no launcher, nothing for a player to sign up to.
    /// <summary>Signs this machine in (EOS_Connect_Login with a device id, made first if there's none); the product user id.</summary>
    void Login(string displayName, Action<EosResult, string?> done);

    // P2P (EOS_P2P_*), one socket, channel 0, unreliable: the game's protocol does its own reliability.
    /// <summary>Whether the relays may carry our packets: allowed (direct first), forced (always), or never.</summary>
    void SetRelayControl(EosRelay relay);
    /// <summary>What sort of NAT this machine is behind, as EOS measured it (EOS_P2P_QueryNATType); Unknown until it has.</summary>
    EosNat Nat { get; }
    /// <summary>Sends one packet. Sending to a peer also accepts that peer's connection, as EOS does.</summary>
    void SendPacket(string localUser, string to, ReadOnlySpan<byte> data);
    bool ReceivePacket(string localUser, Span<byte> buffer, out int length, out string from);
    /// <summary>Lets a peer that asked (<see cref="ConnectionRequested"/>) reach us.</summary>
    void AcceptConnection(string localUser, string from);
    void CloseConnection(string localUser, string peer);
    /// <summary>Someone we haven't accepted sent to us (EOS_P2P_AddNotifyPeerConnectionRequest); their packets wait on it.</summary>
    event Action<string>? ConnectionRequested;
    /// <summary>How an open connection goes, from EOS_P2P_AddNotifyPeerConnectionEstablished; null while there's none.</summary>
    EosConnection? ConnectionTo(string peer);

    // Lobbies (EOS_Lobby_*).
    /// <param name="done">The new lobby's id, or why not.</param>
    void CreateLobby(string localUser, int maxMembers, Action<EosResult, string?> done);
    /// <summary>EOS_Lobby_JoinLobbyById (the lobby was made with EnableJoinById).</summary>
    void JoinLobby(string localUser, string lobby, Action<EosResult> done);
    void LeaveLobby(string localUser, string lobby, Action<EosResult> done);
    /// <summary>
    /// One EOS_Lobby_UpdateLobby with everything in <paramref name="change"/> (EOS limits how often a lobby may be updated, so
    /// the backend gathers a frame's changes into one). The owner's lobby attributes; anyone's own member attributes.
    /// </summary>
    void UpdateLobby(string localUser, string lobby, EosLobbyChange change, Action<EosResult> done);
    /// <summary>The lobby as EOS has it cached (EOS_Lobby_CopyLobbyDetailsHandle): lobbies we're in. Null when it hasn't.</summary>
    EosLobby? Lobby(string localUser, string lobby);
    /// <summary>EOS_LobbySearch_Find: advertised lobbies whose attributes equal every one given (or the one lobby by id).</summary>
    void SearchLobbies(string localUser, EosSearch search, Action<EosResult, IReadOnlyList<EosLobby>> done);
    /// <summary>EOS_Lobby_SendInvite: arrives as <see cref="InviteReceived"/> on the target's machine.</summary>
    void SendInvite(string localUser, string lobby, string to, Action<EosResult> done);
    /// <summary>(lobby, from) when an invite arrives (EOS_Lobby_AddNotifyLobbyInviteReceived).</summary>
    event Action<string, string>? InviteReceived;
    /// <summary>(lobby, user, status) for lobbies we're in (EOS_Lobby_AddNotifyLobbyMemberStatusReceived).</summary>
    event Action<string, string, EosMemberStatus>? MemberStatus;
}

/// <summary>The EOS_EResult values the game tells apart; anything else is <see cref="Other"/>.</summary>
public enum EosResult : byte
{
    Success,
    /// <summary>EOS_NotFound: the lobby is gone.</summary>
    NotFound,
    /// <summary>EOS_Lobby_TooManyPlayers.</summary>
    LobbyFull,
    /// <summary>EOS_Lobby_NotAllowed / EOS_Lobby_InviteOnly: the doors are shut.</summary>
    NotAllowed,
    /// <summary>EOS_NoConnection / EOS_TimedOut: Epic's servers didn't answer.</summary>
    NoConnection,
    /// <summary>EOS_InvalidCredentials / EOS_InvalidAuth: the game's EOS credentials are wrong or missing.</summary>
    InvalidCredentials,
    Other,
}

/// <summary>EOS_ERelayControl.</summary>
public enum EosRelay : byte
{
    /// <summary>Straight to the peer when NAT lets us, Epic's relays when it doesn't (the default).</summary>
    AllowRelays,
    /// <summary>Always through the relays: nobody's address reaches anybody, as on Steam.</summary>
    ForceRelays,
    /// <summary>Never the relays: two strict NATs can't meet.</summary>
    NoRelays,
}

/// <summary>EOS_ENATType.</summary>
public enum EosNat : byte { Unknown, Open, Moderate, Strict }

/// <summary>EOS_ELobbyMemberStatus, the ones the game reads.</summary>
public enum EosMemberStatus : byte { Joined, Left, Disconnected, Kicked, Promoted, Closed }

/// <summary>An open P2P connection: through Epic's relays or punched straight through (EOS_EConnectionEstablishedType's network).</summary>
public sealed record EosConnection(bool Relayed);

/// <summary>A lobby as EOS describes it (EOS_LobbyDetails): owner, members with their own attributes, the limit, the lobby's attributes.</summary>
public sealed record EosLobby(string Id, string Owner, IReadOnlyList<string> Members, int MaxMembers, bool Open,
    IReadOnlyDictionary<string, string> Attributes, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> MemberAttributes)
{
    public string Get(string key) => Attributes.GetValueOrDefault(key, "");
}

/// <summary>What one EOS_Lobby_UpdateLobby changes: lobby attributes (owner only), our own member attributes, the doors and the limit.</summary>
public sealed record EosLobbyChange
{
    public Dictionary<string, string> Attributes { get; } = new();
    public Dictionary<string, string> MemberAttributes { get; } = new();
    /// <summary>EOS_LobbyModification_SetPermissionLevel: advertised (open) or invite only (shut); null leaves it.</summary>
    public bool? Open { get; set; }
    public int? MaxMembers { get; set; }
    public bool IsEmpty => Attributes.Count == 0 && MemberAttributes.Count == 0 && Open is null && MaxMembers is null;
}

/// <summary>An EOS_LobbySearch: by id, or by attributes that must equal these.</summary>
public sealed record EosSearch(IReadOnlyDictionary<string, string> Equal, int MaxResults = 50)
{
    public string? LobbyId { get; init; }
}
