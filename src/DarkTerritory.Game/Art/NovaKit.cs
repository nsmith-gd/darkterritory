using System.Numerics;
using Ballast.Render;

namespace DarkTerritory.Game.Art;

/// <summary>
/// The country's own pieces (GDD §30's "rail corridor civilization", set in a Nova Scotia that went dark): black
/// spruce and balsam fir, white birch, grey ghost spruce killed where the water came up, granite erratics the ice
/// left lying in the fields, dry stone walls, clapboard saltbox houses in their faded paints, gambrel barns, the white
/// wooden church with its needle steeple, a burying ground of leaning slate, a fish shed on its stilts with its traps
/// stacked by the door. Cooked in their own frame, foot at the origin, front toward −Z.
/// </summary>
public static class NovaKit
{
    /// <summary>The faded house paints: white, barn red, ochre, a sea blue-grey, a dark green.</summary>
    static readonly Vector3[] Paints =
    [
        new(0.78f, 0.76f, 0.70f), new(0.46f, 0.16f, 0.12f), new(0.62f, 0.50f, 0.26f), new(0.36f, 0.42f, 0.46f), new(0.20f, 0.28f, 0.22f),
    ];

    /// <summary>A spruce (<paramref name="width"/> 0.3 of its height) or a fir (0.45): crossed cards round a faceted trunk, darker and bluer than the pines.</summary>
    public static MeshAsset Conifer(Look? look, int variant, float height, float width)
    {
        var k = new Kit(look, 2100 + variant);
        k.Use("pine_bark", Palette.DeepBrown, 0.6f, 0, tile: 1.5f);
        k.Cylinder(Vector3.Zero, new Vector3(0, height * 0.7f, 0), height * 0.018f, 5, caps: false, radiusB: height * 0.006f);
        // A spruce wears the Maritime spire (narrow, ragged, clubbed: maritime-rules.md §5), a fir the broader card.
        bool spire = width < 0.4f && look?.Layer("spruce_card") >= 0;
        k.Use(spire ? "spruce_card" : "pine_card", Palette.PineDark, 0.3f, 0, tile: 1);
        k.Baked = 0;
        k.Tint = spire ? new Vector3(0.75f, 0.85f, 0.85f) : new Vector3(0.62f, 0.72f, 0.74f);
        if (spire)
            width = 0.32f;
        bool flip = variant % 2 == 1;
        for (int i = 0; i < 3; i++)
        {
            float a = variant * 0.8f + i * MathF.PI / 3;
            var n = new Vector3(MathF.Sin(a), 0, MathF.Cos(a));
            k.Panel(new Vector3(0, height * 0.5f, 0), n, Vector3.UnitY, height * width, height,
                flip ? new Vector2(1, 0) : Vector2.Zero, flip ? new Vector2(0, 1) : Vector2.One, twoSided: true);
        }
        // Black spruce's club: a knot of dense growth at the very top (the spire card has its own).
        if (width < 0.4f && !spire)
            for (int i = 0; i < 2; i++)
            {
                float a = variant * 1.3f + i * MathF.PI / 2;
                k.Panel(new Vector3(0, height * 0.9f, 0), new Vector3(MathF.Sin(a), 0, MathF.Cos(a)), Vector3.UnitY, height * 0.16f, height * 0.16f,
                    Vector2.Zero, new Vector2(1, 0.3f), twoSided: true);
            }
        return k.Build($"conifer-{variant}-{height:0}-{width:0.00}");
    }

    /// <summary>A white birch, leafless: a pale trunk leaning a little, the bare crown in crossed cards, paler than the dead pines.</summary>
    public static MeshAsset Birch(Look? look, int variant)
    {
        var k = new Kit(look, 2200 + variant);
        float height = 9 + variant * 1.5f;
        float lean = (variant % 3 - 1) * 0.35f;
        k.Use("plaster_ruin", new Vector3(0.8f, 0.8f, 0.76f), 0.9f, 0.05f, tile: 0.6f);
        k.Tint = new Vector3(1.25f, 1.25f, 1.2f);
        k.Cylinder(Vector3.Zero, new Vector3(lean, height * 0.75f, lean * 0.4f), 0.13f, 6, caps: false, radiusB: 0.06f);
        k.Use("dead_tree_card", Palette.SootBlack, 0.3f, 0, tile: 1);
        k.Baked = 0;
        k.Tint = new Vector3(1.45f, 1.45f, 1.55f);
        for (int i = 0; i < 2; i++)
        {
            float a = variant * 1.1f + i * MathF.PI / 2;
            k.Panel(new Vector3(lean * 0.8f, height * 0.62f, lean * 0.3f), new Vector3(MathF.Sin(a), 0, MathF.Cos(a)), Vector3.UnitY, height * 0.5f, height * 0.62f,
                Vector2.Zero, new Vector2(1, 0.62f), twoSided: true);
        }
        return k.Build($"birch-{variant}");
    }

