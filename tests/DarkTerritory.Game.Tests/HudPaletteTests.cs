using Ballast;
using Ballast.Render;
using DarkTerritory.Game;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// COLOURS (note 348; GDD §32 "Accessibility"): the HUD's colours that mean something (good, a warning, danger) told apart
/// by a player who can't tell red from green. Each colour is seen as a protanope, deuteranope or tritanope would see it
/// (Machado, Oliveira and Fernandes 2009, full severity, on linear sRGB) and measured in CIELAB, where under 20 apart reads
/// as one colour.
/// </summary>
public class HudPaletteTests
{
    static readonly string Content = DataFile.FindContentRoot();

    static readonly double[][][] Deficiencies =
    [
        [[0.152286, 1.052583, -0.204868], [0.114503, 0.786281, 0.099216], [-0.003882, -0.048116, 1.051998]], // protanopia
        [[0.367322, 0.860646, -0.227968], [0.280085, 0.672501, 0.047413], [-0.011820, 0.042940, 0.968881]], // deuteranopia
        [[1.255528, -0.076749, -0.178779], [-0.078411, 0.930809, 0.147602], [0.004733, 0.691367, 0.303900]], // tritanopia
    ];

    static double Linear(double c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

    static (double L, double A, double B) Lab(double r, double g, double b)
    {
        double x = 0.4124 * r + 0.3576 * g + 0.1805 * b, y = 0.2126 * r + 0.7152 * g + 0.0722 * b, z = 0.0193 * r + 0.1192 * g + 0.9505 * b;
        static double F(double t) => t > 0.008856 ? Math.Cbrt(t) : 7.787 * t + 16.0 / 116;
        double fx = F(x / 0.9505), fy = F(y), fz = F(z / 1.089);
        return (116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz));
    }

    static (double, double, double) Seen(double[] colour, double[][] m)
    {
        var l = colour.Select(Linear).ToArray();
        double Row(int i) => Math.Clamp(m[i][0] * l[0] + m[i][1] * l[1] + m[i][2] * l[2], 0, 1);
        return Lab(Row(0), Row(1), Row(2));
    }

    static double Apart(double[] a, double[] b, double[][] m)
    {
        var (l1, a1, b1) = Seen(a, m);
        var (l2, a2, b2) = Seen(b, m);
        return Math.Sqrt((l1 - l2) * (l1 - l2) + (a1 - a2) * (a1 - a2) + (b1 - b2) * (b1 - b2));
    }

    static double Closest(HudPalette p) => Deficiencies.Min(m => new[]
        { Apart(p.Good, p.Warn, m), Apart(p.Good, p.Danger, m), Apart(p.Warn, p.Danger, m) }.Min());

    [Fact]
    public void TheColourblindPaletteTellsGoodWarningAndDangerApartForEveryone()
    {
        var t = DataFile.Load<HudTuning>(Path.Combine(Content, HudTuning.File));
        // As drawn, a protanope sees the ping's good and its warning, and SPEAKING beside a heading, as one colour.
        Assert.True(Closest(t.Standard) < 20, $"standard {Closest(t.Standard):0.0}");
        Assert.True(Closest(t.Colourblind) >= 40, $"colourblind {Closest(t.Colourblind):0.0}");
        // And with full colour vision it's no worse than the standard.
        double[][] normal = [[1, 0, 0], [0, 1, 0], [0, 0, 1]];
        Assert.True(new[] { Apart(t.Colourblind.Good, t.Colourblind.Warn, normal), Apart(t.Colourblind.Warn, t.Colourblind.Danger, normal) }.Min() >= 40);
    }

    [Fact]
    public void ColoursChoosesWhatTheHudDraws()
    {
        // The defaults are the file's (QuietHudTests pins that), so the HUD's own tuning is the file's palettes.
        var t = Hud.Tuning;
        var was = Hud.Keys;
        try
        {
            var s = new PrototypeSession(Content, "test-loop", 4);
            // The roster's SPEAKING is the good colour: green as drawn, blue in the colourblind palette.
            var (lines, heard) = Staging.Roster(s.Train, Content);
            foreach (var (colours, good) in new[] { (HudColours.Standard, t.Standard.GoodColour), (HudColours.Colourblind, t.Colourblind.GoodColour) })
            {
                Hud.Keys = new Settings { Colours = colours };
                var o = new Overlay();
                Hud.Roster(o, 480, 270, lines, heard);
                Assert.Contains(o.Vertices, v => v.Colour == good);
            }
        }
        finally
        {
            Hud.Keys = was;
        }
    }
}
