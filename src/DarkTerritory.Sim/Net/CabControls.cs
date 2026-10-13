using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Net;

/// <summary>
/// Turns crew intent into train controls. Only someone standing on the engine can drive
/// (GDD §12: roles emerge from position), which is also what the Deadman will exploit.
/// </summary>
public static class CabControls
{
    public static bool CanDrive(in PlayerState s, TrainOnLine train) => s.Alive && PlayerMotor.InCab(s, train);

    /// <summary>Below this a train on its brake is standing on it, and stays on it until let off (T97).</summary>
    public const double StandingBelow = 0.1;

    /// <summary>
    /// Whether this tick the held brake is cleared (to be re-applied by whoever's holding it): always while moving; at a
    /// stand only when the driver lets it off, since with steam driving (T97) a standing engine off its brake pulls away.
    /// Without steam driving (no boiler, the old regulator) it's cleared as it always was.
    /// </summary>
    public static bool Clears(in TrainControls controls, TrainOnLine train, bool released) =>
        train.BoilerTuning?.SteamDrive != true || train.Dynamics.Speed >= StandingBelow || released;

    /// <summary>A notch up from the cab is letting the brake off (T97: there's no regulator; that key releases the brake).</summary>
    public static bool ReleasesBrake(in PlayerIntent intent, in PlayerState state, TrainOnLine train) =>
        intent.ThrottleNotch > 0 && CanDrive(state, train);

    /// <summary>Whether this intent works the cab's controls: a notch of the regulator, the brake, the reverser (note 574).</summary>
    public static bool Works(in PlayerIntent intent) =>
        intent.ThrottleNotch != 0 || intent.Has(PlayerButtons.Brake) || intent.Has(PlayerButtons.Reverser);

    /// <summary>Applies one player's cab input. The host clears the brake each tick before applying everyone.</summary>
    public static void Apply(ref TrainControls controls, in PlayerIntent intent, in PlayerState state, TrainOnLine train)
    {
        if (!CanDrive(state, train))
            return;
        double trainSpeed = train.Dynamics.Speed;
        if (intent.ThrottleNotch != 0)
            controls.Throttle = Math.Clamp(Math.Round(controls.Throttle * 4 + intent.ThrottleNotch) / 4, 0, 1);
        if (intent.Has(PlayerButtons.Brake))
            controls.Brake = 1;
        if (intent.Has(PlayerButtons.Reverser) && trainSpeed < 0.05)
            controls.Reverser = controls.Reverser >= 0 ? -1 : 1;
    }
}
