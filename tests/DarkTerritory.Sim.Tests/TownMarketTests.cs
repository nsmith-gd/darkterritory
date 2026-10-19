using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Towns;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The night market on the square (queue #552, ARCHITECTURE §8 note 825; the director's playtest of 14 Oct, line 3, in
/// frontier:7's town: "The town's looking better. It's looking a little more explorable here. Still a bit basic"): a row
/// of open stalls between the buildings' fronts and the centrepiece, clear of everything else in the square and of the
/// doors, each with its wares and a stallholder behind it; more of them in a bigger town; and nothing else in the town
/// moved for them.
/// </summary>
public class TownMarketTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TownContent Towns = TownContent.Load(Content)!;

    static TownPlan Night(string spec, int people, string form = "", int[]? market = null)
    {
        var tuning = Towns.Tuning with
        {
            Population = [people, people],
            Forms = Towns.Tuning.Forms with { Force = form },
            Square = Towns.Tuning.Square with { Market = market ?? Towns.Tuning.Square.Market },
        };
        var route = Routes.Generate(Content, spec, 6);
        double gate = route.GateOr(RouteTuning.Load(Content).YardLength);
        var world = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), route.Build(), gate - 8), Tuning.Combat);
        world.EnableRun(Tuning.Run, route, gate, authority: true);
        world.EnableTown(Towns with { Tuning = tuning }, route, gate, []);
        return world.Town!.Plan;
    }

    [Theory]
    [InlineData("frontier:7", 97, "")]
    [InlineData("local:3", 1200, "")]
    [InlineData("frontier:7", 140, "harbour")]
    public void TheSquareHasItsNightMarketEachStallKeptAndClear(string spec, int people, string form)
    {
        var plan = Night(spec, people, form);
        var sq = plan.Square;
        var stalls = plan.Fixtures.Where(f => f.Kind == "marketstall").ToList();
        Assert.InRange(stalls.Count, Towns.Tuning.Square.Market[0], Towns.Tuning.Square.Market[1]);
        // In the square, between the line and the buildings' fronts; clear of everything else in it and of each other.
        double front = Towns.Tuning.Square.WallOut - 1 - Towns.Tuning.Square.BuildingDepth;
        foreach (var s in stalls)
        {
            Assert.InRange(s.S, sq.S0, sq.S1);
            Assert.True(Math.Sign(s.D) == sq.Side && Math.Abs(s.D) + s.SolidD < front && Math.Abs(s.D) - s.SolidD > 3.4,
                $"a stall at ({s.S:0}, {s.D:0}) not in the square's open ground");
            foreach (var f in plan.Fixtures.Where(f => f != s && f.House < 0))
                Assert.False(Math.Abs(f.S - s.S) < f.SolidS + s.SolidS && Math.Abs(f.D - s.D) < f.SolidD + s.SolidD,
                    $"the stall at {s.S:0} stands on {f.Kind} at ({f.S:0}, {f.D:0})");
            foreach (var b in plan.Buildings)
                Assert.True(Math.Abs(b.S - s.S) > s.SolidS + 1.5, $"the stall at {s.S:0} in front of the {b.Kind}'s door");
            // Something to look at, and someone behind it.
            Assert.False(string.IsNullOrEmpty(s.Name));
            Assert.False(string.IsNullOrEmpty(s.Text));
            var holder = Assert.Single(plan.People, p => p.Role == "stallholder" && Math.Abs(p.S - s.S) < 0.01);
            // Behind the counter (away from the way it faces), facing the way it does, still in the square.
            Assert.True((holder.D - s.D) * s.FaceD < -s.SolidD && Math.Abs(holder.D) < front && Math.Abs(holder.D) > 3.4,
                "the stallholder isn't behind the counter");
            Assert.Equal(Math.Sign(s.FaceD), Math.Sign(holder.FaceD));
        }
        Assert.Equal(stalls.Count, stalls.Select(s => s.Text).Distinct().Count());
        // Most of them talk about their stalls (a close-mouthed one says only how they carry it: note 474).
        var holders = plan.People.Where(p => p.Role == "stallholder").ToList();
        Assert.True(holders.Count(h => h.Lines.Any(l => Towns.Writing.Roles["stallholder"].Lines.Contains(l))) * 2 >= holders.Count,
            "the stallholders hardly talk about their stalls");
    }

    [Fact]
    public void ItsStallsFaceEachOtherAcrossAnAisleNotInOneRuledRow()
    {
        // Note 870 (the director's playtest of 19 Oct, line 4: "Too many cut and paste stalls in a row ... doesn't look like
        // a market"): a big town's market is two rows, counters facing across the aisle, and no row a ruled line.
        var plan = Night("frontier:7", 3000);
        var stalls = plan.Fixtures.Where(f => f.Kind == "marketstall").ToList();
        var rows = stalls.GroupBy(f => Math.Sign(f.FaceD)).ToList();
        Assert.Equal(2, rows.Count);
        var (_, hd, _) = TownFixtures.Size("marketstall");
        double aisle = Math.Abs(rows[0].Average(f => f.D) - rows[1].Average(f => f.D)) - 2 * hd;
        Assert.InRange(aisle, Towns.Tuning.Square.MarketAisle - 2 * Towns.Tuning.Square.MarketJitter, Towns.Tuning.Square.MarketAisle + 2 * Towns.Tuning.Square.MarketJitter);
        // Each row's counters face the other's, the far row's toward the line.
        var far = rows.OrderByDescending(r => r.Average(f => Math.Abs(f.D))).First();
        Assert.Equal(-plan.Square.Side, far.Key);
        // Staggered: not all on one line across, nor all the same distance apart.
        foreach (var row in rows.Where(r => r.Count() >= 3))
        {
            Assert.True(row.Select(f => Math.Round(f.D, 2)).Distinct().Count() > 1, "a row of stalls all on one line");
            var gaps = row.OrderBy(f => f.S).Zip(row.OrderBy(f => f.S).Skip(1), (a, b) => Math.Round(b.S - a.S, 2)).Distinct().Count();
            Assert.True(gaps > 1, "a row of stalls evenly spaced, as if ruled");
        }
    }

    [Fact]
    public void ABiggerTownHasABiggerMarket()
    {
        int small = Night("frontier:7", 97).Fixtures.Count(f => f.Kind == "marketstall");
        int big = Night("frontier:7", 3000).Fixtures.Count(f => f.Kind == "marketstall");
        Assert.True(big > small, $"{big} stalls at 3000 people, {small} at 97");
    }

    [Fact]
    public void TheMarketMovesNothingElse()
    {
        // With no market, every other fixture and every townsperson is as it is with one: the stalls are the fixtures' last
        // but the forge, their stallholders the people's last but the blacksmith and the Flame's keeper (who stay last of
        // everyone, note 697), so everyone before them is who they were and those two where they were.
        var with = Night("local:3", 1200);
        var without = Night("local:3", 1200, market: [0, 0]);
        Assert.DoesNotContain(without.Fixtures, f => f.Kind == "marketstall");
        Assert.Equal(without.Fixtures.Select(f => (f.Kind, f.S, f.D, f.Text)), with.Fixtures.Where(f => f.Kind != "marketstall").Select(f => (f.Kind, f.S, f.D, f.Text)));
        int before = with.People.TakeWhile(p => p.Role != "stallholder").Count();
        Assert.InRange(without.People.Count - before, 0, 2);
        Assert.Equal(without.People.Take(before).Select(p => (p.Name, p.Role, p.S, p.D)), with.People.Take(before).Select(p => (p.Name, p.Role, p.S, p.D)));
        Assert.Equal(without.People.Select(p => (p.Role, p.S, p.D)), with.People.Where(p => p.Role != "stallholder").Select(p => (p.Role, p.S, p.D)));
        Assert.Equal(without.Houses.Count, with.Houses.Count);
    }

    [Fact]
    public void TheMarketIsTheSameEveryTime()
    {
        static string Of(TownPlan p) => string.Join(";", p.Fixtures.Where(f => f.Kind == "marketstall").Select(f => $"{f.S:0.00}:{f.Text}"))
            + string.Join(";", p.People.Where(x => x.Role == "stallholder").Select(x => x.Name));
        Assert.Equal(Of(Night("frontier:7", 97)), Of(Night("frontier:7", 97)));
    }
}
