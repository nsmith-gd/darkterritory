using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// GDD App. D.4 as the line generator lays it: every facility pad, halt and dead town has its Holdouts, where D.4 says
/// they go, the same for the same seed; and the validator refuses one that isn't.
/// </summary>
[Collection(nameof(LineGenTests))]
public class HoldoutSiteTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly HoldoutTuning T = Tuning.Holdouts;
    static readonly FacilityTuning Facilities = DataFile.Load<FacilityTuning>(Path.Combine(Content, FacilityTuning.File));

    static (LinePlan Plan, RailLine Line, TerrainField Terrain) Night(string spec, int cars)
    {
        var route = Routes.Generate(Content, spec, cars);
        var line = route.Build();
        return (route.Plan!, line, ((PlanConditions)line.Conditions!).Terrain);
    }

    static List<string> Check(LinePlan plan, RailLine line, TerrainField terrain) => HoldoutSites.Check(plan, line, terrain, T, Facilities, Tuning.Train);

    [Theory]
    [InlineData("local:1", 10)]
    [InlineData("frontier:7", 6)]
    [InlineData("deadLines:3", 10)]
    [InlineData("deepTerritory:4", 10)]
    [InlineData("frontier:6", 20)]
    public void EverySiteHasItsHoldoutsWhereD4SaysTheyGo(string spec, int cars)
    {
        var (plan, line, terrain) = Night(spec, cars);
        Assert.Empty(Check(plan, line, terrain));
        Assert.Contains(plan.Validation.Checks, c => c.Name == "holdouts" && c.Pass);
        // One at every facility (two on a big pad or a switchyard), one at every halt and dead town.
        foreach (var poi in plan.Pois)
        {
            int want = poi.Pad.RadiusM >= T.Placement.SecondPadScaleM || poi.Type == FacilityKind.Switchyard ? 2 : 1;
            Assert.Equal(want, plan.Holdouts.Count(h => h.Site == poi.Id));
            Assert.All(plan.Holdouts.Where(h => h.Site == poi.Id), h => Assert.Equal(HoldoutSiteKind.Facility, h.SiteKind));
        }
        var stations = plan.Landmarks.Where(l => l.Type is "halt" or "town").ToList();
        Assert.Equal(plan.Pois.Sum(p => plan.Holdouts.Count(h => h.Site == p.Id)) + stations.Count, plan.Holdouts.Count);
        foreach (var h in plan.Holdouts)
        {
            // The lamp's up on the Holdout, and the door's on its side.
            Assert.InRange(h.Lamp[1] - h.Y, T.Placement.LampHeightM[0] - 1e-6, T.Placement.LampHeightM[1] + 1e-6);
            Assert.InRange(HoldoutSites.Horizontal(new Double3(h.Door[0], 0, h.Door[2]), new Double3(h.X, 0, h.Z)), h.Size[0] - 0.02, h.Size[0] + 0.02);
            Assert.True(h.Zone.S1 > h.Zone.S0);
        }
    }

    [Fact]
    public void TheSameSeedGivesTheSameHoldouts()
    {
        var p = RunParameters.Parse("frontier:9", 8);
        var a = LineGenerator.Attempt(LineGenContent.Load(Content), p, 0).Plan!;
        var b = LineGenerator.Attempt(LineGenContent.Load(Content), p, 0).Plan!;
        Assert.NotEmpty(a.Holdouts);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(a.Holdouts, DataFile.Options), System.Text.Json.JsonSerializer.Serialize(b.Holdouts, DataFile.Options));
        // Each site's sub-seed is the one it was made from.
        foreach (var h in a.Holdouts.Where(h => h.SiteKind == HoldoutSiteKind.Facility))
            Assert.StartsWith("0x", h.SubSeed);
        var c = LineGenerator.Attempt(LineGenContent.Load(Content), RunParameters.Parse("frontier:10", 8), 0).Plan!;
        Assert.NotEqual(a.Holdouts.Select(h => (h.X, h.Z)), c.Holdouts.Select(h => (h.X, h.Z)));
    }

    [Fact]
    public void TypesFollowTheSitesRules()
    {
        foreach (var (spec, cars) in new[] { ("local:1", 10), ("frontier:7", 6), ("deadLines:3", 10), ("deepTerritory:4", 10), ("frontier:6", 20) })
        {
            var plan = Night(spec, cars).Plan;
            foreach (var h in plan.Holdouts)
            {
                switch (h.SiteKind)
                {
                    case HoldoutSiteKind.Halt:
                        Assert.Equal(HoldoutType.HaltLockup, h.Type);
                        break;
                    case HoldoutSiteKind.DeadTown:
                        Assert.Equal(HoldoutType.BarricadedShelter, h.Type);
                        break;
                    default:
                        var kind = plan.Pois.First(p => p.Id == h.Site).Type;
                        if (kind == FacilityKind.MineHead)
                            Assert.Equal(HoldoutType.BarricadedShelter, h.Type);
                        if (h.Type == HoldoutType.PrisonCar)
                            Assert.Contains(HoldoutSites.Key(kind), T.Placement.SpareSidingAt);
                        Assert.NotEqual(HoldoutType.HaltLockup, h.Type);
                        break;
                }
                Assert.Equal(T.Placement.Size[h.Type], h.Size);
            }
        }
    }

    /// <summary>The generator's reckoning of the loading modules against where the run actually lays them out (Run.EnableSites).</summary>
    [Theory]
    [InlineData("frontier:7", 6)]
    [InlineData("deadLines:3", 10)]
    public void TheWalkToAFacilitysHoldoutKeepsClearOfItsLoadingModulesAsTheRunLaysThemOut(string spec, int cars)
    {
        var route = Routes.Generate(Content, spec, cars);
        var line = route.Build();
        var run = new Run.Run(Tuning.Run, route);
        run.EnableSites(Facilities, line);
        int checkedSites = 0;
        foreach (var site in run.Sites.OfType<Site>())
        {
            var poi = route.Plan!.Pois[site.Index];
            var points = site.CrateStack.Concat(site.HeavyStack).Concat(site.Handles).ToList();
            if (site.Has(ModuleKind.Winch))
                points.AddRange([site.Capstan, site.SledFrom, site.SledTo]);
            if (site.Crane is { } crane)
                points.AddRange(crane.Castings.Select(c => c.At).Append(crane.Controls));
            foreach (var h in route.Plan.Holdouts.Where(h => h.Site == poi.Id))
                foreach (var m in points)
                    Assert.True(HoldoutSites.SegmentDistance(new Double3(h.From[0], h.From[1], h.From[2]), new Double3(h.Door[0], h.Door[1], h.Door[2]), m)
                        >= T.Placement.RouteClearanceM - 0.5, $"{h.Id} at {poi.Name}");
            checkedSites++;
        }
        Assert.True(checkedSites > 0);
    }

    [Fact]
    public void TheValidatorRejectsBadPlacements()
    {
        var (plan, line, terrain) = Night("frontier:7", 6);
        Assert.Empty(Check(plan, line, terrain));
        var h = plan.Holdouts.First(x => x.SiteKind == HoldoutSiteKind.Facility);
        LinePlan With(PlanHoldout changed) => plan with { Holdouts = [.. plan.Holdouts.Select(x => x.Id == changed.Id ? changed : x)] };
        List<string> Faults(PlanHoldout changed) => Check(With(changed), line, terrain);

        // Too far from the consist.
        var far = h with { X = h.X + (h.X - h.From[0]) * 4, Z = h.Z + (h.Z - h.From[2]) * 4 };
        Assert.Contains(Faults(far), f => f.Contains("from the consist"));
        // On the track.
        var track = line.Sample(plan.Pois.First(p => p.Id == h.Site).S).Position;
        Assert.Contains(Faults(h with { X = track.X, Z = track.Z }), f => f.Contains("on the track") || f.Contains("from the consist"));
        // A lamp down in the ground can't be seen from the board.
        Assert.Contains(Faults(h with { Lamp = [h.Lamp[0], h.Y - 20, h.Lamp[2]] }), f => f.Contains("can't be seen"));
        // Seen from somewhere that isn't the 1 km board.
        Assert.Contains(Faults(h with { Board = [h.Board[0] + 300, h.Board[1], h.Board[2]] }), f => f.Contains("1 km board"));
        // A walk that goes through the loading modules.
        var poi = plan.Pois.First(p => p.Id == h.Site);
        var module = HoldoutSites.ModulePoints(poi, line, Facilities, plan).First();
        Assert.Contains(Faults(h with { From = [module.X, module.Y, module.Z] }), f => f.Contains("loading module") || f.Contains("reachable"));
        // A type the site can't have.
        Assert.Contains(Faults(h with { Type = HoldoutType.HaltLockup }), f => f.Contains("HaltLockup"));
        // A site missing one.
        Assert.Contains(Check(plan with { Holdouts = [.. plan.Holdouts.Where(x => x.Id != h.Id)] }, line, terrain), f => f.Contains("wants"));
        // A halt's too far from the line.
        if (plan.Holdouts.FirstOrDefault(x => x.SiteKind == HoldoutSiteKind.Halt) is { } halt)
        {
            var at = new Double3(halt.X, 0, halt.Z);
            var from = new Double3(halt.From[0], 0, halt.From[2]);
            var out_ = at + (at - from).Normalized * 60;
            Assert.Contains(Faults(halt with { X = out_.X, Z = out_.Z }), f => f.Contains("from the line"));
        }
        // A prison car where there's no spare siding.
        if (plan.Holdouts.FirstOrDefault(x => x.SiteKind == HoldoutSiteKind.Facility
            && !T.Placement.SpareSidingAt.Contains(HoldoutSites.Key(plan.Pois.First(p => p.Id == x.Site).Type))) is { } plain)
            Assert.Contains(Faults(plain with { Type = HoldoutType.PrisonCar, Size = T.Placement.Size[HoldoutType.PrisonCar] }), f => f.Contains("spare siding"));
    }
}
