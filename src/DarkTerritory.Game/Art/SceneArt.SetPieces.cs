using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Art;

public sealed partial class SceneArt
{
    /// <summary>
    /// GDD §18's set pieces (notes 185, 368) modelled (note 398), where the sim lays them and moving as it says: the grain
    /// elevator's loading bin astride the track (its grain left in the sight glass, its lever, the pour), the slaughterhouse's
    /// pen of panels and its ramp (the herd in it, the next one going up), the chemical works' hose stand (its gauge
    /// going red with the pressure, the hose sagging over to the car it's coupled to, the leak), and the mine head's ore
    /// bin and trough (the ore left, the skip up the headframe's guides as far as it's wound, its lever, the ore down
    /// the chute). Each is turned as GreyboxScene.SetPieces turns its boxes. False where a module's models aren't built
    /// (the greybox draws them all).
    /// </summary>
    public bool SetPieces(MeshBuilder mesh, Sim.Run.Site site, IReadOnlyList<CarFrame> frames, Double3 eye, double time)
    {
        var props = PropArt.Of(Look);
        var handle = props.Get("lever_handle");
        bool spout = site.Has(Sim.Run.ModuleKind.Spout), ramp = site.Has(Sim.Run.ModuleKind.Ramp);
        bool hose = site.Has(Sim.Run.ModuleKind.Hose), lift = site.Has(Sim.Run.ModuleKind.Lift);
        if (handle is null || spout && props.Get("spout_bin") is null || hose && props.Get("hose_stand") is null
            || lift && (props.Get("lift_works") is null || props.Get("ore_skip") is null)
            || ramp && (props.Get("cattle_pen") is null || props.Get("cattle_ramp") is null))
            return false;
        var grain = new Vector3(0.72f, 0.6f, 0.36f);
        if (spout)
        {
            var mouth = site.Spout;
            var (along, across) = SetPieceAxes(mouth, site.SpoutLever);
            var m = SetPieceFrame(mouth - Double3.Up * 5.2, along, across, eye);
            mesh.Instances.Add(new MeshInstance(props.Get("spout_bin")!, m));
            // The grain left, up the sight glass on the lever's side (the model's glass: 0.9 along, 2.42 across, 8.7-10.85 up).
            float level = (float)Math.Clamp(site.Bin / 3.0, 0, 1);
            var foot = ModelPoint(m, 0.9f, 2.445f, 8.7f);
            var up = Vector3.UnitY;
            if (level > 0.01f)
                mesh.Box(foot + up * (1.075f * level), Vector3.Normalize(ModelAxis(m, 1, 0, 0)), up, Vector3.Normalize(ModelAxis(m, 0, 1, 0)),
                    new Vector3(0.15f, 1.075f * level, 0.012f), grain);
            Lever(mesh, handle, site.SpoutLever, ToF((site.SpoutLever - mouth) with { Y = 0 }), site.Pouring, eye, across);
            if (site.Pouring)
                for (int i = 0; i < 18; i++)
                {
                    double fall = (time * 6 + i * 0.31) % 2.6;
                    var p = mouth - Double3.Up * fall + ToD(along) * (0.18 * Math.Sin(i * 2.3)) + ToD(across) * (0.18 * Math.Cos(i * 1.7));
                    mesh.Box(p.RelativeTo(eye), along, Vector3.UnitY, across, new Vector3(0.1f, 0.18f, 0.1f), grain * (i % 2 == 0 ? 1f : 0.8f));
                }
        }
        if (lift)
        {
            var mouth = site.LiftChute;
            var (along, across) = SetPieceAxes(mouth, site.Headframe);
            var m = SetPieceFrame(mouth - Double3.Up * 4.6, along, across, eye);
            mesh.Instances.Add(new MeshInstance(props.Get("lift_works")!, m));
            // The ore left down the shaft, in the gauge on the bin's track side (the model's: -2.22 across, 6.3-7.7 up).
            float left = (float)Math.Clamp(site.Ore / 3.0, 0, 1);
            if (left > 0.01f)
                mesh.Box(ModelPoint(m, 0, -2.245f, 6.3f) + Vector3.UnitY * (0.7f * left), Vector3.Normalize(ModelAxis(m, 1, 0, 0)), Vector3.UnitY,
                    Vector3.Normalize(ModelAxis(m, 0, 1, 0)), new Vector3(0.25f, 0.7f * left, 0.012f), Palette.Charcoal * 1.4f);
            // The skip up the guides on the headframe's face, as far as it's wound.
            var skip = ModelPoint(m, 0, 12.9f, 0.9f + 9.8f * (float)Math.Clamp(site.Wind, 0, 1));
            mesh.Instances.Add(new MeshInstance(props.Get("ore_skip")!, m with { M41 = skip.X, M42 = skip.Y, M43 = skip.Z }));
            Lever(mesh, handle, site.LiftLever, -ToF((site.Headframe - mouth) with { Y = 0 }), site.Winding, eye, across);
            if (site.Winding && site.Wind < 0.15)
                for (int i = 0; i < 16; i++)
                {
                    double fall = (time * 6 + i * 0.29) % 2.2;
                    var p = mouth - Double3.Up * fall + ToD(along) * (0.2 * Math.Sin(i * 2.3)) + ToD(across) * (0.2 * Math.Cos(i * 1.7));
                    mesh.Box(p.RelativeTo(eye), along, Vector3.UnitY, across, new Vector3(0.14f, 0.14f, 0.14f), i % 2 == 0 ? Palette.Charcoal : Palette.IronGrey * 0.7f);
                }
        }
        if (hose)
        {
            var stand = site.HoseStand;
            double hint = site.Track.Length;
            var track = site.Track.Sample(Math.Clamp(site.Track.Nearest(stand, ref hint).Distance, 0, site.Track.Length)).Position;
            var toward = ToF(((track - stand) with { Y = 0 }).Normalized);
            // The model's front (-Y) to the track.
            var m = SetPieceFrame(stand, Vector3.Cross(Vector3.UnitY, -toward), -toward, eye);
            mesh.Instances.Add(new MeshInstance(props.Get("hose_stand")!, m));
            // The gauge's face, green to red with the pressure, brighter while it's leaking.
            float pr = (float)Math.Clamp(site.Pressure, 0, 1);
            var face = Vector3.Lerp(Palette.SignalGreen, Palette.SignalRed, pr) * (site.Leaking ? 1.6f : 1f);
            float glow = mesh.Emissive;
            mesh.Emissive = 0.35f;
            mesh.Box(ModelPoint(m, 0, -0.205f, 1.75f), Vector3.Cross(Vector3.UnitY, toward), Vector3.UnitY, toward, new Vector3(0.1f, 0.1f, 0.006f), face);
            mesh.Emissive = glow;
            // The hose, off the coupling: over to the car's filler, sagging; or hung down off it.
            var outlet = stand + Double3.Up * 3.25 + ToD(toward) * 0.62;
            var filler = site.HoseCar >= 0 && site.HoseCar < frames.Count
                ? frames[site.HoseCar].ToWorld(new Double3(0, frames[site.HoseCar].Shape.RoofHeight + 0.2, 0))
                : outlet - Double3.Up * 3.0 + ToD(toward) * 0.4;
            var sag = (outlet + filler) * 0.5 - Double3.Up * (site.HoseCar >= 0 ? 1.0 : 0.2);
            var previous = outlet;
            for (int i = 1; i <= 10; i++)
            {
                double t = i / 10.0;
                var q = outlet * ((1 - t) * (1 - t)) + sag * (2 * (1 - t) * t) + filler * (t * t);
                // Canvas-wrapped and tarred, banded so it reads against a car's side in the dark.
                HoseRun(mesh, previous, q, 0.1f, i % 3 == 0 ? Palette.HazardYellow * 0.55f : Palette.SootBlack * 1.6f, eye);
                previous = q;
            }
            if (site.Leaking)
                for (int i = 0; i < 6; i++)
                {
                    double t = (time * 0.4 + i / 6.0) % 1;
                    var p = outlet + new Double3(Math.Sin(i * 1.9) * 2.5 * t, -1.5 + 2.5 * t, Math.Cos(i * 1.3) * 2.5 * t);
                    mesh.Billboard(p.RelativeTo(eye), 1.5f + 3f * (float)t, (float)(i + time * 0.3), new Vector4(0.55f, 0.65f, 0.25f, 0.5f * (1 - (float)t)), -1, FxBlend.Alpha);
                }
        }
        if (ramp)
            PenAndRamp(mesh, site, eye, time, props.Get("cattle_pen")!, props.Get("cattle_ramp")!);
        return true;
    }

