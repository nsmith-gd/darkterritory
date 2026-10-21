using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The final surge (#663, note 960; #613's late − early): over the metres before the terminus approach's kept quiet, λ and the
/// pressure's build rise to a peak, so that window's encounters come before it, never in it. Off as shipped.
/// </summary>
public class FinalSurgeTests
{
    static readonly DirectorTuning D = Tuning.Enemies.Director;
    static readonly FinalSurgeTuning T = D.Encounters.Drive.FinalSurge with { On = true };

    [Fact]
    public void ItShipsOff() => Assert.False(D.Encounters.Drive.FinalSurge.On);

    [Fact]
    public void ItRampsUpToThePeakAndNoFurther()
    {
        Assert.Equal(1, T.At(T.RampMetres + 1), 9);
        Assert.Equal(1, T.At(T.RampMetres), 9);
        Assert.Equal(1 + (T.Peak - 1) / 2, T.At(T.RampMetres / 2), 9);
        Assert.True(T.At(1) > T.At(T.RampMetres / 2));
        Assert.True(T.At(1) <= T.Peak + 1e-9);
        // Past the start of the quiet (behind it), nothing: the quiet holds the director anyway.
        Assert.Equal(1, T.At(-10), 9);
        Assert.Equal(1, (T with { On = false }).At(T.RampMetres / 2), 9);
    }

    static World Near(bool on, double before)
    {
        var e = Tuning.Enemies with
        {
            Director = D with { Encounters = D.Encounters with { Drive = D.Encounters.Drive with { FinalSurge = T with { On = on } } } },
        };
        var route = RouteGenerator.Generate(Tuning.Route, RouteTier.Frontier, 3);
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 4, 1)), route.Build(), route.RunLength - 500 - before);
        var world = new World(train, Tuning.Combat);
        world.EnableEnemies(e, route, 3, crew: 2, authority: true);
        return world;
    }

    /// <summary>Near the terminus approach, on: λ and the pressure's rate are raised; off, or far from it, as before.</summary>
    [Fact]
    public void NearTheTerminusApproachTheNightPresses()
    {
        var on = Near(on: true, before: T.RampMetres / 4);
        var off = Near(on: false, before: T.RampMetres / 4);
        on.Director!.Decide(on, 1_000, [], 500);
        off.Director!.Decide(off, 1_000, [], 500);
        Assert.True(on.Director.Surge > 1.5, $"surge {on.Director.Surge}");
        Assert.Equal(1, off.Director.Surge);
        Assert.True(on.Director.Terms.Rate > off.Director.Terms.Rate * 1.5, $"on {on.Director.Terms.Rate}, off {off.Director.Terms.Rate}");
        Assert.True(on.Director.EncounterRate > off.Director.EncounterRate * 1.5);
        var far = Near(on: true, before: T.RampMetres * 3);
        far.Director!.Decide(far, 1_000, [], 500);
        Assert.Equal(1, far.Director.Surge);
    }
}
