using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Towns;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The town's blacksmith, who does the trains (ARCHITECTURE §8 note 697; the director, 13 Oct 2026: "Upgrades need to be
/// purchasable in the fortress towns from a vendor who is clearly marked as the vendor blacksmith of the town who does train
/// upgrades"): every town has one, at a forge beside the engine as it stands in the yard, always there to buy from.
/// </summary>
public class BlacksmithTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TownContent Towns = TownContent.Load(Content)!;

    static TownSite Site(int seed)
    {
        string[] industries = [.. Towns.Writing.Industries.Keys.Order(StringComparer.Ordinal)];
        return new TownSite($"Fort {Towns.Surnames[seed % Towns.Surnames.Count]}", industries[seed % industries.Length], 1100, (ulong)seed, []);
    }

    [Fact]
    public void EveryTownHasOneBlacksmithAtTheirForgeByTheEngine()
    {
        var smithy = Towns.Tuning.Smithy!;
        for (int seed = 1; seed <= 40; seed++)
        {
            var plan = TownGenerator.Generate(Towns, Site(seed));
            var smith = Assert.Single(plan.People, p => p.Role == "blacksmith");
            var forge = Assert.Single(plan.Fixtures, f => f.Kind == "forge");
            Assert.Equal("blacksmith", smith.Title);
            Assert.NotEmpty(smith.Lines);
            Assert.Contains("BLACKSMITH", forge.Text, StringComparison.Ordinal);
            Assert.Contains("TRAIN UPGRADES", forge.Text, StringComparison.Ordinal);
            // Beside the engine as it stands in the yard (8 m short of the gate, its 20 m back from there), on the town's side.
            int side = plan.Square.Side;
            Assert.Equal(plan.Gate + smithy.FromGate, forge.S, 6);
            Assert.InRange(forge.S, plan.Gate - 28, plan.Gate - 8);
            Assert.Equal(side * smithy.Out, forge.D, 6);
            // In front of it, between it and the line, facing the line.
            Assert.Equal(forge.S, smith.S, 6);
            Assert.Equal(side * (smithy.Out - smithy.Stand), smith.D, 6);
            Assert.Equal(-side, smith.FaceD, 6);
        }
    }

    [Fact]
    public void TheForgeStandsClearOfTheTownsBuildingsAndThings()
    {
        static bool Overlap(double s0, double hs0, double d0, double hd0, double s1, double hs1, double d1, double hd1) =>
            Math.Abs(s0 - s1) < hs0 + hs1 && Math.Abs(d0 - d1) < hd0 + hd1;
        for (int seed = 1; seed <= 40; seed++)
        {
            var plan = TownGenerator.Generate(Towns, Site(seed));
            var forge = plan.Fixtures.Single(f => f.Kind == "forge");
            foreach (var b in plan.Buildings)
                Assert.False(Overlap(forge.S, forge.SolidS, forge.D, forge.SolidD, b.S, b.Length / 2, b.D, b.Depth / 2), $"seed {seed}: the forge in {b.Name}");
            foreach (var h in plan.Houses)
                Assert.False(Overlap(forge.S, forge.SolidS, forge.D, forge.SolidD, h.S, h.Width / 2, h.D, h.Depth / 2), $"seed {seed}: the forge in the {h.Family} house");
            foreach (var f in plan.Fixtures.Where(f => f.House < 0 && f.Id != forge.Id && f.Kind is not "mural" and not "line"))
                Assert.False(Overlap(forge.S, forge.SolidS, forge.D, forge.SolidD, f.S, f.SolidS, f.D, f.SolidD), $"seed {seed}: the forge on {f.Name}");
            // Nobody else stands in it.
            foreach (var p in plan.People.Where(p => p.House < 0))
                Assert.False(Overlap(forge.S, forge.SolidS, forge.D, forge.SolidD, p.S, 0.3, p.D, 0.3), $"seed {seed}: {p.Name} in the forge");
        }
    }

    [Fact]
    public void TheBlacksmithNeverLeavesTheForge()
    {
        // The others go off on their rounds (note 353); the blacksmith has to be found to be bought from.
        var route = Routes.Generate(Content, "frontier:7", 6);
        var line = route.Build();
        double gate = route.GateOr(RouteTuning.Load(Content).YardLength);
        var town = new Town(TownGenerator.Generate(Towns, TownSite.Of(route, gate, [], Towns)), Towns.Tuning, line);
        var smith = town.Plan.People.Single(p => p.Role == "blacksmith");
        var post = town.World(smith.S, smith.D);
        for (double t = 0; t < 1800; t += 7)
        {
            town.Clock = t;
            Assert.True((town.Now(smith).Feet - post).Length < 0.5, $"at {t} s the blacksmith's {(town.Now(smith).Feet - post).Length:0.0} m from the forge");
        }
    }

    [Fact]
    public void EverybodyElseIsWhoTheyWereBeforeTheBlacksmithCame()
    {
        // The blacksmith and the forge come last (note 697), so nobody else's name, place or number changed with them.
        var withSmith = TownGenerator.Generate(Towns, Site(7));
        var without = TownGenerator.Generate(Towns with { Tuning = Towns.Tuning with { Smithy = null } }, Site(7));
        Assert.Equal(without.People.Count + 1, withSmith.People.Count);
        Assert.Equal(without.People.Select(p => (p.Id, p.Name, p.S, p.D)), withSmith.People.Take(without.People.Count).Select(p => (p.Id, p.Name, p.S, p.D)));
        Assert.Equal(without.Fixtures.Select(f => (f.Id, f.Kind)), withSmith.Fixtures.Take(without.Fixtures.Count).Select(f => (f.Id, f.Kind)));
    }
}
