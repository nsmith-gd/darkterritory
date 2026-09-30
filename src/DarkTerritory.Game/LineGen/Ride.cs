using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.LineGen;

/// <summary>
/// Ride mode (linegen plan §20.2): the cab worked by the line's authority, as the validator's ideal driver keeps to
/// it (§16.1), so a designer can watch a generated line go by. It doesn't stop at the facilities.
/// </summary>
public static class Ride
{
    public static void Drive(TrainOnLine train, LinePlan plan, ref TrainControls controls)
    {
        double target = LineAuthority.For(plan, train.Line).Allowed(train);
        // Short of the end of the line (the arrival yard's buffer stop), stop.
        double remaining = train.Line.PathLength(train.Dynamics.Path) - train.Dynamics.Distance;
        target = Math.Min(target, Math.Sqrt(2 * plan.Authority.BrakeMs2 * 0.8 * Math.Max(0, remaining - 30)));
        double v = train.Dynamics.Speed;
        controls.Reverser = 1;
        controls.Brake = v > target + 0.3 ? 1 : 0;
        controls.Throttle = v < target - 1.5 ? 1 : v < target - 0.3 ? 0.4 : 0;
    }
}