    /// <summary>A set piece's two axes, level: across toward <paramref name="to"/>, along square to it (as GreyboxScene.SetPieces).</summary>
    static (Vector3 Along, Vector3 Across) SetPieceAxes(Double3 from, Double3 to)
    {
        var d = (to - from) with { Y = 0 };
        var across = ToF(d.Length > 1e-6 ? d.Normalized : new Double3(1, 0, 0));
        return (Vector3.Cross(Vector3.UnitY, across), across);
    }

    /// <summary>
    /// A set piece model's place (tools/models facility_pieces: +X along the track, +Y across it, +Z up in Blender, so the
    /// engine's +X, −Z and +Y): its model −Z is <paramref name="across"/>, its X square to it (right-handed), at
    /// <paramref name="ground"/>.
    /// </summary>
    static Matrix4x4 SetPieceFrame(Double3 ground, Vector3 along, Vector3 across, Double3 eye)
    {
        var z = -across;
        var x = Vector3.Cross(Vector3.UnitY, z);
        var at = ground.RelativeTo(eye);
        return new Matrix4x4(x.X, x.Y, x.Z, 0, 0, 1, 0, 0, z.X, z.Y, z.Z, 0, at.X, at.Y, at.Z, 1);
    }

    /// <summary>A point given in a set piece's Blender coordinates (along, across, up), where it is in the scene.</summary>
    static Vector3 ModelPoint(in Matrix4x4 m, float along, float across, float up) => Vector3.Transform(new Vector3(along, up, -across), m);

