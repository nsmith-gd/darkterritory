using System.Numerics;
using Ballast.Render;

namespace DarkTerritory.Game.Art;

/// <summary>
/// The train's dressing (ARCHITECTURE §8 note 701): the silly things bought from the blacksmith, each a thing on the train to
/// see and point at (docs/design/upgrades.md §8.1, the first of the three tells). Flat colours (no texture by those names),
/// so the paint reads by lamplight. Each centred on where it hangs or sticks, in metres.
/// </summary>
public static class DressKit
{
    static readonly Vector3 White = new(0.92f, 0.9f, 0.86f), Black = new(0.05f, 0.05f, 0.06f), Chrome = new(0.85f, 0.86f, 0.88f);

    /// <summary>
    /// Lettering in the 5×7 pixel font, each row's run of lit pixels one quad: from <paramref name="origin"/> (the first
    /// letter's top left) along <paramref name="right"/> and down <paramref name="down"/> (unit vectors), <paramref name="px"/> a pixel.
    /// </summary>
    public static void Letter(Kit k, string text, Vector3 origin, Vector3 right, Vector3 down, float px)
    {
        var font = BitmapFont.Default;
        var at = origin;
        foreach (char ch in text)
        {
            var g = font.Glyph(ch);
            for (int gy = 0; gy < g.GetLength(0); gy++)
                for (int gx = 0; gx < g.GetLength(1); gx++)
                {
                    if (!g[gy, gx])
                        continue;
                    int run = gx;
                    while (run + 1 < g.GetLength(1) && g[gy, run + 1])
                        run++;
                    var a = at + right * (gx * px) + down * (gy * px);
                    var b = at + right * ((run + 1) * px) + down * (gy * px);
                    k.Quad(a, b, b + down * px, a + down * px);
                    gx = run;
                }
            at += right * (font.Advance * px);
        }
    }

    /// <summary>How wide <paramref name="text"/> letters at <paramref name="px"/> a pixel.</summary>
    public static float Width(string text, float px) => BitmapFont.Default.Measure(text) * px;

    /// <summary>Fuzzy dice on their string, hung from the top (0, 0, 0): two red cubes, white pips, swinging as drawn.</summary>
    public static MeshAsset FuzzyDice(Look? look)
    {
        var k = new Kit(look, 1710);
        k.Use("dress_string", White * 0.8f, 0.9f, 0);
        // On a long enough string to hang in the top of the glass, where the driver sees them swing.
        k.Rod(Vector3.Zero, new Vector3(-0.04f, -0.46f, 0), 0.002f);
        k.Rod(Vector3.Zero, new Vector3(0.04f, -0.5f, 0), 0.002f);
        foreach (var (c, spin) in new[] { (new Vector3(-0.04f, -0.5f, 0), 0.3f), (new Vector3(0.04f, -0.54f, 0), -0.5f) })
        {
            k.Push(Matrix4x4.CreateRotationY(spin) * Matrix4x4.CreateTranslation(c));
            k.Use("dress_fuzz", new Vector3(0.85f, 0.12f, 0.2f), 1, 0);
            k.BevelBox(new Vector3(-0.04f), new Vector3(0.04f), 0.012f);
            k.Use("dress_pip", White, 0.8f, 0);
            k.Disc(new Vector3(0, 0, 0.041f), Vector3.UnitZ, 0.009f, 6);
            foreach (float s in new[] { -0.018f, 0.018f })
                k.Disc(new Vector3(0.041f, s, s), Vector3.UnitX, 0.008f, 6);
            k.Pop();
        }
        return k.Build("dress-dice");
    }

    /// <summary>A pine-tree air freshener on its string, hung from (0, 0, 0): the green card, a tree in three tiers.</summary>
    public static MeshAsset Freshener(Look? look)
    {
        var k = new Kit(look, 1711);
        k.Use("dress_string", White * 0.8f, 0.9f, 0);
        k.Rod(Vector3.Zero, new Vector3(0, -0.38f, 0), 0.0015f);
        k.Use("dress_card", new Vector3(0.12f, 0.5f, 0.18f), 0.9f, 0);
        for (int tier = 0; tier < 3; tier++)
        {
            float top = -0.38f - tier * 0.028f, w = 0.025f + tier * 0.012f;
            k.Tri(new Vector3(0, top, 0), new Vector3(w, top - 0.04f, 0), new Vector3(-w, top - 0.04f, 0));
            k.Tri(new Vector3(0, top, 0), new Vector3(-w, top - 0.04f, 0), new Vector3(w, top - 0.04f, 0));
        }
        k.Box(new Vector3(-0.006f, -0.5f, -0.001f), new Vector3(0.006f, -0.46f, 0.001f));
        return k.Build("dress-freshener");
    }

