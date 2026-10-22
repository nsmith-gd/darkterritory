using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Towns;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// What stands between a town's houses (GDD §3.1's "fish sheds, fences, granite and lobster traps between the houses";
/// ARCHITECTURE §8 note 989): laid from the town's seed, the same every time; on the free ground only, never on the line, a
/// street, a lane, a way, a house, its front and door, a yard thing, the square, the green, the works or the rail yard, nor
/// where anyone stands or walks; and solid where it's drawn.
/// </summary>
public class TownBetweenTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TownContent Towns = TownContent.Load(Content)!;

    public static TheoryData<string, string, int> Cases()
    {
        // The street town (""), and every town type, small and big.
        var data = new TheoryData<string, string, int> { { "", "frontier:7", 1200 }, { "", "frontier:3", 2400 } };
        foreach (var form in TownForms.All)
            data.Add(form, form.Length % 2 == 0 ? "frontier:7" : "local:5", form.Length % 2 == 0 ? 300 : 2400);
        return data;
    }

    static TownContent As(string form, int people) =>
        Towns with { Tuning = Towns.Tuning with { Population = [people, people], Forms = Towns.Tuning.Forms with { Force = form } } };

    static Town Night(string form, string spec, int people)
    {
        var route = Routes.Generate(Content, spec, 6);
        double gate = route.GateOr(RouteTuning.Load(Content).YardLength);
        var world = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), route.Build(), gate - 8), Tuning.Combat);
        world.EnableRun(Tuning.Run, route, gate, authority: true);
        world.EnableTown(As(form, people), route, gate, []);
        return world.Town!;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void WhatsBetweenTheHousesIsTheSameEveryTime(string form, string spec, int people)
    {
        var a = Night(form, spec, people).Plan.Between;
        var b = Night(form, spec, people).Plan.Between;
        Assert.NotEmpty(a);
        Assert.Equal(a, b);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ItStandsOnlyOnTheFreeGroundBetweenTheHouses(string form, string spec, int people)
    {
        var town = Night(form, spec, people);
        var plan = town.Plan;
        var bounds = plan.Bounds!;
        var t = Towns.Tuning.Between;
        Assert.True(plan.Between.Count >= plan.Houses.Count * t.PerHouseFor(form) / 4, $"{plan.Between.Count} pieces for {plan.Houses.Count} houses");
        static bool Overlap(double a0, double a1, double b0, double b1) => a0 < b1 && b0 < a1;
        foreach (var p in plan.Between)
        {
            double s0 = p.S - p.HalfS, s1 = p.S + p.HalfS, d0 = p.D - p.HalfD, d1 = p.D + p.HalfD;
            string what = $"{p.Kind} at {p.S - bounds.Gate:0.0}, {p.D:0.0}";
            // Off the line, and inside the wall.
            Assert.True(d0 >= t.Line || d1 <= -t.Line, $"{what} is by the line");
            Assert.True(bounds.Holds(p.S, p.D), $"{what} is outside the wall");
            // Off every street and lane.
            foreach (var st in bounds.Streets.Where(st => Overlap(s0, s1, st.S0, st.S1)))
                foreach (double s in new[] { s0, p.S, s1 })
                    Assert.False(Overlap(d0, d1, st.At(s) - st.Width / 2, st.At(s) + st.Width / 2), $"{what} is on a street");
            foreach (var lane in bounds.Lanes)
            {
                double lo = Math.Min(lane.D0, lane.D1), hi = Math.Max(lane.D0, lane.D1);
                if (Overlap(d0, d1, lo, hi))
                {
                    var (ls, hs) = lane.Span(Math.Max(lo, d0), Math.Min(hi, d1));
                    Assert.False(Overlap(s0, s1, ls, hs), $"{what} is on a lane");
                }
            }
            // Out of the square, the green, the works and the rail yard.
            var sq = plan.Square;
            Assert.False(Math.Sign(p.D) == sq.Side && Overlap(s0, s1, sq.S0, sq.S1) && Math.Min(Math.Abs(d0), Math.Abs(d1)) < Math.Abs(sq.WallD), $"{what} is in the square");
            foreach (var c in new[] { (p.S, p.D), (s0, d0), (s1, d1), (s0, d1), (s1, d0) })
            {
                Assert.False(plan.Green?.Holds(c.Item1, c.Item2) ?? false, $"{what} is on the green");
                Assert.False(plan.Works?.Holds(c.Item1, c.Item2) ?? false, $"{what} is in the works");
                Assert.False(plan.Yard?.Holds(c.Item1, c.Item2) ?? false, $"{what} is in the rail yard");
                Assert.Null(town.Ground.Water(c.Item1, c.Item2));
            }
            // Off every house, its front and its yard's things.
            foreach (var h in plan.Houses)
            {
                var (u0, u1, v0, v1) = h.Extent();
                foreach (var (a0, a1, b0, b1) in h.Yard.Select(y => (y.U0, y.U1, y.V0, y.V1)).Append((u0, u1, Math.Min(v0, -t.Front), v1)))
                {
                    var (sa, da) = h.Rail(a0, b0);
                    var (sb, db) = h.Rail(a1, b1);
                    Assert.False(Overlap(s0, s1, Math.Min(sa, sb), Math.Max(sa, sb)) && Overlap(d0, d1, Math.Min(da, db), Math.Max(da, db)),
                        $"{what} is on house {h.Id}, its front or its yard");
                }
            }
            // Clear of the square's and the works' things, and of each other (a rock may lean on a rock).
            foreach (var f in plan.Fixtures.Where(f => f.House < 0))
                Assert.False(Math.Abs(f.S - p.S) < p.HalfS + 0.5 && Math.Abs(f.D - p.D) < p.HalfD + 0.5, $"{what} is on the {f.Kind}");
            foreach (var q in plan.Between.Where(q => q != p && !(q.Kind == BetweenKind.Granite && p.Kind == BetweenKind.Granite)))
                Assert.False(Math.Abs(q.S - p.S) < q.HalfS + p.HalfS && Math.Abs(q.D - p.D) < q.HalfD + p.HalfD, $"{what} is on a {q.Kind}");
        }
        // Nobody's round goes through one: where they stand, and the ways between.
        foreach (var r in town.Rounds.OfType<TownRound>())
            foreach (var stop in r.Stops)
                foreach (var (s, d) in stop.Via.Append((stop.S, stop.D)))
                    Assert.DoesNotContain(plan.Between, p => Math.Abs(s - p.S) < p.Solid.HalfS && Math.Abs(d - p.D) < p.Solid.HalfD);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void EachIsSolidWhereItsDrawn(string form, string spec, int people)
    {
        var town = Night(form, spec, people);
        foreach (var p in town.Plan.Between)
        {
            var mid = town.World(p.S, p.D) with { Y = 0 };
            double ground = town.World(p.S, p.D).Y;
            var (hs, hd, height) = p.Solid;
            Assert.Contains(town.Walls, w => ((w.Centre - mid) with { Y = 0 }).Length < 0.05 && Math.Abs(w.HalfLength - hs) < 1e-6
                && Math.Abs(w.HalfWidth - hd) < 1e-6 && w.Top >= ground + height - 0.01 && w.Bottom <= ground);
        }
    }

    /// <summary>All four kinds come up, over a few towns: the GDD's fish sheds, fences, granite and lobster traps.</summary>
    [Fact]
    public void ATownHasFishShedsFencesGraniteAndTraps()
    {
        var seen = new HashSet<BetweenKind>();
        foreach (var form in new[] { "", "harbour" })
            foreach (var p in Night(form, "frontier:7", 1200).Plan.Between)
                seen.Add(p.Kind);
        Assert.Equal(Enum.GetValues<BetweenKind>().ToHashSet(), seen);
    }

    /// <summary>Off, a town has nothing between its houses, and everything else in it is as it was.</summary>
    [Fact]
    public void OffNothingStandsBetweenAndTheRestOfTheTownIsUnchanged()
    {
        var site = new TownSite("Fort Boudreau", "coal", 1180, 1234, []) { Form = "" };
        var on = TownGenerator.Generate(As("", 1200), site);
        var off = TownGenerator.Generate(As("", 1200) with { Tuning = As("", 1200).Tuning with { Between = new BetweenTuning { Enabled = false } } }, site);
        Assert.NotEmpty(on.Between);
        Assert.Empty(off.Between);
        Assert.Equal(off.Houses.Select(h => (h.Id, h.S, h.D, h.Kind)), on.Houses.Select(h => (h.Id, h.S, h.D, h.Kind)));
        Assert.Equal(off.People.Select(p => (p.Id, p.S, p.D)), on.People.Select(p => (p.Id, p.S, p.D)));
        Assert.Equal(off.Fixtures.Select(f => (f.Id, f.S, f.D)), on.Fixtures.Select(f => (f.Id, f.S, f.D)));
    }
}
