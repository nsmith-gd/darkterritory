using System.Numerics;
using Ballast.Render;
using DarkTerritory.Sim.Enemies;

namespace DarkTerritory.Game.Art;

/// <summary>
/// The Bruin (GDD §21; the director's playtest of 23 Oct 2026, line 36; ARCHITECTURE §8 note 1007): drawn from boxes until a
/// modelled one comes (its rig and clips: come, climb, feed, rise, charge, maul, turn, stagger, death), by the sim's mode
/// (replicated in its Height). A great bear gone to grey moss, its chest opened and a pale thing breathing in there: on all
/// fours coming and feeding (its head down, the body heaving), staggered aside; risen on its hind legs, arms up, over whoever's
/// in front of it and mauling.
/// </summary>
public sealed partial class CreatureArt
{
    BruinMode? _bruinMode;

    /// <summary>The Bruin's mode as its phase has it, for a draw without the sim's (a test's, the greybox's).</summary>
    public static BruinMode BruinModeOf(SpinePhase phase) => phase switch
    {
        SpinePhase.Commit => BruinMode.Feed,
        SpinePhase.Grab or SpinePhase.Punish => BruinMode.Maul,
        SpinePhase.BreakOff or SpinePhase.Gone => BruinMode.Leave,
        _ => BruinMode.Come,
    };

    // Its colours: moss gone grey over dark hide, the pale thing in its chest, and its eye-shine.
    static readonly Vector3 BruinMoss = new(0.16f, 0.17f, 0.14f), BruinHide = new(0.09f, 0.08f, 0.07f), BruinChest = new(0.55f, 0.50f, 0.42f),
        BruinEye = new(1.4f, 0.9f, 0.3f);

    /// <summary>A Bruin at <paramref name="model"/> (its feet at the origin, facing −Z) in <paramref name="mode"/>, <paramref name="since"/> s in. Always drawn (true).</summary>
    static bool BruinStandIn(MeshBuilder mesh, in Matrix4x4 model, BruinMode mode, double since)
    {
        // Its own colours, not the world's surfaces (the scene's style textures a box as stone or planks).
        var style = mesh.Style;
        mesh.Style = null;
        try
        {
            var o = model.Translation;
            var r = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitX, model));
            var u = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitY, model));
            var b = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, model));
            float t = (float)since;
            void Box(float x, float y, float z, float hx, float hy, float hz, Vector3 colour) =>
                mesh.Box(o + r * x + u * y + b * z, r, u, b, new Vector3(hx, hy, hz), colour);
            // The breath in the opened chest: a slow heave.
            float breathe = 0.03f * MathF.Sin(t * 2.2f);
            if (mode is BruinMode.Rise or BruinMode.Maul)
            {
                // Up on its hind legs, the head in a car's roof, the arms up (down on whoever it has, mauling).
                bool maul = mode == BruinMode.Maul;
                Box(0, 1.1f, 0.1f, 0.55f, 0.85f + breathe, 0.45f, BruinMoss);
                Box(0, 2.05f, -0.15f, 0.3f, 0.28f, 0.32f, BruinHide);
                Box(0, 1.3f, -0.42f, 0.22f, 0.3f, 0.05f, BruinChest);
                foreach (float x in new[] { -0.12f, 0.12f })
                    Box(x, 2.1f, -0.48f, 0.03f, 0.03f, 0.02f, BruinEye);
                foreach (float x in new[] { -0.7f, 0.7f })
                    Box(x, maul ? 1.2f : 1.7f, maul ? -0.8f : -0.5f, 0.14f, 0.14f, 0.5f, BruinMoss);
                foreach (float x in new[] { -0.3f, 0.3f })
                    Box(x, 0.25f, 0.2f, 0.16f, 0.25f, 0.18f, BruinHide);
            }
            else
            {
                // On all fours: coming in a lumbering run, its head down feeding, knocked aside staggered, going.
                bool feeding = mode == BruinMode.Feed;
                float sway = mode == BruinMode.Stagger ? 0.18f : mode is BruinMode.Come or BruinMode.Leave ? 0.05f * MathF.Sin(t * 5) : 0;
                Box(sway, 0.95f, 0.1f, 0.6f, 0.45f + breathe, 1.1f, BruinMoss);
                Box(sway, feeding ? 0.65f : 1.0f, -1.25f, 0.3f, 0.28f, 0.35f, BruinHide);
                Box(sway, 0.62f, -0.75f, 0.25f, 0.18f, 0.05f, BruinChest);
                foreach (float x in new[] { -0.12f, 0.12f })
                    Box(sway + x, feeding ? 0.72f : 1.07f, -1.6f, 0.03f, 0.03f, 0.02f, BruinEye);
                foreach (float x in new[] { -0.4f, 0.4f })
                    foreach (float z in new[] { -0.7f, 0.8f })
                        Box(x, 0.28f, z, 0.15f, 0.28f, 0.15f, BruinHide);
            }
            return true;
        }
        finally
        {
            mesh.Style = style;
        }
    }
}