    /// <summary>
    /// Flames painted down a boiler's side: tongues from the front (−Z) sweeping back, yellow in red, on the plane x = 0
    /// facing +X, <paramref name="length"/> long and <paramref name="height"/> tall, centred on its middle.
    /// </summary>
    public static MeshAsset Flames(Look? look, float length, float height)
    {
        var k = new Kit(look, 1712);
        void Tongues(Vector3 colour, float scale, float lift)
        {
            k.Use("dress_paint", colour, 0.4f, 0.2f);
            // A touch of glow, as fresh enamel catches what light there is: it's there to be seen.
            k.Emissive = 0.3f;
            int n = 5;
            float z0 = -length / 2, yMid = lift;
            // The base: a band along the front, the tongues off it reaching back.
            for (int i = 0; i < n; i++)
            {
                float y = yMid + (i - (n - 1) / 2f) * height / n * scale, reach = length * scale * (0.55f + 0.4f * MathF.Abs(MathF.Sin(i * 1.7f + 0.4f)));
                float half = height / n * 0.55f * scale;
                var a = new Vector3(0, y + half, z0);
                var b = new Vector3(0, y - half, z0);
                var mid = new Vector3(0, y + half * 0.3f, z0 + reach * 0.55f);
                var tip = new Vector3(0, y + half * 0.9f, z0 + reach);
                // Both faces: the paint's seen from whichever side it's mirrored to.
                k.Tri(a, b, mid);
                k.Tri(mid, b, tip);
                k.Tri(a, mid, b);
                k.Tri(mid, tip, b);
            }
            k.Emissive = 0;
        }
        Tongues(new Vector3(0.75f, 0.08f, 0.04f), 1f, 0);
        k.Push(Matrix4x4.CreateTranslation(0.002f, 0, 0));
        Tongues(new Vector3(0.95f, 0.45f, 0.05f), 0.78f, -0.02f);
        k.Pop();
        k.Push(Matrix4x4.CreateTranslation(0.004f, 0, 0));
        Tongues(new Vector3(1f, 0.85f, 0.2f), 0.5f, -0.03f);
        k.Pop();
        return k.Build($"dress-flames-{length:0.0}-{height:0.00}");
    }

    /// <summary>A spinner hubcap, facing +X from (0, 0, 0): a chrome dome and three swept blades, <paramref name="radius"/> across.</summary>
    public static MeshAsset Hubcap(Look? look, float radius)
    {
        var k = new Kit(look, 1713);
        k.Use("dress_chrome", Chrome, 0.05f, 1);
        k.Cylinder(Vector3.Zero, new Vector3(0.02f, 0, 0), radius * 0.35f, 12);
        k.Disc(new Vector3(0.03f, 0, 0), Vector3.UnitX, radius * 0.18f, 10);
        for (int i = 0; i < 3; i++)
        {
            float a = i * MathF.Tau / 3;
            var dir = new Vector3(0, MathF.Cos(a), MathF.Sin(a));
            var side = new Vector3(0, -MathF.Sin(a), MathF.Cos(a));
            var root = new Vector3(0.025f, 0, 0) + dir * radius * 0.3f;
            var tip = new Vector3(0.025f, 0, 0) + dir * radius * 0.95f + side * radius * 0.35f;
            k.Quad(root - side * 0.025f, root + side * 0.025f, tip + side * 0.015f, tip - side * 0.015f, twoSided: true);
        }
        return k.Build($"dress-hubcap-{radius:0.00}");
    }

