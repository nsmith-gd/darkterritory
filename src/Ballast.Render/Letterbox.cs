namespace Ballast.Render;

/// <summary>
/// A picture shown at its own shape in a window of another (ARCHITECTURE §8 note 459): the largest rectangle of the picture's
/// shape that fits, centred, the rest of the window left as bars. The frame is 16:9 whatever the window is (a 16:10 screen,
/// an ultrawide, a window dragged to any shape), so neither the scene nor the HUD drawn over it is stretched.
/// </summary>
public static class Letterbox
{
    /// <summary>
    /// Where a <paramref name="width"/> x <paramref name="height"/> picture goes in a <paramref name="windowWidth"/> x
    /// <paramref name="windowHeight"/> window, in the window's pixels. A window wider than the picture has bars at its sides,
    /// a taller one at its top and bottom; one within a pixel of the picture's shape (1366x768 for 16:9) is filled.
    /// </summary>
    public static (int X, int Y, int Width, int Height) Fit(int width, int height, int windowWidth, int windowHeight)
    {
        if (width <= 0 || height <= 0 || windowWidth <= 0 || windowHeight <= 0)
            return (0, 0, Math.Max(0, windowWidth), Math.Max(0, windowHeight));
        int w = (int)Math.Round((double)windowHeight * width / height), h = (int)Math.Round((double)windowWidth * height / width);
        if (w < windowWidth - 1)
            return ((windowWidth - w) / 2, 0, w, windowHeight);
        if (h < windowHeight - 1)
            return (0, (windowHeight - h) / 2, windowWidth, h);
        return (0, 0, windowWidth, windowHeight);
    }

    /// <summary>
    /// A point given as a fraction of the window (the mouse, 0 to 1 across it) as a fraction of the picture
    /// <paramref name="fit"/> puts in it: on a bar, under 0 or over 1.
    /// </summary>
    public static (float X, float Y) Inside(float x, float y, (int X, int Y, int Width, int Height) fit, int windowWidth, int windowHeight) =>
        fit.Width <= 0 || fit.Height <= 0 ? (x, y)
            : ((x * windowWidth - fit.X) / fit.Width, (y * windowHeight - fit.Y) / fit.Height);
}
