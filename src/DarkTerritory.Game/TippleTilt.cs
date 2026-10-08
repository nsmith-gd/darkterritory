using Ballast;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game;

/// <summary>
/// The mine head's tipple as drawn (ARCHITECTURE §8 note 423): the car clamped in its cradle rolled over toward the ore bin
/// by as far as the cradle's turned (facilities.json tipple.rollDegrees at the top of the roll), about the cradle's axis
/// through the car's middle; and a car off its rails slewed, down on its bin side with its other wheels up off the rail.
/// Presentation only, as <see cref="CarLean"/>: the sim's frames never roll (whoever's on a car through the roll is still
/// stood where they were), so nothing is sent and nothing is predicted; every machine draws it alike from the site's state.
/// </summary>
public static class TippleTilt
{
    /// <summary>How high the cradle's axis is over the rail (m): a cargo car's body's middle.</summary>
    public const double AxisHeight = 2.1;
    // A car off its rails: how far over it lies (degrees), how far round it's slewed (degrees), how far down it's dropped (m).
    const double OffLean = 7, OffSlew = 3, OffDrop = 0.15;

    /// <summary>Rolls the clamped cars and slews the derailed ones in <paramref name="frames"/> (the train's, by vehicle id) in place.</summary>
    public static void Apply(List<CarFrame> frames, TrainOnLine train, Run? run)
    {
        if (run?.FacilityTuning?.Tipple is not { } tp)
            return;
        foreach (var site in run.Sites)
        {
            if (site is null || !site.Has(ModuleKind.Tipple))
                continue;
            if (site.Clamped >= 0 && site.Clamped < frames.Count && site.Roll > 0)
                frames[site.Clamped] = Roll(frames[site.Clamped], Angle(site, frames[site.Clamped], tp));
            for (int i = 0; i < frames.Count && i < train.Vehicles.Count; i++)
                if (train.Vehicles[i].OffRails && run.OffRailsAt(train, site) == train.Vehicles[i])
                    frames[i] = Off(frames[i], ToBin(site, frames[i]));
        }
    }

    /// <summary>How far the cradle's rolled a car over (radians, signed as <see cref="CarLean.Lean"/>: toward the bin).</summary>
    public static double Angle(Site site, in CarFrame f, TippleTuning tp) => ToBin(site, f) * site.Roll * tp.RollDegrees * Math.PI / 180;

    /// <summary>+1 if the bin's on the car's right, −1 on its left.</summary>
    static int ToBin(Site site, in CarFrame f) => Double3.Dot(site.TippleBin - site.Cradle, f.Right) >= 0 ? 1 : -1;

    /// <summary><paramref name="f"/> rolled by <paramref name="radians"/> about its own long axis <see cref="AxisHeight"/> up (positive: top to its right).</summary>
    public static CarFrame Roll(in CarFrame f, double radians)
    {
        double c = Math.Cos(radians), s = Math.Sin(radians);
        var right = f.Right * c - f.Up * s;
        var up = f.Up * c + f.Right * s;
        var pivot = f.Origin + f.Up * AxisHeight;
        return f with { Origin = pivot - up * AxisHeight, Right = right, Up = up };
    }

    /// <summary>A car off its rails: over toward <paramref name="side"/> about that rail, slewed round and dropped onto the sleepers.</summary>
    static CarFrame Off(in CarFrame f, int side)
    {
        var lean = CarLean.Lean(f, side * OffLean * Math.PI / 180);
        double c = Math.Cos(OffSlew * Math.PI / 180), s = Math.Sin(OffSlew * Math.PI / 180);
        var right = lean.Right * c + lean.Back * s;
        var back = lean.Back * c - lean.Right * s;
        return lean with { Origin = lean.Origin - Double3.Up * OffDrop, Right = right, Back = back };
    }
}
