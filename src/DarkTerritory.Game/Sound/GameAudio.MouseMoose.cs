using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Game.Sound;

/// <summary>
/// The Mouse Moose heard (queue #537, note 809; A1's #1042, note 790; docs/design/creatures/mouse-moose.md): each read off its
/// record (its <see cref="MouseMooseMode"/> in Height). A pest, heard as one: creeping in out of the dark, its claws ticking
/// over the cinders and a snuffle while it moves, and a too-big rat's squeak now and then (the tell); its teeth scraping on a
/// casting's iron as it bites; the casting ground over the gravel in short tugs as it drags; a squeal and a scrabble as it's
/// scared off; the earth kicked back as it goes down its hole. A casting dragged all the way there is heard tipping in
/// (PlaceSite, off the casting's own record). It never hurts anyone and nothing kills it, so it has no hurt or death.
/// </summary>
public sealed partial class GameAudio
{
    /// <summary>A Mouse Moose going faster than this (m/s, flat) is heard creeping: it steps, it doesn't glide.</summary>
    const double MouseMooseMoving = 0.3;
    /// <summary>Its squeak while it creeps in: about every this many seconds, never in step with another's.</summary>
    const double SqueakEvery = 5;

    void MouseMooseSounds(MouseMoose mm, Creature was, Double3 at, float occ, double dt)
    {
        var mode = mm.Mode;
        var wasMode = (MouseMooseMode)(int)was.Height;
        bool began = mode != wasMode;
        double speed = ((at - was.At) with { Y = 0 }).Length / dt;
        switch (mode)
        {
            case MouseMooseMode.Creep:
                // Back out of its hole for a casting: a squeak to say so, then its claws while it comes.
                if (began)
                    was.Next = _time + 0.2 + 0.6 * _creatureRng.Next();
                if (speed >= MouseMooseMoving)
                    Hold("tell-mouse-moose.creep", mm.Id, at, occ);
                Squeak(was, at, occ);
                break;
            case MouseMooseMode.Bite when began:
                Cue("cs-mouse-moose.bite", at + Double3.Up * 0.4, occ);
                break;
            case MouseMooseMode.Drag:
                // Slow while the hook could still reach the casting, three times that once it can't (MouseMoose.Tick): louder
                // as it gets away with it. Between tugs, the strain goes on.
                double away = Math.Clamp(speed / 0.5, 0, 1);
                HoldLevel("cs-mouse-moose.drag", mm.Id, at + Double3.Up * 0.3, occ, 0.7 + 0.3 * away);
                break;
            case MouseMooseMode.Flee when began:
                Cue("cs-mouse-moose.bolt", at + Double3.Up * 0.3, occ);
                break;
            case MouseMooseMode.Hide when began && wasMode == MouseMooseMode.Flee:
                // Down its hole, scared off. Down it with a casting in its teeth, the casting's tipping in is the sound
                // (PlaceSite's LostDown).
                Cue("cs-mouse-moose.dig", at, occ);
                break;
        }
    }

    /// <summary>Its squeak while it creeps in, now and then.</summary>
    void Squeak(Creature was, Double3 at, float occ)
    {
        if (was.Next <= 0)
            was.Next = _time + SqueakEvery * (0.3 + 0.7 * _creatureRng.Next());
        else if (_time >= was.Next)
        {
            Cue("tell-mouse-moose.squeak", at + Double3.Up * 0.4, occ);
            was.Next = _time + SqueakEvery * (0.6 + 0.8 * _creatureRng.Next());
        }
    }

    /// <summary>
    /// A casting the Mouse Moose dragged to its hole (MouseMoose.Tick: off the ground, not off a hook, to Lost): heard tipping
    /// in where it lies, the casting's own record, so it's heard where its creature isn't (a client's Mouse Moose may be gone).
    /// </summary>
    void LostDown(CastingState was, CastingState now, Double3 at, float outside)
    {
        if (now == CastingState.Lost && was is CastingState.Stacked or CastingState.Taken)
            Cue("cs-mouse-moose.lost", at, outside);
    }
}
