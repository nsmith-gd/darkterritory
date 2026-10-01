using Ballast;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Game;

/// <summary>
/// T109 playtest ("feedback ... in multiple ways that pressure is too high"): on the engine, the boiler straining in the red
/// shakes you, harder towards the rupture. Presentation only: it moves the eye, never the player.
/// </summary>
public static class BoilerShake
{
    /// <summary>How hard it strains: 0 under the redline, 1 at the point of rupture.</summary>
    public static double Strain(World world)
    {
        var b = world.Train.Boiler;
        if (world.Train.BoilerTuning is not { } bt || b.Ruptured || b.Pressure < bt.Redline)
            return 0;
        return 0.6 * Math.Clamp((b.Pressure - bt.Redline) / Math.Max(1, bt.PressureMax - bt.Redline), 0, 1)
            + 0.4 * Math.Clamp(b.AtMaxSeconds / bt.RuptureHoldSeconds, 0, 1);
    }

    /// <summary>The eye's offset this frame (m), for someone on the engine; zero elsewhere.</summary>
    public static Double3 Offset(World world, in PlayerState viewer, double seconds)
    {
        double strain = viewer.Parent == 0 ? Strain(world) : 0;
        if (strain <= 0)
            return default;
        double a = 0.004 + 0.02 * strain * strain;
        return new Double3(a * Math.Sin(seconds * 53.1), a * Math.Sin(seconds * 71.7 + 1.3), a * 0.5 * Math.Sin(seconds * 37.9 + 2.1));
    }
}
