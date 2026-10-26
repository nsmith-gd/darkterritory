using System.Text.Json;
using Ballast;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Corruption growing on the train (GDD Part Eleven q8; queue #730, ARCHITECTURE §8 note 1039): where a creature got on, the
/// campaign grows black under that car, at its couplings and a little in the tender, more each night; the save keeps it (an
/// older save loads clean), and the blacksmith scrapes it off for a small fee. Look only, behind campaign.json growth.
/// </summary>
public class TrainGrowthTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly CampaignTuning C = DataFile.Load<CampaignTuning>(Path.Combine(Content, CampaignTuning.File));
    static readonly CampaignTuning On = C with { Growth = (C.Growth ?? new()) with { On = true } };

    /// <summary>Frontier:7's four cars rolled in at yard speed through its town's gate, a hound got on car 2's roof on the way.</summary>
    static RunReport NightWithAHoundOnCar2()
    {
        var route = Routes.Generate(Content, "frontier:7", 4);
        double gate = route.Plan!.Terminus.GateM;
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 4, 1)), route.Build(), gate - 60, Tuning.Boiler);
        train.Dynamics.Velocity = 2.5;
        var world = new World(train, Tuning.Combat);
        world.EnableBodies();
        world.EnableRun(Tuning.Run, route, route.GateOr(Tuning.Route.YardLength), authority: true);
        world.Run!.Resume(1200, -1, train.Boiler.Tender, 0);
        world.EnableEnemies(HoundRunTests.Quiet, route, 1, crew: 1, authority: true);
        var driver = PlayerMotor.SpawnInCab(train, Tuning.Player);
        int car = train.Dynamics.Consist.Vehicles[2].Id;
        var hound = world.AddEnemy(e => new CinderHound(e, 900) { Health = HoundRunTests.Quiet.CinderHounds.Health });
        hound.Restore(SpinePhase.Telegraph, 0, HoundRunTests.Quiet.CinderHounds.Health, car, new Double3(0, train.Frames[car].Shape.RoofHeight, 0), 0, 0, 0, 900, 0);
        var run = world.Run!;
        for (int t = 0; t < 240 * SimConstants.TickRate && !run.Over; t++)
        {
            world.BeginTick();
            bool home = train.Dynamics.Distance >= run.HomeFront(train) + 2;
            if (!home && train.Dynamics.Velocity < 2.5)
                train.Dynamics.Velocity = 2.5;
            world.Step(new TrainControls { Reverser = 1, Brake = home ? 1 : 0 });
            world.StepRun([driver]);
        }
        return Assert.IsType<RunReport>(run.Report);
    }

    [Fact]
    public void ItGrowsWhereTheBoardersCameOnAndALittleMoreEachNight()
    {
        var report = NightWithAHoundOnCar2();
        // The hound counted once, on car 2.
        Assert.Equal(1, report.Boarded[2]);
        Assert.Equal(1, report.Boarded.Sum());

        var g = On.Growth!;
        var s = Campaign.Campaign.New(On, 1, "Crew", 7) with { Cars = 4 };
        var once = Campaign.Campaign.Settle(s, report, On);
        var grown = Assert.IsType<TrainGrowth>(once.Growth);
        Assert.Equal(g.PerBoarder, grown.Car(2), 6);
        // Its couplings, ahead of car 2 (car 1's rear) and behind it, a share of that; the tender a little; the rest clean.
        Assert.Equal(g.ToCouplings * g.PerBoarder, grown.Coupling(1), 6);
        Assert.Equal(g.ToCouplings * g.PerBoarder, grown.Coupling(2), 6);
        Assert.Equal(g.ToTender * g.PerBoarder, grown.Car(0), 6);
        Assert.Equal(0, grown.Car(1));
        Assert.Equal(0, grown.Car(3));
        Assert.Equal(0, grown.Coupling(0));
        Assert.Equal(0, grown.Coupling(3));

        // A quiet night after: what's there spreads a little; nothing new comes.
        var quiet = report with { Boarded = [] };
        var twice = Campaign.Campaign.Settle(once, quiet, On).Growth!;
        Assert.Equal(g.PerBoarder + g.PerNight, twice.Car(2), 6);
        Assert.Equal(0, twice.Car(1));
        // Night after night it goes black all over, and no further.
        var later = once;
        for (int n = 0; n < 60; n++)
            later = Campaign.Campaign.Settle(later, quiet, On);
        Assert.Equal(g.Most, later.Growth!.Car(2), 6);

        // Off (campaign.json growth's default until the director says), a night grows nothing.
        Assert.Null(Campaign.Campaign.Settle(s, report, C with { Growth = (C.Growth ?? new()) with { On = false } }).Growth);
    }

    [Fact]
    public void TheSaveKeepsItAndAnOlderSaveLoadsClean()
    {
        var s = Campaign.Campaign.New(On, 1, "Crew", 7) with { Cars = 4, Growth = new([0.05, 0, 0.4, 0, 0.9], [0, 0.2, 0.25, 0]) };
        var back = JsonSerializer.Deserialize<CampaignState>(JsonSerializer.Serialize(s, DataFile.Options), DataFile.Options)!;
        Assert.Equal(s.Growth!.Cars, back.Growth!.Cars);
        Assert.Equal(s.Growth.Couplings, back.Growth.Couplings);
        // A save from before it has none: a clean train, and a night settles on it as on any.
        var json = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(s, DataFile.Options))!.AsObject();
        Assert.True(json.Remove("growth"));
        var old = json.Deserialize<CampaignState>(DataFile.Options)!;
        Assert.Null(old.Growth);
        Assert.Equal(s.Cars, old.Cars);
        // And it rides to every machine in the night's setup, to the percent.
        var spec = TrainGrowth.Parse(s.Growth.Spec)!;
        Assert.Equal("5,0,40,0,90/0,20,25,0", s.Growth.Spec);
        Assert.Equal(s.Growth.Cars, spec.Cars);
        Assert.Null(TrainGrowth.Parse("not,a/spec"));
    }

    [Fact]
    public void TheBlacksmithScrapesItOffForASmallFee()
    {
        double fee = On.Growth!.ScrapeCost;
        Assert.InRange(fee, 1, C.Stores!.Lamps.Cost);
        var grown = Campaign.Campaign.New(On, 1, "Crew", 7) with { Cars = 4, Scrip = 500, Growth = new([0, 0, 0.4, 0, 0], [0, 0.1, 0.1, 0]) };
        var p = Campaign.Campaign.Scrape(On, grown);
        Assert.True(p.Ok, p.Refused);
        Assert.Null(p.State.Growth);
        Assert.Equal(500 - fee, p.State.Scrip);
        // Nothing to scrape, too poor, mid-night, or the growth off: the blacksmith says why.
        Assert.False(Campaign.Campaign.Scrape(On, p.State).Ok);
        Assert.False(Campaign.Campaign.Scrape(On, grown with { Scrip = fee - 1 }).Ok);
        Assert.False(Campaign.Campaign.Scrape(On, grown with { Current = new Contract(Route.RouteTier.Local, 1, 700) }).Ok);
        Assert.False(Campaign.Campaign.Scrape(C with { Growth = null }, grown).Ok);
    }
}
