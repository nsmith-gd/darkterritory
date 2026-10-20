using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The encounter rate (the director's brief §1, §3; note 877; spec B.10 "The encounter rate"): the brief's six mathematical
/// requirements, on the shipped tuning, at every tier, over crews 1–8 and trains 2–24 cars.
/// </summary>
public class EncounterPacingTests
{
    static readonly EncounterTuning T = Tuning.Enemies.Director.Encounters;
    static readonly int[] Crews = [1, 2, 3, 4, 6, 8];
    static readonly int[] Cars = [2, 3, 4, 8, 12, 18, 24];
    static readonly RouteTier[] Tiers = Enum.GetValues<RouteTier>();
    static IEnumerable<double> Steps(int n = 50) => Enumerable.Range(0, n + 1).Select(i => (double)i / n);

    [Fact]
    public void TheShippedTuningIsTheBriefsPrototype()
    {
        // λ = (0.12 + (0.33 + 0.035(P−1))·p^1.2)·(1 + 0.07(P−1))·(1 + 0.05·max(0, C−3)), at Frontier's 1.
        foreach (int p in Crews)
            foreach (int c in Cars)
                foreach (double x in Steps(10))
                {
                    double brief = (0.12 + (0.33 + 0.035 * (p - 1)) * Math.Pow(x, 1.2)) * (1 + 0.07 * (p - 1)) * (1 + 0.05 * Math.Max(0, c - 3));
                    Assert.Equal(Math.Min(T.Max, brief), EncounterPacing.Rate(x, p, c, RouteTier.Frontier, T), 12);
                }
    }

    [Fact]
    public void TheRateNeverFallsAsTheNightGoesOn()
    {
        foreach (var tier in Tiers)
            foreach (int p in Crews)
                foreach (int c in Cars)
                {
                    double last = -1;
                    foreach (double x in Steps())
                    {
                        double r = EncounterPacing.Rate(x, p, c, tier, T);
                        Assert.True(r >= last, $"{tier} crew {p} cars {c}: {r} at {x} under {last}");
                        last = r;
                    }
                    Assert.True(EncounterPacing.Rate(1, p, c, tier, T) > EncounterPacing.Rate(0, p, c, tier, T) || EncounterPacing.Rate(0, p, c, tier, T) == T.Max);
                }
    }

    [Fact]
    public void MoreCrewMeansMoreEncounters()
    {
        foreach (var tier in Tiers)
            foreach (int c in Cars)
                foreach (double x in Steps(10))
                    for (int p = 1; p < 8; p++)
                    {
                        double less = EncounterPacing.Rate(x, p, c, tier, T), more = EncounterPacing.Rate(x, p + 1, c, tier, T);
                        Assert.True(more > less || more == T.Max, $"{tier} cars {c} at {x}: crew {p + 1} {more} vs {p} {less}");
                    }
    }

    [Fact]
    public void ABiggerCrewsNightWorsensFaster()
    {
        // The late-night rise (the end's rate less the gate's) is steeper for each crewmate added, absolutely and as a share.
        foreach (var tier in Tiers)
            foreach (int c in Cars.Where(c => c <= 12))
                for (int p = 1; p < 8; p++)
                {
                    double Rise(int crew) => EncounterPacing.Rate(1, crew, c, tier, T) - EncounterPacing.Rate(0, crew, c, tier, T);
                    double Ratio(int crew) => EncounterPacing.Rate(1, crew, c, tier, T) / EncounterPacing.Rate(0, crew, c, tier, T);
                    Assert.True(Rise(p + 1) > Rise(p), $"{tier} cars {c}: rise at {p + 1} {Rise(p + 1)} vs {Rise(p)}");
                    Assert.True(Ratio(p + 1) > Ratio(p), $"{tier} cars {c}: ratio at {p + 1} {Ratio(p + 1)} vs {Ratio(p)}");
                }
        Assert.True(EncounterPacing.Ramp(8, T) > EncounterPacing.Ramp(1, T));
    }

    [Fact]
    public void MoreCarsMeanMoreEncountersWithNoBreakpoint()
    {
        foreach (var tier in Tiers)
            foreach (int p in Crews)
                foreach (double x in Steps(10))
                {
                    double last = EncounterPacing.Rate(x, p, T.CarsFrom, tier, T), step = -1;
                    for (int c = T.CarsFrom + 1; c <= 24; c++)
                    {
                        double r = EncounterPacing.Rate(x, p, c, tier, T);
                        if (r == T.Max)
                            break;
                        Assert.True(r > last, $"{tier} crew {p} at {x}: {c} cars {r} vs {last}");
                        // Even steps: every car past carsFrom adds the same, eight and nine cars no different from three and four.
                        if (step >= 0)
                            Assert.Equal(step, r - last, 9);
                        step = r - last;
                        last = r;
                    }
                    // Short trains aren't punished below carsFrom: the floor is the same.
                    Assert.Equal(EncounterPacing.Rate(x, p, 1, tier, T), EncounterPacing.Rate(x, p, T.CarsFrom, tier, T));
                }
    }

    [Fact]
    public void ASoloCrewOnAShortTrainHasAFloor()
    {
        // Brief §1 B, §3 requirement 5: solo play has a meaningful minimum: at least one opportunity every ten minutes at the
        // gate on the easiest tier, and a solo 20-minute Frontier night holds the brief's 5–8 band (§7) in opportunities.
        Assert.True(EncounterPacing.Rate(0, 1, 2, RouteTier.Local, T) >= 0.09);
        double night = EncounterPacing.Night(20, 1, 2, RouteTier.Frontier, T);
        Assert.InRange(night, 5, 8);
        // Nobody alive counts as one: the rate never collapses to nothing.
        Assert.Equal(EncounterPacing.Rate(0.5, 1, 2, RouteTier.Frontier, T), EncounterPacing.Rate(0.5, 0, 2, RouteTier.Frontier, T));
    }

    [Fact]
    public void TheRateIsBoundedAndTheTierRises()
    {
        foreach (var tier in Tiers)
            Assert.True(EncounterPacing.Rate(1, 64, 200, tier, T) <= T.Max);
        foreach (int p in Crews)
            foreach (double x in Steps(10))
                for (int i = 1; i < Tiers.Length; i++)
                    Assert.True(EncounterPacing.Rate(x, p, 8, Tiers[i], T) > EncounterPacing.Rate(x, p, 8, Tiers[i - 1], T));
        // Out of range inputs are held, not extrapolated.
        Assert.Equal(EncounterPacing.Rate(1, 4, 8, RouteTier.Frontier, T), EncounterPacing.Rate(3, 4, 8, RouteTier.Frontier, T));
        Assert.Equal(EncounterPacing.Rate(0, 4, 8, RouteTier.Frontier, T), EncounterPacing.Rate(-1, 4, 8, RouteTier.Frontier, T));
    }

    [Fact]
    public void ProgressIsTheFurtherOfTheClockAndTheLineAndNeverGoesBack()
    {
        // The clock ahead (standing still, loading): time counts.
        Assert.Equal(0.5, EncounterPacing.Progress(elapsed: 600, dawnSeconds: 1200, distance: 1000, runLength: 10_000));
        // The train ahead: the line counts.
        Assert.Equal(0.8, EncounterPacing.Progress(elapsed: 120, dawnSeconds: 1200, distance: 8000, runLength: 10_000), 12);
        // A train backing up doesn't take the night back.
        double before = EncounterPacing.Progress(120, 1200, 8000, 10_000);
        Assert.Equal(before, EncounterPacing.Progress(130, 1200, 5000, 10_000, before));
        // No dawn: only the line; no line: only the clock; both past the end: 1.
        Assert.Equal(0.25, EncounterPacing.Progress(9999, 0, 2500, 10_000));
        Assert.Equal(0.25, EncounterPacing.Progress(300, 1200, 0, 0));
        Assert.Equal(1, EncounterPacing.Progress(5000, 1200, 20_000, 10_000));
        // Stood still the whole night, the clock still brings it to its end.
        double p = 0;
        for (double t = 0; t <= 1200; t += 1)
        {
            double next = EncounterPacing.Progress(t, 1200, 500, 10_000, p);
            Assert.True(next >= p);
            p = next;
        }
        Assert.Equal(1, p);
    }

    [Fact]
    public void TheActiveCrewIsTheLiving()
    {
        // The dead don't count, nor one waiting in the queue for a Holdout (App. D.5: not dead, but not in the night either).
        var alive = new PlayerState();
        var dead = new PlayerState { Death = DeathCause.Mauled };
        var waiting = new PlayerState { Death = DeathCause.Waiting };
        Assert.Equal(2, EncounterPacing.ActiveCrew([(1, alive), (2, dead), (3, alive), (4, waiting)]));
        Assert.Equal(0, EncounterPacing.ActiveCrew([]));
    }

    [Fact]
    public void TheRealPowerIsTheSameEverywhereAndCloseToTheLibrarys()
    {
        // DMath.Pow(x, double): square roots and products only, so the bits don't depend on the platform's C library.
        foreach (double x in new[] { 0.0, 1e-9, 0.001, 0.1, 0.25, 0.5, 0.73, 0.999, 1, 2, 37.5 })
            foreach (double y in new[] { 0, 0.5, 1, 1.2, 1.5, 2, 2.75, 3.1 })
                Assert.Equal(Math.Pow(x, y), DMath.Pow(x, y), Math.Max(1e-13, Math.Pow(x, y) * 1e-13));
        Assert.Equal(DMath.Pow(0.3, 3), DMath.Pow(0.3, 3.0));
        Assert.True(double.IsNaN(DMath.Pow(-1, 0.5)));
        Assert.Equal(0.5, DMath.Pow(0.25, 0.5));
    }
}