    /// <summary>The tiny caboose: a red toy caboose 0.45 m long on its wheels, its cupola, a string off its front (−Z) to (0, 0.12, −0.6).</summary>
    public static MeshAsset ToyCaboose(Look? look)
    {
        var k = new Kit(look, 1714);
        k.Use("dress_paint", new Vector3(0.7f, 0.1f, 0.06f), 0.4f, 0.2f);
        k.Box(new Vector3(-0.09f, 0.06f, -0.2f), new Vector3(0.09f, 0.2f, 0.2f));
        k.Box(new Vector3(-0.06f, 0.2f, -0.06f), new Vector3(0.06f, 0.26f, 0.08f));
        k.Use("dress_paint", Black, 0.5f, 0.2f);
        k.Box(new Vector3(-0.1f, 0.2f, -0.22f), new Vector3(0.1f, 0.215f, 0.22f));
        k.Box(new Vector3(-0.07f, 0.26f, -0.07f), new Vector3(0.07f, 0.27f, 0.09f));
        k.Use("dress_window", new Vector3(0.95f, 0.8f, 0.4f), 0.2f, 0);
        k.Emissive = 0.4f;
        foreach (float z in new[] { -0.1f, 0.1f })
        {
            k.Box(new Vector3(0.091f, 0.12f, z - 0.03f), new Vector3(0.093f, 0.16f, z + 0.03f));
            k.Box(new Vector3(-0.093f, 0.12f, z - 0.03f), new Vector3(-0.091f, 0.16f, z + 0.03f));
        }
        k.Emissive = 0;
        k.Use("dress_wheel", Black * 3, 0.5f, 0.6f);
        foreach (float z in new[] { -0.13f, 0.13f })
            foreach (float x in new[] { -0.08f, 0.08f })
                k.Cylinder(new Vector3(x - 0.01f, 0.035f, z), new Vector3(x + 0.01f, 0.035f, z), 0.035f, 8);
        k.Use("dress_string", White * 0.8f, 0.9f, 0);
        k.Rod(new Vector3(0, 0.08f, -0.2f), new Vector3(0, 0.12f, -0.6f), 0.002f);
        return k.Build("dress-caboose");
    }

    /// <summary>A participation ribbon: a blue rosette 0.2 m across facing +Z from (0, 0, 0), a gold middle lettered, two tails.</summary>
    public static MeshAsset Ribbon(Look? look)
    {
        var k = new Kit(look, 1715);
        k.Use("dress_ribbon", new Vector3(0.1f, 0.25f, 0.7f), 0.6f, 0.1f);
        // The tails, hanging, notched.
        foreach (float x in new[] { -0.035f, 0.035f })
        {
            var t = new Vector3(x * 1.6f, -0.2f, 0);
            k.Quad(new Vector3(x - 0.03f, 0, 0), new Vector3(x + 0.03f, 0, 0), t + new Vector3(0.03f, 0, 0), t + new Vector3(-0.03f, 0, 0), twoSided: true);
        }
        // The pleats: a ring of little fins round the disc.
        for (int i = 0; i < 18; i++)
        {
            float a = i * MathF.Tau / 18, b = (i + 0.5f) * MathF.Tau / 18;
            k.Tri(new Vector3(0, 0, 0.004f), new Vector3(MathF.Cos(a) * 0.1f, MathF.Sin(a) * 0.1f, 0.002f), new Vector3(MathF.Cos(b) * 0.11f, MathF.Sin(b) * 0.11f, 0.008f));
        }
        k.Use("dress_gold", new Vector3(0.85f, 0.65f, 0.15f), 0.3f, 0.8f);
        k.Cylinder(new Vector3(0, 0, 0.005f), new Vector3(0, 0, 0.012f), 0.055f, 16);
        k.Use("dress_ink", Black, 0.4f, 0);
        const string word = "TRIED";
        float px = 0.0055f;
        DressKit.Letter(k, word, new Vector3(-Width(word, px) / 2, 3.5f * px, 0.0125f), Vector3.UnitX, -Vector3.UnitY, px);
        return k.Build("dress-ribbon");
    }

