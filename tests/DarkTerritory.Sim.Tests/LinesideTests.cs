using Ballast;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The line's boards (sight.json): posted curves, weak bridges and tunnel mouths, read in the headlamp, and in the dark
/// not at all, only what they warn of when it's almost on you. After the 100-night playtest: running dark has to cost sign
/// sight as well as obstacle sight.
/// </summary>
public class LinesideTests
{
    static readonly TrainTuning T = Tuning.Train;
    static readonly PlayerTuning P = Tuning.Player;
    static readonly SightTuning S = DataFile.Load<SightTuning>(Path.Combine(DataFile.FindContentRoot(), SightTuning.File));

    static Route.Route Line(IReadOnlyList<TrackSegment> segments, params RouteFeature[] features) =>
        new("test", RouteTier.Frontier, 1, new LineDefinition("test", segments), features, new RouteWeather(0.01, false, 0, 0), 3600);

    /// <summary>A straight run in, a curve of this radius, and a long straight after.</summary>
    static Route.Route Curve(double radius) => Line([new TrackSegment(800), new TrackSegment(300, radius), new TrackSegment(3000)]);

    static Route.Route Tunnel() => Line([new TrackSegment(4000)], new RouteFeature(FeatureKind.Tunnel, 1500, 1700));

    [Fact]
    public void BoardsStandBeforeSharpCurvesWeakBridgesAndTunnels()
    {
        foreach (var tier in Enum.GetValues<RouteTier>())
        {
            var route = RouteGenerator.Generate(Tuning.Route, tier, 7);
            var signs = Lineside.Boards(S, route).ToList();
            foreach (var t in route.Of(FeatureKind.Tunnel))
                Assert.Contains(signs, s => s.Kind == SignKind.LowClearance && s.Start == t.Start && s.Board == Math.Max(0, t.Start - S.BoardAhead));
            foreach (var b in route.Of(FeatureKind.Bridge).Where(b => b.MaxCars > 0))
                Assert.Contains(signs, s => s.Kind == SignKind.SpeedLimit && s.Start == b.Start && s.Limit == S.WeakBridgeLimit);
            // Every curve too sharp to take at the posting speed is inside a posted stretch, at no more than its own speed.
            double at = 0;
            foreach (var seg in route.Line.Segments)
            {
                double own = seg.Radius == 0 ? double.MaxValue : Math.Sqrt(S.CurveLateral * Math.Abs(seg.Radius));
                if (own < S.PostBelow)
                    Assert.Contains(signs, s => s.Kind == SignKind.SpeedLimit && s.Start <= at + 1e-6 && s.End >= at + seg.Length - 1e-6 && s.Limit <= own + 1e-9);
                at += seg.Length;
            }
        }
    }

