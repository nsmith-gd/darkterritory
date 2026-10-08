using Ballast;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// A lamp guttering (note 346; orchestrator.md §5.1 U3; GDD App. F.3, the director, 7 Oct 2026: "on the train, still
/// relatively boring from point A to point B"): a lit car's lamp flickers as the train runs; the lamp key in the car trims
/// it; left alone it goes out.
/// </summary>
public class GutterTests
{
    static readonly TrainTuning T = Tuning.Train;
    static readonly GutterTuning G = DataFile.Load<UpkeepTuning>(Path.Combine(DataFile.FindContentRoot(), UpkeepTuning.File)).Lamp;
    static readonly TrainControls Forward = new() { Throttle = 1, Reverser = 1 };

    static World Night(double speed, int cars = 6)
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(60_000)]));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, cars, 1)), line, 5_000);
        train.Dynamics.Velocity = speed;
        var world = new World(train, Tuning.Combat);
        world.EnableBodies();
        world.Upkeep = new UpkeepTuning { HotBox = new() { Enabled = false }, Lamp = G };
        return world;
    }

    static PlayerState Inside(TrainOnLine train, int car) => new()
    {
        Parent = car,
        Position = new Double3(T.Geometry.Interior!.DoorX, T.Geometry.Interior.FloorHeight, 0),
        Surface = Surface.Deck,
        Health = Tuning.Player.Health,
        LineHint = train.Cars[car].FrontDistance,
    };

    static readonly PlayerIntent Lamp = new() { Actions = PlayerActions.CarLamp };

    [Fact]
    public void RunningComesToAGutteringLampOneAtATime()
    {
        var w = Night(15);
        double start = w.Train.Dynamics.Distance;
        for (int i = 0; i < 1200 * SimConstants.TickRate && !w.Train.Vehicles.Any(v => v.Gutter > 0); i++)
            w.Step(Forward);
        var guttering = Assert.Single(w.Train.Vehicles, v => v.Gutter > 0);
        Assert.True(guttering.LampLit && !guttering.IsEngine);
        Assert.InRange(w.Train.Dynamics.Distance - start, G.FirstAfterMetres + G.EveryMetres * (1 - G.Jitter) - 50,
            G.FirstAfterMetres + G.EveryMetres * (1 + G.Jitter) + 50);
    }

    [Fact]
    public void LeftAloneItGoesOut()
    {
        var w = Night(12);
        w.Train.Vehicles[2].Gutter = G.OutAfter - 1;
        for (int i = 0; i < 2 * SimConstants.TickRate; i++)
            w.Step(Forward);
        Assert.False(w.Train.Vehicles[2].LampLit);
        Assert.Equal(0, w.Train.Vehicles[2].Gutter);
        Assert.Equal(1, w.GutterCount.WentOut);
    }

    [Fact]
    public void TheLampKeyTrimsItAndThenPutsItOutAsEver()
    {
        var w = Night(12);
        w.Train.Vehicles[2].Gutter = 20;
        var s = Inside(w.Train, 2);
        Assert.True(PlayerMotor.Indoors(s, w.Train));
        w.CrewAct(ref s, Lamp, 1);
        Assert.True(w.Train.Vehicles[2].LampLit);
        Assert.Equal(0, w.Train.Vehicles[2].Gutter);
        // Let go, and pressed again on a lamp burning steady: it's the switch, as ever.
        w.CrewAct(ref s, default, 1);
        w.CrewAct(ref s, Lamp, 1);
        Assert.False(w.Train.Vehicles[2].LampLit);
    }

    [Fact]
    public void ABotInTheCarTrimsItWithAPressEveryHalfSecond()
    {
        var w = Night(12);
        w.Train.Vehicles[2].Gutter = 20;
        var s = Inside(w.Train, 2);
        int presses = 0;
        for (uint t = 0; t < SimConstants.TickRate; t++)
            if (Heed.Gutter(default, s, w, t).Has(PlayerActions.CarLamp))
                presses++;
        Assert.Equal(2, presses);
        // Burning steady, it's left alone.
        w.Train.Vehicles[2].Gutter = 0;
        Assert.False(Heed.Gutter(default, s, w, 0).Has(PlayerActions.CarLamp));
    }
}
