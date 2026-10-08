using Ballast;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Game.Sound;

/// <summary>
/// The grain elevator's conveyor line heard (queue #202, ARCHITECTURE §8 note 466; A1's note 400: "its sound is AU1's: the
/// belt, the drive and the jam"), off its site's replicated record, alike on every machine. The drive house is an engine
/// house (C1's model: an exhaust stack, a flywheel), so the drive is an oil engine started off the yard's power: cranked
/// while someone holds the starter, catching as the line starts, running while it does, labouring once it's jammed (the
/// belt held at the jam and slipping on the drum), dying if the jam's left. Along the belt, nearest the ear, its idlers
/// while it runs; at the head, the grain going into the car while it carries; at a jam, the belt bunching, the hands
/// clearing it, and the belt jerking free.
/// </summary>
public sealed partial class GameAudio
{
    // How far the drive is listened for (the catch's and the labour's maxDistance, install.py CUE_DEF), how long the catch
    // takes to come up to the engine's beat before the running loop takes over, and how long that loop takes to come in.
    const double ConveyorReach = 250, ConveyorCatch = 3.2, ConveyorFadeIn = 0.5;

    readonly Dictionary<int, double> _beltStarted = [];
    readonly Dictionary<int, Double3> _jamWas = [];

    /// <summary>A conveyor line's sounds; its edges are read wherever the ear is, so one that changed out of earshot plays nothing late.</summary>
    void ConveyorSounds(Site site, ConveyorTuning? tuning, Double3 ear, float outside, bool primed)
    {
        int key = site.Index;
        bool jammed = site.Jam >= 0;
        int ran = Flipped("place-conveyor.running", key, site.Running, primed);
        int jam = Flipped("place-conveyor.jammed", key, jammed, primed);
        bool near = (site.ConveyorStarter - ear).Length < ConveyorReach;
        // The drive house: the engine at its middle, behind the starter (C1's drive_house), its stack up through the roof.
        var drive = site.ConveyorStarter + Double3.Up * 1.2;
        if (ran > 0)
        {
            _beltStarted[key] = _time;
            if (near)
                Cue("place-conveyor.catch", drive, outside);
        }
        // Stalled: a jam left too long dragged the engine down (the sim stops it with the jam still in).
        if (ran < 0 && jammed && near)
            Cue("place-conveyor.stall", drive, outside);
        if (jam > 0)
        {
            _jamWas[key] = site.JamAt;
            if (near)
                Cue("place-conveyor.jam", site.JamAt, outside);
        }
        // Cleared with the drive still turning: the belt jerks straight and runs on. Cleared while it's stalled, it just lies.
        if (jam < 0 && site.Running && near)
            Cue("place-conveyor.free", _jamWas.GetValueOrDefault(key, site.JamAt), outside);
        if (!near)
            return;
        // Someone at the starter, cranking it over (Start counts the hold; letting go starts it over).
        if (!site.Running && site.Start > 0)
            HoldLevel("place-conveyor.cranking", key, drive, outside, 1);
        if (site.Running)
        {
            double since = _time - _beltStarted.GetValueOrDefault(key, double.NegativeInfinity);
            double up = Math.Clamp((since - ConveyorCatch) / ConveyorFadeIn, 0, 1);
            if (jammed)
            {
                // Labouring harder the longer the jam's left, up to the stall (D.2 "unattended jam stops the line").
                double left = tuning is { StallSeconds: > 0 } t ? Math.Clamp(site.JamFor / t.StallSeconds, 0, 1) : 0;
                HoldLevel("place-conveyor.labour", key, drive, outside, Math.Max(up, 0.3) * (0.7 + 0.3 * left));
            }
            else
            {
                HoldLevel("place-conveyor.engine", key, drive, outside, up);
                HoldLevel("place-conveyor.belt", key, NearestOnBelt(site, ear), outside, site.Carrying ? 1 : 0.7);
            }
        }
        if (site.Carrying)
            HoldLevel("place-conveyor.pour", key, site.ConveyorHead - Double3.Up * 0.8, outside, 1);
        if (jammed && site.Clear > 0)
            HoldLevel("place-conveyor.clearing", key, site.JamAt, outside, 1);
    }

    /// <summary>The point on the belt nearest the ear: along its low run (the tail to the knee) or up its riser to the head.</summary>
    static Double3 NearestOnBelt(Site site, Double3 ear)
    {
        static Double3 OnSegment(Double3 a, Double3 b, Double3 p)
        {
            var d = b - a;
            double u = Math.Clamp(Double3.Dot(p - a, d) / Math.Max(1e-9, Double3.Dot(d, d)), 0, 1);
            return a + d * u;
        }
        var low = OnSegment(site.ConveyorTail, site.ConveyorKnee, ear);
        var riser = OnSegment(site.ConveyorKnee, site.ConveyorHead, ear);
        return (low - ear).Length <= (riser - ear).Length ? low : riser;
    }
}
