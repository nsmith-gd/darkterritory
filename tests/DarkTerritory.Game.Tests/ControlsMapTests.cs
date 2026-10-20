using Ballast;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// Note 898 (the director, 20 Oct 2026: "We shouldn't default any input to caps lock, find a different key easily accessible
/// to left hand that isn't taken up already for shove. Also update the control map in the github, it's out of date"): the
/// shove's key, the saved keys brought past it, and the controls map kept to the defaults.
/// </summary>
public class ControlsMapTests
{
    static readonly string Root = Path.GetDirectoryName(DataFile.FindContentRoot())!;

    /// <summary>The keys under a left hand on WASD (the number row's left half, the letters to T, G and B, its modifiers).</summary>
    static readonly HashSet<string> LeftHand = ["D1", "D2", "D3", "D4", "D5", "Q", "W", "E", "R", "T", "A", "S", "D", "F", "G",
        "Z", "X", "C", "V", "B", "Tab", "CapsLock", "LeftShift", "LeftCtrl", "LeftAlt", "Space"];

    [Fact]
    public void NothingDefaultsToCapsLockAndTheShoveIsUnderTheLeftHandOnAKeyOfItsOwn()
    {
        Assert.DoesNotContain("CapsLock", Controls.Defaults.Values);
        Assert.Equal("D5", Controls.Defaults[Control.Shove]);
        Assert.Contains(Controls.Defaults[Control.Shove], LeftHand);
        Assert.Equal("LeftAlt", Controls.Defaults[Control.Vent]);
        // No two controls on one key, and none on the menus' own.
        Assert.Equal(Controls.Defaults.Count, Controls.Defaults.Values.Distinct().Count());
        Assert.DoesNotContain(Controls.Defaults.Values, Controls.Reserved.Contains);
        // 5 isn't the hotbar's (its slots are 1 to Kit.Slots).
        Assert.True(Sim.Player.Kit.Slots < 5);
    }

    [Fact]
    public void ASavedShoveOnCapsLockMovesToFiveAndEveryOtherSavedKeyStays()
    {
        var saved = new Settings { Keys = new() { ["Shove"] = "CapsLock", ["Jump"] = "MouseMiddle", ["Push"] = "Space" } };
        var now = saved.Migrated();
        Assert.Equal("D5", now.KeyFor(Control.Shove));
        Assert.False(now.Keys.ContainsKey("Shove"));
        Assert.Equal("MouseMiddle", now.KeyFor(Control.Jump));
        Assert.Equal("Space", now.KeyFor(Control.Push));
        // A shove the player put somewhere else of their own stays there.
        Assert.Equal("M", new Settings { Keys = new() { ["Shove"] = "M" } }.Migrated().KeyFor(Control.Shove));
        // Once moved, moved: a second load changes nothing; nothing saved, nothing to move.
        Assert.Same(now, now.Migrated());
        var none = new Settings();
        Assert.Same(none, none.Migrated());
    }

    [Fact]
    public void APlayerWhoPutSomethingElseOnFiveKeepsTheShoveWhereItWas()
    {
        // The shove saved on Caps Lock, 5 the emote wheel's: the shove stays put.
        var held = new Settings { Keys = new() { ["Shove"] = "CapsLock", ["Emote"] = "D5" } }.Migrated();
        Assert.Equal("CapsLock", held.KeyFor(Control.Shove));
        Assert.Equal("D5", held.KeyFor(Control.Emote));
        // The shove never changed (on Caps Lock by default till now), 5 the player's for something else: it keeps Caps Lock,
        // so 5 doesn't do two things.
        var kept = new Settings { Keys = new() { ["Emote"] = "D5" } }.Migrated();
        Assert.Equal("CapsLock", kept.KeyFor(Control.Shove));
        var keys = Enum.GetValues<Control>().Select(kept.KeyFor).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void LoadingASavedFileMigratesIt()
    {
        string dir = Path.Combine(Path.GetTempPath(), "dt-controls-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(dir, "settings.json");
        try
        {
            new Settings { Keys = new() { ["Shove"] = "CapsLock" } }.Save(path);
            Assert.Equal("D5", Settings.Load(path).KeyFor(Control.Shove));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TheReadmeAndThePlayersSheetSayTheDefaults()
    {
        // `dt controls --write` rewrites both after a default moves.
        string readme = File.ReadAllText(Path.Combine(Root, ControlsMap.Readme));
        Assert.Equal(readme, ControlsMap.SpliceReadme(readme));
        string playing = File.ReadAllText(Path.Combine(Root, ControlsMap.Playing));
        Assert.Equal(playing, ControlsMap.SplicePlaying(playing));
        // Every control is on the map, on its key.
        foreach (var c in Enum.GetValues<Control>())
        {
            Assert.Contains($"| {ControlsMap.Key(Controls.Defaults[c])} | {Controls.Help(c)} |", readme);
            Assert.Contains($"{ControlsMap.Key(Controls.Defaults[c])}", playing);
        }
        Assert.Contains("| 5 | Shove the crewmate in front of you |", readme);
        Assert.Contains("| Left Alt | Vent the boiler's steam (held, in the cab) |", readme);
        Assert.DoesNotContain("Caps Lock", readme);
        Assert.DoesNotContain("Caps Lock", playing);
    }
}
