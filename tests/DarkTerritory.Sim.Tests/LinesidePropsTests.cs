using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The lineside is solid (ARCHITECTURE §8 notes 371 and 389; GDD App. F.1 "the world is solid", note 279's "Not yet"). A
/// generated line's trees, boulders and telegraph poles, and the rest of what stands beside it (the road's homesteads, poles
/// and cars, the shore's sheds and rocks, the biomes' buildings and walls), are dealt by the sim from the night's seed, the
/// same on every machine, kept off everything that's the line's own, and stood as walls: a crewmate walks round a trunk and
/// is stopped by a house, what's loose is put out of one.
/// </summary>
[Collection(nameof(LineGenTests))]
public class LinesidePropsTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly PlayerTuning P = Tuning.Player;

    static (Route.Route Route, TrainOnLine Train, LinesideProps Side, IReadOnlyList<Fort> Forts) Night(string spec, double at = 600)
    {
        var route = Routes.Generate(Content, spec, 6);
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), route.Build(), at);
        var forts = Fortresses.Of(route, train.Line, 600, Tuning.Run.TerminusZone);
        var side = LinesideProps.Of(route, train.Line)!;
        train.Walls = StopWalls.Of(route, train.Line, forts, Tuning.Run.Walls);
        train.Walls.Add(side.Walls(forts, Tuning.Run.Walls.LinesideReachM));
        train.Walls.Add(side.BranchWalls(Tuning.Run.Walls.LinesideReachM));
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

    /// <summary>The woods' own, dealt by the slot (note 371): a tree of the biome's flora, a boulder, a telegraph pole.</summary>
    static bool OfTheWoods(LinesideProp p) => p.Kind == LinesideKind.Pole || (p.Kind == LinesideKind.Rock && p.Species == "rock")
        || (p.Kind == LinesideKind.Tree && p.Species is not ("fieldSpruce" or "apple"));

    /// <summary>The shore's own, which stand at and in its water (note 389): its sheds, wharves and lighthouses, its ledges and boulders.</summary>
    static bool OfTheShore(LinesideProp p) => p.Species is "fishShed" or "wharf" or "lighthouse" or "ledge" or "weed" or "boulder" or "redBoulder";

    /// <summary>How many walls the lineside stands outside the forts (the telegraph poles run on through them).</summary>
    static int WallsOutsideForts(LinesideProps side, IEnumerable<LinesideProp> props, IReadOnlyList<Fort> forts) =>
        props.Where(p => p.Kind == LinesideKind.Pole || !LinesideProps.InsideAFort(forts, p.Along, p.Lateral)).Sum(p => side.WallsOf(p).Count());

    [Fact]
    public void TheSameNightStandsTheSameWoodsOnEveryMachineAndAnotherNightOthers()
    {
        // Two machines build the night from its plan, each its own route and line: the same trees, to the bit.
        var c = LineGenContent.Load(Content);
        LinesideProps Build(string spec)
        {
            var plan = LineGenerator.Generate(c, RunParameters.Parse(spec, 6));
            var route = plan.ToRoute(c.Route);
            return LinesideProps.Of(route, route.Build())!;
        }
        var a = Build("frontier:7").Props(0, 30000).ToList();
        var b = Build("frontier:7").Props(0, 30000).ToList();
        Assert.True(a.Count > 1000, $"only {a.Count} things beside frontier:7");
        Assert.Equal(a, b);
        var other = Build("frontier:8").Props(0, 30000).Take(200).ToList();
        Assert.NotEqual(a.Take(200), other);

        // Asking for a stretch, or only what's within reach, deals the same as the whole: the art's cells and the sim's walls
        // agree. Within reach is all that stands there; nothing's dealt that the whole doesn't have.
        static IEnumerable<LinesideProp> Sorted(IEnumerable<LinesideProp> ps) => ps.OrderBy(p => p.Kind).ThenBy(p => p.Along).ThenBy(p => p.Lateral).ThenBy(p => p.Species, StringComparer.Ordinal);
        foreach (var spec in new[] { "frontier:7", "local:1" })
        {
            var side = Build(spec);
            var whole = side.Props(0, 30000).ToList();
            var cells = Enumerable.Range(0, 300).SelectMany(i => side.Props(i * 100, (i + 1) * 100)).ToList();
            Assert.Equal(Sorted(whole), Sorted(cells));
            var near = side.Props(0, 30000, 40).ToList();
            Assert.Equal(Sorted(whole.Where(p => Math.Abs(p.Lateral) <= 40)), Sorted(near.Where(p => Math.Abs(p.Lateral) <= 40)));
            var all = whole.ToHashSet();
            Assert.All(near, p => Assert.Contains(p, all));
            Assert.Contains(near, p => p.Kind == LinesideKind.Piece);
        }
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
        foreach (var p in side.Props(0, train.Line.Length).Where(OfTheWoods))
        {
            var foot = Foot(train, p);
            if (p.Kind == LinesideKind.Pole)
                Assert.Equal(LinesideProps.PoleOut, p.Lateral);
            else
            {
                trees += p.Kind == LinesideKind.Tree ? 1 : 0;
                // Off the formation and the verge, and off every stop's ground.
                Assert.True(Math.Abs(p.Lateral) >= 6, $"{spec}: a {p.Kind} {p.Lateral:0.0} m from the line at {p.Along:0}");
                Assert.False(side.InClearing(p.Along, p.Lateral), $"{spec}: a {p.Kind} on a stop's ground at {p.Along:0}, {p.Lateral:0}");
            }
            Assert.False(side.OnStop(p.Along, p.Lateral), $"{spec}: a {p.Kind} on a stop's buildings or tracks at {p.Along:0}, {p.Lateral:0}");
            Assert.True(side.Clear(p.Along - p.Along % LinesideProps.SlotM) || p.Kind == LinesideKind.Pole, $"{spec}: a {p.Kind} by a tunnel or on a bridge at {p.Along:0}");
            // Not in water, on a road, or on another track.
            Assert.Null(terrain.WaterAt(foot.X, foot.Z));
            foreach (var road in plan.Roads.Where(r => p.Along >= r.S0 && p.Along <= r.S1))
                Assert.True(Math.Abs(p.Lateral - TerrainField.RoadLateral(road, plan.Crossings, p.Along, plan.Rules.Terrain.Roads.RampM)) >= plan.Rules.Terrain.Roads.HalfWidthM,
                    $"{spec}: a {p.Kind} on a road at {p.Along:0}");
            foreach (var b in train.Line.Branches)
                Assert.False(p.Along > b.Toe && p.Along < b.End && Math.Sign(p.Lateral) == b.Side && Math.Abs(p.Lateral) < 16, $"{spec}: a {p.Kind} on branch {b.Index}'s ground at {p.Along:0}");
        }
        Assert.True(trees > (spec.StartsWith("local") ? 500 : 2000), $"{spec}: only {trees} trees");
        // The rest (note 389): a building, a stone wall, an old field's spruce, an orchard's tree, an erratic or an outcrop,
        // a road's pole or a fence post stands on dry ground off every stop's, every branch's and the road itself; the
        // shore's own stand at its water's edge and in it, off a stop's buildings and tracks. A road's car is on its verge.
        foreach (var p in side.Props(0, train.Line.Length).Where(p => !OfTheWoods(p)))
        {
            var foot = Foot(train, p);
            if (p.Species is not ("wharf" or "ledge" or "weed" or "boulder" or "redBoulder"))
            {
                Assert.False(side.OnStop(p.Along, p.Lateral), $"{spec}: a {p.Species} on a stop's buildings or tracks at {p.Along:0}, {p.Lateral:0}");
                Assert.False(side.InClearing(p.Along, p.Lateral) && p.Species is not ("fishShed" or "lighthouse"), $"{spec}: a {p.Species} on a stop's ground at {p.Along:0}, {p.Lateral:0}");
            }
            if (OfTheShore(p) || p.Species is "car" or "woodpile" or "stoneWall")
                continue;
            Assert.Null(terrain.WaterAt(foot.X, foot.Z));
            foreach (var road in plan.Roads.Where(r => p.Along >= r.S0 && p.Along <= r.S1))
                Assert.True(Math.Abs(p.Lateral - TerrainField.RoadLateral(road, plan.Crossings, p.Along, plan.Rules.Terrain.Roads.RampM)) >= plan.Rules.Terrain.Roads.HalfWidthM,
                    $"{spec}: a {p.Species} on a road at {p.Along:0}");
        }
        // Inside the forts, nothing wild: the sim stands the poles there and nothing else.
        var all = side.Props(0, train.Line.Length).ToList();
        Assert.Contains(all, p => p.Kind != LinesideKind.Pole && LinesideProps.InsideAFort(forts, p.Along, p.Lateral));
        Assert.Equal(WallsOutsideForts(side, all, forts), side.Walls(forts, double.MaxValue).Count());
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
        // A boulder sunk to under a step stands no higher than a crewmate steps up, so it's walked over (PlayerMotor's stepUp);
        // one sunk wholly into a steep slope is under the ground and no wall at all. Every wall stands up from its foot (a
        // box upside down threw in the bodies' contact: D1.2's express night found one).
        foreach (var spec in new[] { "deepTerritory:2", "deadLines:3", "frontier:7", "deepTerritory:4" })
        {
            var (_, t, s, forts) = Night(spec);
            var all = s.Props(0, t.Line.Length, Tuning.Run.Walls.LinesideReachM).ToList();
            var walls = s.Walls(forts, Tuning.Run.Walls.LinesideReachM).ToList();
            Assert.All(walls, w => Assert.True(w.Top > w.Bottom && w.Top > w.Bottom + 3, $"{spec}: a wall from {w.Bottom:0.0} to {w.Top:0.0} at {w.Centre}"));
            Assert.Equal(WallsOutsideForts(s, all, forts), walls.Count);
        }
    }

    [Fact]
    public void AHomesteadStopsACrewmateWalkingAtIt()
    {
        // Note 389: a house by the country road stands on its footprint, the box round its walls and roof, turned to face
        // the road; walked at from the line's side, a crewmate stops at its wall.
        var (_, train, side, forts) = Night("local:1");
        var props = side.Props(1500, train.Line.Length - 1500, Tuning.Run.Walls.LinesideReachM).ToList();
        Double3 From(LinesideProp p, double back)
        {
            var t = train.Line.Sample(p.Along);
            return Foot(train, p) - Double3.Cross(t.Tangent, Double3.Up).Normalized * Math.Sign(p.Lateral) * back;
        }
        var house = props.First(p => p.Species == "saltbox" && Math.Abs(p.Lateral) < 36 && !LinesideProps.InsideAFort(forts, p.Along, p.Lateral)
            && Enumerable.Range(0, 7).All(k => !InAnyWall(train.Walls!, From(p, 8 + k) + Double3.Up, P.Radius + 0.2)));
        var walls = side.WallsOf(house).ToList();
        Assert.Single(walls);
        var foot = Foot(train, house);
        Assert.True(InAnyWall(train.Walls!, foot + Double3.Up, 0), "no wall where the house stands");
        var from = From(house, 12);
        var s = PlayerMotor.SpawnOnGround(from, train.Line, house.Along, P);
        var d = foot - from;
        s.Yaw = Math.Atan2(-d.X, -d.Z);
        double closest = double.MaxValue;
        for (int i = 0; i < 4 * SimConstants.TickRate; i++)
        {
            PlayerMotor.Step(ref s, new PlayerIntent { MoveZ = 1 }, train, P, Tuning.Train, SimConstants.TickSeconds);
            Assert.False(InAnyWall(train.Walls!, s.Position, P.Radius - 0.02), $"inside a wall at {s.Position}, tick {i}");
            closest = Math.Min(closest, double.Hypot(s.Position.X - foot.X, s.Position.Z - foot.Z));
        }
        var w = walls[0];
        Assert.True(closest >= Math.Min(w.HalfLength, w.HalfWidth) + P.Radius - 0.05, $"walked to {closest:0.0} m of a house {w.HalfLength:0.0} by {w.HalfWidth:0.0}");
        Assert.True(closest < double.Hypot(w.HalfLength, w.HalfWidth) + P.Radius + 1, $"never reached the house ({closest:0.0} m)");
    }

    [Fact]
    public void EachPieceStandsOnItsFootprint()
    {
        // Every kind of piece is dealt somewhere across the tiers, and stands as its footprint has it: a house, a car, a shed
        // one box; a church its nave and its tower; a tank round; a headframe on its four legs and its stay, open between; a
        // burying ground and a wharf walked through.
        var seen = new HashSet<string>();
        foreach (var spec in new[] { "local:1", "deadLines:3", "deepTerritory:2" })
        {
            var (route, train, side, _) = Night(spec);
            foreach (var p in side.Props(0, train.Line.Length).Where(p => p.Kind == LinesideKind.Piece))
            {
                seen.Add(p.Species);
                var walls = side.WallsOf(p).ToList();
                int boxes = route.Plan!.Rules.Footprints[p.Species][p.Variant].Length;
                Assert.Equal(boxes, walls.Count);
                switch (p.Species)
                {
                    case "church": Assert.Equal(2, walls.Count); break;
                    case "tank": Assert.Equal(2, walls.Count); break;
                    case "wharf" or "buryingGround": Assert.Empty(walls); break;
                    case "headframe":
                        Assert.Equal(5, walls.Count);
                        // Its foot, under the frame between the legs, is open ground.
                        var foot = Foot(train, p) + Double3.Up;
                        Assert.DoesNotContain(walls, w => Math.Abs(w.ToLocal(foot).X) < w.HalfLength && Math.Abs(w.ToLocal(foot).Z) < w.HalfWidth);
                        break;
                }
                foreach (var w in walls)
                    Assert.True(w.Top > p.Ground - p.Sink && w.Bottom < p.Ground, $"{spec}: a {p.Species}'s wall from {w.Bottom:0.0} to {w.Top:0.0} over ground {p.Ground:0.0}");
            }
        }
        var every = Routes.Generate(Content, "local:1", 6).Plan!.Rules.Footprints.Keys.Where(k => k != "rock");
        Assert.All(every, name => Assert.Contains(name, seen));
    }

    [Fact]
    public void TheWorldStandsTheLinesideAtTheRunsStartAndLeavesTheHandLaidLinesAlone()
    {
        var route = Routes.Generate(Content, "frontier:7", 6);
        var n = new Night(4, speed: 0, route);
        n.World.EnableRun(Tuning.Run, route, 600, authority: true);
        var side = LinesideProps.Of(route, n.Train.Line)!;
        var tree = side.Props(3000, 9000, 40).First(p => p.Kind == LinesideKind.Tree);
        Assert.True(InAnyWall(n.Train.Walls!, Foot(n.Train, tree) + Double3.Up, 0), "the run's walls have no tree");
        // A hand-laid line's lineside is the art's alone.
        var legacy = Route.RouteGenerator.Generate(Tuning.Route, Route.RouteTier.Frontier, 1);
        Assert.Null(LinesideProps.Of(legacy, legacy.Build()));
    }

    static Double3 Foot(RailLine local, BranchTree tree)
    {
        var t = local.Sample(tree.Along);
        return (t.Position + Double3.Cross(t.Tangent, Double3.Up).Normalized * tree.Lateral) with { Y = tree.Ground };
    }

    [Theory]
    [InlineData("frontier:7")]
    [InlineData("deadLines:3")]
    [InlineData("deepTerritory:2")]
    public void TheBranchesPinesAreDealtAsTheMainLinesWoodsAre(string spec)
    {
        // Note 432 (the last of the lineside the art dealt alone, after notes 371 and 389): the alternates' and dead lines'
        // pines are the sim's, dealt by the cell from the night's seed, the same on every machine and asked in any pieces;
        // none in water, on the main line's own land (its woods are its own), or within 9 m of any track.
        var (route, train, side, _) = Night(spec);
        var again = LinesideProps.Of(Routes.Generate(Content, spec, 6), train.Line)!;
        var terrain = ((PlanConditions)train.Line.Conditions!).Terrain;
        int dealt = 0, main = route.Plan!.Alignment.ToList().FindIndex(a => a.Role == EdgeRole.Main);
        foreach (var a in route.Plan.Alignment.Where(a => a.Role is EdgeRole.Alternate or EdgeRole.DeadLine))
        {
            var local = train.Line.Branches[a.Branch].Local;
            var whole = side.BranchTrees(a.Branch, 0, local.Length).ToList();
            Assert.Equal(whole, again.BranchTrees(a.Branch, 0, local.Length));
            // Asked in pieces that cut across its cells.
            var pieces = new List<BranchTree>();
            for (double from = 0; from < local.Length; from += 137)
                pieces.AddRange(side.BranchTrees(a.Branch, from, Math.Min(from + 137, local.Length)));
            Assert.Equal(whole.OrderBy(t => t.Along).ThenBy(t => t.Lateral), pieces.OrderBy(t => t.Along).ThenBy(t => t.Lateral));
            foreach (var tree in whole)
            {
                var foot = Foot(local, tree);
                Assert.Null(terrain.WaterAt(foot.X, foot.Z));
                Assert.DoesNotContain(terrain.Nearby(foot.X, foot.Z, 12), n => Math.Abs(n.Lateral) < 9);
                Assert.DoesNotContain(terrain.Nearby(foot.X, foot.Z, 300), n => n.Edge == main && Math.Abs(n.Lateral) < LinesideProps.MainLandM);
                Assert.InRange(tree.Ground - terrain.Height(foot.X, foot.Z), -0.2, -0.1);
            }
            dealt += whole.Count;
        }
        Assert.True(dealt > 50, $"{spec}: only {dealt} pines beside its branches");
    }

    [Fact]
    public void ACrewmateWalksRoundABranchsPineNotThroughIt()
    {
        var (route, train, side, _) = Night("frontier:7");
        double reach = Tuning.Run.Walls.LinesideReachM;
        // Every pine in reach of its own track stands as a wall (and the world stands them at the run's start).
        var trees = route.Plan!.Alignment.Where(a => a.Role is EdgeRole.Alternate or EdgeRole.DeadLine)
            .SelectMany(a => side.BranchTrees(a.Branch, 0, train.Line.Branches[a.Branch].Local.Length)).ToList();
        var near = trees.Where(t => Math.Abs(t.Lateral) - t.Radius <= reach).ToList();
        Assert.NotEmpty(near);
        Assert.Equal(near.Count, side.BranchWalls(reach).Count());
        var night = new Night(4, speed: 0, route);
        night.World.EnableRun(Tuning.Run, route, 600, authority: true);
        Assert.All(near, t => Assert.True(InAnyWall(night.Train.Walls!, Foot(train.Line.Branches[t.Branch].Local, t) + Double3.Up, 0), $"no wall at the pine {t.Along:0} along branch {t.Branch}"));
        // Walked at from 4 m nearer its track, a crewmate stops at its trunk.
        Double3 From(BranchTree t)
        {
            var local = train.Line.Branches[t.Branch].Local;
            var s = local.Sample(t.Along);
            return Foot(local, t) - Double3.Cross(s.Tangent, Double3.Up).Normalized * Math.Sign(t.Lateral) * 4;
        }
        var tree = near.First(t => t.Radius >= 0.15 && near.All(q => q.Equals(t) || (Foot(train.Line.Branches[q.Branch].Local, q) - Foot(train.Line.Branches[t.Branch].Local, t)).Length > 6)
            && !InAnyWall(train.Walls!, From(t) + Double3.Up, P.Radius + 0.3));
        var trunk = Foot(train.Line.Branches[tree.Branch].Local, tree);
        var from = From(tree);
        var st = PlayerMotor.SpawnOnGround(from, train.Line, 0, P);
        var d = trunk - from;
        st.Yaw = Math.Atan2(-d.X, -d.Z);
        double closest = double.MaxValue;
        for (int i = 0; i < 3 * SimConstants.TickRate; i++)
        {
            PlayerMotor.Step(ref st, new PlayerIntent { MoveZ = 1 }, train, P, Tuning.Train, SimConstants.TickSeconds);
            Assert.False(InAnyWall(train.Walls!, st.Position, P.Radius - 0.02), $"inside a wall at {st.Position}, tick {i}");
            closest = Math.Min(closest, double.Hypot(st.Position.X - trunk.X, st.Position.Z - trunk.Z));
        }
        Assert.True(closest >= tree.Radius + P.Radius - 0.05, $"walked to {closest:0.00} m of a pine {tree.Radius:0.00} m round");
        Assert.True(closest < tree.Radius + P.Radius + 0.5, $"never reached the pine ({closest:0.0} m)");
    }
}
