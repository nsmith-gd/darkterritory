using Ballast.Net;

namespace Ballast.Net.Tests;

public class LoopbackNetworkTests
{
    static List<TransportEvent> Drain(ITransport t)
    {
        var list = new List<TransportEvent>();
        t.Poll(list);
        return list;
    }

    [Fact]
    public void ClientAndHostSeeEachOtherConnect()
    {
        var net = new LoopbackNetwork();
        var host = net.CreateHost();
        var client = net.CreateClient();
        Assert.Contains(Drain(host), e => e.Kind == TransportEventKind.Connected && e.Peer == client.LocalId);
        Assert.Contains(Drain(client), e => e.Kind == TransportEventKind.Connected && e.Peer == PeerId.Host);
    }

    [Fact]
    public void PacketsArriveAfterLatency()
    {
        var net = new LoopbackNetwork(conditions: new LinkConditions(LatencySeconds: 0.1));
        var host = net.CreateHost();
        var client = net.CreateClient();
        Drain(host);
        client.Send(PeerId.Host, [1, 2, 3], Delivery.Unreliable);

        net.Advance(0.05);
        Assert.Empty(Drain(host));
        net.Advance(0.06);
        var e = Assert.Single(Drain(host));
        Assert.Equal([1, 2, 3], e.Payload);
    }

    [Fact]
    public void ReliableOrderedSurvivesLossAndStaysInOrder()
    {
        var net = new LoopbackNetwork(seed: 7, new LinkConditions(0.05, 0.02, 0.3));
        var host = net.CreateHost();
        var client = net.CreateClient();
        Drain(host);
        for (byte i = 0; i < 100; i++)
            client.Send(PeerId.Host, [i], Delivery.ReliableOrdered);
        net.Advance(1);
        var got = Drain(host).Select(e => e.Payload![0]).ToArray();
        Assert.Equal(Enumerable.Range(0, 100).Select(i => (byte)i), got);
    }

    [Fact]
    public void UnreliableLossIsSeededAndRoughlyMatchesRate()
    {
        int Received(int seed)
        {
            var net = new LoopbackNetwork(seed, new LinkConditions(LossRate: 0.25));
            var host = net.CreateHost();
            var client = net.CreateClient();
            Drain(host);
            for (int i = 0; i < 2000; i++)
                client.Send(PeerId.Host, [0], Delivery.Unreliable);
            net.Advance(1);
            return Drain(host).Count;
        }
        Assert.Equal(Received(3), Received(3));
        Assert.InRange(Received(3), 1400, 1600);
    }

    [Fact]
    public void DisposingClientNotifiesHost()
    {
        var net = new LoopbackNetwork();
        var host = net.CreateHost();
        var client = net.CreateClient();
        Drain(host);
        client.Dispose();
        Assert.Contains(Drain(host), e => e.Kind == TransportEventKind.Disconnected && e.Peer == client.LocalId);
    }
}
