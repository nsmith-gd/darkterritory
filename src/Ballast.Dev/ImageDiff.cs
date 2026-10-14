using Ballast.Render;

namespace Ballast.Dev;

/// <summary>
/// Two renders of the same shot compared (ARCHITECTURE §8 note 517: a pull request's review packet against main's): how much
/// of the frame changed, by how much, and where, and a strip to look at (before, after, and the change marked over a dimmed
/// after). A pixel has changed when any channel moved by more than <c>tolerance</c> (out of 255): small enough to catch a
/// tell's light gone, large enough to let a dither's last step through.
/// </summary>
public static class ImageDiff
{
    /// <param name="Changed">The share of pixels that changed (0..1).</param>
    /// <param name="Mean">The mean of each pixel's largest channel change, over the whole frame (0..255).</param>
    /// <param name="Box">Where the change is: x, y, width, height in pixels (empty when none).</param>
    public sealed record Result(int Width, int Height, double Changed, double Mean, int Max, int[] Box)
    {
        /// <summary>None changed; a few stray pixels (under 0.05 % of the frame); or a change worth looking at.</summary>
        public string Verdict => Changed == 0 ? "same" : Changed < 0.0005 ? "noise" : "changed";
    }

    public static Result Compare(Image before, Image after, int tolerance = 8)
    {
        if (before.Width != after.Width || before.Height != after.Height)
            return new Result(after.Width, after.Height, 1, 255, 255, [0, 0, after.Width, after.Height]);
        int w = after.Width, h = after.Height;
        long changed = 0, total = 0;
        int max = 0, x0 = w, y0 = h, x1 = -1, y1 = -1;
        var a = before.Rgba;
        var b = after.Rgba;
        for (int y = 0; y < h; y++)
            for (int x = 0, i = y * w * 4; x < w; x++, i += 4)
            {
                int d = Math.Max(Math.Abs(a[i] - b[i]), Math.Max(Math.Abs(a[i + 1] - b[i + 1]), Math.Abs(a[i + 2] - b[i + 2])));
                total += d;
                max = Math.Max(max, d);
                if (d > tolerance)
                {
                    changed++;
                    x0 = Math.Min(x0, x);
                    y0 = Math.Min(y0, y);
                    x1 = Math.Max(x1, x);
                    y1 = Math.Max(y1, y);
                }
            }
        int[] box = x1 < 0 ? [] : [x0, y0, x1 - x0 + 1, y1 - y0 + 1];
        return new Result(w, h, changed / (double)(w * h), total / (double)(w * h), max, box);
    }

    /// <summary>
    /// Before, after, and the change (changed pixels in magenta over the after, dimmed to a third), side by side, each
    /// scaled down by <paramref name="shrink"/>; the change box outlined on the after.
    /// </summary>
    public static Image Strip(Image before, Image after, Result result, int tolerance = 8, int shrink = 2)
    {
        int w = after.Width / shrink, h = after.Height / shrink, gap = 4;
        var px = new byte[(w * 3 + gap * 2) * h * 4];
        int stride = (w * 3 + gap * 2) * 4;
        bool same = before.Width == after.Width && before.Height == after.Height;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int sx = x * shrink, sy = y * shrink, s = (sy * after.Width + sx) * 4;
                var b = after.Rgba;
                int d = 0;
                if (same)
                {
                    var a = before.Rgba;
                    d = Math.Max(Math.Abs(a[s] - b[s]), Math.Max(Math.Abs(a[s + 1] - b[s + 1]), Math.Abs(a[s + 2] - b[s + 2])));
                    Put(px, y * stride + x * 4, a[s], a[s + 1], a[s + 2]);
                }
                Put(px, y * stride + (w + gap + x) * 4, b[s], b[s + 1], b[s + 2]);
                int at = y * stride + (2 * (w + gap) + x) * 4;
                if (d > tolerance)
                    Put(px, at, 255, 0, 255);
                else
                    Put(px, at, (byte)(b[s] / 3), (byte)(b[s + 1] / 3), (byte)(b[s + 2] / 3));
            }
        if (result.Box is [var bx, var by, var bw, var bh])
            Outline(px, stride, w + gap + bx / shrink, by / shrink, Math.Max(1, bw / shrink), Math.Max(1, bh / shrink), h);
        return new Image(w * 3 + gap * 2, h, px);
    }

    static void Put(byte[] px, int i, byte r, byte g, byte b)
    {
        px[i] = r;
        px[i + 1] = g;
        px[i + 2] = b;
        px[i + 3] = 255;
    }

    static void Outline(byte[] px, int stride, int x, int y, int w, int h, int panelH)
    {
        for (int i = 0; i < w; i++)
        {
            Put(px, y * stride + (x + i) * 4, 255, 0, 255);
            Put(px, Math.Min(panelH - 1, y + h - 1) * stride + (x + i) * 4, 255, 0, 255);
        }
        for (int j = 0; j < h && y + j < panelH; j++)
        {
            Put(px, (y + j) * stride + x * 4, 255, 0, 255);
            Put(px, (y + j) * stride + (x + w - 1) * 4, 255, 0, 255);
        }
    }
}
