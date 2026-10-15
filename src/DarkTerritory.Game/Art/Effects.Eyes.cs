using System.Numerics;
using Ballast;
using Ballast.Render;

namespace DarkTerritory.Game.Art;

public sealed partial class Effects
{
    /// <summary>
    /// The dark's answer to a draw (ARCHITECTURE §8 note 287; World.Answer): a pair of eyes at the lamp's edge, caught in its
    /// light the way an animal's are (eyeshine: a cold green-gold, no face), turned toward the train. They open as the answer
    /// is heard, blink once, and go out before it comes. Worked out from the answer's time left, so every machine draws the
    /// same and a screenshot repeats. (A sign shown the crew afoot has a body under its eyes: CreatureArt.Watching, note 761.)
    /// </summary>
    /// <param name="left">Seconds the answer still shows (World.Answer.Seconds); <paramref name="show"/> its whole length.</param>
    public void Eyes(MeshBuilder mesh, Double3 at, Double3 eye, double left, double show)
    {
        var rel = at - eye;
        if (left <= 0 || rel.Length > 260)
            return;
        float open = Open(left, show);
        if (open <= 0.01f)
            return;
        // Side by side across the line of sight, level.
        var flat = new Vector3((float)rel.X, 0, (float)rel.Z);
        var across = flat.LengthSquared() > 1e-4f ? Vector3.Normalize(new Vector3(-flat.Z, 0, flat.X)) : Vector3.UnitX;
        var centre = F(rel);
        // (Set wider as EyePair draws them larger, so out at the lamp's edge they're still two.)
        var apart = across * (EyesApart * MathF.Max(1, (float)rel.Length / EyesFrom));
        EyePair(mesh, centre - apart, centre + apart, open);
    }

    /// <summary>Half the gap between an animal's two eyes (m) where nothing says otherwise: the answer's.</summary>
    public const float EyesApart = 0.07f;

    /// <summary>How far off (m) a pair of eyes is drawn its own size; past it, a little larger, so it still reads as two.</summary>
    public const float EyesFrom = 24;

    /// <summary>An eye's hot core, and its halo, across (m), at its own size.</summary>
    public const float EyeCore = 0.05f, EyeHalo = 0.2f;

    /// <summary>
    /// How open a sign's eyes are (0..1), <paramref name="left"/> of <paramref name="show"/> seconds still to show: in over
    /// half a second, out over the last second and a half, shut for a blink a little after the middle.
    /// </summary>
    public static float Open(double left, double show)
    {
        float age = (float)(show - left);
        float open = MathF.Min(1, age / 0.5f) * MathF.Min(1, (float)left / 1.5f);
        return MathF.Abs(age - (float)show * 0.55f) < 0.12f ? 0 : MathF.Max(0, open);
    }

    /// <summary>
    /// Two eyes at <paramref name="left"/> and <paramref name="right"/> (camera-relative), <paramref name="open"/> open: each a
    /// hot core and a dim halo of eyeshine, their own size (note 761: no longer swelling with distance into two lamps), but
    /// for a little more past 24 m so at the lamp's edge they still read as two.
    /// </summary>
    public void EyePair(MeshBuilder mesh, Vector3 left, Vector3 right, float open)
    {
        if (open <= 0.01f)
            return;
        foreach (var p in new[] { left, right })
        {
            float scale = MathF.Max(1, p.Length() / EyesFrom);
            mesh.Billboard(p, EyeCore * scale, 0, new Vector4(1.6f, 2.0f, 0.9f, open), -1, FxBlend.Additive);
            mesh.Billboard(p, EyeHalo * scale, 0, new Vector4(0.45f, 0.7f, 0.22f, 0.45f * open), -1, FxBlend.Additive);
        }
    }
}
