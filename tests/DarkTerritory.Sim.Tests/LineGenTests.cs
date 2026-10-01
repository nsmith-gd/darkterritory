using Ballast;
using Ballast.Net;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The line generator against its own acceptance (docs/design/linegen-plan.md §21): the same seed is the same line,
/// byte for byte (M0); a train drives a generated line end to end, its grades and curves in the sim (M1); there's
/// ground wherever a player can walk, and a client's copy of the plan builds the host's terrain exactly (M2); nights
/// pass validation within two attempts (M3); and the line's own rules hold the train to its authority.
/// </summary>
/// <remarks>
/// A collection of its own that doesn't run in parallel: xunit runs it after the parallel classes, so the generations
/// (each a couple of seconds of solid CPU) don't land on top of the other assemblies' wall-clock tests at startup
/// (CI's Windows runner has four cores: UdpTransportTests' loopback ping and CreatureArtTests' frame time both starved).
/// </remarks>
[Collection(nameof(LineGenTests))]
[CollectionDefinition(nameof(LineGenTests), DisableParallelization = true)]
public class LineGenTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TrainTuning T = Tuning.Train;

    [Fact]
    public void TheSameSeedIsTheSameLineByteForByte()
    {
        // Content loaded twice over (nothing cached between them), one attempt each: every stage, validation included.
        var p = RunParameters.Parse("local:3", 6);
        var a = LineGenerator.Attempt(LineGenContent.Load(Content), p, 0).Plan!;
        var b = LineGenerator.Attempt(LineGenContent.Load(Content), p, 0).Plan!;
        Assert.Equal(a.ToJson(), b.ToJson());
        // And what a client receives builds the same plan again.
        Assert.Equal(a.ToJson(), LinePlan.FromJson(a.ToJson()).ToJson());
        // Another seed is another line.
        var c = LineGenerator.Attempt(LineGenContent.Load(Content), RunParameters.Parse("local:4", 6), 0).Plan!;
        Assert.NotEqual(a.ToJson(), c.ToJson());
    }

    [Theory]
    [InlineData("local:1", 10)]
    [InlineData("frontier:2", 10)]
    [InlineData("deadLines:3", 10)]
    [InlineData("deepTerritory:4", 10)]
    [InlineData("frontier:5", 3)]
    [InlineData("frontier:6", 20)]
    public void NightsPassValidationWithinTwoAttempts(string spec, int cars)
    {
        var plan = Routes.Generate(Content, spec, cars).Plan!;
        var v = plan.Validation;
        Assert.True(v.Passed, string.Join("; ", v.Checks.Where(c => !c.Pass).Select(c => $"{c.Name}: {c.Detail}")));
        Assert.InRange(v.Attempts, 1, 2);
        Assert.False(v.Fallback);
        // Zero tell violations (§16.3): every demand has its tell in time, every sign where the authority says.
        Assert.All(v.Checks.Where(c => c.Name.Contains("tell", StringComparison.OrdinalIgnoreCase)), c => Assert.True(c.Pass, c.Detail));
        // §17.5: small enough to send a joiner whole.
        Assert.True(plan.CompressedBytes() < 500 * 1024);
    }

    [Fact]
    public void ATrainDrivesAGeneratedLineEndToEnd()
    {
        var route = Routes.Generate(Content, "frontier:7", 6);
        var plan = route.Plan!;
        var line = route.Build();
        // Grades and curves are the sim's: the line climbs and bends, and the plan's main line is the rail model's.
        Assert.Equal(plan.LengthM, line.Length, 1);
        var samples = Enumerable.Range(0, (int)(line.Length / 10)).Select(i => line.Sample(i * 10.0)).ToList();
        Assert.Contains(samples, s => Math.Abs(s.GradePercent) > 0.5);
        Assert.Contains(samples, s => Math.Abs(s.Curvature) > 1 / 2000.0);
        // The ideal driver (§16.1) on the real train, from the yard to the terminus, stopping at every facility.
        var way = new PlanRouteWay(plan, line, []);
        var profile = new SpeedProfile(way, plan.Authority.BrakeMs2, []);
        var drive = Drivers.Drive(way, profile, T, 6, new Drivers.Options(false, 1, 0, [], 3600, plan.Rules.ADerail, 0.5,
            400, plan.GateM, plan.Terminus.GateM));
        Assert.True(drive.Survived, drive.Failure);
        Assert.True(drive.TransitSeconds < plan.RouteCard.DawnS, $"{drive.TransitSeconds:0} s against a {plan.RouteCard.DawnS:0} s dawn");
        // The route the rest of the game sees: its gate, its facilities (each with its spur's points in its zone).
        Assert.Equal(plan.GateM, route.GateOr(0));
        Assert.Equal(plan.Pois.Count, route.Of(FeatureKind.Facility).Count());
        var run = new Run.Run(Tuning.Run, route);
        for (int i = 0; i < plan.Pois.Count; i++)
            Assert.Equal(plan.Pois[i].SpurEdge is not null, run.SpurOf(i) >= 0);
    }

    [Fact]
    public void ThereIsGroundWithinReachOfTheTrackAndTheFormationIsAtRailHeight()
    {
        var route = Routes.Generate(Content, "frontier:7", 6);
        var line = route.Build();
        var terrain = ((PlanConditions)line.Conditions!).Terrain;
        for (double s = 0; s < line.Length; s += 500)
        {
            var t = line.Sample(s);
            var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
            // On the ballast either side, the ground is the rail's height: people stand where they always have.
            foreach (double lateral in new[] { -2.0, 2.0 })
            {
                double g = line.Conditions!.Ground(t.Position + right * lateral);
                if (route.InTunnel(s) || route.BridgeAt(s) is not null)
                    continue;
                Assert.InRange(g, t.Position.Y - 0.35, t.Position.Y + 0.05);
            }
            // Out to 250 m (§21 M2: walk out anywhere and find ground), finite and within the land's own range.
            foreach (double lateral in new[] { -250.0, -120, -40, 40, 120, 250 })
            {
                var p = t.Position + right * lateral;
                double h = terrain.Height(p.X, p.Z);
                Assert.True(double.IsFinite(h), $"no ground at {s:0} m, {lateral} m out");
                Assert.InRange(h - t.Position.Y, -200, 200);
            }
        }
    }

    [Fact]
    public void AClientsCopyOfThePlanBuildsTheHostsTerrainExactly()
    {
        // §17.3: the host sends the plan; the joiner rebuilds line and terrain from it. Tile checksums have to match.
        var host = Routes.Generate(Content, "frontier:7", 6);
        var client = Routes.FromPlan(Content, LinePlan.FromJson(host.Plan!.ToJson()));
        var a = ((PlanConditions)host.Build().Conditions!).Terrain;
        var b = ((PlanConditions)client.Build().Conditions!).Terrain;
        var line = host.Build();
        var tiles = new HashSet<(int, int)>();
        for (double s = 0; s < line.Length && tiles.Count < 40; s += 600)
        {
            var p = line.Sample(s).Position;
            tiles.Add(a.TileOf(p.X, p.Z));
        }
        foreach (var (x, z) in tiles)
            Assert.Equal(a.TileChecksum(x, z), b.TileChecksum(x, z));
        // And the fingerprint a joiner is checked by on the way in.
        Assert.Equal(a.Print(), b.Print());
    }

    [Fact]
    public void OnAPerfectLinkPredictionMatchesTheHostOnAGeneratedLine()
    {
        // The line's conditions (wet rail, brass drag, terrain ground) are in every machine's sim alike, so a client
        // predicting its own crew on a generated line is as exact as on the test loop (NetcodeTests).
        var route = Routes.Generate(Content, "frontier:7", 6);
        var r = Harness.Run(route.Build(), T, Tuning.Player, new HarnessOptions
        {
            Bots = 4,
            Cars = 6,
            Seconds = 60,
            Link = LinkConditions.Perfect,
            StartDistance = 400,
            Route = route,
            Run = Tuning.Run,
            YardLength = route.GateOr(0),
        });
        Assert.All(r.Clients, c => Assert.True(c.MaxCorrectionM < 0.001, $"player {c.Id} corrected by {c.MaxCorrectionM} m"));
        Assert.Equal(0, r.Deaths);
    }

    [Fact]
    public void ACurveTakenAboveItsDerailSpeedDerails()
    {
        // The main line's tightest curve, past the yard: √(a_derail R) is lethal there (§7.3). The first frontier night with
        // one the train can overspeed (below its top speed: a gentle night's line has none).
        Route.Route route = null!;
        LinePlan plan = null!;
        double tightest = 0, s0 = 0, s1 = 0, lethal = double.MaxValue;
        for (int seed = 7; seed < 27 && lethal + 2 > T.MaxSpeed; seed++)
        {
            route = Routes.Generate(Content, $"frontier:{seed}", 6);
            plan = route.Plan!;
            double at = 0;
            tightest = 0;
            foreach (var seg in route.Line.Segments)
            {
                if (at > plan.GateM && seg.EndRadius is null && Math.Abs(seg.Curvature) > tightest)
                    (tightest, s0, s1) = (Math.Abs(seg.Curvature), at, at + seg.Length);
                at += seg.Length;
            }
            lethal = tightest > 0 ? Math.Sqrt(plan.Rules.ADerail / tightest) : double.MaxValue;
        }
        Assert.True(lethal + 2 <= T.MaxSpeed, "no frontier night with a curve the train can overspeed");
        var line = route.Build();
        var (world, train) = Night(route, 6, s0 - 150, lethal + 2);
        for (int i = 0; i < 60 * SimConstants.TickRate && !world.Derailed && train.Dynamics.Distance < s1; i++)
            world.Step(new TrainControls { Reverser = 1 });
        Assert.True(world.Derailed, $"took a {1 / tightest:0} m curve at {train.Dynamics.Speed:0.0} m/s");
        Assert.Contains("curve", world.DerailCause);

        // At the speed the line communicates there it's taken safely.
        double posted = new PlanRouteWay(plan, line, []).Communicated((s0 + s1) / 2);
        Assert.True(posted < lethal);
        (world, train) = Night(route, 6, s0 - 150, posted);
        for (int i = 0; i < 60 * SimConstants.TickRate && !world.Derailed && train.Dynamics.Distance < s1; i++)
            world.Step(new TrainControls { Reverser = 1, Brake = train.Dynamics.Speed > posted ? 1 : 0 });
        Assert.False(world.Derailed, world.DerailCause);
    }

    [Fact]
    public void AWeakBridgeGivesWayUnderTheFirstCarPastItsLimitAndAWashoutDerails()
    {
        // deadLines:1 planned for six cars has a main-line trestle rated for six (§7.3, §22.5), and a washout on the main
        // line with the switch set around it.
        // The first Dead Lines night with both on its main line (which seed that is moves with the tuning the planner reads).
        var route = Enumerable.Range(1, 40).Select(seed => Routes.Generate(Content, $"deadLines:{seed}", 6))
            .First(r => r.Plan!.Structures.Any(s => s.Edge == "main" && s.Weak is not null)
                && r.Plan.Structures.Any(s => s.Type == StructureType.Washout && s.Edge == "main"));
        var plan = route.Plan!;
        var bridge = plan.Structures.First(s => s.Edge == "main" && s.Weak is not null);
        int limit = bridge.Weak!.MaxCars;

        var (world, train) = Night(route, limit, bridge.S0 - 40, bridge.Weak.SpeedMs);
        for (int i = 0; i < 90 * SimConstants.TickRate && !world.Derailed && train.Dynamics.RearDistance < bridge.S1 + 5; i++)
            world.Step(new TrainControls { Reverser = 1, Throttle = 0.3, Brake = train.Dynamics.Speed > bridge.Weak.SpeedMs ? 1 : 0 });
        Assert.False(world.Derailed, world.DerailCause);

        (world, train) = Night(route, limit + 2, bridge.S0 - 40, bridge.Weak.SpeedMs);
        for (int i = 0; i < 90 * SimConstants.TickRate && !world.Derailed && train.Dynamics.RearDistance < bridge.S1 + 5; i++)
            world.Step(new TrainControls { Reverser = 1, Throttle = 0.3, Brake = train.Dynamics.Speed > bridge.Weak.SpeedMs ? 1 : 0 });
        Assert.True(world.Derailed);
        Assert.Contains($"car {limit + 1}", world.DerailCause);

        // The washout: its junction starts the night set for the alternate; on the main line past the points, it's the end.
        var washout = plan.Structures.First(s => s.Type == StructureType.Washout && s.Edge == "main");
        var fork = plan.Alignment.Where(a => a.Role == EdgeRole.Alternate && a.Toe < washout.S0 && a.Rejoin > washout.S1).OrderByDescending(a => a.Toe).First();
        Assert.True(route.Build().Branches[fork.Branch].Definition.StartsDiverging);
        (world, train) = Night(route, 6, washout.S0 - 30, 4);
        for (int i = 0; i < 30 * SimConstants.TickRate && !world.Derailed; i++)
            world.Step(new TrainControls { Reverser = 1, Throttle = 0.3 });
        Assert.True(world.Derailed);
        Assert.Contains("washout", world.DerailCause);
    }

    /// <summary>A host's world on the route: <paramref name="cars"/> cars, the engine's front at <paramref name="at"/> on the main line.</summary>
    static (World World, TrainOnLine Train) Night(Route.Route route, int cars, double at, double speed)
    {
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, cars, 1)), route.Build(), at);
        train.Dynamics.Velocity = speed;
        var world = new World(train);
        world.EnableBodies();
        world.EnableRun(Tuning.Run, route, route.GateOr(0), authority: true);
        return (world, train);
    }
}