    /// <summary>A ghost spruce: killed standing where the water came up, grey and bare, its stub branches broken short.</summary>
    public static MeshAsset GhostSpruce(Look? look, int variant)
    {
        var k = new Kit(look, 2300 + variant);
        var rng = new Random(2300 + variant);
        float height = 8 + variant * 2.5f;
        k.Use("wood_grey", Palette.BlueGrey, 0.9f, 0.05f, tile: 1);
        k.Tint = new Vector3(1.15f, 1.15f, 1.2f);
        k.Cylinder(Vector3.Zero, new Vector3(0, height, 0), 0.16f, 5, caps: false, radiusB: 0.02f);
        for (float y = 1.6f; y < height * 0.92f; y += 0.55f + (float)rng.NextDouble() * 0.5f)
        {
            float a = (float)(rng.NextDouble() * MathF.Tau), len = (0.25f + (float)rng.NextDouble() * 0.9f) * (1.1f - y / height);
            var from = new Vector3(0, y, 0);
            k.Rod(from, from + new Vector3(MathF.Sin(a) * len, -len * 0.25f, MathF.Cos(a) * len), 0.025f, 3);
        }
        return k.Build($"ghost-{variant}");
    }

    /// <summary>A length of dry stone wall (8 m, along −Z): fieldstone the ice left, stacked waist-high, fallen in places.</summary>
    public static MeshAsset StoneWall(Look? look, int variant)
    {
        var k = new Kit(look, 2400 + variant);
        var rng = new Random(2400 + variant);
        k.Use("stone_block", Palette.Charcoal, 0.9f, 0.05f, tile: 0.8f);
        for (float z = 0; z > -8; z -= 0.45f)
        {
            float top = rng.NextDouble() < 0.12 ? 0.3f : 0.7f + (float)rng.NextDouble() * 0.3f;
            for (float y = 0; y < top; y += 0.28f)
            {
                float w = 0.3f + (float)rng.NextDouble() * 0.25f, jx = (float)(rng.NextDouble() - 0.5) * 0.12f;
                k.Tint = Vector3.One * (0.7f + 0.5f * (float)rng.NextDouble());
                k.Box(new Vector3(-w + jx, y - 0.05f, z - 0.25f), new Vector3(w + jx, y + 0.26f, z + 0.2f), y == 0 ? Kit.Faces.All & ~Kit.Faces.NegY : Kit.Faces.All);
            }
        }
        return k.Build($"stonewall-{variant}");
    }

