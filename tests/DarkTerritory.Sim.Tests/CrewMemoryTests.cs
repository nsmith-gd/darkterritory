using System.Text.Json;
using Ballast;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Towns;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Crew memory (GDD §11, §3.1; the director, 14 Oct 2026; ARCHITECTURE §8 note 721): the logbook's page a night from the
/// incident report, kept in the campaign's save; the crews a player rode with, the ended ones posted on the town's board
/// of lost crews.
/// </summary>
public class CrewMemoryTests
{
    static readonly string Content = DataFile.FindContentRoot();

    static RunReport Report(RunEnd end, double net = 1450) =>
        new(end, 1500, 7, 5, 1, 4.2, 3200, 100, 20, 46, net, 3, 1)
        {
            Lines =
            [
                new ReportLine(IncidentKind.Grab, "Okafor", "Grabbed by the Car Hugger on the roof of car 3 at km 1. Got away."),
                new ReportLine(IncidentKind.Death, "Dunmore", "Mauled by the hounds on the roof of car 3 at km 4. Nearest crew: Dave, 12 m. Fee 350. Body recovered. Refund 263.") { Seconds = 400 },
                new ReportLine(IncidentKind.Rescue, "Okafor", "Freed from the Holdout at Hollin Halt. Broken out by Dave."),
            ],
        };

