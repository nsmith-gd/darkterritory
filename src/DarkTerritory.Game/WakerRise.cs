using DarkTerritory.Sim.Enemies;

namespace DarkTerritory.Game;

/// <summary>
/// A Waker getting up out of the ground (note 599; docs/design/creatures/wakers.md §3: "the first sight of one reads as the
/// land getting up"), as both its stand-ins draw it and the effects time it: under the ground (<see cref="Depth"/> down)
/// till it stirs, then over its rise (TELEGRAPH, <see cref="WakersTuning.RiseSeconds"/>) heaving up in surges, quick at
/// first and slowing as it stands, and stood once it runs.
/// </summary>
public static class WakerRise
{
    /// <summary>How far under the ground it lies before it rises (m): the stand-in's whole height.</summary>
    public const double Depth = 18;

    /// <summary>How far up it is (0 under the ground .. 1 stood).</summary>
    public static double Up(SpinePhase phase, double phaseSeconds, double riseSeconds) => phase switch
    {
        SpinePhase.Dormant => 0,
        SpinePhase.Telegraph => Ease(Math.Clamp(phaseSeconds / Math.Max(0.1, riseSeconds), 0, 1)),
        _ => 1,
    };

    /// <summary>How far under its standing height it's drawn (m).</summary>
    public static double Sink(SpinePhase phase, double phaseSeconds, double riseSeconds) => Depth * (1 - Up(phase, phaseSeconds, riseSeconds));

    /// <summary>Seconds since it began to rise (−1 not yet): the rise's own, and on through the chase and the catch.</summary>
    public static double Since(SpinePhase phase, double phaseSeconds, double riseSeconds) => phase switch
    {
        SpinePhase.Telegraph => phaseSeconds,
        SpinePhase.Commit or SpinePhase.Punish => riseSeconds + phaseSeconds,
        _ => -1,
    };

    /// <summary>Quick out of the ground and slowing as it stands, in three surges, the land heaving with each.</summary>
    static double Ease(double s) => Math.Clamp(1 - (1 - s) * (1 - s) + 0.06 * Math.Sin(s * Math.PI * 6) * (1 - s), 0, 1);
}
