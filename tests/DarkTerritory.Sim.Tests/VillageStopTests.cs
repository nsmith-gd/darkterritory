using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Stops;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// More abandoned villages in the same night (ARCHITECTURE §8 note 868; the director, 19 Oct 2026: "I'd love more [abandoned
/// villages] but we can't have a longer night, fewer hazards, or fewer stops"). The 7 km night's free stretches are short,
/// and its every-so-often dead settlement fell in one with room one night in twelve; now a filled stretch's straight recovery
/// long enough becomes one (setpieces.json deadSettlement perNight, leadM; its hazards untouched), and each is a village stop.
/// </summary>
public class VillageStopTests
{
    static readonly string Content = DataFile.FindContentRoot();

    [Fact]
    public void MostNightsHaveAnAbandonedVillageOnTheMainLineWithTheirThreeStops()
    {
        int nights = 0, withVillage = 0, villages = 0;
        foreach (var tier in new[] { "local", "frontier", "deadLines" })
            for (int seed = 1; seed <= 4; seed++)
            {
                var route = Routes.Generate(Content, $"{tier}:{seed}", 6);
                nights++;
                // The stops are as many as ever: three facilities a night (a coaling tower or mine head has no yard).
                Assert.Equal(3, route.Features.Count(f => f.Kind == FeatureKind.Facility));
                int here = route.Features.Count(f => f is { Kind: FeatureKind.Village, Stop.Kind: StopKind.Village });
                villages += here;
                if (here > 0)
                    withVillage++;
            }
        // Measured: 8 of 12 nights, 12 village stops (one night in twelve before, one stop).
        Assert.True(withVillage >= 7, $"{withVillage} of {nights} nights with a village");
        Assert.True(villages >= 10, $"{villages} village stops");
    }
}
