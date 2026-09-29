using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Ballast.Net;

/// <summary>Knobs for <see cref="UdpTransport"/>. Defaults suit play; tests shorten them.</summary>
public sealed record UdpOptions
{
    /// <summary>Silence from a peer for this long and it's gone.</summary>
    public double TimeoutSeconds { get; init; } = 8;
    /// <summary>Send something at least this often so an idle link doesn't time out.</summary>
    public double KeepaliveSeconds { get; init; } = 1;
    /// <summary>A client gives up connecting after this long.</summary>
    public double ConnectSeconds { get; init; } = 10;
    /// <summary>Test hook: drop this fraction of outgoing datagrams (seeded), to prove the reliable channel.</summary>
    public double SimulatedLoss { get; init; }
    public int Seed { get; init; } = 1;
}

/// <summary>
/// <see cref="ITransport"/> over plain UDP: direct IP and LAN play (itch.io builds, playtests) before the
/// Steam and EOS backends (ARCHITECTURE §4). One socket, polled once per tick, no threads.
/// <list type="bullet">
/// <item>Handshake: the client repeats Connect until the host Accepts with its peer id and a session token.
/// The token rides on every datagram, so a stray or stale sender can't inject into a session.</item>
/// <item>Unreliable: one datagram per message, dropped if lost (snapshots, input, voice).</item>
/// <item>Reliable-ordered: sequence numbers, a cumulative ack on every datagram, resend until acked,
/// in-order delivery with a reorder buffer (welcome, RPCs).</item>
/// </list>
/// Messages must fit one datagram (<see cref="MaxPayload"/>); nothing here fragments.
/// </summary>
public sealed class UdpTransport : ITransport
{
    public const int MaxPayload = 1200;
    const uint Magic = 0x31425444; // "DTB1"
    const ushort Version = 1;

    enum Kind : byte { Connect = 1, Accept = 2, Data = 3, Ack = 4, Ping = 5, Bye = 6, Pong = 7 }

    readonly Socket _socket;
    readonly UdpOptions _options;
    readonly bool _isHost;
    readonly Dictionary<EndPoint, Link> _byAddress = new();
    readonly Dictionary<PeerId, Link> _byPeer = new();
    readonly List<TransportEvent> _events = new();
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly byte[] _receive = new byte[2048];
    readonly NetWriter _writer = new();
    readonly Random _loss;
    readonly EndPoint? _hostAddress;
    readonly ulong _nonce;
    ulong _nextPeer = 1;
    double _connectStarted, _lastConnectSent = double.NegativeInfinity;
    bool _disposed, _gaveUp;

    UdpTransport(Socket socket, UdpOptions options, bool isHost, EndPoint? hostAddress)
    {
        _socket = socket;
        _options = options;
        _isHost = isHost;
        _hostAddress = hostAddress;
        _loss = new Random(options.Seed);
        _nonce = (ulong)Random.Shared.NextInt64();
        LocalId = isHost ? PeerId.Host : new PeerId(ulong.MaxValue);
    }

    /// <summary>Listens on <paramref name="port"/> (0 = any free port; see <see cref="Port"/>).</summary>
    public static UdpTransport Host(int port, UdpOptions? options = null, IPAddress? bind = null)
    {
        var socket = MakeSocket();
        socket.Bind(new IPEndPoint(bind ?? IPAddress.Any, port));
        return new UdpTransport(socket, options ?? new UdpOptions(), isHost: true, hostAddress: null);
    }

    /// <summary>Starts connecting to a host. <see cref="TransportEventKind.Connected"/> arrives once it accepts.</summary>
    public static UdpTransport Connect(IPEndPoint host, UdpOptions? options = null)
    {
        if (host.AddressFamily != AddressFamily.InterNetwork)
            throw new ArgumentException("IPv4 only for now", nameof(host));
        var socket = MakeSocket();
        socket.Bind(new IPEndPoint(IPAddress.Any, 0));
        var t = new UdpTransport(socket, options ?? new UdpOptions(), isHost: false, hostAddress: host);
        t._connectStarted = t.Now;
        return t;
    }

