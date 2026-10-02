using System.Numerics;
using Ballast.Render;

namespace DarkTerritory.Game.Art;

/// <summary>
/// The lineside signage kit (linegen plan §9.5, §18 "Signage"): km posts, speed and restricted boards, gradient posts,
/// whistle boards, junction and facility boards, bridge plates and limits, line-closed, yard limit, station names. A
/// board on its post, the lettering in the HUD's own pixel font, painted on so the lamp picks it out (they're
/// reflective: a little light of their own). In the piece's frame: −Z the way the train comes, the face toward +Z (the
/// oncoming train), foot at the origin.
/// </summary>
public static class SignKit
{
    /// <summary>A board of <paramref name="type"/>, <paramref name="width"/> across, its top <paramref name="height"/> up its post.</summary>
    public static MeshAsset Board(Look? look, string type, string text, float width, float height)
    {
        var k = new Kit(look, 1300 + text.Length);
        var lines = Lines(type, text.ToUpperInvariant(), width);
        var font = BitmapFont.Default;
        int widest = Math.Max(1, lines.Max(l => font.Measure(l)));
        // The lettering's pixel: as large as fits the board, a margin round it.
        float px = MathF.Min(width * 0.84f / widest, 0.09f);
        float boardH = type switch
        {
            "kmPost" or "minorPost" => MathF.Max(0.3f, lines.Count * font.LineHeight * px + 0.12f),
            _ => MathF.Max(width * 0.45f, lines.Count * font.LineHeight * px + 0.16f),
        };
        bool post = type is not "tunnelPlate";
        float top = height, bottom = MathF.Max(0.3f, top - boardH);
        (Vector3 face, Vector3 letter) = type switch
        {
            // Speed boards: black figures on white, as ever; restricted: yellow; closed and limits: red.
            "restricted" or "endRestricted" => (new Vector3(0.78f, 0.66f, 0.2f), Palette.SootBlack),
            "lineClosed" or "bridgeLimit" => (new Vector3(0.62f, 0.12f, 0.08f), new Vector3(0.9f, 0.88f, 0.8f)),
            // The mail crane's warning board (sight.json drop): the mail's green, white letters.
            "mailDrop" => (new Vector3(0.16f, 0.36f, 0.2f), new Vector3(0.9f, 0.88f, 0.8f)),
            "stationName" or "facility" or "junction" => (new Vector3(0.12f, 0.14f, 0.16f), new Vector3(0.85f, 0.82f, 0.72f)),
            _ => (new Vector3(0.86f, 0.84f, 0.78f), Palette.SootBlack),
        };
        if (post)
        {
            k.Use("paint_black", Palette.SootBlack, 0.7f, 0.1f, tile: 1);
            // Two posts under a wide board, one under a narrow one.
            var feet = width > 1.3f ? new[] { -width * 0.38f, width * 0.38f } : new[] { 0f };
            foreach (float x in feet)
                k.Box(new Vector3(x - 0.05f, 0, -0.05f), new Vector3(x + 0.05f, bottom + boardH * 0.5f, 0.05f), Kit.Faces.All & ~Kit.Faces.NegY);
        }
        k.Use("iron_plate", face, 0.35f, 0.3f, tile: 1);
        k.Tint = face;
        k.Emissive = 0.08f;
        k.Box(new Vector3(-width / 2, bottom, 0.05f), new Vector3(width / 2, top, 0.09f));
        // The lettering, raised a hair off the face.
        k.Use("paint_black", letter, 0.2f, 0.1f, tile: 1);
        k.Tint = letter;
        k.Emissive = letter.X > 0.5f ? 0.12f : 0;
        float y = top - (boardH - lines.Count * font.LineHeight * px) / 2 - px;
        foreach (var line in lines)
        {
            float x = -font.Measure(line) * px / 2;
            foreach (char ch in line)
            {
                var g = font.Glyph(ch);
                for (int gy = 0; gy < g.GetLength(0); gy++)
                    for (int gx = 0; gx < g.GetLength(1); gx++)
                        if (g[gy, gx])
                        {
                            float x0 = x + gx * px, y0 = y - gy * px;
                            k.Quad(new Vector3(x0, y0, 0.095f), new Vector3(x0 + px, y0, 0.095f), new Vector3(x0 + px, y0 - px, 0.095f), new Vector3(x0, y0 - px, 0.095f));
                        }
                x += font.Advance * px;
            }
            y -= font.LineHeight * px;
        }
        return k.Build($"sign-{type}-{text}");
    }

    /// <summary>The words on a board, broken into lines that fit it: a name above its figure, a long name over two.</summary>
    static List<string> Lines(string type, string text, float width)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length <= 1 || type is "kmPost" or "minorPost" or "speedBoard" or "resumeBoard")
            return [text];
        int max = Math.Max(4, (int)(width / 0.09f / BitmapFont.Default.Advance));
        var lines = new List<string>();
        string current = "";
        foreach (var w in words)
        {
            if (current.Length > 0 && current.Length + 1 + w.Length > max)
            {
                lines.Add(current);
                current = w;
            }
            else
                current = current.Length == 0 ? w : current + " " + w;
        }
        lines.Add(current);
        return lines;
    }

    /// <summary>A dead signal: a lattice mast, its arm dropped, its lamp long out.</summary>
    public static MeshAsset DeadSignal(Look? look) => WorldKit.Signal(look, false);
}