    [Fact]
    public void TheLampReadsABoardFarOffAndTheDarkOnlyWhatItWarnsOf()
    {
        var route = Tunnel();
        var sign = Lineside.Boards(S, route).Single(s => s.Kind == SignKind.LowClearance);
        bool ReadAt(double front, bool lamp)
        {
            var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 3, 1)), route.Build(), front);
            var lineside = new Lineside(S, route);
            lineside.See(train, lamp);
            return lineside.Read(sign.Id);
        }
        Assert.False(ReadAt(sign.Board - S.LampSignRange - 5, lamp: true));
        Assert.True(ReadAt(sign.Board - S.LampSignRange + 5, lamp: true));
        // Lamps down, the board goes by unread: the mouth itself is made out, close.
        Assert.False(ReadAt(sign.Board + 5, lamp: false));
        Assert.False(ReadAt(sign.Start - S.DarkSignRange - 5, lamp: false));
        Assert.True(ReadAt(sign.Start - S.DarkSignRange + 5, lamp: false));
    }

    /// <summary>A train held at a speed through a route, with crew aboard stepped as the host steps them.</summary>
    static (World World, PlayerState[] Crew) Ride(Route.Route route, double speed, double until, Func<TrainOnLine, PlayerState[]> crew, int cars = 5)
    {
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, cars, 1)), route.Build(), 600);
        var world = new World(train, Tuning.Combat);
        world.EnableBodies();
        world.EnableLineside(S, route);
        var states = crew(train);
        while (train.Dynamics.RearDistance < until && !world.Derailed)
        {
            train.Dynamics.Velocity = speed;
            world.BeginTick();
            for (int i = 0; i < states.Length; i++)
                world.CrewAct(ref states[i], default, i + 1);
            world.Step(new TrainControls { Reverser = 1 });
            world.ApplyDamage(id => states[id - 1], (id, s) => states[id - 1] = s, Enumerable.Range(1, states.Length));
            for (int i = 0; i < states.Length; i++)
                PlayerMotor.Step(ref states[i], default, train, P, T, SimConstants.TickSeconds, applyLook: false);
        }
        return (world, states);
    }

    [Fact]
    public void ACurveTakenOverItsBoardThrowsTheRoofRidersAndShakesTheCargo()
    {
        var route = Curve(300);
        double limit = Lineside.Boards(S, route).Single(s => s.Kind == SignKind.SpeedLimit).Limit;
        double Cargo(World w) => w.Train.Vehicles.Where(v => v.Kind == VehicleKind.Cargo).Min(v => v.CargoIntegrity);
        PlayerState[] Rider(TrainOnLine train) => [PlayerMotor.SpawnOnRoof(train, 2, 0, P)];

        // At the board: nothing.
        var (at, crew) = Ride(route, limit - 0.5, 1200, Rider);
        Assert.True(crew[0].Alive);
        Assert.Equal(1, Cargo(at), 9);
        // A little over: the cargo's thrown about, the rider holds on.
        var (lurch, held) = Ride(route, limit + S.LurchOver + 0.5, 1200, Rider);
        Assert.True(held[0].Alive);
        Assert.True(Cargo(lurch) < 1);
        // Well over: whoever's up top goes over the side.
        var (_, thrown) = Ride(route, limit + S.ThrowOver + 0.5, 1200, Rider);
        Assert.Equal(DeathCause.Thrown, thrown[0].Death);
        // Far over: off the rails.
        var (derailed, _) = Ride(route, limit * S.DerailRatio + 0.2, 1200, Rider);
        Assert.True(derailed.Derailed);
    }

    [Fact]
    public void ATunnelMouthTakesWhoeverStandsOnARoof()
    {
        var (world, crew) = Ride(Tunnel(), 12, 1750, train =>
        {
            var guard = train.Dynamics.Consist.Vehicles.Last(v => v.Kind == VehicleKind.Guard);
            var mount = train.Frames[guard.Id].Shape.Gun!.Value;
            var room = train.Frames[3].Shape.Interior!.Value;
            return
            [
                PlayerMotor.SpawnOnRoof(train, 2, 0, P),
                PlayerMotor.SpawnOnRoof(train, guard.Id, mount.Position.Z - mount.Facing.Z * 0.7, P),
                new PlayerState { Parent = 3, Position = new Double3(0, room.Min.Y, 0), Surface = Surface.Deck, Health = P.Health, LineHint = train.Cars[3].FrontDistance },
            ];
        });
        Assert.Equal(DeathCause.Struck, crew[0].Death);
        Assert.True(crew[1].Alive, $"the gunner: {crew[1].Death}");
        Assert.True(crew[2].Alive, $"inside: {crew[2].Death}");
    }

    [Fact]
    public void GreaseTakesTheGrip()
    {
        var route = Line([new TrackSegment(3000)], new RouteFeature(FeatureKind.Grease, 500, 700));
        double GripAt(double front)
        {
            var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 3, 1)), route.Build(), front);
            new Lineside(S, route).See(train, true);
            return train.Traction;
        }
        Assert.Equal(S.GreaseTraction, GripAt(600));
        Assert.Equal(1, GripAt(900));
    }

    /// <summary>The driver bot drives a route from 200 m in at cruise; the fastest any car took the posted stretch.</summary>
    static (double Fastest, double Integrity) Drive(Route.Route route, bool lamp)
    {
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 10, 1)), route.Build(), 300);
        train.Dynamics.Velocity = 12;
        var world = new World(train);
        world.EnableBodies();
        world.EnableLineside(S, route);
        world.LampLit = lamp;
        var sign = world.Lineside!.Signs.Single(s => s.Kind == SignKind.SpeedLimit);
        var bot = new ConductorBot { DarkCruiseSpeed = 10.5 };
        var s = PlayerMotor.SpawnInCab(train, P);
        var controls = new TrainControls { Reverser = 1 };
        double fastest = 0;
        for (uint tick = 0; train.Dynamics.RearDistance < sign.End + 20 && tick < 30 * 400; tick++)
        {
            world.BeginTick();
            var intent = bot.Decide(s, world, tick, out _) with { Lamp = LampSwitch.None };
            Net.CabControls.Apply(ref controls, intent, s, train);
            world.CrewAct(ref s, intent, 1);
            world.Step(controls);
            PlayerMotor.Step(ref s, intent, train, P, T, SimConstants.TickSeconds, applyLook: false);
            if (train.Dynamics.Distance >= sign.Start && train.Dynamics.RearDistance <= sign.End)
                fastest = Math.Max(fastest, train.Dynamics.Speed);
        }
        return (fastest, train.Vehicles.Min(v => v.Integrity));
    }

    [Fact]
    public void TheDriverTakesAPostedCurveAtItsBoardAndRunningDarkCostsIt()
    {
        var route = Curve(150);
        double limit = Lineside.Boards(S, route).Single(s => s.Kind == SignKind.SpeedLimit).Limit;
        var (lit, litSound) = Drive(route, lamp: true);
        Assert.True(lit <= limit + 0.2, $"took it at {lit:0.0} m/s, posted {limit:0.0}");
        Assert.Equal(1, litSound, 9);
        // Lamps down it never read the board: it found the curve under it at its dark cruise, and the train paid.
        var (dark, darkSound) = Drive(route, lamp: false);
        Assert.True(dark > limit + S.LurchOver, $"dark: {dark:0.0} m/s");
        Assert.True(darkSound < 1, $"integrity {darkSound}");
    }

    [Fact]
    public void WalkersGetOffTheRoofsForAPostedTunnel()
    {
        var route = Tunnel();
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 6, 1)), route.Build(), 400);
        var world = new World(train);
        world.EnableBodies();
        world.EnableLineside(S, route);
        var bots = new[] { new RoofWalkerBot(5, P.Cold), new RoofWalkerBot(6, P.Cold) };
        var crew = new[] { PlayerMotor.SpawnOnRoof(train, 2, 0, P), PlayerMotor.SpawnOnRoof(train, 4, 0, P) };
        bool wentIn = false;
        for (uint tick = 0; train.Dynamics.RearDistance < 1720; tick++)
        {
            train.Dynamics.Velocity = 10;
            world.BeginTick();
            var intents = new PlayerIntent[crew.Length];
            for (int i = 0; i < crew.Length; i++)
            {
                intents[i] = bots[i].Decide(crew[i], world, tick, out _);
                world.CrewAct(ref crew[i], intents[i], i + 1);
            }
            world.Step(new TrainControls { Reverser = 1 });
            world.ApplyDamage(id => crew[id - 1], (id, s) => crew[id - 1] = s, [1, 2]);
            for (int i = 0; i < crew.Length; i++)
            {
                PlayerMotor.Step(ref crew[i], intents[i], train, P, T, SimConstants.TickSeconds, applyLook: false);
                wentIn |= crew[i].Surface == Surface.Deck;
            }
        }
        Assert.All(crew, c => Assert.True(c.Alive, $"died of {c.Death}"));
        Assert.True(wentIn);
    }

    [Fact]
    public void AWalkerOnTheLastCarWithTheCarAheadAlightStillGetsInForATunnel()
    {
        // The 100-night rerun: on the last car (no plate behind it) with a fire in the car ahead, the walker's only way in
        // was a troubled car, so it stayed on the roof and the mouth took it. A fire's a chance; the roof isn't.
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 6, 1)), Tunnel().Build(), 400);
        int last = train.Vehicles.Count - 1;
        var self = PlayerMotor.SpawnOnRoof(train, last, 3, P);
        var warm = new WarmUp(P.Cold) { Troubled = car => car == last - 1 };
        Assert.Null(warm.Decide(self, train)); // warm, and nothing coming: stays up
        warm.Shelter = true;
        Assert.NotNull(warm.Decide(self, train));
        Assert.True(warm.Active);
    }
}

