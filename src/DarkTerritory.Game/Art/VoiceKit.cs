using System.Numerics;
using Ballast.Render;

namespace DarkTerritory.Game.Art;

/// <summary>
/// The voice's fittings (the director's calls of 14 Oct 2026, friendslop.md queue #454; ARCHITECTURE §8 note 720): the
/// speaking tube's mouthpiece and its pipe down into the floor, in the cab and the guard van, and the whistle codes' enamel card
/// hung by the cord. Their own pieces, placed from the sim's numbers (<see cref="Sim.Train.SpeakingTube"/>, the whistle cord's
/// footing), so they ride with the footplate whatever the cab behind it becomes.
/// </summary>
public static class VoiceKit
{
    /// <summary>The horn's bell (m): its radius at the mouth, and how far it stands out from the wall.</summary>
    public const float BellRadius = 0.1f, Reach = 0.2f;

    /// <summary>
    /// A speaking tube's end: a brass horn opening toward +X (into the room), its mouth at the origin, its throat back to the
    /// wall at −X, where the pipe turns down <paramref name="drop"/> m to the floor (the tube runs under the floors the length of
    /// the train, clear of the windows), clipped to the wall twice; a brass whistle-plug hung on a chain beside the mouth, the
    /// old tubes' way of calling the other end.
    /// </summary>
    public static MeshAsset TubeEnd(Look? look, float drop)
    {
        var k = new Kit(look, 720);
        k.Use("brass", Palette.TarnishedBrass, 0.3f, 0.7f);
        // The bell, flaring to the mouth, and its rolled lip.
        k.Cylinder(new Vector3(-0.13f, 0, 0), new Vector3(0, 0, 0), 0.032f, 14, caps: false, radiusB: BellRadius);
        k.Cylinder(new Vector3(-0.01f, 0, 0), new Vector3(0.008f, 0, 0), BellRadius + 0.01f, 14, caps: false);
        // Its throat back to the wall, the elbow, and the pipe down the wall into the floor.
        k.Cylinder(new Vector3(-Reach, 0, 0), new Vector3(-0.13f, 0, 0), 0.026f, 8);
        k.Cylinder(new Vector3(-Reach, 0.035f, 0), new Vector3(-Reach, -0.06f, 0), 0.034f, 8);
        k.Cylinder(new Vector3(-Reach, -0.06f, 0), new Vector3(-Reach, -drop, 0), 0.026f, 8);
        // The inside of the bell, dark, so the mouth reads as an opening.
        k.Use("iron_plate", new Vector3(0.05f, 0.04f, 0.03f), 0.9f, 0.05f, tile: 0.3f);
        k.Disc(new Vector3(-0.07f, 0, 0), Vector3.UnitX, 0.06f, 14);
        // Its clips to the wall.
        k.Use("iron_plate", Palette.IronGrey, 0.7f, 0.4f, tile: 0.3f);
        foreach (float y in (ReadOnlySpan<float>)[-0.3f, -MathF.Max(0.45f, drop - 0.2f)])
            k.Box(new Vector3(-Reach - 0.035f, y - 0.025f, -0.045f), new Vector3(-Reach + 0.035f, y + 0.025f, 0.045f));
        // The whistle-plug on its chain: hung from the bell's lip, down beside it.
        k.Use("brass", Palette.TarnishedBrass, 0.3f, 0.7f);
        float side = BellRadius + 0.014f;
        k.Cylinder(new Vector3(-0.02f, -0.1f, side), new Vector3(-0.02f, -0.01f, side), 0.003f, 4, caps: false);
        k.Cylinder(new Vector3(-0.04f, -0.15f, side), new Vector3(0, -0.15f, side), 0.025f, 8, radiusB: 0.014f);
        k.Cylinder(new Vector3(-0.02f, -0.15f, side), new Vector3(-0.02f, -0.1f, side), 0.007f, 4);
        return k.Build($"speaking-tube-{drop:0.00}");
    }

    /// <summary>How big the whistle card is (m) and its print's pixel.</summary>
    public const float CardWidth = 0.38f, CardPixel = 0.0024f;

