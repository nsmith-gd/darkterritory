using System.Numerics;
using Ballast.Render;

namespace DarkTerritory.Game.Art;

/// <summary>
/// A walled town's green and what its people put up to make the world bearable (the director, 8 Oct 2026: "parks, signs of
/// governance, signs of culture, statues, things that tell the story of a people walled in for fear of the outside world";
/// queue #90, note 353): the statue's plinth (its figure is the survivors', cast: <see cref="StatueClip"/>), the wall of
/// names, the bandstand with a sky painted under its roof, a raised bed under lamps, a tin tree or the old elm hung with
/// paper leaves, the flag, the council's laws on their board, the day painted on the walls; and the green itself, its grass
/// and its gravel paths. Each in its fixture's frame: its front toward −Z, the size the Sim gives it
/// (<see cref="Sim.Towns.TownFixtures.Size"/>).
/// </summary>
public static class CivicKit
{
    /// <summary>The civic kinds this kit draws.</summary>
    public static bool Draws(string kind) => kind is "statue" or "memorial" or "bandstand" or "garden" or "tree" or "flag" or "laws" or "mural";

    /// <summary>
    /// Which of a kind's looks a piece takes, by its name (towns.json civic titles): the tin tree or the elm, the lamp
    /// garden or the tin one, the sunrise, the window or the day; a statue's figure.
    /// </summary>
    public static int Variant(string kind, string name) => kind switch
    {
        "tree" => name.Contains("elm", StringComparison.OrdinalIgnoreCase) ? 1 : 0,
        "garden" => name.Contains("tin", StringComparison.OrdinalIgnoreCase) ? 1 : 0,
        // Dave's (note 528), signed with his D: his valley, his sea, his lake.
        "mural" when name.Contains("D.)", StringComparison.Ordinal) => name.Contains("sea", StringComparison.OrdinalIgnoreCase) ? 4
            : name.Contains("lake", StringComparison.OrdinalIgnoreCase) ? 5 : 3,
        "mural" => name.Contains("window", StringComparison.OrdinalIgnoreCase) ? 1 : name.Contains("sunrise", StringComparison.OrdinalIgnoreCase) ? 0 : 2,
        "statue" => name.Contains("Lamp", StringComparison.Ordinal) ? 1 : name.Contains("Child", StringComparison.Ordinal) ? 2
            : name.Contains("Railway", StringComparison.Ordinal) ? 3 : 0,
        _ => 0,
    };

    /// <summary>A statue's figure (the survivors', cast in bronze or stone): the clip it's frozen in, how big, which way round,
    /// and whether it's stone.</summary>
    public static (string Clip, float Scale, bool TurnedAway, bool Stone) StatueClip(int variant) => variant switch
    {
        1 => ("lantern", 1.05f, false, true),
        2 => ("idle", 0.72f, false, false),
        3 => ("idle", 1.0f, true, false),
        _ => ("wave", 1.1f, false, false),
    };

    /// <summary>The top of a statue's plinth (m), where its figure's feet go.</summary>
    public const float PlinthTop = 1.4f;

