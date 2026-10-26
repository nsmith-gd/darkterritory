using DarkTerritory.Sim.LineGen;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Linegen plan §15.1's terrain affinity as the director reads it (note 1048): under a tag, the kinds it names weigh their
/// multiplier, and every other kind stays as it was. Read as 0, a climb sent only the Cinder Hounds (kill 59 % of the
/// Local/Deep sweep's spend against a 40 % share).
/// </summary>
public class DirectorAffinityTests
{
    static PlanDirector Context() => new([], [], [], new PlanPressure(100, []), [], [])
    {
        Affinity = new Dictionary<string, IReadOnlyDictionary<string, double>>
        {
            ["climb"] = new Dictionary<string, double> { ["cinderHound"] = 1.5 },
            ["climb_long"] = new Dictionary<string, double> { ["cinderHound"] = 2 },
        },
    };

    [Fact]
    public void AKindATagDoesntNameKeepsItsWeight()
    {
        var context = Context();
        Assert.Equal(1, context.AffinityOf(["climb"], "whistler"));
        Assert.Equal(1, context.AffinityOf(["near_facility"], "whistler"));
        Assert.Equal(1, context.AffinityOf([], "cinderHound"));
    }

    [Fact]
    public void AKindATagNamesWeighsItsMultiplierUnderEachTag()
    {
        var context = Context();
        Assert.Equal(1.5, context.AffinityOf(["climb"], "cinderHound"));
        Assert.Equal(3, context.AffinityOf(["climb", "climb_long"], "cinderHound"));
    }
}
