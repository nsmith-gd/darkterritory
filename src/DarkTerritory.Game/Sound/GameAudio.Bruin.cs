using Ballast;
using DarkTerritory.Sim.Enemies;

namespace DarkTerritory.Game.Sound;

/// <summary>
/// The Bruin heard (queue #712, note 1007; docs/design/creatures/bruin.md §4), read off its record (its <see cref="BruinMode"/>
/// in Height). The tell is its huff with the chest's whistle in it, now and then while it comes out of the trees and while it
/// feeds (slower then); feeding, its car rocking on its springs, held; rising over whoever's in front of it, a roar. Synth
/// stand-ins (content/audio/sounds/tell-bruin.*, cs-bruin.*) until the audio chat builds its line's takes.
/// </summary>
public sealed partial class GameAudio
{
    /// <summary>Its huff while it comes: about every this many seconds; feeding, half as often.</summary>
    const double HuffEvery = 2.2;

    void BruinSounds(Bruin b, Creature was, Double3 at, float occ)
    {
        var mode = b.Mode;
        bool began = mode != (BruinMode)(int)was.Height;
        if (b.Gone)
            return;
        if (mode is BruinMode.Feed or BruinMode.Rise or BruinMode.Maul or BruinMode.Stagger)
            Hold("cs-bruin.rock", b.Id, at, occ);
        if (mode == BruinMode.Rise && began)
            Cue("cs-bruin.roar", at + Double3.Up * 1.4, occ);
        if (mode is BruinMode.Come or BruinMode.Feed or BruinMode.Leave)
        {
            double every = mode == BruinMode.Feed ? HuffEvery * 2 : HuffEvery;
            if (was.Next <= 0)
                was.Next = _time + 0.2 + 0.6 * _creatureRng.Next();
            else if (_time >= was.Next)
            {
                Cue("tell-bruin.huff", at + Double3.Up * 1.1, occ);
                was.Next = _time + every * (0.7 + 0.6 * _creatureRng.Next());
            }
        }
    }
}