    /// <summary>A bumper sticker, facing +Z from (0, 0, 0) (its middle): white, a red rim, the pun in black.</summary>
    public static MeshAsset Sticker(Look? look, string text, float maxWidth = 1)
    {
        var k = new Kit(look, 1716);
        float px = MathF.Min(0.012f, (maxWidth - 0.08f) / Math.Max(1, BitmapFont.Default.Measure(text))), w = Width(text, px) + 0.08f, h = 7 * px + 0.06f;
        k.Use("dress_sticker", new Vector3(0.8f, 0.1f, 0.08f), 0.5f, 0);
        k.Box(new Vector3(-w / 2 - 0.012f, -h / 2 - 0.012f, 0), new Vector3(w / 2 + 0.012f, h / 2 + 0.012f, 0.002f));
        k.Use("dress_sticker", White, 0.5f, 0);
        k.Emissive = 0.1f;
        k.Box(new Vector3(-w / 2, -h / 2, 0.002f), new Vector3(w / 2, h / 2, 0.003f));
        k.Emissive = 0;
        k.Use("dress_ink", Black, 0.4f, 0);
        Letter(k, text, new Vector3(-Width(text, px) / 2, 3.5f * px, 0.0035f), Vector3.UnitX, -Vector3.UnitY, px);
        return k.Build($"dress-sticker-{text}");
    }

    /// <summary>
    /// A towel over a rail at (0, 0, 0) (the rail along Z), hanging both sides of it 0.5 m, its monogram embroidered on the
    /// side facing +X, in <paramref name="colour"/>.
    /// </summary>
    public static MeshAsset Towel(Look? look, string monogram, Vector3 colour)
    {
        var k = new Kit(look, 1717);
        const float half = 0.22f, drop = 0.5f;
        k.Use("dress_towel", colour, 1, 0);
        k.Box(new Vector3(-0.03f, -drop, -half), new Vector3(-0.008f, 0.01f, half));
        k.Box(new Vector3(0.008f, -drop, -half), new Vector3(0.03f, 0.01f, half));
        k.Box(new Vector3(-0.03f, 0.005f, -half), new Vector3(0.03f, 0.025f, half));
        // The monogram in white thread, as big as fits, in up to three lines.
        k.Use("dress_thread", White, 0.8f, 0);
        var lines = Wrap(monogram, 11);
        float px = MathF.Min(0.011f, 2 * half * 0.9f / Math.Max(1, lines.Max(l => BitmapFont.Default.Measure(l))));
        for (int i = 0; i < lines.Count; i++)
        {
            float y = -0.12f - i * 9 * px;
            Letter(k, lines[i], new Vector3(0.031f, y, Width(lines[i], px) / 2), -Vector3.UnitZ, -Vector3.UnitY, px);
        }
        return k.Build($"dress-towel-{monogram}-{colour.X:0.00}");
    }

    /// <summary>The towels' rail: a brass rod <paramref name="length"/> long along Z, on two brackets off the wall at +X.</summary>
    public static MeshAsset TowelRail(Look? look, float length)
    {
        var k = new Kit(look, 1720);
        k.Use("dress_brass", new Vector3(0.75f, 0.58f, 0.25f), 0.3f, 0.9f);
        k.Cylinder(new Vector3(0, 0, -length / 2), new Vector3(0, 0, length / 2), 0.012f, 8);
        foreach (float z in new[] { -length / 2 + 0.05f, length / 2 - 0.05f })
            k.Rod(new Vector3(0, 0, z), new Vector3(0.08f, 0, z), 0.008f);
        return k.Build($"dress-towel-rail-{length:0.0}");
    }