/// <summary>
/// The mail cranes (the playtest's rewards) and the night's pace ("a reward or a problem every 30 s at most, ideally 20").
/// </summary>
public class DropAndPacingTests
{
    static readonly TrainTuning T = Tuning.Train;
    static readonly PlayerTuning P = Tuning.Player;
    static readonly SightTuning S = DataFile.Load<SightTuning>(Path.Combine(DataFile.FindContentRoot(), SightTuning.File));

    [Fact]
    public void CranesStandAlongTheLineClearOfItsStructuresEachWithABoard()
    {
        var route = RouteGenerator.Generate(Tuning.Route, RouteTier.Frontier, 7);
        var drops = Lineside.Drops(S, route).ToList();
        Assert.True(drops.Count >= route.Length / S.DropSpacing[1] * 0.6, $"{drops.Count} cranes on {route.Length:0} m");
        for (int i = 1; i < drops.Count; i++)
            Assert.True(drops[i].At - drops[i - 1].At >= S.DropSpacing[0] - 1e-6);
        Assert.All(drops, d => Assert.DoesNotContain(route.Features, f => f.Kind is FeatureKind.Tunnel or FeatureKind.Bridge or FeatureKind.Facility && f.Contains(d.At)));
        var boards = Lineside.Boards(S, route).Where(s => s.Kind == SignKind.Drop).ToList();
        Assert.Equal(drops.Count, boards.Count);
        Assert.Equal(3, Lineside.Boards(S, route).Count(s => s.Kind == SignKind.Terminus));
        // Every machine has the same cranes.
        Assert.Equal(drops, Lineside.Drops(S, route).ToList());
    }