    /// <summary>
    /// The whistle codes' card (GDD §12; note 720): white enamel on an iron plate, a red rule under its title, the codes in black
    /// in the HUD's pixel font, one a line. Its face toward +Z, across X, its top edge's middle at the origin, hung from a nail.
    /// </summary>
    public static MeshAsset WhistleCard(Look? look, string title, IReadOnlyList<string> codes)
    {
        var k = new Kit(look, 721);
        var font = BitmapFont.Default;
        float px = CardPixel, line = font.LineHeight * px, margin = 0.02f;
        float h = margin * 2 + line * (codes.Count + 1) + px * 3, w = CardWidth;
        // The plate, a rim of iron round the enamel, and the nail it hangs from.
        k.Use("iron_plate", Palette.IronGrey, 0.6f, 0.3f, tile: 0.3f);
        k.Box(new Vector3(-w / 2 - 0.006f, -h - 0.006f, -0.008f), new Vector3(w / 2 + 0.006f, 0.006f, -0.001f));
        k.Cylinder(new Vector3(0, 0.03f, -0.01f), new Vector3(0, 0.03f, 0.012f), 0.004f, 5);
        k.Cylinder(new Vector3(-0.002f, 0.03f, -0.002f), new Vector3(-0.03f, 0.004f, -0.002f), 0.0015f, 3, caps: false);
        k.Cylinder(new Vector3(0.002f, 0.03f, -0.002f), new Vector3(0.03f, 0.004f, -0.002f), 0.0015f, 3, caps: false);
        k.Use("enamel", new Vector3(0.86f, 0.84f, 0.76f), 0.25f, 0.2f);
        k.Tint = new Vector3(0.86f, 0.84f, 0.76f);
        k.Quad(new Vector3(-w / 2, 0, 0), new Vector3(w / 2, 0, 0), new Vector3(w / 2, -h, 0), new Vector3(-w / 2, -h, 0));
        float y = -margin;
        // The title, then a red rule.
        Print(k, font, title, y, px, new Vector3(0.08f, 0.07f, 0.06f));
        y -= line;
        k.Use("enamel", Palette.SignalRed, 0.2f, 0.2f);
        k.Tint = Palette.SignalRed;
        k.Quad(new Vector3(-w / 2 + margin, y + px, 0.0015f), new Vector3(w / 2 - margin, y + px, 0.0015f),
            new Vector3(w / 2 - margin, y, 0.0015f), new Vector3(-w / 2 + margin, y, 0.0015f));
        y -= px * 3;
        foreach (var code in codes)
        {
            Print(k, font, code, y, px, new Vector3(0.08f, 0.07f, 0.06f));
            y -= line;
        }
        return k.Build($"whistle-card-{title}-{string.Join('|', codes)}");
    }

    /// <summary>
    /// A small enamel tag (note 720: over the speaking tube's horn, where it goes): white on a dark iron plate, one line of
    /// the pixel print, its face toward +Z, its middle at the origin.
    /// </summary>
    public static MeshAsset Tag(Look? look, string text)
    {
        var k = new Kit(look, 722);
        var font = BitmapFont.Default;
        const float px = 0.0024f;
        float w = font.Measure(text) * px + 0.03f, h = font.Height * px + 0.024f;
        k.Use("iron_plate", Palette.IronGrey, 0.6f, 0.3f, tile: 0.3f);
        k.Box(new Vector3(-w / 2 - 0.004f, -h / 2 - 0.004f, -0.006f), new Vector3(w / 2 + 0.004f, h / 2 + 0.004f, -0.001f));
        k.Use("enamel", new Vector3(0.12f, 0.16f, 0.22f), 0.25f, 0.2f);
        k.Tint = new Vector3(0.12f, 0.16f, 0.22f);
        k.Quad(new Vector3(-w / 2, h / 2, 0), new Vector3(w / 2, h / 2, 0), new Vector3(w / 2, -h / 2, 0), new Vector3(-w / 2, -h / 2, 0));
        Print(k, font, text, font.Height * px / 2, px, new Vector3(0.86f, 0.84f, 0.76f));
        return k.Build($"tube-tag-{text}");
    }

    /// <summary>A line of pixel print, centred, its top at <paramref name="top"/>, a little proud of the enamel.</summary>
    static void Print(Kit k, BitmapFont font, string text, float top, float px, Vector3 ink)
    {
        k.Use("enamel", ink, 0.2f, 0.1f);
        k.Tint = ink;
        float x = -font.Measure(text) * px / 2;
        foreach (char ch in text)
        {
            var g = font.Glyph(ch);
            for (int gy = 0; gy < g.GetLength(0); gy++)
                for (int gx = 0; gx < g.GetLength(1); gx++)
                {
                    if (!g[gy, gx] || gx > 0 && g[gy, gx - 1])
                        continue;
                    int run = 1;
                    while (gx + run < g.GetLength(1) && g[gy, gx + run])
                        run++;
                    float xa = x + gx * px, xb = xa + run * px, ya = top - gy * px, yb = ya - px;
                    k.Quad(new Vector3(xa, ya, 0.0015f), new Vector3(xb, ya, 0.0015f), new Vector3(xb, yb, 0.0015f), new Vector3(xa, yb, 0.0015f));
                }
            x += font.Advance * px;
        }
    }
}
