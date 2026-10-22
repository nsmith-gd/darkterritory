using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Route;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 934 (queue #634; found under #609, note 895: no bot night ever stood at a halt): with <c>crew.haltStandSeconds</c>
/// on, the driver bot stands the train over a halt on the line (an abandoned village's, the route card's "halt" rows) for
/// that long, everyone aboard, and goes on; off (the game's default for now), it drives past as before.
/// </summary>
public class HaltStopTests
{
    static readonly string Content = DataFile.FindContentRoot();

    static HarnessReport Night(double stand, double seconds)
    {
        var route = Routes.Generate(Content, "frontier:2", 8);
        double gate = route.GateOr(Tuning.Route.YardLength);
        var facilities = DataFile.Load<Run.FacilityTuning>(Path.Combine(Content, Run.FacilityTuning.File));
        facilities = facilities with { Crew = facilities.Crew with { HaltStandSeconds = stand } };
        return Harness.Run(route.Build(), Tuning.Train, Tuning.Player, new HarnessOptions
        {
            Bots = 2,
            Cars = 8,
            Seconds = seconds,
            Seed = 1,
            Link = new Ballast.Net.LinkConditions(0.09, 0.02, 0.03),
            StartDistance = Tuning.Run.DepartFrom(gate, Train.Consist.Uniform(Tuning.Train, 8, 1).LengthMetres),
            Combat = Tuning.Combat,
            Enemies = Tuning.Enemies,
            Route = route,
            Run = Tuning.Run,
            Facilities = facilities,
            YardLength = gate,
        });
    }

    [Fact]
    public void ItsOffInTheGameForNow() =>
        Assert.Equal(0, DataFile.Load<Run.FacilityTuning>(Path.Combine(Content, Run.FacilityTuning.File)).Crew.HaltStandSeconds);

    [Fact]
    public void OnTheDriverStandsAtTheFirstHaltItsWhileAndGoesOn()
    {
        // frontier:2's first halt is 1.70–2.00 km in; the train's stood there by about 80 s and gone on after its 60 s.
        var route = Routes.Generate(Content, "frontier:2", 8);
        var halt = route.Features.First(f => f.Kind == FeatureKind.Village && f.Stop?.Halt is not null);
        var on = Night(stand: 60, seconds: 200);
        var stood = Assert.Single(on.Stops!, s => s.Kind == "Halt");
        Assert.InRange(stood.Seconds, 60, 60 + 120);
        // Gone on: past the halt's end (the run-in and pull-away are eased, note 935, so how far past in the time is the
        // driving's, not this test's).
        Assert.True(on.TrainDistance > halt.End, $"stuck at {on.TrainDistance:0} m");
        // Off, the same night drives past it.
        var off = Night(stand: 0, seconds: 200);
        Assert.DoesNotContain(off.Stops!, s => s.Kind == "Halt");
    }
}