    public static MeshAsset Piece(Look? look, string kind, int variant)
    {
        var k = new Kit(look, 3100 + kind.Sum(c => c) % 89 + variant * 7);
        var (hs, hd, ht) = Sim.Towns.TownFixtures.Size(kind);
        float x = (float)hs, z = (float)hd, h = (float)ht;
        switch (kind)
        {
            case "statue":
                Stone(k);
                k.BevelBox(new Vector3(-x, 0, -z), new Vector3(x, 0.3f, z), 0.04f);
                k.BevelBox(new Vector3(-x + 0.15f, 0.3f, -z + 0.15f), new Vector3(x - 0.15f, PlinthTop - 0.12f, z - 0.15f), 0.03f);
                k.BevelBox(new Vector3(-x + 0.05f, PlinthTop - 0.12f, -z + 0.05f), new Vector3(x - 0.05f, PlinthTop, z - 0.05f), 0.03f);
                // Its plate, brass, on the front.
                k.Use("brass", Palette.TarnishedBrass, 0.4f, 0.6f);
                k.Box(new Vector3(-0.32f, 0.6f, -z + 0.12f), new Vector3(0.32f, 0.9f, -z + 0.15f));
                break;
            case "memorial":
                // Slate on a granite footing, the names in rows (a dark cut each), the blank row along the bottom.
                Stone(k);
                k.BevelBox(new Vector3(-x, 0, -z), new Vector3(x, 0.4f, z), 0.04f);
                k.Use("roof_slate", Palette.Charcoal, 0.6f, 0.25f, tile: 1);
                k.Tint = new Vector3(1.5f, 1.55f, 1.65f);
                k.Box(new Vector3(-x + 0.1f, 0.4f, -z + 0.12f), new Vector3(x - 0.1f, h - 0.15f, z - 0.05f));
                Stone(k);
                k.BevelBox(new Vector3(-x - 0.05f, h - 0.15f, -z), new Vector3(x + 0.05f, h, z), 0.03f);
                // The names, cut pale into the slate, in rows; the heading's brass plate over them.
                k.Use("paper_form", Palette.BoardEnamel, 0.7f, 0, tile: 1);
                k.Tint = new Vector3(0.75f);
                for (float y = h - 0.55f; y > 0.8f; y -= 0.1f)
                    for (float lx = -x + 0.3f; lx < x - 0.4f; lx += 0.62f)
                    {
                        float len = 0.28f + (MathF.Sin(lx * 7.1f + y * 13.3f) * 0.5f + 0.5f) * 0.22f;
                        k.Box(new Vector3(lx, y, -z + 0.105f), new Vector3(lx + len, y + 0.035f, -z + 0.118f));
                    }
                k.Use("brass", Palette.TarnishedBrass, 0.4f, 0.6f);
                k.Box(new Vector3(-1.1f, h - 0.42f, -z + 0.08f), new Vector3(1.1f, h - 0.2f, -z + 0.12f));
                // The iron rings beside some of the names.
                k.Use("rust_heavy", Palette.IronGrey, 0.8f, 0.4f);
                for (int i = 0; i < 9; i++)
                {
                    float rx = -x + 0.5f + (i * 2.37f % (2 * x - 1)), ry = h - 0.45f - (i * 0.53f % (h - 1.4f));
                    k.Cylinder(new Vector3(rx, ry, -z + 0.1f), new Vector3(rx, ry, -z + 0.07f), 0.045f, 8);
                }
                // The blank slates, paler, along the foot.
                k.Use("roof_slate", Palette.BlueGrey, 0.5f, 0.2f, tile: 1);
                k.Tint = new Vector3(1.4f);
                for (float lx = -x + 0.2f; lx < x - 0.5f; lx += 0.7f)
                    k.Box(new Vector3(lx, 0.45f, -z + 0.1f), new Vector3(lx + 0.6f, 0.7f, -z + 0.13f));
                break;
            case "bandstand":
                Bandstand(k, x, h);
                break;
            case "garden":
                Garden(k, x, z, variant);
                break;
            case "tree":
                Tree(k, variant);
                break;
            case "flag":
                Iron(k);
                k.Cylinder(Vector3.Zero, new Vector3(0, h, 0), 0.07f, 8, radiusB: 0.045f);
                k.Use("brass", Palette.TarnishedBrass, 0.4f, 0.6f);
                k.Lathe(new Vector3(0, h, 0), [new(0.09f, 0), new(0.09f, 0.04f), new(0, 0.14f)], 8);
                // The flag, hung a little off the wind (there's none in here): the town's blue with its lamp, patched.
                k.Use("scarf", Palette.BlueGrey, 0.6f, 0, tile: 1);
                k.Tint = new Vector3(0.45f, 0.6f, 1.1f);
                var top = new Vector3(0.07f, h - 0.2f, 0);
                k.Quad(top, top + new Vector3(1.6f, -0.12f, 0.1f), top + new Vector3(1.55f, -1.15f, 0.12f), top + new Vector3(0, -1.0f, 0), twoSided: true);
                k.Use("paint_oxide", Palette.LampAmber, 0.5f, 0.1f, tile: 1);
                k.Tint = new Vector3(1.6f, 1.2f, 0.5f);
                k.Quad(top + new Vector3(0.6f, -0.35f, 0.04f), top + new Vector3(0.95f, -0.37f, 0.06f), top + new Vector3(0.93f, -0.75f, 0.07f), top + new Vector3(0.6f, -0.7f, 0.05f), twoSided: true);
                break;
            case "laws":
                // The council's board: two posts, a roof, one long sheet of ordinances with the council's red seal.
                Timber(k);
                foreach (float lx in (float[])[-x + 0.07f, x - 0.07f])
                    k.Box(new Vector3(lx - 0.06f, 0, -0.06f), new Vector3(lx + 0.06f, h, 0.06f));
                k.Box(new Vector3(-x, 0.9f, -0.04f), new Vector3(x, h - 0.15f, 0.04f));
                k.Use("roof_slate", Palette.Charcoal, 0.8f, 0.2f, tile: 1);
                k.Quad(new Vector3(-x - 0.12f, h + 0.05f, 0.1f), new Vector3(x + 0.12f, h + 0.05f, 0.1f), new Vector3(x + 0.12f, h - 0.15f, -0.3f), new Vector3(-x - 0.12f, h - 0.15f, -0.3f), twoSided: true);
                k.Use("paper_form", Palette.BoardEnamel, 0.6f, 0, tile: 1);
                k.Emissive = 0.15f;
                k.Panel(new Vector3(0, 1.4f, -0.05f), -Vector3.UnitZ, Vector3.UnitY, 1.3f, 0.85f, Vector2.Zero, Vector2.One);
                k.Emissive = 0;
                k.Use("paint_black", Palette.SootBlack, 0.8f, 0, tile: 1);
                for (float y = 1.72f; y > 1.05f; y -= 0.06f)
                    k.Box(new Vector3(-0.55f, y, -0.06f), new Vector3(0.55f - (y * 37 % 1) * 0.3f, y + 0.015f, -0.055f));
                k.Use("paint_oxide", Palette.SignalRed, 0.5f, 0.2f, tile: 1);
                k.Cylinder(new Vector3(0.45f, 1.05f, -0.06f), new Vector3(0.45f, 1.05f, -0.075f), 0.06f, 10);
                break;
            case "mural":
                Mural(k, x, h, variant);
                break;
        }
        return k.Build($"civic-{kind}-{variant}");
    }

