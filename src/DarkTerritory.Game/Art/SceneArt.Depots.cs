using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Art;

/// <summary>
/// The facilities' working modules (spec D.2), modelled (tools/models depot_modules), drawn where the sim has them and
/// moved as it moves them: the capstan winch turned by its crank with the sled as far as it's hauled (T43), the gantry
/// crane's bridge, trolley and hook with its castings (T48), and the coaling tower's lever. Each returns false where
/// the models aren't built, and the greybox draws it instead.
/// </summary>
public sealed partial class SceneArt
{
    /// <summary>A placement: model axes (x, y, z) as world directions, at a point relative to the eye.</summary>
    static Matrix4x4 Placed(Vector3 x, Vector3 y, Vector3 z, Vector3 at) =>
        new(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, at.X, at.Y, at.Z, 1);

    public bool Winch(MeshBuilder mesh, Sim.Run.Site site, Double3 eye)
    {
        var props = PropArt.Of(Look);
        if (!site.Has(Sim.Run.ModuleKind.Winch) || props.Get("winch_drum") is not { } drum)
            return false;
        var axis = Vector3.Normalize(ToF(site.Axis));
        var outward = Vector3.Normalize(ToF(site.Outward));
        var basis = Placed(axis, Vector3.UnitY, outward, Vector3.Zero);
        var ground = site.Capstan.RelativeTo(eye);
        var hub = (site.Capstan + Double3.Up * 0.9).RelativeTo(eye);
        if (props.Get("winch_frame") is { } frame)
            mesh.Append(frame, basis * Matrix4x4.CreateTranslation(ground));
        // The drum turns with the crank; each crank's arm points where the sim's grip is (Site.Grip).
        mesh.Append(drum, Matrix4x4.CreateRotationX(-(float)site.Crank) * basis * Matrix4x4.CreateTranslation(hub));
        if (props.Get("winch_crank") is { } crank)
            for (int i = 0; i < site.Handles.Length; i++)
            {
                float a = (float)(site.Crank + i * Math.PI);
                // Handle 0's grip sticks out the other way, off the drum's far end.
                var flip = i == 0 ? Matrix4x4.CreateRotationZ(MathF.PI) : Matrix4x4.Identity;
                mesh.Append(crank, flip * Matrix4x4.CreateRotationX(-a) * basis * Matrix4x4.CreateTranslation(site.Handles[i].RelativeTo(eye)));
            }
        // The rope off the drum's top to the sled, and the sled as far as it's come.
        var from = site.Capstan + Double3.Up * 1.12;
        var to = site.Sled + Double3.Up * 0.14;
        var rope = to - from;
        if (rope.Length > 0.5)
        {
            var dir = rope.Normalized;
            var side = Double3.Cross(dir, Double3.Up).Normalized;
            mesh.Box(((from + to) * 0.5).RelativeTo(eye), ToF(dir), ToF(Double3.Cross(side, dir)), ToF(side),
                new Vector3((float)rope.Length * 0.5f, 0.018f, 0.018f), Palette.DeepBrown);
        }
        if (site.SledsLeft > 0 && props.Get("winch_sled") is { } sled)
        {
            var along = site.SledTo - site.SledFrom;
            var toward = Vector3.Normalize(ToF(along.Length > 0.1 ? along * -1 : site.Outward * -1) with { Y = 0 });
            var across = Vector3.Cross(Vector3.UnitY, toward);
            mesh.Append(sled, Placed(toward, Vector3.UnitY, across, site.Sled.RelativeTo(eye)));
        }
        return true;
    }