    [Fact]
    public void ANightsPageKeepsTheReportsLossesTheCommendationsAndTheNet()
    {
        var page = Logbook.Page(Report(RunEnd.Delivered), 3, "Frontier 7, Hollin Halt to Barrow Bay", "14 October 2026",
            [("Dave", "Came Back For Me", "Okafor"), ("Priya", "Kept the Fire", "Dave")]);
        Assert.Equal(3, page.Night);
        Assert.Equal(1450, page.Net);
        Assert.Equal("DELIVERED. 5 CARS, 1 LOST. CREW HOME 3", page.Line);
        // The cause of death, as the clerk read it; not the grabs got away from or the rescues.
        var loss = Assert.Single(page.Losses);
        Assert.StartsWith("DUNMORE: Mauled by the hounds", loss, StringComparison.Ordinal);
        Assert.Equal(["DAVE: Came Back For Me, from Okafor", "PRIYA: Kept the Fire, from Dave"], page.Commendations);
        // A derailment's own line goes in too: the train's end is a loss.
        var derailed = Report(RunEnd.Derailed) with { Lines = [new ReportLine(IncidentKind.Derailed, "", "Consist derailed at km 14, 68 km/h. Throttle: Dave.")] };
        Assert.Equal(["Consist derailed at km 14, 68 km/h. Throttle: Dave."], Logbook.Page(derailed, 4, "", "", []).Losses);
        Assert.StartsWith("TAKEN AT DAWN", Logbook.Page(Report(RunEnd.DawnMissed), 5, "", "", []).Line, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLogbookGainsAPageANightAndANightWrittenAgainReplacesItsOwn()
    {
        var book = Logbook.With([], Logbook.Page(Report(RunEnd.Delivered), 1, "Local 4", "12 October 2026", []) with { Photo = "night1.png" });
        book = Logbook.With(book, Logbook.Page(Report(RunEnd.Derailed), 2, "Frontier 3", "13 October 2026", []));
        Assert.Equal([1, 2], book.Select(p => p.Night));
        // Night 1 written again (its commendations came in), without the photo this time: the photo's kept, one page still.
        book = Logbook.With(book, Logbook.Page(Report(RunEnd.Delivered), 1, "Local 4", "12 October 2026", [("Dave", "Kept the Fire", "Priya")]));
        Assert.Equal([1, 2], book.Select(p => p.Night));
        Assert.Equal("night1.png", book[0].Photo);
        Assert.Single(book[0].Commendations);
    }

    [Fact]
    public void TheCampaignsLogbookRoundTripsTheSaveAndOutlivesTheCampaign()
    {
        var t = DataFile.Load<CampaignTuning>(Path.Combine(Content, CampaignTuning.File)) with { DawnEndsCampaign = true };
        var s = Campaign.Campaign.Begin(Campaign.Campaign.New(t, 1, "The Night Mail", 99), new Contract(RouteTier.Frontier, 7, 1000));
        var report = Report(RunEnd.DawnMissed, -900);
        s = Campaign.Campaign.Settle(s, report, t);
        s = s with { Logbook = Logbook.With(s.Logbook, Logbook.Page(report, s.Runs, "Frontier 7", "14 October 2026", [])) };
        Assert.True(s.Over);
        var back = JsonSerializer.Deserialize<CampaignState>(JsonSerializer.Serialize(s, DataFile.Options), DataFile.Options)!;
        var page = Assert.Single(back.Logbook);
        Assert.Equal(1, page.Night);
        Assert.Equal(RunEnd.DawnMissed, page.End);
        Assert.Equal(s.Logbook[0].Losses, page.Losses);
        // A save from before the logbook reads as an empty one.
        Assert.Empty(JsonSerializer.Deserialize<CampaignState>("{\"name\":\"Old\",\"cars\":3}", DataFile.Options)!.Logbook);
        // Its key is the same on every machine (the setup carries it), and tells two crews of one name apart.
        Assert.Equal(Logbook.Key(s), Logbook.Key(back));
        Assert.NotEqual(Logbook.Key(s), Logbook.Key(s with { Seed = 100 }));
    }

    [Fact]
    public void ACampaignTakenAtDawnIsPostedOnTheBoardOfLostCrews()
    {
        IReadOnlyList<CrewLog> crews = [];
        crews = Logbook.Remember(crews, "a#1", "The Night Mail", Logbook.Page(Report(RunEnd.Delivered), 1, "Local 4", "", []), over: false);
        crews = Logbook.Remember(crews, "b#2", "Cold Feet", Logbook.Page(Report(RunEnd.Delivered), 1, "Local 2", "", []), over: false);
        Assert.Empty(Logbook.Lost(crews));
        var taken = Report(RunEnd.DawnMissed) with { Lines = [new ReportLine(IncidentKind.Death, "Priya", "Taken at dawn by a Waker on the line at km 21. At the controls at dawn: Dave.")] };
        crews = Logbook.Remember(crews, "a#1", "The Night Mail", Logbook.Page(taken, 2, "Dead Lines 2", "", []), over: true);
        var lost = Assert.Single(Logbook.Lost(crews));
        Assert.Equal("The Night Mail", lost.Name);
        Assert.Equal(2, lost.Pages.Count);
        var (title, text) = Logbook.Card(lost);
        Assert.Equal("The Night Mail", title);
        Assert.Equal("Ran 2 nights. TAKEN AT DAWN. PRIYA: Taken at dawn by a Waker on the line at km 21. At the controls at dawn: Dave.", text);
        // Once over, a page written again doesn't open it up.
        crews = Logbook.Remember(crews, "a#1", "The Night Mail", Logbook.Page(taken, 2, "Dead Lines 2", "", []), over: false);
        Assert.Single(Logbook.Lost(crews));
    }

    [Fact]
    public void EveryTownsSquareHasTheBoardOfLostCrewsBesideItsNoticeBoard()
    {
        var towns = TownContent.Load(Content)!;
        foreach (string spec in new[] { "frontier:7", "local:3", "deadLines:2" })
        {
            var route = Routes.Generate(Content, spec, 6);
            var line = route.Build();
            double gate = route.GateOr(RouteTuning.Load(Content).YardLength);
            var town = new Town(TownGenerator.Generate(towns, TownSite.Of(route, gate, [], towns)), towns.Tuning, line);
            var board = town.Plan.Fixtures.Single(f => f.Kind == "board");
            var lost = town.Plan.Fixtures.Single(f => f.Kind == "lostcrews");
            // Beside it, a few metres along, clear of it; with words of its own to read when nothing's posted.
            Assert.Equal(board.D, lost.D);
            Assert.InRange(Math.Abs(board.S - lost.S), board.SolidS + lost.SolidS + 0.5, 6);
            Assert.False(string.IsNullOrEmpty(lost.Text));
            // Read from in front of it, it's the thing looked at, not the notice board beside it.
            var at = town.LookAt(lost);
            var before = at + town.Direction(lost.S, lost.FaceS, lost.FaceD) * 1.6 + Double3.Up * 0.1;
            Assert.Equal(new TownTarget(TownTargetKind.Fixture, lost.Id), town.Target(before, (at - before).Normalized));
        }
    }
}
