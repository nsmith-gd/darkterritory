using Ballast;
using Ballast.Render;
using DarkTerritory.Game;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// TEXT SIZE (note 347; the director, 8 Oct, on accessibility: "these are all quite important"): the overlay's canvas is drawn
/// smaller and stretched over the same window, so at every size on offer the HUD and the menus still lie inside the frame.
/// </summary>
public sealed class TextSizeTests : IDisposable
{
    static readonly string Content = DataFile.FindContentRoot();
    readonly string _dir = Path.Combine(Path.GetTempPath(), $"dt-textsize-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    public static TheoryData<double> Sizes() => [.. Settings.TextSizes];

    static void Inside(Overlay o, int w, int h, string what)
    {
        Assert.True(o.Count > 0, $"{what} drew nothing");
        var outside = o.Vertices.Where(v => v.Position.X < -2 || v.Position.X > w + 2 || v.Position.Y < -2 || v.Position.Y > h + 2).ToList();
        Assert.True(outside.Count == 0, $"{what} at {w}x{h}: {outside.Count} vertices outside, the first at {outside.FirstOrDefault().Position}");
    }

    [Theory]
    [InlineData(540)]
    [InlineData(720)]
    [InlineData(1080)]
    [InlineData(1440)]
    public void TheFinePrintNeverShrinksAsTheTextGrows(int screen)
    {
        // Note 351: on a 720p window 150% drew the prompts and the corner smaller than 100% did (the half step).
        float was = 0;
        foreach (double size in Settings.TextSizes)
        {
            var (_, h) = new Settings { TextSize = size }.Canvas;
            float pixels = (float)screen / h, onScreen = Hud.PromptScaleAt(pixels, (float)size) * pixels;
            Assert.True(onScreen >= was, $"{screen}p at {size:0%}: {onScreen:0.##} screen pixels a font pixel, under {was:0.##}");
            Assert.True(onScreen >= 2, $"{screen}p at {size:0%}: {onScreen:0.##}");
            was = onScreen;
        }
    }

    [Fact]
    public void EachSizeIsASmallerCanvasOfTheSameShape()
    {
        Assert.Equal((480, 270), new Settings().Canvas);
        Assert.Equal((384, 216), new Settings { TextSize = 1.25 }.Canvas);
        Assert.Equal((320, 180), new Settings { TextSize = 1.5 }.Canvas);
        // Anything else read from a settings file is 100%.
        Assert.Equal((480, 270), new Settings { TextSize = 3 }.Canvas);
    }

    // Every size on the windows a player draws at (note 351: a 720p window has the prompts' fine print a whole step bigger).
    public static TheoryData<double, int> SizesAndScreens() =>
        [.. Settings.TextSizes.SelectMany(s => new[] { 540, 720, 1080, 1440 }.Select(screen => (s, screen)))];

    [Theory]
    [MemberData(nameof(SizesAndScreens))]
    public void TheHudItsPanelsAndTheReportFitAtEverySize(double size, int screen)
    {
        var (w, h) = new Settings { TextSize = size }.Canvas;
        var route = RouteGenerator.Generate(RouteTuning.Load(Content), RouteTier.Frontier, 1);
        var s = new PrototypeSession(Content, route, 6, enemies: false);
        var o = new Overlay();
        Hud.Build(o, w, h, s, pixels: (float)screen / h);
        Inside(o, w, h, "the HUD");
        // A new player's first nights' card in the yard (note 350), and what's heard (note 349).
        o = new Overlay();
        Hud.Build(o, w, h, s, pixels: (float)screen / h, firstNight: true, captions: ["[A SQUEAL AT THE WHEELS, BEHIND]", "[A CHILD CALLING, LEFT]"]);
        Inside(o, w, h, "the first nights' card");
        o = new Overlay();
        Hud.Supplies(o, w, h, s);
        Inside(o, w, h, "the supplies");
        var (lines, heard) = Staging.Roster(s.Train, Content);
        o = new Overlay();
        Hud.Roster(o, w, h, lines, heard);
        Inside(o, w, h, "the roster");
        // The run's end: the report, its settlement line and all (it wraps now, where it ran off the plate at 125%).
        s.World.Run!.MirrorReport(Staging.Report(s.World, RunEnd.Derailed));
        o = new Overlay();
        Hud.Build(o, w, h, s, pixels: (float)screen / h);
        Inside(o, w, h, "the report");
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void EveryMenuScreenFitsAtEverySize(double size)
    {
        var c = DataFile.Load<CampaignTuning>(Path.Combine(Content, CampaignTuning.File));
        var r = DataFile.Load<RunTuning>(Path.Combine(Content, RunTuning.File));
        var saves = new SaveSlots(Path.Combine(_dir, "saves"), c.SaveSlots);
        var m = new FrontEnd(c, r, saves, Path.Combine(_dir, "settings.json"), () => 42);
        saves.Save(Campaign.New(c, 1, "The Night Shift", 5));
        var (w, h) = new Settings { TextSize = size }.Canvas;
        foreach (var screen in Enum.GetValues<Screen>())
        {
            m.ShowFortress(1);
            m.Show(screen);
            // The longest list's last row too: a long screen scrolls rather than run off the foot.
            for (int i = 0; i < 40; i++)
                m.Down();
            var o = new Overlay();
            m.Draw(o, w, h);
            Inside(o, w, h, screen.ToString());
        }
    }

    [Fact]
    public void TextSizeIsASettingThatSteps()
    {
        var m = new FrontEnd(DataFile.Load<CampaignTuning>(Path.Combine(Content, CampaignTuning.File)),
            DataFile.Load<RunTuning>(Path.Combine(Content, RunTuning.File)), new SaveSlots(Path.Combine(_dir, "saves"), 3),
            Path.Combine(_dir, "settings.json"), () => 42);
        m.Show(Screen.Settings);
        int at = m.Items.ToList().FindIndex(i => i.Label.StartsWith("TEXT SIZE", StringComparison.Ordinal));
        Assert.True(at >= 0);
        while (m.Selected != at)
            m.Down();
        Assert.Equal("TEXT SIZE: 100%", m.Items[at].Label);
        m.Right();
        Assert.Equal("TEXT SIZE: 125%", m.Items[at].Label);
        Assert.Equal(1.25, Settings.Load(Path.Combine(_dir, "settings.json")).TextSize);
        m.Right();
        m.Right();
        Assert.Equal("TEXT SIZE: 100%", m.Items[at].Label);
    }
}
