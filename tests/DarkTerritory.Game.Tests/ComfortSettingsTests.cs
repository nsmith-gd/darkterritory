using Ballast;
using DarkTerritory.Game;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Game.Tests;

/// <summary>Comfort settings (note 297): the field of view, an inverted mouse and the camera's shake, saved as they change.</summary>
public sealed class ComfortSettingsTests : IDisposable
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly CampaignTuning C = DataFile.Load<CampaignTuning>(Path.Combine(Content, CampaignTuning.File));
    static readonly RunTuning R = DataFile.Load<RunTuning>(Path.Combine(Content, RunTuning.File));
    readonly string _dir = Path.Combine(Path.GetTempPath(), $"dt-comfort-{Guid.NewGuid():N}");

    string SettingsPath => Path.Combine(_dir, "settings.json");
    FrontEnd Menu() => new(C, R, new SaveSlots(Path.Combine(_dir, "saves"), C.SaveSlots), SettingsPath, () => 42);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    static void Pick(FrontEnd m, string label)
    {
        int i = m.Items.ToList().FindIndex(x => x.Label.StartsWith(label, StringComparison.Ordinal));
        Assert.True(i >= 0, $"no '{label}' on {m.Screen}: {string.Join(" | ", m.Items.Select(x => x.Label))}");
        while (m.Selected != i)
            m.Down();
    }

    [Fact]
    public void TheDefaultsAreTheGameAsItWasDrawn()
    {
        var s = new Settings();
        Assert.Equal(75f, s.EyeFov);
        Assert.False(s.InvertMouse);
        Assert.Equal(1, s.CameraShake);
        // A field of view that isn't on offer (a hand-edited file) is drawn at the default.
        Assert.Equal(75f, (s with { FieldOfView = 140 }).EyeFov);
    }

    [Fact]
    public void TheFieldOfViewStepsByFiveBetweenItsEndsAndIsSaved()
    {
        var m = Menu();
        m.Show(Screen.Settings);
        Pick(m, "FIELD OF VIEW");
        Assert.Equal("FIELD OF VIEW: 75", m.Items[m.Selected].Label);
        m.Right();
        Assert.Equal(80, m.Settings.FieldOfView);
        for (int i = 0; i < 10; i++)
            m.Right();
        Assert.Equal(90, m.Settings.FieldOfView);
        for (int i = 0; i < 10; i++)
            m.Left();
        Assert.Equal(60, m.Settings.FieldOfView);
        Assert.Equal(60, Settings.Load(SettingsPath).FieldOfView);
        Assert.Equal(60f, Settings.Load(SettingsPath).EyeFov);
    }

    [Fact]
    public void InvertMouseAndCameraShakeAreSavedAndChangeTheSettings()
    {
        var m = Menu();
        m.Show(Screen.Settings);
        Pick(m, "INVERT MOUSE");
        var before = m.Settings;
        m.Select();
        Assert.True(m.Settings.InvertMouse);
        // The app takes a change up when the settings differ: these count.
        Assert.False(before.Equals(m.Settings));
        Pick(m, "CAMERA SHAKE");
        Assert.Equal("CAMERA SHAKE: 100%", m.Items[m.Selected].Label);
        for (int i = 0; i < 4; i++)
            m.Left();
        Assert.Equal("CAMERA SHAKE: OFF", m.Items[m.Selected].Label);
        m.Right();
        Assert.Equal(0.25, m.Settings.CameraShake);
        var saved = Settings.Load(SettingsPath);
        Assert.True(saved.InvertMouse);
        Assert.Equal(0.25, saved.CameraShake);
        Assert.True(saved.Equals(m.Settings));
    }

    [Fact]
    public void TheyreOnTheInNightMenusSettingsToo()
    {
        var m = Menu();
        m.OpenNight(new NightMenu());
        m.Show(Screen.Settings);
        foreach (var label in new[] { "INVERT MOUSE", "FIELD OF VIEW", "CAMERA SHAKE" })
            Assert.Contains(m.Items, i => i.Label.StartsWith(label, StringComparison.Ordinal));
    }
}
