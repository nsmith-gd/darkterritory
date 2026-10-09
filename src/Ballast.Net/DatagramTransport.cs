using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Ballast.Net;

/// <summary>Knobs for a <see cref="DatagramTransport{TAddress}"/>. Defaults suit play; tests shorten them.</summary>
public sealed record DatagramOptions
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
/// Moves raw datagrams to and from addresses: what a <see cref="DatagramTransport{TAddress}"/> runs over.
/// A UDP socket (addresses are IP endpoints), Steam's P2P messages or EOS P2P (addresses are user ids).
/// Best effort: a datagram may be dropped or reordered, and is never fragmented or merged.
/// </summary>
public interface IDatagramCarrier<TAddress> : IDisposable where TAddress : notnull
{
    void Send(TAddress to, ReadOnlySpan<byte> datagram);
    /// <summary>The next datagram that has arrived, or false when there are none waiting.</summary>
    bool TryReceive(Span<byte> buffer, out int length, [MaybeNullWhen(false)] out TAddress from);
    /// <summary>The transport has let this peer go (a carrier with sessions of its own can close them).</summary>
    void Forget(TAddress peer) { }
    /// <summary>
    /// How the carrier reaches a peer, as far as it knows (netcode-audit.md gap 3): relayed or direct, and a platform's own
    /// ping and quality. Null when it can't say (no session yet).
    /// </summary>
    CarrierLink? Describe(TAddress peer) => null;
}

/// <summary>How a carrier reaches a peer (netcode-audit.md gap 3; spec E "ping visibility is load-bearing").</summary>
/// <param name="Network">Whose network carries it ("Steam"), or empty for the internet itself (UDP).</param>
/// <param name="Relayed">Through the network's relays rather than straight to the peer.</param>
/// <param name="PingMs">The network's own round trip, when it measures one.</param>
/// <param name="Quality">The network's own share of packets arriving (1 = none lost), when it measures one.</param>
public sealed record CarrierLink(string Network, bool Relayed, int? PingMs = null, float? Quality = null);

/// <summary>What a transport knows about its connections.</summary>
public interface IConnectionInfo
{
    /// <summary>A host is always up; a client is up once the host has accepted it.</summary>
    bool IsConnected { get; }
    /// <summary>Round-trip estimate to a peer, seconds.</summary>
    double RoundTrip(PeerId peer);
    /// <summary>How a peer is reached (<see cref="IDatagramCarrier{TAddress}.Describe"/>); null when nobody can say.</summary>
    CarrierLink? Via(PeerId peer) => null;
}

/// <summary>
/// The game's own connection protocol over any <see cref="IDatagramCarrier{TAddress}"/>, so a direct-IP game and a
/// Steam game behave identically (ARCHITECTURE §4). Polled once per tick, no threads.
/// <list type="bullet">
/// <item>Handshake: the client repeats Connect until the host Accepts with its peer id and a session token.
/// The token rides on every datagram, so a stray or stale sender can't inject into a session.</item>
/// <item>Unreliable: one datagram per message, dropped if lost (snapshots, input, voice).</item>
/// <item>Reliable-ordered: sequence numbers, a cumulative ack on every datagram, resend until acked,
/// in-order delivery with a reorder buffer (welcome, RPCs).</item>
/// <item>Ping/pong every keepalive for the round trip, and a timeout on silence.</item>
/// </list>
/// Messages must fit one datagram (<see cref="MaxPayload"/>); nothing here fragments.
/// </summary>
public class DatagramTransport<TAddress> : ITransport, IConnectionInfo where TAddress : notnull
{
    public const int MaxPayload = 1200;
    const uint Magic = 0x31425444; // "DTB1"
    const ushort Version = 1;

    enum Kind : byte { Connect = 1, Accept = 2, Data = 3, Ack = 4, Ping = 5, Bye = 6, Pong = 7 }

    readonly IDatagramCarrier<TAddress> _carrier;
    readonly bool _ownsCarrier;
    readonly DatagramOptions _options;
    readonly bool _isHost;
    readonly Dictionary<TAddress, Link> _byAddress = new();
    readonly Dictionary<PeerId, Link> _byPeer = new();
    readonly List<TransportEvent> _events = new();
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly byte[] _receive = new byte[2048];
    readonly NetWriter _writer = new();
    readonly Random _loss;
    readonly TAddress? _hostAddress;
    readonly ulong _nonce;
    ulong _nextPeer = 1;
    double _connectStarted, _lastConnectSent = double.NegativeInfinity;
    bool _disposed, _gaveUp;

