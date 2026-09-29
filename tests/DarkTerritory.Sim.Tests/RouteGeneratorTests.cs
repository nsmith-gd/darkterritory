using System.Text.Json;
using Ballast;
using DarkTerritory.Sim.Route;

namespace DarkTerritory.Sim.Tests;

public class RouteGeneratorTests
{
    static readonly RouteTuning R = Tuning.Route;

    public static TheoryData<RouteTier> Tiers => new(Enum.GetValues<RouteTier>());

    static IEnumerable<Route.Route> Sweep(RouteTier tier, int seeds = 60) =>
        Enumerable.Range(1, seeds).Select(s => RouteGenerator.Generate(R, tier, (ulong)s));

    [Fact]
    public void SameSeedSameRouteDifferentSeedDifferentRoute()
    {
        string A(ulong seed) => JsonSerializer.Serialize(RouteGenerator.Generate(R, RouteTier.Frontier, seed), DataFile.Options);
        Assert.Equal(A(42), A(42));
        Assert.NotEqual(A(42), A(43));
    }

    [Fact]
    public void Pcg32IsFixedByDefinition()
    {
        // Reference values pinned so a refactor can't silently change every generated route.
        var rng = new Pcg32(42, 54);
        Assert.Equal([0xa15c02b7u, 0x7b47f409u, 0xba1d3330u], new[] { rng.NextUInt(), rng.NextUInt(), rng.NextUInt() });
    }

    [Theory]
    [MemberData(nameof(Tiers))]
    public void RoutesRespectTheirTier(RouteTier tier)
    {
        var tt = R.Tiers[tier];
        foreach (var r in Sweep(tier))
        {
            Assert.InRange(r.Length, tt.LengthKm[0] * 1000 - 1, tt.LengthKm[1] * 1000 + 1);
            Assert.All(r.Line.Segments, s => Assert.True(Math.Abs(s.GradePercent) <= tt.MaxGrade + 1e-9, $"{r.Name}: grade {s.GradePercent}"));
            Assert.All(r.Line.Segments.Where(s => s.Radius != 0), s => Assert.True(Math.Abs(s.Radius) >= tt.MinRadius, $"{r.Name}: radius {s.Radius}"));
            int pois = r.Of(FeatureKind.Facility).Count();
            Assert.InRange(pois, 1, tt.Pois[1]);
            Assert.Equal(r.Length / R.DawnAverageSpeed * (1 + R.DawnSlack), r.DawnSeconds, 0);
        }
    }

    [Theory]
    [MemberData(nameof(Tiers))]
    public void EndsAndFacilitiesAreLevelStraightTrack(RouteTier tier)
    {
        foreach (var r in Sweep(tier))
        {
            var line = r.Build();
            void Level(double s) => Assert.True(line.Sample(s) is { GradePercent: 0, Curvature: 0 }, $"{r.Name} at {s:0} m");
            for (double s = 0; s < R.YardLength; s += 50)
                Level(s);
            for (double s = r.Length - R.TerminusApproach + 1; s < r.Length - 1; s += 50)
                Level(s);
            foreach (var f in r.Of(FeatureKind.Facility))
                for (double s = f.Start + 1; s < f.End; s += 50)
                    Level(s);
        }
    }

    [Theory]
    [MemberData(nameof(Tiers))]
    public void HazardsRespectThePacingRules(RouteTier tier)
    {
        foreach (var r in Sweep(tier))
        {
            // App. B.2: never within the first 2 km. App. B.1: nothing in the final approach.
            Assert.All(r.Of(FeatureKind.Sleepers), f => Assert.True(f.Start >= R.NoSleepersFirst, $"{r.Name}: sleepers at {f.Start}"));
            Assert.All(r.Of(FeatureKind.Sleepers).Concat(r.Of(FeatureKind.Grease)),
                f => Assert.True(f.End <= r.Length - R.NoSpawnFinalApproach, $"{r.Name}: {f.Kind} at {f.Start}"));
        }
    }

    [Theory]
    [MemberData(nameof(Tiers))]
    public void FacilitiesAreSpacedAndFeaturesDoNotOverlap(RouteTier tier)
    {
        foreach (var r in Sweep(tier))
        {
            var pois = r.Of(FeatureKind.Facility).Select(f => (f.Start + f.End) / 2).OrderBy(x => x).ToList();
            for (int i = 1; i < pois.Count; i++)
                Assert.True(pois[i] - pois[i - 1] >= R.PoiMinSpacing - 1, $"{r.Name}: facilities {pois[i] - pois[i - 1]:0} m apart");
            var spans = r.Features.Where(f => f.Kind is FeatureKind.Tunnel or FeatureKind.Bridge or FeatureKind.Facility or FeatureKind.Junction).OrderBy(f => f.Start).ToList();
            for (int i = 1; i < spans.Count; i++)
                Assert.True(spans[i].Start >= spans[i - 1].End, $"{r.Name}: {spans[i - 1].Kind} overlaps {spans[i].Kind}");
        }
    }

    [Fact]
    public void DeeperTiersAreHarder()
    {
        // App. B.9 tier progression: more hazards, weaker bridges, more climbing the farther out you go.
        var tiers = Enum.GetValues<RouteTier>().Select(t => Sweep(t, 100).ToList()).ToList();
        double Mean(int tier, Func<Route.Route, double> f) => tiers[tier].Average(f);
        for (int i = 1; i < tiers.Count; i++)
        {
            Assert.True(Mean(i, r => r.Of(FeatureKind.Sleepers).Count()) > Mean(i - 1, r => r.Of(FeatureKind.Sleepers).Count()));
            Assert.True(Mean(i, r => r.Length) > Mean(i - 1, r => r.Length));
            Assert.True(Mean(i, r => r.Line.Segments.Where(s => s.GradePercent > 0).Sum(s => s.Length * s.GradePercent)) >
                        Mean(i - 1, r => r.Line.Segments.Where(s => s.GradePercent > 0).Sum(s => s.Length * s.GradePercent)));
        }
    }

    [Fact]
    public void LongNightsGetACoalingStop()
    {
        foreach (var r in Sweep(RouteTier.DeepTerritory, 30))
            Assert.Contains(r.Of(FeatureKind.Facility), f => f.Facility == FacilityKind.CoalingTower);
    }

    [Fact]
    public void RoutesRoundTripThroughJson()
    {
        var r = RouteGenerator.Generate(R, RouteTier.DeadLines, 9);
        var json = JsonSerializer.Serialize(r, DataFile.Options);
        var back = JsonSerializer.Deserialize<Route.Route>(json, DataFile.Options)!;
        Assert.Equal(json, JsonSerializer.Serialize(back, DataFile.Options));
    }
}
