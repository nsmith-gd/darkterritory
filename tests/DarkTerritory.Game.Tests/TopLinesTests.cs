using System.Numerics;
using Ballast.Render;
using DarkTerritory.Game;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The HUD's top lines kept apart (queue #494, ARCHITECTURE §8 note 764; the director, 14 Oct: "We have the UI at the top.
/// Wreck yard is down the spur. It overlaps with Dawn"). The top left's fine print said what's down a spur in one long line,
/// which ran under the dawn clock at the top centre. Beside the centre's lines it now keeps to their left, wrapped, or starts
/// under them; under them it has the frame's width.
/// </summary>
public sealed class TopLinesTests
{
    const string Spur = "WRECK YARD IS DOWN THE SPUR: ENGINE + 3 CARS FIT, CUT THE REST";

    /// <summary>Each rectangle drawn (two triangles, six vertices; a glyph is a few of them), as its box.</summary>
    static List<(float Left, float Top, float Right, float Bottom)> Boxes(Overlay o) =>
        [.. o.Vertices.Chunk(6).Select(q => (q.Min(v => v.Position.X), q.Min(v => v.Position.Y), q.Max(v => v.Position.X), q.Max(v => v.Position.Y)))];

    /// <summary>How many rectangles a text is drawn with, on one line: the same however it's wrapped (a space draws none).</summary>
    static int Drawn(params string[] texts)
    {
        var o = new Overlay();
        foreach (var t in texts)
            o.Text(0, 0, t, Vector4.One);
        return Boxes(o).Count;
    }

    /// <summary>Every TEXT SIZE on the windows a player draws at: the fine print's scale is the HUD's at each (half at 1080p, whole at 720p).</summary>
    public static TheoryData<double, int> SizesAndScreens() =>
        [.. Settings.TextSizes.SelectMany(s => new[] { 540, 720, 1080, 1440 }.Select(screen => (s, screen)))];

    [Theory]
    [MemberData(nameof(SizesAndScreens))]
    public void TheTopLeftKeepsOutOfTheDawnClock(double size, int screen)
    {
        var (w, h) = new Settings { TextSize = size }.Canvas;
        float k = Hud.PromptScaleAt((float)screen / h, (float)size);
        // The dawn clock and a place's name under it, as TopCentre draws them, and the band it reports.
        var centre = new Overlay();
        int line = centre.Font.LineHeight;
        centre.TextCentred(w / 2f, 6, "DAWN 2:30", Vector4.One, 1);
        centre.TextCentred(w / 2f, 6 + line + 4, "WRECK YARD", Vector4.One, 1);
        float half = Math.Max(centre.Measure("DAWN 2:30"), centre.Measure("WRECK YARD")) / 2;
        var band = new Hud.TopBand(6 + 2 * line + 4, half);

        var left = new Overlay();
        float end = Hud.TopLeft(left, [(Spur, Vector4.One), ("STOPPED AT WRECK YARD", Vector4.One)], band, w, k);
        var lines = Boxes(left);
        var clock = Boxes(centre);
        float clockLeft = clock.Min(b => b.Left);
        // Nothing of the top left's beside the band reaches it; and every word was drawn (it wrapped, it wasn't cut).
        Assert.All(lines.Where(b => b.Top < band.Bottom), b => Assert.True(b.Right < clockLeft, $"at {size:0%} on {screen}p a glyph at x {b.Right} runs into the clock at {clockLeft}"));
        Assert.Equal(Drawn(Spur, "STOPPED AT WRECK YARD"), lines.Count);
        // Inside the frame.
        Assert.All(lines, b => Assert.True(b.Left >= 0 && b.Right <= w, $"at {size:0%} on {screen}p a glyph at {b.Left}..{b.Right} leaves the {w}-wide frame"));
        Assert.True(end > 6);
    }

    [Fact]
    public void WithNoRoomBesideTheCentreALineStartsUnderIt()
    {
        // A centre line nearly the frame's width (the news, note 663): a word won't fit beside it, so the line goes under.
        var band = new Hud.TopBand(30, 200);
        var o = new Overlay();
        Hud.TopLeft(o, [(Spur, Vector4.One)], band, 480, 1);
        Assert.All(Boxes(o), b => Assert.True(b.Top >= band.Bottom, $"a glyph at y {b.Top} is beside the band"));
    }

    [Fact]
    public void WithNothingAtTheTopCentreALineHasTheFrame()
    {
        // Nothing centred (no clock, no place): the spur's line is one line, as it was.
        var o = new Overlay();
        float end = Hud.TopLeft(o, [(Spur, Vector4.One)], new Hud.TopBand(6, 0), 480, 0.5f);
        var boxes = Boxes(o);
        Assert.Equal(Drawn(Spur), boxes.Count);
        Assert.True(boxes.Max(b => b.Bottom) - boxes.Min(b => b.Top) <= o.Font.LineHeight, "one row");
        Assert.InRange(end, 7, 6 + (o.Font.LineHeight + 1) * 0.5f + 0.01f);
    }

    [Fact]
    public void AtSevenTwentyTheSpursLineWrapsBesideTheClock()
    {
        // The director's case: at a scale of 1 (a 720p window, or TEXT SIZE 150%), the spur's line is 372 pixels on a
        // 480-wide canvas, past the middle; beside the clock it's two rows or more, each left of it.
        var band = new Hud.TopBand(30, 30);
        var o = new Overlay();
        Hud.TopLeft(o, [(Spur, Vector4.One)], band, 480, 1);
        var boxes = Boxes(o);
        Assert.Equal(Drawn(Spur), boxes.Count);
        Assert.True(boxes.Max(b => b.Bottom) - boxes.Min(b => b.Top) > o.Font.LineHeight, "wrapped");
        Assert.All(boxes.Where(b => b.Top < band.Bottom), b => Assert.True(b.Right <= 240 - 30 - 10));
    }
}
