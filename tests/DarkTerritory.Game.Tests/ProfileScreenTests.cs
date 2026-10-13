using Ballast;
using Ballast.Render;
using DarkTerritory.Game;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The PROFILE page (note 293; GDD App. D.12): the commendations a player's crews have given them, kept in the profile and
/// shown, with where the nights' stills are kept.
/// </summary>
public sealed class ProfileScreenTests : IDisposable
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly CampaignTuning C = DataFile.Load<CampaignTuning>(Path.Combine(Content, CampaignTuning.File));
    static readonly RunTuning R = DataFile.Load<RunTuning>(Path.Combine(Content, RunTuning.File));
    readonly string _dir = Path.Combine(Path.GetTempPath(), $"dt-profile-{Guid.NewGuid():N}");

    FrontEnd Menu() => new(C, R, new SaveSlots(Path.Combine(_dir, "saves"), C.SaveSlots), Path.Combine(_dir, "settings.json"), () => 42);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    static Launch? Choose(FrontEnd m, string label)
    {
        int i = m.Items.ToList().FindIndex(x => !x.Heading && x.Label.StartsWith(label, StringComparison.Ordinal));
        Assert.True(i >= 0, $"no '{label}' on {m.Screen}: {string.Join(" | ", m.Items.Select(x => x.Label))}");
        while (m.Selected != i)
            m.Down();
        return m.Select();
    }

    [Fact]
    public void TheTitleOpensTheProfileAndEscapeComesBack()
    {
        var m = Menu();
        Choose(m, "PROFILE");
        Assert.Equal(Screen.Profile, m.Screen);
        Assert.Equal(["BACK"], m.Items.Select(i => i.Label));
        m.Back();
        Assert.Equal(Screen.Title, m.Screen);
    }

    [Fact]
    public void EachBadgeIsShownWithHowManyTimesACrewGaveIt()
    {
        // As the app keeps it: a night's commendations recorded into the profile file, then loaded for the page.
        var profile = new PlayerProfile(Path.Combine(_dir, "profile.json"));
        profile.Record([(2, 1, 0), (3, 1, 0), (4, 1, 2), (1, 2, 1)], me: 1);
        var m = Menu();
        m.Profile = profile.Load();
        m.StillsFolder = "/home/nick/.local/share/DarkTerritory/bookmarks";
        m.Show(Screen.Profile);
        Assert.Equal("GIVEN BY YOUR CREWS: 3 IN ALL", m.TallyLine);
        // Every one of the starter set is there in its order, given or not: one given to someone else isn't yours.
        Assert.Equal([(UiStyle.Commendation.CameBackForMe, 2), (UiStyle.Commendation.HeldTheSwitch, 0), (UiStyle.Commendation.KeptTheFire, 1),
            (UiStyle.Commendation.BroughtThemHome, 0), (UiStyle.Commendation.LastOneStanding, 0)], m.Tally);
        var o = new Overlay();
        m.Draw(o, 480, 270);
        // The page draws more than a bare list's BACK: the badges, their rows and the folder.
        Assert.True(o.Count > 2000, $"drew {o.Count} vertices");
        Assert.All(o.Vertices, v => Assert.InRange(v.Position.X, -2, 482));
        Assert.All(o.Vertices, v => Assert.InRange(v.Position.Y, -2, 272));
    }

    [Fact]
    public void ANewPlayersProfileSaysHowTheyreGiven()
    {
        var m = Menu();
        m.Show(Screen.Profile);
        Assert.StartsWith("NO COMMENDATIONS YET", m.TallyLine);
        Assert.All(m.Tally, t => Assert.Equal(0, t.Given));
    }
}
