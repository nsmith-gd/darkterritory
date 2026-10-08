using DarkTerritory.Game;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The settings in sections (note 386): a heading over each group of rows, which the selection passes over and a click does
/// nothing to, so a player finds TEXT SIZE under ACCESSIBILITY rather than past the microphone.
/// </summary>
public sealed class SettingsSectionsTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), $"dt-sections-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    static readonly string[] Sections = ["YOU", "SOUND AND VOICE", "SCREEN", "CONTROLS AND COMFORT", "ACCESSIBILITY"];

    static string SectionOf(IReadOnlyList<MenuItem> items, string label)
    {
        int at = items.ToList().FindIndex(i => i.Label == label || i.Label.StartsWith(label + ":", StringComparison.Ordinal));
        Assert.True(at >= 0, label);
        return items.Take(at).Last(i => i.Heading).Label;
    }

    [Fact]
    public void EveryRowSitsUnderItsSection()
    {
        var m = FirstNightsTests.Menu(_dir);
        m.Show(Screen.Settings);
        var items = m.Items;
        Assert.Equal(Sections, items.Where(i => i.Heading).Select(i => i.Label));
        Assert.True(items[0].Heading, "a heading first");
        Assert.All(items.Where(i => i.Heading), h => Assert.False(h.Enabled));
        Assert.Equal("YOU", SectionOf(items, "PLAYER NAME"));
        Assert.Equal("YOU", SectionOf(items, "OUTFIT"));
        Assert.Equal("SOUND AND VOICE", SectionOf(items, "MIC LEVEL"));
        Assert.Equal("SCREEN", SectionOf(items, "RENDER SCALE"));
        Assert.Equal("CONTROLS AND COMFORT", SectionOf(items, "FIELD OF VIEW"));
        Assert.Equal("CONTROLS AND COMFORT", SectionOf(items, "CONTROLS"));
        foreach (var a in new[] { "TEXT SIZE", "COLOURS", "CAPTIONS", "HOLD KEYS", "FIRST NIGHTS" })
            Assert.Equal("ACCESSIBILITY", SectionOf(items, a));
        Assert.Equal("BACK", items[^1].Label);
    }

    [Fact]
    public void TheSelectionNeverRestsOnAHeading()
    {
        var m = FirstNightsTests.Menu(_dir);
        m.Show(Screen.Settings);
        Assert.StartsWith("PLAYER NAME", m.Items[m.Selected].Label, StringComparison.Ordinal);
        for (int i = 0; i < m.Items.Count * 2; i++)
        {
            m.Down();
            Assert.False(m.Items[m.Selected].Heading, $"down onto {m.Items[m.Selected].Label}");
        }
        for (int i = 0; i < m.Items.Count * 2; i++)
        {
            m.Up();
            Assert.False(m.Items[m.Selected].Heading, $"up onto {m.Items[m.Selected].Label}");
        }
    }

    [Fact]
    public void InANightTheSectionsKeepWhatTheNightHas()
    {
        // The night's menu (note 292) has no name to type and no headset's comfort; the sections stand, OUTFIT under YOU.
        var m = FirstNightsTests.Menu(_dir);
        m.OpenNight(new NightMenu());
        m.Show(Screen.Settings);
        var items = m.Items;
        Assert.Equal(Sections, items.Where(i => i.Heading).Select(i => i.Label));
        Assert.DoesNotContain(items, i => i.Label.StartsWith("PLAYER NAME", StringComparison.Ordinal));
        Assert.Equal("YOU", SectionOf(items, "OUTFIT"));
        Assert.StartsWith("OUTFIT", items[m.Selected].Label, StringComparison.Ordinal);
    }
}
