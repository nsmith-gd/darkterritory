using Ballast.Net;
using Ballast.Online;
using Ballast.Online.Eos;
using DarkTerritory.Sim.Net;

namespace DarkTerritory.Game;

/// <summary>
/// The harness over the EOS path without EOS (<c>dt harness --eos</c>, note 896): the host signs in to <see cref="FakeEos"/>
/// and opens a lobby, every bot is another machine that joins it by its code, and the game runs over EOS P2P to the lobby's
/// owner. Every machine is behind a strict NAT unless asked otherwise, so no two can meet directly and all of it goes
/// through the relays: the case EOS is here for.
/// </summary>
public sealed class FakeEosLobbyNetwork(EosNat nat = EosNat.Strict, EosRelay relay = EosRelay.AllowRelays) : IHarnessNetwork
{
    readonly FakeEos _cloud = new();
    readonly List<Lobby> _lobbies = new();
    readonly List<EosBackend> _machines = new();
    Lobby? _host;
    const int RoomForBots = 64;

    public string Name => "fake EOS lobby";
    public FakeEos Cloud => _cloud;
    /// <summary>Everyone in the host's lobby, the host included.</summary>
    public int Members => _host?.Members.Count ?? 0;

    EosBackend SignIn(string name)
    {
        var machine = EosBackend.TryStart(_cloud.Machine(nat), name, relay, out var error) ?? throw new IOException($"eos: {error}");
        _machines.Add(machine);
        return machine;
    }

    public ITransport Host()
    {
        var owner = SignIn("host");
        // As FakeLobbyNetwork's: roomier than any cap, so it's the host session that turns a bot away past it (note 254).
        _host = Open(Lobby.Host(owner, "darkterritory", Protocol.Version, RoomForBots));
        return OnlineTransport.Host(owner);
    }

    public ITransport Client(int index)
    {
        if (_host is null)
            throw new InvalidOperationException("the host opens the lobby first");
        var bot = SignIn($"bot {index + 1}");
        var lobby = Open(Lobby.JoinByCode(bot, _host.Code, "darkterritory", Protocol.Version));
        return OnlineTransport.Connect(bot, lobby.Owner);
    }

    Lobby Open(Lobby lobby)
    {
        // The fake answers a request on the next tick, and a join by code is two (the search, then the join).
        for (int i = 0; i < 6 && lobby.Status is Lobby.State.Creating or Lobby.State.Joining; i++)
            lobby.Poll();
        if (lobby.Status != Lobby.State.Open)
            throw new IOException($"lobby: {lobby.Error}");
        // The host's code goes up with its first update, a poll after it's made.
        lobby.Poll();
        _lobbies.Add(lobby);
        return lobby;
    }

    public void Pump()
    {
        foreach (var l in _lobbies)
            l.Poll();
    }

    public void Dispose()
    {
        foreach (var l in _lobbies)
            l.Dispose();
        foreach (var m in _machines)
            m.Dispose();
    }
}
