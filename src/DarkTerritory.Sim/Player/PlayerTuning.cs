namespace DarkTerritory.Sim.Player;

/// <summary>Mirror of content/tuning/player.json.</summary>
public sealed record PlayerTuning(
    double Run, double Walk, double RoofRun, double RoofWalkSafe, double LadderClimb,
    double CarryHeavy, double JumpGap, int Health, ColdTuning Cold,
    double Gravity, double Radius, double Height, double StepUp,
    LandingTuning Landing, LadderTuning Ladder, HandTuning Hand)
{
    public const string File = "tuning/player.json";

    /// <summary>Take-off speed such that a flat jump at roof-run speed spans exactly <see cref="JumpGap"/>.</summary>
    public double JumpVelocity => Gravity * JumpGap / (2 * RoofRun);
}

public sealed record ColdTuning(double OnsetSeconds, double DeathSeconds, double RecoverSecondsNearHeat, double OnsetSpeedScale);
public sealed record LandingTuning(double RollAbove, int RollDamage);
public sealed record LadderTuning(double GrabRange, double GrabMaxRelativeSpeed);
/// <summary>A VR player's reaching hand (T29). Field docs live in player.json.</summary>
public sealed record HandTuning(double Arm, double Overhead, double Grab);
