using Ballast;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Stops;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>Stop layouts (docs/design/level-design.md Parts D and Z): the invariants, the difficulty bands, and the loot.</summary>
public class StopGeneratorTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly StopTuning S = Tuning.Route.Stops!;
    static readonly LootTuning L = DataFile.Load<LootTuning>(Path.Combine(Content, LootTuning.File));
    static readonly StopContext Cx = Tuning.Route.StopContext;

    public static TheoryData<RouteTier, StopKind> TiersAndKinds()
    {
        var d = new TheoryData<RouteTier, StopKind>();
        foreach (var tier in Enum.GetValues<RouteTier>())
            foreach (var kind in Enum.GetValues<StopKind>())
                d.Add(tier, kind);
        return d;
    }

    [Theory]
    [MemberData(nameof(TiersAndKinds))]
    public void EveryStopPassesEveryInvariant(RouteTier tier, StopKind kind)
    {
        // Z.5: whatever else a seed does, the layout holds; P15 keeps it in its tier's band (a few may miss at the edge).
        int missed = 0;
        for (ulong seed = 1; seed <= 30; seed++)
        {
            var stop = StopGenerator.Generate(S, tier, seed, kind, Cx);
            var failed = stop.Checks.Where(c => c.Applies && !c.Pass && c.Name != "Difficulty inside the tier's band").ToList();
            Assert.True(failed.Count == 0, $"{tier} {kind} seed {seed}: {string.Join("; ", failed.Select(c => $"{c.Name}: {c.Detail}"))}");
            if (!stop.InBand)
                missed++;
        }
        Assert.True(missed <= 1, $"{tier} {kind}: {missed} of 30 outside the band");
    }

    [Theory]
    [InlineData(StopKind.Yard)]
    [InlineData(StopKind.YardAndVillage)]
    [InlineData(StopKind.Village)]
    public void DeeperTiersAreHarderToWork(StopKind kind)
    {
        // P15, level-design D.2: early runs easier, later runs harder, stop by stop.
        double Median(RouteTier tier)
        {
            var scores = Enumerable.Range(1, 40).Select(s => StopGenerator.Generate(S, tier, (ulong)s, kind, Cx).Moves.Score).Order().ToList();
            return scores[scores.Count / 2];
        }
        var medians = Enum.GetValues<RouteTier>().Select(Median).ToList();
        for (int i = 1; i < medians.Count; i++)
            Assert.True(medians[i] > medians[i - 1], $"{kind}: {string.Join(" → ", medians)}");
    }

    [Theory]
    [InlineData(RouteTier.Local)]
    [InlineData(RouteTier.Frontier)]
    [InlineData(RouteTier.DeadLines)]
    [InlineData(RouteTier.DeepTerritory)]
    public void BlockedSidingsFollowTheTierAndNeverTheFacilitysOwn(RouteTier tier)
    {
        // Level-design D.2 (note 294): 0, 0–1, 1–2, 1–3 sidings with derelicts on them, never all, never the facility's own.
        var range = S.Tiers[tier].Blocked;
        int seen = 0;
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var stop = StopGenerator.Generate(S, tier, seed, StopKind.Yard, Cx);
            var blocked = stop.Tracks.Where(t => t.Blocked).ToList();
            int open = stop.Tracks.Count(t => !t.Primary && t.Capacity > 0);
            Assert.InRange(blocked.Count, Math.Min(range[0], open), Math.Min(range[1], open));
            Assert.DoesNotContain(blocked, t => t.Primary);
            Assert.All(blocked, t => Assert.InRange(t.Derelicts, 1, Math.Min(S.Derelict.Cars[1], t.Capacity)));
            Assert.Contains(stop.Checks, c => c.Name == "Blocked sidings leave a way in" && c.Pass);
            seen += blocked.Count;
            // A switchyard's sidings have its standing cars instead (note 187).
            Assert.DoesNotContain(StopGenerator.Generate(S, tier, seed, StopKind.Yard, Cx with { Facility = FacilityKind.Switchyard }).Tracks, t => t.Blocked);
        }
        if (range[1] == 0)
            Assert.Equal(0, seen);
        else
            Assert.True(seen > 0, $"{tier}: no blocked siding in 40 yards");
    }

    [Fact]
    public void ClearingABlockedSidingCostsTwoThrowsAReversalAndTheClearance()
    {
        // D.1: "two more [throws] for every blocked siding that has to be cleared", "one [reversal] per clearance", ×5.
        var w = S.Score;
        int checkedStops = 0;
        for (ulong seed = 1; seed <= 60 && checkedStops < 5; seed++)
        {
            var stop = StopGenerator.Generate(S, RouteTier.DeepTerritory, seed, StopKind.Yard, Cx);
            if (stop.Moves.Clearances == 0)
                continue;
            var open = stop with { Tracks = [.. stop.Tracks.Select(t => t with { Derelicts = 0 })] };
            var without = StopGenerator.Measure(S, S.Tiers[RouteTier.DeepTerritory], open);
            int n = stop.Moves.Clearances;
            Assert.Equal(without.Trips, stop.Moves.Trips);
            Assert.Equal(without.Throws + 2 * n, stop.Moves.Throws);
            Assert.Equal(without.Reversals + n, stop.Moves.Reversals);
            Assert.Equal(without.Yard + n * (w.Clearance + 2 * w.Throw + w.Reversal), stop.Moves.Yard, 1);
            checkedStops++;
        }
        Assert.True(checkedStops > 0, "no deep-territory yard cleared a siding");
    }

    [Theory]
    [MemberData(nameof(Tiers))]
    public void ADeadTownHasItsStationAndAGoodsYardOfDerelictStock(RouteTier tier)
    {
        // Linegen plan §11.3 (note 302): "platforms, station building, goods shed, sidings with derelict stock". A halt has none.
        var tt = S.Tiers[tier];
        var dt = S.DeadTown;
        for (ulong seed = 1; seed <= 25; seed++)
        {
            var cx = Cx with { ZoneLength = 700, HaltAt = 350, Side = seed % 2 == 0 ? 1 : -1 };
            var town = StopGenerator.Generate(S, tier, seed, StopKind.Village, cx with { DeadTown = true });
            // Every invariant (the band's spread is EveryStopPassesEveryInvariant's).
            var failed = town.Checks.Where(c => c.Applies && !c.Pass && c.Name != "Difficulty inside the tier's band").ToList();
            Assert.True(failed.Count == 0, $"{tier} {seed}: " + string.Join("; ", failed.Select(c => c.Detail)));
            var halt = town.Halt!.Value;
            // The station behind the platform, on the halt's side, near it along the line.
            var station = Assert.Single(town.Buildings, b => b.Kind == BuildingKind.Station);
            Assert.Equal(Math.Sign(halt.D), Math.Sign(station.D));
            Assert.InRange(Math.Abs(station.D) - station.Width / 2, Math.Abs(halt.D) + 2.2, Math.Abs(halt.D) + 2.2 + dt.Station.Gap + 0.01);
            Assert.InRange(Math.Abs(station.S - halt.S), 0, station.Length / 2 + dt.Station.FromLane[1] + 0.01);
            // The goods siding past the rail buffer, its derelicts on it, the goods shed beyond it; its find out of the buffer (P13).
            var siding = Assert.Single(town.Sidings);
            double d = siding[0].D;
            Assert.InRange(Math.Abs(d), tt.Buffer + dt.Goods.Beyond[0] - 0.01, tt.Buffer + dt.Goods.Beyond[1] + 0.01);
            var stock = town.Buildings.Where(b => b.Kind == BuildingKind.Derelict).ToList();
            Assert.InRange(stock.Count, dt.Goods.Cars[0], dt.Goods.Cars[1]);
            Assert.All(stock, c => Assert.True(c.D == d && c.S - c.Length / 2 >= siding[0].S && c.S + c.Length / 2 <= siding[^1].S));
            var shed = Assert.Single(town.Buildings, b => b.Kind == BuildingKind.GoodsShed);
            Assert.True(Math.Abs(shed.D) > Math.Abs(d) && Math.Sign(shed.D) == Math.Sign(d));
            Assert.All(town.Containers.Where(c => c.Building == town.Buildings.ToList().IndexOf(shed)), c => Assert.True(c.Kind == ContainerKind.Bench && Math.Abs(c.At.D) >= tt.Buffer));
            // All of it stands as walls (StopWalls), and none of it at a halt.
            Assert.All(Enumerable.Range(0, town.Buildings.Count).Where(i => town.Buildings[i].Kind is BuildingKind.Station or BuildingKind.GoodsShed or BuildingKind.Derelict),
                i => Assert.True(StopWalls.Walled(town, i)));
            var plain = StopGenerator.Generate(S, tier, seed, StopKind.Village, cx);
            Assert.DoesNotContain(plain.Buildings, b => b.Kind is BuildingKind.Station or BuildingKind.GoodsShed or BuildingKind.Derelict);
            Assert.Empty(plain.Sidings);
        }
    }

    [Fact]
    public void TheLinesDeadTownsAreStopsWithTheirRailwaySide()
    {
        // frontier:7's Maddox (and every main-line dead town on these nights): a village halt with its station and goods yard.
        int towns = 0;
        foreach (var spec in new[] { "frontier:7", "deepTerritory:2", "deepTerritory:5" })
        {
            var route = LineGen.Routes.Generate(Content, spec, 10);
            foreach (var t in route.Plan!.Landmarks.Where(l => l.Type == "town" && l.Edge == "main"))
            {
                var v = Assert.Single(route.Features, f => f.Kind == FeatureKind.Village && f.Start < t.S1 && t.S0 < f.End);
                Assert.Contains(v.Stop!.Buildings, b => b.Kind == BuildingKind.Station);
                Assert.Single(v.Stop!.Sidings);
                towns++;
            }
            Assert.All(route.Features.Where(f => f.Kind == FeatureKind.Village && !route.Plan.Landmarks.Any(l => l.Type == "town" && l.Edge == "main" && f.Start < l.S1 && l.S0 < f.End)),
                f => Assert.Empty(f.Stop!.Sidings));
        }
        Assert.True(towns > 0, "no dead town on the main line of these nights");
    }

    [Fact]
    public void TheSameSeedIsTheSameStop()
    {
        foreach (var kind in Enum.GetValues<StopKind>())
        {
            var a = StopGenerator.Generate(S, RouteTier.DeadLines, 11, kind, Cx);
            var b = StopGenerator.Generate(S, RouteTier.DeadLines, 11, kind, Cx);
            Assert.Equal(System.Text.Json.JsonSerializer.Serialize(a, DataFile.Options), System.Text.Json.JsonSerializer.Serialize(b, DataFile.Options));
            Assert.NotEqual(System.Text.Json.JsonSerializer.Serialize(a, DataFile.Options),
                System.Text.Json.JsonSerializer.Serialize(StopGenerator.Generate(S, RouteTier.DeadLines, 12, kind, Cx), DataFile.Options));
        }
    }

    [Fact]
    public void TheTuningAgreesWithTheTrainAndTheLoads()
    {
        // stops.json sizes sidings in cars and scores loads: both have to match what the train and the loading really are.
        var t = Tuning.Train.Geometry;
        Assert.Equal(t.CarLength + t.CouplingGap, S.CarPitch, 6);
        Assert.Equal(t.EngineLength, S.EngineLength, 6);
        Assert.Equal(FacilityTests.F.Crane.LoadPerCasting, S.Score.CraneBayLoad, 6);
        Assert.Equal(FacilityTests.F.Crates.Heavy.LoadPerCrate, S.Score.StrongroomLoad, 6);
        var crates = L.Yard.CrateStack.Crates;
        Assert.Equal(FacilityTests.F.Crates.LoadPerCrate * (crates[0] + crates[1]) / 2.0, S.Score.CrateStackLoad, 6);
    }

    [Theory]
    [MemberData(nameof(Tiers))]
    public void EveryFacilityHasAYardAndItsTrackIsTheRoutesTrack(RouteTier tier)
    {
        for (ulong seed = 1; seed <= 8; seed++)
        {
            var route = RouteGenerator.Generate(Tuning.Route, tier, seed);
            var line = route.Build();
            foreach (var f in route.Of(FeatureKind.Facility))
            {
                if (f.Facility == FacilityKind.CoalingTower)
                {
                    Assert.Null(f.Stop);
                    continue;
                }
                var stop = Assert.IsType<StopLayout>(f.Stop);
                Assert.True(stop.HasYard);
                Assert.Equal(stop.YardSide, f.Side);
                // Each track is a spur off the main line at its own switch, laid where the layout says it is.
                foreach (var track in stop.Tracks)
                {
                    var branch = Assert.Single(line.Branches, b => Math.Abs(b.Toe - (f.Start + track.Toe)) < 1e-6 && b.Side == track.Side);
                    Assert.Equal(track.Standing, branch.Definition.Standing);
                    for (double u = 0; u < branch.Local.Length; u += 7)
                    {
                        var world = branch.Local.Sample(u).Position;
                        var planned = Run.Run.StopWorld(line, f, Along(track.Path, u));
                        Assert.True(((world - planned) with { Y = 0 }).Length < 0.3, $"{route.Name} track {track.Index} at {u:0} m: {(world - planned).Length:0.00} m off");
                    }
                }
                // The facility's own track is its first, and the outermost (P6).
                Assert.True(stop.Tracks[0].Primary);
                Assert.Equal(line.Branches.First(b => b.Kind == BranchKind.Spur && f.Contains(b.Toe)).Toe, f.Start + stop.Tracks[0].Toe, 6);
            }
        }
    }

    public static TheoryData<RouteTier> Tiers => new(Enum.GetValues<RouteTier>());

    /// <summary>The point <paramref name="u"/> metres along a polyline.</summary>
    static Pt Along(IReadOnlyList<Pt> path, double u)
    {
        for (int i = 1; i < path.Count; i++)
        {
            double seg = Pt.Distance(path[i - 1], path[i]);
            if (u <= seg)
                return path[i - 1] + (path[i] - path[i - 1]) * (seg > 0 ? u / seg : 0);
            u -= seg;
        }
        return path[^1];
    }

    [Theory]
    [MemberData(nameof(Tiers))]
    public void VillageHaltsSitOnLevelStraightTrackBetweenTheFacilities(RouteTier tier)
    {
        int halts = 0;
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var route = RouteGenerator.Generate(Tuning.Route, tier, seed);
            var line = route.Build();
            var villages = route.Of(FeatureKind.Village).ToList();
            halts += villages.Count;
            Assert.InRange(villages.Count, 0, S.Tiers[tier].Halts[1]);
            foreach (var v in villages)
            {
                var stop = Assert.IsType<StopLayout>(v.Stop);
                Assert.Equal(StopKind.Village, stop.Kind);
                for (double s = v.Start + 1; s < v.End; s += 50)
                    Assert.True(line.Sample(s) is { GradePercent: 0, Curvature: 0 }, $"{route.Name}: halt at {s:0} m isn't level straight");
                Assert.DoesNotContain(route.Features, f => f != v && f.Kind is FeatureKind.Facility or FeatureKind.Tunnel or FeatureKind.Bridge or FeatureKind.Junction
                    && f.Start < v.End && f.End > v.Start);
            }
        }
        Assert.True(halts > 0, $"no {tier} route had a village halt");
    }

    [Fact]
    public void AStopInARouteIsTheStopItsSeedMakes()
    {
        // Z.1: a stop hangs off its own hash (and its facility and the grade out of it), so regenerating it alone gives the
        // layout the whole route did.
        var route = RouteGenerator.Generate(Tuning.Route, RouteTier.Frontier, 5);
        var line = route.Build();
        foreach (var f in route.Features.Where(f => f.Stop is not null))
        {
            var cx = f.Kind == FeatureKind.Facility ? Cx with { Facility = f.Facility, ExitGrade = RouteGenerator.ExitGradeOf(line, f) } : Cx;
            var again = StopGenerator.Generate(S, route.Tier, f.Stop!.Seed, f.Stop.Kind, cx);
            Assert.Equal(System.Text.Json.JsonSerializer.Serialize(f.Stop, DataFile.Options), System.Text.Json.JsonSerializer.Serialize(again, DataFile.Options));
        }
    }

    [Fact]
    public void FindsHoldOnlyWhatTheirKindAllowsAndShareTheBudget()
    {
        // P14: the layout places containers; the economy fills them, within the kind's list and the stop's budget.
        var route = RouteGenerator.Generate(Tuning.Route, RouteTier.DeadLines, 3);
        double perCar = Tuning.Run.Economy.PerCar["deadLines"];
        int stops = 0;
        for (int i = 0; i < route.Features.Count; i++)
        {
            if (route.Features[i].Stop is not { HasVillage: true } stop)
                continue;
            stops++;
            var finds = StopLoot.Village(L, stop, route.Seed, i, perCar);
            Assert.Equal(stop.Containers.Count(c => c.Zone == StopZone.Village), finds.Count);
            foreach (var find in finds)
            {
                var c = stop.Containers[find.Container];
                Assert.Contains(find.Item, L.Of(c.Kind).Items);
                Assert.True(find.Value > 0);
            }
            // Rounded to fives, the whole is the budget's range.
            Assert.InRange(finds.Sum(f => f.Value), perCar * L.VillageBudget[0] - 5 * finds.Count, perCar * L.VillageBudget[1] + 5 * finds.Count);
        }
        Assert.True(stops > 0);
    }

    [Fact]
    public void AFindStowedAboardPaysOnDelivery()
    {
        // P12: stop at a village halt, its finds come out; one carried into a car and left there is scrip in the report.
        var (route, halt) = WithHalt();
        var line = route.Build();
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 3, 0)), line, (halt.Start + halt.End) / 2, Tuning.Boiler);
        var world = new World(train, Tuning.Combat);
        world.EnableBodies();
        world.EnableRun(Tuning.Run, route, 600, authority: true, FacilityTests.F, L);
        Tick(world, 0.5);
        var loot = world.Bodies.All.Where(b => b.Kind == BodyKind.Loot).ToList();
        Assert.Equal(halt.Stop!.Containers.Count(c => c.Zone == StopZone.Village), loot.Count);
        var body = loot[0];
        var find = world.Run!.FindOf(body)!.Value;
        Assert.False(string.IsNullOrEmpty(world.Run.FindName(body)));

        var car = train.Vehicles.First(v => v.Kind == VehicleKind.Cargo);
        var room = train.Frames[car.Id].Shape.Interior!.Value;
        body.Parent = car.Id;
        body.Pbd.Particles[0].Position = new Double3(0, room.Min.Y + 0.3, 0);
        body.Pbd.Particles[0].Previous = body.Pbd.Particles[0].Position;
        Tick(world, L.SettleSeconds + 1);
        Assert.DoesNotContain(body, world.Bodies.All);
        Assert.Equal(find.Value, world.Run.Scavenged, 6);
        Assert.Equal(find, Assert.Single(world.Run.Stowed));
    }

    [Fact]
    public void AYardsCranesStandOverItsCranedFaces()
    {
        // P5, P18: each craned face but the facility's own has a gantry, with a casting for each bay its runway reaches.
        for (ulong seed = 1; seed < 60; seed++)
        {
            var route = RouteGenerator.Generate(Tuning.Route, RouteTier.Frontier, seed);
            var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 3, 0)), route.Build(), 700, Tuning.Boiler);
            var world = new World(train, Tuning.Combat);
            world.EnableRun(Tuning.Run, route, 600, authority: true, FacilityTests.F, L);
            foreach (var site in world.Run!.Sites)
            {
                if (site?.Feature.Stop is not { } stop)
                    continue;
                var faces = stop.Tracks.Where(t => t.Crane is { Bays: > 0 } && !(t.Primary && site.Crane is not null)).ToList();
                Assert.Equal(faces.Count, site.YardCranes.Count);
                Assert.Equal(faces.Sum(t => t.Crane!.Bays), site.YardCranes.Sum(c => c.Castings.Length));
                if (faces.Count > 0)
                    return;
            }
        }
        throw new InvalidOperationException("no frontier yard had a craned face besides its own");
    }

    static (Route.Route Route, RouteFeature Halt) WithHalt()
    {
        for (ulong seed = 1; seed < 100; seed++)
        {
            var route = RouteGenerator.Generate(Tuning.Route, RouteTier.Local, seed);
            if (route.Of(FeatureKind.Village).FirstOrDefault(v => v.Stop!.Containers.Count(c => c.Zone == StopZone.Village) > 1) is { } halt)
                return (route, halt);
        }
        throw new InvalidOperationException("no local route has a village halt with finds");
    }

    static void Tick(World world, double seconds)
    {
        for (int t = 0; t < seconds * SimConstants.TickRate; t++)
        {
            world.Train.Dynamics.Velocity = 0;
            world.BeginTick();
            world.Step(new TrainControls { Reverser = 1, Brake = 1 });
            world.StepBodies([]);
            world.StepRun([]);
        }
    }
}
