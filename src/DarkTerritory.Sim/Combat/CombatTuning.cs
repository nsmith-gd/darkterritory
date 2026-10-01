namespace DarkTerritory.Sim.Combat;

/// <summary>Mirror of content/tuning/combat.json. Field docs live in that file.</summary>
public sealed record CombatTuning(GunTuning Guns, ChoirTuning Choir)
{
    public const string File = "tuning/combat.json";
}

/// <summary>
/// The crude cannons (GDD v1.1 §12, App. C.3): arc-limited, loud, and every shot a timed decision. After each shot a full
/// manual reload (powder, ball, ram) at the gun before it fires again. Field docs live in combat.json.
/// </summary>
public sealed record GunTuning(double RoundsPerSecond, double Range, double TraverseDegrees, double DeadZoneDegrees,
    double MinPitchDegrees, double MaxPitchDegrees, int Ammo, double Reach, double DamagePerRound, double MinPressure)
{
    public int TicksPerRound => (int)Math.Round(SimConstants.TickRate / RoundsPerSecond);
    /// <summary>The reload's steps: powder, ball, ram (0: the old magazine gun, no reload).</summary>
    public int ReloadSteps { get; init; } = 3;
    /// <summary>Seconds of Use held at the gun per step.</summary>
    public double ReloadStepSeconds { get; init; } = 1.5;
    /// <summary>How far behind the pivot the gunner's seat is (T112).</summary>
    public double SeatBehind { get; init; } = 0.75;
    /// <summary>How fast the seated gunner can turn the carriage, and lift or drop the barrel (T112).</summary>
    public double TraverseDegreesPerSecond { get; init; } = 70;
    public double ElevateDegreesPerSecond { get; init; } = 35;
    /// <summary>A bot fires once the barrel's within this of its mark.</summary>
    public double LaidDegrees { get; init; } = 2.5;
}

/// <summary>The loudness meter and the Choir it draws (GDD v1.1 App. A.7, C.7). Field docs live in combat.json.</summary>
public sealed record ChoirTuning
{
    public double WindowSeconds { get; init; } = 4;
    public double VoicePerPlayer { get; init; } = 0.45;
    public double RoundLoudness { get; init; } = 1.2;
    public double WhistleLoudness { get; init; } = 0.9;
    public double MachineryLoudness { get; init; } = 0.35;
    public double Threshold { get; init; } = 0.6;
    public double BuildSeconds { get; init; } = 40;
    public double QuietDecayPerSecond { get; init; } = 0.06;
    public double MaxLoudness { get; init; } = 3;
    public double LivestockFloor { get; init; } = 0.3;
    /// <summary>T113: seconds after it disperses (driven off quiet) before it can begin to gather again.</summary>
    public double RestSeconds { get; init; } = 300;
}
