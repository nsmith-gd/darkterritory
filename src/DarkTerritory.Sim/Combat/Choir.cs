namespace DarkTerritory.Sim.Combat;

public enum ChoirPhase : byte { Distant, Approach, Swarm }

/// <summary>
/// The Choir (GDD App. A.6): not an entity, a global value. Every round fired raises it; silence for 45 s
/// lets it fall. It cannot be killed, and shooting it raises it faster than it thins. The keystone of the
/// roster: it's why the gunner's job is restraint.
/// </summary>
public struct ChoirState
{
    public double Aggro;
    public double SecondsSinceShot;
    /// <summary>Raised by livestock aboard (App. B.8) and during a Vigil.</summary>
    public double Floor;

    /// <summary>Seconds-since-shot saturates here so it stays replicable.</summary>
    public const double LongAgo = 100_000;

    public static ChoirState Quiet => new() { SecondsSinceShot = LongAgo };

    public readonly ChoirPhase Phase(ChoirTuning t) =>
        Aggro >= t.SwarmThreshold ? ChoirPhase.Swarm : Aggro >= t.ApproachThreshold ? ChoirPhase.Approach : ChoirPhase.Distant;

    public void RoundFired(ChoirTuning t)
    {
        Aggro = Math.Min(t.MaxAggro, Aggro + t.AggroPerRound);
        SecondsSinceShot = 0;
    }

    /// <summary>App. C.2: the Vigil's vent is deafening. Aggro to maximum, instantly.</summary>
    public void Deafening(ChoirTuning t)
    {
        Aggro = t.MaxAggro;
        SecondsSinceShot = 0;
    }

    public void Step(ChoirTuning t, double dt)
    {
        SecondsSinceShot = Math.Min(LongAgo, SecondsSinceShot + dt);
        if (SecondsSinceShot > t.QuietSecondsBeforeDecay)
            Aggro -= t.DecayPerSecond * dt;
        Aggro = Math.Max(Aggro, Floor);
    }
}
