using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Game;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// TEXT BACKING (note 404; GDD §32 "Accessibility"): a dim band behind the HUD's print in play, a line at a time, as a
/// subtitle's background, so a caption or a prompt reads over a fire's glare or the fog. Off by default (note 285: no frames
/// in play); the plates, cards and slots that have their own ground get none.
/// </summary>
// Hud.Keys is the HUD's settings, static: these set it (note 390).
[Collection("Hud.Keys")]
public sealed class TextBackingTests : IDisposable
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly HudTuning Tuning = DataFile.Load<HudTuning>(Path.Combine(Content, HudTuning.File));
    // The tests' band, a darkness nothing else on the HUD is drawn in (a keycap's shadow is black at 0.6, as hud.json's band).
    static readonly HudTuning Marked = Tuning with { TextBacking = 0.537 };
    static readonly Vector4 Band = new(0, 0, 0, (float)Marked.TextBacking);
    readonly Settings _keys = Hud.Keys;
    readonly HudTuning _tuning = Hud.Tuning;

    public void Dispose()
    {
        Hud.Keys = _keys;
        Hud.Tuning = _tuning;
    }

    static List<OverlayVertex> Bands(Overlay o) => [.. o.Vertices.Where(v => v.Colour == Band)];

    [Fact]
    public void TheBandIsDarkEnoughToReadByAndTheNightStillShows()
    {
        Assert.InRange(Tuning.TextBacking, 0.4, 0.8);
    }

    [Fact]
    public void ALineOfTextHasOneBandBehindItAndPlainTextNone()
    {
        var o = new Overlay();
        o.Text(10, 10, "[TIPTOEING, ABOVE]", Vector4.One, 0.5f);
        Assert.Empty(Bands(o));
        o.Clear();
        o.Backing = Band;
        float w = o.Text(10, 10, "[TIPTOEING, ABOVE]", Vector4.One, 0.5f);
        var band = Bands(o);
        Assert.Equal(6, band.Count);
        // Drawn first, so the print is over it, and round the print and its shadow.
        Assert.Equal(Band, o.Vertices[0].Colour);
        Assert.True(band.Min(v => v.Position.X) < 10 && band.Max(v => v.Position.X) > 10 + w + 0.5f);
        Assert.True(band.Min(v => v.Position.Y) < 10 && band.Max(v => v.Position.Y) > 10 + o.Font.Height * 0.5f + 0.5f);
        // Text drawn without a shadow is on something of its own (a keycap, a plate): no band.
        o.Clear();
        o.Text(10, 10, "R", Vector4.One, 0.5f, shadow: false);
        Assert.Empty(Bands(o));
    }

    [Fact]
    public void AKeyedLineIsOneBandKeycapsAndAll()
    {
        var o = new Overlay { Backing = Band };
        UiStyle.Keyed(o, 10, 10, "VENT : HOLD [LEFT CTRL]   LAMP OFF : [L]", Vector4.One, 0.5f);
        Assert.Equal(6, Bands(o).Count);
        Assert.Equal(Band, o.Backing);
    }

    [Fact]
    public void APlateSetsItAsideAndPutsItBack()
    {
        var o = new Overlay { Backing = Band };
        using (UiStyle.OnPlate(o))
        {
            Assert.Equal(default, o.Backing);
            o.Text(10, 10, "ON A PLATE", Vector4.One, 1);
        }
        Assert.Empty(Bands(o));
        Assert.Equal(Band, o.Backing);
    }

    public static TheoryData<double> Sizes() => [.. Settings.TextSizes];

    [Theory]
    [MemberData(nameof(Sizes))]
    public void OnTheCaptionsAndTheCornerAreBandedAndTheMenusAfterArent(double size)
    {
        Hud.Tuning = Marked;
        var (w, h) = new Settings { TextSize = size }.Canvas;
        var s = new PrototypeSession(Content, Sim.LineGen.Routes.Generate(Content, "frontier:7", 4), 4, enemies: false);
        string[] lines = ["[A SQUEAL AT THE WHEELS, BEHIND]", "[TIPTOEING, ABOVE]"];
        Hud.Keys = new Settings { TextSize = size };
        var off = new Overlay();
        Hud.Build(off, w, h, s, pixels: 1080f / h, captions: lines);
        Assert.Empty(Bands(off));
        Hud.Keys = new Settings { TextSize = size, TextBacking = true };
        var on = new Overlay();
        Hud.Build(on, w, h, s, pixels: 1080f / h, captions: lines);
        var bands = Bands(on);
        // Only bands added: the print is as it was, over them.
        Assert.Equal(off.Count, on.Count - bands.Count);
        // A band a caption at least, at the foot of the frame on the left, inside it.
        int captions = bands.Chunk(6).Count(q => q.Max(v => v.Position.X) < w * 0.7f && q.Min(v => v.Position.Y) > h * 0.6f);
        Assert.True(captions >= lines.Length, $"{captions} bands under the captions");
        Assert.All(bands, v => Assert.InRange(v.Position.X, -2, w + 2));
        Assert.All(bands, v => Assert.InRange(v.Position.Y, -2, h + 2));
        // The build's own: what's drawn over it after (the in-night menu, on its plate) isn't banded.
        Assert.Equal(default, on.Backing);
    }

    [Fact]
    public void TheHotbarsSlotsAndTheRosterHaveTheirOwnGround()
    {
        Hud.Tuning = Marked;
        var s = new PrototypeSession(Content, Sim.LineGen.Routes.Generate(Content, "frontier:7", 4), 4, enemies: false);
        Hud.Keys = new Settings { TextBacking = true };
        var o = new Overlay();
        Hud.Build(o, 480, 270, s, pixels: 4);
        // No band reaches into the hotbar's slots, at the foot of the frame's middle.
        Assert.DoesNotContain(Bands(o), v => v.Position.Y > 270 - 6 - Hud.SlotSize && Math.Abs(v.Position.X - 240) < 20);
        var roster = new Overlay { Backing = Band };
        Hud.Roster(roster, 480, 270, [new RosterLine(1, "DAVE", "CAB", true)], null);
        Assert.Empty(Bands(roster));
    }

    [Fact]
    public void TextBackingIsASettingThatsOffUntilTurnedOn()
    {
        Assert.False(new Settings().TextBacking);
        Assert.False(new Settings().Equals(new Settings { TextBacking = true }));
        string dir = Path.Combine(Path.GetTempPath(), $"dt-backing-{Guid.NewGuid():N}");
        try
        {
            var m = FirstNightsTests.Menu(dir);
            m.Show(Screen.Settings);
            FirstNightsTests.Toggle(m, "TEXT BACKING", "OFF", "ON");
            Assert.True(Settings.Load(Path.Combine(dir, "settings.json")).TextBacking);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }
}
