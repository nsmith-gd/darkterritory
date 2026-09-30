using System.Text.Json;
using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Stops;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Stops on a generated line (PlanStops; linegen plan §11, level-design Part Z): the line generator's facilities get
/// their yards at their own junctions and its settlements their villages, each with its Holdouts, on flattened ground,
/// the same on every machine.
/// </summary>
public class PlanStopTests
{
    static readonly string Content = DataFile.FindContentRoot();

    static readonly string[] Specs = ["local:1", "frontier:7", "deadLines:3", "deepTerritory:2"];
    public static TheoryData<string> Nights => [.. Specs];

    static Route.Route Night(string spec, int cars = 6) => Routes.Generate(Content, spec, cars);

    [Theory]
    [MemberData(nameof(Nights))]
    public void EveryFacilityOnASpurHasItsYardAtItsOwnJunction(string spec)
    {
        var r = Night(spec);
        var plan = r.Plan!;
        foreach (var f in r.Of(FeatureKind.Facility))
        {
            // The coaling tower stands on the main line and a mine head's spur runs to its portal: neither is a yard.
            if (f.Facility is FacilityKind.CoalingTower or FacilityKind.MineHead)
            {
                Assert.Null(f.Stop);
                continue;
            }
            var stop = Assert.IsType<StopLayout>(f.Stop);
            Assert.True(stop.Valid, $"{spec}: {f.Facility} at {f.Start}");
            var primary = stop.Tracks.Single(t => t.Primary);
            var poi = plan.Pois.Single(p => p.Type == f.Facility && Math.Abs(f.Start + primary.Toe - p.S) < 0.01);
            // The plan's spur edge keeps its branch, and that branch is the yard's first track.
            var branch = r.Branches[plan.Edge(poi.SpurEdge!).Branch];
            Assert.Equal(poi.S, branch.Toe, 3);
            Assert.Equal(primary.Segments, branch.Segments);
            // Its other tracks are branches too.
            foreach (var t in stop.Tracks.Where(t => !t.Primary))
                Assert.Contains(r.Branches, b => Math.Abs(b.Toe - (f.Start + t.Toe)) < 0.01 && b.Side == t.Side);
        }
    }

    [Theory]
    [MemberData(nameof(Nights))]
    public void EveryStopLiesOnStraightLevelMainLine(string spec)
    {
        var r = Night(spec);
        var line = r.Build();
        foreach (var f in r.Features.Where(f => f.Stop is not null))
        {
            double y0 = line.Sample(f.Start).Position.Y;
            for (double s = f.Start; s <= f.End; s += 10)
            {
                var t = line.Sample(s);
                Assert.True(Math.Abs(t.Curvature) < 1e-6, $"{spec}: {f.Kind} {f.Start}-{f.End} curves at {s}");
                Assert.True(Math.Abs(t.Position.Y - y0) < 0.05, $"{spec}: {f.Kind} {f.Start}-{f.End} climbs at {s}");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Nights))]
    public void SettlementsGetVillagesWithTheirHaltAtThePlatform(string spec)
    {
        var r = Night(spec);
        var plan = r.Plan!;
        var villages = r.Of(FeatureKind.Village).Where(f => f.Stop is not null).ToList();
        Assert.NotEmpty(villages);
        foreach (var v in villages)
        {
            var town = Assert.Single(plan.Landmarks, l => l.Type is "halt" or "town" && l.Edge == "main" && l.S0 == v.Start);
            Assert.Equal(town.S1, v.End);
            Assert.True(v.Stop!.Valid);
            if (plan.Structures.FirstOrDefault(s => s.Type == StructureType.Platform && s.Edge == "main" && s.S0 < v.End && v.Start < s.S1) is { } platform)
            {
                var halt = Assert.IsType<Pt>(v.Stop.Halt);
                Assert.Equal((platform.S0 + platform.S1) / 2, v.Start + halt.S, 1);
                Assert.Equal(Math.Sign(platform.Side), Math.Sign(halt.D));
            }
        }
    }

    [Fact]
    public void GeneratedNightsHaveHoldouts()
    {
        foreach (var spec in Specs)
            Assert.True(Night(spec).Features.Sum(f => f.Stop?.Holdouts.Count ?? 0) > 0, spec);
    }

    [Theory]
    [MemberData(nameof(Nights))]
    public void EachStopFlattensItsGround(string spec)
    {
        var r = Night(spec);
        var pads = r.Plan!.Pads.Where(p => p.Id.StartsWith(PlanStops.PadPrefix, StringComparison.Ordinal)).ToList();
        Assert.Equal(r.Features.Count(f => f.Stop is not null), pads.Count);
        Assert.Equal(pads.Count, pads.Select(p => p.Id).Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(Nights))]
    public void AJoinerBuildsTheSameStopsFromThePlan(string spec)
    {
        // The plan a joiner is sent already carries the stop pads: building its route again gives them once, and the same
        // stops.
        var host = Night(spec);
        var joiner = Routes.FromPlan(Content, host.Plan!);
        Assert.Equal(Json(host.Plan!.Pads), Json(joiner.Plan!.Pads));
        Assert.Equal(Json(host.Features), Json(joiner.Features));
        Assert.Equal(Json(host.Branches), Json(joiner.Branches));
    }

    [Fact]
    public void StopsOnAGeneratedNightAreTheSameEveryTime()
    {
        var a = Night("deepTerritory:2");
        var b = Night("deepTerritory:2");
        Assert.Equal(Json(a.Features), Json(b.Features));
        Assert.Equal(Json(a.Plan!.Pads), Json(b.Plan!.Pads));
    }

    static string Json<T>(T value) => JsonSerializer.Serialize(value);
}
