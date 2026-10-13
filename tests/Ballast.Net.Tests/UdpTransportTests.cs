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

    static void Until(Func<bool> done, params Peer[] peers) => Until(done, sleep: true, peers);

    /// <param name="sleep">False: poll flat out. On Windows <c>Thread.Sleep(1)</c> is a 15.6 ms timer tick, so a round trip
    /// measured between sleeping polls is mostly the test's own sleeps (a 71 ms "loopback ping" on a busy runner).</param>
    static void Until(Func<bool> done, bool sleep, params Peer[] peers)
    {
        var clock = Stopwatch.StartNew();
        while (!done())
        {
            Assert.True(clock.Elapsed.TotalSeconds < 5, "timed out");
            foreach (var p in peers)
                p.Poll();
            if (sleep)
                Thread.Sleep(1);
            else
                Thread.Yield();
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
    public void AWholeProcessStandingStillDropsNobody()
    {
        // The host and its bots in one process, none of them polled while a night's scene loads (the director, 9 Oct: a night
        // with bots began with them all dropped). Longer than the timeout, but nobody was listening: both links live on.
        var (host, client) = Pair(Fast with { TimeoutSeconds = 0.3 }, Fast with { TimeoutSeconds = 0.3 });
        using var _ = host.T;
        using var __ = client.T;
        Thread.Sleep(800);
        // The bots step before the host on the first frame (NetPlaySession.Step), so a client hears the stand first. Polled for
        // half the timeout by the clock, not a count of sleeps: on Windows twenty 5 ms sleeps are twenty 15.6 ms timer ticks,
        // 0.31 s, past the 0.3 s timeout, and the next keepalive isn't due till a second after the last (main's Windows job
        // failed here on every PR).
        var after = Stopwatch.StartNew();
        while (after.Elapsed.TotalSeconds < 0.15)
        {
            client.Poll();
            host.Poll();
            Thread.Sleep(1);
        }
        Assert.DoesNotContain(host.Events, e => e.Kind == TransportEventKind.Disconnected);
        Assert.DoesNotContain(client.Events, e => e.Kind == TransportEventKind.Disconnected);
        Assert.True(client.T.IsConnected);
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
        // What's asserted is that the trip was measured at all (an unmeasured link reads the 100 ms it starts with), not how
        // fast a busy runner's loopback is: a Windows runner with the other assemblies starting read 63 ms (#644's CI).
        const double measured = 0.09;
        bool Settled() => client.T.RoundTrip(PeerId.Host) < measured && host.T.RoundTrip(client.T.LocalId) < measured;
        Until(() => clock.Elapsed.TotalSeconds > 0.3 && (Settled() || clock.Elapsed.TotalSeconds > 3), sleep: false, host, client);
        Assert.InRange(client.T.RoundTrip(PeerId.Host), 0, measured);
        Assert.InRange(host.T.RoundTrip(client.T.LocalId), 0, measured);
    }

    [Fact]
    public void APumpedLinkOutlivesAFrozenFrameLoopAndMeasuresItsOwnPing()
    {
        // Note 532: the app's transports are kept by a thread of their own, so a frame loop that stands still longer than the
        // timeout (a village loading, the film compiling) neither drops its peers nor is dropped by them, and the round trip is
        // the link's, not the frames'.
        var quick = Fast with { TimeoutSeconds = 0.3, KeepaliveSeconds = 0.02 };
        var (host, client) = Pair(quick, quick);
        using var _ = host.T;
        using var __ = client.T;
        host.T.StartPump(2);
        client.T.StartPump(2);
        Assert.True(host.T.Pumped && client.T.Pumped);
        // Nobody polls for three timeouts: the pumps ping and answer throughout.
        Thread.Sleep(900);
        host.Poll();
        client.Poll();
        Assert.DoesNotContain(host.Events, e => e.Kind == TransportEventKind.Disconnected);
        Assert.DoesNotContain(client.Events, e => e.Kind == TransportEventKind.Disconnected);
        Assert.True(client.T.IsConnected);
        // The pings went on while the loop stood still: a measured, loopback-sized round trip, not a 900 ms one.
        Assert.InRange(client.T.RoundTrip(PeerId.Host), 0, 0.1);
        // And the link still carries: a reliable message across, both ways, read on the next polls.
        var peer = host.Events.Single(e => e.Kind == TransportEventKind.Connected).Peer;
        client.T.Send(PeerId.Host, [7], Delivery.ReliableOrdered);
        host.T.Send(peer, [8], Delivery.ReliableOrdered);
        Until(() => host.Data.Any(d => d.Payload![0] == 7) && client.Data.Any(d => d.Payload![0] == 8), host, client);
        // A peer that really stops (its pump too) is still dropped in the usual time.
        client.T.Dispose();
        var clock = Stopwatch.StartNew();
        Until(() => host.Events.Any(e => e.Kind == TransportEventKind.Disconnected), host);
        Assert.True(clock.Elapsed.TotalSeconds < 2);
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
