using Ballast;
using Ballast.Render;
using DarkTerritory.Game;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The yard's fun (GDD §9: "the crew wait for friends, hang out, dance, try on outfits"; note 298): the emote wheel, the
/// emote as the crew draw it, and the outfit in the settings and on the yard's menu.
/// </summary>
public sealed class YardFunTests : IDisposable
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly CampaignTuning C = DataFile.Load<CampaignTuning>(Path.Combine(Content, CampaignTuning.File));
    static readonly RunTuning R = DataFile.Load<RunTuning>(Path.Combine(Content, RunTuning.File));
    static readonly PlayerTuning P = DataFile.Load<PlayerTuning>(Path.Combine(Content, PlayerTuning.File));
    readonly string _dir = Path.Combine(Path.GetTempPath(), $"dt-yard-{Guid.NewGuid():N}");

    FrontEnd Menu() => new(C, R, new SaveSlots(Path.Combine(_dir, "saves"), C.SaveSlots), Path.Combine(_dir, "settings.json"), () => 42);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void TheWheelSendsWhatTheMouseLeansTowardOnLettingGo()
    {
        var wheel = new EmoteWheel();
        Assert.Equal(Emote.None, wheel.Update(false, 0, 0));
        // Held, nothing's sent; the mouse leaning left picks the wave.
        Assert.Equal(Emote.None, wheel.Update(true, -30, 2));
        Assert.Equal(Emote.None, wheel.Update(true, -30, 0));
        Assert.True(wheel.Open);
        Assert.Equal(Emote.Wave, wheel.Picked);
        Assert.Equal(Emote.Wave, wheel.Update(false, 0, 0));
        Assert.False(wheel.Open);
        // Up is the dance, right the point.
        wheel.Update(true, 0, -80);
        Assert.Equal(Emote.Dance, wheel.Update(false, 0, 0));
        wheel.Update(true, 90, 0);
        Assert.Equal(Emote.Point, wheel.Update(false, 0, 0));
        // A tap without leaning sends the last one again.
        wheel.Update(true, 3, -2);
        Assert.Equal(Emote.Point, wheel.Update(false, 0, 0));
        // A change of mind needs no long way back: the lean is kept short.
        wheel.Update(true, 5000, 0);
        wheel.Update(true, -250, 0);
        Assert.Equal(Emote.Wave, wheel.Update(false, 0, 0));
    }

    [Fact]
    public void TheWheelIsDrawnRoundTheCrosshairOnlyWhileHeld()
    {
        var wheel = new EmoteWheel();
        var o = new Overlay();
        wheel.Draw(o, 480, 270);
        Assert.Equal(0, o.Count);
        wheel.Update(true, 0, 0);
        wheel.Draw(o, 480, 270);
        Assert.True(o.Count > 0);
        // Close round the middle: inside the middle third of the screen.
        Assert.All(o.Vertices, v => Assert.InRange(v.Position.X, 160, 320));
        Assert.All(o.Vertices, v => Assert.InRange(v.Position.Y, 90, 180));
    }

    [Fact]
    public void ACrewmateIsDrawnAtTheirEmoteWhileItLastsAndTheyStandStill()
    {
        var line = RailLine.Load(Path.Combine(Content, "lines/test-loop.json"));
        var t = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(t, 4, 1)), line, 600);
        var world = new World(train) { EmoteTuning = P.Emotes };
        var s = PlayerMotor.SpawnOnRoof(train, 2, 0, P);
        Assert.Equal(Emote.None, CrewActs.EmoteOf(3, s, world).Kind);
        world.Emotes.Add(new EmoteEvent(1, world.Tick, 3, Emote.Wave));
        for (int i = 0; i < SimConstants.TickRate; i++)
            world.BeginTick();
        Assert.Equal(Emote.Wave, CrewActs.EmoteOf(3, s, world).Kind);
        // Someone else's isn't theirs; walking off ends it.
        Assert.Equal(Emote.None, CrewActs.EmoteOf(4, s, world).Kind);
        Assert.Equal(Emote.None, CrewActs.EmoteOf(3, s with { Velocity = new Double3(1, 0, 0) }, world).Kind);
        var drawn = CrewActs.Crewmate(3, s, world, train.Frames);
        Assert.Equal(Emote.Wave, drawn.Emote);
    }

    [Fact]
    public void ACrewmatesOutfitIsTheLookTheyreDrawnIn()
    {
        var line = RailLine.Load(Path.Combine(Content, "lines/test-loop.json"));
        var t = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(t, 4, 1)), line, 600);
        var world = new World(train);
        var s = PlayerMotor.SpawnOnRoof(train, 2, 0, P);
        Assert.Equal(3, CrewActs.Crewmate(3, s, world, train.Frames).Variant);
        world.Outfits[3] = 6;
        Assert.Equal(6, CrewActs.Crewmate(3, s, world, train.Frames).Variant);
    }

    [Fact]
    public void TheOutfitGoesRoundTheCrewsLooksAndIsSaved()
    {
        var m = Menu();
        m.Show(Screen.Settings);
        int i = m.Items.ToList().FindIndex(x => x.Label.StartsWith("OUTFIT", StringComparison.Ordinal));
        while (m.Selected != i)
            m.Down();
        Assert.Equal("OUTFIT: THE CREW'S PICK", m.Items[i].Label);
        m.Right();
        Assert.Equal("OUTFIT: RED", m.Items[i].Label);
        Assert.Equal(0, m.Settings.OutfitByte(m.OutfitNames.Count));
        m.Left();
        m.Left();
        Assert.Equal("OUTFIT: WHITE", m.Items[i].Label);
        Assert.Equal(7, Settings.Load(Path.Combine(_dir, "settings.json")).Outfit);
        // The crew's pick goes out as none: their id's look.
        Assert.Equal(Sim.Net.Messages.NoOutfit, new Settings().OutfitByte(8));
        Assert.Equal(Sim.Net.Messages.NoOutfit, new Settings { Outfit = 12 }.OutfitByte(8));
    }

    [Fact]
    public void OutfitsAreTriedOnFromTheMenuInTheYardOnly()
    {
        var m = Menu();
        m.OpenNight(new NightMenu(Yard: true));
        Assert.Contains(m.Items, x => x.Label.StartsWith("OUTFIT", StringComparison.Ordinal));
        m.CloseNight();
        m.OpenNight(new NightMenu());
        Assert.DoesNotContain(m.Items, x => x.Label.StartsWith("OUTFIT", StringComparison.Ordinal));
    }

    [Fact]
    public void TheEmoteKeyIsBindableAndOnTheControlsPage()
    {
        Assert.Equal("J", Controls.Defaults[Control.Emote]);
        Assert.Contains("EMOTE", Controls.Label(Control.Emote));
    }
}
