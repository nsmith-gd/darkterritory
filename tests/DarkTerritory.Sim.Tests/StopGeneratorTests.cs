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
        // Z.1: a stop hangs off its own hash (and its facility), so regenerating it alone gives the layout the whole route did.
        var route = RouteGenerator.Generate(Tuning.Route, RouteTier.Frontier, 5);
        foreach (var f in route.Features.Where(f => f.Stop is not null))
        {
            var again = StopGenerator.Generate(S, route.Tier, f.Stop!.Seed, f.Stop.Kind, Cx with { Facility = f.Kind == FeatureKind.Facility ? f.Facility : null });
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
