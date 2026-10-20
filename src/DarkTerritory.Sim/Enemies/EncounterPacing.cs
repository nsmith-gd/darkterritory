using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;

namespace DarkTerritory.Sim.Enemies;

/// <summary>
/// The encounter rate (the director's brief, docs/design/spawn-director-overhaul.md §1, §3; note 877; spec B.10 "The
/// encounter rate"): how many encounter opportunities a minute the director should make, by how far into the night the crew
/// is, how many are alive in it, how long the train is and the route's tier. Time × crew × train length: each makes the
/// night more hostile, and a bigger crew's night also gets worse faster.
/// <code>
/// λ = (base + (ramp + rampPerCrew·(P−1)) · p^power) · (1 + perCrew·(P−1)) · (1 + perCar·max(0, C−carsFrom)) · tier
/// </code>
/// to at most <see cref="EncounterTuning.Max"/>. Pure statics: nothing here draws, reads the world or holds state, so the
/// host and every client get the same bits (<see cref="DMath"/> for the power). The director's channels are driven by it in
/// #608; this is only the rate.
/// </summary>
public static class EncounterPacing
{
    /// <summary>
    /// How far into the night (0..1): the night's time toward dawn or the train's distance along the line, whichever is
    /// further (brief §1 A: "whichever places the crew further into the run"), and never less than <paramref name="previous"/>,
    /// so a stopped or backing train doesn't take the night back. With no dawn (<paramref name="dawnSeconds"/> 0 or less) only
    /// the line counts, and with no line only the clock.
    /// </summary>
    public static double Progress(double elapsed, double dawnSeconds, double distance, double runLength, double previous = 0)
    {
        double time = dawnSeconds > 0 ? elapsed / dawnSeconds : 0;
        double route = runLength > 0 ? distance / runLength : 0;
        return Math.Max(Math.Clamp(previous, 0, 1), Math.Clamp(Math.Max(time, route), 0, 1));
    }

    /// <summary>
    /// The crew the rate is for (brief §1 B): those in the night alive this tick, bots and players alike. A crewmate who's
    /// disconnected has left the session's crew (<see cref="World.CrewThisTick"/>), so isn't counted; the dead aren't either, nor
    /// one waiting in the queue for a Holdout (not dead, but not alive in the night).
    /// </summary>
    public static int ActiveCrew(IReadOnlyList<(int Id, PlayerState State)> crew)
    {
        int alive = 0;
        foreach (var (_, state) in crew)
            if (state.Alive)
                alive++;
        return alive;
    }

    /// <summary>
    /// Encounter opportunities a minute at <paramref name="progress"/> (0..1) for <paramref name="crew"/> alive (at least one
    /// counts) and <paramref name="cars"/> cars on <paramref name="tier"/>. Never falls as progress, crew or cars rise.
    /// </summary>
    public static double Rate(double progress, int crew, int cars, RouteTier tier, EncounterTuning t)
    {
        double ramp = Ramp(crew, t) * DMath.Pow(Math.Clamp(progress, 0, 1), Math.Max(0, t.Power));
        double rate = (Math.Max(0, t.Base) + ramp) * CrewFactor(crew, t) * CarFactor(cars, t)
            * Math.Max(0, t.TierFactor(tier));
        return Math.Min(rate, t.Max);
    }

    /// <summary><see cref="Rate"/> a second.</summary>
    public static double PerSecond(double progress, int crew, int cars, RouteTier tier, EncounterTuning t) =>
        Rate(progress, crew, cars, tier, t) / 60;

    /// <summary>How much the rate climbs from the gate to the end of the night (before the crew and car factors): steeper with each crewmate over one.</summary>
    public static double Ramp(int crew, EncounterTuning t) =>
        Math.Max(0, t.Ramp + Math.Max(0, t.RampPerCrew) * (Math.Max(1, crew) - 1));

    /// <summary>The crew's factor: 1 alone, <see cref="EncounterTuning.PerCrew"/> more for each crewmate over one.</summary>
    public static double CrewFactor(int crew, EncounterTuning t) => 1 + Math.Max(0, t.PerCrew) * (Math.Max(1, crew) - 1);

    /// <summary>The train's factor: 1 to <see cref="EncounterTuning.CarsFrom"/> cars, <see cref="EncounterTuning.PerCar"/> more for each car past it, with no breakpoints.</summary>
    public static double CarFactor(int cars, EncounterTuning t) => 1 + Math.Max(0, t.PerCar) * Math.Max(0, cars - t.CarsFrom);

    /// <summary>
    /// The opportunities a whole night of <paramref name="minutes"/> holds if progress runs evenly from 0 to 1 (the rate's
    /// mean over the night × its length): what the sweep's encounter counts are read against (brief §7). Midpoint sums over
    /// <paramref name="steps"/>.
    /// </summary>
    public static double Night(double minutes, int crew, int cars, RouteTier tier, EncounterTuning t, int steps = 200)
    {
        double sum = 0;
        for (int i = 0; i < steps; i++)
            sum += Rate((i + 0.5) / steps, crew, cars, tier, t);
        return sum / steps * minutes;
    }
}

/// <summary>
/// The encounter rate's tuning (<see cref="EncounterPacing"/>; note 877). Mirror of enemies.json <c>director.encounters</c>;
/// field docs live there. The defaults are the brief's prototype (§3). #607's encounter debt adds its <c>debt</c> here.
/// </summary>
public sealed record EncounterTuning
{
    public double Base { get; init; } = 0.12;
    public double Ramp { get; init; } = 0.33;
    public double RampPerCrew { get; init; } = 0.035;
    public double Power { get; init; } = 1.2;
    public double PerCrew { get; init; } = 0.07;
    public double PerCar { get; init; } = 0.05;
    public int CarsFrom { get; init; } = 3;
    public Dictionary<string, double> Tier { get; init; } = new() { ["local"] = 0.8, ["frontier"] = 1, ["deadLines"] = 1.2, ["deepTerritory"] = 1.4 };
    public double Max { get; init; } = 3;
    /// <summary>The tier's factor, by its camel-cased name in <see cref="Tier"/> (1 where it isn't listed).</summary>
    public double TierFactor(RouteTier tier) =>
        Tier.GetValueOrDefault(char.ToLowerInvariant(tier.ToString()[0]) + tier.ToString()[1..], 1);
}
