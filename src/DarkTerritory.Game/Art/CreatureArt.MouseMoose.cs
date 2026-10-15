using System.Numerics;
using Ballast.Render;
using DarkTerritory.Sim.Enemies;

namespace DarkTerritory.Game.Art;

/// <summary>
/// The Mouse Moose (GDD §21; the director, 15 Oct 2026: "a mutant rat with little tiny moose antlers"; ARCHITECTURE §8 notes
/// 790, 796): its model (tools/blender/mouse_moose.py) by the sim's mode (replicated in its Height) and how long it's been in
/// it, or, where that isn't there, its boxes. A dog-sized rat, hunched and mangy, its pink tail behind it, and on its head a
/// little pair of moose's palmate antlers. Creeping, low and slow; its teeth in a casting, braced back on its haunches and
/// tugging; bolting, stretched out flat; down its hole, only its antlers, its ears and its eyes over the earth.
/// </summary>
public sealed partial class CreatureArt
{
    MouseMooseMode? _mouseMooseMode;

    // Its clips' lengths (tools/blender/mouse_moose.py): the dive into its hole; the emerge out of it, of which a creep
    // plays the scramble (from its peering, the sim already moving it); the pace (m/s) its drag's heaves are made for (the
    // sim's drag), played faster when it's dragging the casting away (dragAway) but no more than so.
    public const double MouseMooseDiveSeconds = 0.5, MouseMooseEmergeSeconds = 0.8, MouseMooseScrambleFrom = 0.47;
    const float MouseMooseDragPace = 0.15f, MouseMooseDragFastest = 2.2f;

    /// <summary>
    /// The clip the Mouse Moose plays, how far into it, and whether it loops: by its mode (the sim's), what it was doing before
    /// (out of its hole it scrambles; into it, from a bolt or with a casting, it dives), how long it's been at it and how fast
    /// it's going (stood still creeping, it sniffs; the drag's heaves come quicker as it drags faster).
    /// </summary>
    public static (string Clip, double Time, bool Loop) MouseMooseClip(MouseMooseMode mode, MouseMooseMode? was, double modeSeconds, float pace) => mode switch
    {
        MouseMooseMode.Creep when was == MouseMooseMode.Hide && modeSeconds < MouseMooseEmergeSeconds - MouseMooseScrambleFrom
            => ("emerge", MouseMooseScrambleFrom + modeSeconds, false),
        MouseMooseMode.Creep => pace > 0.3f ? ("creep", modeSeconds, true) : ("idle", modeSeconds, true),
        MouseMooseMode.Bite => ("bite", modeSeconds, false),
        MouseMooseMode.Drag => ("drag", modeSeconds * Math.Clamp(pace / MouseMooseDragPace, 1f, MouseMooseDragFastest), true),
        MouseMooseMode.Flee => ("flee", modeSeconds, true),
        MouseMooseMode.Hide when was is MouseMooseMode.Flee or MouseMooseMode.Drag && modeSeconds < MouseMooseDiveSeconds
            => ("dive", modeSeconds, false),
        MouseMooseMode.Hide when was is MouseMooseMode.Flee or MouseMooseMode.Drag => ("hide", modeSeconds - MouseMooseDiveSeconds, true),
        _ => ("hide", modeSeconds, true),
    };

