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
    /// same and a screenshot repeats.
    /// </summary>
    /// <param name="left">Seconds the answer still shows (World.Answer.Seconds); <paramref name="show"/> its whole length.</param>
    public void Eyes(MeshBuilder mesh, Double3 at, Double3 eye, double left, double show)
    {
        var rel = at - eye;
        if (left <= 0 || rel.Length > 260)
            return;
        float age = (float)(show - left);
        // In over half a second, out over the last second and a half; one blink a little after the middle.
        float alpha = MathF.Min(1, age / 0.5f) * MathF.Min(1, (float)left / 1.5f);
        float blink = MathF.Abs(age - (float)show * 0.55f) < 0.12f ? 0 : 1;
        if (alpha * blink <= 0.01f)
            return;
        // Side by side across the line of sight, level.
        var flat = new Vector3((float)rel.X, 0, (float)rel.Z);
        var across = flat.LengthSquared() > 1e-4f ? Vector3.Normalize(new Vector3(-flat.Z, 0, flat.X)) : Vector3.UnitX;
        var centre = F(rel);
        // Far off they'd be a speck: drawn a little larger with distance, so at the lamp's edge they still read as two.
        float scale = 1 + MathF.Max(0, (float)rel.Length - 20) / 40;
        foreach (float side in new[] { -1f, 1f })
        {
            var p = centre + across * (0.09f * scale * side);
            mesh.Billboard(p, 0.07f * scale, 0, new Vector4(0.75f, 0.95f, 0.45f, alpha * blink), -1, FxBlend.Additive);
            mesh.Billboard(p, 0.3f * scale, 0, new Vector4(0.35f, 0.55f, 0.18f, 0.35f * alpha * blink), -1, FxBlend.Additive);
        }
    }
}
