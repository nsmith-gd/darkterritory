using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game;

/// <summary>
/// The train's own creatures in the scene (the director's brief of 8 Oct 2026): the Brakeman (note 364), the Knotter (note
/// 365) and Hotbox (note 367). Each keeps its mode in its <see cref="Enemy.Height"/>; the scene keeps how long it's been at it
/// and what it did before, and tells the art what the scene knows: the gap the Knotter's across, the wheel the Brakeman's at,
/// the train's speed. And what they do to the train: a wound car's shoes biting, a seized car's wheel dragged in sparks.
/// </summary>
public sealed partial class GreyboxScene
{
    // What each of them was doing when last drawn (its mode's number), since when (scene time), and what it did before that.
    readonly Dictionary<int, (int Mode, double Since, int? Before)> _trainModes = new();

    /// <summary>What a staged one of them was doing before it's first seen (by id: its mode's number; dt screenshot's Brakeman
    /// gone over the side from his walk).</summary>
    public Dictionary<int, int>? StagedBefore { get; set; }

    /// <summary>
    /// How long one of the train's own has been at what it's doing (its mode, s) and what it did before (its mode's number). Its
    /// mode isn't timed on the wire, only its phase, so this watches it change; first seen, its phase's time stands in.
    /// </summary>
    (double Since, int? Before) TrainModeSince(Enemy e)
    {
        int mode = (int)e.Height;
        if (!_trainModes.TryGetValue(e.Id, out var was) || Time < was.Since)
            _trainModes[e.Id] = was = (mode, Time - e.PhaseSeconds, StagedBefore?.TryGetValue(e.Id, out var staged) == true ? staged : null);
        else if (was.Mode != mode)
            _trainModes[e.Id] = was = (mode, Time, was.Mode);
        return (Time - was.Since, was.Before);
    }

    /// <summary>
    /// Before one of the train's own is drawn: its mode's time (returned, for its clips) and what came before; a Knotter's gap,
    /// twice how far past its car's end the sim has it (it's at the gap's middle); a winding Brakeman's wheel, the nearest of
    /// his car's to him; the train's speed, for Hotbox's knock and smoke.
    /// </summary>
    double Trainfolk(Enemy e, IReadOnlyList<CarFrame> frames, Art.CreatureArt? art)
    {
        var (since, before) = TrainModeSince(e);
        if (art is null)
            return since;
        art.ModeWas = before;
        art.TrainSpeed = (float)_speed;
        if (e.Attached >= 0 && e.Attached < frames.Count)
        {
            var shape = frames[e.Attached].Shape;
            switch (e)
            {
                case Knotter:
                    art.KnotterSpan = (float)Math.Max(0.6, 2 * (e.Local.Z - shape.HalfLength));
                    break;
                case Brakeman { Mode: BrakemanMode.Wind }:
                    if (shape.Interactables.Where(i => i.Kind == InteractableKind.Handbrake).Select(i => (Interactable?)i)
                        .MinBy(i => Math.Abs(i!.Value.Position.Z - e.Local.Z)) is { } wheel)
                        art.BrakemanWheel = new Vector3((float)(wheel.Position.X - e.Local.X), 0, (float)(wheel.Position.Z - e.Local.Z));
                    break;
            }
        }
        return since;
    }

    /// <summary>
    /// What the train's own have done to this car, as it runs: its handbrake wound on (the Brakeman's), the shoes biting each
    /// wheel in a glow and a spray of sparks; its axle seized (Hotbox's), that truck's wheel on the near side dragged along
    /// the rail in a fan of them (Hotbox's own truck and side while it's there; once it's gone, the rear truck's).
    /// </summary>
    void TrainCar(MeshBuilder mesh, CarFrame frame, Vehicle? vehicle, Double3 eye)
    {
        if (Look?.Art.Effects is not { } fx || vehicle is null || Math.Abs(_speed) < 0.3 || (frame.Origin - eye).Length > 140)
            return;
        var right = ToF(frame.Right);
        var up = ToF(frame.Up);
        var back = ToF(frame.Back);
        var o = V(frame.Origin, eye);
        float l = (float)frame.Shape.HalfLength;
        Vector3 L(float x, float y, float z) => o + right * x + up * y + back * z;
        // The trucks' centres and their two axles either side (TrainKit's: bolsters 1.9 m in from each end).
        float[] trucks = [-l + 1.9f, l - 1.9f];
        if (vehicle.Wound)
            foreach (float z in trucks)
                foreach (float axle in (ReadOnlySpan<float>)[-0.85f, 0.85f])
                    foreach (int side in (ReadOnlySpan<int>)[-1, 1])
                        fx.BrakeShoe(mesh, L(side * Art.TrainKit.HalfGauge, 0.32f, z + axle - 0.42f), up, back, (float)_speed, Time,
                            frame.Index * 16 + (z > 0 ? 8 : 0) + (axle > 0 ? 4 : 0) + (side > 0 ? 2 : 0));
        if (vehicle.Seized)
        {
            var bug = Enemies?.OfType<Hotbox>().FirstOrDefault(h => h.Attached == frame.Index && !h.Gone);
            float z = bug is not null ? (float)bug.Local.Z : l - 1.9f;
            int[] sides = bug is not null ? [bug.Side] : [-1, 1];
            foreach (int side in sides)
                fx.Skid(mesh, L(side * Art.TrainKit.HalfGauge, 0.02f, z + 0.4f), up, back, (float)_speed, Time, frame.Index * 2 + (side > 0 ? 1 : 0));
        }
    }

    /// <summary>
    /// The train's own as boxes, until the art's there (--greybox): the Brakeman a stooped dark figure with a wheel on his back;
    /// the Knotter a pale bar across its gap; Hotbox a low dark dome by its truck, its belly lit.
    /// </summary>
    static void TrainfolkBoxes(Enemy e, Action<double, double, double, double, double, double, Vector3> draw)
    {
        switch (e)
        {
            case Brakeman { Mode: BrakemanMode.Hidden }:
                break;
            case Brakeman:
                draw(0, 0.5, 0, 0.18, 0.5, 0.14, Palette.SootBlack);
                draw(0, 1.15, -0.12, 0.2, 0.28, 0.16, Palette.RustRed * 0.7f);
                draw(0, 1.55, -0.2, 0.1, 0.12, 0.1, Palette.BlueGrey);
                draw(0.12, 1.3, 0.2, 0.25, 0.25, 0.02, Palette.RustRed);
                break;
            case Knotter:
                draw(0, 0, 0, 0.09, 0.09, Art.CreatureArt.KnotterEnd, Palette.BoardEnamel * 0.85f);
                break;
            case Hotbox:
                draw(0, -0.1, 0, 0.35, 0.18, 0.55, Palette.Charcoal);
                draw(0, -0.2, 0, 0.3, 0.06, 0.45, Palette.FurnaceOrange);
                break;
        }
    }
}
