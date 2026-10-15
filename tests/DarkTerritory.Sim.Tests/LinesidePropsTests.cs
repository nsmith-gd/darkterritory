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
        var a = Build("frontier:3").Props(0, 30000).ToList();
        var b = Build("frontier:3").Props(0, 30000).ToList();
        Assert.True(a.Count > 1000, $"only {a.Count} things beside frontier:3");
        Assert.Equal(a, b);
        var other = Build("frontier:8").Props(0, 30000).Take(200).ToList();
        Assert.NotEqual(a.Take(200), other);

        // Asking for a stretch, or only what's within reach, deals the same as the whole: the art's cells and the sim's walls
        // agree. Within reach is all that stands there; nothing's dealt that the whole doesn't have.
        static IEnumerable<LinesideProp> Sorted(IEnumerable<LinesideProp> ps) => ps.OrderBy(p => p.Kind).ThenBy(p => p.Along).ThenBy(p => p.Lateral).ThenBy(p => p.Species, StringComparer.Ordinal);
        foreach (var spec in new[] { "frontier:3", "local:1" })
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

    /// <summary>
    /// The Corruption has about three trees in ten (the director, 13 Oct 2026: "Not every tree is corrupted in the world but
    /// of all the trees we should have a corruption rate of about 30%"; biomes.json corruptedTrees, note 696), living and
    /// dead alike, on a night of every tier; each stood as its own trunk (its model's, 0.024 h a metre up).
    /// </summary>
    [Theory]
    [InlineData("local:5")]
    [InlineData("frontier:7")]
    [InlineData("deadLines:3")]
    [InlineData("deepTerritory:2")]
    public void TheCorruptionHasAboutThreeTreesInTen(string spec)
    {
        var route = Routes.Generate(Content, spec, 6);
        var line = route.Build();
        var trees = LinesideProps.Of(route, line)!.Props(0, line.Length).Where(p => p.Kind == LinesideKind.Tree && OfTheWoods(p)).ToList();
        Assert.True(trees.Count > 500, $"{spec}: only {trees.Count} trees");
        double share = trees.Count(t => t.Corrupted) / (double)trees.Count;
        Assert.InRange(share, 0.26, 0.34);
        Assert.Contains(trees, t => t.Corrupted && !t.Dead);
        Assert.All(trees.Where(t => t.Corrupted), t => Assert.Equal(Math.Clamp(t.Height * 0.024, 0.1, 0.55), t.Radius, 9));
        // The old field's spruce and an orchard's apples are a homestead's, not the woods': none corrupted.
        Assert.DoesNotContain(LinesideProps.Of(route, line)!.Props(0, line.Length), p => p.Species is "fieldSpruce" or "apple" && p.Corrupted);
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
        var (_, train, side, _) = Night("deepTerritory:16");
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

    static bool Inside(Wall w, Double3 p, double pad)
    {
        var l = w.ToLocal(p);
        return p.Y < w.Top && p.Y > w.Bottom && Math.Abs(l.X) < w.HalfLength + pad && Math.Abs(l.Z) < w.HalfWidth + pad;
    }

    [Fact]
    public void AWharfIsWalkedOutOnUpItsSteps()
    {
        // Note 817 (note 797's "not yet"): a wharf stood nothing, walked through on the bed under the water. Walked at from the
        // shore along it, a crewmate goes up its steps and out along its deck, over the water, at the deck's height.
        var (_, train, side, _) = Night("local:1");
        var wharf = side.Props(0, train.Line.Length).First(p => p.Species == "wharf");
        var t = train.Line.Sample(wharf.Along);
        var ahead = new Double3(t.Tangent.X, 0, t.Tangent.Z).Normalized;
        var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
        var foot = (t.Position + right * wharf.Lateral) with { Y = 0 };
        double c = DMath.Cos(wharf.Yaw), sn = DMath.Sin(wharf.Yaw);
        // Its own frame (NovaKit.Wharf, as LinesideProps.FootprintWalls turns it): x across, −z out over the water.
        Double3 At(double x, double z) => foot + right * (x * c + z * sn) + ahead * (x * sn - z * c);
        // Up the side clear of the traps stacked on the deck.
        var from = At(1.2, 4);
        var s = PlayerMotor.SpawnOnGround(from, train.Line, wharf.Along, P);
        var d = At(1.2, -12) - from;
        s.Yaw = Math.Atan2(-d.X, -d.Z);
        double deck = wharf.Ground - wharf.Sink + 1.6, was = s.Position.Y;
        for (int i = 0; i < 6 * SimConstants.TickRate; i++)
        {
            PlayerMotor.Step(ref s, new PlayerIntent { MoveZ = 1 }, train, P, Tuning.Train, SimConstants.TickSeconds);
            // In a wall over a step: one under it (the next step) is stepped up onto when the feet come over it.
            Assert.False(train.Walls!.Near(s.Position).Any(w => w.Top - s.Position.Y > P.StepUp && Inside(w, s.Position, P.Radius - 0.02)),
                $"inside a wall at {s.Position}, tick {i}");
            Assert.True(s.Position.Y > was - 0.02 || !s.Grounded, $"stepped down at tick {i}: {was:0.00} to {s.Position.Y:0.00}");
            was = s.Position.Y;
        }
        double outAlong = -Double3.Dot(s.Position - foot, right * sn - ahead * c);
        Assert.True(outAlong > 6, $"only {outAlong:0.0} m out along the wharf (at {s.Position})");
        Assert.Equal(deck, s.Position.Y, 6);
        var water = ((PlanConditions)train.Line.Conditions!).Terrain.WaterAt(s.Position.X, s.Position.Z);
        Assert.True(water is { } w && w < deck, $"not over the water: {water}");
        Assert.True(train.Line.Conditions.Ground(s.Position) < deck - 1, "the deck isn't over the bed");
    }

    [Fact]
    public void TheWoodsKeepOffTheBuildings()
    {
        // Note 800 (note 797's "not yet"): the woods were dealt without asking where the lineside's buildings stand, and a
        // spruce grew up through a barn or a burying ground's stones. No trunk, nor a boulder's lump, is inside a building's
        // wall; a tree between its walls (in a burying ground's rows) stands.
        foreach (var spec in new[] { "local:4", "deadLines:49", "deepTerritory:2", "local:1" })
        {
            var (_, train, side, _) = Night(spec);
            var all = side.Props(0, train.Line.Length).ToList();
            var buildings = all.Where(p => p.Kind == LinesideKind.Piece).Select(p => (Piece: p, Walls: side.WallsOf(p).ToList())).Where(b => b.Walls.Count > 0).ToList();
            Assert.Contains(buildings, b => b.Piece.Species is "saltbox" or "barn" or "tank" or "buryingGround");
            foreach (var p in all.Where(p => p.Kind is LinesideKind.Tree or LinesideKind.Rock))
            {
                double reach = p.Kind == LinesideKind.Tree ? p.Radius : side.Extent(p);
                var at = Foot(train, p);
                foreach (var (b, walls) in buildings.Where(b => Math.Abs(b.Piece.Along - p.Along) < 60))
                    foreach (var w in walls)
                    {
                        var local = w.ToLocal(at);
                        Assert.False(Math.Abs(local.X) < w.HalfLength + reach && Math.Abs(local.Z) < w.HalfWidth + reach,
                            $"{spec}: a {p.Species} at {p.Along:0.0} m, {p.Lateral:0.0} out, stands in a {b.Species} at {b.Along:0.0}, {b.Lateral:0.0}");
                    }
            }
        }
    }

    [Fact]
    public void TheBuildingsKeepOffEachOther()
    {
        // Note 808 (note 800's "not yet"): the road's, the shore's and the biomes' buildings were dealt without asking where
        // each other stood, and a church stood half inside a house. No building's wall is inside another's (a stone wall's
        // lengths run on into each other as they're laid; a leaving's pieces stand together).
        foreach (var spec in new[] { "deadLines:3", "deadLines:49", "deepTerritory:2", "deadLines:2" })
        {
            var (_, train, side, _) = Night(spec);
            var buildings = side.Props(0, train.Line.Length).Where(p => p.Kind == LinesideKind.Piece && !Leavings.PieceNames.Contains(p.Species))
                .Select(p => (Piece: p, Walls: side.WallsOf(p).ToList())).Where(b => b.Walls.Count > 0).ToList();
            Assert.Contains(buildings, b => b.Piece.Species is "church" or "saltbox");
            for (int i = 0; i < buildings.Count; i++)
                for (int j = i + 1; j < buildings.Count; j++)
                {
                    var (a, b) = (buildings[i], buildings[j]);
                    if (Math.Abs(a.Piece.Along - b.Piece.Along) > 80 || (a.Piece.Species == "stoneWall" && b.Piece.Species == "stoneWall"))
                        continue;
                    Assert.False(a.Walls.Any(w => b.Walls.Any(v => Inside(w, v))),
                        $"{spec}: a {a.Piece.Species} at {a.Piece.Along:0.0}, {a.Piece.Lateral:0.0} and a {b.Piece.Species} at {b.Piece.Along:0.0}, {b.Piece.Lateral:0.0} stand in each other");
                }
        }
    }

    /// <summary>Whether two walls' boxes overlap on the ground (separating axes: each one's along and across).</summary>
    static bool Inside(Wall a, Wall b)
    {
        double dx = b.Centre.X - a.Centre.X, dz = b.Centre.Z - a.Centre.Z;
        foreach (var (nx, nz) in new[] { (a.Axis.X, a.Axis.Z), (-a.Axis.Z, a.Axis.X), (b.Axis.X, b.Axis.Z), (-b.Axis.Z, b.Axis.X) })
        {
            double ra = a.HalfLength * Math.Abs(a.Axis.X * nx + a.Axis.Z * nz) + a.HalfWidth * Math.Abs(-a.Axis.Z * nx + a.Axis.X * nz);
            double rb = b.HalfLength * Math.Abs(b.Axis.X * nx + b.Axis.Z * nz) + b.HalfWidth * Math.Abs(-b.Axis.Z * nx + b.Axis.X * nz);
            if (Math.Abs(dx * nx + dz * nz) >= ra + rb)
                return false;
        }
        return true;
    }

    [Fact]
    public void AHomesteadStopsACrewmateWalkingAtIt()
    {
        // Note 389: a house by the country road stands on its footprint, the box round its walls and roof, turned to face
        // the road; walked at from the line's side, a crewmate stops at its wall.
        var (_, train, side, forts) = Night("deadLines:3");
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
        // burying ground its stones and its fence's runs, open between its rows (note 797); a wharf its deck, the traps on it and
        // its steps up from the shore, each a step's rise over the last (note 817).
        var seen = new HashSet<string>();
        foreach (var spec in new[] { "local:4", "deadLines:49", "deepTerritory:2", "local:1", "deadLines:3" })
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
                    case "wharf":
                        Assert.Equal(6, walls.Count);
                        Assert.Equal(p.Ground - p.Sink + 1.6, walls.Max(w => w.HalfLength * w.HalfWidth > 15 ? w.Top : double.MinValue), 3);
                        var tops = walls.Where(w => w.Top < p.Ground - p.Sink + 1.6).Select(w => w.Top).Order().Append(p.Ground - p.Sink + 1.6).ToList();
                        Assert.All(tops.Zip(tops.Skip(1)), t => Assert.InRange(t.Second - t.First, 0.01, P.StepUp));
                        break;
                    case "headframe":
                        Assert.Equal(5, walls.Count);
                        // Its foot, under the frame between the legs, is open ground.
                        var foot = Foot(train, p) + Double3.Up;
                        Assert.DoesNotContain(walls, w => Math.Abs(w.ToLocal(foot).X) < w.HalfLength && Math.Abs(w.ToLocal(foot).Z) < w.HalfWidth);
                        break;
                    case "buryingGround":
                        // A wall to every stone and every run of the fence; its middle, between two rows of stones, is open.
                        Assert.InRange(walls.Count, 20, 45);
                        var middle = Foot(train, p) + Double3.Up;
                        Assert.DoesNotContain(walls, w => Math.Abs(w.ToLocal(middle).X) < w.HalfLength && Math.Abs(w.ToLocal(middle).Z) < w.HalfWidth);
                        break;
                }
                foreach (var w in walls)
                    Assert.True(w.Top > p.Ground - p.Sink && w.Bottom < p.Ground, $"{spec}: a {p.Species}'s wall from {w.Bottom:0.0} to {w.Top:0.0} over ground {p.Ground:0.0}");
            }
        }
        // The facilities' footprints are the stops' (StopWalls.AddFacilities, note 758), not the lineside's.
        var every = Routes.Generate(Content, "local:1", 6).Plan!.Rules.Footprints.Keys.Where(k => k != "rock" && !k.StartsWith("facility", StringComparison.Ordinal));
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
    [InlineData("deadLines:6")]
    [InlineData("deepTerritory:3")]
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
