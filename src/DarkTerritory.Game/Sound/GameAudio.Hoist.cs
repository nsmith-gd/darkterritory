using Ballast;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Game.Sound;

/// <summary>
/// A friend on the crane's hook heard (queue #525, note 798; K1's #1046, note 726): the cable creaking under whoever rides it,
/// and a yelp from one who comes off it up in the air. Off what every machine has: the crane's riders (its wire record) and
/// the rider's <see cref="PlayerFlags.Hoisted"/> coming off. The operator's shake-off is the host's alone, so a high let-go
/// and a shake are heard alike: off the hook with a drop under them.
/// </summary>
public sealed partial class GameAudio
{
    // How far above what's under the hook (a car's roof, or the ground) a rider must be for coming off it to be a drop worth a
    // cry: above a jump's apex and the hook's grab height (facilities.json hoist.height up to 1.9 m), so taking hold and
    // letting go again at the ground is quiet.
    const double YelpAbove = 2.5;

    /// <summary>The hook's cable while anyone rides it, louder with two on it.</summary>
    void HoistCreak(Crane crane, int key, float outside)
    {
        if (crane.Riders > 0)
            HoldLevel("crew-hoist.creak", key, crane.HookAt, outside, crane.Riders > 1 ? 1 : 0.75);
    }

    /// <summary>Off the hook alive, high up (shaken off, or let go): the cry as the drop takes them.</summary>
    void CrewHoist(World world, in PlayerState s, CrewMember c, Double3 feet, float occlusion)
    {
        if (!c.Flags.HasFlag(PlayerFlags.Hoisted) || s.Flags.HasFlag(PlayerFlags.Hoisted) || !s.Alive || s.Flags.HasFlag(PlayerFlags.Held))
            return;
        if (feet.Y - Below(world, feet) >= YelpAbove)
            Cue("crew-hoist.yelp", feet + Double3.Up * HeadUp, occlusion);
    }

    /// <summary>What's under a rider coming off: under the nearest crane's hook (a car's roof, or the ground), else the ground.</summary>
    static double Below(World world, Double3 feet)
    {
        Crane? nearest = null;
        double best = 6;
        foreach (var site in world.Run?.Sites ?? [])
            foreach (var crane in site?.Cranes ?? [])
                if ((crane.HookAt - feet).Length is var d && d < best)
                    (nearest, best) = (crane, d);
        double hint = 0;
        return nearest?.Under(world.Train).Y ?? PlayerMotor.GroundAt(feet, world.Train.Line, ref hint);
    }
}