    /// <summary>The bandstand: an octagon of granite and boards, eight posts, the roof, a sky painted on the roof's underside.</summary>
    static void Bandstand(Kit k, float r, float floor)
    {
        Stone(k);
        k.Lathe(Vector3.Zero, [new(r, 0), new(r, floor - 0.08f)], 8, smooth: false);
        Timber(k);
        k.Lathe(new Vector3(0, floor - 0.08f, 0), [new(r + 0.1f, 0), new(r + 0.1f, 0.08f)], 8, smooth: false);
        // Steps up to it on the front.
        Stone(k);
        for (int i = 0; i < 3; i++)
            k.Box(new Vector3(-0.8f, 0, -r - 0.9f + i * 0.3f), new Vector3(0.8f, floor * (i + 1) / 3.5f, -r - 0.6f + i * 0.3f + 0.3f));
        Paint(k);
        float roof = floor + 2.6f;
        for (int i = 0; i < 8; i++)
        {
            float a = (i + 0.5f) * MathF.Tau / 8;
            var at = new Vector3(MathF.Cos(a) * (r - 0.15f), floor, -MathF.Sin(a) * (r - 0.15f));
            k.Cylinder(at, at with { Y = roof }, 0.07f, 6);
            // A rail between the posts but at the steps.
            if (i != 5 && i != 6)
            {
                float b = (i + 1.5f) * MathF.Tau / 8;
                var next = new Vector3(MathF.Cos(b) * (r - 0.15f), floor, -MathF.Sin(b) * (r - 0.15f));
                k.Rod(at + Vector3.UnitY * 0.85f, next + Vector3.UnitY * 0.85f, 0.03f);
            }
        }
        k.Use("roof_slate", Palette.Charcoal, 0.7f, 0.2f, tile: 1);
        k.Lathe(new Vector3(0, roof, 0), [new(r + 0.5f, 0), new(r + 0.45f, 0.15f), new(0.1f, 1.3f), new(0, 1.35f)], 8, smooth: false);
        // The sky under the roof: blue, a pale sun at its middle (lit faintly by the lamps below).
        k.Use("paint_olive", new Vector3(0.3f, 0.45f, 0.7f), 0.4f, 0, tile: 1);
        k.Tint = new Vector3(0.55f, 0.85f, 1.6f);
        k.Disc(new Vector3(0, roof - 0.01f, 0), -Vector3.UnitY, r + 0.4f, 8);
        k.Use("paint_oxide", Palette.LampAmber, 0.3f, 0, tile: 1);
        k.Tint = new Vector3(1.8f, 1.6f, 0.9f);
        k.Emissive = 0.25f;
        k.Disc(new Vector3(0, roof - 0.02f, 0), -Vector3.UnitY, 0.45f, 12);
        k.Emissive = 0;
    }

