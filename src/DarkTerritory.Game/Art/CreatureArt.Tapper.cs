using System.Numerics;
using Ballast.Render;
using DarkTerritory.Sim.Enemies;

namespace DarkTerritory.Game.Art;

/// <summary>
/// The Tapper (GDD §21; the director's playtest of 23 Oct 2026, line 36; ARCHITECTURE §8 note 1011): drawn from boxes until a
/// modelled one comes (its long-limbed rig and clips: crawl, tap, reach, lift, flinch, knocked, death), by the sim's mode
/// (replicated in its Height). A long folded thing, arms twice its length, a small blank head: flat on the roof like a broken
/// umbrella, crawling or flinched; up on its knees at the tap with an arm out over the gunner's shoulder; lifting, both up.
/// </summary>
public sealed partial class CreatureArt
{
    TapperMode? _tapperMode;

    /// <summary>The Tapper's mode as its phase has it, for a draw without the sim's (a test's, the greybox's).</summary>
    public static TapperMode TapperModeOf(SpinePhase phase) => phase switch
    {
        SpinePhase.Telegraph or SpinePhase.Commit => TapperMode.Tap,
        SpinePhase.Grab or SpinePhase.Punish => TapperMode.Lift,
        SpinePhase.BreakOff or SpinePhase.Gone => TapperMode.Flinch,
        _ => TapperMode.Crawl,
    };

    // Its colours: grey skin over bone, and the blank head a shade paler.
    static readonly Vector3 TapperSkin = new(0.26f, 0.25f, 0.24f), TapperHead = new(0.40f, 0.39f, 0.37f);

    /// <summary>A Tapper at <paramref name="model"/> (its feet at the origin, facing −Z) in <paramref name="mode"/>, <paramref name="since"/> s in. Always drawn (true).</summary>
    static bool TapperStandIn(MeshBuilder mesh, in Matrix4x4 model, TapperMode mode, double since)
    {
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
            if (mode is TapperMode.Crawl or TapperMode.Flinch)
            {
                // Flat, the long arms out like a broken umbrella's ribs, pulling it along (flinched: pressed flatter, still).
                float flat = mode == TapperMode.Flinch ? 0.6f : 1;
                float reach = mode == TapperMode.Crawl ? 0.08f * MathF.Sin(t * 4) : 0;
                Box(0, 0.12f * flat, 0, 0.25f, 0.1f * flat, 0.5f, TapperSkin);
                Box(0, 0.15f * flat, -0.55f, 0.1f, 0.08f * flat, 0.1f, TapperHead);
                foreach (float x in new[] { -0.6f, 0.6f })
                    Box(x, 0.08f * flat, -0.2f + reach * MathF.Sign(x), 0.45f, 0.03f, 0.03f, TapperSkin);
            }
            else
            {
                // Up on its knees: at the tap an arm out forward over the gunner's shoulder; lifting, both arms up.
                bool lifting = mode == TapperMode.Lift;
                Box(0, 0.6f, 0, 0.18f, 0.55f, 0.15f, TapperSkin);
                Box(0, 1.25f, -0.05f, 0.09f, 0.1f, 0.09f, TapperHead);
                foreach (float x in new[] { -0.25f, 0.25f })
                    Box(x, lifting ? 1.7f : 1.05f, lifting ? -0.2f : -0.9f, 0.03f, lifting ? 0.6f : 0.03f, lifting ? 0.03f : 0.8f, TapperSkin);
            }
            return true;
        }
        finally
        {
            mesh.Style = style;
        }
    }
}
