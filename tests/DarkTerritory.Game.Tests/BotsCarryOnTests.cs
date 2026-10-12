using Ballast;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Towns;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The bots carry on (queue #343, ARCHITECTURE §8 note 611; D1: "the director plays campaign nights with bots"): a campaign
/// night takes the crew's bots, and the host going on from the town after a delivered night (note 606) takes the same crew
/// into the next one, aboard and working when it opens.
/// </summary>
// Hud.Keys is the HUD's settings, static, which the front end's settings touch (FrontEndTests' collection).
[Collection("Hud.Keys")]
public sealed class BotsCarryOnTests : IDisposable
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly CampaignTuning C = DataFile.Load<CampaignTuning>(Path.Combine(Content, CampaignTuning.File));
    static readonly RunTuning R = DataFile.Load<RunTuning>(Path.Combine(Content, RunTuning.File));
    readonly string _dir = Path.Combine(Path.GetTempPath(), $"dt-botscarry-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    static Launch.CampaignNight Tonight(FrontEnd m)
    {
        int i = m.Items.ToList().FindIndex(x => !x.Heading && x.Label.StartsWith("TONIGHT", StringComparison.Ordinal));
        Assert.True(i >= 0, string.Join(" | ", m.Items.Select(x => x.Label)));
        while (m.Selected != i)
            m.Down();
        return Assert.IsType<Launch.CampaignNight>(m.Select());
    }

    /// <summary>A hosted campaign night as the app starts it (Program's Start), with <paramref name="bots"/>, run on a while.</summary>
    static NetPlaySession Host(string route, int bots, double seconds, out double start)
    {
        var night = NetPlaySession.HostGame(Content, new SessionSetup(Route: route, Cars: 4, Enemies: false), port: null, bots: bots);
        start = night.Host!.Train.Dynamics.Distance;
        for (int t = 0; t < seconds * SimConstants.TickRate; t++)
        {
            night.Step(default);
            Thread.Sleep(1);
        }
        return night;
    }

    [Fact]
    public void TheHostsBotsGoOnWithItToTheNextNightAboardAndWorking()
    {
        var saves = new SaveSlots(Path.Combine(_dir, "saves"), C.SaveSlots);
        var m = new FrontEnd(C, R, saves, Path.Combine(_dir, "settings.json"), () => 42);
        saves.Save(Campaign.New(C, 1, "Crew 1", 5));
        m.ShowFortress(1);
        // The crew's bots on the board, as `--campaign 1 --bots 2` or the CREW choice leaves them.
        Assert.Contains(m.Items, i => i.Label == "CREW: JUST YOU");
        m.Bots = 2;
        m.ShowFortress(1);
        Assert.Contains(m.Items, i => i.Label == "CREW: YOU AND 2 BOTS");
        var first = Tonight(m);
        Assert.Equal(2, first.Bots);

        // Night 1, its bots aboard; delivered (forced: a night's 45 minutes is too long for a test).
        var s = Campaign.Begin(saves.Load(1)!, Campaign.Offers(C, R, saves.Load(1)!)[first.Contract]);
        string route1 = Campaign.RouteOf(s, s.Current!);
        TownAt? town;
        using (var night1 = Host(route1, first.Bots, 1, out _))
        {
            Assert.Equal(2, night1.BotCrew!.Bots.Count);
            town = TownAt.Terminus(night1.Route!, LineGenContent.Cached(Content).Config.Tiers.Fortress.Identities);
            s = Campaign.Arrived(Campaign.Settle(s, Staging.Report(night1.Host!.World, RunEnd.Delivered)), Staging.Report(night1.Host.World, RunEnd.Delivered), town);
        }
        Assert.NotNull(town);
        Assert.Equal(town, s.Town);
        saves.Save(s);

        // Back at the board in the town (note 604): the next contract takes the same crew.
        m.ShowFortress(1);
        var next = Tonight(m);
        Assert.Equal(2, next.Bots);
        string route2 = Campaign.RouteOf(saves.Load(1)!, Campaign.Offers(C, R, saves.Load(1)!)[next.Contract]);
        Assert.Equal(town, TownAt.Split(route2).From);

        // Night 2 from that town: both bots aboard, alive, the first in the cab and the train under way.
        using var night2 = Host(route2, next.Bots, 40, out double start);
        var crew = Assert.IsType<Sim.Bots.BotCrew>(night2.BotCrew);
        Assert.Equal(2, crew.Bots.Count);
        Assert.Equal(town!.Name, night2.Route!.Plan!.Fortress.Name);
        var bots = crew.Bots.Select(b => night2.Host!.Players.First(p => p.Id == b.Session.PlayerId).State).ToList();
        Assert.All(bots, b => Assert.True(b.Alive));
        Assert.All(bots, b => Assert.True(b.Parent >= 0, $"a bot isn't aboard: {b.Parent}"));
        Assert.True(PlayerMotor.InCab(bots[0], night2.Host!.Train), $"the driver bot is at {bots[0].Parent}");
        Assert.True(night2.Host.Train.Dynamics.Distance > start + 20, $"the train went {night2.Host.Train.Dynamics.Distance - start:0} m");
    }
}
