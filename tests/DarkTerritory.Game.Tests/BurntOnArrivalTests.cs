using System.Net;
using Ballast;
using DarkTerritory.Sim;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Towns;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// A town burnt on an earlier visit shows it as you arrive (queue #344, ARCHITECTURE §8 note 612; note 608's not-yet): the
/// campaign's memory of a town's burnt houses goes into the night, so a terminus the crew's burnt before is built with
/// them, on the host and on every joiner.
/// </summary>
public class BurntOnArrivalTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly IReadOnlyList<string> Identities = LineGenContent.Cached(Content).Config.Tiers.Fortress.Identities;

    [Fact]
    public void TownAtRememberedCarriesOnlyItsOwnBurntCount()
    {
        var town = new TownAt("Fort Boudreau", "coal", 1234);
        var burnt = new Dictionary<string, int> { [town.Key] = 2, ["Grieve/9"] = 1 };
        Assert.Equal(town with { Burnt = 2 }, TownAt.Remembered(town, burnt));
        Assert.Equal(town, TownAt.Remembered(town, new Dictionary<string, int> { ["Grieve/9"] = 1 }));
        Assert.Equal(town, TownAt.Remembered(town, null));
        Assert.Null(TownAt.Remembered(null, burnt));
    }

    [Fact]
    public void TheHostAndAJoinerBuildTheTerminusWithItsBurntHouses()
    {
        // Tonight's terminus, as the campaign remembers it: two of its houses burnt on an earlier visit.
        var whole = NetPlaySession.HostGame(Content, new SessionSetup(Route: "frontier:7", Cars: 4, Enemies: false), port: null);
        var terminus = Assert.IsType<TownAt>(TownAt.Terminus(whole.Route!, Identities));
        int wholeBurnt = whole.World.Arrival!.Plan.Houses.Count(h => h.Kind == HouseKind.Burnt);
        var harmed = TownGenerator.Harmed(whole.World.Arrival.Plan, 2).Select(h => h.Id).ToHashSet();
        whole.Dispose();

        var setup = new SessionSetup(Route: "frontier:7", Cars: 4, Enemies: false) { Burnt = new Dictionary<string, int> { [terminus.Key] = 2 } };
        using var host = NetPlaySession.HostGame(Content, setup, port: 0);
        using var joiner = NetPlaySession.Join(Content, new IPEndPoint(IPAddress.Loopback, host.Port), () => host.Step(default));
        foreach (var night in new[] { host, joiner })
        {
            var arrival = night.World.Arrival!;
            Assert.Equal(2, arrival.Plan.Burnt);
            Assert.Equal(wholeBurnt + 2, arrival.Plan.Houses.Count(h => h.Kind == HouseKind.Burnt));
            Assert.All(harmed, id => Assert.Equal(HouseKind.Burnt, arrival.Plan.Houses[id].Kind));
            // Its departure town is its own: tonight's start isn't one the crew's burnt.
            Assert.Equal(0, night.World.Town!.Plan.Burnt);
        }
        // The joiner had it from the host's setup.
        Assert.Equal(2, joiner.Setup.Burnt![terminus.Key]);
    }
}
