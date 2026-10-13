using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Enemies;

/// <summary>
/// Boarding-first (the director's decisions of 6 Oct 2026, GDD App. F.1; ARCHITECTURE §8 note 286): "Slowing opens the
/// doors. Stops, facilities and tight curves are where things board." What the spawn rules and the boarders read of the
/// train and the line to know whether this is such a place: from the train and the line alone, so the same on every machine.
/// </summary>
public static class Boarding
{
    /// <summary>
    /// A bend at least as sharp as <paramref name="radius"/> (m) under any of the train, or within <paramref name="aheadM"/>
    /// ahead of it the way it's going: a tight bend, where a driver who reads the boards is slow.
    /// </summary>
    public static bool OnTightBend(TrainOnLine train, double radius, double aheadM)
    {
        var rake = train.Dynamics;
        if (radius <= 0)
            return false;
        double kTight = 1 / radius, length = train.Line.PathLength(rake.Path);
        int travel = rake.Velocity < 0 ? -1 : 1;
        double from = Math.Min(rake.RearDistance, rake.Distance), to = Math.Max(rake.RearDistance, rake.Distance);
        if (travel > 0)
            to += aheadM;
        else
            from -= aheadM;
        const double Step = 10;
        for (double s = Math.Max(0, from); s <= Math.Min(length, to); s += Step)
            if (Math.Abs(train.Line.Sample(rake.Path, s).Curvature) >= kTight)
                return true;
        return false;
    }

    /// <summary>
    /// A car shut up tight: every door and the hatch shut, and its shell whole (a breach is an open door, decided 1 Oct).
    /// What a shut door stops (note 286's table) can't get in.
    /// </summary>
    public static bool ShutUp(TrainOnLine train, int car) => train.Vehicles[car] is { DoorsOpen: 0, Breached: false };
}