    /// <summary>A raised bed under its two lamps (the lamp garden: rows of greens and glass cloches; the tin garden: painted tin flowers).</summary>
    static void Garden(Kit k, float x, float z, int variant)
    {
        Timber(k);
        k.Box(new Vector3(-x, 0, -z), new Vector3(x, 0.45f, z), Kit.Faces.Sides);
        k.Use("ground_mud", Palette.DeepBrown, 0.9f, 0, tile: 1);
        k.Box(new Vector3(-x + 0.05f, 0.35f, -z + 0.05f), new Vector3(x - 0.05f, 0.4f, z - 0.05f), Kit.Faces.PosY);
        for (float lx = -x + 0.25f; lx < x - 0.15f; lx += 0.3f)
            for (float lz = -z + 0.2f; lz < z - 0.1f; lz += 0.35f)
            {
                float jitter = MathF.Sin(lx * 9.1f + lz * 5.3f);
                if (variant == 1)
                {
                    // Tin flowers: a stalk and a painted disc.
                    k.Use("rust_heavy", Palette.IronGrey, 0.6f, 0.3f);
                    k.Rod(new Vector3(lx, 0.4f, lz), new Vector3(lx, 0.62f + jitter * 0.05f, lz), 0.006f);
                    k.Use("paint_oxide", jitter > 0.3f ? Palette.SignalRed : jitter > -0.3f ? Palette.LampAmber : Palette.BoardEnamel, 0.5f, 0.3f, tile: 1);
                    k.Disc(new Vector3(lx, 0.63f + jitter * 0.05f, lz - 0.01f), Vector3.Normalize(new Vector3(0, 0.4f, -1)), 0.045f, 6);
                }
                else if ((int)((lx + 5) * 3.3f) % 3 == 0)
                {
                    // A glass cloche over a seedling.
                    k.Use("glass_dirty", Palette.BlueGrey, 0.3f, 0.7f, tile: 1);
                    k.Tint = new Vector3(2.2f, 2.3f, 2.4f);
                    k.Lathe(new Vector3(lx, 0.4f, lz), [new(0.09f, 0), new(0.085f, 0.1f), new(0.05f, 0.17f), new(0, 0.19f)], 8);
                }
                else
                {
                    k.Use("pine_bough", Palette.PineDark, 0.7f, 0, tile: 1);
                    k.Tint = new Vector3(0.7f, 1.0f, 0.6f);
                    k.Lathe(new Vector3(lx, 0.4f, lz), [new(0.1f + jitter * 0.02f, 0), new(0.08f, 0.1f), new(0, 0.16f + jitter * 0.04f)], 5, smooth: false);
                }
            }
        // The lamps on their poles at either end, hung over the bed (WorldArt lights them).
        Iron(k);
        foreach (float lx in (float[])[-x - 0.1f, x + 0.1f])
        {
            k.Cylinder(new Vector3(lx, 0, 0), new Vector3(lx, LampHigh, 0), 0.04f, 6);
            k.Rod(new Vector3(lx, LampHigh, 0), new Vector3(lx * 0.6f, LampHigh, 0), 0.02f);
            k.Use("lamp_lens", Palette.LampAmber, 0, 0, tile: 0.25f);
            k.Emissive = 1;
            k.Cylinder(new Vector3(lx * 0.6f, LampHigh - 0.25f, 0), new Vector3(lx * 0.6f, LampHigh - 0.05f, 0), 0.06f, 8, radiusB: 0.045f);
            k.Emissive = 0;
            Iron(k);
        }
    }

