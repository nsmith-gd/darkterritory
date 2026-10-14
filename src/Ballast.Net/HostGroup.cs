namespace Ballast.Net;

/// <summary>
/// One host listening on several transports at once: Steam for friends, UDP for LAN, direct IP and the host's
/// own player on localhost. Each inner peer gets a peer id of its own here, so the session above sees one crew.
/// </summary>
public sealed class HostGroup : ITransport, IConnectionInfo
{
    readonly ITransport[] _inner;
    readonly Dictionary<PeerId, (int Transport, PeerId Peer)> _outward = new();
    readonly Dictionary<(int Transport, PeerId Peer), PeerId> _inward = new();
    readonly List<TransportEvent> _poll = new();
    ulong _next = 1;

    public HostGroup(params ITransport[] inner)
    {
        if (inner.Length == 0)
            throw new ArgumentException("a host group needs at least one transport", nameof(inner));
        _inner = inner;
    }

    public IReadOnlyList<ITransport> Transports => _inner;
    public PeerId LocalId => PeerId.Host;
    public bool IsConnected => true;

    /// <summary>Which transport a peer came in on, and its id there.</summary>
    public (ITransport Transport, PeerId Peer)? Route(PeerId peer) =>
        _outward.TryGetValue(peer, out var r) ? (_inner[r.Transport], r.Peer) : null;

    public double RoundTrip(PeerId peer) =>
        Route(peer) is ({ } t, var p) && t is IConnectionInfo info ? info.RoundTrip(p) : 0;

    public CarrierLink? Via(PeerId peer) =>
        Route(peer) is ({ } t, var p) && t is IConnectionInfo info ? info.Via(p) : null;

    public void Send(PeerId to, ReadOnlySpan<byte> payload, Delivery delivery)
    {
        if (_outward.TryGetValue(to, out var r))
            _inner[r.Transport].Send(r.Peer, payload, delivery);
    }

    public void Poll(List<TransportEvent> into)
    {
        for (int i = 0; i < _inner.Length; i++)
        {
            _poll.Clear();
            _inner[i].Poll(_poll);
            foreach (var e in _poll)
            {
                var key = (i, e.Peer);
                if (e.Kind == TransportEventKind.Connected && !_inward.ContainsKey(key))
                {
                    var id = new PeerId(_next++);
                    _inward[key] = id;
                    _outward[id] = key;
                }
                if (!_inward.TryGetValue(key, out var outer))
                    continue;
                into.Add(e with { Peer = outer });
                if (e.Kind == TransportEventKind.Disconnected)
                    Forget(outer);
            }
        }
    }

    public void Disconnect(PeerId peer)
    {
        if (!_outward.TryGetValue(peer, out var r))
            return;
        _inner[r.Transport].Disconnect(r.Peer);
        Forget(peer);
    }

    void Forget(PeerId outer)
    {
        if (_outward.Remove(outer, out var key))
            _inward.Remove(key);
    }

    public void Dispose()
    {
        foreach (var t in _inner)
            t.Dispose();
    }
}
