namespace DarkTerritory.Sim.Combat;

/// <summary>Mirror of content/tuning/combat.json. Field docs live in that file.</summary>
public sealed record CombatTuning(GunTuning Guns, ChoirTuning Choir)
{
    public const string File = "tuning/combat.json";
}

public sealed record GunTuning(double RoundsPerSecond, double Range, double TraverseDegrees, double DeadZoneDegrees,
    double MinPitchDegrees, double MaxPitchDegrees, int Ammo, double Reach, double DamagePerRound, double MinPressure)
{
    public int TicksPerRound => (int)Math.Round(SimConstants.TickRate / RoundsPerSecond);
}

public sealed record ChoirTuning(double AggroPerRound, double QuietSecondsBeforeDecay, double DecayPerSecond,
    double ApproachThreshold, double SwarmThreshold, double MaxAggro, double LivestockFloor);