    static List<string> Wrap(string text, int width)
    {
        var lines = new List<string>();
        var line = "";
        foreach (var word in text.Split(' '))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > width)
            {
                lines.Add(line);
                line = word;
            }
            else
                line = line.Length == 0 ? word : line + " " + word;
        }
        if (line.Length > 0)
            lines.Add(line);
        return lines;
    }

    /// <summary>A stuffed moose head on its plaque, facing −Z from (0, 0, 0) (the plaque's back): the muzzle, the ears, the palmate antlers.</summary>
    public static MeshAsset MooseHead(Look? look)
    {
        var k = new Kit(look, 1718);
        k.Use("dress_plaque", new Vector3(0.4f, 0.2f, 0.08f), 0.4f, 0.3f);
        k.Cylinder(new Vector3(0, 0, 0), new Vector3(0, 0, -0.04f), 0.22f, 10);
        k.Use("dress_hide", new Vector3(0.5f, 0.34f, 0.2f), 0.95f, 0);
        // The neck and head reaching out, the long muzzle drooping, the bell under the chin.
        k.Cylinder(new Vector3(0, 0, -0.04f), new Vector3(0, 0.02f, -0.3f), 0.15f, 10, radiusB: 0.12f);
        k.Cylinder(new Vector3(0, 0.02f, -0.3f), new Vector3(0, -0.12f, -0.62f), 0.11f, 10, radiusB: 0.07f);
        k.Cylinder(new Vector3(0, -0.1f, -0.3f), new Vector3(0, -0.32f, -0.32f), 0.03f, 6, radiusB: 0.012f);
        k.Use("dress_ink", Black, 0.3f, 0.4f);
        foreach (float x in new[] { -0.07f, 0.07f })
            k.Disc(new Vector3(x * 1.5f, 0.06f, -0.38f), new Vector3(MathF.Sign(x), 0.2f, -0.6f), 0.018f, 6);
        k.Disc(new Vector3(-0.025f, -0.1f, -0.66f), -Vector3.UnitZ, 0.015f, 6);
        k.Disc(new Vector3(0.025f, -0.1f, -0.66f), -Vector3.UnitZ, 0.015f, 6);
        // The ears and the antlers' palms, spread wide, their tines up.
        k.Use("dress_antler", new Vector3(0.82f, 0.74f, 0.58f), 0.7f, 0);
        foreach (float s in new[] { -1f, 1f })
        {
            var root = new Vector3(s * 0.09f, 0.12f, -0.3f);
            var palm = new Vector3(s * 0.48f, 0.3f, -0.26f);
            k.Rod(root, palm, 0.022f, 5);
            k.Quad(palm + new Vector3(-s * 0.18f, -0.04f, 0), palm + new Vector3(s * 0.12f, -0.02f, 0), palm + new Vector3(s * 0.16f, 0.2f, 0.02f),
                palm + new Vector3(-s * 0.2f, 0.12f, 0.02f), twoSided: true);
            for (int t = 0; t < 4; t++)
            {
                var b = palm + new Vector3(s * (-0.15f + 0.09f * t), 0.12f + 0.02f * t, 0.01f);
                k.Rod(b, b + new Vector3(s * 0.02f, 0.1f, 0), 0.008f, 4);
            }
            k.Use("dress_hide", new Vector3(0.5f, 0.34f, 0.2f), 0.95f, 0);
            k.Quad(new Vector3(s * 0.08f, 0.1f, -0.36f), new Vector3(s * 0.2f, 0.16f, -0.34f), new Vector3(s * 0.2f, 0.2f, -0.32f), new Vector3(s * 0.09f, 0.14f, -0.34f), twoSided: true);
            k.Use("dress_antler", new Vector3(0.82f, 0.74f, 0.58f), 0.7f, 0);
        }
        return k.Build("dress-moose-head");
    }

    /// <summary>The disco ball, centred on (0, 0, 0), 0.32 m across: mirrored facets, its chain up.</summary>
    public static MeshAsset DiscoBall(Look? look)
    {
        var k = new Kit(look, 1719);
        k.Use("dress_chain", Chrome * 0.6f, 0.4f, 1);
        k.Rod(new Vector3(0, 0.16f, 0), new Vector3(0, 0.5f, 0), 0.004f);
        // Faceted: flat-shaded quads round the sphere, every other a shade off, so the turning catches the light.
        const int rings = 8, segments = 14;
        const float r = 0.16f;
        for (int i = 0; i < rings; i++)
            for (int j = 0; j < segments; j++)
            {
                float t0 = MathF.PI * i / rings, t1 = MathF.PI * (i + 1) / rings, p0 = MathF.Tau * j / segments, p1 = MathF.Tau * (j + 1) / segments;
                Vector3 P(float t, float p) => new(r * MathF.Sin(t) * MathF.Cos(p), r * MathF.Cos(t), r * MathF.Sin(t) * MathF.Sin(p));
                k.Use("dress_mirror", (i + j) % 2 == 0 ? Chrome : Chrome * 0.7f, 0.02f, 1);
                k.Quad(P(t0, p0), P(t0, p1), P(t1, p1), P(t1, p0));
            }
        return k.Build("dress-disco-ball");
    }
}
