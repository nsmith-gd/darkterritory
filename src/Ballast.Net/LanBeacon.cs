using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace Ballast.Net;

/// <summary>A game someone's hosting on the local network, as its beacon describes it.</summary>
/// <param name="Address">Where to connect: the beacon's sender, at the game's port.</param>
public sealed record LanGame(IPEndPoint Address, string Host, string Night, int Aboard, int Protocol, string Game)
{
    /// <summary>What the host called the lobby ("" from an older beacon: the browser shows the host's name).</summary>
    public string Name { get; init; } = "";
    /// <summary>How many it takes (0: not said).</summary>
    public int Max { get; init; }
    /// <summary>The game's own word for how hard it is (a route tier), or "".</summary>
    public string Tier { get; init; } = "";
    /// <summary>The same game's platform lobby, if it's listed there too (so a browser shows it once).</summary>
    public string Lobby { get; init; } = "";
    /// <summary>Where the beacon came from: where a ping goes.</summary>
    public IPEndPoint? Beacon { get; init; }
    /// <summary>The measured round trip to the host's beacon, in milliseconds; null until a pong has come back.</summary>
    public double? PingMs { get; init; }
    /// <summary>A private game: a joiner needs its password (the browser shows a lock).</summary>
    public bool Locked { get; init; }
    /// <summary>The game's own word for who it's for (a mood), or "".</summary>
    public string Mood { get; init; } = "";
}

/// <summary>What a host's beacon says about its game.</summary>
public sealed record LanAdvert(string Game, int Protocol, int GamePort, string Host, string Night, int Aboard)
{
    public string Name { get; init; } = "";
    public int Max { get; init; }
    public string Tier { get; init; } = "";
    public string Lobby { get; init; } = "";
    public bool Locked { get; init; }
    public string Mood { get; init; } = "";
}

/// <summary>
/// A hosted game saying where it is on the local network, about once a second: a small datagram broadcast to the beacon
/// port, so a friend on the same wifi finds it without typing an address (the LAN mode of co-op games like Lethal Company).
/// Nothing in it is trusted; it only fills a list the joiner picks from. The beacon's socket also answers pings
/// ("BALLAST-PING/1" and a token, sent straight back as a pong): the browser's measured round trip to each host is the ping it lists, as Lethal Company's
/// lobby list shows one.
/// </summary>
public sealed class LanBeacon : IDisposable
{
    public const int DefaultPort = 27451;
    const string Magic = "BALLAST-LAN/1";

    readonly Socket _socket;
    readonly int _beaconPort;
    readonly IReadOnlyList<IPAddress> _targets;
    readonly byte[] _buffer = new byte[256];
    double _next;

    /// <param name="targets">Where to send; by default the limited broadcast and each interface's own broadcast address.</param>
    public LanBeacon(int beaconPort = DefaultPort, IReadOnlyList<IPAddress>? targets = null)
    {
        _beaconPort = beaconPort;
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { EnableBroadcast = true, Blocking = false };
        _socket.Bind(new IPEndPoint(IPAddress.Any, 0));
        _targets = targets ?? Broadcasts();
    }

    /// <summary>Answers any pings, and sends the beacon if it's due (<paramref name="now"/> in seconds, any clock).</summary>
    public void Tick(double now, LanAdvert advert)
    {
        Answer();
        if (now < _next)
            return;
        _next = now + 1;
        // The fields after the seventh are newer; an older browser reads the first seven and ignores the rest.
        var payload = Encoding.UTF8.GetBytes(string.Join('\n', Magic, advert.Game, advert.Protocol, advert.GamePort, advert.Aboard, Clean(advert.Host),
            Clean(advert.Night), Clean(advert.Name), advert.Max, Clean(advert.Tier), Clean(advert.Lobby), advert.Locked ? 1 : 0, Clean(advert.Mood)));
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

    /// <summary>Sends every ping that's come in back where it came from, as a pong with the same token.</summary>
    void Answer()
    {
        EndPoint from = new IPEndPoint(IPAddress.Any, 0);
        while (true)
        {
            int n;
            try
            {
                if (_socket.Available <= 0)
                    return;
                n = _socket.ReceiveFrom(_buffer, ref from);
            }
            catch (SocketException)
            {
                return;
            }
            var text = _buffer.AsSpan(0, n);
            if (!text.StartsWith(PingPrefix))
                continue;
            int reply = PongPrefix.Length + n - PingPrefix.Length;
            if (reply > _buffer.Length)
                continue;
            var pong = new byte[reply];
            PongPrefix.CopyTo(pong);
            text[PingPrefix.Length..].CopyTo(pong.AsSpan(PongPrefix.Length));
            try
            {
                _socket.SendTo(pong, from);
            }
            catch (SocketException)
            {
            }
        }
    }

    internal static ReadOnlySpan<byte> PingPrefix => "BALLAST-PING/1\n"u8;
    internal static ReadOnlySpan<byte> PongPrefix => "BALLAST-PONG/1\n"u8;

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
        return new LanGame(new IPEndPoint(from, port), f[5], f[6], aboard, protocol, f[1])
        {
            Name = f.Length > 7 ? f[7] : "",
            Max = f.Length > 8 && int.TryParse(f[8], out int max) ? max : 0,
            Tier = f.Length > 9 ? f[9] : "",
            Lobby = f.Length > 10 ? f[10] : "",
            Locked = f.Length > 11 && f[11] == "1",
            Mood = f.Length > 12 ? f[12] : "",
        };
    }

    public void Dispose() => _socket.Dispose();
}

