using Ballast;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// GDD App. D.12, the run's end: the incident report (the deaths and where, the rescues, the bodies delivered and lost, the
/// cars lost, what the dead voted for, the bookmarks) on every machine, and the commendations: one each, from anyone in the
/// session, never to themselves, no demerits.
/// </summary>
[Collection(nameof(LineGenTests))]
public class IncidentReportTests
{
    static readonly Route.Route Line = LineGen.Routes.Generate(DataFile.FindContentRoot(), "frontier:7", 6);
    static readonly HoldoutTuning H = Tuning.Holdouts;

    /// <summary>Three aboard a kilometre short of the terminus; the third dies on the way in, then the train stops there.</summary>
    static HoldoutSessionTests.Crew ToTheTerminus(Action<HoldoutSessionTests.Crew>? whileDead = null)
    {
        var crew = new HoldoutSessionTests.Crew(front: Line.Length - 1000, players: 3);
        crew.Kill(2, crew.Host.Train.Dynamics.Distance - 30);
        crew.Run(0.5);
        whileDead?.Invoke(crew);
        crew.Run(800 / 20.0, holdSpeed: 20);
        crew.Run(2, holdSpeed: 0);
        Assert.True(crew.World.Run!.Over);
        crew.Run(0.5);
        return crew;
    }

    [Fact]
    public void EveryoneGetsTheReportAndItsSettlementAtTheEnd()
    {
        var crew = ToTheTerminus(c =>
        {
            // The dead player bookmarks what they're watching (a still, on their machine; the moment and whose view here).
            c.Clients[2].Send(new Request(RequestKind.Bookmark, 0));
            c.Run(0.3);
        });
        var host = crew.Host.Report!;
        Assert.NotNull(host);
        Assert.Equal(RunEnd.Delivered, host.Run.End);
        var death = Assert.Single(host.Deaths);
        Assert.Equal(crew.Id(2), death.Player);
        Assert.Equal(DeathCause.Mauled, death.Cause);
        Assert.StartsWith("km ", death.Where);
        var mark = Assert.Single(host.Bookmarks);
        Assert.Equal(crew.Id(2), mark.By);
        Assert.Contains(mark.Followed, new[] { (int)crew.Id(0), crew.Id(1) });
        Assert.Equal([crew.Id(0), crew.Id(1), crew.Id(2)], host.Session.Select(x => (byte)x));
        // The body was left out on the line: lost, and its fee charged.
        Assert.Equal(1, host.BodiesLost);
        Assert.Equal(H.FeeShare * Tuning.Run.Economy.PerCar["frontier"], host.Run.CrewLossFees);
        foreach (var c in crew.Clients)
        {
            Assert.Equal(host.ToJson(), c.Report!.ToJson());
            // The settlement is every machine's (the run-end screen, and the campaign's books where it's kept).
            Assert.Same(c.Report.Run, c.World.Run!.Report);
            Assert.Equal(host.Run.Net, c.World.Run.Report!.Net);
        }
    }

    [Fact]
    public void OneCommendationEachFromAnyoneToSomeoneElseAndOnlyAtTheEnd()
    {
        var crew = ToTheTerminus(c =>
        {
            // Not before the run's over.
            c.Clients[0].Send(new Request(RequestKind.Commend, c.Id(1), 0));
            c.Run(0.3);
        });
        Assert.Contains(crew.Host.Requests, q => q.Request.Kind == RequestKind.Commend && !q.Allowed);
        Assert.Empty(crew.World.Commendations);
        void Ask(int from, int to, int award) => crew.Clients[from].Send(new Request(RequestKind.Commend, to, award));
        // Never to themselves; nobody outside the session; only an award that exists.
        Ask(0, crew.Id(0), 0);
        Ask(0, 99, 0);
        Ask(0, crew.Id(1), H.Commendations.Awards.Length);
        crew.Run(0.3);
        Assert.Empty(crew.World.Commendations);
        // One each: the living and the dead alike.
        Ask(0, crew.Id(1), 1);
        crew.Run(0.3);
        Ask(0, crew.Id(2), 0);
        Ask(2, crew.Id(0), 3);
        crew.Run(0.5);
        Assert.Equal(
            [new Commendation(crew.Id(0), crew.Id(1), H.Commendations.Awards[1]), new Commendation(crew.Id(2), crew.Id(0), H.Commendations.Awards[3])],
            crew.World.Commendations);
        // Each one reaches every screen as it's given.
        foreach (var c in crew.Clients)
            Assert.Equal(crew.World.Commendations, c.Report!.Commendations);
        Assert.Equal([H.Commendations.Awards[1]], crew.Clients[1].Report!.To(crew.Id(1)).Select(x => x.Award));
    }

    [Fact]
    public void AReportTooBigForOneDatagramArrivesWhole()
    {
        var crew = ToTheTerminus(c =>
        {
            // A night of many deaths (as if): the report grows past a datagram, and goes in parts.
            var rng = new Random(3);
            for (int i = 0; i < 400; i++)
                c.World.Deaths.Add(new DeathRecord(40 + i % 7, 1000 + i, rng.Next(100_000) / 10.0, rng.Next(1_000_000) / 10.0,
                    (DeathCause)rng.Next(1, 4), null, false));
        });
        Assert.True(crew.Host.Report!.Compress().Length > Messages.ReportPartBytes);
        foreach (var c in crew.Clients)
            Assert.Equal(crew.World.Deaths.Count, c.Report!.Deaths.Count);
    }

    [Fact]
    public void TheRescuesAreWhoFreedWhomAndWhere()
    {
        var site = Line.Plan!.Holdouts.First(h => h.SiteKind == HoldoutSiteKind.Facility && !h.Second);
        var n = new HoldoutTests.Night(front: site.Zone.S0 + 150);
        n.DeadAtTheFortress(2);
        n.Step(0.2);
        var h = n.Holdouts.Of(site.Id)!;
        n.AtTheDoor(1, h, BodyKind.Crowbar);
        n.Hold(1, true);
        n.Step(H.Step(n.Holdouts.MethodFor(h, BodyKind.Crowbar)!.Value).Seconds + 0.3);
        Assert.True(n.Crew[2].Alive);
        // Then the whole crew's lost: the run's over.
        foreach (int id in n.Crew.Keys.ToList())
            n.Crew[id] = n.Crew[id] with { Health = 0, Death = DeathCause.Mauled };
        n.Step(0.1);
        Assert.Equal(RunEnd.CrewLost, n.World.Run!.End);
        var report = IncidentReport.Of(n.World, n.Crew.Keys);
        var rescue = Assert.Single(report.Rescues);
        Assert.Equal((2, 1, site.Name), (rescue.Player, rescue.By, rescue.Site));
        // Three deaths: 2 at the fortress end, then 1 and 2 (again) at the facility, where it happened by name.
        Assert.Equal([1, 2, 2], report.Deaths.Select(d => d.Player).Order());
        Assert.Equal(2, report.Deaths[0].Player);
        Assert.StartsWith("km ", report.Deaths[0].Where);
        Assert.All(report.Deaths.Skip(1), d => Assert.Equal(site.Name, d.Where));
        // The report reads back as it was written.
        Assert.Equal(report.ToJson(), IncidentReport.FromJson(report.ToJson()).ToJson());
        Assert.Equal(report.ToJson(), IncidentReport.Decompress(report.Compress()).ToJson());
    }
}
