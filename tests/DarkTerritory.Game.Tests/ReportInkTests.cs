using Ballast;
using DarkTerritory.Game;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The run's report inked by what each line cost the crew (note 416; note 190's not-yet, where everything but a death, a
/// rescue and the night's end was as dim as a grab somebody was pulled out of), and, when they don't all fit, the lines
/// that matter most kept.
/// </summary>
// Hud.Keys is the HUD's settings, static: these set it (note 390).
[Collection("Hud.Keys")]
public sealed class ReportInkTests : IDisposable
{
    static readonly string Content = DataFile.FindContentRoot();
    readonly Settings _keys = Hud.Keys;
    readonly HudTuning _tuning = Hud.Tuning;

    public void Dispose()
    {
        Hud.Keys = _keys;
        Hud.Tuning = _tuning;
    }

    static readonly IncidentKind[] Took = [IncidentKind.Derailed, IncidentKind.Stranded, IncidentKind.CarLost, IncidentKind.Fire, IncidentKind.Nest,
        IncidentKind.Aboard, IncidentKind.Runaway, IncidentKind.Points, IncidentKind.Rupture, IncidentKind.Struck];
    static readonly IncidentKind[] WentWell = [IncidentKind.Rescue, IncidentKind.Slain];
    static readonly IncidentKind[] Faint = [IncidentKind.Grab, IncidentKind.Voted, IncidentKind.Punished, IncidentKind.Drawn];

    [Theory]
    [InlineData(HudColours.Standard)]
    [InlineData(HudColours.Colourblind)]
    public void EachLineIsInkedByWhatItCost(HudColours colours)
    {
        Hud.Tuning = DataFile.Load<HudTuning>(Path.Combine(Content, HudTuning.File));
        Hud.Keys = new Settings { Colours = colours };
        var palette = colours == HudColours.Colourblind ? Hud.Tuning.Colourblind : Hud.Tuning.Standard;
        // Every kind has its ink, and the lists above are all of them.
        Assert.Equal(Enum.GetValues<IncidentKind>().Length, Took.Length + WentWell.Length + Faint.Length + 1);
        Assert.All(Took, k => Assert.Equal(palette.WarnColour, Hud.ReportInk(k)));
        Assert.All(WentWell, k => Assert.Equal(palette.GoodColour, Hud.ReportInk(k)));
        var dim = Hud.ReportInk(IncidentKind.Grab);
        Assert.All(Faint, k => Assert.Equal(dim, Hud.ReportInk(k)));
        // A death in the report's own ink: brighter than the faint lines, and none of the palette's.
        var death = Hud.ReportInk(IncidentKind.Death);
        Assert.True(death.X + death.Y + death.Z > dim.X + dim.Y + dim.Z);
        Assert.DoesNotContain(death, new[] { palette.WarnColour, palette.GoodColour, palette.DangerColour });
    }

    [Fact]
    public void ALongNightKeepsItsEndingAndItsDeathsInTheOrderTheyHappened()
    {
        int G = Hud.ReportRank(IncidentKind.Grab), D = Hud.ReportRank(IncidentKind.Death), L = Hud.ReportRank(IncidentKind.CarLost),
            R = Hud.ReportRank(IncidentKind.Rescue), E = Hud.ReportRank(IncidentKind.Derailed);
        // Ten lines, a row each, in the order they happened: grabs, a death, a rescue, a car lost, more grabs, the derailment.
        (float, int)[] lines = [(1, G), (1, G), (1, D), (1, R), (1, G), (1, L), (1, G), (1, G), (1, G), (1, E)];
        // All of them fit: all of them, as they were.
        Assert.Equal(Enumerable.Range(0, 10), Hud.ReportKept(lines, 10, 1));
        // Room for five beside the "and n more" row: the end, the death, the car, the rescue, then the first grab.
        Assert.Equal([0, 2, 3, 5, 9], Hud.ReportKept(lines, 6, 1));
        // Room for two: the end and the death, never the first two grabs.
        Assert.Equal([2, 9], Hud.ReportKept(lines, 3, 1));
        // A line too tall for what's left is passed over for a shorter one of a lower rank.
        Assert.Equal([0, 2], Hud.ReportKept([(1, G), (3, D), (1, E)], 3, 1));
    }
}
