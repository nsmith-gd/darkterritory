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
        var advert = new LanAdvert("darkterritory", 3, 27450, "Nick's PC", "FRONTIER-7, 6 CARS", 2) { Name = "NICK'S RUN", Max = 12, Tier = "Frontier" };
        while (browser.Games.Count == 0 && clock.Elapsed.TotalSeconds < 3)
        {
            beacon.Tick(clock.Elapsed.TotalSeconds, advert);
            browser.Poll(clock.Elapsed.TotalSeconds);
            Thread.Sleep(20);
        }
        var game = Assert.Single(browser.Games);
        Assert.Equal(new IPEndPoint(IPAddress.Loopback, 27450), game.Address);
        Assert.Equal("Nick's PC", game.Host);
        Assert.Equal(2, game.Aboard);
        Assert.Equal(3, game.Protocol);
        Assert.Equal(("NICK'S RUN", 12, "Frontier"), (game.Name, game.Max, game.Tier));
        // Gone quiet, it drops off the list.
        browser.Poll(clock.Elapsed.TotalSeconds + 5);
        Assert.Empty(browser.Games);
    }

    [Fact]
    public void TheBrowserMeasuresItsPingToEachHost()
    {
        // The user's playtest ("I see active lobbies I can join and then what my ping is"): a round trip to the host's
        // beacon and back, timed, not a guess. On loopback it's well under a frame.
        const int port = 27462;
        using var browser = new LanBrowser(port);
        using var beacon = new LanBeacon(port, [IPAddress.Loopback]);
        var advert = new LanAdvert("darkterritory", 3, 27450, "nick", "FRONTIER-7", 1);
        var clock = Stopwatch.StartNew();
        while (browser.Games is not [{ PingMs: not null }] && clock.Elapsed.TotalSeconds < 5)
        {
            beacon.Tick(clock.Elapsed.TotalSeconds, advert);
            browser.Poll(clock.Elapsed.TotalSeconds);
            Thread.Sleep(5);
        }
        var game = Assert.Single(browser.Games);
        Assert.NotNull(game.PingMs);
        Assert.InRange(game.PingMs.Value, 0.0, 200.0);
        Assert.Equal(IPAddress.Loopback, game.Beacon!.Address);
    }

    [Fact]
    public void AnOlderBeaconStillReads()
    {
        // The first seven fields are the original beacon's; what came after (name, size, tier, lobby) is optional.
        var game = LanBeacon.Parse("BALLAST-LAN/1\ndarkterritory\n3\n27450\n2\nnick\nFRONTIER-7"u8, IPAddress.Loopback);
        Assert.NotNull(game);
        Assert.Equal(("", 0, ""), (game.Name, game.Max, game.Tier));
    }

    [Fact]
    public void AnythingElseOnThePortIsIgnored()
    {
        Assert.Null(LanBeacon.Parse("hello"u8, IPAddress.Loopback));
        Assert.Null(LanBeacon.Parse("BALLAST-LAN/1\ngame\nx\n1\n1\nh\nn"u8, IPAddress.Loopback));
    }
}
