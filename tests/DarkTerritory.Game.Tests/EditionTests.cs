using Ballast;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The demo edition (T79): editions/demo baked over the base content is GDD §21/§35's demo, the Frontier with two
/// facilities and the roster of five, and the front end offers only that.
/// </summary>
public sealed class EditionTests : IDisposable
{
    static readonly string Content = DataFile.FindContentRoot();
    readonly string _dir = Path.Combine(Path.GetTempPath(), $"dt-edition-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void TheBaseContentIsTheFullGame()
    {
        var full = EditionTuning.Load(Content);
        Assert.False(full.Demo);
        Assert.True(full.Campaign);
        Assert.All(Enum.GetValues<RouteTier>(), t => Assert.True(full.HasTier(t)));
        Assert.Empty(DataFile.Load<EnemyTuning>(Path.Combine(Content, EnemyTuning.File)).Director.Roster);
    }

    [Fact]
    public void TheDemoBakedInIsTheFrontierWithTwoFacilitiesAndTheFive()
    {
        var demo = Mods.Bake(Content, "demo", Path.Combine(_dir, "content"));
        var edition = EditionTuning.Load(demo);
        Assert.True(edition.Demo);
        Assert.False(edition.Campaign);
        Assert.True(edition.HasTier(RouteTier.Frontier));
        Assert.False(edition.HasTier(RouteTier.DeadLines));
        var roster = DataFile.Load<EnemyTuning>(Path.Combine(demo, EnemyTuning.File)).Director.Roster;
        // GDD v1.1 §21's five (the Choir runs underneath), and the hazards and fire.
        Assert.Equal(["trackDoll", "carHugger", "whistler", "tippyToesie", "ribbits", "sleepers", "drift", "carFire"], roster);
        // The rest of enemies.json is the base game's, patched, not replaced.
        Assert.Equal(DataFile.Load<EnemyTuning>(Path.Combine(Content, EnemyTuning.File)).Director.Pressure.Threshold,
            DataFile.Load<EnemyTuning>(Path.Combine(demo, EnemyTuning.File)).Director.Pressure.Threshold);
        foreach (ulong seed in new ulong[] { 1, 3, 7 })
        {
            var route = Sim.LineGen.Routes.Generate(demo, RouteTier.Frontier, seed, 6);
            Assert.Equal(2, route.Of(FeatureKind.Facility).Count());
            Assert.Empty(route.Of(FeatureKind.Grease));
        }
    }

    [Fact]
    public void TheDemosFrontEndHasAQuickNightOnTheFrontierAndNoCampaign()
    {
        var demo = Mods.Bake(Content, "demo", Path.Combine(_dir, "content"));
        var c = DataFile.Load<CampaignTuning>(Path.Combine(demo, CampaignTuning.File));
        var r = DataFile.Load<RunTuning>(Path.Combine(demo, RunTuning.File));
        var m = new FrontEnd(c, r, new SaveSlots(Path.Combine(_dir, "saves"), c.SaveSlots), Path.Combine(_dir, "settings.json"), () => 1, EditionTuning.Load(demo));
        Assert.DoesNotContain(m.Items, i => i.Label == "CAMPAIGN");
        Assert.Equal("QUICK NIGHT", m.Items[0].Label);
        m.Select();
        Assert.Equal(Screen.QuickNight, m.Screen);
        // The tier's the Frontier and can't be changed; the cars stop at the demo's most.
        var tier = m.Items.First(i => i.Label.StartsWith("TIER", StringComparison.Ordinal));
        Assert.Equal("TIER: FRONTIER", tier.Label);
        Assert.False(tier.Enabled);
        int cars = m.Items.ToList().FindIndex(i => !i.Heading && i.Label.StartsWith("CARS", StringComparison.Ordinal));
        while (m.Selected != cars)
            m.Down();
        for (int i = 0; i < 30; i++)
            m.Right();
        Assert.Contains(m.Items, i => i.Label == "CARS: 8");
        int alone = m.Items.ToList().FindIndex(i => i.Label == "PLAY");
        while (m.Selected != alone)
            m.Down();
        var night = Assert.IsType<Launch.Night>(m.Select());
        Assert.StartsWith("frontier:", night.Route);
        m.NightOver();
        Assert.Equal(Screen.Title, m.Screen);
        Assert.Contains("Wishlist", m.Message);
    }
}
