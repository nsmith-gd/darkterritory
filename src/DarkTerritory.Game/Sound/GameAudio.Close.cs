using Ballast;
using DarkTerritory.Sim.Enemies;

namespace DarkTerritory.Game.Sound;

/// <summary>
/// The Close heard (queue #716, note 1013; docs/design/creatures/the-close.md §4), read off its record (its <see cref="CloseMode"/>
/// in Height): the bowing, the roof's boards creaking in and easing like a breath every 3 s while it presses on the roof; once it
/// pours in, only its weight. A synth stand-in (content/audio/sounds/tell-close.bow) until the audio chat builds its line's takes.
/// </summary>
public sealed partial class GameAudio
{
    /// <summary>The bow's breath: a creak every this many seconds (Sloe's design §4).</summary>
    const double CloseBreathEvery = 3;

    void CloseSounds(Close c, Creature was, Double3 at, float occ)
    {
        if (c.Gone || c.Mode != CloseMode.Bow)
            return;
        if (was.Next <= 0 || _time >= was.Next)
        {
            Cue("tell-close.bow", at, occ);
            was.Next = _time + CloseBreathEvery;
        }
    }
}
