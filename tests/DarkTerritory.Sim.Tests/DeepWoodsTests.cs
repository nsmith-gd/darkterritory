using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The big forests (note 1006; the director, 23 Oct 2026: "We should really try and have, like, big forests in this game"):
/// long stretches of deep woods along the line in the countries that grow them (biomes.json deepWoods), dealt from the
/// night's seed and plan alone; in them the trees stand thick, close to the line and solid, and something has somewhere to
/// come out of them (#709's hook, <see cref="DeepWoods.Sites"/>).
/// </summary>
[Collection(nameof(LineGenTests))]
public class DeepWoodsTests
{
    static readonly string Content = DataFile.FindContentRoot();

    [Fact]
    public void TheFrontierRunsThroughBigForestsAndTheDykelandsGrowNone()
    {
        // frontier:7's black spruce: over a third of the night between the gates in deep woods, each stretch long.
        var plan = Routes.Generate(Content, "frontier:7", 6).Plan!;
        var woods = DeepWoods.Of(plan);
        Assert.InRange(woods.Share(plan.GateM, plan.TerminusM), 0.3, 0.7);
        Assert.All(woods.Stretches, w => Assert.True(w.S1 - w.S0 >= 400, $"a stretch only {w.S1 - w.S0:0} m"));
        // None where the country's biome has no big forests (the dykelands, the bog).
        Assert.All(plan.Biomes.Where(b => b.Edge == "main" && plan.Rules.Biomes[b.Biome].DeepWoods is null),
            b => Assert.True(woods.Share(b.S0 + 1, b.S1 - 1) == 0 || b.S1 - b.S0 < 2,
                $"deep woods in {b.Biome} at {b.S0:0}-{b.S1:0}"));
        // Out in the territory every forest night has some (and the coal country, none).
        foreach (var spec in new[] { "frontier:1", "frontier:3", "deadLines:3" })
            Assert.NotEmpty(DeepWoods.Of(Routes.Generate(Content, spec, 6).Plan!).Stretches);
    }

    [Fact]
    public void TheWoodsAreTheSameEveryTimeTheNightIsMade()
    {
        var a = DeepWoods.Of(Routes.Generate(Content, "deadLines:3", 6).Plan!).Stretches;
        var plan = LinePlan.FromJson(Routes.Generate(Content, "deadLines:3", 6).Plan!.ToJson());
        var b = DeepWoods.Of(plan).Stretches;
        Assert.Equal(a.Select(w => (w.S0, w.S1)), b.Select(w => (w.S0, w.S1)));
    }

    [Fact]
    public void InTheDeepWoodsTheTreesStandThickCloseAndSolid()
    {
        var route = Routes.Generate(Content, "frontier:7", 6);
        var line = route.Build();
        var side = LinesideProps.Of(route, line)!;
        // 200 m wholly in the deep woods and clear of any stop's ground (a stop's yard is cleared of them).
        double mid = Enumerable.Range(0, (int)(line.Length / 50)).Select(i => i * 50.0 + 100)
            .First(m => side.Woods.Depth(m - 100) == 1 && side.Woods.Depth(m + 100) == 1
                && !route.Features.Any(f => f.Stop is not null && f.Start < m + 150 && f.End > m - 150));
        var w = side.Woods.At(mid)!;
        // Against 200 m of the same night's woods out of any.
        var deepTrees = Trees(side, mid - 100, mid + 100);
        var outside = Enumerable.Range(0, (int)(line.Length / 200)).Select(i => i * 200.0)
            .Where(s => side.Woods.Depth(s) == 0 && side.Woods.Depth(s + 200) == 0 && side.Biome(s + 100)?.TreeDensity >= 0.8)
            .Select(s => Trees(side, s, s + 200)).Where(t => t.Count > 0).ToList();
        Assert.NotEmpty(outside);
        // Thickest where it counts, in the wall either side of the line (the rule's wallM): the black spruce stands out of
        // the deep woods already cover nine-tenths of their land further out.
        static int Wall(List<LinesideProp> trees, double wallM) => trees.Count(t => Math.Abs(t.Lateral) < wallM);
        double wall = side.Woods.At(mid)!.Rule.WallM;
        Assert.True(Wall(deepTrees, wall) > 1.5 * outside.Average(t => Wall(t, wall)),
            $"{Wall(deepTrees, wall)} trees within {wall} m in 200 m of deep woods, {outside.Average(t => Wall(t, wall)):0} out of them");
        double near = w.Rule.NearM;
        Assert.All(deepTrees, t => Assert.True(Math.Abs(t.Lateral) >= Math.Min(near, 6) - 1e-9, $"a trunk {t.Lateral:0.0} m out"));
        // and close in on the line: three times the trunks within 8 and 20 m of the night's woods out of them (17 and 231 in
        // 200 m against 2.8 and 52 when this was written), none nearer than the rule's nearM.
        foreach (double within in new[] { 8.0, 20 })
            Assert.True(Wall(deepTrees, within) >= Math.Max(10, 3 * outside.Average(t => Wall(t, within))),
                $"{Wall(deepTrees, within)} trunks within {within} m in the deep woods, {outside.Average(t => Wall(t, within)):0.0} out of them");
        // Solid: every one of them stands as a wall the crew walk into.
        var walls = side.Walls([], 90).ToList();
        foreach (var t in deepTrees.Where(t => Math.Abs(t.Lateral) < 8).Take(20))
            Assert.Contains(walls, x => Math.Abs(x.HalfWidth - t.Radius) < 1e-9 && x.Top == t.Ground + t.Height && x.Bottom == t.Ground - 3);
    }

    [Fact]
    public void SomethingHasSomewhereToComeOutOfTheTrees()
    {
        // The hook for #709's forest heavies: sites along every stretch, both sides, at the woods' inner edge.
        var woods = DeepWoods.Of(Routes.Generate(Content, "frontier:7", 6).Plan!);
        var sites = woods.Sites(40).ToList();
        Assert.NotEmpty(sites);
        Assert.Contains(sites, s => s.Lateral < 0);
        Assert.Contains(sites, s => s.Lateral > 0);
        Assert.All(sites, s => Assert.Contains(woods.Stretches, w => s.Along >= w.S0 && s.Along <= w.S1));
        Assert.All(sites, s => Assert.InRange(Math.Abs(s.Lateral), 4, 10));
    }

    static List<LinesideProp> Trees(LinesideProps side, double from, double to) =>
        [.. side.Props(from, to).Where(p => p.Kind == LinesideKind.Tree && p.Species is not ("fieldSpruce" or "apple") && p.Along >= from && p.Along < to)];
}
