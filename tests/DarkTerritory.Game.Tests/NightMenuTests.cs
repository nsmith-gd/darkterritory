using Ballast;
using Ballast.Render;
using DarkTerritory.Game;
using DarkTerritory.Game.Sound;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The in-night menu (note 292): Escape in a night opens RESUME, SETTINGS, INVITE and LEAVE over it, and a night is never
/// left on one stray key (before, a second Escape ended a host's night for the whole crew, with no word).
/// </summary>
public sealed class NightMenuTests : IDisposable
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly CampaignTuning C = DataFile.Load<CampaignTuning>(Path.Combine(Content, CampaignTuning.File));
    static readonly RunTuning R = DataFile.Load<RunTuning>(Path.Combine(Content, RunTuning.File));
    readonly string _dir = Path.Combine(Path.GetTempPath(), $"dt-nightmenu-{Guid.NewGuid():N}");

    string SettingsPath => Path.Combine(_dir, "settings.json");
    FrontEnd Menu() => new(C, R, new SaveSlots(Path.Combine(_dir, "saves"), C.SaveSlots), SettingsPath, () => 42);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    static string[] Labels(FrontEnd m) => [.. m.Items.Select(i => i.Label)];

    static Launch? Choose(FrontEnd m, string label)
    {
        int i = m.Items.ToList().FindIndex(x => x.Label.StartsWith(label, StringComparison.Ordinal));
        Assert.True(i >= 0, $"no '{label}' on {m.Screen}: {string.Join(" | ", Labels(m))}");
        while (m.Selected != i)
            m.Down();
        return m.Select();
    }

    [Fact]
    public void EscapeOpensTheMenuOverTheNightAndEscapeAgainShutsIt()
    {
        var m = Menu();
        m.OpenNight(new NightMenu());
        Assert.Equal(Screen.Night, m.Screen);
        // Alone on this machine: nobody to invite.
        Assert.Equal(["RESUME", "SETTINGS", "LEAVE"], Labels(m));
        // RESUME is first, and lit: Enter straight away goes back to the night.
        Assert.Equal(0, m.Selected);
        Assert.Contains("goes on", m.Items[0].Detail);
        MenuInput.Apply(m, MenuKey.Back);
        Assert.Null(m.Night);
        // Shut, where the front end was is put back (the title), and its pages aren't there without a night.
        Assert.Equal(Screen.Title, m.Screen);
        m.Show(Screen.Leave);
        Assert.Equal(Screen.Title, m.Screen);

        m.OpenNight(new NightMenu());
        Assert.Null(Choose(m, "RESUME"));
        Assert.Null(m.Night);
    }

    [Fact]
    public void LeavingIsAskedTwiceAndStayIsWhereTheConfirmationStarts()
    {
        var m = Menu();
        m.OpenNight(new NightMenu());
        // LEAVE doesn't leave: it asks, with STAY lit, so the Enter that chose LEAVE pressed again stays.
        Assert.Null(Choose(m, "LEAVE"));
        Assert.Equal(Screen.Leave, m.Screen);
        Assert.Equal("STAY", m.Items[m.Selected].Label);
        Assert.Null(m.Select());
        Assert.Equal(Screen.Night, m.Screen);
        // Escape on the confirmation backs up to the menu, not out of it.
        Choose(m, "LEAVE");
        m.Back();
        Assert.Equal(Screen.Night, m.Screen);
        Assert.NotNull(m.Night);
        // Asked and answered, it's the app's to leave.
        Choose(m, "LEAVE");
        Assert.IsType<Launch.Leave>(Choose(m, "LEAVE"));
    }

    [Fact]
    public void TheConfirmationSaysWhatLeavingCostsTheCrew()
    {
        var m = Menu();
        // A host with others aboard ends the night for them: it says so, and how many.
        m.OpenNight(new NightMenu(Hosting: true, Others: 3));
        Assert.Contains("END THE NIGHT", Labels(m));
        Choose(m, "END THE NIGHT");
        Assert.Equal("END THE NIGHT FOR EVERYONE", m.Items[1].Label);
        Assert.Contains("all 3 others", m.Items[1].Detail);
        // Said under STAY too, which is lit: it's read before LEAVE is reached.
        Assert.Equal(m.Items[1].Detail, m.Items[m.Selected].Detail);
        m.CloseNight();
        m.OpenNight(new NightMenu(Hosting: true, Others: 1));
        Choose(m, "END THE NIGHT");
        Assert.Contains("the other one", m.Items[1].Detail);
        m.CloseNight();
        // A joiner's leaving is theirs alone.
        m.OpenNight(new NightMenu(Hosting: false));
        Choose(m, "LEAVE");
        Assert.Contains("carries on without you", m.Items[1].Detail);
        m.CloseNight();
        // A campaign night alone carries on from the last stop; a quick night alone keeps nothing.
        m.OpenNight(new NightMenu(Campaign: true));
        Choose(m, "LEAVE");
        Assert.Contains("last stop", m.Items[1].Detail);
        m.CloseNight();
        m.OpenNight(new NightMenu());
        Choose(m, "LEAVE");
        Assert.Contains("Nothing from tonight", m.Items[1].Detail);
    }

    [Fact]
    public void OverTheReportLeavingIsntAskedTwice()
    {
        var m = Menu();
        m.OpenNight(new NightMenu(Hosting: true, Others: 2, Over: true));
        Assert.Contains("LEAVE", Labels(m));
        Assert.IsType<Launch.Leave>(Choose(m, "LEAVE"));
    }

    [Fact]
    public void TheMenuFollowsTheNightWhileItsOpen()
    {
        var m = Menu();
        m.OpenNight(new NightMenu(Hosting: true, Others: 2));
        Choose(m, "END THE NIGHT");
        Assert.Contains("all 2 others", m.Items[1].Detail);
        // Someone left while it was open: the confirmation says so, still on the same page.
        m.RefreshNight(new NightMenu(Hosting: true, Others: 1));
        Assert.Equal(Screen.Leave, m.Screen);
        Assert.Contains("the other one", m.Items[1].Detail);
        // The report came up: leaving goes at once from the first page.
        m.Back();
        m.RefreshNight(new NightMenu(Hosting: true, Others: 1, Over: true));
        Assert.IsType<Launch.Leave>(Choose(m, "LEAVE"));
        // Shut, a refresh opens nothing.
        m.CloseNight();
        m.RefreshNight(new NightMenu());
        Assert.Null(m.Night);
    }

    [Fact]
    public void InviteIsSteamsWhenThereIsALobbyAndOtherwiseSaysTheAddress()
    {
        var m = Menu();
        m.OpenNight(new NightMenu(Invites: true));
        Assert.IsType<Launch.Invite>(Choose(m, "INVITE"));
        // Still open: the overlay's over the menu.
        Assert.Equal(Screen.Night, m.Screen);
        m.CloseNight();
        // Hosting on the network without one: INVITE says the address to type.
        m.OpenNight(new NightMenu(JoinAt: "192.168.1.20:27960"));
        Assert.Null(Choose(m, "INVITE"));
        Assert.Contains("192.168.1.20:27960", m.Message);
        Assert.Equal(Screen.Night, m.Screen);
        m.CloseNight();
        // A joiner with no lobby has nobody to ask along.
        m.OpenNight(new NightMenu(Hosting: false));
        Assert.DoesNotContain("INVITE", Labels(m));
    }

    [Fact]
    public void SettingsInANightAreTheFrontEndsAndBackUpToTheMenu()
    {
        var m = Menu();
        m.OpenNight(new NightMenu());
        Choose(m, "SETTINGS");
        Assert.Equal(Screen.Settings, m.Screen);
        var labels = Labels(m);
        // The name was sent as you joined, and the headset's comfort is set as a night starts: neither's here.
        Assert.DoesNotContain(labels, l => l.StartsWith("PLAYER NAME", StringComparison.Ordinal));
        Assert.DoesNotContain(labels, l => l.StartsWith("VR ", StringComparison.Ordinal));
        Assert.Contains(labels, l => l.StartsWith("MOUSE SPEED", StringComparison.Ordinal));
        // Changed here, saved at once (the app takes them up the same frame).
        Choose(m, "MOUSE SPEED");
        m.Right();
        Assert.Equal(1.1, m.Settings.MouseSpeed, 6);
        Assert.Equal(1.1, Settings.Load(SettingsPath).MouseSpeed, 6);
        // CONTROLS backs up to the settings, the settings to the menu, and BACK says so too.
        Choose(m, "CONTROLS");
        m.Back();
        Assert.Equal(Screen.Settings, m.Screen);
        m.Back();
        Assert.Equal(Screen.Night, m.Screen);
        Choose(m, "SETTINGS");
        Choose(m, "BACK");
        Assert.Equal(Screen.Night, m.Screen);
        // Out of the night, the settings are the title's again, the name and the headset's with them.
        m.CloseNight();
        m.Show(Screen.Settings);
        Assert.Contains(Labels(m), l => l.StartsWith("PLAYER NAME", StringComparison.Ordinal));
    }

    [Fact]
    public void TheMenuDrawsOverWhatsThereWithTheNightDimmedBehindIt()
    {
        var m = Menu();
        var o = new Overlay();
        o.Text(10, 10, "HUD", new System.Numerics.Vector4(1, 1, 1, 1));
        int hud = o.Count;
        m.OpenNight(new NightMenu(Hosting: true, Others: 3, JoinAt: "192.168.1.20:27960"));
        m.Draw(o, 480, 270);
        // The HUD's still there under it (the menu doesn't clear what the night drew), and the menu's on top, inside the frame.
        Assert.True(o.Count > hud + 200, $"drew {o.Count - hud} vertices");
        Assert.All(o.Vertices, v => Assert.InRange(v.Position.X, -2, 482));
        Assert.All(o.Vertices, v => Assert.InRange(v.Position.Y, -2, 272));
        // The mouse works it, as the front end's: a click on RESUME shuts it.
        Assert.Null(MenuInput.Apply(m, MenuKey.None, mouse: new MenuMouse(new(30, 41), Moved: true, Left: true)));
        Assert.Null(m.Night);
    }

    [Fact]
    public void TheMenusSoundsAreTheFrontEnds()
    {
        var m = Menu();
        var heard = new List<string>();
        m.Cue = heard.Add;
        m.OpenNight(new NightMenu());
        m.Down();
        m.Back();
        Assert.Equal([UiCue.Select, UiCue.Move, UiCue.Back], heard);
    }
}