    /// <summary>
    /// A saltbox house: two storeys at the front, one at the back, under a long lopsided roof; clapboard in a faded
    /// paint, black windows, a brick chimney. Some have lost their glass and a patch of roof.
    /// </summary>
    public static MeshAsset Saltbox(Look? look, int variant)
    {
        var k = new Kit(look, 2500 + variant);
        var rng = new Random(2500 + variant);
        float w = 7 + (float)rng.NextDouble() * 2.5f, d = 8 + (float)rng.NextDouble() * 2, front = 5.4f, back = 2.6f;
        float ridgeZ = -d * 0.18f, ridge = front + 2.8f;
        var paint = Paints[variant % Paints.Length];
        k.Use("wood_siding", paint, 0.85f, 0.05f, tile: 2);
        // The siding texture is brown timber; the paint over it has to lift it to read as white, red, ochre.
        k.Tint = paint * 2.1f;
        // The walls: the front (−Z) tall, the back low, the ends shaped to the roof.
        k.Quad(new Vector3(-w / 2, -0.4f, -d / 2), new Vector3(w / 2, -0.4f, -d / 2), new Vector3(w / 2, front, -d / 2), new Vector3(-w / 2, front, -d / 2));
        k.Quad(new Vector3(w / 2, -0.4f, d / 2), new Vector3(-w / 2, -0.4f, d / 2), new Vector3(-w / 2, back, d / 2), new Vector3(w / 2, back, d / 2));
        foreach (int side in new[] { -1, 1 })
        {
            float x = side * w / 2;
            var p = new[] { new Vector3(x, -0.4f, -d / 2), new Vector3(x, front, -d / 2), new Vector3(x, ridge, ridgeZ), new Vector3(x, back, d / 2), new Vector3(x, -0.4f, d / 2) };
            // As a fan from the front foot (convex): outward-facing, whichever side.
            for (int i = 1; i + 1 < p.Length; i++)
                if (side > 0)
                    k.Tri(p[0], p[i + 1], p[i], new(p[0].Z, -p[0].Y), new(p[i + 1].Z, -p[i + 1].Y), new(p[i].Z, -p[i].Y));
                else
                    k.Tri(p[0], p[i], p[i + 1], new(-p[0].Z, -p[0].Y), new(-p[i].Z, -p[i].Y), new(-p[i + 1].Z, -p[i + 1].Y));
        }
        k.Use("roof_slate", Palette.Charcoal, 0.9f, 0.15f, tile: 1.4f);
        k.Quad(new Vector3(-w / 2 - 0.3f, front - 0.1f, -d / 2 - 0.35f), new Vector3(w / 2 + 0.3f, front - 0.1f, -d / 2 - 0.35f),
            new Vector3(w / 2 + 0.3f, ridge + 0.1f, ridgeZ), new Vector3(-w / 2 - 0.3f, ridge + 0.1f, ridgeZ), twoSided: true);
        // The long back roof, sagging a little in the middle on the ones that have been empty longest.
        float sag = variant % 4 == 3 ? 0.35f : 0;
        var midL = new Vector3(-w / 2 - 0.3f, (ridge + back) / 2 - sag, (ridgeZ + d / 2 + 0.35f) / 2);
        var midR = midL with { X = w / 2 + 0.3f };
        k.Quad(new Vector3(-w / 2 - 0.3f, ridge + 0.1f, ridgeZ), new Vector3(w / 2 + 0.3f, ridge + 0.1f, ridgeZ), midR, midL, twoSided: true);
        k.Quad(midL, midR, new Vector3(w / 2 + 0.3f, back - 0.1f, d / 2 + 0.35f), new Vector3(-w / 2 - 0.3f, back - 0.1f, d / 2 + 0.35f), twoSided: true);
        k.Use("brick_soot", Palette.RustRed, 0.9f, 0.05f, tile: 1);
        k.Box(new Vector3(-0.45f, ridge - 1.2f, ridgeZ - 0.4f), new Vector3(0.45f, ridge + 1.3f, ridgeZ + 0.4f), Kit.Faces.All & ~Kit.Faces.NegY);
        // Black windows, two rows at the front, a door; the trim a paler paint.
        k.Use("glass_dirty", Palette.SootBlack, 0.4f, 0.4f, tile: 1);
        k.Shade(0.15f);
        foreach (float y in new[] { 1.5f, 4.0f })
            foreach (float x in new[] { -w * 0.3f, w * 0.3f })
                k.Panel(new Vector3(x, y, -d / 2 - 0.02f), -Vector3.UnitZ, Vector3.UnitY, 0.9f, 1.3f, Vector2.Zero, Vector2.One);
        k.Panel(new Vector3(0, 1.0f, -d / 2 - 0.02f), -Vector3.UnitZ, Vector3.UnitY, 1.0f, 2.1f, Vector2.Zero, Vector2.One);
        return k.Build($"saltbox-{variant}");
    }

    /// <summary>A gambrel barn: the roof's double pitch, board-and-batten in faded red or grey, the big doors, a loft door over them.</summary>
    public static MeshAsset Barn(Look? look, int variant)
    {
        var k = new Kit(look, 2600 + variant);
        float w = 10, d = 14, wall = 4.2f;
        var paint = variant % 2 == 0 ? Paints[1] * 1.3f : new Vector3(0.75f, 0.72f, 0.68f);
        // The gambrel's profile across X: the steep lower pitch, then the shallow upper one to the ridge.
        Vector2[] roof = [new(-w / 2, wall), new(-w * 0.34f, wall + 3.2f), new(0, wall + 4.6f), new(w * 0.34f, wall + 3.2f), new(w / 2, wall)];
        k.Use("wood_siding", paint, 0.9f, 0.05f, tile: 2.4f);
        k.Tint = paint * 1.3f;
        k.Box(new Vector3(-w / 2, -0.4f, -d / 2), new Vector3(w / 2, wall, d / 2), Kit.Faces.Sides);
        foreach (float z in new[] { -d / 2, d / 2 })
            for (int i = 1; i + 1 < roof.Length; i++)
            {
                Vector3 P(Vector2 v) => new(v.X, v.Y, z);
                var (a, b, c) = (P(roof[0]), P(roof[i]), P(roof[i + 1]));
                if (z < 0)
                    k.Tri(a, b, c, new(a.X, -a.Y), new(b.X, -b.Y), new(c.X, -c.Y));
                else
                    k.Tri(a, c, b, new(-a.X, -a.Y), new(-c.X, -c.Y), new(-b.X, -b.Y));
            }
        k.Use("roof_slate", Palette.Charcoal, 0.9f, 0.15f, tile: 1.6f);
        for (int i = 0; i + 1 < roof.Length; i++)
        {
            var a = roof[i];
            var b = roof[i + 1];
            k.Quad(new Vector3(a.X, a.Y, -d / 2 - 0.3f), new Vector3(b.X, b.Y, -d / 2 - 0.3f), new Vector3(b.X, b.Y, d / 2 + 0.3f), new Vector3(a.X, a.Y, d / 2 + 0.3f), twoSided: true);
        }
        k.Use("wood_grey", Palette.DeepBrown, 0.9f, 0.05f, tile: 1.5f);
        k.Shade(0.5f);
        k.Panel(new Vector3(0, 1.7f, -d / 2 - 0.03f), -Vector3.UnitZ, Vector3.UnitY, 3.6f, 3.4f, Vector2.Zero, Vector2.One);
        k.Panel(new Vector3(0, wall + 1.6f, -d / 2 - 0.03f), -Vector3.UnitZ, Vector3.UnitY, 1.4f, 1.4f, Vector2.Zero, Vector2.One);
        return k.Build($"barn-{variant}");
    }

