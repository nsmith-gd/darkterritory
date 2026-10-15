using Ballast;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game;

/// <summary>
/// What's come apart as drawn (note 779, the art checklist's <c>failing-stages</c>: "Come apart: the car off its rails, slewed
/// and tilted where it stopped. For the engine: ... broken down (cold, slumped)"). A car worn through on the line (note 576's
/// <see cref="Failing.Apart"/>: off its rails, its train parted ahead of it) lies where it stopped, dropped onto the
/// sleepers, over on one side, slewed round and down at one end, which way the car chooses. The engine broken down sits
/// slumped on its broken springs, its back end down and listing a little; its nose, where the driver stands at the cab's
/// front windows, stays where it was. Presentation only, as <see cref="TippleTilt"/> (whose
/// own derailed car, at its cradle, this leaves to it): the sim's frames never tilt, so nothing is sent or predicted, and
/// every machine draws it alike from the replicated <see cref="Vehicle.OffRails"/> and integrity.
/// </summary>
public static class ComeApart
{
    // A car come apart: how far over it lies, how far round it's slewed, how far down one end is (degrees), and how far it's
    // dropped (m). Further gone than the tipple's (TippleTilt.Off): it left the rails running.
    const double Lean = 9, Slew = 6, Pitch = 1.8, Drop = 0.28;
    // The engine broken down: how far its back end's down from its nose, and its list (degrees).
    const double SlumpPitch = 0.7, SlumpLean = 1.8;

    /// <summary>Lays the come-apart cars over and slumps a broken-down engine in <paramref name="frames"/> (by vehicle id), in place.</summary>
    public static void Apply(List<CarFrame> frames, TrainOnLine train, Run? run)
    {
        for (int i = 1; i < frames.Count && i < train.Vehicles.Count; i++)
        {
            var v = train.Vehicles[i];
            if (!v.OffRails || AtTipple(train, run, v))
                continue;
            int side = Side(v.Id), end = Side(v.Id * 3 + 1);
            frames[i] = Pitched(TippleTilt.Off(frames[i], side, Lean, Slew, Drop), end * Pitch);
        }
        if (frames.Count > 0 && train.Vehicles.Count > 0 && Failing.Stage(train, 0) == FailStage.Broken)
            frames[0] = Pitched(CarLean.Lean(frames[0], Side(train.Vehicles[0].Id) * SlumpLean * Math.PI / 180), -SlumpPitch,
                frames[0].Shape.Cab is { } cab ? cab.Min.Z : -frames[0].Shape.HalfLength);
    }

    /// <summary>The car the tipple's bad clamp put off its rails at a mine head (TippleTilt draws that one).</summary>
    static bool AtTipple(TrainOnLine train, Run? run, Vehicle v) =>
        run is not null && run.Sites.Any(site => site is not null && site.Has(ModuleKind.Tipple) && run.OffRailsAt(train, site) == v);

    /// <summary>+1 or −1, the same for a car every time.</summary>
    static int Side(int id) => ((uint)id * 2654435761u >> 16 & 1) == 0 ? 1 : -1;

    /// <summary>
    /// <paramref name="f"/> pitched by <paramref name="degrees"/> about the point <paramref name="about"/> along it (m, its
    /// middle by default) at rail height: its front (its −Back end) down, for positive. That point stays where it was.
    /// </summary>
    public static CarFrame Pitched(in CarFrame f, double degrees, double about = 0)
    {
        double a = degrees * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
        var back = f.Back * c + f.Up * s;
        return f with { Up = f.Up * c - f.Back * s, Back = back, Origin = f.Origin + (f.Back - back) * about };
    }
}
