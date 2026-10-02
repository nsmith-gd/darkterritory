using System.Diagnostics;
using System.Net;
using Ballast.Net;

namespace Ballast.Net.Tests;

/// <summary>T116 ("how is she supposed to join if we're on the same wifi?"): a hosted game is found on the local network.</summary>
public class LanBeaconTests
{
    [Fact]
    public void ABrowserHearsTheHostsBeaconAndWhereToConnect()
    {
        const int port = 27461;
        using var browser = new LanBrowser(port);
        Assert.Null(browser.Error);
        using var beacon = new LanBeacon(port, [IPAddress.Loopback]);
        var clock = Stopwatch.StartNew();
        while (browser.Games.Count == 0 && clock.Elapsed.TotalSeconds < 3)
        {
            beacon.Tick(clock.Elapsed.TotalSeconds, "darkterritory", 3, 27450, "Nick's PC", "FRONTIER-7, 6 CARS", 2);
            browser.Poll(clock.Elapsed.TotalSeconds);
            Thread.Sleep(20);
        }
        var game = Assert.Single(browser.Games);
        Assert.Equal(new IPEndPoint(IPAddress.Loopback, 27450), game.Address);
        Assert.Equal("Nick's PC", game.Host);
        Assert.Equal(2, game.Aboard);
        Assert.Equal(3, game.Protocol);
        // Gone quiet, it drops off the list.
        browser.Poll(clock.Elapsed.TotalSeconds + 5);
        Assert.Empty(browser.Games);
    }

    [Fact]
    public void AnythingElseOnThePortIsIgnored()
    {
        Assert.Null(LanBeacon.Parse("hello"u8, IPAddress.Loopback));
        Assert.Null(LanBeacon.Parse("BALLAST-LAN/1\ngame\nx\n1\n1\nh\nn"u8, IPAddress.Loopback));
    }
}