    static Socket MakeSocket()
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { Blocking = false };
        if (OperatingSystem.IsWindows())
        {
            // Otherwise an ICMP "port unreachable" from a departed peer surfaces as a ConnectionReset on
            // the next receive, for everyone on the socket.
            const int SioUdpConnReset = -1744830452;
            socket.IOControl(SioUdpConnReset, [0, 0, 0, 0], null);
        }
        return socket;
    }

    public PeerId LocalId { get; private set; }
    public int Port => ((IPEndPoint)_socket.LocalEndPoint!).Port;
    public bool IsConnected => _isHost || _byPeer.ContainsKey(PeerId.Host);
    /// <summary>Round-trip estimate to a peer, seconds (smoothed from reliable acks).</summary>
    public double RoundTrip(PeerId peer) => _byPeer.TryGetValue(peer, out var l) ? l.Rtt : 0;
    public int DatagramsSent { get; private set; }
    public int DatagramsReceived { get; private set; }

    double Now => _clock.Elapsed.TotalSeconds;

    sealed class Link(PeerId peer, EndPoint address, ulong token)
    {
        public readonly PeerId Peer = peer;
        public readonly EndPoint Address = address;
        public readonly ulong Token = token;
        public double LastHeard, LastSent, LastPing = double.NegativeInfinity;
        public double Rtt = 0.1;
        public bool Measured;
        // Reliable send side.
        public uint NextSendSeq = 1;
        public readonly SortedDictionary<uint, (byte[] Payload, double SentAt, double FirstSent)> Unacked = new();
        // Reliable receive side.
        public uint NextExpected = 1;
        public readonly SortedDictionary<uint, byte[]> Early = new();
        public bool AckOwed;
    }

    public void Send(PeerId to, ReadOnlySpan<byte> payload, Delivery delivery)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (payload.Length > MaxPayload)
            throw new ArgumentException($"message of {payload.Length} bytes is over the {MaxPayload}-byte datagram limit");
        if (!_byPeer.TryGetValue(to, out var link))
            return; // not (or no longer) connected: like a lost packet
        if (delivery == Delivery.ReliableOrdered)
        {
            uint seq = link.NextSendSeq++;
            link.Unacked[seq] = (payload.ToArray(), Now, Now);
            SendData(link, seq, payload);
        }
        else
        {
            SendData(link, 0, payload);
        }
    }

    void SendData(Link link, uint reliableSeq, ReadOnlySpan<byte> payload)
    {
        Begin(Kind.Data, link.Token);
        _writer.U32(link.NextExpected - 1); // cumulative ack
        _writer.U32(reliableSeq);           // 0 = unreliable
        _writer.Bytes(payload);
        link.AckOwed = false;
        Transmit(link.Address, link);
    }

    void Begin(Kind kind, ulong token)
    {
        _writer.Reset();
        _writer.U8((byte)kind);
        _writer.U64(token);
    }

    void Transmit(EndPoint to, Link? link)
    {
        if (link is not null)
            link.LastSent = Now;
        if (_options.SimulatedLoss > 0 && _loss.NextDouble() < _options.SimulatedLoss)
            return;
        try
        {
            _socket.SendTo(_writer.Written, SocketFlags.None, to);
            DatagramsSent++;
        }
        catch (SocketException)
        {
            // Unreachable or buffer full: indistinguishable from loss, and handled the same way.
        }
    }

    public void Poll(List<TransportEvent> into)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        double now = Now;
        Receive(now);
        if (!_isHost && !IsConnected && !_gaveUp && _hostAddress is not null)
            KeepConnecting(now);
        foreach (var link in _byPeer.Values.ToList())
            Maintain(link, now);
        into.AddRange(_events);
        _events.Clear();
    }

    void KeepConnecting(double now)
    {
        if (now - _connectStarted > _options.ConnectSeconds)
        {
            // Nobody answered: report it once, the same way a dropped connection is reported.
            _gaveUp = true;
            _events.Add(new TransportEvent(TransportEventKind.Disconnected, PeerId.Host));
            return;
        }
        if (now - _lastConnectSent < 0.25)
            return;
        _lastConnectSent = now;
        _writer.Reset();
        _writer.U8((byte)Kind.Connect);
        _writer.U32(Magic);
        _writer.U16(Version);
        _writer.U64(_nonce);
        Transmit(_hostAddress!, null);
    }

    void Maintain(Link link, double now)
    {
        if (now - link.LastHeard > _options.TimeoutSeconds)
        {
            Drop(link, notify: true);
            return;
        }
        // Resend anything unacked for longer than a round trip and a half (at least 80 ms).
        double rto = Math.Max(0.08, link.Rtt * 1.5);
        foreach (var seq in link.Unacked.Keys.ToList())
        {
            var (payload, sentAt, first) = link.Unacked[seq];
            if (now - sentAt < rto)
                continue;
            link.Unacked[seq] = (payload, now, first);
            SendData(link, seq, payload);
        }
        if (link.AckOwed)
        {
            Begin(Kind.Ack, link.Token);
            _writer.U32(link.NextExpected - 1);
            link.AckOwed = false;
            Transmit(link.Address, link);
        }
        // A ping every keepalive interval, busy or idle: it keeps the link alive and measures the round trip.
        if (now - link.LastPing > _options.KeepaliveSeconds)
        {
            link.LastPing = now;
            Begin(Kind.Ping, link.Token);
            _writer.F64(now);
            Transmit(link.Address, link);
        }
    }

    void Receive(double now)
    {
        EndPoint from = new IPEndPoint(IPAddress.Any, 0);
        while (true)
        {
            int n;
            try
            {
                // Not Available: an empty datagram reads as 0 bytes available and would wedge the queue.
                if (!_socket.Poll(0, SelectMode.SelectRead))
                    return;
                n = _socket.ReceiveFrom(_receive, SocketFlags.None, ref from);
            }
            catch (SocketException e) when (e.SocketErrorCode is SocketError.WouldBlock or SocketError.ConnectionReset or SocketError.MessageSize)
            {
                if (e.SocketErrorCode == SocketError.WouldBlock)
                    return;
                continue;
            }
            DatagramsReceived++;
            try
            {
                Handle(_receive.AsSpan(0, n), from, now);
            }
            catch (Exception e) when (e is EndOfStreamException or InvalidDataException)
            {
                // Malformed datagram: ignore it, don't let anyone crash the host with one.
            }
        }
    }

    void Handle(ReadOnlySpan<byte> datagram, EndPoint from, double now)
    {
        var r = new NetReader(datagram);
        var kind = (Kind)r.U8();
        if (kind == Kind.Connect)
        {
            if (_isHost && r.U32() == Magic && r.U16() == Version)
                Accept(from, r.U64(), now);
            return;
        }
        ulong token = r.U64();
        if (kind == Kind.Accept)
        {
            if (!_isHost && !IsConnected && Equals(from, _hostAddress))
            {
                ulong peer = r.U64();
                if (r.U64() != _nonce)
                    return;
                LocalId = new PeerId(peer);
                var host = new Link(PeerId.Host, from, token) { LastHeard = now };
                _byAddress[from] = host;
                _byPeer[PeerId.Host] = host;
                _events.Add(new TransportEvent(TransportEventKind.Connected, PeerId.Host));
            }
            return;
        }
        if (!_byAddress.TryGetValue(from, out var link) || link.Token != token)
            return;
        link.LastHeard = now;
        switch (kind)
        {
            case Kind.Data:
                Acked(link, r.U32(), now);
                uint seq = r.U32();
                var payload = r.Rest().ToArray();
                if (seq == 0)
                    _events.Add(new TransportEvent(TransportEventKind.Data, link.Peer, payload, Delivery.Unreliable));
                else
                    ReceiveReliable(link, seq, payload);
                break;
            case Kind.Ack:
                Acked(link, r.U32(), now);
                break;
            case Kind.Ping:
                double sent = r.F64();
                Begin(Kind.Pong, link.Token);
                _writer.F64(sent);
                Transmit(link.Address, link);
                break;
            case Kind.Pong:
                double sample = now - r.F64();
                if (sample is >= 0 and < 10)
                {
                    link.Rtt = link.Measured ? link.Rtt * 0.8 + sample * 0.2 : sample;
                    link.Measured = true;
                }
                break;
            case Kind.Bye:
                Drop(link, notify: true);
                break;
        }
    }

    void Accept(EndPoint from, ulong nonce, double now)
    {
        if (!_byAddress.TryGetValue(from, out var link))
        {
            // A new face. (A repeated Connect from someone already here just gets the Accept again.)
            var id = new PeerId(_nextPeer++);
            link = new Link(id, from, (ulong)Random.Shared.NextInt64() | 1) { LastHeard = now };
            _byAddress[from] = link;
            _byPeer[id] = link;
            _events.Add(new TransportEvent(TransportEventKind.Connected, id));
        }
        Begin(Kind.Accept, link.Token);
        _writer.U64(link.Peer.Value);
        _writer.U64(nonce);
        Transmit(from, link);
    }

    static void Acked(Link link, uint cumulative, double now)
    {
        foreach (var seq in link.Unacked.Keys.TakeWhile(s => s <= cumulative).ToList())
        {
            var (_, sentAt, first) = link.Unacked[seq];
            // Only unambiguous samples (never resent) feed the round-trip estimate.
            if (sentAt == first)
            {
                link.Rtt = link.Measured ? link.Rtt * 0.875 + (now - sentAt) * 0.125 : now - sentAt;
                link.Measured = true;
            }
            link.Unacked.Remove(seq);
        }
    }

    void ReceiveReliable(Link link, uint seq, byte[] payload)
    {
        link.AckOwed = true;
        if (seq < link.NextExpected)
            return; // a resend we already have
        link.Early[seq] = payload;
        while (link.Early.Remove(link.NextExpected, out var next))
        {
            _events.Add(new TransportEvent(TransportEventKind.Data, link.Peer, next, Delivery.ReliableOrdered));
            link.NextExpected++;
        }
    }

    void Drop(Link link, bool notify)
    {
        _byAddress.Remove(link.Address);
        _byPeer.Remove(link.Peer);
        if (notify)
            _events.Add(new TransportEvent(TransportEventKind.Disconnected, link.Peer));
    }

    public void Disconnect(PeerId peer)
    {
        if (!_byPeer.TryGetValue(peer, out var link))
            return;
        Begin(Kind.Bye, link.Token);
        Transmit(link.Address, link);
        Drop(link, notify: false);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        foreach (var peer in _byPeer.Keys.ToList())
            Disconnect(peer);
        _disposed = true;
        _socket.Dispose();
    }
}
