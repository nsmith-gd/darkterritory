using Ballast;
using DarkTerritory.Sim.Enemies;

namespace DarkTerritory.Game.Sound;

/// <summary>
/// The Tapper heard (queue #714, note 1011; docs/design/creatures/tapper.md §4), read off its record (its <see cref="TapperMode"/>
/// in Height): the tap, two knocks on the gun's iron behind the gunner's head, each time it comes to tap (again after a look
/// has held it off). A synth stand-in (content/audio/sounds/tell-tapper.tap) until the audio chat builds its line's takes.
/// </summary>
public sealed partial class GameAudio
{
    void TapperSounds(Tapper t, Creature was, Double3 at, float occ)
    {
        if (!t.Gone && t.Mode == TapperMode.Tap && (TapperMode)(int)was.Height != TapperMode.Tap)
            Cue("tell-tapper.tap", at + Double3.Up * 0.9, occ);
    }
}