    /// <summary>Where a garden's lamps hang, either end (local): for their light.</summary>
    public static IEnumerable<Vector3> GardenLamps()
    {
        float x = (float)Sim.Towns.TownFixtures.Size("garden").HalfS;
        yield return new Vector3((-x - 0.1f) * 0.6f, LampHigh - 0.15f, 0);
        yield return new Vector3((x + 0.1f) * 0.6f, LampHigh - 0.15f, 0);
    }

    const float LampHigh = 2.2f;

    /// <summary>A tree: riveted tin with painted leaves, or the old elm hung with paper leaves and little lamps (<see cref="ElmLamps"/>).</summary>
    static void Tree(Kit k, int variant)
    {
        bool elm = variant == 1;
        if (elm)
            k.Use("pine_bark", Palette.DeepBrown, 0.8f, 0, tile: 1);
        else
            Iron(k);
        k.Cylinder(Vector3.Zero, new Vector3(0, 2.4f, 0), 0.3f, 8, radiusB: 0.18f);
        for (int i = 0; i < 7; i++)
        {
            float a = i * 2.4f, up = 2.0f + i * 0.35f, len = 1.2f + (i % 3) * 0.4f;
            var from = new Vector3(0, up, 0);
            var to = from + new Vector3(MathF.Cos(a) * len, 0.7f + (i % 2) * 0.4f, MathF.Sin(a) * len);
            k.Cylinder(from, to, 0.08f - i * 0.006f, 5, radiusB: 0.03f);
            // Leaves along it: tin painted green, or paper.
            if (elm)
                k.Use("paper_form", Palette.BoardEnamel, 0.6f, 0, tile: 1);
            else
            {
                k.Use("paint_olive", new Vector3(0.25f, 0.4f, 0.2f), 0.6f, 0.3f, tile: 1);
                k.Tint = new Vector3(0.7f, 1.2f, 0.6f);
            }
            for (int l = 0; l < 12; l++)
            {
                var at = Vector3.Lerp(from, to, 0.2f + l * 0.07f) + new Vector3(MathF.Sin(l * 3.1f + i) * 0.3f, -0.15f + MathF.Cos(l * 1.3f) * 0.12f, MathF.Cos(l * 2.3f + i) * 0.3f);
                var n = Vector3.Normalize(new Vector3(MathF.Sin(l + i), 0.6f, MathF.Cos(l * 1.7f + i)));
                k.Disc(at, n, elm ? 0.11f : 0.18f, elm ? 4 : 5);
            }
            if (elm)
                k.Use("pine_bark", Palette.DeepBrown, 0.8f, 0, tile: 1);
            else
                Iron(k);
        }
        if (elm)
        {
            // The swing.
            k.Use("rope", Palette.TrailCloth, 0.8f, 0, tile: 1);
            var bough = new Vector3(MathF.Cos(2.4f) * 0.8f, 2.6f, MathF.Sin(2.4f) * 0.8f);
            k.Rod(bough + new Vector3(-0.2f, 0, 0), bough + new Vector3(-0.2f, -2.0f, 0), 0.012f);
            k.Rod(bough + new Vector3(0.2f, 0, 0), bough + new Vector3(0.2f, -2.0f, 0), 0.012f);
            Timber(k);
            k.Box(bough + new Vector3(-0.28f, -2.05f, -0.1f), bough + new Vector3(0.28f, -2.0f, 0.1f));
            k.Use("lamp_lens", Palette.LampAmber, 0, 0, tile: 0.25f);
            k.Emissive = 1;
            foreach (var p in ElmLamps())
                k.Lathe(p, [new(0.035f, 0), new(0.03f, 0.06f), new(0, 0.08f)], 6);
            k.Emissive = 0;
        }
    }

