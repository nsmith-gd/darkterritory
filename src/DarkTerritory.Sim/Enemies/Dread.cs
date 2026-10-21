namespace DarkTerritory.Sim.Enemies;

/// <summary>
/// Dread at the stops (queue #646, note 944; the director's playtest of 20 Oct evening, line 15: "You start safely collecting
/// loot and then suddenly it doesn't feel so safe. You've been in there just long enough"): the longer a crew stays afoot off
/// the train in one outing, the less safe it is. Three stages by the outing's seconds afoot:
/// <list type="bullet">
/// <item><b>Signs</b> (from the first step off): note 327's signs, coming more often and nearer as the outing goes on.</item>
/// <item><b>A scare</b> (from <see cref="DreadTuning.ScareAt"/>): the signs at close range, at the scare's pace.</item>
/// <item><b>A real threat</b> (from <see cref="DreadTuning.ThreatAt"/>, once an outing): the encounter clock is brought up to due
/// (note 908), so the director's own channels send one, met out there by the afoot weighting, under #609's placement and
/// #611's caps; each kind's telegraph is its own. Not a channel of its own.</item>
/// </list>
/// Pure: the director keeps the outing's seconds and asks these.
/// </summary>
public static class Dread
{
    public enum Stage { Signs, Scare, Threat }

    public static Stage At(double seconds, DreadTuning t) =>
        seconds >= t.ThreatAt ? Stage.Threat : seconds >= t.ScareAt ? Stage.Scare : Stage.Signs;

    /// <summary>How far through the build to the scare (0 at the first step off, 1 at <see cref="DreadTuning.ScareAt"/> and after).</summary>
    public static double Build(double seconds, DreadTuning t) => t.ScareAt > 0 ? Math.Clamp(seconds / t.ScareAt, 0, 1) : 1;

    /// <summary>The factor on the wait to the next sign: 1 at the first step off, falling to <see cref="DreadTuning.SignEveryAtScare"/>.</summary>
    public static double SignEvery(double seconds, DreadTuning t) => 1 + (Math.Clamp(t.SignEveryAtScare, 0.05, 1) - 1) * Build(seconds, t);

    /// <summary>
    /// The factor on how far out a sign is shown: 1 at first, falling to <see cref="DreadTuning.SignOutAtScare"/> by the scare,
    /// and <see cref="DreadTuning.ScareOut"/> from it.
    /// </summary>
    public static double SignOut(double seconds, DreadTuning t) => At(seconds, t) >= Stage.Scare
        ? Math.Clamp(t.ScareOut, 0.05, 1)
        : 1 + (Math.Clamp(t.SignOutAtScare, 0.05, 1) - 1) * Build(seconds, t);
}

/// <summary>Dread at the stops' numbers (note 944). Mirror of enemies.json <c>director.afoot.dread</c>; field docs live there.</summary>
public sealed record DreadTuning
{
    public bool On { get; init; }
    public double ScareAt { get; init; } = 90;
    public double ThreatAt { get; init; } = 180;
    public double SignEveryAtScare { get; init; } = 0.5;
    public double SignOutAtScare { get; init; } = 0.7;
    public double ScareOut { get; init; } = 0.45;
    public double ForgetSeconds { get; init; } = 30;
}
