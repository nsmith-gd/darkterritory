using Ballast;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 854 (queue #582; the director's playtest of 16 Oct, line 13: "What is making these couplings loose? It doesn't make
/// any sense"): a pin's jump is told to the crew as it happens, the gap and what jumped it, and the parting 90 s later names
/// the same cause, as the report does.
/// </summary>
public class LooseToldTests
{
    static readonly SightTuning S = DataFile.Load<SightTuning>(Path.Combine(DataFile.FindContentRoot(), SightTuning.File));
    static readonly LooseTuning L = DataFile.Load<UpkeepTuning>(Path.Combine(DataFile.FindContentRoot(), UpkeepTuning.File)).Coupling;

    static World Straight(double speed)
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(60_000)]));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), line, 5_000);
        train.Dynamics.Velocity = speed;
        var w = new World(train, Tuning.Combat) { NewsTuning = Tuning.Player.News };
        w.EnableBodies();
        w.Upkeep = new UpkeepTuning { HotBox = new() { Enabled = false }, Lamp = new() { Enabled = false }, Coupling = L };
        return w;
    }

    static void Tick(World w, TrainControls c)
    {
        w.BeginTick();
        w.Step(c);
    }

    /// <summary>
    /// Note 1003 (the director's playtest of 23 Oct, line 30: "I can hear the sound of one of the couplings is loose ... I don't
    /// know why"): still knocking at retellAt, the crew are told again what did it, once; and the gap's prompt says it.
    /// </summary>
    [Fact]
    public void APinStillKnockingIsToldAgainWhatLoosenedItOnce()
    {
        var w = Straight(15);
        for (int i = 0; i < 3 * SimConstants.TickRate; i++)
            Tick(w, new TrainControls { Brake = 1, Reverser = 1 });
        var loose = Assert.Single(w.Train.Vehicles, v => v.Loose > 0);
        Assert.Equal("A HARD BRAKE", Couplings.CauseWords(loose.LooseCause));
        for (int i = 0; i < (L.RetellAt + 5) * SimConstants.TickRate; i++)
        {
            w.Train.Dynamics.Velocity = 10;
            Tick(w, new TrainControls { Reverser = 1 });
        }
        var again = Assert.Single(w.NewsLog, e => e.Kind == NewsKind.StillLoose);
        Assert.Equal($"COUPLING STILL LOOSE BEHIND CAR {loose.Id} · A HARD BRAKE AT 54 KM/H", News.Line(w, again));
        Assert.True(again.Tick * SimConstants.TickSeconds - w.NewsLog.Single(e => e.Kind == NewsKind.Loose).Tick * SimConstants.TickSeconds >= L.RetellAt - 4);
    }

    [Fact]
    public void AHardBrakesPinIsToldAsItJumpsAtItsGapAndThePartingSaysTheSame()
    {
        var w = Straight(15);
        for (int i = 0; i < 3 * SimConstants.TickRate; i++)
            Tick(w, new TrainControls { Brake = 1, Reverser = 1 });
        var loose = Assert.Single(w.Train.Vehicles, v => v.Loose > 0);
        var told = Assert.Single(w.NewsLog);
        Assert.Equal(NewsKind.Loose, told.Kind);
        Assert.Equal($"COUPLING LOOSE BEHIND CAR {loose.Id} · A HARD BRAKE AT 54 KM/H", News.Line(w, told));
        // Up top for the crew now, while there's the whole partAfter to tighten it in.
        Assert.Contains(w.News, e => e.Kind == NewsKind.Loose);
        // Left: it parts, and the line says what loosened it.
        for (int i = 0; i < (L.PartAfter + 2) * SimConstants.TickRate && w.Train.TrainRakes == 1; i++)
        {
            w.Train.Dynamics.Velocity = 10;
            Tick(w, new TrainControls { Reverser = 1 });
        }
        Tick(w, new TrainControls { Reverser = 1 });
        var lost = Assert.Single(w.NewsLog, e => e.Kind == NewsKind.CarsLost);
        Assert.Equal($"CARS {loose.Id + 1}-6 LOST · THE PIN THE HARD BRAKE WORKED LOOSE", News.Line(w, lost));
        Assert.Single(w.NewsLog, e => e.Kind == NewsKind.Loose);
    }

    [Fact]
    public void ACouplingUpTooHardIsToldAsTheKnock()
    {
        var w = Straight(0);
        var told = new NewsEvent(1, 0, NewsKind.Loose, [3], (byte)LooseCause.Knock, 0, -1, 1.6);
        Assert.Equal("COUPLING LOOSE BEHIND CAR 3 · COUPLED UP AT 6 KM/H", News.Line(w, told));
        Assert.Equal("CARS 4-5 LOST · THE PIN THE HARD COUPLING WORKED LOOSE",
            News.Line(w, new NewsEvent(2, 0, NewsKind.CarsLost, [4, 5], (byte)Parting.Pin, (byte)LooseCause.Knock, -1, 1.6)));
    }

    [Fact]
    public void ABendsPinIsToldWithTheBoardItWasTakenOver()
    {
        var route = new Route.Route("test", RouteTier.Frontier, 1, new LineDefinition("test", [new TrackSegment(800), new TrackSegment(300, 300), new TrackSegment(3000)]),
            [], new RouteWeather(0.01, false, 0, 0), 3600);
        var board = Lineside.Boards(S, route).Single(s => s.Kind == SignKind.SpeedLimit);
        double speed = board.Limit + S.LurchOver + 0.5;
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), route.Build(), 600);
        var w = new World(train, Tuning.Combat) { NewsTuning = Tuning.Player.News };
        w.EnableBodies();
        w.EnableLineside(S, route);
        w.Upkeep = new UpkeepTuning { HotBox = new() { Enabled = false }, Lamp = new() { Enabled = false }, Coupling = L };
        while (train.Dynamics.RearDistance < 1300 && !w.Derailed)
        {
            train.Dynamics.Velocity = speed;
            Tick(w, new TrainControls { Reverser = 1 });
        }
        var loose = Assert.Single(train.Vehicles, v => v.Loose > 0);
        var told = Assert.Single(w.NewsLog, e => e.Kind == NewsKind.Loose);
        Assert.Equal($"COUPLING LOOSE BEHIND CAR {loose.Id} · A {board.LimitKmh} KM/H BEND TAKEN AT {speed * 3.6:0} KM/H", News.Line(w, told));
    }
}
