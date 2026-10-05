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
    /// A black-forest spruce as the 2008-2012 benchmarks built their trees, not the old crossed pictures of one: a tapered
    /// trunk carrying whorls of boughs from low on it to the leader, each bough a card of one spruce branch (pine_bough)
    /// bent in two, rising off the trunk and drooping to its tip, longest at the bottom, packed close enough to overlap into a
    /// mass. Alternate boughs roll either way about their length so none is ever seen edge-on. About 2000 triangles; the
    /// lineside keeps <see cref="PineCard"/> for the far field. <paramref name="variant"/> turns the whorls and jitters the boughs so a stand isn't one tree repeated.
    /// </summary>
    public static MeshAsset Pine(Look? look, int variant, float height) =>
        Boughs(look, variant, height, reach: 0.34f, whorls: 18, core: "pine_card", tint: null, club: false, name: $"pine-{variant}-{height:0}");

    /// <summary>
    /// A black spruce near the line (maritime-rules.md §5: narrow, ragged, clubbed), modelled as <see cref="Pine"/> is: short
    /// boughs in many whorls close up the trunk, so it stands a narrow spire with a mass, not crossed cards that read from
    /// above as a column of separate clumps (the 5 October audit, the chase camera), and a club of dense growth at the top.
    /// Bluer and darker than the pines, like the far field's cards (NovaKit.Conifer).
    /// </summary>
    public static MeshAsset Spruce(Look? look, int variant, float height) =>
        Boughs(look, variant, height, reach: 0.15f, whorls: 24, core: look?.Layer("spruce_card") >= 0 ? "spruce_card" : "pine_card",
            tint: new Vector3(0.75f, 0.85f, 0.85f), club: true, name: $"spruce3d-{variant}-{height:0}");

    static MeshAsset Boughs(Look? look, int variant, float height, float reach, int whorls, string core, Vector3? tint, bool club, string name)
    {
        var k = new Kit(look, 200 + variant);
        k.Use("pine_bark", Palette.DeepBrown, 0.6f, 0, tile: 1.5f);
        // (The trunk stops in the leader: it never shows above it as a bare stick.)
        k.Cylinder(Vector3.Zero, new Vector3(0, height * 0.88f, 0), height * 0.015f, 7, caps: false, radiusB: height * 0.004f);
        // A slim core of the far field's crossed cards inside the boughs: from above, or with the sky behind, the gaps
        // between the whorls are foliage, not a stack of separate discs (the 5 October audit, the chase camera).
        k.Use(core, Palette.PineDark, 0.3f, 0, tile: 1);
        k.Baked = 0;
        if (tint is { } t)
            k.Tint = t;
        for (int i = 0; i < 2; i++)
        {
            float a = variant * 0.7f + i * MathF.PI / 2;
            k.Panel(new Vector3(0, height * 0.52f, 0), new Vector3(MathF.Sin(a), 0, MathF.Cos(a)), Vector3.UnitY, height * Math.Min(0.3f, reach * 1.6f), height * 0.86f,
                Vector2.Zero, Vector2.One, twoSided: true);
        }
        k.Use("pine_bough", Palette.PineDark, 0.3f, 0, tile: 1);
        k.Baked = 0;
        if (tint is { } t2)
            k.Tint = t2;
        float Jit(int i, float scale) => (Frac(MathF.Sin(i * 12.9898f + variant * 78.233f) * 43758.5f) - 0.5f) * scale;
        int n = 0;
        void Bough(float y, float length, float droop, float a)
        {
            var dir = new Vector3(MathF.Sin(a), 0, MathF.Cos(a));
            float l = length * (0.85f + Jit(n + 90, 0.3f));
            var p0 = new Vector3(0, y, 0) + dir * height * 0.012f;
            var p1 = p0 + dir * l * 0.55f + Vector3.UnitY * l * 0.06f;
            var p2 = p1 + Vector3.Normalize(dir - Vector3.UnitY * droop) * l * 0.47f;
            // The card's width, rolled about the bough (alternately), so from the side it still has breadth.
            float roll = (n % 2 == 0 ? 1 : -1) * 0.55f;
            var flat = Vector3.Cross(Vector3.UnitY, dir);
            var across = Vector3.Normalize(flat * MathF.Cos(roll) + Vector3.UnitY * MathF.Sin(roll)) * MathF.Max(l * 0.34f, height * 0.035f);
            k.Quad(p0 - across * 0.7f, p0 + across * 0.7f, p1 + across, p1 - across,
                new Vector2(0, 0), new Vector2(0, 1), new Vector2(0.55f, 1), new Vector2(0.55f, 0), twoSided: true);
            k.Quad(p1 - across, p1 + across, p2 + across * 0.8f, p2 - across * 0.8f,
                new Vector2(0.55f, 0), new Vector2(0.55f, 1), new Vector2(1, 1), new Vector2(1, 0), twoSided: true);
        }
        for (int w = 0; w < whorls; w++)
        {
            float f = w / (float)(whorls - 1);
            float y = height * (0.1f + 0.82f * f) + Jit(n, 0.25f);
            // Longest at the bottom, a spike of short ones at the top; the lower ones droop more, weighed down.
            float length = height * reach * MathF.Pow(1 - f, 0.75f) + height * 0.05f;
            int count = f > 0.85f ? 5 : 9;
            float turn = variant * 0.9f + w * 0.73f;
            for (int b = 0; b < count; b++, n++)
                Bough(y, length, 0.3f + 0.35f * (1 - f), turn + b * MathF.Tau / count + Jit(n + 50, 0.5f));
        }
        // Black spruce's club: a knot of short dense boughs at the very top, stood out round the leader.
        if (club)
            for (int b = 0; b < 10; b++, n++)
                Bough(height * (0.82f + 0.012f * b), height * 0.07f, 0.15f, variant * 1.3f + b * 2.4f);
        // The leader: two short crossed boughs pointing up out of the top whorl.
        for (int i = 0; i < 2; i++)
        {
            var side = i == 0 ? Vector3.UnitX : Vector3.UnitZ;
            var b0 = new Vector3(0, height * 0.86f, 0);
            var b1 = new Vector3(0, height * 1.02f, 0);
            k.Quad(b0 - side * 0.35f, b0 + side * 0.35f, b1 + side * 0.12f, b1 - side * 0.12f,
                new Vector2(0.2f, 0), new Vector2(0.2f, 1), new Vector2(1, 1), new Vector2(1, 0), twoSided: true);
        }
        return k.Build(name);
    }

    /// <summary>
    /// A black-forest pine as three crossed cards of the pine texture (alpha-tested, lit from either side) round a faceted
    /// trunk: the far field's (and the old look's). <paramref name="variant"/> mirrors and turns the cards so a stand isn't
    /// one tree repeated.
    /// </summary>
    public static MeshAsset PineCard(Look? look, int variant, float height)
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
        return k.Build($"pinecard-{variant}-{height:0}");
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

    /// <summary>
    /// Tufts of dead grass (or the corruption's brass weeds): five narrow cards fanned round a centre, each leaning out
    /// and a different height, so a tuft is a clump from any side rather than a cross of two flat pictures. Darkened
    /// toward the root: grass in the dark is lit at its tips.
    /// </summary>
    public static MeshAsset Tuft(Look? look, int variant, bool weed)
    {
        var k = new Kit(look, 400 + variant);
        k.Use(weed ? "brass_weed_card" : "grass_card", weed ? Palette.TarnishedBrass : Palette.MuddyOlive, 0.3f, 0, tile: 1);
        k.Baked = 0.6f;
        k.Shade(weed ? 0.7f : 0.55f);
        float w = weed ? 0.7f : 0.8f, h = weed ? 0.75f : 0.55f;
        for (int i = 0; i < 5; i++)
        {
            float a = variant * 1.3f + i * MathF.PI * 2 / 5 + MathF.Sin(i * 3.7f + variant) * 0.3f;
            var dir = new Vector3(MathF.Sin(a), 0, MathF.Cos(a));
            float hi = h * (0.75f + 0.4f * Frac(MathF.Sin(i * 17.3f + variant * 4.1f) * 43758.5f));
            // Leaning out from the clump's middle, its foot a little off centre.
            var side = Vector3.Cross(dir, Vector3.UnitY);
            var up = Vector3.Normalize(Vector3.UnitY + side * 0.28f);
            k.Panel(side * 0.08f + up * (hi / 2 - 0.04f), dir, up, w * (0.8f + 0.2f * (i % 2)), hi,
                i % 2 == 0 ? Vector2.Zero : new Vector2(1, 0), i % 2 == 0 ? new Vector2(1, 1) : new Vector2(0, 1), twoSided: true);
        }
        return k.Build($"tuft-{variant}-{weed}");
    }

    /// <summary>
    /// A boulder: an icosahedron split once (80 facets), each point pushed in or out by a few broad swells round it so
    /// it's lumped and fractured rather than a cut gem; flat-shaded, wet dark rock, squat and sunk into the ground.
    /// </summary>
    public static MeshAsset Rock(Look? look, int variant, float size)
    {
        var k = new Kit(look, 500 + variant);
        k.Use("rock_cliff", Palette.Charcoal, 0.6f, 0.1f, tile: 1.5f);
        float t = (1 + MathF.Sqrt(5)) / 2;
        Vector3[] ico =
        [
            new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0), new(0, -1, t), new(0, 1, t),
            new(0, -1, -t), new(0, 1, -t), new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1),
        ];
        int[] f = [0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8, 3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1];
        // The swells: a few directions of the variant's own, each lifting the side it points to.
        var swells = new (Vector3 Dir, float Amount)[5];
        for (int i = 0; i < swells.Length; i++)
        {
            float h(float x) => Frac(MathF.Sin(x * 12.9898f + variant * 78.233f + i * 37.719f) * 43758.5f);
            swells[i] = (Vector3.Normalize(new Vector3(h(1) - 0.5f, h(2) - 0.5f, h(3) - 0.5f) + new Vector3(0, 0, 1e-4f)), (h(4) - 0.35f) * 0.5f);
        }
        Vector3 Shape(Vector3 dir)
        {
            float r = 0.92f;
            foreach (var (d, a) in swells)
                r += a * MathF.Max(0, Vector3.Dot(dir, d)) * MathF.Max(0, Vector3.Dot(dir, d));
            // A flat fracture across one side: the face it broke away along.
            float cut = Vector3.Dot(dir, swells[0].Dir);
            r = MathF.Min(r, 0.78f / MathF.Max(0.2f, -cut + 1e-3f) * 0.5f + 0.55f);
            var p = dir * r;
            return new Vector3(p.X * size, (p.Y * 0.6f + 0.2f) * size, p.Z * size * 0.85f);
        }
        for (int i = 0; i < f.Length; i += 3)
        {
            var (a, b, c) = (Vector3.Normalize(ico[f[i]]), Vector3.Normalize(ico[f[i + 1]]), Vector3.Normalize(ico[f[i + 2]]));
            var (ab, bc, ca) = (Vector3.Normalize(a + b), Vector3.Normalize(b + c), Vector3.Normalize(c + a));
            var (A, B, C, AB, BC, CA) = (Shape(a), Shape(b), Shape(c), Shape(ab), Shape(bc), Shape(ca));
            k.Tri(A, AB, CA);
            k.Tri(AB, B, BC);
            k.Tri(CA, BC, C);
            k.Tri(AB, BC, CA);
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
