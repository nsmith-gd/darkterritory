using Ballast;
using DarkTerritory.Sim.Enemies;

namespace DarkTerritory.Game.Sound;

/// <summary>
/// The Pickers heard (queue #499, note 769; G1's #315, note 592; pickers.md §4): each read off its record (its
/// <see cref="PickerMode"/> in Height). Coming up out of its drain, the grating shoved aside and a chitter (the tell); its
/// small feet scuttling while it's about, and a chitter now and then; a crate knocked and scraped along with a chattering pant
/// as it carries; a snap and a hiss as it bites; a burst of squeals as they bolt; the grating rattling as one goes down; a
/// squeal cut short when a blow kills one.
/// </summary>
public sealed partial class GameAudio
{
    /// <summary>A Picker going faster than this (m/s, flat) is heard scuttling.</summary>
    const double PickerMoving = 0.4;
    /// <summary>Its chitter now and then while it's up: about every this many seconds, never in step with another's.</summary>
    const double ChitterEvery = 4;
    /// <summary>Struck, every one near scatters at once (Picker.Struck), and the train going sends them all down: one burst
    /// of squeals, one rattle of a grating, for each such moment, not one a Picker.</summary>
    const double PickersOnce = 0.4;
    double _pickersScattered = double.NegativeInfinity, _pickersDown = double.NegativeInfinity;

    void PickerSounds(Picker p, Creature was, Double3 at, float occ, double dt)
    {
        var mode = p.Mode;
        bool began = mode != (PickerMode)(int)was.Height;
        switch (mode)
        {
            case PickerMode.Emerge when began:
                Cue("tell-pickers.emerge", at, occ);
                break;
            case PickerMode.Bite when began:
                Cue("cs-pickers.bite", at + Double3.Up * 0.5, occ);
                break;
            case PickerMode.Scatter when began && _time - _pickersScattered >= PickersOnce:
                Cue("cs-pickers.scatter", at + Double3.Up * 0.3, occ);
                _pickersScattered = _time;
                break;
            case PickerMode.Down when began:
                PickerDown(at, occ);
                break;
        }
        if (mode is PickerMode.Down or PickerMode.Emerge)
        {
            was.Next = 0;
            return;
        }
        // About the yard: a crate carried (its knock and scrape fall away while a heavy one waits for its second, the pant
        // goes on), or its feet while it moves.
        bool moving = ((at - was.At) with { Y = 0 }).Length / dt >= PickerMoving;
        if (mode == PickerMode.Carry)
            HoldLevel("cs-pickers.carry", p.Id, at + Double3.Up * 0.6, occ, moving ? 1 : 0.5);
        else if (moving)
            Hold("tell-pickers.feet", p.Id, at, occ);
        if (was.Next <= 0)
            was.Next = _time + ChitterEvery * (0.3 + 0.7 * _creatureRng.Next());
        else if (_time >= was.Next)
        {
            Cue("tell-pickers.chitter", at + Double3.Up * 0.3, occ);
            was.Next = _time + ChitterEvery * (0.6 + 0.8 * _creatureRng.Next());
        }
    }

    /// <summary>One down its drain (with a crate, scattered, or gone for good as the train leaves): the grating's rattle.</summary>
    void PickerDown(Double3 at, float occ)
    {
        if (_time - _pickersDown < PickersOnce)
            return;
        Cue("cs-pickers.down", at, occ);
        _pickersDown = _time;
    }

    /// <summary>A Picker's first tick is its coming up (Picker.Up), and a first sight plays nothing else: heard here.</summary>
    void PickerFirstSeen(Enemy e, Double3 at, int space)
    {
        if (e is Picker { Mode: PickerMode.Emerge })
            Cue("tell-pickers.emerge", at, Occlusion(space));
    }

    /// <summary>Gone: killed by a blow (a squeal cut short), or down its drain for good as the train leaves.</summary>
    void PickerGone(Creature c, bool killed, float occ)
    {
        if (killed)
            Cue("cs-pickers.hit", c.At + Double3.Up * 0.3, occ);
        else if ((PickerMode)(int)c.Height != PickerMode.Down)
            PickerDown(c.At, occ);
    }
}
