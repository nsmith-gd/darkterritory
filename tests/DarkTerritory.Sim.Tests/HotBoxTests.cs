using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The hot box (note 331; orchestrator.md §5.1 U1; GDD App. F.3, the director, 7 Oct 2026: "on the train, still relatively
/// boring from point A to point B"): an axle box running dry as the train runs, greased from the gap behind its car or the
/// ground beside it; left alone it drags, then the car's alight.
/// </summary>
public class HotBoxTests
{
    static readonly TrainTuning T = Tuning.Train;
    static readonly HotBoxTuning H = DataFile.Load<UpkeepTuning>(Path.Combine(DataFile.FindContentRoot(), UpkeepTuning.File)).HotBox;
    const double Dt = SimConstants.TickSeconds;
    static readonly TrainControls Forward = new() { Throttle = 1, Reverser = 1 };

    static World Night(double speed, int cars = 6, bool enemies = false)
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(60_000)]));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, cars, 1)), line, 5_000);
        train.Dynamics.Velocity = speed;
        var world = new World(train, Tuning.Combat);
        if (enemies)
            world.EnableEnemies(Tuning.Enemies with { Director = Tuning.Enemies.Director with { GraceMinSeconds = 1e9, GraceMaxSeconds = 1e9 } },
                route: null, 1, crew: 1, authority: true);
        else
            world.EnableBodies();
        world.Upkeep = new UpkeepTuning { HotBox = H };
        return world;
    }

    [Fact]
    public void RunningComesToAHotBoxOneAtATime()
    {
        var w = Night(15);
        double start = w.Train.Dynamics.Distance;
        for (int i = 0; i < 1200 * SimConstants.TickRate && !w.Train.Vehicles.Any(v => v.HotBox > 0); i++)
            w.Step(Forward);
        var hot = w.Train.Vehicles.Where(v => v.HotBox > 0).ToList();
        Assert.Single(hot);
        Assert.False(hot[0].IsEngine);
        double ran = w.Train.Dynamics.Distance - start;
        Assert.InRange(ran, H.FirstAfterMetres + H.EveryMetres * (1 - H.Jitter) - 50, H.FirstAfterMetres + H.EveryMetres * (1 + H.Jitter) + 50);
        // With nobody aboard to answer it, it's the only one open (one per crewmate, at least one).
        for (int i = 0; i < 30 * SimConstants.TickRate; i++)
            w.Step(Forward);
        Assert.Single(w.Train.Vehicles, v => v.HotBox > 0);
    }

    [Fact]
    public void LeftAloneItDragsTheTrainOffItsTopSpeed()
    {
        var w = Night(T.MaxSpeed);
        w.Train.Vehicles[3].HotBox = H.DragAfter;
        for (int i = 0; i < 40 * SimConstants.TickRate; i++)
            w.Step(Forward);
        Assert.InRange(w.Train.Dynamics.Speed, T.MaxSpeed - H.SlowBy - 0.5, T.MaxSpeed - H.SlowBy + 0.5);
    }

    [Fact]
    public void LeftTooLongTheCarCatches()
    {
        var w = Night(10, enemies: true);
        w.Train.Vehicles[2].HotBox = H.FireAfter - 1;
        for (int i = 0; i < 2 * SimConstants.TickRate; i++)
            w.Step(Forward);
        Assert.Contains(w.ActiveEnemies, e => e is CarFire { Attached: 2 });
        Assert.Equal(0, w.Train.Vehicles[2].HotBox);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GreasedFromTheGapBehindOrTheGroundBesideItsCool(bool fromTheGap)
    {
        var w = Night(fromTheGap ? 12 : 0);
        var train = w.Train;
        train.Vehicles[2].HotBox = 30;
        var p = fromTheGap
            ? new PlayerState { Parent = 2, Surface = Surface.Coupler, Position = new Double3(T.Geometry.PlateX, T.Geometry.CouplerHeight, T.Geometry.CarLength / 2 + 0.7), Health = 100 }
            : new PlayerState
            {
                Parent = PlayerState.World,
                Surface = Surface.Ground,
                Position = train.Frames[2].ToWorld(new Double3(train.Frames[2].Shape.HalfWidth + 0.6, -0.2, train.Frames[2].Shape.HalfLength - H.BogieInset)),
                Health = 100,
            };
        Assert.Equal(2, HotBoxes.Within(p, train, H));
        var use = new PlayerIntent { Buttons = PlayerButtons.Use };
        for (int i = 0; i < (H.GreaseSeconds - 0.5) * SimConstants.TickRate; i++)
            CrewActions.Apply(ref p, use, train, Dt);
        Assert.True(train.Vehicles[2].HotBox > 0, "greased before its time");
        for (int i = 0; i < SimConstants.TickRate; i++)
            CrewActions.Apply(ref p, use, train, Dt);
        Assert.Equal(0, train.Vehicles[2].HotBox);
    }

    [Fact]
    public void NotFromTheRoof()
    {
        var w = Night(12);
        w.Train.Vehicles[2].HotBox = 30;
        var p = PlayerMotor.SpawnOnRoof(w.Train, 2, T.Geometry.CarLength / 2 - 1.5, Tuning.Player);
        Assert.Null(HotBoxes.Within(p, w.Train, H));
    }

    [Fact]
    public void AWalkerGoesDownIntoTheGapAndGreasesAHotBox()
    {
        // A crew of three out on the line (the driver, the gunner, one walker), and car 4's box comes on 20 s in: the walker
        // goes along the roofs to it, down the end ladder into the gap behind car 4, and greases it before it drags.
        double? cameAt = null, greasedAt = null;
        var report = CrewOfTwoTests.Night("frontier:7", 6, 90, null, start: 2500, bots: 3,
            upkeep: new UpkeepTuning { HotBox = H with { FirstAfterMetres = 1e9 } },
            each: w =>
            {
                if (cameAt is null && w.ElapsedSeconds >= 20)
                {
                    w.Train.Vehicles[4].HotBox = Dt;
                    cameAt = w.ElapsedSeconds;
                }
                else if (cameAt is not null && greasedAt is null && w.Train.Vehicles[4].HotBox == 0)
                    greasedAt = w.ElapsedSeconds;
            });
        Assert.NotNull(cameAt);
        Assert.True(greasedAt is { } at && at - cameAt < H.DragAfter, $"came at {cameAt:0} s, greased at {greasedAt?.ToString("0") ?? "never"} ({report.Deaths} died)");
    }
}
