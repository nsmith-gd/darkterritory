namespace DarkTerritory.Sim.Player;

/// <summary>Mirror of content/tuning/player.json.</summary>
public sealed record PlayerTuning(
    double Run, double Walk, double RoofRun, double RoofWalkSafe, double LadderClimb,
    double CarryHeavy, double JumpGap, int Health, ColdTuning Cold,
    double Gravity, double Radius, double Height, double StepUp,
    LandingTuning Landing, LadderTuning Ladder, HandTuning Hand, double JumpHeight = 0)
{
    public const string File = "tuning/player.json";

    /// <summary>
    /// Take-off speed that lifts the feet <see cref="JumpHeight"/>; without one, the old derivation (a flat jump at roof-run
    /// speed spans exactly <see cref="JumpGap"/>), which is also the least it may be.
    /// </summary>
    public double JumpVelocity => Math.Max(Gravity * JumpGap / (2 * RoofRun), Math.Sqrt(2 * Gravity * JumpHeight));
}

/// <param name="IndoorsRate">How fast the cold comes on inside a car's walls with a door open, against outside (spec B.2).</param>
public sealed record ColdTuning(double OnsetSeconds, double DeathSeconds, double RecoverSecondsNearHeat, double OnsetSpeedScale, double RevivedOnsetScale,
    double IndoorsRate = 1);
/// <param name="LethalAbove">Speed over the ground on landing that kills (spec B.3 after T90); 0 for the train's jump-off band.</param>
/// <param name="DamageAtLethal">A landing's knock rises from <paramref name="RollDamage"/> to this at the lethal edge.</param>
public sealed record LandingTuning(double RollAbove, int RollDamage, double LethalAbove = 0, int DamageAtLethal = 0);
public sealed record LadderTuning(double GrabRange, double GrabMaxRelativeSpeed);
/// <summary>A VR player's reaching hand (T29). Field docs live in player.json.</summary>
public sealed record HandTuning(double Arm, double Overhead, double Grab);
