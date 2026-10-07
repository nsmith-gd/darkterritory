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

    /// <summary>
    /// A scripted ride (note 260): a train driven by <paramref name="speedAt"/> (m/s, by the second) over a route, with riders
    /// stood on every car's roof who never get down, lamps lit or not. Every tick it watches the roof warning as a player's
    /// HUD would (<see cref="Lineside.Warning"/>), and each roof death, when it came, and how long the warning had been up.
    /// </summary>
    static (List<(DeathCause Cause, double Warned)> Deaths, int Spared, double FirstWarning) Watch(Route.Route route, Func<double, double> speedAt, bool lamp,
        double until, int cars = 5)
    {
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, cars, 1)), route.Build(), 600);
        var world = new World(train, Tuning.Combat) { LampLit = lamp };
        world.EnableBodies();
        world.EnableLineside(S, route);
        var states = Enumerable.Range(1, cars - 1).Select(car => PlayerMotor.SpawnOnRoof(train, car, 0, P)).ToArray();
        var deaths = new List<(DeathCause, double)>();
        double up = -1, first = double.NaN;
        for (int tick = 0; train.Dynamics.RearDistance < until && !world.Derailed && tick < 30 * 600; tick++)
        {
            double now = tick * SimConstants.TickSeconds;
            train.Dynamics.Velocity = speedAt(now);
            world.BeginTick();
            for (int i = 0; i < states.Length; i++)
                world.CrewAct(ref states[i], default, i + 1);
            world.Step(new TrainControls { Reverser = 1 });
            // The warning as this tick's HUD has it: up since when.
            bool warned = world.Lineside!.Warning(train) is not null;
            up = warned ? up < 0 ? now : up : -1;
            if (warned && double.IsNaN(first))
                first = now;
            // The host's commits this tick, before they're applied: the mouth's blow, the throw.
            foreach (var d in world.Damage)
                if (d.Cause is DeathCause.Struck or DeathCause.Thrown)
                    deaths.Add((d.Cause, up < 0 ? -1 : now - up));
            world.ApplyDamage(id => states[id - 1], (id, s) => states[id - 1] = s, Enumerable.Range(1, states.Length));
            for (int i = 0; i < states.Length; i++)
                PlayerMotor.Step(ref states[i], default, train, P, T, SimConstants.TickSeconds, applyLook: false);
        }
        return (deaths, world.Lineside!.Spared, first);
    }

    [Fact]
    public void EveryRoofDeathOnTheLineIsWarnedAFullLeadAhead()
    {
        // GDD App. A.1: "TELEGRAPH always precedes COMMIT." The line's own kills (a tunnel's mouth, a bend too fast) come
        // after a warning up for leadSeconds at least, at any speed, lamp lit or not, and with the train gathering speed.
        double lead = S.RoofWarning.LeadSeconds;
        var bend = Curve(300);
        double limit = Lineside.Boards(S, bend).Single(s => s.Kind == SignKind.SpeedLimit).Limit;
        var runs = new (string Name, Route.Route Route, Func<double, double> Speed, bool Lamp, double Until, DeathCause Cause)[]
        {
            ("tunnel at 12 m/s", Tunnel(), _ => 12, true, 1750, DeathCause.Struck),
            ("tunnel at 22 m/s, dark", Tunnel(), _ => 22, false, 1750, DeathCause.Struck),
            ("tunnel, gathering speed", Tunnel(), t => Math.Min(22, 4 + 0.6 * t), true, 1750, DeathCause.Struck),
            ("bend well over", bend, _ => limit + S.ThrowOver + 0.5, true, 1200, DeathCause.Thrown),
            ("bend, dark", bend, _ => limit + S.ThrowOver + 0.5, false, 1200, DeathCause.Thrown),
            // Under the board to 10 m short of it, then over in a moment: the warning comes late, so the throw waits for it.
            ("bend, a surge at the board", bend, t => 600 + limit * t < 790 ? limit - 1 : limit + S.ThrowOver + 0.5, true, 1200, DeathCause.Thrown),
        };
        foreach (var (name, route, speed, lamp, until, cause) in runs)
        {
            var (deaths, _, first) = Watch(route, speed, lamp, until);
            Assert.True(deaths.Count > 0, $"{name}: nobody up top was taken (warned at {first:0.0} s)");
            Assert.All(deaths, d => Assert.Equal(cause, d.Cause));
            Assert.All(deaths, d => Assert.True(d.Warned >= lead - 1e-6, $"{name}: {d.Cause} with the warning up {d.Warned:0.00} s, under {lead} s"));
        }
    }

    [Fact]
    public void ABendTakenAtItsBoardIsNoWarningAndATunnelAlwaysIs()
    {
        var bend = Curve(300);
        double limit = Lineside.Boards(S, bend).Single(s => s.Kind == SignKind.SpeedLimit).Limit;
        var (none, _, first) = Watch(bend, _ => limit, true, 1200);
        Assert.Empty(none);
        Assert.True(double.IsNaN(first), $"warned at {first:0.0} s for a bend at its board");
        // A tunnel's mouth is warned of at a crawl too.
        var (_, _, crawl) = Watch(Tunnel(), _ => 4, true, 1460);
        Assert.False(double.IsNaN(crawl));
    }

    [Fact]
    public void TheWarningIsForWhoeverIsUpTop()
    {
        var route = Tunnel();
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 5, 1)), route.Build(), 1450);
        var world = new World(train, Tuning.Combat);
        world.EnableLineside(S, route);
        var lineside = world.Lineside!;
        var warning = lineside.Warning(train);
        Assert.Equal(SignKind.LowClearance, warning?.Kind);
        var room = train.Frames[3].Shape.Interior!.Value;
        Assert.True(lineside.For(PlayerMotor.SpawnOnRoof(train, 2, 0, P), world));
        Assert.False(lineside.For(PlayerMotor.SpawnInCab(train, P), world));
        Assert.False(lineside.For(new PlayerState { Parent = 3, Position = new Double3(0, room.Min.Y, 0), Surface = Surface.Deck, Health = P.Health }, world));
        // roofOnly off: the cab's told too.
        var everyone = new Lineside(S with { RoofWarning = S.RoofWarning with { RoofOnly = false } }, route);
        Assert.True(everyone.For(PlayerMotor.SpawnInCab(train, P), world));
    }

    [Fact]
    public void ACrouchClearsAMouthOnlyIfTunedTo()
    {
        // Note 260: the spec doesn't say; the kept reading is that it doesn't (a tuning flag says otherwise).
        PlayerState[] Crouched(TrainOnLine train) => [PlayerMotor.SpawnOnRoof(train, 2, 0, P) with { Head = 0.8 }];
        var (_, kept) = Ride(Tunnel(), 12, 1750, Crouched);
        Assert.Equal(DeathCause.Struck, kept[0].Death);
        var flagged = S with { RoofWarning = S.RoofWarning with { CrouchClearsMouth = true } };
        var route = Tunnel();
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 5, 1)), route.Build(), 600);
        var world = new World(train, Tuning.Combat);
        world.EnableBodies();
        world.EnableLineside(flagged, route);
        world.Hand = P.Hand;
        var s = Crouched(train)[0];
        // A headset rider down on their haunches: the head's height comes with a reported hand (T82).
        var crouch = new PlayerIntent { Buttons = PlayerButtons.Hand, HandX = 0.3f, HandY = 0.6f, HandZ = -0.3f, Head = 0.8f };
        while (train.Dynamics.RearDistance < 1750)
        {
            train.Dynamics.Velocity = 12;
            world.BeginTick();
            world.CrewAct(ref s, crouch, 1);
            world.Step(new TrainControls { Reverser = 1 });
            world.ApplyDamage(_ => s, (_, v) => s = v, [1]);
            PlayerMotor.Step(ref s, crouch, train, P, T, SimConstants.TickSeconds, applyLook: false);
        }
        Assert.True(s.Alive, $"crouched under the mouth: {s.Death}");
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

    /// <summary>Two roof-walker bots up on cars 2 and 4 over a route at a speed: how they ended, and whether either went in.</summary>
    static (PlayerState[] Crew, bool WentIn, double Slowest) Walk(Route.Route route, double speed, double until, bool lamp = true)
    {
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 6, 1)), route.Build(), 400);
        var world = new World(train) { LampLit = lamp };
        world.EnableBodies();
        world.EnableLineside(S, route);
        var bots = new[] { new RoofWalkerBot(5, P.Cold), new RoofWalkerBot(6, P.Cold) };
        var crew = new[] { PlayerMotor.SpawnOnRoof(train, 2, 0, P), PlayerMotor.SpawnOnRoof(train, 4, 0, P) };
        bool wentIn = false;
        // How long after the warning went up the last of them was off the roofs (s).
        double up = -1, slowest = 0;
        for (uint tick = 0; train.Dynamics.RearDistance < until && !world.Derailed; tick++)
        {
            double now = tick * SimConstants.TickSeconds;
            train.Dynamics.Velocity = speed;
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
            bool warned = RoofWalkerBot.RoofWarned(world);
            up = warned ? up < 0 ? now : up : -1;
            if (up >= 0 && crew.Any(c => c.Alive && c.Surface == Surface.Roof))
                slowest = Math.Max(slowest, now - up);
        }
        return (crew, wentIn, slowest);
    }

    [Fact]
    public void WalkersGetOffTheRoofsOnTheRoofWarning()
    {
        // Note 260: the bots get down on the warning a player gets (RoofWalkerBot.RoofWarned, the same Lineside.Warning), and
        // in its lead: for a tunnel, lit or dark (it was the board read, so lamps down they learned of the mouth 10 m short),
        // and for a bend the train's taking too fast.
        var bend = Curve(300);
        double limit = Lineside.Boards(S, bend).Single(s => s.Kind == SignKind.SpeedLimit).Limit;
        foreach (var (name, route, speed, lamp, until) in new[]
        {
            ("tunnel", Tunnel(), 10.0, true, 1720.0),
            ("tunnel, dark", Tunnel(), 10.0, false, 1720.0),
            ("tunnel, fast", Tunnel(), 20.0, true, 1720.0),
            ("bend too fast", bend, limit + S.ThrowOver + 0.5, true, 1200.0),
        })
        {
            var (crew, wentIn, slowest) = Walk(route, speed, until, lamp);
            Assert.All(crew, c => Assert.True(c.Alive, $"{name}: died of {c.Death}"));
            Assert.True(wentIn, name);
            Assert.True(slowest < S.RoofWarning.LeadSeconds, $"{name}: still up top {slowest:0.0} s into the warning");
        }
    }

    [Fact]
    public void AWalkerDownOnAPlateForAPostedTunnelStaysOffTheRoofs()
    {
        // T81 (deepTerritory:2): three walkers turned away from a car a Climber had climbed straight back up the ladder from
        // the plate, into the mouth. Off the roofs already, they stay off them till it's by.
        var route = Tunnel();
        // (Note 260: 120 m short of the mouth at 10 m/s, inside the roof warning's lead from the start.)
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 6, 1)), route.Build(), 1380);
        var world = new World(train);
        world.EnableBodies();
        world.EnableLineside(S, route);
        var bot = new RoofWalkerBot(5, P.Cold);
        var s = PlayerMotor.SpawnOnRoof(train, 3, 0, P);
        s.Position = Enemies.CrewSense.GapLocal(train, 3) with { Y = 0.9 };
        s.Surface = Surface.Coupler;
        bool roofed = false, sawItPosted = false;
        for (uint tick = 0; train.Dynamics.RearDistance < 1720; tick++)
        {
            train.Dynamics.Velocity = 10;
            world.BeginTick();
            var intent = bot.Decide(s, world, tick, out _);
            world.CrewAct(ref s, intent, 1);
            world.Step(new TrainControls { Reverser = 1 });
            world.ApplyDamage(_ => s, (_, v) => s = v, [1]);
            PlayerMotor.Step(ref s, intent, train, P, T, SimConstants.TickSeconds, applyLook: false);
            roofed |= s.Surface == Surface.Roof && RoofWalkerBot.RoofWarned(world);
            sawItPosted |= tick < 30 && RoofWalkerBot.RoofWarned(world);
        }
        Assert.True(s.Alive, $"died of {s.Death}");
        Assert.False(roofed, "up on a roof with the tunnel's mouth near");
        Assert.True(sawItPosted, "the tunnel was posted from the plate");
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
    public void ANightOutOnTheLineIsNeverQuietForLongerThanTheLongestQuietSpell()
    {
        // The director's decision of 6 Oct 2026 (note 266): a quiet spell runs 20–90 s, not the 100-night playtest's 30 s at
        // most; something still happens a couple of times a minute.
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
        Assert.True(pace.LongestQuietSeconds <= Tuning.Enemies.Director.GraceMaxSeconds, $"quiet for {pace.LongestQuietSeconds} s ({string.Join(", ", pace.Kinds.Select(k => $"{k.Key} {k.Value}"))})");
        Assert.True(pace.BeatsPerMinute >= 2, $"{pace.BeatsPerMinute} a minute");
    }
}
