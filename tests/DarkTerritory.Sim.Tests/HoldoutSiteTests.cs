using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Stops;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Holdouts and lairs as level content (GDD App. D.4, B.6, B.8; level-design Part H): every site gets its Holdouts, of
/// the type its facility calls for, placed from their own seed; the outside creatures have somewhere to be. The D.4
/// placement rules themselves are checks every stop runs (<see cref="StopGeneratorTests.EveryStopPassesEveryInvariant"/>).
/// </summary>
public class HoldoutSiteTests
{
    static readonly StopTuning S = Tuning.Route.Stops!;
    static readonly StopContext Cx = Tuning.Route.StopContext;

    static IEnumerable<StopLayout> Yards(FacilityKind? kind, RouteTier tier = RouteTier.Frontier) =>
        Enumerable.Range(1, 30).Select(s => StopGenerator.Generate(S, tier, (ulong)s, StopKind.Yard, Cx with { Facility = kind }));

    [Fact]
    public void AMineHeadsHoldoutIsItsLampRoom()
    {
        foreach (var stop in Yards(FacilityKind.MineHead))
            Assert.All(stop.Holdouts, h => Assert.Equal(BuildingKind.LampRoom, stop.Buildings[h.Building].Kind));
    }

    [Fact]
    public void ASwitchyardAlwaysHasASecondHoldout()
    {
        foreach (var stop in Yards(FacilityKind.Switchyard))
        {
            Assert.Equal(2, stop.Holdouts.Count);
            Assert.Single(stop.Holdouts, h => h.Second);
        }
    }

    [Fact]
    public void PrisonCarsAreCommonestWhereTheKindPrefersThem()
    {
        // D.4: "preferred at switchyards, wreck yards and military depots".
        int Cars(FacilityKind kind) => Yards(kind).Sum(s => s.Holdouts.Count(h => h.Kind == HoldoutKind.PrisonCar));
        Assert.True(Cars(FacilityKind.WreckYard) > Cars(FacilityKind.Foundry) * 1.5, $"wreck yard {Cars(FacilityKind.WreckYard)}, foundry {Cars(FacilityKind.Foundry)}");
        // A prison car stands on its own spare siding.
        foreach (var stop in Yards(FacilityKind.WreckYard))
            Assert.All(stop.Holdouts.Where(h => h.Kind == HoldoutKind.PrisonCar), h => Assert.Equal(2, h.Siding.Count));
    }

    [Theory]
    [InlineData(RouteTier.Local)]
    [InlineData(RouteTier.DeepTerritory)]
    public void AVillageHaltHasOneHoldoutByItsPlatformOrInTheVillage(RouteTier tier)
    {
        var sites = new HashSet<HoldoutSite>();
        for (ulong seed = 1; seed <= 30; seed++)
        {
            var stop = StopGenerator.Generate(S, tier, seed, StopKind.Village, Cx);
            var h = Assert.Single(stop.Holdouts);
            sites.Add(h.Site);
            Assert.False(h.Second);
            if (h.Kind == HoldoutKind.Lockup)
                Assert.Equal(HoldoutSite.Halt, h.Site);
        }
        Assert.Equal([HoldoutSite.Halt, HoldoutSite.Village], sites.Order());
    }

    [Fact]
    public void EveryGeneratedNightHasAHoldoutAtEveryStop()
    {
        foreach (var tier in Enum.GetValues<RouteTier>())
        {
            var route = RouteGenerator.Generate(Tuning.Route, tier, 11);
            foreach (var f in route.Features.Where(f => f.Stop is not null))
            {
                Assert.NotEmpty(f.Stop!.Holdouts);
                if (f.Facility == FacilityKind.Switchyard)
                    Assert.Equal(2, f.Stop.Holdouts.Count);
            }
        }
    }

    [Fact]
    public void DeeperTiersBreedMoreRibbits()
    {
        double Warrens(RouteTier tier) => Enumerable.Range(1, 30)
            .Average(s => StopGenerator.Generate(S, tier, (ulong)s, StopKind.YardAndVillage, Cx).Lairs.Count(l => l.Kind == LairKind.Warren));
        Assert.True(Warrens(RouteTier.DeepTerritory) > Warrens(RouteTier.Local) + 1, $"local {Warrens(RouteTier.Local):0.0}, deep {Warrens(RouteTier.DeepTerritory):0.0}");
    }
}