    /// <summary>The white wooden church: clapboard nave, a square tower at the front, and its needle steeple; windows black.</summary>
    public static MeshAsset Church(Look? look)
    {
        var k = new Kit(look, 2700);
        var white = Paints[0] * 2.2f;
        k.Use("wood_siding", white, 0.85f, 0.05f, tile: 2);
        k.Tint = white;
        k.Box(new Vector3(-5, -0.5f, -6), new Vector3(5, 7, 12), Kit.Faces.Sides);
        foreach (float z in new[] { -6f, 12f })
        {
            var a = new Vector3(-5, 7, z);
            var b = new Vector3(5, 7, z);
            var c = new Vector3(0, 11.5f, z);
            if (z < 0)
                k.Tri(a, c, b, new(a.X, -a.Y), new(c.X, -c.Y), new(b.X, -b.Y));
            else
                k.Tri(b, c, a, new(-b.X, -b.Y), new(-c.X, -c.Y), new(-a.X, -a.Y));
        }
        k.Box(new Vector3(-2.2f, -0.5f, -10.4f), new Vector3(2.2f, 15, -6), Kit.Faces.All & ~Kit.Faces.NegY);
        k.Box(new Vector3(-1.6f, 15, -9.8f), new Vector3(1.6f, 18, -6.6f), Kit.Faces.All & ~Kit.Faces.NegY);
        k.Lathe(new Vector3(0, 18, -8.2f), [new(2.0f, 0), new(0.05f, 14)], 8, smooth: false, capTop: false);
        k.Use("roof_slate", Palette.Charcoal, 0.9f, 0.15f, tile: 1.5f);
        k.Quad(new Vector3(0, 11.55f, 12.3f), new Vector3(0, 11.55f, -6.3f), new Vector3(-5.4f, 6.8f, -6.3f), new Vector3(-5.4f, 6.8f, 12.3f), twoSided: true);
        k.Quad(new Vector3(0, 11.55f, -6.3f), new Vector3(0, 11.55f, 12.3f), new Vector3(5.4f, 6.8f, 12.3f), new Vector3(5.4f, 6.8f, -6.3f), twoSided: true);
        k.Use("glass_dirty", Palette.SootBlack, 0.4f, 0.4f, tile: 1);
        k.Shade(0.15f);
        for (float z = -4; z < 11; z += 3)
            foreach (int side in new[] { -1, 1 })
                k.Panel(new Vector3(side * 5.01f, 3.8f, z), new Vector3(side, 0, 0), Vector3.UnitY, 1.0f, 3.0f, Vector2.Zero, Vector2.One);
        k.Panel(new Vector3(0, 1.8f, -10.42f), -Vector3.UnitZ, Vector3.UnitY, 1.6f, 3.0f, Vector2.Zero, Vector2.One);
        k.Panel(new Vector3(0, 16.5f, -9.82f), -Vector3.UnitZ, Vector3.UnitY, 1.0f, 1.4f, Vector2.Zero, Vector2.One);
        return k.Build("nova-church");
    }