    public bool Crane(MeshBuilder mesh, Sim.Run.Crane crane, IReadOnlyList<CarFrame> frames, Double3 eye)
    {
        var props = PropArt.Of(Look);
        if (props.Get("crane_leg") is not { } leg)
            return false;
        var alongD = crane.Corner(1, 0) - crane.Corner(0, 0);
        var acrossD = crane.Corner(0, 1) - crane.Corner(0, 0);
        var along = Vector3.Normalize(ToF(alongD));
        var across = Vector3.Normalize(ToF(acrossD));
        var up = Vector3.UnitY;
        // Model axes: the along-crane parts lie along x; the bridge lies across; everything stands up y.
        var alongBasis = Placed(along, up, Vector3.Cross(along, up), Vector3.Zero);
        var acrossBasis = Placed(across, up, Vector3.Cross(across, up), Vector3.Zero);
        double top = crane.Top;
        for (int end = 0; end < 2; end++)
            for (int side = 0; side < 2; side++)
                mesh.Append(leg, alongBasis * Matrix4x4.CreateTranslation(crane.Corner(end, side).RelativeTo(eye)));
        // The rail girders along the top, 5 m lengths laid end to end (the last one cut to fit).
        if (props.Get("crane_girder") is { } girder)
            for (int side = 0; side < 2; side++)
            {
                double length = alongD.Length;
                for (double x = 0; x < length - 0.01; x += 5)
                {
                    double piece = Math.Min(5, length - x);
                    var at = crane.Corner(0, side) + alongD * ((x + piece / 2) / length) + Double3.Up * top;
                    mesh.Append(girder, Matrix4x4.CreateScale((float)piece / 5, 1, 1) * alongBasis * Matrix4x4.CreateTranslation(at.RelativeTo(eye)));
                }
            }
        // The bridge across, on the rails where it is; the trolley on it; the cable down to the hook.
        var bridgeMid = (crane.BridgeEnd(0) + crane.BridgeEnd(1)) * 0.5;
        float span = (float)(crane.BridgeEnd(1) - crane.BridgeEnd(0)).Length;
        const double deck = 0.97, onDeck = 0.49;
        if (props.Get("crane_bridge") is { } bridge)
            mesh.Append(bridge, Matrix4x4.CreateScale(span / 12.5f, 1, 1) * acrossBasis * Matrix4x4.CreateTranslation((bridgeMid + Double3.Up * (deck - 0.4)).RelativeTo(eye)));
        var hook = crane.HookAt;
        var trolleyAt = new Double3(hook.X, bridgeMid.Y + deck + onDeck - 0.4, hook.Z);
        if (props.Get("crane_trolley") is { } trolley)
            mesh.Append(trolley, alongBasis * Matrix4x4.CreateTranslation(trolleyAt.RelativeTo(eye)));
        var drumAt = trolleyAt + Double3.Up * 0.72;
        var cable = hook + Double3.Up * 0.66 - drumAt;
        if (cable.Length > 0.1)
            mesh.Box(((drumAt + hook + Double3.Up * 0.66) * 0.5).RelativeTo(eye), Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ,
                new Vector3(0.015f, (float)cable.Length * 0.5f, 0.015f), Palette.SootBlack);
        if (props.Get("crane_hook") is { } hookModel)
            mesh.Append(hookModel, alongBasis * Matrix4x4.CreateTranslation((hook + Double3.Up * 0.32).RelativeTo(eye)));
        if (props.Get("crane_cab") is { } cab)
            mesh.Append(cab, alongBasis * Matrix4x4.CreateTranslation(crane.Cab.RelativeTo(eye)));
        if (props.Get("crane_controls") is { } controls)
            mesh.Append(controls, acrossBasis * Matrix4x4.CreateTranslation(crane.Controls.RelativeTo(eye)));
        // The castings: stacked, on the hook, or lashed on a car's roof.
        if (props.Get("casting") is { } casting)
            foreach (var c in crane.Castings)
                if (crane.Where(c, frames) is { } at)
                {
                    bool onCar = c.State == Sim.Run.CastingState.Loaded && c.Car >= 0 && c.Car < frames.Count;
                    var carUp = onCar ? ToF(frames[c.Car].Up) : up;
                    var x = onCar ? ToF(frames[c.Car].Right) : along;
                    mesh.Append(casting, Placed(x, carUp, Vector3.Cross(x, carUp), (at + (onCar ? frames[c.Car].Up : Double3.Up) * 0.5).RelativeTo(eye)));
                }
        return true;
    }

    /// <summary>The coaling tower's lever stand at its place by the track, the handle down when pouring, up when shut.</summary>
    public bool ChuteLever(MeshBuilder mesh, Double3 lever, Double3 tangent, double side, bool open, Double3 eye)
    {
        var props = PropArt.Of(Look);
        if (props.Get("chute_lever") is not { } stand)
            return false;
        var right = Vector3.Normalize(ToF(Double3.Cross(tangent, Double3.Up)));
        var back = -Vector3.Normalize(ToF(tangent));
        var basis = Placed(right, Vector3.UnitY, back, Vector3.Zero);
        var ground = (lever - Double3.Up * 0.9).RelativeTo(eye);
        mesh.Append(stand, basis * Matrix4x4.CreateTranslation(ground));
        if (props.Get("chute_handle") is { } handle)
        {
            var turn = (side < 0 ? Matrix4x4.CreateRotationY(MathF.PI) : Matrix4x4.Identity) * Matrix4x4.CreateRotationZ(open ? -0.6f : 0.45f);
            mesh.Append(handle, turn * basis * Matrix4x4.CreateTranslation(ground + new Vector3(0, 0.9f, 0)));
        }
        return true;
    }
}
