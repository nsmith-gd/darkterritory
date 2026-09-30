namespace DarkTerritory.Sim.Combat;

/// <summary>Where the Choir is: not about, gathering (the long rising telegraph), or here (the swarm).</summary>
public enum ChoirPhase : byte { Distant, Approach, Swarm }

/// <summary>
/// THE CHOIR · sound · structural (GDD v1.1 §14, §21, App. A.7, C.7). Not spawned: drawn by the crew's combined loudness
/// (voices, cannons, the whistle, machinery) held above a threshold. The meter is measured over a few seconds, so a single
/// shout doesn't count and a crew arguing at full volume does. Held loud, it gathers (a long, rising telegraph); quiet before
/// it's gathered and it's skipped. Gathered, the swarm arrives (<see cref="Enemies.ChoirGhost"/>). Quiet held while it's
/// here and it disperses. It takes at most one crew member a run, then it's gone for the rest of it. Host-side; replicated.
/// </summary>
public struct ChoirState
{
    /// <summary>The crew's loudness, smoothed over the meter's window (App. C.7).</summary>
    public double Loudness;
    /// <summary>How far it's gathered, 0 to 1 (the telegraph). 1: the swarm's here.</summary>
    public double Build;
    /// <summary>The swarm's here.</summary>
    public bool Present;
    /// <summary>It's taken its one crew member this run (App. A.7 LIMIT): gone for the rest of it.</summary>
    public bool Spent;
    /// <summary>Seconds quiet while the swarm's here (it disperses at the tuning's).</summary>
    public double QuietSeconds;
    /// <summary>A floor under the loudness: livestock aboard, never quiet (App. B.9); the Vigil's vent.</summary>
    public double Floor;

    public static ChoirState Quiet => new();

    public readonly ChoirPhase Phase(ChoirTuning t) => Present ? ChoirPhase.Swarm : Build > 0 ? ChoirPhase.Approach : ChoirPhase.Distant;

    /// <summary>A cannon fired: a burst of loudness (App. C.7 "every cannon shot feeds the loudness meter").</summary>
    public void RoundFired(ChoirTuning t) => Loudness += t.RoundLoudness;

    /// <summary>The Vigil's vent is deafening (spec C.2): loud as it gets, while it roars.</summary>
    public void Deafening(ChoirTuning t) => Loudness = Math.Max(Loudness, t.MaxLoudness);

    /// <summary>
    /// A tick of the meter: <paramref name="now"/> is this tick's loudness (voices, the whistle, machinery). Returns true on the
    /// tick it's gathered and the swarm should come.
    /// </summary>
    public bool Step(ChoirTuning t, double now, double dt)
    {
        double target = Math.Max(now, Floor);
        Loudness += (target - Loudness) * Math.Min(1, dt / t.WindowSeconds);
        Loudness = Math.Clamp(Loudness, 0, t.MaxLoudness);
        if (Spent)
        {
            Build = 0;
            Present = false;
            return false;
        }
        bool loud = Loudness >= t.Threshold;
        if (Present)
        {
            QuietSeconds = loud ? 0 : QuietSeconds + dt;
            return false;
        }
        Build = Math.Clamp(Build + (loud ? dt / t.BuildSeconds : -t.QuietDecayPerSecond * dt), 0, 1);
        if (Build < 1)
            return false;
        Present = true;
        QuietSeconds = 0;
        return true;
    }

    /// <summary>The swarm's gone (quiet held, or its one taken).</summary>
    public void Disperse(bool took)
    {
        Present = false;
        Build = 0;
        QuietSeconds = 0;
        Spent |= took;
    }
}