    /// <summary>A burying ground: rows of thin slate stones leaning every way, and the picket fence round it half down.</summary>
    public static MeshAsset BuryingGround(Look? look, int variant)
    {
        var k = new Kit(look, 2800 + variant);
        var rng = new Random(2800 + variant);
        k.Use("stone_block", Palette.Charcoal, 0.9f, 0.05f, tile: 0.6f);
        for (int row = 0; row < 4; row++)
            for (int i = 0; i < 6; i++)
            {
                if (rng.NextDouble() < 0.15)
                    continue;
                float x = -6 + i * 2.2f + (float)(rng.NextDouble() - 0.5) * 0.5f, z = -4 + row * 2.4f;
                float h = 0.7f + (float)rng.NextDouble() * 0.6f, lean = (float)(rng.NextDouble() - 0.5) * 0.5f;
                k.Tint = new Vector3(0.55f, 0.6f, 0.68f) * (0.8f + 0.4f * (float)rng.NextDouble());
                k.With(Matrix4x4.CreateRotationZ(lean) * Matrix4x4.CreateRotationX((float)(rng.NextDouble() - 0.5) * 0.35f) * Matrix4x4.CreateTranslation(x, 0, z), () =>
                {
                    k.Box(new Vector3(-0.3f, -0.3f, -0.05f), new Vector3(0.3f, h, 0.05f), Kit.Faces.All & ~Kit.Faces.NegY);
                    k.Box(new Vector3(-0.2f, h, -0.05f), new Vector3(0.2f, h + 0.12f, 0.05f), Kit.Faces.All & ~Kit.Faces.NegY);
                });
            }
        k.Use("paint_black", Paints[0] * 0.9f, 0.9f, 0.05f, tile: 1);
        k.Tint = Paints[0] * 0.95f;
        for (float x = -7.5f; x <= 7.5f; x += 0.5f)
            foreach (float z in new[] { -6f, 6f })
                if (rng.NextDouble() > 0.25)
                    k.Box(new Vector3(x - 0.04f, 0, z - 0.02f), new Vector3(x + 0.04f, 1.0f, z + 0.02f), Kit.Faces.All & ~Kit.Faces.NegY);
        return k.Build($"burying-{variant}");
    }

    /// <summary>A fish shed on its stilts: shingle walls grey with salt, a steep roof, and a stack of wooden lobster traps by the door.</summary>
    public static MeshAsset FishShed(Look? look, int variant)
    {
        var k = new Kit(look, 2900 + variant);
        var rng = new Random(2900 + variant);
        float w = 4.5f, d = 6, lift = 1.2f, wall = 3;
        k.Use("wood_grey", Palette.BlueGrey, 0.9f, 0.05f, tile: 1.2f);
        foreach (float x in new[] { -w / 2 + 0.2f, w / 2 - 0.2f })
            foreach (float z in new[] { -d / 2 + 0.2f, 0f, d / 2 - 0.2f })
                k.Box(new Vector3(x - 0.12f, -0.8f, z - 0.12f), new Vector3(x + 0.12f, lift, z + 0.12f), Kit.Faces.Sides);
        k.Box(new Vector3(-w / 2, lift, -d / 2), new Vector3(w / 2, lift + wall, d / 2), Kit.Faces.All);
        foreach (float z in new[] { -d / 2, d / 2 })
        {
            var a = new Vector3(-w / 2, lift + wall, z);
            var b = new Vector3(w / 2, lift + wall, z);
            var c = new Vector3(0, lift + wall + 2.4f, z);
            if (z < 0)
                k.Tri(a, c, b, new(a.X, -a.Y), new(c.X, -c.Y), new(b.X, -b.Y));
            else
                k.Tri(b, c, a, new(-b.X, -b.Y), new(-c.X, -c.Y), new(-a.X, -a.Y));
        }
        k.Use("roof_slate", Palette.Charcoal, 0.9f, 0.15f, tile: 1.2f);
        float top = lift + wall + 2.4f;
        k.Quad(new Vector3(0, top + 0.05f, d / 2 + 0.3f), new Vector3(0, top + 0.05f, -d / 2 - 0.3f), new Vector3(-w / 2 - 0.3f, lift + wall - 0.1f, -d / 2 - 0.3f), new Vector3(-w / 2 - 0.3f, lift + wall - 0.1f, d / 2 + 0.3f), twoSided: true);
        k.Quad(new Vector3(0, top + 0.05f, -d / 2 - 0.3f), new Vector3(0, top + 0.05f, d / 2 + 0.3f), new Vector3(w / 2 + 0.3f, lift + wall - 0.1f, d / 2 + 0.3f), new Vector3(w / 2 + 0.3f, lift + wall - 0.1f, -d / 2 - 0.3f), twoSided: true);
        k.Use("glass_dirty", Palette.SootBlack, 0.4f, 0.4f, tile: 1);
        k.Shade(0.2f);
        k.Panel(new Vector3(0, lift + 1.1f, -d / 2 - 0.02f), -Vector3.UnitZ, Vector3.UnitY, 1.0f, 2.0f, Vector2.Zero, Vector2.One);
        // The traps: slatted boxes stacked by the door, a few fallen.
        k.Use("wood_crate", Palette.DeepBrown, 0.9f, 0.05f, tile: 0.8f);
        for (int i = 0; i < 9; i++)
        {
            int col = i % 3, lvl = i / 3;
            float x = w / 2 + 0.7f + col * 0.95f, y = -0.2f + lvl * 0.5f, z = -d / 2 + 0.6f + (float)(rng.NextDouble() - 0.5) * 0.2f;
            if (lvl > 0 && rng.NextDouble() < 0.3)
                continue;
            k.Box(new Vector3(x - 0.45f, y, z - 0.3f), new Vector3(x + 0.45f, y + 0.48f, z + 0.3f), Kit.Faces.All & ~Kit.Faces.NegY);
        }
        return k.Build($"fishshed-{variant}");
    }

