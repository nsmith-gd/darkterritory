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
    public void TheCreditsListEveryTrackWithItsPerformersLicenceAndSource()
    {
        // GDD v1.4 App. E.6: "the credits screen lists every performer anyway" (note 194): a row a track from the manifest.
        var music = DarkTerritory.Sim.Music.MusicManifest.Load(Content).Tracks;
        var m = Menu();
        m.Music = music;
        Choose(m, "CREDITS");
        Assert.Equal(Screen.Credits, m.Screen);
        Assert.Equal(music.Length + 1, m.Items.Count);
        foreach (var (t, item) in music.Zip(m.Items))
        {
            Assert.Contains(t.Work, item.Label);
            Assert.Contains(t.Composer, item.Label);
            Assert.Contains(t.Performers, item.Detail);
            Assert.Contains(t.Licence, item.Detail);
            Assert.Contains(t.Recorded ? t.Source.Replace("https://", "") : Path.GetFileName(t.Source), item.Detail);
        }
        // It draws (two lines a track, scrolled), and Esc goes back to the title.
        var o = new Overlay();
        m.Draw(o, 480, 270);
        m.Back();
        Assert.Equal(Screen.Title, m.Screen);
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

        // Upgrades: bought once, then owned and greyed out.
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
        // Every upgrade the fortress sells does something tonight (note 196): none says it isn't modelled.
        Assert.All(C.Upgrades, u => Assert.DoesNotContain("not modelled",
            m.Items.Single(i => i.Label.StartsWith(u.Name.ToUpperInvariant(), StringComparison.Ordinal)).Detail ?? ""));
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
        // Just you by default (T110 playtest); up for a crew of bots (T89), and down again to none.
        Assert.Contains(m.Items, i => i.Label == "CREW: JUST YOU");
        Assert.Equal(new Launch.Night("deadLines:9", 8, Host: false), Choose(m, "PLAY"));
        Pick(m, "CREW");
        for (int i = 0; i < 3; i++)
            m.Right();
        Assert.Equal(new Launch.Night("deadLines:9", 8, Host: false) { Bots = 3 }, Choose(m, "PLAY"));
        Pick(m, "CREW");
        for (int i = 0; i < 5; i++)
            m.Left();
        Assert.Equal(new Launch.Night("deadLines:9", 8, Host: false), Choose(m, "PLAY"));
        m.Back();
        Assert.Equal(Screen.Title, m.Screen);
    }

    [Fact]
    public void TheTitleSaysHostAndJoin()
    {
        // The user's playtest: "You should be able to host a run, not a night. The button should just say HOST."
        var m = Menu();
        var labels = m.Items.Select(i => i.Label).ToList();
        Assert.Contains("HOST", labels);
        Assert.Contains("JOIN", labels);
        Assert.DoesNotContain(labels, l => l.Contains("A NIGHT", StringComparison.Ordinal));
    }

    [Fact]
    public void HostingOpensALobbyOnTheRunChosen()
    {
        // T116: the co-op games' way: the host opens a lobby (the yard) on the run they pick, public and named for them.
        var m = Menu();
        m.DefaultPlayerName = "Nick";
        Choose(m, "HOST");
        Assert.Equal(Screen.Host, m.Screen);
        Pick(m, "SEED");
        m.Right();
        Assert.Equal(new Launch.Night("frontier:8", 6, Host: true) { Public = true, LobbyName = "NICK'S RUN" }, Choose(m, "OPEN THE LOBBY"));
    }

    [Fact]
    public void TheHostChoosesPublicOrPrivateAndItsRemembered()
    {
        // The user's playtest: "I can join it if its public. If it's a private lobby its not listed."
        var m = Menu();
        Choose(m, "HOST");
        Assert.Contains("VISIBILITY: PUBLIC", m.Items.Select(i => i.Label));
        Pick(m, "VISIBILITY");
        m.Right();
        Assert.Equal("VISIBILITY: PRIVATE", m.Items[m.Selected].Label);
        Assert.False(Assert.IsType<Launch.Night>(Choose(m, "OPEN THE LOBBY")).Public);
        // Saved: the next start of the game has it so.
        var again = Menu();
        Choose(again, "HOST");
        Assert.Contains("VISIBILITY: PRIVATE", again.Items.Select(i => i.Label));
        Pick(again, "VISIBILITY");
        again.Left();
        Assert.Contains("VISIBILITY: PUBLIC", again.Items.Select(i => i.Label));
    }

    [Fact]
    public void TheHostNamesTheLobby()
    {
        var m = Menu();
        m.DefaultPlayerName = "nick";
        Choose(m, "HOST");
        Assert.False(m.WantsText);
        // Note 267 ("when I go to name of lobby, it just starts automatically typing"): chosen, it doesn't take typing, and
        // WASD still moves through the screen.
        Pick(m, "NAME");
        Assert.False(m.WantsText);
        Assert.Equal("NAME: NICK'S RUN", m.Items[m.Selected].Label);
        m.Type("wasd");
        Assert.Equal("NICK'S RUN", m.LobbyName);
        Assert.Equal(MenuKey.Down, MenuInput.Keys(k => k == "S", m.WantsText));
        // Enter starts typing into it; WASD and Space are letters then.
        Assert.Null(m.Select());
        Assert.True(m.WantsText);
        Assert.Equal(TextField.LobbyName, m.Editing);
        Assert.Equal("NAME: NICK'S RUN_", m.Items[m.Selected].Label);
        Assert.Equal(MenuKey.None, MenuInput.Keys(k => k is "W" or "S" or "Space", m.WantsText));
        for (int i = 0; i < 3; i++)
            m.Erase();
        m.Type("ride, 2am");
        Assert.Equal("NICK'S RIDE 2AM", m.LobbyName);
        // Enter ends it (and chooses nothing); the name's kept.
        Assert.Null(m.Select());
        Assert.False(m.WantsText);
        Assert.Equal(Screen.Host, m.Screen);
        Assert.Equal("NICK'S RIDE 2AM", Assert.IsType<Launch.Night>(Choose(m, "OPEN THE LOBBY")).LobbyName);
        // Erased away, it's blank to type into; Esc ends it, and goes no further back; left blank, the name's the default.
        Pick(m, "NAME");
        m.Select();
        for (int i = 0; i < 20; i++)
            m.Erase();
        Assert.Equal("", m.LobbyName);
        m.Back();
        Assert.False(m.WantsText);
        Assert.Equal(Screen.Host, m.Screen);
        Assert.Equal("NICK'S RUN", m.LobbyName);
        Assert.Equal("NICK'S RUN", Assert.IsType<Launch.Night>(Choose(m, "OPEN THE LOBBY")).LobbyName);
    }

    [Fact]
    public void ThePlayerNameIsAFieldThatTakesTypingOnlyOnceEntered()
    {
        // Note 267: every text field alike (the lobby's name, the join address, the player's name).
        var m = Menu();
        m.DefaultPlayerName = "nick";
        Choose(m, "SETTINGS");
        Pick(m, "PLAYER NAME");
        Assert.Equal("PLAYER NAME: NICK", m.Items[m.Selected].Label);
        Assert.False(m.WantsText);
        // WASD moves, as on every other screen.
        Assert.Null(MenuInput.Apply(m, MenuInput.Keys(k => k == "S", m.WantsText)));
        Assert.StartsWith("SOUND", m.Items[m.Selected].Label);
        Assert.Null(MenuInput.Apply(m, MenuInput.Keys(k => k == "W", m.WantsText)));
        Assert.Null(MenuInput.Apply(m, MenuKey.Select));
        Assert.True(m.WantsText);
        MenuInput.Apply(m, MenuInput.Keys(k => k == "S", m.WantsText), "Dave");
        Assert.Equal("PLAYER NAME: Dave_", m.Items[m.Selected].Label);
        MenuInput.Apply(m, MenuKey.Back);
        Assert.False(m.WantsText);
        Assert.Equal(Screen.Settings, m.Screen);
        // Saved, and the lobby's default name follows it.
        Assert.Equal("Dave", Settings.Load(SettingsPath).PlayerName);
        Assert.Equal("DAVE'S RUN", m.LobbyName);
    }

    /// <summary>Every point <paramref name="label"/>'s row (or its arrow, <paramref name="step"/>) was last drawn on, for the mouse.</summary>
    static List<System.Numerics.Vector2> Drawn(FrontEnd m, string label, int step = 0)
    {
        // The row called that, or else the first that starts so (the controls have a BACK: S as well as BACK).
        int item = m.Items.ToList().FindIndex(x => x.Label == label);
        if (item < 0)
            item = m.Items.ToList().FindIndex(x => x.Label.StartsWith(label, StringComparison.Ordinal));
        Assert.True(item >= 0, $"no '{label}' on {m.Screen}");
        var hits = new List<System.Numerics.Vector2>();
        for (int y = 0; y < 270; y++)
            for (int x = 0; x < 480; x++)
                if (m.HitTest(x + 0.5f, y + 0.5f) == new MenuHit(item, step))
                    hits.Add(new(x + 0.5f, y + 0.5f));
        return hits;
    }

    /// <summary>The middle of what <paramref name="label"/>'s row (or its arrow) was last drawn on.</summary>
    static System.Numerics.Vector2 Where(FrontEnd m, string label, int step = 0)
    {
        var hits = Drawn(m, label, step);
        Assert.True(hits.Count > 0, $"'{label}' ({step}) isn't drawn where the mouse can reach it");
        return hits[hits.Count / 2];
    }

    static Launch? Click(FrontEnd m, string label, int step = 0, bool right = false)
    {
        m.Draw(new Overlay(), 480, 270);
        return MenuInput.Apply(m, MenuKey.None, "", new MenuMouse(Where(m, label, step), Moved: true, Left: !right, Right: right));
    }

    [Fact]
    public void TheMouseHoversClicksStepsAndScrolls()
    {
        // Note 267 (the director's notes on build 1121: "we really should be able to click with a mouse").
        var m = Menu();
        var o = new Overlay();
        m.Draw(o, 480, 270);
        // Hovering a row selects it; a click on it chooses it, as Enter does.
        MenuInput.Apply(m, MenuKey.None, "", new MenuMouse(Where(m, "JOIN"), Moved: true));
        Assert.StartsWith("JOIN", m.Items[m.Selected].Label);
        Assert.Null(Click(m, "QUICK NIGHT"));
        Assert.Equal(Screen.QuickNight, m.Screen);
        // A value's arrows step it, as left and right do; a click on the row steps it on, the right button back.
        Click(m, "CARS", +1);
        Assert.Equal("CARS: 7", m.Items[m.Selected].Label);
        Click(m, "CARS", -1);
        Click(m, "CARS", -1);
        Assert.Equal("CARS: 5", m.Items[m.Selected].Label);
        Click(m, "CARS");
        Assert.Equal("CARS: 6", m.Items[m.Selected].Label);
        Click(m, "CARS", right: true);
        Assert.Equal("CARS: 5", m.Items[m.Selected].Label);
        Assert.Equal(new Launch.Night("frontier:7", 5, Host: false), Click(m, "PLAY"));
        // A click on a toggle toggles it.
        m.Show(Screen.Settings);
        Click(m, "SOUND");
        Assert.Equal("SOUND: OFF", m.Items[m.Selected].Label);
        Assert.True(Settings.Load(SettingsPath).Mute);
        // BACK, clicked, backs out.
        m.Show(Screen.QuickNight);
        Click(m, "BACK");
        Assert.Equal(Screen.Title, m.Screen);

        // The wheel scrolls a list longer than the screen (the controls), and a click on a row it brought up works it.
        m.Show(Screen.Controls);
        m.Draw(o, 480, 270);
        Assert.NotEmpty(Drawn(m, Controls.Label(Control.Forward)));
        Assert.Empty(Drawn(m, "RESET TO DEFAULTS"));
        for (int i = 0; i < 40; i++)
            MenuInput.Apply(m, MenuKey.None, "", new MenuMouse(new(5, 5), Wheel: -1));
        m.Draw(o, 480, 270);
        Assert.Empty(Drawn(m, Controls.Label(Control.Forward)));
        Assert.NotEmpty(Drawn(m, "RESET TO DEFAULTS"));
        // Back up, a notch at a time.
        for (int i = 0; i < 40; i++)
            MenuInput.Apply(m, MenuKey.None, "", new MenuMouse(new(5, 5), Wheel: 1));
        m.Draw(o, 480, 270);
        Assert.NotEmpty(Drawn(m, Controls.Label(Control.Forward)));
        for (int i = 0; i < 40; i++)
            MenuInput.Apply(m, MenuKey.None, "", new MenuMouse(new(5, 5), Wheel: -1));
        Click(m, "BACK");
        Assert.Equal(Screen.Settings, m.Screen);
    }

    [Fact]
    public void AClickStartsTypingInAFieldAndAClickElsewhereEndsIt()
    {
        var m = Menu();
        m.DefaultPlayerName = "nick";
        Choose(m, "HOST");
        Assert.Null(Click(m, "NAME"));
        Assert.Equal(TextField.LobbyName, m.Editing);
        // Clicking the field again carries on; typing goes in.
        Click(m, "NAME");
        MenuInput.Apply(m, MenuKey.None, "!");
        Assert.Equal("NICK'S RUN!", m.LobbyName);
        // Clicking off any row ends it, and does nothing else.
        m.Draw(new Overlay(), 480, 270);
        MenuInput.Apply(m, MenuKey.None, "", new MenuMouse(new(470, 260), Left: true));
        Assert.Null(m.Editing);
        Assert.Equal(Screen.Host, m.Screen);
        // Clicking another row while typing ends the typing and works that row.
        Click(m, "NAME");
        Assert.NotNull(m.Editing);
        Click(m, "VISIBILITY");
        Assert.Null(m.Editing);
        Assert.Contains("VISIBILITY: PRIVATE", m.Items.Select(i => i.Label));
    }

    [Fact]
    public void TheJoinScreenListsThePublicGamesNearestFirst()
    {
        // The user's playtest: "Join should work like Lethal Company, I see active lobbies I can join and then what my ping
        // is in relation to that." The network's games and Steam's, in one list, by ping; what can't be joined says why.
        var m = Menu();
        static Ballast.Net.LanGame Lan(string ip, string host, int protocol, int aboard, double? ping) =>
            new(new System.Net.IPEndPoint(System.Net.IPAddress.Parse(ip), 27450), host, "FRONTIER:7, 6 CARS, IN THE YARD", aboard, protocol, "darkterritory")
            { Name = $"{host.ToUpperInvariant()}'S RUN", Max = 12, Tier = "Frontier", PingMs = ping };
        var steam = new Ballast.Online.LobbyListing(new Ballast.Online.LobbyId(77), 2, 12, new Dictionary<string, string>
        {
            [Ballast.Online.Lobby.HostKey] = "priya",
            [Ballast.Online.Lobby.ProtocolKey] = m.Protocol.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [NetPlaySession.NameKey] = "PRIYA'S RUN",
            [NetPlaySession.TierKey] = "DeadLines",
            [NetPlaySession.AboardKey] = "3",
        }, 41);
        m.Games = ListedGame.Merge([Lan("192.168.1.20", "nick", m.Protocol, 1, 3.4), Lan("192.168.1.30", "old", m.Protocol + 1, 1, 2),
            Lan("192.168.1.40", "packed", m.Protocol, 12, 5)], [steam], "Steam");
        Choose(m, "JOIN");
        Assert.Equal(Screen.Join, m.Screen);
        var rows = m.Items.Select(i => i.Label).ToList();
        Assert.StartsWith("LOBBY", rows[0]);
        Assert.Equal(["OLD'S RUN", "NICK'S RUN", "PACKED'S RUN", "PRIYA'S RUN"], rows.Skip(1).Take(4).Select(r => r[..25].TrimEnd()));
        Assert.Matches(@"^NICK'S RUN +1/12 +FRONTIER +3 MS$", rows[2]);
        Assert.Matches(@"^PRIYA'S RUN +3/12 +DEAD LINES +41 MS$", rows[4]);
        // Greyed, with the reason on the row: another version, a full crew.
        Assert.False(m.Items[1].Enabled);
        Assert.EndsWith("OTHER VERSION", rows[1]);
        Assert.False(m.Items[3].Enabled);
        Assert.EndsWith("FULL", rows[3]);
        // The first you can join is chosen; a network game joins by address, a Steam one by its lobby.
        Assert.Equal(2, m.Selected);
        Assert.Equal(new Launch.Join("192.168.1.20:27450"), m.Select());
        Assert.Equal(new Launch.JoinLobby(new Ballast.Online.LobbyId(77)), Choose(m, "PRIYA'S RUN"));
        Assert.False(m.TakeRefresh());
        Choose(m, "REFRESH");
        Assert.True(m.TakeRefresh());
        Assert.False(m.TakeRefresh());
        Assert.Contains(rows, r => r.StartsWith("ADDRESS", StringComparison.Ordinal));
    }

    [Fact]
    public void AGameOnTheNetworkAndOnSteamIsListedOnce()
    {
        var lan = new Ballast.Net.LanGame(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 27450), "nick", "", 1, 1, "darkterritory") { Lobby = "77", PingMs = 1 };
        var steam = new Ballast.Online.LobbyListing(new Ballast.Online.LobbyId(77), 1, 12, new Dictionary<string, string>(), 30);
        var game = Assert.Single(ListedGame.Merge([lan], [steam], "Steam"));
        Assert.IsType<Launch.Join>(game.Join);
    }

    [Fact]
    public void JoiningTakesATypedAddress()
    {
        var m = Menu();
        Choose(m, "JOIN");
        // Note 267: the join screen doesn't take typing until the address is entered.
        Assert.False(m.WantsText);
        Pick(m, "ADDRESS");
        m.Type("1.2.3.4");
        Assert.Equal("", m.Address);
        Assert.Null(m.Select());
        Assert.True(m.WantsText);
        for (int i = 0; i < 9; i++)
            m.Erase();
        m.Type("10.0.0.5:27015 !");
        Assert.Equal("10.0.0.5:27015", m.Address);
        // Enter ends it; JOIN joins it.
        Assert.Null(m.Select());
        Assert.False(m.WantsText);
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
    public void TheSoundSettingsAreSavedAndAreTheMixersVolumes()
    {
        // The audio checklist's mix-settings: master, effects, music and voice volumes, the microphone and its level.
        var m = new FrontEnd(C, R, Saves, SettingsPath, () => 42) { MicDevices = ["Desk Mic", "Headset"] };
        Choose(m, "SETTINGS");
        Pick(m, "EFFECTS VOLUME");
        m.Left();
        m.Left();
        m.Left();
        Pick(m, "VOICE VOLUME");
        m.Right();
        Pick(m, "MUSIC VOLUME");
        for (int i = 0; i < 12; i++)
            m.Left();
        Pick(m, "MICROPHONE");
        m.Right();
        m.Right();
        Pick(m, "MIC LEVEL");
        m.Right();
        m.Right();
        Assert.Equal(0.7, m.Settings.EffectsVolume, 6);
        Assert.Equal(1, m.Settings.VoiceVolume, 6);
        Assert.Equal(0, m.Settings.MusicVolume, 6);
        Assert.Equal("Headset", m.Settings.MicDevice);
        Assert.Equal(1.2, m.Settings.MicLevel, 6);
        Assert.Equal(m.Settings, Settings.Load(SettingsPath));
        // What the mixer's given: effects on the tells and the train, music silent, the crew at full.
        var v = m.Settings.Volumes;
        Assert.Equal(0.7f, v.Of(1), 4);
        Assert.Equal(0.7f, v.Of(5), 4);
        Assert.Equal(1f, v.Of(2), 4);
        Assert.Equal(0f, v.Of(7), 4);
        // Round past the last microphone to the default.
        Pick(m, "MICROPHONE");
        m.Right();
        Assert.Equal("", m.Settings.MicDevice);
    }

    [Fact]
    public void TheDisplaySettingsAreSavedAndSayWhatTheGameDrawsAt()
    {
        // T83: fullscreen, the resolution, the render scale, vsync.
        var m = Menu();
        Choose(m, "SETTINGS");
        Assert.Equal((1280, 720), m.Settings.InternalSize);
        Choose(m, "DISPLAY");
        Pick(m, "RESOLUTION");
        m.Right();
        m.Right();
        Pick(m, "RENDER SCALE");
        m.Left();
        Choose(m, "VSYNC");
        Assert.True(m.Settings.Fullscreen);
        Assert.Equal("1920x1080", m.Settings.Resolution);
        Assert.Equal(0.75, m.Settings.RenderScale, 6);
        Assert.False(m.Settings.VSync);
        // Drawn at three quarters of 1080p, and a window the resolution's size.
        Assert.Equal((1440, 810), m.Settings.InternalSize);
        Assert.Equal((1920, 1080), m.Settings.WindowSize);
        Assert.Equal(m.Settings, Settings.Load(SettingsPath));
        // Round from the last back to the first.
        Pick(m, "RESOLUTION");
        m.Right();
        m.Right();
        Assert.Equal(Settings.Resolutions[0], m.Settings.Resolution);
        // A resolution it doesn't know draws at 720p rather than failing.
        Assert.Equal((640, 360), new Settings { Resolution = "nonsense", RenderScale = 0.5 }.InternalSize);
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
        // Use, from E to F: F is the ladder's (T94), which takes E.
        Choose(m, "USE: E");
        Assert.Equal(Control.Use, m.Capturing);
        Assert.Contains(m.Items, i => i.Label == "USE: PRESS A KEY");
        m.Bind("F");
        Assert.Null(m.Capturing);
        Assert.Equal("F", m.Settings.KeyFor(Control.Use));
        Assert.Equal("E", m.Settings.KeyFor(Control.Ladder));
        Assert.Contains("GRAB LADDER", m.Message);
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
