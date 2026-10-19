using Ballast;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game;

/// <summary>
/// Note 865 (the director, 19 Oct: "when it does it should have hella telegraphing to the players"): a pin working loose is felt
/// by whoever stands on either car of its gap, on the roof, inside or in the gap. A shudder that grows through the warning, a
/// jolt as the pin jumps, then a light judder while it's out. Presentation only, as <see cref="HouseShake"/>: it moves the eye,
/// never the player, and the settings' CAMERA SHAKE scales it.
/// </summary>
public static class PinShake
{
    /// <summary>The shudder at the warning's start and end (m of the eye's travel), the jolt as it jumps, the judder after.</summary>
    const double WarnFrom = 0.004, WarnTo = 0.02, Jolt = 0.035, Judder = 0.005;
    /// <summary>How long the jump's jolt lasts (s).</summary>
    const double JoltSeconds = 0.6;

    /// <summary>The eye's offset this frame (m) for <paramref name="viewer"/>: the hardest pin they're riding beside.</summary>
    public static Double3 Offset(World world, in PlayerState viewer, double seconds)
    {
        var train = world.Train;
        if (train.Loose is not { } t || !viewer.Alive || viewer.Parent <= 0 || viewer.Parent >= train.Vehicles.Count)
            return default;
        double a = 0;
        // The gap behind the car they're on, and the gap ahead of it (the car ahead's).
        foreach (int car in (ReadOnlySpan<int>)[viewer.Parent, train.VehicleAhead(viewer.Parent)])
        {
            if (car <= 0 || car >= train.Vehicles.Count || train.Vehicles[car] is not { Loose: > 0 } v || !Couplings.TakesTwo(v.LooseCause))
                continue;
            a = Math.Max(a, v.LooseCause == LooseCause.Working ? WarnFrom + (WarnTo - WarnFrom) * Couplings.Warned(v, t)
                : v.Loose < JoltSeconds ? Judder + (Jolt - Judder) * (1 - v.Loose / JoltSeconds) : Judder);
        }
        if (a <= 0)
            return default;
        // A knocking judder: mostly up and down and fore and aft, as the slack works.
        return new Double3(a * 0.4 * Math.Sin(seconds * 41.3 + 0.7), a * (0.7 * Math.Sin(seconds * 33.1) + 0.3 * Math.Sin(seconds * 79.7 + 1.1)),
            a * 0.8 * Math.Sin(seconds * 27.9 + 2.1));
    }
}
