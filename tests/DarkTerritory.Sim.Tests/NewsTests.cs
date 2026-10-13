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
/// Note 663 (queue #394; the director: "did a coupler break on its own or was that a creature?"): cars the engine loses and
/// its failing or breaking down are told to everyone as they happen, with what did it, and the report says the same.
/// </summary>
public class NewsTests
{
    static readonly PlayerTuning P = Tuning.Player;
    static readonly LooseTuning L = DataFile.Load<UpkeepTuning>(Path.Combine(DataFile.FindContentRoot(), UpkeepTuning.File)).Coupling;

    sealed class Night
    {
        public readonly World World;
        public readonly TrainOnLine Train;

        public Night(double speed, double engine = 1)
        {
            var route = RouteGenerator.Generate(Tuning.Route, RouteTier.Frontier, 1);
            Train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 4, 0.5)), route.Build(), 3_000, Tuning.Boiler);
            Train.Dynamics.Velocity = speed;
            Train.Vehicles[0].Integrity = engine;
            World = new World(Train, Tuning.Combat) { NewsTuning = P.News };
            World.EnableBodies();
            World.EnableRun(Tuning.Run, route, 600, authority: true);
            World.Run!.Resume(900, -1, Train.Boiler.Tender, 0);
            foreach (var (id, name) in new[] { (0, "Dave"), (1, "Priya") })
                World.Names[id] = name;
        }

        public void Step(double seconds = 0.1)
        {
            for (int t = 0; t < seconds * SimConstants.TickRate; t++)
            {
                World.BeginTick();
                World.Step(new TrainControls { Reverser = 1 });
                World.StepRun([]);
            }
        }

        public string Said => News.Line(World, Assert.Single(World.News));
        public ReportLine Lost() => Assert.Single(World.Run!.Report!.Lines, l => l.Kind == IncidentKind.CarLost);
    }

    [Fact]
    public void ADroppedPinIsToldToTheCrewWithWhatWorkedItLooseAndTheReportSaysTheSame()
    {
        var n = new Night(speed: 12);
        n.Train.Loose = L;
        n.Step();
        n.World.Attribution.Drove(0);
        n.World.Attribution.Loosened(2, LooseCause.RunIn, 14);
        n.Train.Vehicles[2].Loose = L.PartAfter - 0.05;
        n.Step(0.5);
        Assert.Equal(2, n.Train.TrainRakes);
        Assert.Equal("CARS 3-4 LOST · THE PIN WORKED LOOSE", n.Said);
        // Up for its few seconds, then gone; the host's log keeps it for the report.
        n.Step(P.News.Seconds);
        Assert.Empty(n.World.News);
        Assert.Single(n.World.NewsLog);
        n.World.Derail("test");
        n.Step(0.2);
        Assert.Contains("A hard brake at 50 km/h worked the coupling's pin loose, and it parted. Driving: Dave.", n.Lost().Text);
    }

    [Fact]
    public void ACrewmatesCutIsNewsWithTheTrainRunningAndNotWhenItsSetOutAtAStand()
    {
        var n = new Night(speed: 10);
        n.Step();
        n.Train.Uncouple(2, Parting.Crew, 1);
        n.Step();
        Assert.Equal("CARS 3-4 LOST · CUT LOOSE BY PRIYA", n.Said);

        var stood = new Night(speed: 0);
        stood.Step();
        stood.Train.Uncouple(2, Parting.Crew, 1);
        stood.Step();
        Assert.Empty(stood.World.News);
        Assert.False(Assert.Single(stood.World.NewsLog).Told);
    }

    [Fact]
    public void AKnottersPartingIsTheKnottersInTheReportNotTheLastHandOnThatCoupler()
    {
        // Note 630's: the report named the pick-up's shunter for a Knotter's split.
        var n = new Night(speed: 8);
        n.Step();
        n.World.Attribution.PulledCoupler(3, 0);
        n.Train.Uncouple(2, Parting.Knotter);
        n.Step();
        Assert.Equal("CARS 3-4 LOST · A KNOTTER PARTED THEM", n.Said);
        n.World.Derail("test");
        n.Step(0.2);
        Assert.Contains("A Knotter took the coupling, and it parted.", n.Lost().Text);
        Assert.DoesNotContain("Coupler:", n.Lost().Text);
    }

    [Fact]
    public void TheCarHuggersAndThePassengersAreSaidAsTheirs()
    {
        var n = new Night(speed: 8);
        n.Step();
        n.Train.Uncouple(3, Parting.CarHugger);
        n.Step();
        Assert.Equal("CAR 4 LOST · THE CAR HUGGER TOOK IT", n.Said);
        n.Train.Uncouple(2, Parting.Passenger);
        n.Step();
        Assert.Equal("CAR 3 LOST · THE PASSENGER ROLLED IT AWAY", News.Line(n.World, n.World.News[^1]));
    }

    [Fact]
    public void TheEngineGoingDownIsToldWithWhatLastKnockedIt()
    {
        var n = new Night(speed: 0);
        n.Step();
        var engine = n.Train.Vehicles[0];
        engine.Integrity = 0.45;
        engine.Knock = (KnockCause.BufferStop, 6);
        n.Step();
        Assert.Equal("ENGINE FAILING · HIT THE BUFFER STOP AT 22 KM/H", n.Said);
        engine.Integrity = 0;
        engine.Knock = (KnockCause.Worn, 10);
        n.Step();
        Assert.Equal("ENGINE BROKEN DOWN · RUN ON FAILING", News.Line(n.World, n.World.News[^1]));

        // A night begun with the engine failing (carried over, note 576) isn't news.
        var begun = new Night(speed: 0, engine: 0.3);
        begun.Step(1);
        Assert.Empty(begun.World.NewsLog);
    }

    [Fact]
    public void TheNewsReachesEveryClientAndGoesWhenItsTime()
    {
        var line = RailLine.Load(Path.Combine(DataFile.FindContentRoot(), "lines/test-loop.json"));
        TrainOnLine Train() => new(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), line, 600);
        var net = new LoopbackNetwork();
        var host = new HostSession(net.CreateHost(), Train(), Tuning.Train, P);
        var clients = new[] { new ClientSession(net.CreateClient(), Train(), Tuning.Train, P), new ClientSession(net.CreateClient(), Train(), Tuning.Train, P) };
        void Run(double seconds)
        {
            for (int t = 0; t < seconds * SimConstants.TickRate; t++)
            {
                net.Advance(SimConstants.TickSeconds);
                host.Step();
                foreach (var c in clients)
                    c.Step(default);
            }
        }
        Run(1);
        host.World.Train.Uncouple(3, Parting.Pin);
        Run(0.5);
        var told = News.Line(host.World, Assert.Single(host.World.News));
        Assert.Equal("CARS 4-6 LOST · THE PIN WORKED LOOSE", told);
        foreach (var c in clients)
            Assert.Equal(told, News.Line(c.World, Assert.Single(c.World.News)));
        Run(P.News.Seconds);
        Assert.All(clients, c => Assert.Empty(c.World.News));
    }
}
