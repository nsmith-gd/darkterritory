using System.Numerics;
using Ballast.Render;

namespace DarkTerritory.Game.Art;

/// <summary>
/// Track debris (GDD v1.1 §22: "only the forward lamp reveals it; hit it fast and you derail"; the sim's Sleepers): what's
/// come down across the line in the night, in the line's frame (origin on the centre line at rail height, −Z up the
/// line, +X right). Three kinds, by the hazard's id, so every machine sees the same: a pine come down across both rails
/// (WorldKit's spruce laid down, <see cref="FallenPine"/>), its root plate torn up on one side and its crown on the other; a rockfall, boulders on the rails and the smaller stuff
/// strewn back the way it came; and a tangle of old ties and a rail, heaped where something left it.
/// Inert: it's the ground's, not a creature (the v1.0 Sleepers' writhe is gone).
/// </summary>
public static class DebrisKit
{
    public const int Kinds = 3;

    public static MeshAsset Of(Look? look, int kind) => (kind % Kinds) switch
    {
        0 => FallenTree(look),
        1 => Rockfall(look),
        _ => Tangle(look),
    };

    // The fallen pine: across the line at a slant, its root plate on the right, the crown out past the left rail.
    const float TreeLean = 0.35f, TreeRoot = 4.6f, TreeRest = 0.45f;
    public const float TreeHeight = 11;
    static Vector3 TreeDir => Vector3.Normalize(new Vector3(-MathF.Cos(TreeLean), 0, MathF.Sin(TreeLean)));

    /// <summary>
    /// Where the fallen pine itself lies (WorldKit's modelled spruce, <see cref="TreeHeight"/> tall, laid on its side from
    /// its root plate), in the debris's frame: <see cref="Of"/>'s kind 0 is the root plate and what's snapped off round it.
    /// </summary>
    public static Matrix4x4 FallenPine =>
        Matrix4x4.CreateRotationZ(MathF.PI / 2) * Matrix4x4.CreateRotationY(MathF.Atan2(-TreeDir.Z, TreeDir.X) + MathF.PI)
        * Matrix4x4.CreateTranslation(-TreeDir * TreeRoot + new Vector3(0, TreeRest, 0));

    /// <summary>The fallen pine's root plate, torn up on its edge with the roots out of it, and limbs snapped off along it.</summary>
    static MeshAsset FallenTree(Look? look)
    {
        var k = new Kit(look, 940);
        var dir = TreeDir;
        var root = -dir * TreeRoot + new Vector3(0, TreeRest, 0);
        // Limbs snapped off as it came down, lying in the ballast beside it.
        k.Use("pine_bark", Palette.DeepBrown, 0.8f, 0, tile: 1.2f);
        var across = new Vector3(-dir.Z, 0, dir.X);
        for (int i = 0; i < 7; i++)
        {
            var at = root + dir * (2.5f + i * 1.15f) + across * ((i % 2 == 0 ? 1 : -1) * (0.7f + i % 3 * 0.25f)) - new Vector3(0, TreeRest - 0.06f, 0);
            float a = i * 2.1f;
            var along = Vector3.Normalize(dir * MathF.Cos(a) + across * MathF.Sin(a));
            k.Rod(at - along * 0.6f, at + along * 0.6f, 0.045f, 5);
        }
        // The root plate: the earth it tore up, on its edge, the roots sticking out of it.
        k.Use("ground_mud", Palette.DeepBrown, 0.9f, 0, tile: 1.5f);
        var face = -dir;
        k.Cylinder(root - face * 0.15f + new Vector3(0, 0.6f, 0), root + face * 0.35f + new Vector3(0, 0.6f, 0), 1.5f, 12, radiusB: 1.25f);
        k.Use("pine_bark", Palette.DeepBrown, 0.85f, 0, tile: 1);
        for (int i = 0; i < 10; i++)
        {
            float a = i * MathF.Tau / 10 + 0.3f;
            var r = across * MathF.Cos(a) + Vector3.UnitY * MathF.Sin(a);
            var from = root + new Vector3(0, 0.6f, 0) + face * 0.3f + r * 0.5f;
            k.Rod(from, from + face * (0.5f + i % 3 * 0.3f) + r * (0.9f + i % 2 * 0.4f), 0.035f, 4);
        }
        // The hole it came out of, in the bank beside the line.
        k.Use("ground_mud", Palette.SootBlack, 0.95f, 0, tile: 1.5f);
        k.Shade(0.5f);
        k.Cylinder(root + face * 1.2f - new Vector3(0, TreeRest + 0.05f, 0), root + face * 1.2f - new Vector3(0, TreeRest - 0.02f, 0), 1.3f, 12);
        return k.Build("debris-tree");
    }

