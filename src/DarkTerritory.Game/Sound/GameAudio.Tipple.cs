using Ballast;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Sound;

/// <summary>
/// The tipple heard (queue #216, ARCHITECTURE §8 note 480; A1's note 423: "its sound (the clamp, the roll, the derail)
/// AU1's"), off the run record's site and the cars' <see cref="Vehicle.OffRails"/>, alike on every machine: the clamp
/// wound down while the lever's held and biting as it shuts, the cradle rolling the car over (and back by itself), the ore
/// down the bin's chute at the top, the clamp letting go; a bad clamp's derailment, the wrench putting the car back, and
/// its wheels dropping onto the rails.
/// </summary>
public sealed partial class GameAudio
{
    // How far the cradle is listened for (the derail's and the roll's maxDistance, install.py CUE_DEF), and how long after
    // the cradle or the re-railing last moved they're still heard going (the record comes in snapshots, not every frame).
    const double TippleReach = 250, TippleStill = 0.25;
    // Where the clamp beam bears on the roof and where the cradle's hoops turn, up from the cradle's mark (TippleTilt's axis).
    const double TippleBeam = 3.8, TippleAxis = 2.1;

    readonly Dictionary<int, double> _tippleRolled = [], _tippleRerailed = [];
    readonly Dictionary<int, Double3> _tippleOffAt = [];
    /// <summary>The car each tipple last clamped: the one a bad clamp throws off its rails, and that the wrench puts back.</summary>
    readonly Dictionary<int, int> _tippleCar = [];

    void TippleSounds(Site site, TrainOnLine train, Double3 ear, float outside, bool primed)
    {
        int key = site.Index;
        if (site.Clamped >= 0)
            _tippleCar[key] = site.Clamped;
        int id = _tippleCar.GetValueOrDefault(key, -1);
        var off = id >= 0 && id < train.Vehicles.Count && id < train.Frames.Count && train.Vehicles[id].OffRails ? train.Vehicles[id] : null;
        int derailed = Flipped("place-tipple.off", key, off is not null, primed);
        int clamped = Flipped("place-tipple.clamped", key, site.Clamped >= 0, primed);
        bool top = Flipped("place-tipple.top", key, site.Clamped >= 0 && site.Roll >= 1 - 1e-6, primed) > 0;
        double rolled = Moved("place-tipple.roll", key, site.Roll), rerailed = Moved("place-tipple.rerail", key, site.Rerail);
        if (Math.Abs(rolled) > 1e-9 && site.Clamped >= 0)
            _tippleRolled[key] = _time;
        if (rerailed > 1e-9)
            _tippleRerailed[key] = _time;
        if (off is not null)
            _tippleOffAt[key] = train.Frames[off.Id].Origin + Double3.Up;
        bool near = (site.Cradle - ear).Length < TippleReach;
        var beam = site.Cradle + Double3.Up * TippleBeam;
        var axis = site.Cradle + Double3.Up * TippleAxis;
        if (near)
        {
            // A bad clamp (or the train moving with a car clamped): off its rails, and the clamp let go with it (Run.Derail),
            // which isn't a release.
            if (derailed > 0)
                Cue("place-tipple.derail", _tippleOffAt[key], outside);
            else if (clamped < 0)
                Cue("place-tipple.release", beam, outside);
            if (derailed < 0)
                Cue("place-tipple.rerailed", _tippleOffAt.GetValueOrDefault(key, axis), outside);
            if (clamped > 0)
                Cue("place-tipple.clamp", beam, outside);
            if (top)
                Cue("place-tipple.pour", site.TippleBin, outside);
        }
        if (!near)
            return;
        // The lever held: the clamp wound down (Clamp counts the hold; letting go starts it over).
        if (site.Clamped < 0 && site.Clamp > 0)
            HoldLevel("place-tipple.clamping", key, beam, outside, 1);
        // The cradle going over while the lever's held, or back by itself (a little lighter, nothing driving it).
        if (site.Clamped >= 0 && _time - _tippleRolled.GetValueOrDefault(key, double.NegativeInfinity) < TippleStill)
            HoldLevel("place-tipple.roll", key, axis, outside, site.RollingBack ? 0.75 : 1);
        if (off is not null && _time - _tippleRerailed.GetValueOrDefault(key, double.NegativeInfinity) < TippleStill)
            HoldLevel("place-tipple.rerail", key, _tippleOffAt[key] - Double3.Up * 0.6, outside, 1);
    }
}
