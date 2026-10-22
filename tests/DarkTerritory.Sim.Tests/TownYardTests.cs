using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Towns;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// A walled town's rail yard (queue #239, ARCHITECTURE §8 note 502; GDD §3: "Fortified towns survive behind stone and steel
/// walls ... rail yards and warehouses"): behind the home platform, where the first street's near row would have stood; its
/// siding with the stock left standing in it between two buffer stops, its goods sheds fronting the siding, the yard office
/// and the weighbridge, freight waiting; solid, each to be looked at, its porters at work there all night.
/// </summary>
public class TownYardTests
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

    // Street towns (note 335) big enough for a yard: frontier:3's town of all sorts, frontier:7's company town grown. (Most
    // departure towns are a town type now, note 692, their own ground plan: none of them has this yard.)
    [Theory]
    [InlineData("frontier:3", 1200)]
    [InlineData("frontier:3", 2400)]
    [InlineData("frontier:7", 1200)]
    [InlineData("frontier:7", 500)]
    public void AWalledTownsRailYardStandsBehindItsPlatform(string spec, int people)
    {
        var town = Plan(spec, people);
        var plan = town.Plan;
        var yard = Assert.IsType<TownYard>(plan.Yard);
        var gate = plan.Bounds!.Gate;
        // The platform's side, behind it, short of the square's gate end and the quiet house; up to the first street.
        Assert.Equal(-plan.Square.Side, yard.Side);
        Assert.True(yard.Near >= 10 && yard.S0 >= 60 && yard.S1 <= plan.Square.S1 && yard.S1 - yard.S0 >= 60, $"{yard}");
        var first = plan.Bounds.Streets.Where(st => Math.Sign(st.D) == yard.Side).MinBy(st => Math.Abs(st.D))!;
        for (double s = yard.S0; s <= yard.S1; s += 5)
            Assert.True(Math.Abs(first.At(s)) - first.Width / 2 >= yard.Far - 1e-6, $"the first street runs into the yard at {s - gate:0}");
        Assert.True(yard.SidingD > yard.Near + 1.5 && yard.Siding0 > yard.S0 && yard.Siding1 < yard.S1);

        var pieces = plan.Fixtures.Where(f => yard.Holds(f.S, f.D)).ToList();
        foreach (string kind in new[] { "goodsshed", "yardoffice", "weighbridge", "freight", "buffers", "yardlamp" })
            Assert.Contains(pieces, f => f.Kind == kind);
        // Every piece inside the yard, and no two on each other (a thing facing along the line is turned a quarter).
        (double S, double D) Half(TownFixture f) => Math.Abs(f.FaceS) > Math.Abs(f.FaceD) ? (f.SolidD, f.SolidS) : (f.SolidS, f.SolidD);
        foreach (var f in pieces)
        {
            var (hs, hd) = Half(f);
            Assert.True(f.S - hs >= yard.S0 && f.S + hs <= yard.S1, $"{f.Kind} runs out of the yard along the line");
            Assert.True(Math.Abs(f.D) - hd >= yard.Near && Math.Abs(f.D) + hd <= yard.Far, $"{f.Kind} out of the yard across it");
        }
        for (int i = 0; i < pieces.Count; i++)
            for (int j = i + 1; j < pieces.Count; j++)
            {
                var ((a, ah), (b, bh)) = ((pieces[i], Half(pieces[i])), (pieces[j], Half(pieces[j])));
                Assert.False(Math.Abs(a.S - b.S) < ah.S + bh.S && Math.Abs(a.D - b.D) < ah.D + bh.D, $"{a.Kind} on {b.Kind}");
            }
        // The siding: its stock on it between its buffer stops, facing along it; nothing else on it but the lamps beside it.
        var buffers = pieces.Where(f => f.Kind == "buffers").OrderBy(f => f.S).ToList();
        Assert.Equal(2, buffers.Count);
        Assert.True(buffers[0].S < yard.Siding0 && buffers[1].S > yard.Siding1 && buffers[0].FaceS > 0 && buffers[1].FaceS < 0);
        var stock = pieces.Where(f => f.Kind is "van" or "flatdory" or "flatrails").ToList();
        Assert.InRange(stock.Count, Towns.Tuning.Walled.YardStock[0], Towns.Tuning.Walled.YardStock[1]);
        foreach (var f in stock)
        {
            Assert.Equal(yard.Side * yard.SidingD, f.D, 6);
            Assert.True(Math.Abs(f.FaceS) == 1 && f.S - f.SolidD >= yard.Siding0 && f.S + f.SolidD <= yard.Siding1, $"{f.Kind} off its siding");
        }
        foreach (var f in pieces.Except(stock).Except(buffers))
            Assert.True(Math.Abs(Math.Abs(f.D) - yard.SidingD) >= f.SolidD + 1.5, $"{f.Kind} on the siding");
        // The goods sheds front it: their docks at the stock's side.
        foreach (var shed in pieces.Where(f => f.Kind == "goodsshed"))
            Assert.InRange(Math.Abs(shed.D) - shed.SolidD - yard.SidingD, 1.5, 1.8);

        // Each is solid, and what has something to say says it, looked at from somewhere a crewmate can stand beside it: in
        // front, behind or at either end (freight at the siding's edge has the stock in front of it; the stock is looked at
        // from between the siding and the platform).
        foreach (var f in pieces)
        {
            Assert.False(town.Free(town.World(f.S, f.D)), $"{f.Kind} isn't solid");
            if (f.Text.Length == 0)
                continue;
            Assert.True(!f.Name.Contains('{') && !f.Text.Contains('{'), $"{f.Kind} says a placeholder");
            var at = town.LookAt(f);
            var (hs, hd) = Half(f);
            double d = Math.Abs(f.D);
            (double S, double D)[] stands = [(f.S + 0.8, d - hd - 1.2), (f.S + 0.8, d + hd + 1.2), (f.S + hs + 1.2, d), (f.S - hs - 1.2, d)];
            bool seen = stands.Any(p =>
            {
                var eye = town.World(p.S, yard.Side * p.D) + Double3.Up * 1.6;
                return town.Free(eye - Double3.Up * 1.0) && town.Target(eye, (at - eye).Normalized) is { Kind: TownTargetKind.Fixture } t && t.Index == f.Id;
            });
            Assert.True(seen, $"{f.Name} ({f.Kind}) can't be looked at from anywhere beside it");
        }

        // No house or yard of a house stands in it, and no lane runs through it.
        foreach (var h in plan.Houses)
            foreach (var p in h.Solids())
            {
                var (sa, da) = h.Rail(p.U0, p.V0);
                var (sb, db) = h.Rail(p.U1, p.V1);
                Assert.False(Math.Max(sa, sb) > yard.S0 && Math.Min(sa, sb) < yard.S1 && Math.Sign(da) == yard.Side
                    && Math.Max(Math.Abs(da), Math.Abs(db)) > yard.Near && Math.Min(Math.Abs(da), Math.Abs(db)) < yard.Far, $"house {h.Id} in the yard");
            }
        foreach (var lane in plan.Bounds.Lanes)
        {
            var (lo, hi) = lane.Span(yard.Side * yard.Near, yard.Side * yard.Far);
            Assert.False(hi > yard.S0 && lo < yard.S1, $"the lane at {lane.S - gate:0} runs through the yard");
        }

        // Its porters are at work there all night, off the siding, where they can stand, with something to say.
        var porters = plan.People.Where(p => p.Role == "porter" && yard.Holds(p.S, p.D)).ToList();
        Assert.Equal(Towns.Tuning.Walled.YardHands, porters.Count);
        foreach (var p in porters)
        {
            Assert.True(Math.Abs(Math.Abs(p.D) - yard.SidingD) > 2, $"{p.Name} on the siding");
            Assert.True(town.Free(town.World(p.S, p.D, 0.5)), $"{p.Name} stands in something");
            Assert.NotEmpty(p.Lines);
        }
    }

    [Fact]
    public void ASmallTownHasNoRailYard()
    {
        // A hamlet by the track, and a walled town of fewer than yardPeople: its first street's houses are all it has there.
        Assert.Null(Plan("frontier:7", 20).Plan.Yard);
        Assert.Null(Plan("frontier:7", 97).Plan.Yard);
    }

    [Fact]
    public void TheYardIsTheSameEveryTime()
    {
        string Layout(Town t) => string.Join(";", t.Plan.Fixtures.Where(f => t.Plan.Yard!.Holds(f.S, f.D)).Select(f => $"{f.Kind}@{f.S:0.000},{f.D:0.000}"));
        Assert.Equal(Layout(Plan("frontier:3", 1200)), Layout(Plan("frontier:3", 1200)));
    }
}
