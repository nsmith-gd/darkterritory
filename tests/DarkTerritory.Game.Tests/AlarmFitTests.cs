using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Game;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The HUD's alarms and the radio inside the frame at every TEXT SIZE (note 441). An urgent headline is twice the HUD's size,
/// so on 150%'s 320-wide canvas TOO FAST FOR THE BEND AHEAD ran off both sides of the frame; the radio card cut the clerk's
/// lines at its edge, mid-word. Every alarm at its longest, the line's own warnings staged, and the radio's manifest and
/// tally, on the windows a player draws at.
/// </summary>
public sealed class AlarmFitTests
{
    static readonly string Content = DataFile.FindContentRoot();

    public static TheoryData<double, int> SizesAndScreens() =>
        [.. Settings.TextSizes.SelectMany(s => new[] { 540, 720, 1080, 1440 }.Select(screen => (s, screen)))];

    static void Inside(Overlay o, int w, int h, string what)
    {
        Assert.True(o.Count > 0, $"{what} drew nothing");
        var outside = o.Vertices.Where(v => v.Position.X < -0.01 || v.Position.X > w + 0.01 || v.Position.Y < -0.01 || v.Position.Y > h + 0.01).ToList();
        Assert.True(outside.Count == 0, $"{what} at {w}x{h}: {outside.Count} vertices outside, the first at {outside.FirstOrDefault().Position}");
    }

    // Every headline the alarms raise (Hud's Alerts), with what's under it at its longest: the figures at three digits, the
    // vent bound to a key with a space in it (Bound's "[LEFT CTRL]"), a crewmate's long name.
    static readonly (string Head, bool Urgent, string Line)[] Alarms =
    [
        ("TOO FAST FOR THE BEND AHEAD", true, "105 KM/H BEND IN 850 M, DERAILS OVER 115. YOU'RE AT 140: BRAKE"),
        ("FLANGES SCREAMING: YOU'RE COMING OFF", true, "140 KM/H ON A 105 KM/H BEND. IT DERAILS OVER 115: BRAKE NOW"),
        ("THE BEND IS PULLING HARD", false, "140 KM/H, OVER ITS BOARD: EASE OFF"),
        ("BUFFERS AHEAD: THE LINE ENDS", true, "END OF THE LINE IN 850 M, OFF IT OVER 115 KM/H. YOU'RE AT 140: BRAKE"),
        ("DOWN A DEAD LINE", false, "THE LINE ENDS IN 850 M. STOP, BACK UP, SET THE POINTS BACK BY HAND"),
        ("LOW CLEARANCE", true, "TUNNEL MOUTH 1850 M AHEAD: GET OFF THE ROOF"),
        ("TOO FAST FOR THE BRIDGE", true, "105 KM/H BOARD 1850 M AHEAD: GET OFF THE ROOF"),
        ("VENT! RUPTURE IN 20S", true, "VENT : HOLD [LEFT CTRL]"),
        ("BOILER RUPTURED", true, "REPAIR KIT: WITH A CREWMATE"),
        ("SOMETHING HAS YOU", true, "STRUGGLE : HOLD [E]"),
        ("CONNECTION LOST", true, "RECONNECT : [F5]"),
        ("DERAILED", true, "PRESSURE IN THE RED   VENT : HOLD [LEFT CTRL]"),
        ("RECONNECTING", false, "THE WHISTLE: BARTHOLOMEW-FAIRWEATHER ON THE CORD"),
    ];

    [Theory]
    [MemberData(nameof(SizesAndScreens))]
    public void EveryAlarmKeepsInsideTheFrame(double size, int screen)
    {
        var (w, h) = new Settings { TextSize = size }.Canvas;
        float k = Hud.PromptScaleAt((float)screen / h, (float)size);
        foreach (var (head, urgent, line) in Alarms)
        {
            var o = new Overlay();
            float y = Hud.Headline(o, w, h * 0.28f, head, Vector4.One, urgent);
            y = Hud.UnderHeadline(o, w, y, line, Vector4.One, k);
            Assert.True(y < h, $"{head} at {size:0%} on {screen}p ends under the frame, at {y}");
            Inside(o, w, h, head);
        }
        // The run's end's lines are at the HUD's own size.
        var end = new Overlay();
        Hud.UnderHeadline(end, w, h * 0.28f, "RECOVERY AT FIRST LIGHT. RECOVERY IS CHARGEABLE: 1450 SCRIP", Vector4.One, 1, fine: false);
        Inside(end, w, h, "the stranding's recovery");
    }

