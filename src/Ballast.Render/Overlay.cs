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

    /// <summary>
    /// Darkens the frame's edge, clear inside an ellipse round (<paramref name="cx"/>, <paramref name="cy"/>) and
    /// fading to <paramref name="strength"/> by the edge: a headset's comfort vignette.
    /// </summary>
    /// <param name="inner">The clear ellipse's size, as a fraction of the frame's half-size.</param>
    public void Vignette(float width, float height, float cx, float cy, float inner, float strength, int segments = 32)
    {
        if (strength <= 0 || width <= 0 || height <= 0)
            return;
        var clear = new Vector4(0, 0, 0, 0);
        var dark = new Vector4(0, 0, 0, Math.Min(1, strength));
        float rx = width / 2, ry = height / 2;
        inner = Math.Clamp(inner, 0, 0.95f);
        // Far past the corners, so an off-centre middle (a headset eye's) still covers the whole frame.
        const float Far = 3;
        Vector2 At(float r, float a) => new(cx + MathF.Cos(a) * r * rx, cy + MathF.Sin(a) * r * ry);
        for (int i = 0; i < segments; i++)
        {
            float a0 = i * MathF.Tau / segments, a1 = (i + 1) * MathF.Tau / segments;
            Band(At(inner, a0), At(inner, a1), At(1, a1), At(1, a0), clear, dark);
            Band(At(1, a0), At(1, a1), At(Far, a1), At(Far, a0), dark, dark);
        }
    }

    /// <summary>A quad from an inner edge (a, b) in one colour to an outer edge (c, d) in another.</summary>
    void Band(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Vector4 inner, Vector4 outer)
    {
        Vertices.Add(new OverlayVertex(a, inner));
        Vertices.Add(new OverlayVertex(b, inner));
        Vertices.Add(new OverlayVertex(c, outer));
        Vertices.Add(new OverlayVertex(a, inner));
        Vertices.Add(new OverlayVertex(c, outer));
        Vertices.Add(new OverlayVertex(d, outer));
    }

    /// <summary>
    /// A picture (RGBA8, display values like the frame's) in the rectangle, as flat cells: the overlay has no textures, so
    /// the image is averaged down to <paramref name="cols"/> × <paramref name="rows"/> cells, each colour rounded to
    /// <paramref name="levels"/> steps a channel, and each row's runs of one colour drawn as one quad. A night scene is
    /// mostly dark runs, so a thumbnail costs far fewer quads than it has cells.
    /// </summary>
    public void Image(float x, float y, float w, float h, ReadOnlySpan<byte> rgba, int width, int height, int cols, int rows, int levels = 32)
    {
        if (cols <= 0 || rows <= 0 || width <= 0 || height <= 0 || levels < 2 || rgba.Length < width * height * 4)
            return;
        float cw = w / cols, ch = h / rows, step = 255f / (levels - 1);
        for (int r = 0; r < rows; r++)
        {
            int y0 = r * height / rows, y1 = Math.Max(y0 + 1, (r + 1) * height / rows);
            int runFrom = 0;
            Vector4 run = default;
            for (int c = 0; c <= cols; c++)
            {
                Vector4 colour = default;
                if (c < cols)
                {
                    int x0 = c * width / cols, x1 = Math.Max(x0 + 1, (c + 1) * width / cols);
                    float sr = 0, sg = 0, sb = 0;
                    for (int py = y0; py < y1; py++)
                        for (int px = x0; px < x1; px++)
                        {
                            int i = (py * width + px) * 4;
                            sr += rgba[i];
                            sg += rgba[i + 1];
                            sb += rgba[i + 2];
                        }
                    float n = (y1 - y0) * (x1 - x0);
                    colour = new Vector4(MathF.Round(sr / n / step) * step / 255, MathF.Round(sg / n / step) * step / 255, MathF.Round(sb / n / step) * step / 255, 1);
                }
                if (c == 0)
                    run = colour;
                else if (c == cols || colour != run)
                {
                    Rect(x + runFrom * cw, y + r * ch, (c - runFrom) * cw, ch, run);
                    runFrom = c;
                    run = colour;
                }
            }
        }
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
    public float Text(float x, float y, string text, Vector4 colour, int scale = 1, bool shadow = true) =>
        Text(x, y, text, colour, (float)scale, shadow);

    /// <summary>
    /// Text at a fractional scale: 0.5 is fine print, a canvas pixel's half per font pixel (a 480-wide canvas on a 1080p
    /// screen still gives each font pixel two screen pixels).
    /// </summary>
    public float Text(float x, float y, string text, Vector4 colour, float scale, bool shadow = true)
    {
        if (shadow)
            Glyphs(x + scale, y + scale, text, new Vector4(0, 0, 0, colour.W * 0.8f), scale);
        Glyphs(x, y, text, colour, scale);
        return Measure(text, scale);
    }

    /// <summary>The width of <paramref name="text"/> at <paramref name="scale"/>.</summary>
    public float Measure(string text, float scale = 1) => text.Length == 0 ? 0 : (text.Length * Font.Advance - 1) * scale;

    /// <summary>Text centred on x.</summary>
    public float TextCentred(float x, float y, string text, Vector4 colour, int scale = 1) =>
        Text(MathF.Round(x - Font.Measure(text, scale) / 2f), y, text, colour, scale);

    /// <summary>Text ending at x.</summary>
    public float TextRight(float x, float y, string text, Vector4 colour, int scale = 1) =>
        Text(x - Font.Measure(text, scale), y, text, colour, scale);

    void Glyphs(float x, float y, string text, Vector4 colour, float scale)
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