    /// <summary>A direction in a set piece's Blender coordinates, in the scene.</summary>
    static Vector3 ModelAxis(in Matrix4x4 m, float along, float across, float up) => Vector3.TransformNormal(new Vector3(along, up, -across), m);

    /// <summary>
    /// A set piece's hand lever at <paramref name="at"/> (its pivot, 0.9 m up): an iron post down to the ground and the
    /// handle (lever_handle) out over <paramref name="outward"/>'s side, up and ready, or pulled down while it's held.
    /// </summary>
    void Lever(MeshBuilder mesh, MeshAsset handle, Double3 at, Vector3 outward, bool held, Double3 eye, Vector3 across)
    {
        var post = (at - Double3.Up * 0.45).RelativeTo(eye);
        mesh.Box(post, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, new Vector3(0.06f, 0.45f, 0.06f), Palette.IronGrey);
        mesh.Box(at.RelativeTo(eye), Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, new Vector3(0.1f, 0.08f, 0.1f), Palette.IronGrey * 0.8f);
        var side = outward.LengthSquared() > 1e-6f ? Vector3.Normalize(outward) : across;
        var dir = Vector3.Normalize(Vector3.UnitY * (held ? -0.15f : 0.35f) + side * 0.45f);
        var y = Vector3.Normalize(Vector3.Cross(Vector3.Cross(dir, Vector3.UnitY), dir));
        var z = Vector3.Cross(dir, y);
        var p = at.RelativeTo(eye);
        mesh.Instances.Add(new MeshInstance(handle, new Matrix4x4(dir.X, dir.Y, dir.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, p.X, p.Y, p.Z, 1)));
    }

    /// <summary>A length of hose from <paramref name="a"/> to <paramref name="b"/>, round enough at this size: a box along it.</summary>
    static void HoseRun(MeshBuilder mesh, Double3 a, Double3 b, float r, Vector3 colour, Double3 eye)
    {
        var d = b - a;
        if (d.Length < 1e-6)
            return;
        var dir = ToF(d.Normalized);
        var side = Vector3.Normalize(Vector3.Cross(dir, MathF.Abs(dir.Y) > 0.9f ? Vector3.UnitX : Vector3.UnitY));
        mesh.Box(((a + b) * 0.5).RelativeTo(eye), dir, Vector3.Cross(side, dir), side, new Vector3((float)d.Length * 0.5f + r * 0.5f, r, r), colour);
    }