    [Fact]
    public void AHeadlineTooWideWrapsBetweenItsWordsAndAKeycapStaysWhole()
    {
        // 150%: 320 wide. Twice the size, TOO FAST FOR THE BEND AHEAD is 27 letters, 12 pixels each: two rows.
        var o = new Overlay();
        float one = Hud.Headline(o, 480, 0, "TOO FAST FOR THE BEND AHEAD", Vector4.One);
        o = new Overlay();
        float two = Hud.Headline(o, 320, 0, "TOO FAST FOR THE BEND AHEAD", Vector4.One);
        Assert.Equal(2 * one - 2, two);
        // Balanced, not a word left on its own.
        Assert.Equal(["TOO FAST FOR", "THE BEND AHEAD"], Hud.HeadlineRows(o, 320, "TOO FAST FOR THE BEND AHEAD", urgent: true));
        // A keycap is never split across rows, however narrow.
        foreach (string row in Hud.Fitted(new Overlay(), "PRESSURE IN THE RED   VENT : HOLD [LEFT CTRL]", 60, 1))
            Assert.Equal(row.Count(c => c == '['), row.Count(c => c == ']'));
        Assert.Equal(["PRESSURE IN THE RED", "VENT : HOLD [LEFT CTRL]"], Hud.Fitted(new Overlay(), "PRESSURE IN THE RED   VENT : HOLD [LEFT CTRL]", 200, 1));
    }

    // The line's own warnings as a player meets them (the screenshots' staging): the cab's bend warning coming and on it, a
    // roof rider's tunnel and bend.
    static readonly Lazy<(string What, PrototypeSession Session)[]> Staged = new(() =>
    {
        var route = Sim.LineGen.Routes.Generate(Content, "deepTerritory:2", 6);
        return
        [
            ("the bend coming", Staging.BendWarning(Content, route, 6, 4)),
            ("on the bend", Staging.BendWarning(Content, route, 6, 0)),
            ("the tunnel", Staging.RoofWarning(Content, route, 6, "tunnel")),
            ("the roof's bend", Staging.RoofWarning(Content, route, 6, "bend")),
        ];
    });

    [Theory]
    [MemberData(nameof(SizesAndScreens))]
    public void TheLinesWarningsAsTheyCome(double size, int screen)
    {
        var (w, h) = new Settings { TextSize = size }.Canvas;
        foreach (var (what, s) in Staged.Value)
        {
            Assert.True(Hud.BendWarningLines(s) is not null || Hud.RoofWarningLines(s) is not null, $"{what}: no warning staged");
            var o = new Overlay();
            Hud.Build(o, w, h, s, pixels: (float)screen / h);
            Inside(o, w, h, what);
        }
    }

    public static TheoryData<double> Sizes() => [.. Settings.TextSizes];

    // The card is drawn on the canvas alone (its size from the canvas's height): the window doesn't change it.
    [Theory]
    [MemberData(nameof(Sizes))]
    public void TheRadioCardWrapsTheClerksLinesWithinTheFrame(double size)
    {
        var (w, h) = new Settings { TextSize = size }.Canvas;
        var s = new PrototypeSession(Content, RouteGenerator.Generate(RouteTuning.Load(Content), RouteTier.Frontier, 1), 4, enemies: false);
        var radio = s.World.Run?.Tuning.Radio ?? new();
        foreach (var lines in new[] { Radio.Manifest(s.World, [0, 1, 2, 3]), Radio.Tally(Staging.Report(s.World, RunEnd.Delivered)) })
        {
            // Through the reading, a frame every half second.
            double length = Radio.Length(lines, radio);
            for (double t = 0.25; t < length + 1; t += 0.5)
            {
                var o = new Overlay();
                Hud.RadioCard(o, w, h, lines, t, radio);
                if (o.Count > 0)
                    Inside(o, w, h, $"the radio at {t:0.0}s");
            }
            // Each line read whole is on the card whole, a row at a time, none wider than the card.
            float inner = (w * 0.56f - 12) / Math.Max(1, h / 360);
            var font = new Overlay();
            for (int shown = 1; shown <= lines.Count; shown++)
            {
                var rows = Hud.RadioRows(font, inner, lines, shown, 1);
                Assert.InRange(rows.Count, 1, Hud.RadioRowsKept);
                Assert.All(rows, r => Assert.True(font.Font.Measure(r.Text) <= inner, $"\"{r.Text}\" is wider than the card"));
                var read = rows.Where(r => r.Reading).Select(r => r.Text).ToList();
                string whole = lines[shown - 1].ToUpperInvariant();
                if (read.Count < Hud.RadioRowsKept)
                    Assert.Equal(whole, string.Join(" ", read));
                else
                    Assert.EndsWith(string.Join(" ", read), whole, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void TheRadioTypesTheLineBeingReadIntoRowsAlreadyLaidOut()
    {
        // Half typed: the rows it'll take are already there (the card doesn't grow under it), the rest blank.
        var o = new Overlay();
        string[] lines = ["Coal: 120. Powder and shot: 40. Repairs: 15."];
        var whole = Hud.RadioRows(o, 150, lines, 1, 1);
        var half = Hud.RadioRows(o, 150, lines, 1, 0.5);
        Assert.True(whole.Count > 1);
        Assert.Equal(whole.Count, half.Count);
        Assert.StartsWith(string.Concat(half.Select(r => r.Text)), string.Concat(whole.Select(r => r.Text)), StringComparison.Ordinal);
        Assert.Equal("", half[^1].Text);
    }
}
