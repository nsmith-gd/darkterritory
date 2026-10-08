using Ballast.Render;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The picture keeps its shape in any window (note 459): the 16:9 frame was blitted over the window's whole extent, so a 16:10
/// screen, an ultrawide or a window dragged to any shape stretched the night and the HUD with it. It's shown at its own
/// shape in the middle, bars beside it, and the menus read the mouse inside it.
/// </summary>
public sealed class LetterboxTests
{
    [Theory]
    // A 16:9 window: the whole of it.
    [InlineData(1280, 720, 0, 0, 1280, 720)]
    [InlineData(1920, 1080, 0, 0, 1920, 1080)]
    // 1366x768 is 16:9 to within a pixel: filled, no one-pixel bar.
    [InlineData(1366, 768, 0, 0, 1366, 768)]
    // 16:10 (the Steam Deck, a 1920x1200 laptop): bars top and bottom.
    [InlineData(1280, 800, 0, 40, 1280, 720)]
    [InlineData(1920, 1200, 0, 60, 1920, 1080)]
    // Ultrawide: bars at the sides.
    [InlineData(2560, 1080, 320, 0, 1920, 1080)]
    [InlineData(3440, 1440, 440, 0, 2560, 1440)]
    // 4:3, and a window dragged tall.
    [InlineData(1024, 768, 0, 96, 1024, 576)]
    [InlineData(600, 1000, 0, 331, 600, 338)]
    public void A16By9FrameIsShownAtItsOwnShapeInTheMiddle(int ww, int wh, int x, int y, int w, int h)
    {
        var fit = Letterbox.Fit(1280, 720, ww, wh);
        Assert.Equal((x, y, w, h), fit);
        // Inside the window, centred, and the frame's shape to within a pixel.
        Assert.InRange(fit.X, 0, ww);
        Assert.InRange(fit.Y, 0, wh);
        Assert.True(fit.X + fit.Width <= ww && fit.Y + fit.Height <= wh);
        Assert.InRange(Math.Abs(fit.Width * 720.0 / 1280 - fit.Height), 0.0, 1.0);
        Assert.InRange(Math.Abs(ww - fit.Width - 2 * fit.X), 0, 1);
        Assert.InRange(Math.Abs(wh - fit.Height - 2 * fit.Y), 0, 1);
    }

    [Fact]
    public void AFrameDrawnAtTheWindowsOwnShapeFillsIt()
    {
        // The renderer's own size (a render scale, the VR mirror's part of an eye): only the shape matters.
        Assert.Equal((0, 0, 1280, 800), Letterbox.Fit(640, 400, 1280, 800));
        Assert.Equal((0, 0, 0, 0), Letterbox.Fit(1280, 720, 0, 0));
        Assert.Equal((0, 0, 1280, 720), Letterbox.Fit(0, 0, 1280, 720));
    }

    [Fact]
    public void TheMouseIsReadInsideTheFrameNotAcrossTheWindow()
    {
        // The Steam Deck: the frame from y 40 to 760 of 800. The window's middle is the frame's; its top bar is above it.
        var fit = Letterbox.Fit(1280, 720, 1280, 800);
        Assert.Equal((0.5f, 0.5f), Letterbox.Inside(0.5f, 0.5f, fit, 1280, 800));
        var (_, top) = Letterbox.Inside(0.5f, 40f / 800, fit, 1280, 800);
        Assert.Equal(0f, top, 4);
        var (_, bottom) = Letterbox.Inside(0.5f, 760f / 800, fit, 1280, 800);
        Assert.Equal(1f, bottom, 4);
        Assert.True(Letterbox.Inside(0.5f, 0.01f, fit, 1280, 800).Y < 0);
        // An ultrawide: the side bars are off the frame's sides.
        fit = Letterbox.Fit(1280, 720, 2560, 1080);
        Assert.Equal(0f, Letterbox.Inside(320f / 2560, 0.5f, fit, 2560, 1080).X, 4);
        Assert.Equal(1f, Letterbox.Inside(2240f / 2560, 0.5f, fit, 2560, 1080).X, 4);
        Assert.True(Letterbox.Inside(0.05f, 0.5f, fit, 2560, 1080).X < 0);
        // A 16:9 window: as it was, the window is the frame.
        fit = Letterbox.Fit(1280, 720, 1920, 1080);
        Assert.Equal((0.3f, 0.7f), Letterbox.Inside(0.3f, 0.7f, fit, 1920, 1080));
    }
}
