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

    /// <summary>Each glyph quad's box as drawn (four vertices a quad).</summary>
    static List<(float Left, float Top, float Right, float Bottom)> Boxes(Overlay o) =>
        [.. o.Vertices.Chunk(4).Select(q => (q.Min(v => v.Position.X), q.Min(v => v.Position.Y), q.Max(v => v.Position.X), q.Max(v => v.Position.Y)))];

    public static TheoryData<double> Sizes() => [.. Settings.TextSizes];

    [Theory]
    [MemberData(nameof(Sizes))]
    public void TheTopLeftKeepsOutOfTheDawnClock(double size)
    {
        var (w, _) = new Settings { TextSize = size }.Canvas;
        // The dawn clock and a place's name under it, as TopCentre draws them, and the band it reports.
        var centre = new Overlay();
        int line = centre.Font.LineHeight;
        centre.TextCentred(w / 2f, 6, "DAWN 2:30", Vector4.One, 1);
        centre.TextCentred(w / 2f, 6 + line + 4, "WRECK YARD", Vector4.One, 1);
        float half = Math.Max(centre.Measure("DAWN 2:30"), centre.Measure("WRECK YARD")) / 2;
        var band = new Hud.TopBand(6 + 2 * line + 4, half);

        var left = new Overlay();
        float end = Hud.TopLeft(left, [(Spur, Vector4.One), ("STOPPED AT WRECK YARD", Vector4.One)], band, w);
        var lines = Boxes(left);
        var clock = Boxes(centre);
        float clockLeft = clock.Min(b => b.Left);
        // Nothing of the top left's beside the band reaches it; and every word was drawn (it wrapped, it wasn't cut).
        Assert.All(lines.Where(b => b.Top < band.Bottom), b => Assert.True(b.Right < clockLeft, $"at {size:0%} a glyph at x {b.Right} runs into the clock at {clockLeft}"));
        Assert.Equal(Spur.Count(c => c != ' ') + "STOPPED AT WRECK YARD".Count(c => c != ' '), lines.Count);
        // Inside the frame.
        Assert.All(lines, b => Assert.True(b.Left >= 0 && b.Right <= w, $"at {size:0%} a glyph at {b.Left}..{b.Right} leaves the {w}-wide frame"));
        Assert.True(end > 6);
    }

    [Fact]
    public void WithNoRoomBesideTheCentreALineStartsUnderIt()
    {
        // A centre line nearly the frame's width (the news, note 663): a word won't fit beside it, so the line goes under.
        var band = new Hud.TopBand(30, 200);
        var o = new Overlay();
        Hud.TopLeft(o, [(Spur, Vector4.One)], band, 480);
        Assert.All(Boxes(o), b => Assert.True(b.Top >= band.Bottom, $"a glyph at y {b.Top} is beside the band"));
    }

    [Fact]
    public void WithNothingAtTheTopCentreALineHasTheFrame()
    {
        // Nothing centred (no clock, no place): the spur's line is one line, as it was.
        var o = new Overlay();
        float end = Hud.TopLeft(o, [(Spur, Vector4.One)], new Hud.TopBand(6, 0), 480);
        Assert.Equal(1, Boxes(o).Select(b => b.Top).Distinct().Count());
        Assert.Equal(6 + (o.Font.LineHeight + 1) * Hud.Fine, end, 3);
    }
}
