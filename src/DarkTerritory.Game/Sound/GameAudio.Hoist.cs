using Ballast;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Game.Sound;

/// <summary>
/// A friend on the crane's hook heard (queue #525, note 798; K1's #1046, note 726): the cable creaking under whoever rides it,
/// louder while the crane moves them; a yelp from one who comes off it up in the air; and, the drop being "a silly gag, not a
/// big grief" (the director, 15 Oct, note 802), their landing a comic thump and an oof. Off what every machine has: the
/// crane's riders (its wire record) and the rider's <see cref="PlayerFlags.Hoisted"/> coming off. The operator's shake-off is
/// the host's alone, so a high let-go and a shake are heard alike: off the hook with a drop under them.
/// </summary>
public sealed partial class GameAudio
{
    // How far above what's under the hook (a car's roof, or the ground) a rider must be for coming off it to be a drop worth a
    // cry: above a jump's apex and the hook's grab height (facilities.json hoist.height up to 1.9 m), so taking hold and
    // letting go again at the ground is quiet.
    const double YelpAbove = 2.5;
    // How far they come down off the hook for the landing to be the gag's (note 802): more than a jump's apex, so stepping off
    // a hook let down near the ground is a step.
    const double GagAbove = 1.5;

    /// <summary>The hook's cable while anyone rides it: louder while the crane moves them, and with two on it.</summary>
    void HoistCreak(Crane crane, int key, float outside, bool moving)
    {
        if (crane.Riders > 0)
            HoldLevel("crew-hoist.creak", key, crane.HookAt, outside, (moving ? 1 : 0.55) * (crane.Riders > 1 ? 1 : 0.85));
    }

    /// <summary>
    /// Off the hook alive, high up (shaken off, or let go): the cry as the drop takes them, and the gag's landing to come
    /// (<see cref="HoistLanding"/>). Dead, grabbed or back on a hook, there's no landing to make a gag of.
    /// </summary>
    void CrewHoist(World world, in PlayerState s, CrewMember c, Double3 feet, float occlusion)
    {
        if (!s.Alive || s.Flags.HasFlag(PlayerFlags.Held) || s.Flags.HasFlag(PlayerFlags.Hoisted))
            c.OffHook = false;
        if (!c.Flags.HasFlag(PlayerFlags.Hoisted) || s.Flags.HasFlag(PlayerFlags.Hoisted) || !s.Alive || s.Flags.HasFlag(PlayerFlags.Held))
            return;
        double drop = feet.Y - Below(world, feet);
        if (drop >= YelpAbove)
            Cue("crew-hoist.yelp", feet + Double3.Up * HeadUp, occlusion);
        c.OffHook = drop >= GagAbove;
    }

    /// <summary>
    /// Down after a drop off the hook (note 802): a padded thump and a comic oof where they land, in place of the landing's
    /// own sound (and the hurt's: the gag's knock is the landing's, at most 10). True when it was that landing.
    /// </summary>
    bool HoistLanding(CrewMember c, Double3 feet, float occlusion)
    {
        if (!c.OffHook)
            return false;
        c.OffHook = false;
        Cue("crew-hoist.land", feet + Double3.Up * 0.4, occlusion);
        return true;
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
