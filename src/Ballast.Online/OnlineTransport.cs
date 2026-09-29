using Ballast.Net;

namespace Ballast.Online;

/// <summary>
/// The game's connection protocol (<see cref="DatagramTransport{TAddress}"/>) over a platform's relayed P2P
/// datagrams, addressed by user. A Steam game and a direct-IP game are the same game on the wire.
/// </summary>
public sealed class OnlineTransport : DatagramTransport<UserId>
{
    OnlineTransport(IOnlineBackend online, DatagramOptions? options) : base(online.Carrier, options, ownsCarrier: false) => Online = online;
    OnlineTransport(IOnlineBackend online, DatagramOptions? options, UserId host) : base(online.Carrier, options, host, ownsCarrier: false) => Online = online;

    public IOnlineBackend Online { get; }

    /// <summary>Accepts connections from anyone the platform lets reach us (lobby members).</summary>
    public static OnlineTransport Host(IOnlineBackend online, DatagramOptions? options = null) => new(online, options);

    /// <summary>Starts connecting to a user, normally the owner of the lobby we've just entered.</summary>
    public static OnlineTransport Connect(IOnlineBackend online, UserId host, DatagramOptions? options = null)
    {
        if (host == online.Me)
            throw new ArgumentException("can't connect to yourself through the platform; the host plays over localhost", nameof(host));
        return new(online, options, host);
    }
}
