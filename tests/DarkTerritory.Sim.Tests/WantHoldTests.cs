using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Route;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// App. B.1 want balance held to its shares (note 1048; enemies.json director.wantHold). The shares are spent on what's about,
/// and the night's chances can leave a want nothing (Local seed 1 rolled out every cargo kind but the Fire Flies): the want
/// floor brings each want's kinds back, and a rule-less one of each want that has any, for the fill.
/// </summary>
public class WantHoldTests
{
    static readonly DirectorTuning D = Tuning.Enemies.Director;

    static string WantOf(string key) => Director.WantOf(Kind(key)).ToString().ToLowerInvariant();

    static EnemyKind Kind(string key) => Enum.GetValues<EnemyKind>().First(k => Director.Key(k) == key);

    static IEnumerable<string> Sends => Spawns.Rules.Select(r => Director.Key(r.Kind)).Distinct();

    [Fact]
    public void EveryNightHasEachWantsFloorOfKindsAboutAndOneRulelessForTheFill()
    {
        Assert.True(D.WantHold.Floor > 0);
        var free = D.Curve.Kinds.Where(k => k.Value > 0).Select(k => k.Key).ToList();
        for (ulong seed = 1; seed <= 200; seed++)
            foreach (var (tier, crew) in new[] { (RouteTier.Local, 2), (RouteTier.DeepTerritory, 4) })
            {
                var route = new Route.Route("t", tier, seed, new Rail.LineDefinition("t", [new Rail.TrackSegment(1000)]), [], new RouteWeather(0, false, 0, 0), 1440);
                var d = new Director(D, route, seed, 6, crew);
                foreach (var want in D.WantShares.Keys)
                {
                    int about = Sends.Count(k => WantOf(k) == want && !d.OutTonight(Kind(k)));
                    int could = Sends.Count(k => WantOf(k) == want && D.ChanceFor(k, D.NightChance.GetValueOrDefault(k, 1)) > 0);
                    Assert.True(about >= Math.Min(D.WantHold.Floor, could), $"seed {seed}, {tier}: {about} {want} about");
                    if (free.Any(k => WantOf(k) == want))
                        Assert.Contains(free, k => WantOf(k) == want && !d.OutTonight(Kind(k)));
                }
            }
    }

    [Fact]
    public void TheWantFloorOnlyBringsKindsBackAndLeavesTheFloorsOwnAsTheyWere()
    {
        var off = new Director(D with { WantHold = new() }, null, 1, 6, 2);
        var on = new Director(D, null, 1, 6, 2);
        // What the night's floor brought back comes first, in the same order: the want floor draws after it.
        Assert.Equal(off.FloorBrought, on.FloorBrought.Take(off.FloorBrought.Count));
        Assert.All(Sends.Where(k => !off.OutTonight(Kind(k))), k => Assert.False(on.OutTonight(Kind(k))));
        // Seed 1 rolls out every cargo kind but the Fire Flies.
        Assert.True(Sends.Count(k => WantOf(k) == "cargo" && !on.OutTonight(Kind(k))) >= D.WantHold.Floor);
    }
}
