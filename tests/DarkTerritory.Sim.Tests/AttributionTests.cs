using Ballast;
using Ballast.Net;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// GDD v1.4 App. C.9 (failure attribution) and D.12 (the incident report): every death writes one record naming the
/// contributing action C.9's table gives its failure, and who made it (or nobody); the report reads them out in the
/// clerk's voice with each death's fee and refund; the names and the report reach every client.
/// </summary>
public class AttributionTests
{
    static readonly PlayerTuning P = Tuning.Player;

    sealed class Night
    {
        public readonly World World;
        public readonly TrainOnLine Train;
        public readonly List<(int Id, PlayerState State)> Crew = [];

        public Night()
        {
            var route = RouteGenerator.Generate(Tuning.Route, RouteTier.Frontier, 1);
            Train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 4, 0.5)), route.Build(), 3_000, Tuning.Boiler);
            World = new World(Train, Tuning.Combat);
            World.EnableBodies();
            World.EnableRun(Tuning.Run, route, 600, authority: true);
            World.Run!.Resume(900, -1, Train.Boiler.Tender, 0);
            World.EnableHoldouts(Tuning.Holdouts, route);
            foreach (var (id, name) in new[] { (0, "Dave"), (1, "Priya"), (2, "Sam") })
                World.Names[id] = name;
        }

        public int Add(PlayerState s)
        {
            Crew.Add((Crew.Count, s));
            return Crew.Count - 1;
        }

        public void Kill(int id, DeathCause cause) => Crew[id] = (id, Crew[id].State with { Death = cause, Health = 0 });

        public void Step(double seconds = 0.1)
        {
            for (int t = 0; t < seconds * SimConstants.TickRate; t++)
            {
                World.BeginTick();
                World.Step(new TrainControls { Reverser = 1, Brake = 1 });
                World.ApplyDamage(id => Crew[id].State, (id, s) => Crew[id] = (id, s), Crew.Select(c => c.Id).ToList());
                World.StepBodies(Crew);
                World.StepRun([.. Crew.Select(c => c.State)]);
            }
        }

        public Incident DeathOf(int id) => Assert.Single(World.Attribution.Of(IncidentKind.Death), i => i.Victim == id);
    }

    [Fact]
    public void AGrabDeathNamesTheNearestLivingCrewmate()
    {
        var n = new Night();
        int victim = n.Add(PlayerMotor.SpawnOnRoof(n.Train, 3, 0, P));
        int near = n.Add(PlayerMotor.SpawnOnRoof(n.Train, 2, 0, P));
        n.Add(PlayerMotor.SpawnInCab(n.Train, P));
        n.Kill(victim, DeathCause.Eaten);
        n.Step();
        var d = n.DeathOf(victim);
        Assert.Equal(near, d.Actor);
        Assert.Equal("Swallowed by the Car Hugger", d.What);
        Assert.Contains("on the roof of car 3", d.Where);
        Assert.StartsWith("Nearest crew: {actor},", d.Action);
    }

    [Fact]
    public void FreezingLeftBehindNamesWhoWasOnTheThrottle()
    {
        var n = new Night();
        int driver = n.Add(PlayerMotor.SpawnInCab(n.Train, P));
        double back = n.Train.Dynamics.RearDistance - 300;
        int left = n.Add(PlayerMotor.SpawnOnGround(n.Train.Line.Sample(back).Position, n.Train.Line, back, P));
        n.World.Attribution.Drove(driver);
        n.Kill(left, DeathCause.Cold);
        n.Step();
        var d = n.DeathOf(left);
        Assert.Equal(driver, d.Actor);
        Assert.Matches(@"^Throttle: \{actor\}\. \d+ m from the train\.$", d.Action);
    }

    [Fact]
    public void ADeathByFireNamesWhoLitThatCarsLamp()
    {
        var n = new Night();
        int lighter = n.Add(PlayerMotor.SpawnInCab(n.Train, P));
        int burned = n.Add(PlayerMotor.SpawnOnRoof(n.Train, 2, 0, P) with { Parent = 2 });
        n.World.Attribution.LitLamp(2, lighter);
        n.Kill(burned, DeathCause.Burned);
        n.Step();
        Assert.Equal(lighter, n.DeathOf(burned).Actor);
    }

    [Fact]
    public void EveryDeathWritesExactlyOneRecord()
    {
        // D.14's attribution row: one record per death, whatever the cause, naming someone or nobody.
        var n = new Night();
        var causes = Enum.GetValues<DeathCause>().Where(c => c is not DeathCause.None and not DeathCause.Waiting).ToList();
        foreach (var _ in causes)
            n.Add(PlayerMotor.SpawnOnRoof(n.Train, 2, 0, P));
        n.Add(PlayerMotor.SpawnInCab(n.Train, P)); // someone left alive, so the night goes on
        for (int i = 0; i < causes.Count; i++)
            n.Kill(i, causes[i]);
        n.Step();
        var deaths = n.World.Attribution.Of(IncidentKind.Death).ToList();
        Assert.Equal(causes.Count, deaths.Count);
        Assert.All(deaths, d => Assert.False(string.IsNullOrWhiteSpace(d.What)));
        Assert.All(deaths, d => Assert.False(string.IsNullOrWhiteSpace(d.Action)));
        Assert.Equal(causes.Count, deaths.Select(d => d.Victim).Distinct().Count());
    }

    [Fact]
    public void TheReportReadsDeathsInTheClerksVoiceWithTheirFees()
    {
        var n = new Night();
        int victim = n.Add(PlayerMotor.SpawnOnRoof(n.Train, 3, 0, P));
        n.Add(PlayerMotor.SpawnInCab(n.Train, P));
        n.Kill(victim, DeathCause.Eaten);
        n.Step();
        var lines = IncidentLog.Lines(n.World, 350, 263, body => true);
        var line = Assert.Single(lines);
        Assert.Equal("Dave", line.Who);
        // D.12's shape: the failure, where, the contributing action and who made it, the fee and the refund.
        Assert.Matches(@"^Swallowed by the Car Hugger on the roof of car 3 at .+\. Nearest crew: Priya, \d+ m\. Fee 350\. Body recovered\. Refund 263\.$", line.Text);
        Assert.Equal(263, line.Refund);
        Assert.EndsWith("Body not recovered.", IncidentLog.Lines(n.World, 350, 263, body => false)[0].Text);
    }

    [Fact]
    public void ADerailmentEndsTheReportWithItsCauseSpeedAndThrottle()
    {
        var n = new Night();
        int driver = n.Add(PlayerMotor.SpawnInCab(n.Train, P));
        n.World.Attribution.Drove(driver);
        n.Train.Dynamics.Velocity = 19;
        n.World.Derail("over the 12 m/s board at 19.0 m/s");
        n.Step(0.2);
        var r = n.World.Run!.Report!;
        Assert.Equal(RunEnd.Derailed, r.End);
        var last = r.Lines[^1];
        Assert.Equal(IncidentKind.Derailed, last.Kind);
        Assert.StartsWith("Consist derailed, 68 km/h", last.Text);
        Assert.EndsWith("Over the 12 m/s board at 19.0 m/s. Throttle: Dave.", last.Text);
        // Everyone aboard died in it, each with a line naming the throttle too.
        Assert.Contains(r.Lines, l => l.Kind == IncidentKind.Death && l.Who == "Dave" && l.Text.Contains("Throttle: Dave, 68 km/h"));
    }

    [Fact]
    public void ACarCutLooseAndLeftIsReportedWithWhoPulledTheCouplerAndWhatWasInside()
    {
        var n = new Night();
        int cutter = n.Add(PlayerMotor.SpawnInCab(n.Train, P));
        n.Add(PlayerMotor.SpawnInCab(n.Train, P));
        // Behind car 2: car 3 (freight) and the guard van go.
        n.Train.Uncouple(2);
        n.World.Attribution.PulledCoupler(3, cutter);
        n.World.Derail("test");
        n.Step(0.2);
        var lost = Assert.Single(n.World.Run!.Report!.Lines, l => l.Kind == IncidentKind.CarLost);
        Assert.StartsWith("Cars 3-4 lost", lost.Text);
        Assert.Contains("Coupler: Dave.", lost.Text);
        Assert.Contains("Inside: freight", lost.Text);
    }

    [Fact]
    public void TheReportCrossesTheWireInChunks()
    {
        var lines = Enumerable.Range(0, 120).Select(i => new ReportLine(IncidentKind.Death, $"Crew {i}",
            $"Taken by something at km {i}. Nearest crew: Crew {i + 1}, {i * 7 % 50} m. Fee 350. Body not recovered. {Guid.NewGuid()}", 350)).ToList();
        var report = new RunReport(RunEnd.CrewLost, 1000, 12, 3, 1, 2, 0, 1, 2, 3, -100, 0, 4) { Lines = lines };
        var messages = Messages.ReportMessages(report);
        Assert.True(messages.Count > 1);
        Assert.All(messages, m => Assert.True(m.Length < 1200));
        var chunks = messages.Select((m, i) => (i, m[3..])).ToDictionary(x => x.i, x => x.Item2);
        Assert.Null(Messages.ReadReport(chunks.Where(c => c.Key != 1).ToDictionary(), messages.Count));
        var back = Messages.ReadReport(chunks, messages.Count)!;
        Assert.Equal(report.Lines.Select(l => l.Text), back.Lines.Select(l => l.Text));
        Assert.Equal(report.Net, back.Net);
    }

    [Fact]
    public void EveryoneLearnsEveryonesName()
    {
        var net = new LoopbackNetwork();
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(5_000)]));
        var host = new HostSession(net.CreateHost(), new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 3, 1)), line, 600), Tuning.Train, P);
        var clients = new[] { "Dave", "  Priya Shah-Okonkwo-Lindqvist ", "" }.Select(name =>
            new ClientSession(net.CreateClient(), new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 3, 1)), line, 600), Tuning.Train, P) { Name = name })
            .ToArray();
        for (int t = 0; t < 30; t++)
        {
            net.Advance(SimConstants.TickSeconds);
            host.Step();
            foreach (var c in clients)
                c.Step(default);
        }
        Assert.Equal("Dave", host.World.Names[clients[0].PlayerId!.Value]);
        Assert.Equal("Priya Shah-Okonkwo-L", host.World.Names[clients[1].PlayerId!.Value]); // trimmed, cut to 20
        Assert.False(host.World.Names.ContainsKey(clients[2].PlayerId!.Value));
        Assert.All(clients, c => Assert.Equal("Dave", c.World.Names[clients[0].PlayerId!.Value]));
    }
}
