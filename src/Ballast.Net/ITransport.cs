namespace Ballast.Net;

public enum Delivery : byte
{
    /// <summary>May be dropped or reordered. Snapshots, input, voice.</summary>
    Unreliable,
    /// <summary>Arrives exactly once, in order. RPCs, lobby state, chat.</summary>
    ReliableOrdered,
}

public readonly record struct PeerId(ulong Value)
{
    public static readonly PeerId Host = new(0);
    public override string ToString() => $"peer:{Value}";
}

public enum TransportEventKind : byte { Connected, Disconnected, Data }

public readonly record struct TransportEvent(TransportEventKind Kind, PeerId Peer, byte[]? Payload = null, Delivery Delivery = Delivery.Unreliable);

/// <summary>
/// Message transport between host and clients. Implementations: <see cref="LoopbackNetwork"/>
/// (tests, bots, the agent harness), Steam Networking Sockets (Steam builds), and a
/// non-Steam backend for itch.io. Game code never talks to a concrete transport.
/// </summary>
public interface ITransport : IDisposable
{
    PeerId LocalId { get; }
    void Send(PeerId to, ReadOnlySpan<byte> payload, Delivery delivery);
    /// <summary>Drains events that have arrived. Call once per tick.</summary>
    void Poll(List<TransportEvent> into);
    void Disconnect(PeerId peer);
}
