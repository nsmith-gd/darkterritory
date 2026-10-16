using Ballast;
using Ballast.Audio;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Game.Sound;

/// <summary>
/// The human cannonball heard (queue #556, note 828; U1's #1106, note 746: "going for fun in this one"): a crewmate climbing
/// into the guard van's cannon (or back out of it), the whoop as the shot throws them, the air tearing past them as they fly
/// (carried with them), and the gag's landing, a thump, a roll and an oof, in place of the landing's own sound (it never
/// hurts). Off the crewmate's own <see cref="PlayerFlags.InCannon"/> and <see cref="PlayerFlags.Fired"/>, which every machine
/// has; the gun's boom is the gun's own (CrewGuns).
/// </summary>
public sealed partial class GameAudio
{
    /// <summary>The flight's hold owner, clear of the cars', the creatures' and the handcar's (a crewmate's id after it).</summary>
    const int CannonballOwner = 132_000;

    void CrewCannonball(int id, in PlayerState s, CrewMember c, Double3 feet, float occlusion)
    {
        bool inNow = s.Has(PlayerFlags.InCannon), inWas = c.Flags.HasFlag(PlayerFlags.InCannon);
        bool fired = s.Has(PlayerFlags.Fired);
        // Into the barrel (Use held at the muzzle), or back out of it (Jump, before the shot): the bore's iron either way.
        if (s.Alive && inNow != inWas && !fired)
            Cue("crew-cannonball.climb", feet + Double3.Up * 0.5, occlusion, inNow ? 1 : 0.8f);
        if (!fired || !s.Alive)
        {
            c.Whoop = null;
            return;
        }
        var head = feet + Double3.Up * HeadUp;
        // The shot: the whoop as it throws them, carried with them while it lasts; the air past them all the way.
        if (!c.Flying)
            c.Whoop = Cue("crew-cannonball.whoop", head, occlusion);
        else if (c.Whoop is { Finished: false, Stopped: false } whoop)
            whoop.Position = head;
        c.Flying = true;
        Hold("crew-cannonball.flight", CannonballOwner + id, head, occlusion);
    }

    /// <summary>
    /// Down from the cannon (note 746: the landing never hurts): a thump, a roll and an oof where they land, in place of the
    /// landing's own sound. True when it was that landing.
    /// </summary>
    bool CannonballLanding(CrewMember c, Double3 feet, float occlusion)
    {
        if (!c.Flying)
            return false;
        c.Flying = false;
        c.Whoop = null;
        Cue("crew-cannonball.land", feet + Double3.Up * 0.4, occlusion);
        return true;
    }
}
