using Ballast;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Towns;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Town to town (ARCHITECTURE §8 note 591; the director, 9 Oct 2026: "the town that we end in in a run should be the same
/// town that we begin in in the next run ... so that you feel like you are still living up this fantasy of I'm going from
/// town to town to town"): a night delivered to its terminus leaves the crew in that town, and the next night departs from
/// it, the same name, streets and people, on whatever line the board's contract runs.
/// </summary>
public class TownToTownTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TownContent Towns = TownContent.Load(Content)!;
    static readonly CampaignTuning C = DataFile.Load<CampaignTuning>(Path.Combine(Content, CampaignTuning.File));
    static readonly IReadOnlyList<string> Trades = LineGenContent.Cached(Content).Config.Tiers.Fortress.Identities;

    static RunReport Report(RunEnd end) => new(end, 1000, 7, 6, 0, 6, 1000, 50, 0, 0, 950, 4, 0);

    static TownPlan TownOf(Route.Route route, string? last = null)
    {
        double gate = route.GateOr(RouteTuning.Load(Content).YardLength);
        return TownGenerator.Generate(Towns, TownSite.Of(route, gate, [], Towns, last));
    }

    [Fact]
    public void TheTownANightArrivesInIsTheTownTheNextNightLeavesFrom()
    {
        var tonight = Routes.Generate(Content, "frontier:7", 6);
        var arrived = TownAt.Terminus(tonight, Trades);
        Assert.NotNull(arrived);
        Assert.Equal(tonight.Plan!.Terminus.Name, arrived!.Name);
        Assert.Contains(arrived.Industry, Trades);

        var s = Campaign.Campaign.Arrived(Campaign.Campaign.New(C, 1, "test", 1), Report(RunEnd.Delivered), arrived);
        Assert.Equal(arrived, s.Town);
        // Whatever the board offers next, it leaves from there.
        foreach (var contract in Campaign.Campaign.Offers(C, Tuning.Run, s))
        {
            string spec = Campaign.Campaign.RouteOf(s, contract);
            Assert.StartsWith(contract.Route + "@", spec);
            var next = Routes.Generate(Content, spec, 6);
            Assert.Equal(arrived.Name, next.Plan!.Fortress.Name);
            Assert.Equal(arrived.Industry, next.Plan.Fortress.Identity);
            Assert.NotEqual(arrived.Name, next.Plan.Terminus.Name);
        }
    }

    [Fact]
    public void ItsTheSameTownWhicheverLineLeavesItTheSamePeopleAndStreets()
    {
        var arrived = TownAt.Terminus(Routes.Generate(Content, "frontier:7", 6), Trades)!;
        var a = TownOf(Routes.Generate(Content, TownAt.With("frontier:8", arrived), 6), last: "x");
        var b = TownOf(Routes.Generate(Content, TownAt.With("frontier:21", arrived), 6), last: "x");
        Assert.Equal(arrived.Name, a.Name);
        Assert.Equal(a.Name, b.Name);
        Assert.Equal(a.Culture, b.Culture);
        Assert.Equal(a.Industry, b.Industry);
        Assert.Equal(a.People.Select(p => p.Name), b.People.Select(p => p.Name));
        Assert.Equal(a.Houses.Count, b.Houses.Count);
        // And not the town that line would have left from on its own.
        var own = TownOf(Routes.Generate(Content, "frontier:8", 6), last: "x");
        Assert.NotEqual(own.Name, a.Name);
    }

    [Fact]
    public void TheLineIsTheContractsWhicheverTownItLeavesFrom()
    {
        var arrived = TownAt.Terminus(Routes.Generate(Content, "frontier:7", 6), Trades)!;
        var plain = Routes.Generate(Content, "frontier:8", 6);
        var from = Routes.Generate(Content, TownAt.With("frontier:8", arrived), 6);
        Assert.Equal(plain.Length, from.Length);
        Assert.Equal(plain.DawnSeconds, from.DawnSeconds);
        Assert.Equal(plain.Features.Select(f => (f.Kind, f.Start)), from.Features.Select(f => (f.Kind, f.Start)));
        Assert.Equal(plain.Seed, from.Seed);
    }

    [Fact]
    public void ANightThatDoesntArriveLeavesTheCrewWhereTheyWere()
    {
        var home = TownAt.Terminus(Routes.Generate(Content, "frontier:7", 6), Trades)!;
        var other = TownAt.Terminus(Routes.Generate(Content, "frontier:8", 6), Trades)!;
        var s = Campaign.Campaign.New(C, 1, "test", 1) with { Town = home };
        foreach (var end in new[] { RunEnd.Derailed, RunEnd.CrewLost, RunEnd.DawnMissed, RunEnd.Stranded })
            Assert.Equal(home, Campaign.Campaign.Arrived(s, Report(end), other).Town);
        Assert.Equal(other, Campaign.Campaign.Arrived(s, Report(RunEnd.Delivered), other).Town);
        // A silent settlement has nobody to leave from: the crew's where they were.
        Assert.Equal(home, Campaign.Campaign.Arrived(s, Report(RunEnd.Delivered), null).Town);
    }

    [Fact]
    public void ACarriedTownRidesInTheSpecAndBackOut()
    {
        var at = new TownAt("Fort Boudreau", "coal", 1234567890123UL);
        string spec = TownAt.With("deadLines:3", at);
        Assert.Equal("deadLines:3@Fort Boudreau/coal/1234567890123", spec);
        Assert.Equal(("deadLines:3", at), TownAt.Split(spec));
        Assert.Equal(spec, TownAt.With(spec, at));
        Assert.Equal("deadLines:3", TownAt.With(spec, null));
        Assert.Equal((RouteTier.DeadLines, 3UL), Route.Route.ParseSpec(spec));
        var p = RunParameters.Parse(spec, 6);
        Assert.Equal(at, p.From);
        var plain = RunParameters.Parse("deadLines:3", 6);
        Assert.Equal((plain.Tier, plain.Seed, plain.Severity, plain.RouteId), (p.Tier, p.Seed, p.Severity, p.RouteId));
    }
}