    /// <summary>
    /// Speckled alder: the thicket every Maritime right-of-way grows up in, a clump of thin grey stems leaning out from a
    /// root, the late leaves dark olive-brown in crossed cards. Head-high to twice that; it closes the line in.
    /// </summary>
    public static MeshAsset Alder(Look? look, int variant)
    {
        var k = new Kit(look, 3000 + variant);
        var rng = new Random(3000 + variant);
        int stems = 4 + variant % 3;
        k.Use("wood_grey", Palette.BlueGrey, 0.9f, 0.05f, tile: 0.8f);
        k.Tint = new Vector3(0.75f, 0.74f, 0.7f);
        var tops = new List<Vector3>();
        for (int i = 0; i < stems; i++)
        {
            float a = i * MathF.Tau / stems + (float)rng.NextDouble() * 0.6f, lean = 0.6f + (float)rng.NextDouble() * 1.2f, h = 2.6f + (float)rng.NextDouble() * 1.8f;
            var top = new Vector3(MathF.Sin(a) * lean, h, MathF.Cos(a) * lean);
            k.Rod(new Vector3(MathF.Sin(a) * 0.15f, 0, MathF.Cos(a) * 0.15f), top, 0.035f, 3);
            tops.Add(top);
        }
        k.Use("pine_card", Palette.PineDark, 0.3f, 0, tile: 1);
        k.Baked = 0;
        k.Tint = new Vector3(0.95f, 0.85f, 0.55f);
        foreach (var top in tops)
            for (int j = 0; j < 2; j++)
            {
                float a = (float)rng.NextDouble() * MathF.PI;
                k.Panel(top - new Vector3(0, 0.7f, 0), new Vector3(MathF.Sin(a), 0, MathF.Cos(a)), Vector3.UnitY, 1.8f, 1.9f,
                    new Vector2(0, 0.35f), new Vector2(1, 1), twoSided: true);
            }
        return k.Build($"alder-{variant}");
    }

    /// <summary>
    /// A lighthouse on a headland (maritime-rules.md §6): the square, tapered wooden tower of the small lights, white
    /// shingles gone grey, an iron lantern on top with its glass black. Nobody's kept it.
    /// </summary>
    public static MeshAsset Lighthouse(Look? look, int variant)
    {
        var k = new Kit(look, 3100 + variant);
        float h = 10 + variant * 2.5f, b = 2.6f, t = 1.5f;
        k.Use("wood_siding", new Vector3(0.8f, 0.8f, 0.76f), 0.9f, 0.05f, tile: 1.2f);
        k.Tint = new Vector3(1.3f, 1.3f, 1.25f);
        var lo = new[] { new Vector3(-b, 0, -b), new Vector3(b, 0, -b), new Vector3(b, 0, b), new Vector3(-b, 0, b) };
        var hi = new[] { new Vector3(-t, h, -t), new Vector3(t, h, -t), new Vector3(t, h, t), new Vector3(-t, h, t) };
        for (int i = 0; i < 4; i++)
        {
            int j = (i + 1) % 4;
            k.Quad(lo[j], lo[i], hi[i], hi[j]);
        }
        k.Box(new Vector3(-t - 0.4f, h, -t - 0.4f), new Vector3(t + 0.4f, h + 0.3f, t + 0.4f), Kit.Faces.All);
        k.Use("iron_plate", new Vector3(0.45f, 0.12f, 0.1f), 0.7f, 0.3f, tile: 1);
        k.Tint = new Vector3(1.4f, 0.7f, 0.6f);
        k.Cylinder(new Vector3(0, h + 0.3f, 0), new Vector3(0, h + 2.1f, 0), 1.05f, 8, caps: false);
        k.Cylinder(new Vector3(0, h + 2.1f, 0), new Vector3(0, h + 3.1f, 0), 1.2f, 8, caps: true, radiusB: 0.15f);
        k.Use("glass_dirty", Palette.SootBlack, 0.4f, 0.6f, tile: 1);
        k.Tint = Vector3.One;
        k.Cylinder(new Vector3(0, h + 0.5f, 0), new Vector3(0, h + 1.9f, 0), 1.08f, 8, caps: false);
        // A door at the foot, and a window a storey up.
        k.Shade(0.2f);
        k.Panel(new Vector3(0, 1.0f, -b - 0.02f), -Vector3.UnitZ, Vector3.UnitY, 0.9f, 2.0f, Vector2.Zero, Vector2.One);
        k.Panel(new Vector3(0, h * 0.55f, -(b + (t - b) * 0.55f) - 0.05f), -Vector3.UnitZ, Vector3.UnitY, 0.6f, 0.9f, Vector2.Zero, Vector2.One);
        return k.Build($"lighthouse-{variant}");
    }

