namespace Ballast.Net;

/// <summary>Simulated link quality, applied per direction.</summary>
public sealed record LinkConditions(double LatencySeconds = 0, double JitterSeconds = 0, double LossRate = 0)
{
    /// <summary>Note 549: what the host's upload carries in all, kbit/s; 0 for no cap. Past it, the host's datagrams are dropped.</summary>
    public double HostUpKbps { get; init; }
    /// <summary>Note 549: what each client's downlink carries, kbit/s; 0 for no cap.</summary>
    public double DownKbps { get; init; }
    public static readonly LinkConditions Perfect = new();
    /// <summary>A bad-but-playable host connection; the agent harness's default stress profile.</summary>
    public static readonly LinkConditions Rough = new(0.090, 0.020, 0.03);
}

/// <summary>
/// In-process network with a virtual clock and deterministic, seeded packet loss and latency.
/// Lets one process run a host plus any number of clients (players or bots) with no sockets,
/// so netcode can be tested exhaustively and faster than real time.
/// </summary>
public sealed class LoopbackNetwork
{
    readonly Dictionary<PeerId, Endpoint> _endpoints = new();
    readonly PriorityQueue<Packet, (double, long)> _inFlight = new();
    readonly Dictionary<(PeerId, PeerId), double> _lastReliableArrival = new();
    readonly Random _random;
    long _sequence;
    ulong _nextId = 1;

    public LoopbackNetwork(int seed = 1, LinkConditions? conditions = null)
    {
        _random = new Random(seed);
        Conditions = conditions ?? LinkConditions.Perfect;
    }

    public LinkConditions Conditions { get; set; }
    public double Now { get; private set; }

    public ITransport CreateHost() => Add(PeerId.Host);

    public ITransport CreateClient()
    {
        var client = Add(new PeerId(_nextId++));
        if (!_endpoints.ContainsKey(PeerId.Host))
            throw new InvalidOperationException("create the host first");
        _endpoints[PeerId.Host].Pending.Add(new TransportEvent(TransportEventKind.Connected, client.LocalId));
        ((Endpoint)client).Pending.Add(new TransportEvent(TransportEventKind.Connected, PeerId.Host));
        return client;
    }

    /// <summary>Advances the virtual clock and delivers packets whose arrival time has passed.</summary>
    public void Advance(double seconds)
    {
        Now += seconds;
        while (_inFlight.TryPeek(out var packet, out var key) && key.Item1 <= Now)
        {
            _inFlight.Dequeue();
            if (_endpoints.TryGetValue(packet.To, out var ep) && !ep.Disposed)
                ep.Pending.Add(new TransportEvent(TransportEventKind.Data, packet.From, packet.Payload, packet.Delivery));
        }
    }

    Endpoint Add(PeerId id)
    {
        var ep = new Endpoint(this, id);
        _endpoints.Add(id, ep);
        return ep;
    }

    readonly Dictionary<PeerId, (double Allowance, double At)> _down = new();
    (double Allowance, double At) _up;

    /// <summary>A capped pipe (note 553): a bucket refilled at the cap, a quarter second deep; a datagram over it is dropped.</summary>
    bool Fits((double Allowance, double At) bucket, double kbps, int bytes, out (double Allowance, double At) after)
    {
        double perSecond = kbps * 125;
        double allowance = Math.Min(perSecond / 4, bucket.Allowance + (Now - bucket.At) * perSecond);
        bool fits = allowance >= bytes;
        after = (fits ? allowance - bytes : allowance, Now);
        return fits;
    }

    void Enqueue(PeerId from, PeerId to, ReadOnlySpan<byte> payload, Delivery delivery)
    {
        var c = Conditions;
        if (delivery == Delivery.Unreliable && _random.NextDouble() < c.LossRate)
            return;
        if (from == PeerId.Host)
        {
            if (c.HostUpKbps > 0)
            {
                bool fits = Fits(_up, c.HostUpKbps, payload.Length, out _up);
                if (!fits)
                    return;
            }
            if (c.DownKbps > 0)
            {
                bool fits = Fits(_down.GetValueOrDefault(to, (c.DownKbps * 125 / 4, Now)), c.DownKbps, payload.Length, out var after);
                _down[to] = after;
                if (!fits)
                    return;
            }
        }
        // Reliable traffic pays for loss with retransmission delay instead of being dropped.
        double retransmits = delivery == Delivery.ReliableOrdered && c.LossRate > 0 && _random.NextDouble() < c.LossRate ? 2 : 0;
        double jitter = delivery == Delivery.ReliableOrdered ? 0 : (_random.NextDouble() * 2 - 1) * c.JitterSeconds;
        double arrival = Now + Math.Max(0, c.LatencySeconds + jitter) + retransmits * c.LatencySeconds;
        // Ordered delivery: never arrive before an earlier reliable packet on the same link.
        if (delivery == Delivery.ReliableOrdered)
        {
            var link = (from, to);
            if (_lastReliableArrival.TryGetValue(link, out var last))
                arrival = Math.Max(arrival, last);
            _lastReliableArrival[link] = arrival;
        }
        _inFlight.Enqueue(new Packet(from, to, payload.ToArray(), delivery), (arrival, _sequence++));
    }

    void Drop(PeerId a, PeerId b)
    {
        foreach (var (id, other) in new[] { (a, b), (b, a) })
            if (_endpoints.TryGetValue(id, out var ep) && !ep.Disposed)
                ep.Pending.Add(new TransportEvent(TransportEventKind.Disconnected, other));
    }

    readonly record struct Packet(PeerId From, PeerId To, byte[] Payload, Delivery Delivery);

    sealed class Endpoint(LoopbackNetwork net, PeerId id) : ITransport
    {
        public readonly List<TransportEvent> Pending = new();
        public bool Disposed;

        public PeerId LocalId => id;

        public void Send(PeerId to, ReadOnlySpan<byte> payload, Delivery delivery)
        {
            ObjectDisposedException.ThrowIf(Disposed, this);
            net.Enqueue(id, to, payload, delivery);
        }

        public void Poll(List<TransportEvent> into)
        {
            into.AddRange(Pending);
            Pending.Clear();
        }

        public void Disconnect(PeerId peer) => net.Drop(id, peer);

        public void Dispose()
        {
            if (Disposed)
                return;
            if (id != PeerId.Host)
                net.Drop(id, PeerId.Host);
            Disposed = true;
        }
    }
}
