using Ballast;
using Ballast.Render;
using DarkTerritory.Game;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// FIRST NIGHTS (note 350; GDD §32 "Accessibility"; the director, 8 Oct: "these are all quite important"): a tip on the loading
/// screen, and the core controls on a card in the yard for a new player's first nights. Still "learned, not told": the tips
/// are the shape of a night and the crew's habits, never which creature wants what, or what to do when.
/// </summary>
public sealed class FirstNightsTests : IDisposable
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly Onboarding.Data File = Onboarding.Load(Content);
    readonly string _dir = Path.Combine(Path.GetTempPath(), $"dt-firstnights-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    internal static FrontEnd Menu(string dir) =>
        new(DataFile.Load<CampaignTuning>(Path.Combine(Content, CampaignTuning.File)), DataFile.Load<RunTuning>(Path.Combine(Content, RunTuning.File)),
            new SaveSlots(Path.Combine(dir, "saves"), 3), Path.Combine(dir, "settings.json"), () => 42);

    /// <summary>Selects the settings entry named <paramref name="name"/> and steps it from one value to the next.</summary>
    internal static void Toggle(FrontEnd m, string name, string from, string to)
    {
        int at = m.Items.ToList().FindIndex(i => !i.Heading && i.Label.StartsWith(name + ":", StringComparison.Ordinal));
        Assert.True(at >= 0, name);
        while (m.Selected != at)
            m.Down();
        Assert.Equal($"{name}: {from}", m.Items[at].Label);
        m.Right();
        Assert.Equal($"{name}: {to}", m.Items[at].Label);
    }

    [Fact]
    public void TheTipsTellTheShapeOfANightNotWhatToDo()
    {
        Assert.NotEmpty(File.Tips);
        var creatures = Enum.GetNames<EnemyKind>().Where(k => k is not (nameof(EnemyKind.CarFire) or nameof(EnemyKind.Sleepers)))
            .SelectMany(k => System.Text.RegularExpressions.Regex.Split(k, "(?<!^)(?=[A-Z])")).Select(w => w.ToLowerInvariant())
            .Where(w => w is not ("fire" or "car" or "track")).Concat(["tippy", "toesie", "hound", "hounds", "doll", "whistler", "choir", "gaunt", "fireflies"]).Distinct().ToList();
        foreach (var tip in File.Tips)
        {
            var words = tip.ToLowerInvariant().Split([' ', ',', '.', '\''], StringSplitOptions.RemoveEmptyEntries);
            foreach (var c in creatures)
                Assert.False(words.Contains(c), $"\"{tip}\" names {c}");
            // No recipes: "if you hear it, run" is the night's to teach (GDD §32).
            Assert.DoesNotContain("if you", tip, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("when you", tip, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void EachNightHasItsOwnTipAndFirstNightsOffHasNone()
    {
        var on = new Settings();
        Assert.Equal(File.Tips[0], Onboarding.Tip(File, on, 0));
        Assert.Equal(File.Tips[1], Onboarding.Tip(File, on, 1));
        Assert.Equal(File.Tips[0], Onboarding.Tip(File, on, File.Tips.Length));
        Assert.Null(Onboarding.Tip(File, on with { FirstNights = false }, 0));
        Assert.Null(Onboarding.Tip(File with { Tips = [] }, on, 0));
    }

    [Fact]
    public void TheCardIsUpForTheFirstNightsOnly()
    {
        var on = new Settings();
        Assert.True(File.FirstNights > 0);
        Assert.True(Onboarding.FirstNight(File, on, 0));
        Assert.True(Onboarding.FirstNight(File, on, File.FirstNights - 1));
        Assert.False(Onboarding.FirstNight(File, on, File.FirstNights));
        Assert.False(Onboarding.FirstNight(File, on with { FirstNights = false }, 0));
    }

    [Fact]
    public void TheCardSaysThePlayersOwnKeys()
    {
        var keys = new Settings().Bind(Control.Talk, "B");
        var card = Onboarding.Card(keys);
        Assert.Contains(card, r => r.Contains("TALK : HOLD [B]", StringComparison.Ordinal));
        Assert.DoesNotContain(card, r => r.Contains("TALK : HOLD [V]", StringComparison.Ordinal));
    }

    [Fact]
    public void TheProfileCountsTheNightsSeenToTheirEnd()
    {
        var profile = new PlayerProfile(Path.Combine(_dir, "profile.json"));
        Assert.Equal(0, profile.Load().Nights);
        profile.CountNight();
        Assert.Equal(2, profile.CountNight().Nights);
        Assert.Equal(2, new PlayerProfile(Path.Combine(_dir, "profile.json")).Load().Nights);
    }

    public static TheoryData<double> Sizes() => [.. Settings.TextSizes];

    [Theory]
    [MemberData(nameof(Sizes))]
    public void TheHudShowsTheCardInTheYardAndInTheFrame(double size)
    {
        var (w, h) = new Settings { TextSize = size }.Canvas;
        var s = new PrototypeSession(Content, Sim.LineGen.Routes.Generate(Content, "frontier:7", 4), 4, enemies: false);
        Assert.Equal(RunPhase.Yard, s.World.Run!.Phase);
        var without = new Overlay();
        Hud.Build(without, w, h, s, pixels: 1080f / h);
        var with = new Overlay();
        Hud.Build(with, w, h, s, pixels: 1080f / h, firstNight: true);
        Assert.True(with.Count > without.Count, "no card");
        var outside = with.Vertices.Where(v => v.Position.X < -2 || v.Position.X > w + 2 || v.Position.Y < -2 || v.Position.Y > h + 2).ToList();
        Assert.True(outside.Count == 0, $"{outside.Count} vertices outside {w}x{h}");
    }

    [Fact]
    public void FirstNightsIsASettingThatsOnUntilTurnedOff()
    {
        Assert.True(new Settings().FirstNights);
        var m = Menu(_dir);
        m.Show(Screen.Settings);
        Toggle(m, "FIRST NIGHTS", "ON", "OFF");
        Assert.False(Settings.Load(Path.Combine(_dir, "settings.json")).FirstNights);
    }
}
