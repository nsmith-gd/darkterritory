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

    /// <summary>
    /// Note 627 (seed 4's night, in note 626's sweep): car 3's lamp guttering, its walkers on their way in, and the driver stood
    /// for a Knotter: every hand went down to the ballast for it and the lamp went out, a car dark for the Climbers. One hand
    /// is the lamp's (claimed among the walkers, <c>CrewCalls.ClaimGutter</c>): it leaves the ground's errands to the others
    /// and trims the lamp; the rest kill the Knotter.
    /// </summary>
    [Fact]
    public void AtAKnottersStandOneHandStillTrimsTheGutteringLamp()
    {
        var quiet = Tuning.Enemies with { Director = Tuning.Enemies.Director with { GraceMinSeconds = 1e9, GraceMaxSeconds = 1e9 } };
        double? cameAt = null, trimmedAt = null;
        bool wentOut = false, stood = false;
        DarkTerritory.Sim.Enemies.Knotter? knot = null;
        var report = CrewOfTwoTests.Night(CrewOfTwoTests.OpenLine.Spec, 6, 150, null, start: CrewOfTwoTests.OpenLine.Start, bots: 4, enemies: quiet,
            upkeep: new UpkeepTuning { HotBox = new() { Enabled = false }, Lamp = G with { FirstAfterMetres = 1e9 }, Coupling = new() { Enabled = false } },
            each: w =>
            {
                var lamp = w.Train.Vehicles[3];
                if (cameAt is null && w.ElapsedSeconds >= 30 && lamp.LampLit && w.Train.Dynamics.Speed > 8)
                {
                    knot = w.AddEnemy(id => DarkTerritory.Sim.Enemies.Knotter.Into(id, w.Train, 1, quiet.Knotter));
                    lamp.Gutter = SimConstants.TickSeconds;
                    cameAt = w.ElapsedSeconds;
                }
                else if (cameAt is not null && trimmedAt is null)
                {
                    stood |= Math.Abs(w.Train.Dynamics.Velocity) < 0.05;
                    if (lamp.Gutter == 0)
                    {
                        wentOut |= !lamp.LampLit;
                        trimmedAt = w.ElapsedSeconds;
                    }
                }
            });
        Assert.NotNull(cameAt);
        Assert.True(stood, "the driver never stood for the Knotter while the lamp guttered");
        Assert.True(trimmedAt is { } at && at - cameAt < G.OutAfter && !wentOut,
            $"came at {cameAt:0} s, done at {trimmedAt?.ToString("0") ?? "never"}, out {wentOut}; the Knotter {knot?.Mode}, gone {knot?.Gone} ({report.Deaths} died)");
    }

    [Fact]
    public void AWalkerGoesInToAGutteringLampNobodysWithAndTrimsIt()
    {
        // A crew of three out on the line (the driver, the gunner, one walker), and car 3's lamp starts to gutter 20 s in:
        // nobody's in there, so the walker goes along the roofs, in at its door, and trims it before it goes out.
        double? cameAt = null, trimmedAt = null;
        bool wentOut = false;
        var report = CrewOfTwoTests.Night(CrewOfTwoTests.OpenLine.Spec, 6, 100, null, start: CrewOfTwoTests.OpenLine.Start, bots: 3,
            upkeep: new UpkeepTuning { HotBox = new() { Enabled = false }, Lamp = G with { FirstAfterMetres = 1e9 }, Coupling = new() { Enabled = false } },
            each: w =>
            {
                var lamp = w.Train.Vehicles[3];
                if (cameAt is null && w.ElapsedSeconds >= 20 && lamp.LampLit)
                {
                    lamp.Gutter = SimConstants.TickSeconds;
                    cameAt = w.ElapsedSeconds;
                }
                else if (cameAt is not null && trimmedAt is null && lamp.Gutter == 0)
                {
                    wentOut |= !lamp.LampLit;
                    trimmedAt = w.ElapsedSeconds;
                }
            });
        Assert.NotNull(cameAt);
        Assert.True(trimmedAt is { } at && at - cameAt < G.OutAfter && !wentOut,
            $"came at {cameAt:0} s, done at {trimmedAt?.ToString("0") ?? "never"}, out {wentOut} ({report.Deaths} died)");
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
