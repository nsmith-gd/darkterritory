using Ballast;
using DarkTerritory.Game;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// HOLD KEYS (note 399; GDD §32 "Accessibility"): with TOGGLE a press latches run, the brake, talk, the radio and the crew
/// roster on and another lets go. Client-side only: the button held is what the host gets either way.
/// </summary>
public sealed class HoldLatchTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), $"dt-holdlatch-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void HoldIsTheKeyDown()
    {
        var latch = new HoldLatch();
        latch.Press(Control.Brake);
        Assert.False(latch.Held(Control.Brake, down: false));
        Assert.True(latch.Held(Control.Brake, down: true));
    }

    [Fact]
    public void TogglePressesOnAndOffAndTheKeyDownNoLongerCounts()
    {
        var latch = new HoldLatch { Toggles = true };
        foreach (var c in Settings.Toggleable)
        {
            Assert.False(latch.Held(c, down: true));
            latch.Press(c);
            Assert.True(latch.Held(c, down: false), $"{c} on");
            latch.Press(c);
            Assert.False(latch.Held(c, down: true), $"{c} off");
        }
    }

    [Fact]
    public void UseAndTheRestStayHolds()
    {
        // Use's taps and holds mean different things (a locker's tap, a hold's mend), so it's never latched.
        var latch = new HoldLatch { Toggles = true };
        foreach (var c in new[] { Control.Use, Control.Jump, Control.Forward, Control.Swing, Control.Whistle, Control.Emote })
        {
            latch.Press(c);
            Assert.False(latch.Held(c, down: false), $"{c} latched");
            Assert.True(latch.Held(c, down: true), $"{c} not held");
        }
    }

    [Fact]
    public void TurningItOffLetsGoOfEverything()
    {
        var latch = new HoldLatch { Toggles = true };
        latch.Press(Control.Run);
        latch.Press(Control.Radio);
        latch.Toggles = false;
        latch.Toggles = true;
        Assert.False(latch.Held(Control.Run, down: false));
        Assert.False(latch.Held(Control.Radio, down: false));
    }

    [Fact]
    public void ThePromptsSayAPressNotAHold()
    {
        var hold = Onboarding.Card(new Settings());
        Assert.Contains(hold, r => r.Contains("TALK : HOLD [V]", StringComparison.Ordinal));
        var toggle = Onboarding.Card(new Settings { ToggleHolds = true });
        Assert.Contains(toggle, r => r.Contains("TALK : [V]", StringComparison.Ordinal) && r.Contains("RADIO : [T]", StringComparison.Ordinal));
        Assert.Contains(toggle, r => r.Contains("CREW : [Q]", StringComparison.Ordinal));
        Assert.DoesNotContain(toggle, r => r.Contains("HOLD", StringComparison.Ordinal));
    }

    [Fact]
    public void HoldKeysIsASettingThatSaves()
    {
        Assert.False(new Settings().ToggleHolds);
        var m = FirstNightsTests.Menu(_dir);
        m.Show(Screen.Settings);
        Assert.Contains(m.Items, i => i.Label == "VOICE: PUSH TO TALK (HOLD V)" || i.Label == "VOICE: OPEN MIC");
        FirstNightsTests.Toggle(m, "HOLD KEYS", "HOLD", "TOGGLE");
        Assert.True(Settings.Load(Path.Combine(_dir, "settings.json")).ToggleHolds);
        Assert.NotEqual(new Settings(), new Settings { ToggleHolds = true });
    }
}
