using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace Ballast.Net;

/// <summary>A game someone's hosting on the local network, as its beacon describes it.</summary>
/// <param name="Address">Where to connect: the beacon's sender, at the game's port.</param>
public sealed record LanGame(IPEndPoint Address, string Host, string Night, int Aboard, int Protocol, string Game);

/// <summary>
/// A hosted game saying where it is on the local network, about once a second: a small datagram broadcast to the beacon
/// port, so a friend on the same wifi finds it without typing an address (the LAN mode of co-op games like Lethal Company).
/// Nothing in it is trusted; it only fills a list the joiner picks from.
/// </summary>
public sealed class LanBeacon : IDisposable
{
    public const int DefaultPort = 27451;
    const string Magic = "BALLAST-LAN/1";

    readonly Socket _socket;
    readonly int _beaconPort;
    readonly IReadOnlyList<IPAddress> _targets;
    double _next;

    /// <param name="targets">Where to send; by default the limited broadcast and each interface's own broadcast address.</param>
    public LanBeacon(int beaconPort = DefaultPort, IReadOnlyList<IPAddress>? targets = null)
    {
        _beaconPort = beaconPort;
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { EnableBroadcast = true, Blocking = false };
        _socket.Bind(new IPEndPoint(IPAddress.Any, 0));
        _targets = targets ?? Broadcasts();
    }

    /// <summary>Sends the beacon if it's due (<paramref name="now"/> in seconds, any clock).</summary>
    public void Tick(double now, string game, int protocol, int gamePort, string host, string night, int aboard)
    {
        if (now < _next)
            return;
        _next = now + 1;
        var payload = Encoding.UTF8.GetBytes(string.Join('\n', Magic, game, protocol, gamePort, aboard, Clean(host), Clean(night)));
        foreach (var target in _targets)
        {
            try
            {
                _socket.SendTo(payload, new IPEndPoint(target, _beaconPort));
            }
            catch (SocketException)
            {
                // A network that won't take a broadcast (no route, a VPN): the address on the HUD still works.
            }
        }
    }

    static string Clean(string s) => s.Replace('\n', ' ').Length > 48 ? s.Replace('\n', ' ')[..48] : s.Replace('\n', ' ');

    /// <summary>The limited broadcast, and every up IPv4 interface's directed broadcast (some routers drop the former).</summary>
    static List<IPAddress> Broadcasts()
    {
        var list = new List<IPAddress> { IPAddress.Broadcast };
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;
                foreach (var a in nic.GetIPProperties().UnicastAddresses)
                {
                    if (a.Address.AddressFamily != AddressFamily.InterNetwork || a.IPv4Mask is not { } mask)
                        continue;
                    var ip = a.Address.GetAddressBytes();
                    var m = mask.GetAddressBytes();
                    var b = new byte[4];
                    for (int i = 0; i < 4; i++)
                        b[i] = (byte)(ip[i] | ~m[i]);
                    var cast = new IPAddress(b);
                    if (!list.Contains(cast))
                        list.Add(cast);
                }
            }
        }
        catch (NetworkInformationException)
        {
        }
        return list;
    }

    /// <summary>Reads a beacon; null for anything that isn't one.</summary>
    public static LanGame? Parse(ReadOnlySpan<byte> payload, IPAddress from)
    {
        string[] f;
        try
        {
            f = Encoding.UTF8.GetString(payload).Split('\n');
        }
        catch (ArgumentException)
        {
            return null;
        }
        if (f.Length < 7 || f[0] != Magic || !int.TryParse(f[2], out int protocol) || !int.TryParse(f[3], out int port)
            || port is <= 0 or > 65535 || !int.TryParse(f[4], out int aboard))
            return null;
        return new LanGame(new IPEndPoint(from, port), f[5], f[6], aboard, protocol, f[1]);
    }

    public void Dispose() => _socket.Dispose();
}

/// <summary>Listens for <see cref="LanBeacon"/>s: the games on the local network, each until it's gone quiet a few seconds.</summary>
public sealed class LanBrowser : IDisposable
{
    readonly Socket? _socket;
    readonly Dictionary<IPEndPoint, (LanGame Game, double Seen)> _games = [];
    readonly byte[] _buffer = new byte[1024];

    public LanBrowser(int beaconPort = LanBeacon.DefaultPort)
    {
        try
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { Blocking = false };
            // More than one browser on a machine (two copies of the game) shares the port.
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.Bind(new IPEndPoint(IPAddress.Any, beaconPort));
            _socket = socket;
        }
        catch (SocketException e)
        {
            Error = e.Message;
        }
    }

    /// <summary>Why it can't listen, if it can't (the port's taken): joining by address still works.</summary>
    public string? Error { get; }

    /// <summary>Reads what's arrived; games unheard for 4 s drop off. <paramref name="now"/> in seconds, any clock.</summary>
    public void Poll(double now)
    {
        if (_socket is not null)
        {
            EndPoint from = new IPEndPoint(IPAddress.Any, 0);
            while (true)
            {
                int n;
                try
                {
                    if (_socket.Available <= 0)
                        break;
                    n = _socket.ReceiveFrom(_buffer, ref from);
                }
                catch (SocketException)
                {
                    break;
                }
                if (Parse(n, (IPEndPoint)from) is { } game)
                    _games[game.Address] = (game, now);
            }
        }
        foreach (var key in _games.Where(g => now - g.Value.Seen > 4).Select(g => g.Key).ToList())
            _games.Remove(key);
    }

    LanGame? Parse(int n, IPEndPoint from) => LanBeacon.Parse(_buffer.AsSpan(0, n), from.Address);

    /// <summary>What's out there, by host name then address.</summary>
    public IReadOnlyList<LanGame> Games => [.. _games.Values.Select(v => v.Game).OrderBy(g => g.Host, StringComparer.Ordinal).ThenBy(g => g.Address.ToString(), StringComparer.Ordinal)];

    public void Dispose() => _socket?.Dispose();
}
