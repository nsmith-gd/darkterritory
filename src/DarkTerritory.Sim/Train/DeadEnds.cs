using DarkTerritory.Sim.Rail;

namespace DarkTerritory.Sim.Train;

/// <summary>
/// A dead line's end, taken too fast (the director's decision of 7 Oct 2026, ARCHITECTURE §8 note 286: "the switch itself
/// shouldn't cause derail, it should be lines that lead nowhere"). The Switchman throws a junction ahead and the train goes
/// down a line that ends at buffers in the dark. That costs the clock: stop, back out, set the points back by hand. It
/// derails the train only when the driver runs off the end over <see cref="OverspeedTuning.DeadEndDerailAbove"/>, and only
/// once the cab's warning (the bend warning's bell and a HUD line) has been up a full lead: visibly the driver's mistake.
/// </summary>
/// <param name="OnDeadLine">The engine's rake is down a dead line.</param>
/// <param name="Warning">Running toward its end too fast to stop under <see cref="DerailMs"/> within the distance left (and the lead).</param>
/// <param name="AheadM">To the buffers (m).</param>
/// <param name="DerailMs">Over this it goes through them and off (m/s).</param>
public readonly record struct DeadEndWarning(bool OnDeadLine, bool Warning, double AheadM, double DerailMs);

public static class DeadEnds
{
    /// <summary>A path that's a dead line's: off into the Territory to a buffer stop, never rejoining.</summary>
    public static bool IsDeadLine(TrainOnLine train, int path) =>
        path >= 0 && path < train.Line.Branches.Count && train.Line.Branches[path] is { Kind: BranchKind.DeadLine, Rejoins: false };

    /// <summary>
    /// The engine's rake as it is now, against its dead line's end: from the train and the line alone, so every machine
    /// works it out the same and nothing is sent (as <see cref="LineGen.TrackRules.Assess"/>).
    /// </summary>
    public static DeadEndWarning Assess(TrainOnLine train, OverspeedTuning t)
    {
        var rake = train.Dynamics;
        if (train.Wreck is not null || !IsDeadLine(train, rake.Path))
            return default;
        double ahead = Math.Max(0, train.Line.PathLength(rake.Path) - rake.Distance);
        double v = rake.Velocity, limit = t.DeadEndDerailAbove;
        bool warn = v > limit && ahead <= t.WarnDistance(v, limit, rake.RatedBrakeDecel, t.LeadSeconds);
        return new DeadEndWarning(true, warn, ahead, limit);
    }

    /// <summary>
    /// Host, after the train's step: whether the engine's rake hit its dead line's buffers this tick over the limit, with the
    /// warning counted before the step (<paramref name="warnSeconds"/>). Returns that speed (m/s) when it goes off the end
    /// with the warning up a full lead; a hit short of the lead is the buffer stop's damage alone, and counted.
    /// </summary>
    public static double? OverranThisTick(TrainOnLine train, int path, double warnSeconds, OverspeedTuning t, ref int spared)
    {
        if (!IsDeadLine(train, path))
            return null;
        int front = train.Dynamics.Consist.Vehicles[0].Id;
        foreach (var c in train.ContactsThisTick)
        {
            if (c.Rear != -1 || c.Front != front || c.ClosingSpeed <= t.DeadEndDerailAbove)
                continue;
            if (warnSeconds + 1e-9 < t.LeadSeconds)
            {
                spared++;
                return null;
            }
            return c.ClosingSpeed;
        }
        return null;
    }
}
