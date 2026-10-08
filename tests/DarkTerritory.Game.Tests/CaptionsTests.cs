using Ballast;
using Ballast.Audio;
using Ballast.Render;
using DarkTerritory.Game;
using DarkTerritory.Game.Sound;
using DarkTerritory.Sim.Enemies;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// CAPTIONS (note 349; GDD §32 "Accessibility"; the director, 8 Oct: "these are all quite important"): the sounds worth hearing
/// written over the hotbar as they're heard, what each is and where, at the gain the mixer heard it at. What a sound is, never
/// which creature makes it: a reader learns what tiptoeing means as a listener does.
/// </summary>
// Hud.Keys is the HUD's settings, static: the tests that set it don't run beside the one that compares two builds (note 390).
[Collection("Hud.Keys")]
public class CaptionsTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly Captions.Data File = Captions.Load(Content);

    static readonly Listener North = Listener.At(Double3.Zero, 0);

    [Theory]
    [InlineData("sign.ribbits")]
    [InlineData("sign.sootChildren")]
    [InlineData("sign.whistler")]
    [InlineData("sign.grumbler")]
    [InlineData("tell-moose-grazing.chew")]
    [InlineData("tell-moose-warning.grunt")]
    [InlineData("tell-moose-square-up.stamp")]
    [InlineData("tell-moose-charge.hooves")]
    [InlineData("cs-moose-ram.boom")]
    [InlineData("tell-gannet-calls.call")]
    [InlineData("tell-gannet-fold.whistle")]
    [InlineData("tell-gannet-bank.scream")]
    [InlineData("lamp-gutter")]
    [InlineData("state-coupling-loose.knock")]
    [InlineData("crew-house-door.shut")]
    [InlineData("crew-house-door.open")]
    [InlineData("cs-choir.bang-door.wood")]
    public void TheTellsAndCallsSinceCaptionsAreCaptioned(string sound)
    {
        // Note 391: the signs as they play since note 342, the Moose's and the Gannet's tells (notes 334, 384), and the jobs
        // that call for a hand (notes 346, 356, 385) are heard by name, so a reader is told them too; and the house doors and
        // the Choir beating on a door (note 409).
        Assert.NotNull(new Captions(File).CaptionOf(sound));
    }

    [Fact]
    public void EveryCaptionIsASoundThatPlays()
    {
        Assert.NotEmpty(File.Sounds);
        var names = Directory.GetFiles(Path.Combine(Content, "audio", "sounds"), "*.json").Select(Path.GetFileNameWithoutExtension).ToList();
        foreach (var name in File.Sounds.Keys)
            Assert.True(names.Any(n => n == name || n!.StartsWith(name + ".", StringComparison.Ordinal)), $"{name}: no such sound");
    }

    [Fact]
    public void NoCaptionNamesACreature()
    {
        // Each creature's name and its words ("TIPPY", "HOUND"), but not the plain words a sound is said in (a car's FIRE).
        string[] plain = ["CAR", "FIRE", "TRACK"];
        string[] hazards = [nameof(EnemyKind.CarFire), nameof(EnemyKind.Sleepers), nameof(EnemyKind.Drift)];
        var banned = Enum.GetNames<EnemyKind>().Where(k => !hazards.Contains(k))
            .SelectMany(k => System.Text.RegularExpressions.Regex.Split(k, "(?<!^)(?=[A-Z])").Append(k))
            .Select(w => w.ToUpperInvariant()).Where(w => !plain.Contains(w))
            .Concat(["TOESIE", "HOUNDS", "FIREFLY", "FIREFLIES", "RIBBITS", "FOLLOWERS", "CLIMBERS", "DRAGGERS"]).Distinct().ToList();
        foreach (var (sound, caption) in File.Sounds)
        {
            var words = caption.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
            Assert.True(caption == caption.ToUpperInvariant(), $"{sound}: \"{caption}\" in capitals, as the HUD's print is");
            foreach (var w in banned)
                Assert.False(words.Contains(w), $"{sound}: \"{caption}\" names {w}");
        }
    }

    [Fact]
    public void AVariantIsCaptionedAsTheSoundItsAVariantOf()
    {
        var c = new Captions(File);
        Assert.Equal("TIPTOEING", c.CaptionOf("tippy-tiptoe"));
        Assert.Equal("TIPTOEING", c.CaptionOf("tippy-tiptoe.wood"));
        Assert.Equal("SOMETHING HOPPING", c.CaptionOf("cs-ribbits.hop-land.ground"));
        // Its own caption first: the pack close behind isn't the far howl.
        Assert.Equal("HOWLING, CLOSE", c.CaptionOf("tell-hounds.howl-near"));
        Assert.Null(c.CaptionOf("tell-hounds"));
        Assert.Null(c.CaptionOf("bed-chuff.chuff"));
        Assert.Null(c.CaptionOf(""));
    }

    [Fact]
    public void WhereIsFromTheListenersHead()
    {
        Assert.Equal("AHEAD", Captions.Where(North, new Double3(0, 0, -10)));
        Assert.Equal("BEHIND", Captions.Where(North, new Double3(0, 0, 10)));
        Assert.Equal("RIGHT", Captions.Where(North, new Double3(10, 0, 0)));
        Assert.Equal("LEFT", Captions.Where(North, new Double3(-10, 0, 0)));
        Assert.Equal("ABOVE", Captions.Where(North, new Double3(0, 10, -1)));
        Assert.Equal("BELOW", Captions.Where(North, new Double3(1, -10, 0)));
        Assert.Equal("HERE", Captions.Where(North, Double3.Zero));
        // Turned to face east, what was to the right is ahead.
        Assert.Equal("AHEAD", Captions.Where(Listener.At(Double3.Zero, -Math.PI / 2), new Double3(10, 0, 0)));
    }

    [Fact]
    public void ACaptionIsUpWhileItsHeardAndForAMomentAfter()
    {
        var c = new Captions(File with { Threshold = 0.05, HoldSeconds = 2, Lines = 2 });
        var behind = new Double3(0, 0, 5);
        // Under the threshold it isn't heard; a sound with no caption is never up.
        c.Update([new("tippy-tiptoe", behind, 0.01f), new("bed-chuff.chuff", behind, 1)], North, 0);
        Assert.Empty(c.Lines());
        c.Update([new("tippy-tiptoe.roof", behind, 0.2f)], North, 1);
        Assert.Equal(["[TIPTOEING, BEHIND]"], c.Lines());
        // Two of a kind are one caption, from the louder.
        c.Update([new("tippy-tiptoe", new Double3(-5, 0, 0), 0.1f), new("tippy-tiptoe", new Double3(5, 0, 0), 0.3f)], North, 2);
        Assert.Equal(["[TIPTOEING, RIGHT]"], c.Lines());
        // Newest last, and only as many as there are lines: the oldest goes.
        c.Update([new("child-call", behind, 0.5f)], North, 2.5);
        c.Update([new("train-whistle", new Double3(0, 0, -50), 0.5f)], North, 3);
        Assert.Equal(["[A CHILD CALLING, BEHIND]", "[THE WHISTLE, AHEAD]"], c.Lines());
        // Gone once it's not been heard for the hold.
        c.Update([], North, 4.6);
        Assert.Equal(["[THE WHISTLE, AHEAD]"], c.Lines());
        c.Update([], North, 5.1);
        Assert.Empty(c.Lines());
        c.Update([new("child-call", behind, 0.5f)], North, 6);
        c.Clear();
        Assert.Empty(c.Lines());
    }

    [Fact]
    public void TheMixersOwnVoicesAreCaptionedAtTheGainTheEarHeardThem()
    {
        // End to end through the real mixer: a tiptoe just behind the listener is heard and captioned; one far off isn't.
        var audio = new GameAudio(Content);
        audio.Mixer.Listener = North;
        var near = audio.Mixer.Play("tippy-tiptoe", new Double3(0, 0, 4));
        var far = audio.Mixer.Play("child-call", new Double3(0, 0, -2000));
        Assert.NotNull(near);
        Assert.NotNull(far);
        var block = new float[Audio.Block * 2];
        for (int i = 0; i < 4; i++)
            audio.Mixer.Render(block);
        var c = new Captions(File);
        c.Update(audio.Mixer.Voices.Where(v => !v.Finished && !v.Virtual).Select(v => new Heard(v.Name, v.Position, v.AudibleGain)), North, 0);
        Assert.True(near!.AudibleGain >= File.Threshold, $"near {near.AudibleGain}");
        Assert.Equal(["[TIPTOEING, BEHIND]"], c.Lines());
    }

    public static TheoryData<double> Sizes() => [.. Settings.TextSizes];

    [Theory]
    [MemberData(nameof(Sizes))]
    public void TheHudWritesThemAtTheFootOfTheFrame(double size)
    {
        var (w, h) = new Settings { TextSize = size }.Canvas;
        var s = new PrototypeSession(Content, Sim.LineGen.Routes.Generate(Content, "frontier:7", 4), 4, enemies: false);
        var none = new Overlay();
        Hud.Build(none, w, h, s, pixels: 1080f / h);
        var o = new Overlay();
        string[] lines = ["[A SQUEAL AT THE WHEELS, BEHIND]", "[SOMETHING WET ON THE RAIL, AHEAD]", "[THE BOILER SHRIEKING, AHEAD]"];
        Hud.Build(o, w, h, s, pixels: 1080f / h, captions: lines);
        // What the captions added: the vertices drawn with them less those drawn without (the HUD draws them mid-frame).
        var left = none.Vertices.GroupBy(v => (v.Position, v.Colour)).ToDictionary(g => g.Key, g => g.Count());
        var added = o.Vertices.Where(v => !(left.TryGetValue((v.Position, v.Colour), out int n) && n > 0 && (left[(v.Position, v.Colour)] = n - 1) >= 0)).ToList();
        Assert.Equal(o.Count - none.Count, added.Count);
        Assert.NotEmpty(added);
        // In the frame, at its foot and on its left: the hotbar's middle and the corner's keys are elsewhere.
        Assert.All(added, v => Assert.InRange(v.Position.X, 0, w * 0.7f));
        Assert.All(added, v => Assert.InRange(v.Position.Y, h * 0.6f, h));
    }

    [Fact]
    public void CaptionsIsASettingThatsOffUntilTurnedOn()
    {
        Assert.False(new Settings().Captions);
        string dir = Path.Combine(Path.GetTempPath(), $"dt-captions-{Guid.NewGuid():N}");
        try
        {
            var m = FirstNightsTests.Menu(dir);
            m.Show(Screen.Settings);
            FirstNightsTests.Toggle(m, "CAPTIONS", "OFF", "ON");
            Assert.True(Settings.Load(Path.Combine(dir, "settings.json")).Captions);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }
}