/// <summary>
/// Listens for <see cref="LanBeacon"/>s: the games on the local network, each until it's gone quiet a few seconds. It
/// pings each one's beacon about once a second, from a socket of its own, and keeps the round trip: that's a real
/// measurement of the path to the host's machine, which is the game's path less the game's own processing.
/// </summary>
public sealed class LanBrowser : IDisposable
{
    readonly Socket? _socket;
    readonly Socket? _pinger;
    readonly Dictionary<IPEndPoint, Heard> _games = [];
    /// <summary>Pings out, by token: which game, and when (<see cref="Stopwatch"/> ticks, not the caller's clock).</summary>
    readonly Dictionary<ulong, (IPEndPoint Game, long Sent)> _pings = [];
    readonly byte[] _buffer = new byte[1024];
    ulong _token;

    sealed class Heard(LanGame game, double seen)
    {
        public LanGame Game = game;
        public double Seen = seen;
        public double NextPing;
        public double? PingMs;
    }

    public LanBrowser(int beaconPort = LanBeacon.DefaultPort)
    {
        try
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { Blocking = false };
            // More than one browser on a machine (two copies of the game) shares the port.
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.Bind(new IPEndPoint(IPAddress.Any, beaconPort));
            _socket = socket;
            // Pongs come back to a port that's this browser's alone: on the shared beacon port they'd go to whichever
            // copy of the game bound it last.
            var pinger = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { Blocking = false };
            pinger.Bind(new IPEndPoint(IPAddress.Any, 0));
            _pinger = pinger;
        }
        catch (SocketException e)
        {
            Error = e.Message;
        }
    }

    /// <summary>Why it can't listen, if it can't (the port's taken): joining by address still works.</summary>
    public string? Error { get; }

    /// <summary>
    /// Reads what's arrived, pings what's due a ping; games unheard for 4 s drop off. <paramref name="now"/> in seconds,
    /// any clock (it paces the pings and the forgetting; the round trips are timed on the system's own clock).
    /// </summary>
    public void Poll(double now)
    {
        if (_socket is not null)
        {
            EndPoint from = new IPEndPoint(IPAddress.Any, 0);
            while (Receive(_socket, ref from) is int n)
                if (LanBeacon.Parse(_buffer.AsSpan(0, n), ((IPEndPoint)from).Address) is { } game)
                {
                    game = game with { Beacon = (IPEndPoint)from };
                    if (_games.TryGetValue(game.Address, out var known))
                        (known.Game, known.Seen) = (game, now);
                    else
                        _games[game.Address] = new Heard(game, now);
                }
        }
        if (_pinger is not null)
        {
            EndPoint from = new IPEndPoint(IPAddress.Any, 0);
            while (Receive(_pinger, ref from) is int n)
                Pong(_buffer.AsSpan(0, n));
        }
        foreach (var key in _games.Where(g => now - g.Value.Seen > 4).Select(g => g.Key).ToList())
            _games.Remove(key);
        foreach (var (address, heard) in _games)
            if (now >= heard.NextPing && heard.Game.Beacon is { } beacon)
            {
                heard.NextPing = now + 1;
                Ping(address, beacon);
            }
        // A ping that never came back is a lost datagram, not a round trip; forget it after a while.
        long stale = Stopwatch.GetTimestamp() - 5 * Stopwatch.Frequency;
        foreach (var token in _pings.Where(p => p.Value.Sent < stale).Select(p => p.Key).ToList())
            _pings.Remove(token);
    }

    /// <summary>Pings every game now, rather than on its next second (the join screen's REFRESH).</summary>
    public void Refresh()
    {
        foreach (var heard in _games.Values)
            heard.NextPing = 0;
    }

    int? Receive(Socket socket, ref EndPoint from)
    {
        try
        {
            return socket.Available > 0 ? socket.ReceiveFrom(_buffer, ref from) : null;
        }
        catch (SocketException)
        {
            return null;
        }
    }

    void Ping(IPEndPoint game, IPEndPoint beacon)
    {
        ulong token = ++_token;
        var payload = Encoding.ASCII.GetBytes(token.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var datagram = new byte[LanBeacon.PingPrefix.Length + payload.Length];
        LanBeacon.PingPrefix.CopyTo(datagram);
        payload.CopyTo(datagram, LanBeacon.PingPrefix.Length);
        _pings[token] = (game, Stopwatch.GetTimestamp());
        try
        {
            _pinger!.SendTo(datagram, beacon);
        }
        catch (SocketException)
        {
            _pings.Remove(token);
        }
    }

    void Pong(ReadOnlySpan<byte> datagram)
    {
        if (!datagram.StartsWith(LanBeacon.PongPrefix)
            || !ulong.TryParse(datagram[LanBeacon.PongPrefix.Length..], System.Globalization.CultureInfo.InvariantCulture, out ulong token)
            || !_pings.Remove(token, out var ping) || !_games.TryGetValue(ping.Game, out var heard))
            return;
        double ms = Stopwatch.GetElapsedTime(ping.Sent).TotalMilliseconds;
        // Smoothed a little, as a game's ping readout is, so one late datagram doesn't make the row jump.
        heard.PingMs = heard.PingMs is { } was ? was * 0.7 + ms * 0.3 : ms;
    }

    /// <summary>What's out there, by host name then address, each with its ping once one has come back.</summary>
    public IReadOnlyList<LanGame> Games => [.. _games.Values.Select(v => v.Game with { PingMs = v.PingMs })
        .OrderBy(g => g.Host, StringComparer.Ordinal).ThenBy(g => g.Address.ToString(), StringComparer.Ordinal)];

    public void Dispose()
    {
        _socket?.Dispose();
        _pinger?.Dispose();
    }
}
