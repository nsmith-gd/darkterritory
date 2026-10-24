using Ballast;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Route;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// #613 (n1-A2's finding: part of the survival drop is time at stops, not the director): frontier:4's first stop, a grain
/// elevator on a dead yard's power, its powerhouse out by the switch and the belt's drive house 320 m back down the spur.
/// </summary>
public partial class StopCrewTests
{
    static readonly string Content = DataFile.FindContentRoot();

    /// <summary>
    /// From the powerhouse, the belt's hand walked to the drive house by the track-side walk along the spur, which went nowhere
    /// from out by the switch: it stood there the rest of the stop, the belt never ran, and the driver waited out its give-ups
    /// (frontier:4 seed 4 with two bots: 540 s at the loading for nothing, then the dawn caught the train 6.6 km in). Now it
    /// goes by the foot path, as to the powerhouse (note 639), and the grain goes in.
    /// </summary>
    [Fact]
    public void ADeadYardsBeltIsStartedFromAPowerhouseOutByTheSwitch()
    {
        var route = Routes.Generate(Content, "frontier:4", 5);
        int elevator = route.Of(FeatureKind.Facility).ToList().FindIndex(f => f.Facility == FacilityKind.GrainElevator);
        var r = FacilityWork.Run(route, elevator, T, P, Tuning.Boiler, Tuning.Run, F, Tuning.Route.Junctions, cars: 5, hands: 0, seconds: 1200,
            yardLength: Tuning.Route.YardLength);
        string said = $"left {r.Departed} at {r.Seconds} s; legs {string.Join(", ", r.Legs)}; did {string.Join(", ", r.Doing)}";
        Assert.True(r.Departed, said);
        Assert.Contains("restarting the power", r.Doing);
        Assert.Contains("starting the belt", r.Doing);
        // Grain in. Note 1009: since the town's yard has its own limit (tiers.json terminus townYardLimitSpeed), the run home is
        // reckoned shorter, and with the night's spare for the next stop this loading is called done for it (note 609's
        // Onward) at 0.8 car-loads, not the 3 it ran to before.
        Assert.True(r.LoadedAfter > r.LoadedBefore + 0.5, $"loaded {r.LoadedBefore} → {r.LoadedAfter}; {said}");
    }
}