    /// <summary>The little lamps hung in the old elm (local).</summary>
    public static IEnumerable<Vector3> ElmLamps()
    {
        for (int i = 0; i < 7; i += 2)
        {
            float a = i * 2.4f, up = 2.0f + i * 0.35f, len = 1.2f + (i % 3) * 0.4f;
            yield return new Vector3(MathF.Cos(a) * len * 0.8f, up + 0.25f, MathF.Sin(a) * len * 0.8f);
        }
    }

    /// <summary>
    /// The day painted on the wall (<paramref name="variant"/>: a sunrise over the sea and a white house on a hill; a window
    /// with its shutters open on a field and real curtains; the day the wall's length, morning to evening), faded, chalked.
    /// </summary>
    static void Mural(Kit k, float x, float h, int variant)
    {
        float y0 = 0.4f, y1 = y0 + h;
        void Paint(Vector3 colour, float a0, float a1, float b0, float b1, float emissive = 0)
        {
            k.Use("cream", colour, 0.35f, 0, tile: 1);
            k.Tint = colour * 1.15f;
            k.Emissive = emissive;
            k.Quad(new Vector3(a0, b0, -0.02f), new Vector3(a1, b0, -0.02f), new Vector3(a1, b1, -0.02f), new Vector3(a0, b1, -0.02f), twoSided: true);
            k.Emissive = 0;
        }
        var sky = new Vector3(0.36f, 0.55f, 0.78f);
        var dawn = new Vector3(0.95f, 0.72f, 0.42f);
        var sea = new Vector3(0.18f, 0.38f, 0.5f);
        var grass = new Vector3(0.35f, 0.55f, 0.3f);
        switch (variant)
        {
            case >= 3:
                {
                    // Dave's (note 528): one of his landscapes the wall's width, in the hand of his canvases, signed with his D.
                    k.With(Kit.At(0, y0, -0.02f), () => DaveKit.Landscape(k, x, h, variant - 3, back: true));
                    break;
                }
            case 1:
                {
                    // A window: the frame, the field and the sky in it, the shutters open, curtains either side.
                    float w = x * 0.45f;
                    Paint(sky, -w, w, y0 + h * 0.45f, y1 - 0.4f);
                    Paint(grass, -w, w, y0 + 0.5f, y0 + h * 0.45f);
                    Paint(new Vector3(0.95f, 0.9f, 0.6f), w * 0.3f, w * 0.55f, y1 - 1.3f, y1 - 1.0f, 0.1f);
                    Paint(new Vector3(0.9f, 0.88f, 0.82f), -w - 0.15f, w + 0.15f, y0 + 0.35f, y0 + 0.5f);
                    foreach (float side in (float[])[-1, 1])
                    {
                        Paint(new Vector3(0.25f, 0.4f, 0.32f), side * w, side * (w + 1.0f) - (side < 0 ? 0 : 0), y0 + 0.5f, y1 - 0.4f);
                        k.Use("scarf", Palette.RustRed, 0.6f, 0, tile: 1);
                        k.Box(new Vector3(side * (w + 1.15f) - 0.25f, y0 + 0.3f, -0.12f), new Vector3(side * (w + 1.15f) + 0.25f, y1 - 0.2f, -0.04f));
                    }
                    break;
                }
            case 2:
                {
                    // The day, the wall's length: dawn at one end, noon in the middle, dusk at the other, darkened.
                    int bands = 10;
                    for (int i = 0; i < bands; i++)
                    {
                        float t = i / (float)(bands - 1);
                        var c = t < 0.5f ? Vector3.Lerp(dawn, sky, t * 2) : Vector3.Lerp(sky, new Vector3(0.25f, 0.18f, 0.3f), (t - 0.5f) * 2);
                        Paint(c, -x + 2 * x * i / bands, -x + 2 * x * (i + 1) / bands, y0 + 0.9f, y1);
                    }
                    Paint(sea, -x, x, y0, y0 + 0.9f);
                    Paint(new Vector3(1.0f, 0.85f, 0.45f), -x + 0.6f, -x + 1.4f, y0 + 1.2f, y0 + 2.0f, 0.15f);
                    Paint(new Vector3(1.0f, 0.95f, 0.75f), -0.45f, 0.45f, y1 - 1.3f, y1 - 0.4f, 0.2f);
                    break;
                }
            default:
                {
                    // The sunrise: the sky, the sun half up out of the sea, a green hill with a white house.
                    Paint(sky, -x, x, y0 + h * 0.55f, y1);
                    Paint(dawn, -x, x, y0 + h * 0.3f, y0 + h * 0.55f);
                    Paint(sea, -x, x, y0, y0 + h * 0.3f);
                    k.Use("cream", Palette.LampAmber, 0.3f, 0, tile: 1);
                    k.Tint = new Vector3(1.15f, 0.95f, 0.45f);
                    k.Emissive = 0.2f;
                    k.Disc(new Vector3(-x * 0.3f, y0 + h * 0.32f, -0.03f), -Vector3.UnitZ, 1.1f, 16);
                    k.Emissive = 0;
                    Paint(grass, x * 0.35f, x, y0, y0 + h * 0.42f);
                    Paint(new Vector3(0.92f, 0.9f, 0.85f), x * 0.6f, x * 0.75f, y0 + h * 0.42f, y0 + h * 0.58f);
                    Paint(new Vector3(0.5f, 0.2f, 0.18f), x * 0.58f, x * 0.77f, y0 + h * 0.58f, y0 + h * 0.64f);
                    // Chalk under it.
                    Paint(new Vector3(0.85f, 0.85f, 0.8f), -x * 0.2f, x * 0.15f, y0 - 0.3f, y0 - 0.22f);
                    break;
                }
        }
    }

