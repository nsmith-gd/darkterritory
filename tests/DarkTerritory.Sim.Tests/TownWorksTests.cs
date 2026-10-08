using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Towns;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// A walled town's works (queue #166, ARCHITECTURE §8 note 430; GDD §3: "Fortified towns survive behind stone and steel
/// walls ... furnaces, rail yards and warehouses. ... One settlement produces coal. Another grows food. Another ...
/// operates foundries"): its trade at work inside its wall, across the line from its green, solid, each piece to be
/// looked at, its hands at work there all night.
/// </summary>
public class TownWorksTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TownContent Towns = TownContent.Load(Content)!;

    static Town Plan(string spec, int people)
    {
        var content = Towns with { Tuning = Towns.Tuning with { Population = [people, people] } };
        var route = Routes.Generate(Content, spec, 6);
        double gate = route.GateOr(RouteTuning.Load(Content).YardLength);
        var world = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), route.Build(), gate - 8), Tuning.Combat);
        world.EnableRun(Tuning.Run, route, gate, authority: true);
        world.EnableTown(content, route, gate, []);
        return world.Town!;
    }

    [Theory]
    [InlineData("local:3", 1200, "coal", new[] { "tip", "headframe", "coal", "winding" })]
    [InlineData("frontier:3", 1200, "farm", new[] { "glasshouse", "elevator", "cellar" })]
    [InlineData("local:5", 1200, "foundry", new[] { "casting", "slag", "pigs" })]
    public void AWalledTownsWorksAreItsTradeAcrossTheLineFromItsGreen(string spec, int people, string trade, string[] kinds)
    {
        var town = Plan(spec, people);
        var plan = town.Plan;
        var works = Assert.IsType<TownWorks>(plan.Works);
        Assert.Equal(trade, works.Trade);
        Assert.Equal(-plan.Square.Side, works.Side);
        Assert.True(works.S0 < plan.Square.S0 && works.S1 > plan.Square.S1, "the works don't run the square's length across the line");
        var pieces = plan.Fixtures.Where(f => works.Holds(f.S, f.D)).ToList();
        foreach (string kind in kinds)
            Assert.Contains(pieces, f => f.Kind == kind);
        Assert.Contains(pieces, f => f.Kind is "warehouse" or "watertower");

        // Every piece inside the works, clear of the streets either side, and no two on each other.
        foreach (var f in pieces)
        {
            double d = Math.Abs(f.D);
            Assert.True(f.S - f.SolidS >= works.S0 && f.S + f.SolidS <= works.S1, $"{f.Kind} runs out of the works along the line");
            Assert.True(d - f.SolidD >= works.Near && d + f.SolidD <= works.Far, $"{f.Kind} ({d - f.SolidD:0.0}..{d + f.SolidD:0.0}) in a street");
        }
        for (int i = 0; i < pieces.Count; i++)
            for (int j = i + 1; j < pieces.Count; j++)
            {
                var (a, b) = (pieces[i], pieces[j]);
                Assert.False(Math.Abs(a.S - b.S) < a.SolidS + b.SolidS && Math.Abs(a.D - b.D) < a.SolidD + b.SolidD, $"{a.Kind} on {b.Kind}");
            }
        // The pit's winding house stands its ropes' length from its headframe.
        if (trade == "coal")
        {
            var frame = Assert.Single(pieces, f => f.Kind == "headframe");
            var house = Assert.Single(pieces, f => f.Kind == "winding");
            Assert.Equal(32.9, house.S - frame.S, 3);
        }

        // Each is solid, and says what it is looked at from in front of it (a step along, past whoever's at work there).
        foreach (var f in pieces)
        {
            Assert.False(town.Free(town.World(f.S, f.D)), $"{f.Kind} isn't solid");
            Assert.True(f.Text.Length > 0 && !f.Name.Contains('{') && !f.Text.Contains('{'), $"{f.Kind} says nothing, or a placeholder");
            var at = town.LookAt(f);
            var stand = town.World(f.S + 1.8, f.D + f.FaceD * (f.SolidD + 1.5));
            var eye = stand + Double3.Up * 1.6;
            var target = town.Target(eye, (at - eye).Normalized);
            Assert.True(target is { Kind: TownTargetKind.Fixture } t && t.Index == f.Id, $"{f.Name} ({f.Kind}) can't be looked at: {target} "
                + (target is { Kind: TownTargetKind.Fixture } o ? plan.Fixtures[o.Index].Kind : target is { Kind: TownTargetKind.Person } q ? plan.People[q.Index].Role : ""));
        }

        // No house or yard stands in them, and no lane runs through them.
        foreach (var h in plan.Houses)
            foreach (var p in h.Solids())
            {
                var (sa, da) = h.Rail(p.U0, p.V0);
                var (sb, db) = h.Rail(p.U1, p.V1);
                Assert.False(Math.Max(sa, sb) > works.S0 && Math.Min(sa, sb) < works.S1 && Math.Sign(da) == works.Side
                    && Math.Max(Math.Abs(da), Math.Abs(db)) > works.Near && Math.Min(Math.Abs(da), Math.Abs(db)) < works.Far, $"house {h.Id} in the works");
            }
        foreach (var lane in plan.Bounds!.Lanes)
        {
            var (lo, hi) = lane.Span(works.Side * works.Near, works.Side * works.Far);
            Assert.False(hi > works.S0 && lo < works.S1, $"the lane at {lane.S:0} runs through the works");
        }

        // Its hands are at work there all night: at a piece's front, on their rounds about it, with the trade's lines.
        var hands = plan.People.Where(p => p.Role == "hand" && p.House < 0 && works.Holds(p.S, p.D, 3)).ToList();
        Assert.NotEmpty(hands);
        Assert.All(hands, p => Assert.NotEmpty(p.Lines));
        Assert.Contains(hands, p => town.Rounds[p.Id] is { } r && r.Stops.Any(s => s.Act is "work" or "warm" or "mend" && works.Holds(s.S, s.D, 3)));
    }

    [Fact]
    public void ASmallTownHasNoWorksAndAOneStreetTownOnlyWhatFits()
    {
        Assert.Null(Plan("frontier:7", 60).Plan.Works);
        // One street a side: its works between the street and the wall, what fits there and nothing more.
        var town = Plan("frontier:2", 270);
        var works = Assert.IsType<TownWorks>(town.Plan.Works);
        Assert.Single(town.Plan.Bounds!.Streets, st => Math.Sign(st.D) == works.Side);
        foreach (var f in town.Plan.Fixtures.Where(f => works.Holds(f.S, f.D)))
            Assert.True(Math.Abs(f.D) + f.SolidD <= works.Far, $"{f.Kind} past the works into the wall's margin");
    }

    [Fact]
    public void TheWorksAreTheSameEveryTime()
    {
        string Layout(Town t) => string.Join(";", t.Plan.Fixtures.Where(f => t.Plan.Works!.Holds(f.S, f.D)).Select(f => $"{f.Kind}@{f.S:0.000},{f.D:0.000}"));
        Assert.Equal(Layout(Plan("local:3", 1200)), Layout(Plan("local:3", 1200)));
    }
}
