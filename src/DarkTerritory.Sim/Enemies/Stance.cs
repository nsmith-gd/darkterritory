namespace DarkTerritory.Sim.Enemies;

/// <summary>
/// The train's state as the director reads it (queue #703, note 999; the director's playtest of 23 Oct, line 35: "two gameplay
/// states with substates ... stopped ... running"): running (at speed, slowing, through a facility) or stopped (at a stop, at a
/// halt, in the wilderness). Pure: the director keeps the last second's speed and the seconds stood.
/// </summary>
public enum StanceKind : byte
{
    AtSpeed,
    Slowing,
    ThroughFacility,
    AtStop,
    Halt,
    Wilderness,
}

public static class Stance
{
    /// <summary>Stood: the train's speed under <see cref="StanceTuning.StandBelow"/>.</summary>
    public static bool Stopped(StanceKind k) => k >= StanceKind.AtStop;

    /// <summary>
    /// The stance from the train's speed now and a second ago (m/s), whether the run is at a facility, and
    /// <see cref="Director.StopAt"/>'s key (−1 none, under 1000 a facility's stop, 1000 and over a halt).
    /// </summary>
    public static StanceKind Of(double speed, double previousSpeed, bool atFacility, int stopKey, StanceTuning t)
    {
        if (speed < t.StandBelow)
            return stopKey >= 1000 ? StanceKind.Halt : stopKey >= 0 || atFacility ? StanceKind.AtStop : StanceKind.Wilderness;
        if (atFacility)
            return StanceKind.ThroughFacility;
        return speed < t.SlowBelow && speed < previousSpeed - t.SlowingBy ? StanceKind.Slowing : StanceKind.AtSpeed;
    }

    /// <summary>
    /// The factor on the encounter rate λ for a stance (1 with the stance off): running, its own; stopped, rising from 1 to its
    /// own over <see cref="StanceTuning.RampSeconds"/> stood (a crew that stays meets more; a stop of a few seconds isn't punished).
    /// </summary>
    public static double Rate(StanceKind k, double stoodSeconds, StanceTuning t)
    {
        if (!t.On)
            return 1;
        double f = Math.Max(0, t.Rate.GetValueOrDefault(Name(k), 1));
        if (!Stopped(k) || t.RampSeconds <= 0)
            return f;
        return 1 + (f - 1) * Math.Clamp(stoodSeconds / t.RampSeconds, 0, 1);
    }

    /// <summary>The stance's name in the tuning (camel case).</summary>
    public static string Name(StanceKind k) => char.ToLowerInvariant(k.ToString()[0]) + k.ToString()[1..];
}

/// <summary>The stance's numbers (note 999). Mirror of enemies.json <c>director.encounters.stance</c>; field docs live there.</summary>
public sealed record StanceTuning
{
    public bool On { get; init; }
    public double StandBelow { get; init; } = 0.3;
    public double SlowBelow { get; init; } = 8;
    public double SlowingBy { get; init; } = 0.2;
    public double RampSeconds { get; init; } = 45;
    public Dictionary<string, double> Rate { get; init; } = [];
}

public sealed partial record EncountersTuning
{
    /// <summary>λ by the train's state (note 999).</summary>
    public StanceTuning Stance { get; init; } = new();
    /// <summary>Whom a threat is sent at, and for what (note 999).</summary>
    public TargetTuning Target { get; init; } = new();
}