    static void Stone(Kit k) => k.Use("granite_lichen", Palette.Ballast, 0.7f, 0.1f, tile: 1);
    static void Timber(Kit k) => k.Use("wood_grey", Palette.DeepBrown, 0.9f, 0, tile: 1);
    static void Iron(Kit k) => k.Use("rust_heavy", Palette.IronGrey, 0.8f, 0.3f);
    static void Paint(Kit k)
    {
        k.Use("paint_oxide", Palette.BoardEnamel, 0.6f, 0.2f, tile: 1);
        k.Tint = new Vector3(1.6f);
    }

    /// <summary>
    /// The green (in the rail frame's piece: x along the line, −z out toward its side): grass between the streets, gravel
    /// paths crossing it corner to corner and down the middle, a kerb round it.
    /// </summary>
    public static MeshAsset Green(Look? look, float length, float depth)
    {
        var k = new Kit(look, 3301);
        k.Use("ground_grass", new Vector3(0.22f, 0.3f, 0.18f), 0.8f, 0, tile: 4);
        k.Tint = new Vector3(0.8f, 0.85f, 0.7f);
        k.Box(new Vector3(-length / 2, 0, -depth / 2), new Vector3(length / 2, 0.05f, depth / 2), Kit.Faces.PosY);
        k.Use("ballast", Palette.Ballast, 0.8f, 0, tile: 2);
        k.Tint = new Vector3(1.2f);
        void Path(Vector2 a, Vector2 b, float w)
        {
            var d = Vector2.Normalize(b - a);
            var n = new Vector2(-d.Y, d.X) * w / 2;
            k.Quad(new Vector3(a.X + n.X, 0.06f, a.Y + n.Y), new Vector3(b.X + n.X, 0.06f, b.Y + n.Y), new Vector3(b.X - n.X, 0.06f, b.Y - n.Y), new Vector3(a.X - n.X, 0.06f, a.Y - n.Y), twoSided: true);
        }
        float hl = length / 2 - 0.5f, hd = depth / 2 - 0.5f;
        Path(new Vector2(-hl, -hd), new Vector2(hl, hd), 1.6f);
        Path(new Vector2(-hl, hd), new Vector2(hl, -hd), 1.6f);
        Path(new Vector2(-hl, 0), new Vector2(hl, 0), 1.8f);
        Stone(k);
        foreach (float zz in (float[])[-depth / 2, depth / 2])
            k.Box(new Vector3(-length / 2, 0, zz - 0.12f), new Vector3(length / 2, 0.15f, zz + 0.12f));
        foreach (float xx in (float[])[-length / 2, length / 2])
            k.Box(new Vector3(xx - 0.12f, 0, -depth / 2), new Vector3(xx + 0.12f, 0.15f, depth / 2));
        return k.Build("civic-green");
    }
}