    /// <summary>A host, listening.</summary>
    /// <param name="ownsCarrier">Dispose the carrier with the transport (a platform's shared carrier isn't).</param>
    protected DatagramTransport(IDatagramCarrier<TAddress> carrier, DatagramOptions? options, bool ownsCarrier = true)
        : this(carrier, options, true, default, ownsCarrier)
    {
    }

    /// <summary>A client, connecting to <paramref name="host"/>.</summary>
    protected DatagramTransport(IDatagramCarrier<TAddress> carrier, DatagramOptions? options, TAddress host, bool ownsCarrier = true)
        : this(carrier, options, false, host, ownsCarrier)
    {
    }

    DatagramTransport(IDatagramCarrier<TAddress> carrier, DatagramOptions? options, bool isHost, TAddress? hostAddress, bool ownsCarrier)
    {
        _carrier = carrier;
        _ownsCarrier = ownsCarrier;
        _options = options ?? new DatagramOptions();
        _isHost = isHost;
        _hostAddress = hostAddress;
        _loss = new Random(_options.Seed);
        _nonce = (ulong)Random.Shared.NextInt64();
        LocalId = _isHost ? PeerId.Host : new PeerId(ulong.MaxValue);
        _connectStarted = Now;
    }

    public PeerId LocalId { get; private set; }
    public bool IsConnected => _isHost || _byPeer.ContainsKey(PeerId.Host);
    /// <summary>Round-trip estimate to a peer, seconds (smoothed from reliable acks and pings).</summary>
    public double RoundTrip(PeerId peer) => _byPeer.TryGetValue(peer, out var l) ? l.Rtt : 0;
    public CarrierLink? Via(PeerId peer) => _byPeer.TryGetValue(peer, out var l) ? _carrier.Describe(l.Address) : null;
    /// <summary>The carrier address of a connected peer (its endpoint, or its platform user id).</summary>
    public bool TryGetAddress(PeerId peer, [MaybeNullWhen(false)] out TAddress address)
    {
        address = _byPeer.TryGetValue(peer, out var l) ? l.Address : default;
        return address is not null;
    }
    public int DatagramsSent { get; private set; }
    public int DatagramsReceived { get; private set; }

    double Now => _clock.Elapsed.TotalSeconds;

    sealed class Link(PeerId peer, TAddress address, ulong token)
    {
        public readonly PeerId Peer = peer;
        public readonly TAddress Address = address;
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

    void Transmit(TAddress to, Link? link)
    {
        if (link is not null)
            link.LastSent = Now;
        if (_options.SimulatedLoss > 0 && _loss.NextDouble() < _options.SimulatedLoss)
            return;
        _carrier.Send(to, _writer.Written);
        DatagramsSent++;
    }

    // When this transport was last polled (NaN before the first): see Poll.
    double _lastPolled = double.NaN;

    public void Poll(List<TransportEvent> into)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        double now = Now;
        // A poll long after the last one means this process stood still (a night's scene loading before its first frame, a
        // debugger): we weren't listening, so that time is nobody's silence, and every link's clock moves on by it. A peer that
        // really has gone still times out the usual timeout after. The director, 9 Oct: a night with bots began with them all
        // gone (the host's first poll after a long load found its in-process bots "silent" and dropped them).
        if (!double.IsNaN(_lastPolled) && now - _lastPolled > Math.Min(2 * _options.KeepaliveSeconds, _options.TimeoutSeconds / 2))
            foreach (var link in _byPeer.Values)
                link.LastHeard += now - _lastPolled;
        _lastPolled = now;
        Receive(now);
        if (!_isHost && !IsConnected && !_gaveUp)
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
        while (_carrier.TryReceive(_receive, out int n, out var from))
        {
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

    void Handle(ReadOnlySpan<byte> datagram, TAddress from, double now)
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
            if (!_isHost && !IsConnected && !_gaveUp && EqualityComparer<TAddress>.Default.Equals(from, _hostAddress))
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

    void Accept(TAddress from, ulong nonce, double now)
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
        // A client's link to its host, once gone, stays gone: it never dials again on its own. (It did, within the first
        // ConnectSeconds of its life, and the host welcomed the silent second connection as a phantom crewmate after its
        // greeting wait, taking a place against the crew cap: notes 253, 254.) Coming back is a new transport.
        if (!_isHost && link.Peer == PeerId.Host)
            _gaveUp = true;
        if (!notify)
            return;
        // Not on our own Disconnect: closing the carrier's session there could lose the Bye still in flight.
        _carrier.Forget(link.Address);
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
        if (_ownsCarrier)
            _carrier.Dispose();
        GC.SuppressFinalize(this);
    }
}