    /// <summary>A train at speed past a crane with someone in car 2's side door on its side, door open or not, hook out or not.</summary>
    static (Lineside Lineside, World World) Past(bool open, bool hook, DropKind kind = DropKind.Mail)
    {
        var route = new Route.Route("t", RouteTier.Frontier, 1, new LineDefinition("t", [new TrackSegment(3000)]), [], new RouteWeather(0.01, false, 0, 0), 3600);
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 4, 0.5)), route.Build(), 600, Tuning.Boiler);
        var world = new World(train, Tuning.Combat);
        world.EnableBodies();
        world.EnableRun(Tuning.Run, route, Tuning.Route.YardLength, authority: true);
        world.EnableLineside(S, route);
        var lineside = world.Lineside!;
        var drop = lineside.AllDrops.First();
        var shape = train.Frames[2].Shape;
        int door = StopHand.SideDoor(shape, drop.Side)!.Value;
        if (open)
            train.Vehicles[2].ToggleDoor(door);
        var (at, yaw) = WarmUp.Inside(shape, door);
        var s = new PlayerState { Parent = 2, Position = at with { Y = T.Geometry.Interior!.FloorHeight }, Yaw = yaw, Surface = Surface.Deck, Health = P.Health, LineHint = train.Cars[2].FrontDistance };
        var intent = hook ? new PlayerIntent { Buttons = PlayerButtons.Fire } : default;
        while (train.Dynamics.RearDistance < drop.At + 5)
        {
            train.Dynamics.Velocity = 13;
            world.BeginTick();
            world.CrewAct(ref s, intent, 1);
            world.Step(new TrainControls { Reverser = 1 });
            PlayerMotor.Step(ref s, intent, train, P, T, SimConstants.TickSeconds, applyLook: false);
        }
        return (lineside, world);
    }

    [Fact]
    public void ABagIsCaughtFromAnOpenSideDoorWithTheHookOut()
    {
        var (caught, world) = Past(open: true, hook: true);
        var drop = caught.AllDrops.First();
        Assert.True(caught.Caught(drop.Id));
        if (drop.Kind == DropKind.Mail)
            Assert.Equal(drop.Amount, world.Run!.Mail);
        // Door shut, or nobody with the hook out: it goes by.
        var (shut, _) = Past(open: false, hook: true);
        Assert.True(shut.Passed(drop.Id) && !shut.Caught(drop.Id));
        var (idle, _) = Past(open: true, hook: false);
        Assert.True(idle.Passed(drop.Id) && !idle.Caught(drop.Id));
    }

    [Fact]
    public void ANightOutOnTheLineIsNeverQuietForMoreThanThirtySeconds()
    {
        var content = DataFile.FindContentRoot();
        var route = RouteGenerator.Generate(Tuning.Route, RouteTier.Local, 3);
        var report = Net.Harness.Run(route.Build(), T, P, new Net.HarnessOptions
        {
            Bots = 3,
            Cars = 4,
            Seconds = 600,
            Seed = 2,
            Link = Ballast.Net.LinkConditions.Perfect,
            StartDistance = 400,
            Combat = Tuning.Combat,
            Enemies = Tuning.Enemies,
            Route = route,
            Run = Tuning.Run,
            YardLength = Tuning.Route.YardLength,
            Sight = S,
            Holdouts = Tuning.Holdouts,
        }, Tuning.Boiler);
        var pace = report.Pacing!;
        Assert.True(pace.LongestQuietSeconds <= 30, $"quiet for {pace.LongestQuietSeconds} s ({string.Join(", ", pace.Kinds.Select(k => $"{k.Key} {k.Value}"))})");
        Assert.True(pace.BeatsPerMinute >= 2, $"{pace.BeatsPerMinute} a minute");
    }
}
