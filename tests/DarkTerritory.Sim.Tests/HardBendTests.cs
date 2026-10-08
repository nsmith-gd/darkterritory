using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

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
            // One turn, or an S-bend's two (note 359): each boarded (an S's second by its first's), each round its hill.
            var turns = HardBends(route, c).Where(b => b.SMax >= p.S0 && b.SMax <= p.S1).ToList();
            Assert.Equal(p.Params.ContainsKey("sBend") ? 2 : 1, turns.Count);
            foreach (var (s0, sMax, k) in turns)
                BoardedRoundItsHill(s0, sMax, k);
        }

        void BoardedRoundItsHill(double s0, double sMax, double k)
        {
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

    [Fact]
    public void AnSBendIsHeldAsOneBendThroughBothItsTurns()
    {
        // Note 359: two hard turns either way with a short straight between. Boarded once and demanded once, its limit held
        // till the train's tail is round the second turn, so it passes the lethal spacing check (two demands inside a braking
        // distance didn't); a driver who brakes for the first has to hold it through the second, at a speed that takes both.
        var lg = LineGenContent.Load(Content);
        var c = lg.Config.Tiers.Curves;
        int seen = 0;
        foreach (var spec in new[] { "deepTerritory:1", "deepTerritory:2", "deepTerritory:3", "deadLines:1", "deadLines:2", "deadLines:4" })
        {
            var route = Routes.Generate(Content, spec, 6);
            var plan = route.Plan!;
            Assert.True(plan.Validation.Checks.Single(k => k.Name == "lethal spacing").Pass, $"{spec}: {plan.Validation.Checks.Single(k => k.Name == "lethal spacing").Detail}");
            foreach (var p in plan.Pieces.Where(p => p.Edge == "main" && p.Params.ContainsKey("sBend")))
            {
                var turns = HardBends(route, c).Where(b => b.SMax >= p.S0 && b.SMax <= p.S1).ToList();
                Assert.Equal(2, turns.Count);
                // Either way: one a left turn, the other a right.
                Assert.True(turns[0].K * turns[1].K < 0, $"{spec}: an S-bend at {p.S0:0} turns the same way twice");
                var demands = plan.Authority.Demands.Where(d => d is { Edge: "main", Type: DemandType.Curve } && d.SReq > p.S0 - 200 && d.SReq < turns[1].SMax).ToList();
                var d = Assert.Single(demands);
                Assert.True(d.SReq < turns[0].SMax, $"{spec}: the S-bend's demand at {d.SReq:0} is past its first turn's tightest at {turns[0].SMax:0}");
                // Its limit holds through the second turn, at what gets round the tighter of the two.
                var limit = plan.Authority.Limits.Single(l => l.Edge == "main" && Math.Abs(l.S0 - d.SReq) < 1);
                Assert.True(limit.S1 > turns[1].SMax + 50, $"{spec}: the S-bend's limit ends at {limit.S1:0}, before its second turn at {turns[1].SMax:0}");
                double derails = turns.Min(t => Math.Sqrt(c.ADerail / Math.Abs(t.K)));
                Assert.True(d.VReq < derails, $"{spec}: held at {d.VReq:0.0} m/s through turns that derail at {derails:0.0}");
                Assert.Contains("S-bend", limit.Why);
                seen++;
            }
        }
        Assert.True(seen >= 2, $"only {seen} S-bends on six deep nights");
    }

    [Fact]
    public void TheWarningTellsTheBendsTightestPointNotWhereItFirstBites()
    {
        // On full steam a hard bend's easing already bites before its arc: what's told must be what gets round all of it.
        var c = LineGenConfig.Load(Content).Tiers.Curves;
        var route = Routes.Generate(Content, "frontier:7", 6);
        var bend = HardBends(route, c).First(b => b.S0 > route.Plan!.GateM + 2000);
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), route.Build(), bend.S0 - 60);
        train.Dynamics.Velocity = Tuning.Train.MaxSpeed;
        var told = TrackRules.Assess(train, route.Plan!.Rules, Tuning.Train.Overspeed);
        Assert.True(told.Warning);
        Assert.False(told.OnIt);
        Assert.Equal(Math.Sqrt(c.ADerail / Math.Abs(bend.K)), told.DerailMs, 1);
        Assert.Equal(Math.Floor(Math.Sqrt(c.APost / Math.Abs(bend.K))), told.PostedMs);
    }

    [Theory]
    [InlineData("local:1")]
    [InlineData("local:3")]
    [InlineData("frontier:2")]
    [InlineData("frontier:7")]
    [InlineData("deadLines:3")]
    public void NoBranchCrossesTheMainLine(string spec)
    {
        // Found by the bends: an alternate shorter than the main line had the main line bow round toward it, not away (its
        // bow, right-positive like every branch's side, was added to a heading that turns left for positive), and crossed
        // it 300 m past its toe on most nights; and a hard bend in its window swung the main line across its way back.
        // Two tracks over each other, with no bridge and no diamond. Checked on the built rail, past each turnout.
        var route = Routes.Generate(Content, spec, 6);
        var line = route.Build();
        Assert.True(route.Plan!.Validation.Checks.Single(k => k.Name == "crossings").Pass);
        var main = Enumerable.Range(0, (int)(line.Length / 5)).Select(i => line.Sample(i * 5.0)).ToList();
        foreach (var b in line.Branches.Where(b => b.Kind is BranchKind.Alternate or BranchKind.DeadLine))
            for (double u = 200; u < b.Local.Length - (b.Rejoins ? 200 : 0); u += 20)
            {
                var q = b.Local.Sample(u).Position;
                var m = main.MinBy(m => (m.Position.X - q.X) * (m.Position.X - q.X) + (m.Position.Z - q.Z) * (m.Position.Z - q.Z))!;
                var d = q - m.Position;
                if (d.X * d.X + d.Z * d.Z > 100 * 100)
                    continue;
                var right = Double3.Cross(m.Tangent, Double3.Up).Normalized;
                Assert.True((d.X * right.X + d.Z * right.Z) * b.Side > -2, $"{spec}: branch {b.Index} ({b.Kind}, side {b.Side}) is over the main line {u:0} m along it");
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
