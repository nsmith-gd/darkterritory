using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Net;

/// <summary>
/// Turns crew intent into train controls. Only someone standing on the engine can drive
/// (GDD §12: roles emerge from position), which is also what the Deadman will exploit.
/// </summary>
public static class CabControls
{
    public static bool CanDrive(in PlayerState s) => s.Alive && s.Parent == 0 && s.Surface != Surface.Ladder;

    /// <summary>Applies one player's cab input. The host clears the brake each tick before applying everyone.</summary>
    public static void Apply(ref TrainControls controls, in PlayerIntent intent, in PlayerState state, double trainSpeed)
    {
        if (!CanDrive(state))
            return;
        if (intent.ThrottleNotch != 0)
            controls.Throttle = Math.Clamp(Math.Round(controls.Throttle * 4 + intent.ThrottleNotch) / 4, 0, 1);
        if (intent.Has(PlayerButtons.Brake))
            controls.Brake = 1;
        if (intent.Has(PlayerButtons.Reverser) && trainSpeed < 0.05)
            controls.Reverser = controls.Reverser >= 0 ? -1 : 1;
    }
}
