using System.Numerics;
using Ballast.Render;

namespace DarkTerritory.Game.Art;

/// <summary>
/// The lineside kit (GDD §30, "a rail corridor civilization"; pipeline "track kit" and "lineside prop scatter"): the
/// pieces scattered along the line, cooked once. Trees are the era's crossed alpha cards over a faceted trunk; poles
/// carry their crossarms and insulators; rocks are jittered low-poly lumps. All in their own frame, foot at the origin.
/// </summary>
public static class WorldKit
{
    /// <summary>
    /// A black-forest pine: three crossed cards of the pine texture (alpha-tested, lit from either side) round a faceted
    /// trunk. <paramref name="variant"/> mirrors and turns the cards so a stand isn't one tree repeated.
    /// </summary>
    public static MeshAsset Pine(Look? look, int variant, float height)
    {
        var k = new Kit(look, 200 + variant);
        k.Use("pine_bark", Palette.DeepBrown, 0.6f, 0, tile: 1.5f);
        k.Cylinder(Vector3.Zero, new Vector3(0, height * 0.55f, 0), height * 0.022f, 6, caps: false, radiusB: height * 0.008f);
        k.Use("pine_card", Palette.PineDark, 0.3f, 0, tile: 1);
        k.Baked = 0;
        float width = height * 0.62f, turn = variant * 0.7f;
        bool flip = variant % 2 == 1;
        for (int i = 0; i < 3; i++)
        {
            float a = turn + i * MathF.PI / 3;
            var n = new Vector3(MathF.Sin(a), 0, MathF.Cos(a));
            k.Panel(new Vector3(0, height * 0.5f + height * 0.02f, 0), n, Vector3.UnitY, width, height,
                flip ? new Vector2(1, 0) : Vector2.Zero, flip ? new Vector2(0, 1) : Vector2.One, twoSided: true);
        }
        return k.Build($"pine-{variant}-{height:0}");
    }

    /// <summary>A dead tree: two crossed cards of bare branches (the corruption's edge, marsh and burnt ground).</summary>
    public static MeshAsset DeadTree(Look? look, int variant, float height)
    {
        var k = new Kit(look, 300 + variant);
        k.Use("dead_tree_card", Palette.SootBlack, 0.3f, 0, tile: 1);
        k.Baked = 0;
        bool flip = variant % 2 == 1;
        for (int i = 0; i < 2; i++)
        {
            float a = variant * 0.9f + i * MathF.PI / 2;
            k.Panel(new Vector3(0, height * 0.5f, 0), new Vector3(MathF.Sin(a), 0, MathF.Cos(a)), Vector3.UnitY, height * 0.55f, height,
                flip ? new Vector2(1, 0) : Vector2.Zero, flip ? new Vector2(0, 1) : Vector2.One, twoSided: true);
        }
        return k.Build($"dead-{variant}");
    }

    /// <summary>Tufts of dead grass (or the corruption's brass weeds): two small crossed cards.</summary>
    public static MeshAsset Tuft(Look? look, int variant, bool weed)
    {
        var k = new Kit(look, 400 + variant);
        k.Use(weed ? "brass_weed_card" : "grass_card", weed ? Palette.TarnishedBrass : Palette.MuddyOlive, 0.3f, 0, tile: 1);
        k.Baked = 0;
        float w = weed ? 1.1f : 1.4f, h = weed ? 0.75f : 0.6f;
        for (int i = 0; i < 2; i++)
        {
            float a = variant * 1.3f + i * MathF.PI / 2;
            k.Panel(new Vector3(0, h / 2 - 0.03f, 0), new Vector3(MathF.Sin(a), 0, MathF.Cos(a)), Vector3.UnitY, w, h,
                Vector2.Zero, new Vector2(1, 1), twoSided: true);
        }
        return k.Build($"tuft-{variant}-{weed}");
    }

