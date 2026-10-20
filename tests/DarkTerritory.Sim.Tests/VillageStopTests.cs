using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Stops;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// More abandoned villages in the same night (ARCHITECTURE §8 note 868; the director, 19 Oct 2026: "I'd love more [abandoned
/// villages] but we can't have a longer night, fewer hazards, or fewer stops"). The 7 km night's free stretches are short,
/// and its every-so-often dead settlement fell in one with room one night in twelve. With setpieces.json deadSettlement
/// perNight (2: the director, 19 Oct, "Yes, turn the abandoned villages on"), a filled stretch's straight recovery long enough
/// becomes one, its hazards untouched, and each is a village stop.
/// </summary>
public class VillageStopTests
{
    static readonly string Content = DataFile.FindContentRoot();

    static LineGenContent PerNight(int n)
    {
        var c = LineGenContent.Cached(Content);
        var pieces = c.Config.SetPieces.Pieces.Select(p => p.Id != "deadSettlement" ? p
            : p with { Params = new Dictionary<string, double[]>(p.Params) { ["perNight"] = [n] } });
        return c with { Config = c.Config with { SetPieces = c.Config.SetPieces with { Pieces = [.. pieces] } } };
    }

    [Fact]
    public void WithTwoANightMostNightsHaveAnAbandonedVillageAndEveryHazardTheyHad()
    {
        Assert.Equal(2, LineGenContent.Cached(Content).Config.SetPieces.Pieces.Single(p => p.Id == "deadSettlement").Param("perNight"));
        var (off, on) = (PerNight(0), PerNight(2));
        int nights = 0, withVillage = 0, villages = 0;
        foreach (var tier in new[] { "local", "frontier", "deadLines" })
            for (int seed = 1; seed <= 4; seed++)
            {
                var run = RunParameters.Parse($"{tier}:{seed}", 6);
                var (before, after) = (LineGenerator.Generate(off, run), LineGenerator.Generate(on, run));
                nights++;
                // The same pieces but the settlements, and the stops where they were.
                static IEnumerable<string> Hazards(LinePlan p) => p.Pieces.Where(x => x.Type is not ("deadSettlement" or "recovery")).Select(x => x.Type).Order();
                Assert.Equal(Hazards(before), Hazards(after));
                Assert.Equal(before.Pois.Select(x => x.S), after.Pois.Select(x => x.S));
                var route = after.ToRoute(on.Route);
                int here = route.Features.Count(f => f is { Kind: FeatureKind.Village, Stop.Kind: StopKind.Village });
                // Every one laid on the main line has its village (SettlementStopTests).
                Assert.Equal(after.Landmarks.Count(l => l.Type is "halt" or "town" && l.Edge == "main"), here);
                villages += here;
                if (here > 0)
                    withVillage++;
            }
        // Measured: 7 of 12 nights, 11 village stops (perNight 0: 1 night, 1 stop).
        Assert.True(withVillage >= 7, $"{withVillage} of {nights} nights with a village");
        Assert.True(villages >= 10, $"{villages} village stops");
    }
}
