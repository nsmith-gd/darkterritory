using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Rail;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 278, bends worth braking for (the director, 6 Oct 2026: derailing on a bend is the core fear, and always the
/// driver's mistake). Every night carries its tier's count of hard bends, bends that derail the train under its top
/// speed, spread along it; each is boarded at what it takes, and the land shows why it bends: a hill on its inside.
/// </summary>
[Collection(nameof(LineGenTests))]
public class HardBendTests
{
    static readonly string Content = DataFile.FindContentRoot();

    /// <summary>The main line's hard bends between the threshold and the terminus: where each starts, its tightest point, and its signed curvature there.</summary>
    static List<(double S0, double SMax, double K)> HardBends(Route.Route route, CurveRules c)
    {
        var plan = route.Plan!;
        var main = plan.Edge("main");
        var line = new RailLine(new LineDefinition("main", main.Segments));
        var list = new List<(double, double, double)>();
        double start = -1, kMax = 0, sMax = 0;
        for (double s = plan.GateM; s <= Math.Min(line.Length, plan.TerminusM) + 5; s += 5)
        {
            double k = s <= line.Length ? line.Sample(s).Curvature : 0;
            if (Math.Abs(k) > 1e-9 && Math.Sqrt(c.ADerail / Math.Abs(k)) < c.BoardDerailBelow)
            {
                if (start < 0)
                    start = s;
                if (Math.Abs(k) > Math.Abs(kMax))
                    (kMax, sMax) = (k, s);
            }
            else if (start >= 0)
            {
                list.Add((start, sMax, kMax));
                (start, kMax) = (-1, 0);
            }
        }
        return list;
    }

    [Theory]
    [InlineData("local:1")]
    [InlineData("frontier:2")]
    [InlineData("frontier:7")]
    [InlineData("deadLines:3")]
    [InlineData("deepTerritory:4")]
    public void EveryNightHasItsTiersHardBendsSpreadAlongIt(string spec)
    {
        var lg = LineGenContent.Load(Content);
        var c = lg.Config.Tiers.Curves;
        var route = Routes.Generate(Content, spec, 6);
        var plan = route.Plan!;
        var limits = new Limits(lg, RunParameters.Parse(spec, 6));
        var bends = HardBends(route, c);
        Assert.True(bends.Count >= Math.Floor(limits.Bends[0]), $"{spec}: {bends.Count} hard bends against the tier's {limits.Bends[0]:0.#}");
        Assert.True(plan.Validation.Checks.Single(k => k.Name == "hard bends").Pass);

        // The tier's own bends, each at a radius from its derailing speeds (or its minimum radius, where that's gentler).
        var pieces = plan.Pieces.Where(p => p.Edge == "main" && p.Params.ContainsKey("hardBend")).ToList();
        Assert.NotEmpty(pieces);
        double hi = Math.Max(limits.BendDerail[1], Math.Sqrt(c.ADerail * Math.Ceiling(limits.MinRadius)));
        Assert.All(pieces, p => Assert.InRange(p.Params["derailMs"], limits.BendDerail[0] - 0.1, hi + 0.1));

        // Spread along the night: with three or more, one in each half of the line between the threshold and the home straight.
        if (pieces.Count >= 3)
        {
            double mid = (plan.GateM + plan.TerminusM) / 2;
            Assert.Contains(pieces, p => p.S1 < mid);
            Assert.Contains(pieces, p => p.S0 > mid);
        }
    }

    [Theory]
    [InlineData("frontier:7")]
    [InlineData("deadLines:3")]
    public void AHardBendIsBoardedAndGoesRoundAHill(string spec)
    {
        var lg = LineGenContent.Load(Content);
        var c = lg.Config.Tiers.Curves;
        var route = Routes.Generate(Content, spec, 6);
        var plan = route.Plan!;
        var boards = plan.Signage.Where(s => s is { Edge: "main", Type: "speedBoard", Required: true }).ToList();
        foreach (var p in plan.Pieces.Where(p => p.Edge == "main" && p.Params.ContainsKey("hardBend")))
        {
            var (s0, sMax, k) = HardBends(route, c).Single(b => b.SMax >= p.S0 && b.SMax <= p.S1);
            double derails = Math.Sqrt(c.ADerail / Math.Abs(k));
            // A board before it, at what it takes (§8.5's floor(√(a_post R))), never over what derails it.
            Assert.True(boards.Any(b => b.S <= s0 + 1 && b.S >= s0 - 1500 && b.Value is { } v && v <= Math.Floor(Math.Sqrt(c.APost / Math.Abs(k))) && v < derails),
                $"{spec}: no board before the hard bend at {s0:0} (derails at {derails:0.0} m/s)");
            // The hill it goes round on the inside, the fall on the outside.
            var intent = plan.Intents.Single(i => i.Edge == "main" && sMax >= i.S0 && sMax < i.S1);
            var (inside, outside) = k > 0 ? (intent.Left, intent.Right) : (intent.Right, intent.Left);
            Assert.Equal(IntentType.LedgeUp, inside.T);
            Assert.True(inside.H > 0);
            Assert.Equal(IntentType.Embankment, outside.T);
        }
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(3UL)]
    [InlineData(8UL)]
    public void AFrontierNightAlwaysHasSomewhereToComeOff(ulong seed)
    {
        // Note 265's open call: with the Sleepers gone a Frontier night could have had no point where the train derails.
        var c = LineGenConfig.Load(Content).Tiers.Curves;
        var route = Routes.Generate(Content, Route.RouteTier.Frontier, seed, 10);
        var bends = HardBends(route, c);
        Assert.True(bends.Count >= 3, $"frontier:{seed}: {bends.Count} hard bends");
        Assert.Contains(bends, b => Math.Sqrt(c.ADerail / Math.Abs(b.K)) < Tuning.Train.MaxSpeed - 3);
    }
}