    /// <summary>
    /// The slaughterhouse's pen and ramp (the sim's: its middle and radius, the ramp up from its edge to a car's doorway):
    /// eight panels of pen fence round it (cattle_pen), open where the ramp leaves it toward the track; the ramp (cattle_ramp)
    /// stretched to the sim's run and rise; the herd still penned milling in it, and the next one going up the ramp.
    /// </summary>
    void PenAndRamp(MeshBuilder mesh, Sim.Run.Site site, Double3 eye, double time, MeshAsset panel, MeshAsset rampPiece)
    {
        var pen = site.Pen;
        var (along, across) = SetPieceAxes(site.RampTop, pen);
        double r = site.PenRadius;
        float apothem = (float)(r * Math.Cos(Math.PI / 8));
        var centre = pen.RelativeTo(eye);
        for (int k = 0; k < 8; k++)
        {
            // (The gap toward the track, where the ramp leaves the pen: the greybox's.)
            if (k == 6)
                continue;
            float t = k * MathF.PI / 4;
            var radial = along * MathF.Cos(t) + across * MathF.Sin(t);
            var tangent = Vector3.Cross(Vector3.UnitY, radial);
            var z = Vector3.Cross(tangent, Vector3.UnitY);
            var at = centre + radial * apothem;
            mesh.Instances.Add(new MeshInstance(panel, new Matrix4x4(tangent.X, 0, tangent.Z, 0, 0, 1, 0, 0, z.X, 0, z.Z, 0, at.X, at.Y, at.Z, 1)));
        }
        // The ramp: the model's 4 m run and 1.1 m rise stretched to the sim's, its low end at the pen's edge.
        var foot = pen - ToD(across) * r;
        var top = site.RampTop;
        var run = (top - foot) with { Y = 0 };
        double rise = top.Y - foot.Y;
        var h = ToF(run.Normalized);
        var rz = -h;
        var rx = Vector3.Cross(Vector3.UnitY, rz);
        var mid = (foot + run * 0.5).RelativeTo(eye);
        var stretch = Matrix4x4.CreateScale(1, (float)(rise / 1.1), (float)(run.Length / 4.0));
        mesh.Instances.Add(new MeshInstance(rampPiece, stretch * new Matrix4x4(rx.X, rx.Y, rx.Z, 0, 0, 1, 0, 0, rz.X, rz.Y, rz.Z, 0, mid.X, mid.Y, mid.Z, 1)));
        // The herd: still penned, milling (stirred, they startle), and the one being driven up the ramp.
        int penned = site.Herding || site.Herd > 0 ? site.Head - 1 : site.Head;
        for (int i = 0; i < Math.Max(0, penned); i++)
        {
            double ang = i * 2.4 + 0.6, rad = r * (0.25 + 0.5 * ((i * 37) % 10) / 10.0);
            var at = pen + ToD(along) * (rad * Math.Cos(ang)) + ToD(across) * (rad * Math.Sin(ang));
            float yaw = (float)(ang + time * 0.2 * (i % 2 == 0 ? 1 : -1));
            double beat = time + i * 1.7;
            string clip = site.Stirred && (int)(beat / 3) % 3 == 0 ? "startle" : (int)(beat / 5) % 2 == 0 ? "idle" : "shuffle";
            Creatures.Draw(mesh, "sheep", clip, beat % 5, true, Matrix4x4.CreateRotationY(yaw) * Matrix4x4.CreateTranslation(at.RelativeTo(eye)), seed: i % 13);
        }
        if (site.Head > 0 && (site.Herding || site.Herd > 0))
        {
            var on = foot + (top - foot) * Math.Clamp(site.Herd, 0, 1);
            float yaw = MathF.Atan2(-h.X, -h.Z);
            Creatures.Draw(mesh, "sheep", "shuffle", time % 5, true, Matrix4x4.CreateRotationY(yaw) * Matrix4x4.CreateTranslation(on.RelativeTo(eye)), seed: site.Head % 13);
        }
    }

    static Double3 ToD(Vector3 v) => new(v.X, v.Y, v.Z);
}