    /// <summary>
    /// Its hole, under a Mouse Moose down it at <paramref name="model"/> (or climbing out of it, <paramref name="behind"/> m
    /// back along the way it's going): the black mouth of it, and the earth it's dug out heaped round it, most on the side
    /// it comes and goes by.
    /// </summary>
    static void MouseMooseHole(MeshBuilder mesh, in Matrix4x4 model, float behind)
    {
        var style = mesh.Style;
        mesh.Style = null;
        try
        {
            var r = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitX, model));
            var u = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitY, model));
            var b = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, model));
            var o = model.Translation + b * behind;
            mesh.Box(o + u * 0.004f, r, u, b, new Vector3(0.19f, 0.004f, 0.22f), MouseHoleDark);
            for (int k = 0; k < 9; k++)
            {
                // A ring of turned clods round the mouth, heaped up in front (the way out, −Z) where it's thrown the spoil.
                float a = k / 9f * MathF.Tau + 0.3f * Hash01(k, 796);
                float front = MathF.Max(0, -MathF.Cos(a));
                float rad = 0.27f + 0.06f * front + 0.03f * Hash01(k, 797);
                var at = o + r * (MathF.Sin(a) * rad) + b * (MathF.Cos(a) * rad);
                var outward = Vector3.Normalize(r * MathF.Sin(a) + b * MathF.Cos(a));
                var along = Vector3.Cross(u, outward);
                // Tipped back off the hole, as dug earth lies.
                var up = Vector3.Normalize(u + outward * 0.45f);
                var outN = Vector3.Normalize(outward - u * 0.45f);
                float h = 0.035f + 0.05f * front + 0.015f * Hash01(k, 798);
                mesh.Box(at + u * (h * 0.6f), along, up, outN, new Vector3(0.1f + 0.04f * Hash01(k, 799), h, 0.07f + 0.03f * front),
                    MouseEarth * (0.85f + 0.3f * Hash01(k, 800)));
            }
        }
        finally
        {
            mesh.Style = style;
        }
    }

    /// <summary>The Mouse Moose's mode as its phase has it, for a draw without the sim's (a test's, the greybox's).</summary>
    public static MouseMooseMode MouseMooseModeOf(SpinePhase phase) => phase switch
    {
        SpinePhase.Commit or SpinePhase.Grab or SpinePhase.Punish => MouseMooseMode.Drag,
        SpinePhase.BreakOff => MouseMooseMode.Flee,
        _ => MouseMooseMode.Creep,
    };

    // Its colours: grey-brown fur gone patchy, the bare pink of a rat's tail, ears and nose, yellow teeth, a red eye-shine,
    // and the antlers' bone.
    static readonly Vector3 MouseFur = new(0.20f, 0.17f, 0.14f), MouseMange = new(0.30f, 0.25f, 0.21f), MousePink = new(0.55f, 0.33f, 0.33f),
        MouseTooth = new(0.62f, 0.55f, 0.30f), MouseEye = new(1.6f, 0.25f, 0.12f), MouseAntler = new(0.52f, 0.46f, 0.36f), MouseEarth = new(0.07f, 0.06f, 0.05f),
        MouseHoleDark = new(0.008f, 0.007f, 0.006f);

    /// <summary>
    /// A Mouse Moose at <paramref name="model"/> (its feet at the origin, facing −Z) in <paramref name="mode"/>,
    /// <paramref name="since"/> seconds in. Always drawn (true).
    /// </summary>
    static bool MouseMooseStandIn(MeshBuilder mesh, in Matrix4x4 model, MouseMooseMode mode, double since)
    {
        // Its own colours, not the world's surfaces (the scene's style textures a box as stone or planks).
        var style = mesh.Style;
        mesh.Style = null;
        try
        {
            StandIn(mesh, model, mode, since);
        }
        finally
        {
            mesh.Style = style;
        }
        return true;
    }

    static void StandIn(MeshBuilder mesh, in Matrix4x4 model, MouseMooseMode mode, double since)
    {
        var o = model.Translation;
        var r = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitX, model));
        var u = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitY, model));
        var b = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, model));
        float t = (float)since;
        // How it holds itself: down (its hole), how low it crouches, how far it leans back on its haunches, and its gait.
        bool hidden = mode == MouseMooseMode.Hide;
        float sink = hidden ? 0.42f : 0;
        float crouch = mode switch { MouseMooseMode.Creep => -0.05f, MouseMooseMode.Flee => -0.07f, _ => 0f };
        // Tugging: back on its haunches, the head down at the casting, a heave every 0.8 s.
        float tug = mode == MouseMooseMode.Drag ? 0.04f * MathF.Max(0, MathF.Sin(t * MathF.Tau / 0.8f)) : 0;
        float lean = mode is MouseMooseMode.Drag or MouseMooseMode.Bite ? 0.08f : 0;
        float stretch = mode == MouseMooseMode.Flee ? 0.08f : 0;
        float stride = mode switch { MouseMooseMode.Creep => 3.2f, MouseMooseMode.Flee => 11f, MouseMooseMode.Drag => 1.25f, _ => 0f };
        float swing = mode switch { MouseMooseMode.Creep => 0.06f, MouseMooseMode.Flee => 0.13f, MouseMooseMode.Drag => 0.03f, _ => 0f };

        Vector3 P(float x, float y, float z) => o + r * x + u * (y + crouch - sink) + b * (z + tug);
        void Box(float x, float y, float z, float hx, float hy, float hz, Vector3 colour) =>
            mesh.Box(P(x, y, z), r, u, b, new Vector3(hx, hy, hz), colour);
        // A piece turned about the body's long axis (an antler's beam and palm, out and up from the skull).
        void Tilted(float x, float y, float z, float hx, float hy, float hz, float roll, Vector3 colour)
        {
            var rr = r * MathF.Cos(roll) + u * MathF.Sin(roll);
            var uu = u * MathF.Cos(roll) - r * MathF.Sin(roll);
            mesh.Box(P(x, y, z), rr, uu, b, new Vector3(hx, hy, hz), colour);
        }

        if (hidden)
        {
            // Down its hole: a heap of turned earth, and over it the antlers' tips and the eyes, catching the lamp.
            mesh.Box(o + u * 0.05f, r, u, b, new Vector3(0.38f, 0.05f, 0.38f), MouseEarth);
        }
        else
        {
            // The body: haunches, a hunched back, the shoulders; mange in patches.
            Box(0, 0.30f - lean * 0.5f, 0.24f + stretch, 0.21f, 0.17f, 0.19f, MouseFur);
            Box(0, 0.36f - lean * 0.2f, 0.02f, 0.19f, 0.17f, 0.22f + stretch * 0.5f, MouseFur);
            Box(0.08f, 0.47f, 0.08f, 0.09f, 0.04f, 0.11f, MouseMange);
            Box(0, 0.34f, -0.2f - stretch, 0.16f, 0.14f, 0.13f, MouseFur);
            // Four legs, at a walk's swing (none down its hole); bent and braced back when tugging.
            for (int leg = 0; leg < 4; leg++)
            {
                float side = leg % 2 == 0 ? -1 : 1;
                bool fore = leg < 2;
                float phase = (fore ? 0 : MathF.PI) + (side > 0 ? MathF.PI : 0);
                float sw = swing * MathF.Sin(t * stride + phase);
                float z = (fore ? -0.22f - stretch * 1.5f : 0.3f + stretch) + sw + (mode == MouseMooseMode.Drag && !fore ? 0.06f : 0);
                Box(side * (fore ? 0.12f : 0.15f), 0.11f, z, 0.04f, 0.11f, 0.045f, MouseFur);
                Box(side * (fore ? 0.12f : 0.15f), 0.012f, z - 0.03f, 0.045f, 0.012f, 0.06f, MousePink);
            }
            // The tail: a pink rope behind it, swinging.
            float sway = MathF.Sin(t * (stride > 0 ? stride * 0.5f : 1.3f));
            for (int s = 0; s < 7; s++)
            {
                float k = s / 6f;
                float x = sway * 0.14f * k * k;
                float half = 0.035f - 0.022f * k;
                Box(x, 0.24f - 0.17f * k, 0.44f + stretch + 0.1f * s, half, half, 0.07f, MousePink);
            }
        }
        // The head: low and forward, lower still with its teeth in a casting; a long snout, a pink nose, the yellow incisors.
        float head = mode is MouseMooseMode.Drag or MouseMooseMode.Bite ? -0.09f : 0;
        float hz = -0.42f - stretch * 2f;
        Box(0, 0.36f + head, hz, 0.12f, 0.1f, 0.12f, MouseFur);
        if (!hidden)
        {
            Box(0, 0.31f + head, hz - 0.18f, 0.07f, 0.06f, 0.08f, MouseFur);
            Box(0, 0.32f + head, hz - 0.27f, 0.025f, 0.022f, 0.02f, MousePink);
            Box(0, 0.25f + head, hz - 0.24f, 0.028f, 0.03f, 0.008f, MouseTooth);
            foreach (float side in new[] { -1f, 1f })
                Box(side * 0.1f, 0.47f + head, hz + 0.05f, 0.05f, 0.05f, 0.012f, MousePink);
        }
        foreach (float side in new[] { -1f, 1f })
        {
            // The eyes, red in the lamp.
            Box(side * 0.07f, 0.4f + head, hz - 0.1f, 0.018f, 0.018f, 0.012f, MouseEye);
            // A moose's antlers, small: a beam out and up from the skull, the palm on it, three tines along the palm's edge.
            Tilted(side * 0.12f, 0.49f + head, hz + 0.02f, 0.06f, 0.012f, 0.014f, side * 0.5f, MouseAntler);
            Tilted(side * 0.21f, 0.56f + head, hz + 0.02f, 0.07f, 0.035f, 0.008f, side * 0.35f, MouseAntler);
            for (int k = 0; k < 3; k++)
                Tilted(side * (0.17f + 0.05f * k), 0.61f + head + 0.012f * k, hz + 0.02f, 0.009f, 0.032f, 0.009f, side * (0.1f + 0.15f * k), MouseAntler);
            // The brow tine, forward.
            Box(side * 0.11f, 0.52f + head, hz - 0.05f, 0.008f, 0.008f, 0.04f, MouseAntler);
        }
    }
}