    /// <summary>A boulder: an icosahedron pushed about, flat-shaded, wet dark rock.</summary>
    public static MeshAsset Rock(Look? look, int variant, float size)
    {
        var k = new Kit(look, 500 + variant);
        k.Use("rock_cliff", Palette.Charcoal, 0.6f, 0.1f, tile: 1.5f);
        float t = (1 + MathF.Sqrt(5)) / 2;
        Vector3[] v =
        [
            new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0), new(0, -1, t), new(0, 1, t),
            new(0, -1, -t), new(0, 1, -t), new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1),
        ];
        int[] f = [0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8, 3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1];
        for (int i = 0; i < v.Length; i++)
        {
            float j = 0.75f + 0.45f * Frac(MathF.Sin(i * 12.9898f + variant * 78.233f) * 43758.5f);
            var p = Vector3.Normalize(v[i]) * j;
            // Squat: wider than tall, sunk into the ground.
            v[i] = new Vector3(p.X * size, (p.Y * 0.6f + 0.2f) * size, p.Z * size * 0.85f);
        }
        for (int i = 0; i < f.Length; i += 3)
        {
            var (a, b, c) = (v[f[i]], v[f[i + 1]], v[f[i + 2]]);
            k.Tri(a, b, c, new(a.X + a.Z, -a.Y), new(b.X + b.Z, -b.Y), new(c.X + c.Z, -c.Y));
        }
        return k.Build($"rock-{variant}");
    }

    static float Frac(float x) => x - MathF.Floor(x);

    /// <summary>Where on a telegraph pole the wires are held: the insulators' tops, in the pole's frame (+X across the line).</summary>
    public static readonly Vector3[] Insulators =
    [
        new(-0.75f, 7.08f, 0), new(-0.28f, 7.08f, 0), new(0.28f, 7.08f, 0), new(0.75f, 7.08f, 0),
        new(-0.55f, 6.48f, 0), new(0.55f, 6.48f, 0),
    ];

    /// <summary>A telegraph pole (GDD §30): a weathered, slightly faceted pole, two crossarms, braces, glass insulators.</summary>
    public static MeshAsset Pole(Look? look, int variant)
    {
        var k = new Kit(look, 600 + variant);
        k.Use("wood_grey", Palette.DeepBrown, 0.8f, 0, tile: 1.2f);
        float lean = (variant % 3 - 1) * 0.04f;
        var top = new Vector3(lean, 7.5f, lean * 0.5f);
        k.Cylinder(Vector3.Zero, top, 0.14f, 7, caps: true, smooth: false, radiusB: 0.1f);
        foreach (var (y, half) in new[] { (7.0f, 0.95f), (6.4f, 0.7f) })
        {
            k.Use("wood_sleeper", Palette.DeepBrown, 0.8f, 0, tile: 1.5f);
            k.Box(new Vector3(-half, y - 0.05f, -0.06f) + new Vector3(lean * y / 7.5f, 0, 0), new Vector3(half, y + 0.05f, 0.06f) + new Vector3(lean * y / 7.5f, 0, 0));
            k.Use("rust_heavy", Palette.IronGrey, 0.8f, 0.2f);
            k.Rod(new Vector3(-half * 0.6f, y - 0.03f, 0.07f), new Vector3(lean * y / 7.5f, y - 0.6f, 0.07f), 0.012f);
            k.Rod(new Vector3(half * 0.6f, y - 0.03f, 0.07f), new Vector3(lean * y / 7.5f, y - 0.6f, 0.07f), 0.012f);
        }
        k.Use("glass_dirty", Palette.BlueGrey, 0.3f, 0.8f, tile: 0.2f);
        foreach (var i in Insulators)
            k.Lathe(i - new Vector3(0, 0.12f, 0) + new Vector3(lean * i.Y / 7.5f, 0, 0), [new(0.035f, 0), new(0.05f, 0.05f), new(0.03f, 0.1f), new(0.02f, 0.12f)], 6, smooth: false);
        return k.Build($"pole-{variant}");
    }

    /// <summary>
    /// A dead signal: a lattice-ish mast with its semaphore arm dropped and its lamp out, or burning a dim red. A landmark
    /// crews can name ("past the dead signal").
    /// </summary>
    public static MeshAsset Signal(Look? look, bool lit)
    {
        var k = new Kit(look, 700 + (lit ? 1 : 0));
        k.Use("rust_heavy", Palette.IronGrey, 0.9f, 0.3f);
        k.Box(new Vector3(-0.35f, 0, -0.35f), new Vector3(0.35f, 0.4f, 0.35f));
        k.Use("paint_black", Palette.SootBlack, 0.9f, 0.3f);
        foreach (var c in new[] { new Vector2(-0.1f, -0.1f), new(0.1f, -0.1f), new(0.1f, 0.1f), new(-0.1f, 0.1f) })
            k.Rod(new Vector3(c.X, 0.4f, c.Y), new Vector3(c.X * 0.5f, 6.5f, c.Y * 0.5f), 0.025f);
        for (float y = 1.0f; y < 6.3f; y += 0.8f)
            k.Rod(new Vector3(-0.09f, y, -0.09f), new Vector3(0.09f, y + 0.4f, 0.09f), 0.012f);
        k.Lathe(new Vector3(0, 6.5f, 0), [new(0.06f, 0), new(0.06f, 0.3f), new(0.1f, 0.35f), new(0, 0.45f)], 6, smooth: false);
        // The arm, hanging at "danger lost": dropped off the pivot at an angle nobody set.
        k.Use("paint_oxide", Palette.RustRed, 0.9f, 0.1f);
        k.With(Matrix4x4.CreateRotationZ(-1.1f) * Kit.At(-0.12f, 6.0f, -0.08f), () =>
            k.Box(new Vector3(-1.4f, -0.1f, -0.02f), new Vector3(0, 0.1f, 0.02f)));
        k.Use("paint_black", Palette.SootBlack, 0.8f, 0.3f);
        k.Box(new Vector3(-0.22f, 5.45f, -0.15f), new Vector3(0.02f, 5.75f, 0.1f));
        if (lit)
        {
            k.Use("lamp_lens", Palette.SignalRed, 0, 0, tile: 0.2f);
            k.Emissive = 1;
            k.Tint = new Vector3(1.0f, 0.2f, 0.12f);
            k.Panel(new Vector3(-0.1f, 5.6f, -0.155f), -Vector3.UnitZ, Vector3.UnitY, 0.14f, 0.14f);
            k.Emissive = 0;
        }
        // A ladder up the mast.
        TrainKit.RungLadder(k, new Vector3(0, 0, 0.2f), 5.8f, -Vector3.UnitZ, from: 0.4f);
        return k.Build($"signal-{lit}");
    }

    /// <summary>A fence post, leaning, with a strand of rusted wire's staple.</summary>
    public static MeshAsset FencePost(Look? look, int variant)
    {
        var k = new Kit(look, 800 + variant);
        k.Use("wood_grey", Palette.DeepBrown, 0.9f, 0, tile: 1);
        float lean = MathF.Sin(variant * 2.1f) * 0.08f;
        k.With(Matrix4x4.CreateRotationZ(lean), () => k.Box(new Vector3(-0.06f, -0.1f, -0.06f), new Vector3(0.06f, 1.3f, 0.06f)));
        return k.Build($"fence-{variant}");
    }
}
