using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Route;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// T114 playtest ("a stop with nothing there"): a halt or dead town on the main line has its platform and boards, so it has
/// its village too (loot, a Holdout), or is inside a yard's. One in ten went without: the straights' few-hundred-km bow
/// failed the yard's exact straightness test.
/// </summary>
public class SettlementStopTests
{
    [Theory]
    [InlineData("frontier:2")]
    [InlineData("frontier:5")]
    [InlineData("deadLines:12")]
    [InlineData("deepTerritory:8")]
    public void EveryNamedSettlementOnTheMainLineHasItsVillage(string spec)
    {
        var r = Routes.Generate(DataFile.FindContentRoot(), spec, 6);
        var towns = r.Plan!.Landmarks.Where(l => l.Type is "halt" or "town" && l.Edge == "main").ToList();
        Assert.NotEmpty(towns);
        foreach (var town in towns)
            Assert.True(r.Features.Any(f => f.Kind == FeatureKind.Village && f.Start < town.S1 && town.S0 < f.End && f.Stop is { Valid: true })
                || r.Features.Any(f => f.Kind == FeatureKind.Facility && f.Stop is not null && f.Start < town.S1 + 100 && town.S0 < f.End + 100),
                $"the {town.Type} at {town.S0:0}-{town.S1:0} has no village");
    }
}
