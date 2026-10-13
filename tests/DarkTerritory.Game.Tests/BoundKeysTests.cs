using System.Text.RegularExpressions;
using Ballast;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// Every key the HUD names is the player's own (note 426; T80's CONTROLS): the HUD writes each control's default and
/// <see cref="Hud.Bound"/> says it as the player has it bound, so no rebind leaves a prompt naming the wrong key.
/// </summary>
// Hud.Keys is the HUD's settings, static: the tests that set it don't run beside the one that compares two builds (note 390).
[Collection("Hud.Keys")]
public sealed partial class BoundKeysTests
{
    static readonly string Content = DataFile.FindContentRoot();

    /// <summary>Every control bound away from its default, each to a key of its own.</summary>
    static Settings Rebound() =>
        new() { Keys = Controls.Defaults.Keys.Select((c, i) => (c, i)).ToDictionary(x => x.c.ToString(), x => $"Num{x.i}") };

    static string Said(Control c) => $"[{Controls.KeyLabel(Hud.Keys.KeyFor(c))}]";

    [Fact]
    public void EveryControlsDefaultIsSaidAsTheKeyItsBoundTo()
    {
        var was = Hud.Keys;
        try
        {
            Hud.Keys = Rebound();
            foreach (var (c, key) in Controls.Defaults)
                Assert.Equal(Said(c), Hud.Bound($"[{Controls.KeyLabel(key)}]"));
            // The HUD's short names for the mouse buttons and the vent's left Ctrl.
            Assert.Equal(Said(Control.Fire), Hud.Bound("[LMB]"));
            Assert.Equal(Said(Control.Throw), Hud.Bound("[RMB]"));
            Assert.Equal(Said(Control.Vent), Hud.Bound("[VENT]"));
            // What can't be bound is as written: the menus' keys, the hotbar's numbers, a reconnect.
            foreach (string own in new[] { "[F5]", "[ESC]", "[ENTER]", "[1]", "[UP/DOWN]", "[LEFT/RIGHT]", "[CLICK STICK]" })
                Assert.Equal(own, Hud.Bound(own));
            // One pass: a key bound where another default was isn't said twice.
            Hud.Keys = new Settings { Keys = new() { ["Use"] = "F", ["Ladder"] = "E" } };
            Assert.Equal("USE : [F]   CLIMB : [E]", Hud.Bound("USE : [E]   CLIMB : [F]"));
            // The walking keys: one keycap where each is a letter.
            Hud.Keys = new Settings { Keys = new() { ["Forward"] = "Z", ["Left"] = "Q" } };
            Assert.Equal("BRIDGE AND TROLLEY : [ZQSD]", Hud.Bound("BRIDGE AND TROLLEY : [WASD]"));
            Hud.Keys = new();
            Assert.Equal("[WASD]", Hud.Bound("[WASD]"));
        }
        finally
        {
            Hud.Keys = was;
        }
    }

    [GeneratedRegex("\"[^\"\\n]*\"")]
    private static partial Regex Literal();

    [GeneratedRegex(@"\[([A-Z][A-Z0-9 /]*)\]")]
    private static partial Regex KeyLabel();

    [Fact]
    public void EveryKeyTheHudWritesIsAControlsDefaultOrOneThatCantBeBound()
    {
        // The HUD's own strings, every [KEY] in them: either a control's default, said as the player's own (Bound), or a
        // key nobody can rebind (the menus', the headset's stick). A key that's neither would be said wrong after a rebind,
        // or was never a key at all (the driver's [R/F]: the regulator closes on Y).
        string[] own = ["F5", "ESC", "ENTER", "UP/DOWN", "LEFT/RIGHT", "CLICK STICK", "STICK UP/DOWN", "STICK LEFT/RIGHT"];
        string src = Path.Combine(Path.GetDirectoryName(Content)!, "src", "DarkTerritory.Game");
        var labels = Directory.GetFiles(src, "Hud*.cs")
            .SelectMany(f => Literal().Matches(File.ReadAllText(f)))
            .SelectMany(literal => KeyLabel().Matches(literal.Value))
            .Select(m => m.Groups[1].Value)
            .Where(l => !own.Contains(l))
            .Distinct().Order(StringComparer.Ordinal).ToList();
        Assert.Contains("E", labels);
        var was = Hud.Keys;
        try
        {
            Hud.Keys = Rebound();
            Assert.All(labels, l => Assert.NotEqual($"[{l}]", Hud.Bound($"[{l}]")));
        }
        finally
        {
            Hud.Keys = was;
        }
    }

    [Fact]
    public void TheCornerAndThePromptSayTheReboundKeys()
    {
        // At the controls, and at the firebox: what the HUD draws (the corner's lines and the prompt, through Bound) names
        // the rebound keys and none of the defaults.
        var s = new PrototypeSession(Content, "test-loop", 4);
        s.Player = PlayerMotor.SpawnInCab(s.Train, s.PlayerTuning);
        var was = Hud.Keys;
        try
        {
            Hud.Keys = Rebound();
            var lines = Hud.Hints(s).Lines.Select(Hud.Bound).ToList();
            Assert.NotEmpty(lines);
            Assert.Contains(lines, l => l.Contains(Said(Control.Brake), StringComparison.Ordinal)
                || l.Contains(Said(Control.RegulatorOpen), StringComparison.Ordinal));
            var defaults = Controls.Defaults.Values.Select(k => $"[{Controls.KeyLabel(k)}]").Concat(["[LMB]", "[RMB]", "[VENT]", "[WASD]"]).ToList();
            Assert.All(lines, l => Assert.DoesNotContain(defaults, d => l.Contains(d, StringComparison.Ordinal)));
        }
        finally
        {
            Hud.Keys = was;
        }
    }
}
