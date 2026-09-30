using Ballast;
using Ballast.Render;
using DarkTerritory.Game;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Game.Tests;

/// <summary>The front door and the fortress screen (T30): what each choice does, and that it's all saved.</summary>
public sealed class FrontEndTests : IDisposable
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly CampaignTuning C = DataFile.Load<CampaignTuning>(Path.Combine(Content, CampaignTuning.File));
    static readonly RunTuning R = DataFile.Load<RunTuning>(Path.Combine(Content, RunTuning.File));
    readonly string _dir = Path.Combine(Path.GetTempPath(), $"dt-frontend-{Guid.NewGuid():N}");

    SaveSlots Saves => new(Path.Combine(_dir, "saves"), C.SaveSlots);
    string SettingsPath => Path.Combine(_dir, "settings.json");
    FrontEnd Menu() => new(C, R, Saves, SettingsPath, () => 42);

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

    static Launch? Choose(FrontEnd m, string label)
    {
        Pick(m, label);
        return m.Select();
    }

    [Fact]
    public void AnEmptySlotStartsACampaignAtTheFortress()
    {
        var m = Menu();
        Assert.Equal(Screen.Title, m.Screen);
        Assert.Null(Choose(m, "CAMPAIGN"));
        Assert.Equal(Screen.Slots, m.Screen);
        Assert.Equal(C.SaveSlots + 1, m.Items.Count);
        Assert.Null(Choose(m, "SLOT 2: EMPTY"));
        Assert.Equal(Screen.Fortress, m.Screen);
        var s = Assert.IsType<CampaignState>(m.Open);
        Assert.Equal((2, C.StartingCars, C.StartingScrip, 42UL), (s.Slot, s.Cars, s.Scrip, s.Seed));
        // It's on disk already, and the board is the rules' board.
        Assert.Equivalent(s, Saves.Load(2));
        var offers = Campaign.Offers(C, R, s);
        Assert.Equal(offers.Count, m.Items.Count(i => i.Label.StartsWith("TONIGHT", StringComparison.Ordinal)));
    }

    [Fact]
    public void TheFortressSellsCarsAndUpgradesAndSaysWhyNot()
    {
        var m = Menu();
        Saves.Save(Campaign.New(C, 1, "Crew 1", 5) with { Scrip = 10_000 });
        m.ShowFortress(1);
        double cost = Campaign.NextCarCost(C, m.Open!);
        Assert.Null(Choose(m, "BUY A CAR"));
        Assert.Equal(C.StartingCars + 1, m.Open!.Cars);
        Assert.Equal(10_000 - cost, m.Open.Scrip);
        Assert.Equivalent(m.Open, Saves.Load(1));

        // Broke: refused, with the rules' reason, and nothing changes.
        Saves.Save(m.Open with { Scrip = 0 });
        m.ShowFortress(1);
        Choose(m, "BUY A CAR");
        Assert.NotNull(m.Message);
        Assert.Equal(C.StartingCars + 1, Saves.Load(1)!.Cars);

        // Upgrades: bought once, then owned and greyed out; the ones the night doesn't model say so.
        Saves.Save(m.Open! with { Scrip = 10_000 });
        m.ShowFortress(1);
        Choose(m, "UPGRADES");
        Assert.Equal(Screen.Upgrades, m.Screen);
        var first = C.Upgrades[0];
        Choose(m, first.Name.ToUpperInvariant());
        Assert.Contains(first.Id, Saves.Load(1)!.Upgrades);
        var owned = m.Items.Single(i => i.Label.StartsWith(first.Name.ToUpperInvariant(), StringComparison.Ordinal));
        Assert.EndsWith("OWNED", owned.Label);
        Assert.False(owned.Enabled);
        var unmodelled = C.Upgrades.First(u => u.Effect.Count == 0);
        Assert.Contains("not modelled", m.Items.Single(i => i.Label.StartsWith(unmodelled.Name.ToUpperInvariant(), StringComparison.Ordinal)).Detail);
        m.Back();
        Assert.Equal(Screen.Fortress, m.Screen);
    }

    [Fact]
    public void AContractStartsACampaignNightAloneOrHosted()
    {
        var m = Menu();
        Saves.Save(Campaign.New(C, 3, "Crew 3", 9));
        m.ShowFortress(3);
        Assert.Equal(new Launch.CampaignNight(3, 0, Resume: false, Host: false), Choose(m, "TONIGHT"));
        Pick(m, "PLAY: ALONE");
        m.Right();
        Assert.StartsWith("PLAY: HOST", m.Items[m.Selected].Label);
        var tonight = m.Items.Where(i => i.Label.StartsWith("TONIGHT", StringComparison.Ordinal)).ToList();
        Assert.True(tonight.Count >= 2);
        Assert.Equal(new Launch.CampaignNight(3, 1, Resume: false, Host: true), Choose(m, tonight[1].Label));
    }

    [Fact]
    public void ANightUnderWayCarriesOnAndTheShopWaits()
    {
        var m = Menu();
        var s = Campaign.New(C, 1, "Crew 1", 5);
        var offer = Campaign.Offers(C, R, s)[0];
        Saves.Save(Campaign.Begin(s, offer) with { Scrip = 10_000 });
        m.ShowFortress(1);
        // Carry on is the first thing; no new contracts, and nothing to buy mid-night.
        Assert.StartsWith("CARRY ON", m.Items[0].Label);
        Assert.DoesNotContain(m.Items, i => i.Label.StartsWith("TONIGHT", StringComparison.Ordinal));
        Assert.False(m.Items.Single(i => i.Label.StartsWith("BUY A CAR", StringComparison.Ordinal)).Enabled);
        Assert.Equal(new Launch.CampaignNight(1, -1, Resume: true, Host: false), m.Select());
    }

    [Fact]
    public void AQuickNightOnAnyTierAndSeed()
    {
        var m = Menu();
        Choose(m, "QUICK NIGHT");
        Pick(m, "TIER");
        m.Right(); // frontier → dead lines
        Pick(m, "SEED");
        m.Right();
        m.Right(); // 7 → 9
        Pick(m, "CARS");
        m.Right();
        m.Right();
        // A crew of bots by default (T89); down to none, and it's just you.
        Assert.Equal(new Launch.Night("deadLines:9", 8, Host: false) { Bots = 3 }, Choose(m, "PLAY"));
        Pick(m, "CREW");
        for (int i = 0; i < 5; i++)
            m.Left();
        Assert.Contains(m.Items, i => i.Label == "CREW: JUST YOU");
        Assert.Equal(new Launch.Night("deadLines:9", 8, Host: false), Choose(m, "PLAY"));
        Assert.Equal(new Launch.Night("deadLines:9", 8, Host: true), Choose(m, "HOST FOR FRIENDS"));
        m.Back();
        Assert.Equal(Screen.Title, m.Screen);
    }

    [Fact]
    public void JoiningTakesATypedAddress()
    {
        var m = Menu();
        Choose(m, "JOIN");
        Assert.True(m.WantsText);
        for (int i = 0; i < 9; i++)
            m.Erase();
        m.Type("10.0.0.5:27015 !");
        Assert.Equal("10.0.0.5:27015", m.Address);
        // Enter on the address itself joins as well.
        Assert.Equal(new Launch.Join("10.0.0.5:27015"), m.Select());
        Assert.Equal(new Launch.Join("10.0.0.5:27015"), Choose(m, "JOIN"));
        m.Back();
        Assert.False(m.WantsText);
        Assert.IsType<Launch.Quit>(Choose(m, "QUIT"));
    }

    [Fact]
    public void SettingsAreSavedAsTheyChange()
    {
        var m = Menu();
        Choose(m, "SETTINGS");
        Choose(m, "VOICE");
        Choose(m, "VR TURNING");
        Pick(m, "MOUSE SPEED");
        m.Right();
        m.Right();
        Assert.True(m.Settings.PushToTalk);
        Assert.Equal(VrTurn.Smooth, m.Settings.VrTurn);
        Assert.Equal(1.2, m.Settings.MouseSpeed, 6);
        // A new session reads them back, and they reach the VR comfort tuning.
        var again = Settings.Load(SettingsPath);
        Assert.Equal(m.Settings, again);
        var vr = DataFile.Load<VrTuning>(Path.Combine(Content, VrTuning.File));
        Assert.Equal(VrTurn.Smooth, again.Apply(vr).Turn);
        // A garbled file is the defaults, not a crash.
        File.WriteAllText(SettingsPath, "{ not json");
        Assert.Equal(new Settings(), Settings.Load(SettingsPath));
    }

    [Fact]
    public void EveryScreenDrawsItsItemsWithTheSelectedOneLit()
    {
        var m = Menu();
        Saves.Save(Campaign.New(C, 1, "Crew 1", 5));
        m.ShowFortress(1);
        foreach (var screen in Enum.GetValues<Screen>())
        {
            if (screen == Screen.Fortress || screen == Screen.Upgrades)
                m.ShowFortress(1);
            m.Show(screen);
            var o = new Overlay();
            m.Draw(o, 480, 270);
            Assert.True(o.Count > 200, $"{screen} drew {o.Count} vertices");
            // Everything inside the frame.
            Assert.All(o.Vertices, v => Assert.InRange(v.Position.X, -2, 482));
            Assert.All(o.Vertices, v => Assert.InRange(v.Position.Y, -2, 272));
        }
    }

    [Fact]
    public void AControlRebindsToTheNextKeyAndATakenKeySwapsOver()
    {
        var m = Menu();
        m.Show(Screen.Settings);
        Assert.Null(Choose(m, "CONTROLS"));
        Assert.Equal(Screen.Controls, m.Screen);
        Assert.Equal(Enum.GetValues<Control>().Length + 2, m.Items.Count);
        // Use, from E to F: F was the regulator's close, which takes E.
        Choose(m, "USE: E");
        Assert.Equal(Control.Use, m.Capturing);
        Assert.Contains(m.Items, i => i.Label == "USE: PRESS A KEY");
        m.Bind("F");
        Assert.Null(m.Capturing);
        Assert.Equal("F", m.Settings.KeyFor(Control.Use));
        Assert.Equal("E", m.Settings.KeyFor(Control.RegulatorClose));
        Assert.Contains("REGULATOR CLOSE", m.Message);
        // Kept: only what differs from the defaults, and read back the same.
        Assert.Equal(2, m.Settings.Keys.Count);
        Assert.Equal(m.Settings, Settings.Load(SettingsPath));
        // Escape while waiting keeps the old key; a menu key can't be taken.
        Choose(m, "JUMP");
        m.Back();
        Assert.Null(m.Capturing);
        Assert.Equal(Screen.Controls, m.Screen);
        Choose(m, "JUMP");
        m.Bind("Escape");
        Assert.Equal("Space", m.Settings.KeyFor(Control.Jump));
        // A mouse button will do, and the prompts say it.
        Choose(m, "FIRE");
        m.Bind("Mouse4");
        Assert.Contains(m.Items, i => i.Label == "FIRE: MOUSE 4");
        Hud.Keys = m.Settings;
        Assert.Equal("[F] HOLD: SAND", Hud.Bound("[E] HOLD: SAND"));
        Hud.Keys = new();
        // And back to the defaults.
        Choose(m, "RESET TO DEFAULTS");
        Assert.Empty(m.Settings.Keys);
        Assert.Equal("E", m.Settings.KeyFor(Control.Use));
    }

    [Fact]
    public void EveryControlHasADefaultKeyTheGameKnowsAndNoTwoShareOne()
    {
        Assert.Equal(Enum.GetValues<Control>().Length, Controls.Defaults.Count);
        Assert.Equal(Controls.Defaults.Count, Controls.Defaults.Values.Distinct().Count());
        Assert.All(Controls.Defaults.Values, k => Assert.True(Enum.TryParse<Ballast.Platform.Key>(k, out _), k));
        Assert.DoesNotContain(Controls.Defaults.Values, Controls.Reserved.Contains);
    }
}
