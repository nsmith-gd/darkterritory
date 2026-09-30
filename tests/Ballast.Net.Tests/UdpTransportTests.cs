using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Ballast.Net;

namespace Ballast.Net.Tests;

/// <summary>Real sockets on localhost. Each test polls until a condition holds or a short deadline passes.</summary>
public class UdpTransportTests
{
    static readonly DatagramOptions Fast = new() { TimeoutSeconds = 2, ConnectSeconds = 2 };

    sealed class Peer(UdpTransport t)
    {
        public readonly UdpTransport T = t;
        public readonly List<TransportEvent> Events = new();
        public void Poll() => T.Poll(Events);
        public IEnumerable<TransportEvent> Data => Events.Where(e => e.Kind == TransportEventKind.Data);
    }

    static void Until(Func<bool> done, params Peer[] peers)
    {
        var clock = Stopwatch.StartNew();
        while (!done())
        {
            Assert.True(clock.Elapsed.TotalSeconds < 5, "timed out");
            foreach (var p in peers)
                p.Poll();
            Thread.Sleep(1);
        }
    }

    static (Peer Host, Peer Client) Pair(DatagramOptions? hostOptions = null, DatagramOptions? clientOptions = null)
    {
        var host = new Peer(UdpTransport.Host(0, hostOptions ?? Fast, IPAddress.Loopback));
        var client = new Peer(UdpTransport.Connect(new IPEndPoint(IPAddress.Loopback, host.T.Port), clientOptions ?? Fast));
        Until(() => client.T.IsConnected && host.Events.Any(e => e.Kind == TransportEventKind.Connected), host, client);
        return (host, client);
    }

    [Fact]
    public void ClientsConnectAndTalkBothWays()
    {
        var (host, client) = Pair();
        using var _ = host.T;
        using var __ = client.T;
        var peer = host.Events.Single(e => e.Kind == TransportEventKind.Connected).Peer;
        Assert.Equal(peer, client.T.LocalId);
        Assert.Contains(client.Events, e => e.Kind == TransportEventKind.Connected && e.Peer == PeerId.Host);

        client.T.Send(PeerId.Host, [1, 2, 3], Delivery.Unreliable);
        host.T.Send(peer, [4, 5], Delivery.ReliableOrdered);
        Until(() => host.Data.Any() && client.Data.Any(), host, client);
        Assert.Equal([1, 2, 3], host.Data.Single().Payload);
        Assert.Equal(peer, host.Data.Single().Peer);
        Assert.Equal([4, 5], client.Data.Single().Payload);
        Assert.Equal(Delivery.ReliableOrdered, client.Data.Single().Delivery);
    }

    [Fact]
    public void ReliableMessagesArriveOnceAndInOrderThroughHeavyLoss()
    {
        var lossy = Fast with { SimulatedLoss = 0.3, Seed = 7 };
        var (host, client) = Pair(lossy, lossy);
        using var _ = host.T;
        using var __ = client.T;
        var peer = client.T.LocalId;
        for (int i = 0; i < 200; i++)
        {
            host.T.Send(peer, BitConverter.GetBytes(i), Delivery.ReliableOrdered);
            client.T.Send(PeerId.Host, BitConverter.GetBytes(i), Delivery.ReliableOrdered);
        }
        Until(() => client.Data.Count() >= 200 && host.Data.Count() >= 200, host, client);
        Assert.Equal(Enumerable.Range(0, 200), client.Data.Select(e => BitConverter.ToInt32(e.Payload)));
        Assert.Equal(Enumerable.Range(0, 200), host.Data.Select(e => BitConverter.ToInt32(e.Payload)));
    }

    [Fact]
    public void LeavingIsReportedAtOnce()
    {
        var (host, client) = Pair();
        using var _ = host.T;
        client.T.Dispose();
        Until(() => host.Events.Any(e => e.Kind == TransportEventKind.Disconnected), host);
    }

    [Fact]
    public void ASilentPeerTimesOut()
    {
        var (host, client) = Pair(Fast with { TimeoutSeconds = 0.3 });
        using var _ = host.T;
        using var __ = client.T;
        // The client stops polling: no keepalives, no acks. It's as if its machine froze.
        Until(() => host.Events.Any(e => e.Kind == TransportEventKind.Disconnected), host);
    }

    [Fact]
    public void ConnectingToNobodyGivesUp()
    {
        int port;
        using (var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
            port = ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
        var client = new Peer(UdpTransport.Connect(new IPEndPoint(IPAddress.Loopback, port), Fast with { ConnectSeconds = 0.3 }));
        using var _ = client.T;
        Until(() => client.Events.Any(e => e.Kind == TransportEventKind.Disconnected), client);
        Assert.False(client.T.IsConnected);
    }

    [Fact]
    public void GarbageDoesNotHurtTheHost()
    {
        var (host, client) = Pair();
        using var _ = host.T;
        using var __ = client.T;
        using var vandal = new UdpClient();
        var target = new IPEndPoint(IPAddress.Loopback, host.T.Port);
        var rng = new Random(3);
        for (int i = 0; i < 200; i++)
        {
            var junk = new byte[rng.Next(0, 64)];
            rng.NextBytes(junk);
            vandal.Send(junk, junk.Length, target);
        }
        client.T.Send(PeerId.Host, [9], Delivery.ReliableOrdered);
        Until(() => host.Data.Any(), host, client);
        Assert.Single(host.Events, e => e.Kind == TransportEventKind.Connected);
    }

    [Fact]
    public void PingsMeasureTheRoundTrip()
    {
        var quick = Fast with { KeepaliveSeconds = 0.02 };
        var (host, client) = Pair(quick, quick);
        using var _ = host.T;
        using var __ = client.T;
        var clock = Stopwatch.StartNew();
        // At least 0.3 s of keepalives, and on until the smoothed round trip has settled (up to 3 s): a loopback ping is
        // well under a millisecond, but on a CI box whose cores the other test assemblies hold at startup the first few
        // can take tens, and the estimate carries them for a while.
        bool Settled() => client.T.RoundTrip(PeerId.Host) < 0.05 && host.T.RoundTrip(client.T.LocalId) < 0.05;
        Until(() => clock.Elapsed.TotalSeconds > 0.3 && (Settled() || clock.Elapsed.TotalSeconds > 3), host, client);
        Assert.InRange(client.T.RoundTrip(PeerId.Host), 0, 0.05);
        Assert.InRange(host.T.RoundTrip(client.T.LocalId), 0, 0.05);
    }

    [Fact]
    public void OversizedMessagesAreRefused()
    {
        var (host, client) = Pair();
        using var _ = host.T;
        using var __ = client.T;
        Assert.Throws<ArgumentException>(() => client.T.Send(PeerId.Host, new byte[UdpTransport.MaxPayload + 1], Delivery.Unreliable));
    }
}