    /// <summary>
    /// A crib wharf (maritime-rules.md §6): log cribs filled with rock, a plank deck across them, run out from the shore
    /// into the water along +Z's back (−Z out), some of its planks gone.
    /// </summary>
    public static MeshAsset Wharf(Look? look, int variant)
    {
        var k = new Kit(look, 3200 + variant);
        var rng = new Random(3200 + variant);
        float len = 16 + variant * 6, w = 4, deck = 1.6f;
        k.Use("wood_sleeper", Palette.DeepBrown, 0.9f, 0.05f, tile: 1);
        for (float z = 0; z > -len; z -= 5)
            k.Box(new Vector3(-w / 2, -4, z - 3.2f), new Vector3(w / 2, deck - 0.2f, z), Kit.Faces.Sides);
        k.Use("wood_floor", Palette.DeepBrown, 0.9f, 0.05f, tile: 1);
        k.Tint = new Vector3(0.8f, 0.8f, 0.78f);
        for (float z = 0.2f; z > -len; z -= 0.35f)
            if (rng.NextDouble() > 0.1)
                k.Box(new Vector3(-w / 2 - 0.2f, deck - 0.2f, z - 0.3f), new Vector3(w / 2 + 0.2f, deck, z), Kit.Faces.All & ~Kit.Faces.NegY);
        // Lobster traps stacked on the deck, slatted boxes three and four high, some fallen.
        k.Use("wood_crate", Palette.DeepBrown, 0.9f, 0.05f, tile: 0.8f);
        k.Tint = new Vector3(0.85f);
        for (int i = 0; i < 14; i++)
        {
            float z = -2 - (i / 4) * 1.1f, x = -1.2f + (i % 2) * 1.0f, y = deck + ((i % 4) / 2) * 0.5f;
            if (rng.NextDouble() < 0.2)
                continue;
            k.Box(new Vector3(x - 0.45f, y, z - 0.3f), new Vector3(x + 0.45f, y + 0.48f, z + 0.3f), Kit.Faces.All & ~Kit.Faces.NegY);
        }
        return k.Build($"wharf-{variant}");
    }

    /// <summary>
    /// A stretch of stand edge (maritime-rules.md §5, "the spruce wall"): a long two-sided card of packed spruce spires
    /// facing the line, <paramref name="width"/> along it and <paramref name="height"/> tall, set behind the single
    /// trees so the forest reads as a mass with a serrated top, not trees dotted on open ground.
    /// </summary>
    public static MeshAsset Treeline(Look? look, int variant, float width, float height)
    {
        var k = new Kit(look, 3300 + variant);
        k.Use("treeline_card", Palette.PineDark, 0.3f, 0, tile: 1);
        k.Baked = 0;
        k.Tint = new Vector3(0.75f, 0.85f, 0.85f);
        float u0 = variant * 0.37f % 1;
        // Its foot runs 3 m into the ground (the solid stand at the card's bottom), so it never floats on a slope.
        k.Panel(new Vector3(0, height / 2 - 3f, 0), Vector3.UnitX, Vector3.UnitY, width, height + 3, new Vector2(u0, 0), new Vector2(u0 + width / 30, 1), twoSided: true);
        return k.Build($"treeline-{variant}-{width:0}-{height:0}");
    }

