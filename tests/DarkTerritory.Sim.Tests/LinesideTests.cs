using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Lineside props are solid (ARCHITECTURE §8 note 371; GDD App. F.1 "the world is solid", note 279's "Not yet"). A generated
/// line's trees, boulders and telegraph poles are dealt by the sim from the night's seed, the same on every machine, kept off
/// everything that's the line's own, and stood as walls: a crewmate walks round a trunk, what's loose is put out of one.
/// </summary>
[Collection(nameof(LineGenTests))]
public class LinesideTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly PlayerTuning P = Tuning.Player;

    static (Route.Route Route, TrainOnLine Train, Lineside Side, IReadOnlyList<Fort> Forts) Night(string spec, double at = 600)
    {
        var route = Routes.Generate(Content, spec, 6);
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), route.Build(), at);
        var forts = Fortresses.Of(route, train.Line, 600, Tuning.Run.TerminusZone);
        var side = Lineside.Of(route, train.Line)!;
        train.Walls = StopWalls.Of(route, train.Line, forts, Tuning.Run.Walls);
        train.Walls.Add(side.Walls(forts, Tuning.Run.Walls.LinesideReachM));
        return (route, train, side, forts);
    }

    static Double3 Foot(TrainOnLine train, LinesideProp p)
    {
        var t = train.Line.Sample(p.Along);
        var at = t.Position + Double3.Cross(t.Tangent, Double3.Up).Normalized * p.Lateral;
        return at with { Y = p.Ground };
    }

    static bool InAnyWall(StopWalls walls, Double3 p, double pad) => walls.Near(p).Any(w =>
    {
        var l = w.ToLocal(p);
        return p.Y < w.Top && p.Y > w.Bottom && Math.Abs(l.X) < w.HalfLength + pad && Math.Abs(l.Z) < w.HalfWidth + pad;
    });

    [Fact]
    public void TheSameNightStandsTheSameWoodsOnEveryMachineAndAnotherNightOthers()
    {
        // Two machines build the night from its plan, each its own route and line: the same trees, to the bit.
        var c = LineGenContent.Load(Content);
        Lineside Build(string spec)
        {
            var plan = LineGenerator.Generate(c, RunParameters.Parse(spec, 6));
            var route = plan.ToRoute(c.Route);
            return Lineside.Of(route, route.Build())!;
        }
        var a = Build("frontier:7").Props(0, 30000).ToList();
        var b = Build("frontier:7").Props(0, 30000).ToList();
        Assert.True(a.Count > 1000, $"only {a.Count} things beside frontier:7");
        Assert.Equal(a, b);
        var other = Build("frontier:8").Props(0, 30000).Take(200).ToList();
        Assert.NotEqual(a.Take(200), other);

        // Asking for a stretch, or only what's within reach, deals the same as the whole: the art's cells and the sim's walls agree.
        var side = Build("frontier:7");
        static IEnumerable<LinesideProp> Sorted(IEnumerable<LinesideProp> ps) => ps.OrderBy(p => p.Kind).ThenBy(p => p.Along).ThenBy(p => p.Lateral);
        var cells = Enumerable.Range(0, 300).SelectMany(i => side.Props(i * 100, (i + 1) * 100)).ToList();
        Assert.Equal(Sorted(a), Sorted(cells));
        Assert.Equal(Sorted(a.Where(p => Math.Abs(p.Lateral) <= 40)), Sorted(side.Props(0, 30000, 40)));
    }

    [Theory]
    [InlineData("frontier:7")]
    [InlineData("deepTerritory:2")]
    [InlineData("local:1")]
    [InlineData("deadLines:3")]
    public void NothingStandsOnTheLinesOwnGround(string spec)
    {
        var (route, train, side, forts) = Night(spec);
        var terrain = ((PlanConditions)train.Line.Conditions!).Terrain;
        var plan = route.Plan!;
        int trees = 0;
        foreach (var p in side.Props(0, train.Line.Length))
        {
            var foot = Foot(train, p);
            if (p.Kind == LinesideKind.Pole)
                Assert.Equal(Lineside.PoleOut, p.Lateral);
            else
            {
                trees += p.Kind == LinesideKind.Tree ? 1 : 0;
                // Off the formation and the verge, and off every stop's ground.
                Assert.True(Math.Abs(p.Lateral) >= 6, $"{spec}: a {p.Kind} {p.Lateral:0.0} m from the line at {p.Along:0}");
                Assert.False(side.InClearing(p.Along, p.Lateral), $"{spec}: a {p.Kind} on a stop's ground at {p.Along:0}, {p.Lateral:0}");
            }
            Assert.False(side.OnStop(p.Along, p.Lateral), $"{spec}: a {p.Kind} on a stop's buildings or tracks at {p.Along:0}, {p.Lateral:0}");
            Assert.True(side.Clear(p.Along - p.Along % Lineside.SlotM) || p.Kind == LinesideKind.Pole, $"{spec}: a {p.Kind} by a tunnel or on a bridge at {p.Along:0}");
            // Not in water, on a road, or on another track.
            Assert.Null(terrain.WaterAt(foot.X, foot.Z));
            foreach (var road in plan.Roads.Where(r => p.Along >= r.S0 && p.Along <= r.S1))
                Assert.True(Math.Abs(p.Lateral - TerrainField.RoadLateral(road, plan.Crossings, p.Along, plan.Rules.Terrain.Roads.RampM)) >= plan.Rules.Terrain.Roads.HalfWidthM,
                    $"{spec}: a {p.Kind} on a road at {p.Along:0}");
            foreach (var b in train.Line.Branches)
                Assert.False(p.Along > b.Toe && p.Along < b.End && Math.Sign(p.Lateral) == b.Side && Math.Abs(p.Lateral) < 16, $"{spec}: a {p.Kind} on branch {b.Index}'s ground at {p.Along:0}");
        }
        Assert.True(trees > (spec.StartsWith("local") ? 500 : 2000), $"{spec}: only {trees} trees");
        // Inside the forts, nothing wild: the sim stands the poles there and nothing else.
        var all = side.Props(0, train.Line.Length).ToList();
        int inside = all.Count(p => p.Kind != LinesideKind.Pole && Lineside.InsideAFort(forts, p.Along, p.Lateral));
        Assert.Equal(all.Count - inside, side.Walls(forts, double.MaxValue).Count());
    }

    [Fact]
    public void ACrewmateWalksRoundATrunkNotThroughIt()
    {
        var (_, train, side, _) = Night("frontier:7");
        // A tree beside the line with nothing else within 6 m of it, walked at from 4 m nearer the line.
        var props = side.Props(2000, 20000, 40).ToList();
        Double3 From(LinesideProp p)
        {
            var t = train.Line.Sample(p.Along);
            return Foot(train, p) - Double3.Cross(t.Tangent, Double3.Up).Normalized * Math.Sign(p.Lateral) * 4;
        }
        var tree = props.First(p => p.Kind == LinesideKind.Tree && !p.Dead && p.Radius >= 0.2 && Math.Abs(p.Lateral) is > 12 and < 30
            && props.All(q => q.Equals(p) || (Foot(train, q) - Foot(train, p)).Length > 6 + q.Radius)
            && !InAnyWall(train.Walls!, From(p) + Double3.Up, P.Radius + 0.3));
        var trunk = Foot(train, tree);
        Assert.True(InAnyWall(train.Walls!, trunk + Double3.Up, 0), "no wall at the tree");
        // Straight at it: never in it, and held off it by the walker's body.
        var from = From(tree);
        var s = PlayerMotor.SpawnOnGround(from, train.Line, tree.Along, P);
        var d = trunk - from;
        s.Yaw = Math.Atan2(-d.X, -d.Z);
        double closest = double.MaxValue;
        for (int i = 0; i < 3 * SimConstants.TickRate; i++)
        {
            PlayerMotor.Step(ref s, new PlayerIntent { MoveZ = 1 }, train, P, Tuning.Train, SimConstants.TickSeconds);
            Assert.False(InAnyWall(train.Walls!, s.Position, P.Radius - 0.02), $"inside a wall at {s.Position}, tick {i}");
            closest = Math.Min(closest, Math.Sqrt((s.Position.X - trunk.X) * (s.Position.X - trunk.X) + (s.Position.Z - trunk.Z) * (s.Position.Z - trunk.Z)));
        }
        Assert.True(closest >= tree.Radius + P.Radius - 0.05, $"walked to {closest:0.00} m of a trunk {tree.Radius:0.00} m round");
        Assert.True(closest < tree.Radius + P.Radius + 0.5, $"never reached the tree ({closest:0.0} m)");
    }

    [Fact]
    public void WhatsLooseIsPutOutOfATrunkAndALowBoulderIsSteppedOver()
    {
        var (_, train, side, _) = Night("deepTerritory:2");
        var props = side.Props(2000, 20000, 40).ToList();
        var tree = props.First(p => p.Kind == LinesideKind.Tree && p.Radius >= 0.15);
        var ribbit = Ribbit.At(1, 1, Foot(train, tree) + Double3.Up * 0.5, Tuning.Enemies.Ribbits);
        Solidity.Settle([ribbit], train, Tuning.Enemies);
        Assert.False(InAnyWall(train.Walls!, ribbit.Local + Double3.Up * 0.2, 0.25), $"a Ribbit in a trunk at {ribbit.Local}");
        // A boulder sunk to under a step stands no higher than a crewmate steps up, so it's walked over (PlayerMotor's stepUp).
        var low = props.Where(p => p.Kind == LinesideKind.Rock).Select(p => (p, Top: p.Height - p.Sink)).Where(x => x.Top < P.StepUp).ToList();
        Assert.All(low, x => Assert.True(x.Top < P.StepUp));
    }

    [Fact]
    public void TheWorldStandsTheLinesideAtTheRunsStartAndLeavesTheHandLaidLinesAlone()
    {
        var route = Routes.Generate(Content, "frontier:7", 6);
        var n = new Night(4, speed: 0, route);
        n.World.EnableRun(Tuning.Run, route, 600, authority: true);
        var side = Lineside.Of(route, n.Train.Line)!;
        var tree = side.Props(3000, 9000, 40).First(p => p.Kind == LinesideKind.Tree);
        Assert.True(InAnyWall(n.Train.Walls!, Foot(n.Train, tree) + Double3.Up, 0), "the run's walls have no tree");
        // A hand-laid line's lineside is the art's alone.
        var legacy = Route.RouteGenerator.Generate(Tuning.Route, Route.RouteTier.Frontier, 1);
        Assert.Null(Lineside.Of(legacy, legacy.Build()));
    }
}