    /// <summary>A rockfall come down from the left: boulders over the rails, and the smaller stuff strewn back the way it came.</summary>
    static MeshAsset Rockfall(Look? look)
    {
        var k = new Kit(look, 950);
        k.Use("rock_cliff", Palette.Charcoal, 0.6f, 0.1f, tile: 1.5f);
        void Boulder(Vector3 at, float size, int seed)
        {
            // An icosahedron pushed about, as WorldKit.Rock's, at its place.
            float t = (1 + MathF.Sqrt(5)) / 2;
            Vector3[] v =
            [
                new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0), new(0, -1, t), new(0, 1, t),
                new(0, -1, -t), new(0, 1, -t), new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1),
            ];
            int[] f = [0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8, 3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1];
            for (int i = 0; i < v.Length; i++)
            {
                float j = 0.7f + 0.5f * Frac(MathF.Sin(i * 12.9898f + seed * 78.233f) * 43758.5f);
                var p = Vector3.Normalize(v[i]) * j;
                v[i] = at + new Vector3(p.X * size, (p.Y * 0.7f + 0.3f) * size, p.Z * size * 0.9f);
            }
            for (int i = 0; i < f.Length; i += 3)
            {
                var (a, b, c) = (v[f[i]], v[f[i + 1]], v[f[i + 2]]);
                k.Tri(a, b, c);
            }
        }
        // The big ones on the rails, then the rest strewn back up toward the cutting (−X), smaller as they go.
        Boulder(new Vector3(-0.4f, 0, 0.3f), 0.95f, 1);
        Boulder(new Vector3(0.9f, 0, -1.2f), 0.7f, 2);
        Boulder(new Vector3(-1.6f, 0, 1.6f), 0.6f, 3);
        for (int i = 0; i < 18; i++)
        {
            float h = Frac(MathF.Sin(i * 3.17f) * 43758.5f), g = Frac(MathF.Sin(i * 7.31f + 1) * 43758.5f);
            float x = -0.5f - i * 0.42f + (g - 0.5f) * 0.8f, z = (h - 0.5f) * (4 + i * 0.25f);
            Boulder(new Vector3(MathF.Min(x, 1.6f), 0, z), 0.12f + 0.38f * h * MathF.Max(0.3f, 1 - i / 22f), 10 + i);
        }
        return k.Build("debris-rocks");
    }

    /// <summary>Old ties and a bent rail heaped over the line, as if dragged there and dropped.</summary>
    static MeshAsset Tangle(Look? look)
    {
        var k = new Kit(look, 960);
        k.Use("wood_sleeper", Palette.DeepBrown, 0.9f, 0, tile: 1.5f);
        for (int i = 0; i < 7; i++)
        {
            float a = MathF.Sin(i * 2.3f) * 0.9f + (i % 2) * 0.6f;
            float y = 0.17f + (i % 3) * 0.2f, z = (i - 3) * 0.75f + MathF.Sin(i * 1.7f) * 0.3f, x = MathF.Sin(i * 4.1f) * 0.6f;
            var along = new Vector3(MathF.Cos(a), MathF.Sin(i * 1.3f) * 0.15f, MathF.Sin(a));
            // A tie, 2.6 m long, a quarter-metre square, at its angle.
            var c = new Vector3(x, y, z);
            k.Rod(c - along * 1.3f, c + along * 1.3f, 0.12f);
        }
        // A length of rail bent up over the heap.
        k.Use("rail_steel", Palette.IronGrey, 0.6f, 0.5f, tile: 1);
        Vector3 Rail(float t) => new(-2.4f + t * 5.2f, 0.2f + MathF.Sin(t * MathF.PI) * 0.9f, -1.6f + t * 2.4f + MathF.Sin(t * 5) * 0.2f);
        for (int i = 0; i < 8; i++)
            k.Rod(Rail(i / 8f), Rail((i + 1) / 8f), 0.06f, 4);
        return k.Build("debris-tangle");
    }

    static float Frac(float x) => x - MathF.Floor(x);
}