    /// <summary>A level crossing's crossbuck: two white boards in an X on a grey post, the paint gone to the wood in places.</summary>
    public static MeshAsset Crossbuck(Look? look)
    {
        var k = new Kit(look, 3400);
        k.Use("wood_grey", Palette.BlueGrey, 0.9f, 0.05f, tile: 1);
        k.Box(new Vector3(-0.07f, 0, -0.07f), new Vector3(0.07f, 3.6f, 0.07f), Kit.Faces.All & ~Kit.Faces.NegY);
        k.Use("wood_siding", new Vector3(0.8f, 0.8f, 0.76f), 0.9f, 0.05f, tile: 1);
        k.Tint = new Vector3(1.4f, 1.4f, 1.35f);
        foreach (float a in new[] { 0.72f, -0.72f })
        {
            var c = new Vector3(0, 3.05f, -0.1f);
            var along = new Vector3(MathF.Cos(a), MathF.Sin(a), 0);
            k.Panel(c, -Vector3.UnitZ, Vector3.Cross(Vector3.UnitZ, along), 0.22f, 1.35f, Vector2.Zero, new Vector2(0.25f, 1.4f), twoSided: true);
        }
        return k.Build("crossbuck");
    }

    /// <summary>
    /// A car left where it stopped (maritime-rules.md §2.2): a rounded 1940s sedan, its paint gone to rust and primer,
    /// glass out, sitting low on flat tyres.
    /// </summary>
    public static MeshAsset Car(Look? look, int variant)
    {
        var k = new Kit(look, 3500 + variant);
        var paint = Paints[(variant + 1) % Paints.Length] * 0.7f;
        k.Use("rust_heavy", paint, 0.8f, 0.2f, tile: 1.2f);
        k.Tint = paint * 1.8f;
        // Body, cabin and the long hood; wheel arches as dark boxes under it.
        k.Box(new Vector3(-0.9f, 0.35f, -2.5f), new Vector3(0.9f, 1.05f, 2.4f), Kit.Faces.All);
        k.Box(new Vector3(-0.8f, 1.05f, -0.9f), new Vector3(0.8f, 1.6f, 1.1f), Kit.Faces.All & ~Kit.Faces.NegY);
        k.Box(new Vector3(-0.95f, 0.5f, -2.2f), new Vector3(0.95f, 0.95f, -1.4f), Kit.Faces.Sides);
        k.Box(new Vector3(-0.95f, 0.5f, 1.4f), new Vector3(0.95f, 0.95f, 2.1f), Kit.Faces.Sides);
        k.Use("glass_dirty", Palette.SootBlack, 0.4f, 0.4f, tile: 1);
        k.Tint = Vector3.One * 0.4f;
        foreach (float x in new[] { -0.81f, 0.81f })
            k.Panel(new Vector3(x, 1.33f, 0.1f), new Vector3(x, 0, 0), Vector3.UnitY, 1.7f, 0.42f, Vector2.Zero, Vector2.One);
        k.Use("iron_plate", Palette.SootBlack, 0.8f, 0.2f, tile: 1);
        k.Tint = Vector3.One * 0.5f;
        foreach (float x in new[] { -0.8f, 0.8f })
            foreach (float z in new[] { -1.8f, 1.75f })
                k.Cylinder(new Vector3(x - 0.12f, 0.3f, z), new Vector3(x + 0.12f, 0.3f, z), 0.33f, 8, caps: true);
        return k.Build($"car-{variant}");
    }

    /// <summary>A woodpile by a house: split stove wood stacked between two end posts, a sheet of rusted roofing on top.</summary>
    public static MeshAsset Woodpile(Look? look, int variant)
    {
        var k = new Kit(look, 3600 + variant);
        var rng = new Random(3600 + variant);
        k.Use("pine_bark", Palette.DeepBrown, 0.9f, 0.05f, tile: 0.6f);
        float len = 3 + variant;
        for (float y = 0; y < 1.3f; y += 0.18f)
            for (float z = -len / 2; z < len / 2; z += 0.2f)
            {
                if (y > 0.9f && rng.NextDouble() < 0.3)
                    continue;
                k.Tint = Vector3.One * (0.7f + 0.5f * (float)rng.NextDouble());
                k.Box(new Vector3(-0.4f, y, z), new Vector3(0.4f, y + 0.17f, z + 0.18f), Kit.Faces.PosX | Kit.Faces.NegX | Kit.Faces.PosY);
            }
        k.Use("rust_heavy", Palette.RustRed, 0.9f, 0.2f, tile: 1);
        k.Tint = Vector3.One;
        k.Quad(new Vector3(-0.6f, 1.45f, -len / 2 - 0.2f), new Vector3(-0.6f, 1.45f, len / 2 + 0.2f), new Vector3(0.6f, 1.35f, len / 2 + 0.2f), new Vector3(0.6f, 1.35f, -len / 2 - 0.2f), twoSided: true);
        return k.Build($"woodpile-{variant}");
    }
}
