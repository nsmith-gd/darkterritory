using System.Numerics;
using System.Runtime.InteropServices;

namespace Ballast.Render;

[StructLayout(LayoutKind.Sequential)]
public struct OverlayVertex(Vector2 position, Vector4 colour)
{
    public Vector2 Position = position;
    public Vector4 Colour = colour;
    public const uint Stride = 24;
}

/// <summary>
/// Flat 2D drawing over the frame, in the frame's own pixels (top-left origin): panels, bars and pixel-font text.
/// Everything is solid quads, so the renderer needs no textures. The HUD, menus and prompts build on it.
/// </summary>
public sealed class Overlay
{
    public List<OverlayVertex> Vertices { get; } = new();
    public BitmapFont Font { get; init; } = BitmapFont.Default;
    public int Count => Vertices.Count;

    public void Clear() => Vertices.Clear();

    public void Rect(float x, float y, float w, float h, Vector4 colour)
    {
        if (w <= 0 || h <= 0 || colour.W <= 0)
            return;
        var a = new Vector2(x, y);
        var b = new Vector2(x + w, y);
        var c = new Vector2(x + w, y + h);
        var d = new Vector2(x, y + h);
        Vertices.Add(new OverlayVertex(a, colour));
        Vertices.Add(new OverlayVertex(b, colour));
        Vertices.Add(new OverlayVertex(c, colour));
        Vertices.Add(new OverlayVertex(a, colour));
        Vertices.Add(new OverlayVertex(c, colour));
        Vertices.Add(new OverlayVertex(d, colour));
    }

    /// <summary>A frame of the given thickness inside the rectangle.</summary>
    public void Outline(float x, float y, float w, float h, Vector4 colour, float t = 1)
    {
        Rect(x, y, w, t, colour);
        Rect(x, y + h - t, w, t, colour);
        Rect(x, y + t, t, h - 2 * t, colour);
        Rect(x + w - t, y + t, t, h - 2 * t, colour);
    }

    /// <summary>Draws text with its top-left at (x, y); a one-pixel shadow keeps it readable over anything. Returns its width.</summary>
    public float Text(float x, float y, string text, Vector4 colour, int scale = 1, bool shadow = true)
    {
        if (shadow)
            Glyphs(x + scale, y + scale, text, new Vector4(0, 0, 0, colour.W * 0.8f), scale);
        Glyphs(x, y, text, colour, scale);
        return Font.Measure(text, scale);
    }

    /// <summary>Text centred on x.</summary>
    public float TextCentred(float x, float y, string text, Vector4 colour, int scale = 1) =>
        Text(MathF.Round(x - Font.Measure(text, scale) / 2f), y, text, colour, scale);

    /// <summary>Text ending at x.</summary>
    public float TextRight(float x, float y, string text, Vector4 colour, int scale = 1) =>
        Text(x - Font.Measure(text, scale), y, text, colour, scale);

    void Glyphs(float x, float y, string text, Vector4 colour, int scale)
    {
        for (int i = 0; i < text.Length; i++)
        {
            var g = Font.Glyph(text[i]);
            float gx = x + i * Font.Advance * scale;
            // One quad per horizontal run of ink, not per pixel.
            for (int row = 0; row < g.GetLength(0); row++)
                for (int col = 0; col < g.GetLength(1);)
                {
                    if (!g[row, col])
                    {
                        col++;
                        continue;
                    }
                    int start = col;
                    while (col < g.GetLength(1) && g[row, col])
                        col++;
                    Rect(gx + start * scale, y + row * scale, (col - start) * scale, scale, colour);
                }
        }
    }
}
