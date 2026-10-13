using System.Numerics;
using Ballast;
using Ballast.Render;

namespace DarkTerritory.Game.Art;

public sealed partial class SceneArt
{
    /// <summary>The modelled belt's top over its ground (facility_pieces BELT_TOP); the sim's belt is 1 m up off the track.</summary>
    const float BeltTop = 1.06f;

    /// <summary>The riser as modelled (facility_pieces RISER_LENGTH), stretched to the sim's knee and head.</summary>
    const float RiserLength = 6.2f;

    /// <summary>
    /// The grain elevator's conveyor line (A1's note 400; its art note 430) where the sim lays it: the low run from the
    /// drive house at its tail to the knee beside the track in belt sections on their trestles, the riser from the knee up
    /// to its head, the head's gantry astride the track, the drive house past the tail. It moves as the sim says:
    /// <list type="bullet">
    /// <item>the belt's splices crawl and the flywheel turns while it runs;</item>
    /// <item>the starter is down and the lamp green while it runs, the lamp red stopped or jammed;</item>
    /// <item>grain rides the belt while it carries and falls from the head into the car under it;</item>
    /// <item>a jam is a heap fouled on the belt where it is, the grain stood still behind it, some spilled off.</item>
    /// </list>
    /// False when the models aren't built (the greybox draws it).
    /// </summary>
    public bool Conveyor(MeshBuilder mesh, Sim.Run.Site site, Double3 eye, double time)
    {
        var props = PropArt.Of(Look);
        if (props.Get("belt_section") is not { } section || props.Get("belt_riser") is not { } riser || props.Get("belt_head") is not { } head
            || props.Get("drive_house") is not { } house || props.Get("drive_flywheel") is not { } flywheel || props.Get("lever_handle") is not { } handle)
            return false;
        var tail = site.ConveyorTail;
        var knee = site.ConveyorKnee;
        var top = site.ConveyorHead;
        var run = (knee - tail) with { Y = 0 };
        double length = Math.Max(1e-6, run.Length);
        var dir = Vector3.Normalize(ToF(run));
        // Model +X along the belt (SetPieceFrame turns model +Y to `across`; this is the across that makes +X `dir`).
        var across = Vector3.Cross(Vector3.UnitY, dir);
        // The low run: 3 m sections stretched a little to fill it exactly, each on the ground under its middle.
        int n = Math.Max(1, (int)Math.Round(length / 3));
        float stretch = (float)(length / (n * 3));
        for (int i = 0; i < n; i++)
        {
            var mid = Double3.Lerp(tail, knee, (i + 0.5) / n) - Double3.Up * 1.0;
            mesh.Instances.Add(new MeshInstance(section, Matrix4x4.CreateScale(stretch, 1, 1) * SetPieceFrame(mid, dir, across, eye)));
        }
        // The head's gantry astride the track (its +Y toward the riser), and the riser from the knee up into the head's hood.
        var headGround = top - Double3.Up * 4.8;
        var toKnee = ToF((knee - top) with { Y = 0 });
        var headAcross = toKnee.LengthSquared() > 1e-6f ? Vector3.Normalize(toKnee) : across;
        var headFrame = SetPieceFrame(headGround, Vector3.Cross(headAcross, Vector3.UnitY), headAcross, eye);
        mesh.Instances.Add(new MeshInstance(head, headFrame));
        var hood = ToD(ModelPoint(headFrame, 0, 1.25f, 5.3f)) + eye;
        mesh.Instances.Add(new MeshInstance(riser, Inclined(knee + Double3.Up * 0.02, hood, RiserLength, eye)));
        // The drive house past the tail, its tail drum at the belt's end; the flywheel on its +Y side, turning while it runs.
        var houseFrame = SetPieceFrame(tail - Double3.Up * 1.0 - ToD(dir) * 2.05, dir, across, eye);
        mesh.Instances.Add(new MeshInstance(house, houseFrame));
        bool moving = site.Running && site.Jam < 0;
        float spin = moving ? (float)(time * 6 % (Math.PI * 2)) : 0.4f;
        var shaft = ModelPoint(houseFrame, -0.6f, 1.85f, 1.0f);
        var wheel = Matrix4x4.CreateRotationZ(spin) * houseFrame;
        wheel.Translation = shaft;
        mesh.Instances.Add(new MeshInstance(flywheel, wheel));
        // Its lamp by the door (green running, red stopped or jammed), and the starter, down while it runs.
        mesh.Emissive = 1;
        mesh.Box(ModelPoint(houseFrame, -1.66f, 0.55f, 2.0f), dir, Vector3.UnitY, across, new Vector3(0.05f, 0.1f, 0.1f),
            moving ? Palette.SignalGreen : Palette.SignalRed);
        mesh.Emissive = 0;
        var starter = site.ConveyorStarter;
        var outward = ToF((starter - tail) with { Y = 0 });
        Lever(mesh, handle, starter, outward, site.Running, eye, across);
        // The belt's splices crawling toward the head while it runs (its speed is the grain's, 1.3 m/s).
        const double Splice = 1.5;
        for (double d = 0; d < length - 0.1; d += Splice)
        {
            double at = (d + (moving ? time * 1.32 : 0)) % length;
            var p = tail + ToD(dir) * at + Double3.Up * (BeltTop - 1.0 + 0.012);
            mesh.Box(p.RelativeTo(eye), dir, Vector3.UnitY, across, new Vector3(0.04f, 0.006f, 0.2f), Palette.IronGrey * 0.55f);
        }
        // Grain riding it while it carries; stood still behind a jam, a heap fouled at the jam and spilled off the side.
        var grain = new Vector3(0.5f, 0.4f, 0.22f);
        double jam = site.Jam >= 0 ? site.Jam : 1;
        if (site.Running || site.Jam >= 0)
            for (int i = 0; i < 24; i++)
            {
                double u = (i + (site.Carrying ? time * 1.2 % 1 : 0)) / 24.0;
                if (u > jam)
                    continue;
                var p = tail + ToD(dir) * (u * length) + Double3.Up * (BeltTop - 1.0 + 0.06) + ToD(across) * (0.1 * Math.Sin(i * 2.1));
                mesh.Box(p.RelativeTo(eye), dir, Vector3.UnitY, across, new Vector3(0.16f, 0.05f, 0.13f), grain * (i % 2 == 0 ? 1f : 0.85f));
            }
        if (site.Jam >= 0)
        {
            var at = site.JamAt + Double3.Up * (BeltTop - 1.0);
            mesh.Box((at + Double3.Up * 0.22).RelativeTo(eye), dir, Vector3.UnitY, across, new Vector3(0.4f, 0.22f, 0.32f), grain * 0.9f);
            mesh.Box((at + Double3.Up * 0.46 + ToD(dir) * 0.15).RelativeTo(eye), dir, Vector3.UnitY, across, new Vector3(0.22f, 0.12f, 0.2f), grain * 0.8f);
            mesh.Box((at - Double3.Up * 0.98 + ToD(across) * 0.7).RelativeTo(eye), dir, Vector3.UnitY, across, new Vector3(0.6f, 0.05f, 0.45f), grain * 0.7f);
        }
        if (site.Carrying)
        {
            var (along, x) = (Vector3.Cross(headAcross, Vector3.UnitY), headAcross);
            for (int i = 0; i < 12; i++)
            {
                double fall = (time * 6 + i * 0.37) % 2.0;
                var p = top - Double3.Up * (0.4 + fall) + ToD(along) * (0.12 * Math.Sin(i * 2.3)) + ToD(x) * (0.12 * Math.Cos(i * 1.7));
                mesh.Box(p.RelativeTo(eye), along, Vector3.UnitY, x, new Vector3(0.1f, 0.18f, 0.1f), grain * (i % 2 == 0 ? 1f : 0.8f));
            }
        }
        return true;
    }

    /// <summary>
    /// A model built along +X (Blender's +Y across, +Z up) laid from <paramref name="from"/> to <paramref name="to"/> up a
    /// slope, stretched from its modelled <paramref name="modelled"/> length to the distance between them; its up square to
    /// the slope, its across level.
    /// </summary>
    static Matrix4x4 Inclined(Double3 from, Double3 to, float modelled, Double3 eye)
    {
        var d = ToF(to - from);
        float length = d.Length();
        var u = d / Math.Max(1e-6f, length);
        var w = Vector3.Normalize(Vector3.UnitY - u * Vector3.Dot(Vector3.UnitY, u));
        var v = Vector3.Cross(w, u);
        var x = u * (length / modelled);
        var at = from.RelativeTo(eye);
        return new Matrix4x4(x.X, x.Y, x.Z, 0, w.X, w.Y, w.Z, 0, -v.X, -v.Y, -v.Z, 0, at.X, at.Y, at.Z, 1);
    }
}
