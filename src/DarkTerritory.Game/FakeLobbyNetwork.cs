using Ballast.Net;
using Ballast.Online;
using DarkTerritory.Sim.Net;

namespace DarkTerritory.Game;

/// <summary>
/// The harness over the Steam path without Steam (<c>dt harness --online</c>): the host opens a lobby on
/// <see cref="FakeOnline"/>, every bot is a separate account that joins it, and the game runs over
/// <see cref="OnlineTransport"/> to the lobby's owner, exactly as friends' machines would.
/// </summary>
public sealed class FakeLobbyNetwork : IHarnessNetwork
{
    readonly FakeOnline _cloud = new();
    readonly List<Lobby> _lobbies = new();
    Lobby? _host;

    public string Name => "fake Steam lobby";
    public FakeOnline Cloud => _cloud;
    /// <summary>Everyone in the host's lobby, the host included.</summary>
    public int Members => _host?.Members.Count ?? 0;

    public ITransport Host()
    {
        var owner = _cloud.SignIn("host");
        _host = Open(Lobby.Host(owner, "darkterritory", Protocol.Version, NetPlaySession.MaxCrew));
        return OnlineTransport.Host(owner);
    }

    public ITransport Client(int index)
    {
        if (_host is null)
            throw new InvalidOperationException("the host opens the lobby first");
        var bot = _cloud.SignIn($"bot {index + 1}");
        var lobby = Open(Lobby.Join(bot, _host.Id, "darkterritory", Protocol.Version));
        return OnlineTransport.Connect(bot, lobby.Owner);
    }

    Lobby Open(Lobby lobby)
    {
        // The fake answers on the next poll.
        for (int i = 0; i < 3 && lobby.Status is Lobby.State.Creating or Lobby.State.Joining; i++)
            lobby.Poll();
        if (lobby.Status != Lobby.State.Open)
            throw new IOException($"lobby: {lobby.Error}");
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
    }
}
