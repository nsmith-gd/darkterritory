using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The night's pace (GDD App. B.1 "Pacing rules", design decision 2026-10, ARCHITECTURE §8 note 266): a grace picked per
/// night from a range, then a pressure that builds with the night, the quiet, the crew's noise and the cargo, eases when the
/// crew is down, and is spent on what the director's weights pick.
/// </summary>
public class PacingTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly DirectorTuning D = Tuning.Enemies.Director;
    static readonly PlayerTuning P = Tuning.Player;

    /// <summary>The director's tuning with a pressure that never spends: what builds it, and nothing taken off.</summary>
    static EnemyTuning Building() => Tuning.Enemies with { Director = D with { Pressure = D.Pressure with { Threshold = 1e9, Max = 1e9, PressAt = 1e9 } } };

    /// <summary>A world on a plain 40 km line, the train <paramref name="at"/> m along, its director thinking only when asked.</summary>
    static World Plain(double at = 2_000, ulong seed = 1)
    {
        var line = new Rail.RailLine(new Rail.LineDefinition("t", [new Rail.TrackSegment(40_000)]));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), line, at);
        var world = new World(train, Tuning.Combat);
        world.EnableEnemies(Building(), null, seed, crew: 4, authority: true);
        return world;
    }

    /// <summary>The director's next <paramref name="seconds"/> decisions, well past its grace, with nothing about.</summary>
    static void Think(World world, int seconds, double elapsed = 1_000)
    {
        for (int i = 0; i < seconds; i++)
            Assert.Null(world.Director!.Decide(world, elapsed + i, [], 500));
    }

    [Fact]
    public void TheGraceIsARangePickedPerNightFromItsSeed()
    {
        var byTier = new Dictionary<RouteTier, List<double>>();
        foreach (var tier in new[] { RouteTier.Local, RouteTier.Frontier, RouteTier.DeadLines, RouteTier.DeepTerritory })
        {
            var route = RouteGenerator.Generate(Tuning.Route, tier, 1);
            var graces = new List<double>();
            for (ulong seed = 1; seed <= 60; seed++)
            {
                double g = new Director(D, route, seed, 6, 4).Grace;
                Assert.InRange(g, D.GraceMinSeconds, D.GraceMaxSeconds);
                // The same night, the same quiet spell, however often it's asked (a host restarting a night, a replay).
                Assert.Equal(g, new Director(D, route, seed, 6, 4).Grace);
                graces.Add(g);
            }
            // It varies night to night...
            Assert.True(graces.Distinct().Count() > 50);
            byTier[tier] = graces;
        }
        Assert.Equal(20, D.GraceMinSeconds);
        Assert.Equal(90, D.GraceMaxSeconds);
        // ...across the range on a Local line, and shorter on the harder tiers.
        Assert.True(byTier[RouteTier.Local].Max() > 80 && byTier[RouteTier.Local].Min() < 30);
        Assert.True(byTier[RouteTier.DeepTerritory].Average() < byTier[RouteTier.Frontier].Average());
        Assert.True(byTier[RouteTier.Frontier].Average() < byTier[RouteTier.Local].Average());
    }

    [Fact]
    public void NothingIsSentBeforeTheGraceIsOver()
    {
        foreach (ulong seed in new ulong[] { 1, 2, 3, 4 })
        {
            var route = RouteGenerator.Generate(Tuning.Route, RouteTier.DeadLines, seed);
            var n = new Night(8, speed: 14, route, seed: seed);
            var d = n.World.Director!;
            n.Run(d.Grace - 1);
            Assert.DoesNotContain(d.Log, l => l.Kind is not (EnemyKind.Stoker or EnemyKind.Drift));
            Assert.Equal("grace", d.HeldBecause);
            Assert.Equal(0, d.Pressure);
            // Then it builds from where it starts, and something comes.
            n.Run(240);
            Assert.Contains(d.Log, l => l.Kind is not (EnemyKind.Stoker or EnemyKind.Drift));
            Assert.All(d.Log.Where(l => l.Kind is not (EnemyKind.Stoker or EnemyKind.Drift)), l => Assert.True(l.Tick * SimConstants.TickSeconds >= d.Grace));
        }
    }

    [Fact]
    public void QuietBuildsThePressureFasterTheLongerItLasts()
    {
        var world = Plain();
        var d = world.Director!;
        Think(world, 1);
        Assert.Equal("building", d.HeldBecause);
        double first = d.Terms.Rate;
        Assert.InRange(d.Pressure, D.Pressure.Start, D.Pressure.Start + 1);
        Think(world, 59, elapsed: 1_001);
        double minute = d.Terms.Rate;
        Assert.True(minute > first * 1.5, $"rate {first:0.000} at the start of the quiet, {minute:0.000} a minute in");
        Assert.Equal(60, d.Terms.Quiet);
        // One only pacing the train (on the caps, not yet at the crew) doesn't stop the quiet...
        var climber = world.AddEnemy(id => new Climber(id));
        Assert.True(Director.Engaged(climber));
        Assert.Null(d.Decide(world, 1_060, [climber], 500));
        Assert.Equal(61, d.Terms.Quiet);
        // ...one coming at the crew does, and slows the rest: the crew's busy.
        climber.Restore(SpinePhase.Telegraph, 0, 3, -1, default, 0, 0, 0, 0, 0);
        Assert.True(Director.Confronting(climber));
        Assert.Null(d.Decide(world, 1_061, [climber], 500));
        Assert.Equal(0, d.Terms.Quiet);
        Assert.True(d.Terms.Rate < first);
    }

    /// <summary>
    /// GDD App. F.1 (the director, 6 Oct 2026; note 270): "quiet stretches are counted in kilometres, not seconds". A train
    /// running fast meets the quiet's full pull sooner than one stood still; the stood one still gets there on the backstop.
    /// </summary>
    [Fact]
    public void QuietIsCountedInLineRunWithTimeTheBackstop()
    {
        Assert.True(D.Pressure.QuietRampMetres > 0);
        var stood = Plain();
        var running = Plain();
        running.Train.Dynamics.Velocity = 20;
        Think(stood, 45);
        Think(running, 45);
        Assert.Equal(stood.Director!.Terms.Quiet, running.Director!.Terms.Quiet);
        Assert.True(running.Director.Pressure > stood.Director.Pressure, $"running {running.Director.Pressure:0.00}, stood {stood.Director.Pressure:0.00}");
        // Past the backstop the stood train's quiet pulls as hard as the running one's: both rates the same.
        Think(stood, (int)D.Pressure.QuietBackstopSeconds, elapsed: 1_045);
        Think(running, (int)D.Pressure.QuietBackstopSeconds, elapsed: 1_045);
        Assert.Equal(running.Director.Terms.Rate, stood.Director.Terms.Rate, 9);
    }

    [Fact]
    public void ANoisyCrewBuildsItFaster()
    {
        var quiet = Plain();
        var loud = Plain();
        loud.Choir.Loudness = Tuning.Combat.Choir.Threshold;
        Think(quiet, 1);
        Think(loud, 1);
        Assert.True(loud.Director!.Terms.Loud > 0);
        Assert.Equal(0, quiet.Director!.Terms.Loud);
        Assert.True(loud.Director.Pressure > quiet.Director.Pressure);
    }

    [Fact]
    public void ItEscalatesTowardTheEndOfTheNight()
    {
        var early = Plain(at: 1_000);
        var late = Plain(at: 19_000);
        Think(early, 1);
        Think(late, 1);
        Assert.True(late.Director!.Terms.Escalation > early.Director!.Terms.Escalation * 2);
        Assert.True(late.Director.Terms.Rate > early.Director.Terms.Rate);
    }

    [Fact]
    public void CargoAboardDrawsThem()
    {
        var full = Plain();
        var empty = Plain();
        foreach (var v in empty.Train.Dynamics.Consist.Vehicles.Where(v => v.Kind == VehicleKind.Cargo))
            v.Load = 0;
        Think(full, 1);
        Think(empty, 1);
        Assert.True(full.Director!.Terms.Cargo > 0);
        Assert.Equal(0, empty.Director!.Terms.Cargo);
        Assert.True(full.Director.Terms.Rate > empty.Director.Terms.Rate);
    }

    [Fact]
    public void SpendingRelievesIt()
    {
        var world = Plain();
        var d = world.Director!;
        Think(world, 90);
        double before = d.Pressure;
        d.Charge(world, EnemyKind.CinderHound, []);
        Assert.Equal(Math.Max(0, before - D.Pressure.ReliefPerCost * d.Cost(EnemyKind.CinderHound)), d.Pressure, 9);
        Assert.True(d.Pressure < before);
    }

    [Fact]
    public void WithTheCrewDownItEases()
    {
        Night Crewed(bool down)
        {
            var n = new Night(6, speed: 0, enemies: Building());
            for (int i = 1; i <= 4; i++)
                n.Crew[i] = PlayerMotor.SpawnOnRoof(n.Train, i, 0, P);
            if (down)
            {
                n.Crew[3] = n.Crew[3] with { Death = DeathCause.Eaten, Health = 0 };
                n.Crew[4] = n.Crew[4] with { Death = DeathCause.Eaten, Health = 0 };
            }
            n.Run(n.World.Director!.Grace + 60);
            return n;
        }
        var whole = Crewed(down: false);
        var halved = Crewed(down: true);
        Assert.Equal(1, whole.World.Director!.Terms.Relief, 9);
        Assert.Equal(DMath.Pow(0.5, D.Pressure.DownPower), halved.World.Director!.Terms.Relief, 9);
        Assert.True(halved.World.Director.Pressure < whole.World.Director.Pressure);
        // And the hurt ease it too, short of dying.
        var hurt = new Night(6, speed: 0, enemies: Building());
        for (int i = 1; i <= 4; i++)
            hurt.Crew[i] = PlayerMotor.SpawnOnRoof(hurt.Train, i, 0, P) with { Health = i <= 2 ? D.Pressure.HurtBelow - 1 : P.Health };
        hurt.Run(hurt.World.Director!.Grace + 2);
        Assert.Equal(1 - D.Pressure.HurtRelief * 0.5, hurt.World.Director.Terms.Relief, 9);
    }

    [Fact]
    public void TheSameNightBuildsTheSamePressure()
    {
        // The director is the host's alone (clients get its spawns in the snapshot and never run it), but the host's night is
        // deterministic: the same seed and the same inputs make the same trace and the same sends.
        List<(double, string?)> Trace(out List<DirectorSpawn> log)
        {
            var route = RouteGenerator.Generate(Tuning.Route, RouteTier.Frontier, 5);
            var n = new Night(8, speed: 14, route, seed: 5);
            var trace = new List<(double, string?)>();
            for (int s = 0; s < 400; s++)
            {
                n.Run(1);
                trace.Add((n.World.Director!.Pressure, n.World.Director.HeldBecause));
            }
            log = n.World.Director!.Log;
            return trace;
        }
        var a = Trace(out var logA);
        var b = Trace(out var logB);
        Assert.Equal(a, b);
        Assert.Equal(logA, logB);
        Assert.Contains(a, x => x.Item1 > 0);
    }

    [Fact]
    public void TheNightRisesTowardItsEnd()
    {
        // The director alone over a 20 km night at 13 m/s, each threat it sends at the crew for 30 s: fewer sends in the first third
        // than in the last, none of the gaps between them a dead zone, and no early pile-up.
        var line = new Rail.RailLine(new Rail.LineDefinition("t", [new Rail.TrackSegment(40_000)]));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 8, 1)), line, 0);
        var world = new World(train, Tuning.Combat);
        world.EnableEnemies(Tuning.Enemies, null, 3, crew: 4, authority: true);
        var d = world.Director!;
        var about = new List<(Enemy Enemy, int Until)>();
        var sent = new List<(int At, double S)>();
        for (int t = 0; t < 20_000 / 13; t++)
        {
            train.Dynamics.Distance = 13.0 * t;
            about.RemoveAll(a => a.Until <= t);
            if (d.Decide(world, t, [.. about.Select(a => a.Enemy)], 500) is { } kind)
            {
                sent.Add((t, train.Dynamics.Distance));
                var threat = new Climber(1000 + t);
                threat.Restore(SpinePhase.Telegraph, 0, 3, -1, default, 0, 0, 0, 0, 0);
                about.Add((threat, t + 30));
            }
        }
        int early = sent.Count(x => x.S < 20_000 / 3.0), late = sent.Count(x => x.S >= 20_000 * 2 / 3.0);
        Assert.True(late > early * 1.4, $"{early} sent in the first third, {late} in the last");
        var gaps = sent.Zip(sent.Skip(1), (a, b) => b.At - a.At).ToList();
        Assert.True(gaps.Max() < 180, $"longest gap {gaps.Max()} s");
        Assert.True(sent[0].At >= d.Grace);
        Assert.All(gaps.Take(3), g => Assert.True(g >= D.CooldownSeconds[0] - 1, $"early gap {g} s"));
    }

    [Fact]
    public void TheLinesGraceBanGivesWayToTheNightsGrace()
    {
        // T74: a generated line's grace stretch holds the director off only as long as the night's own grace, not for however
        // long the crew takes over its two kilometres.
        var route = Routes.Generate(Content, "frontier:7", 6);
        double at = route.Plan!.GateM + 300;
        Assert.Contains("grace", route.Plan.Director.TagsAt(at));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), route.Build(), at);
        train.Dynamics.Velocity = 12;
        var world = new World(train, Tuning.Combat);
        world.EnableRun(Tuning.Run, route, route.GateOr(0), authority: true);
        world.EnableEnemies(Tuning.Enemies, route, 1, crew: 4, authority: true);
        var director = world.Director!;
        double grace = Math.Max(Tuning.Enemies.Director.LineGraceSeconds, director.Grace);

        // Just out of the gate: the stretch's ban holds.
        world.Run!.Resume(grace / 2, -1, train.Boiler.Tender, 0);
        Assert.Null(director.Decide(world, 100, [], 500));
        Assert.Equal("banned (grace)", director.HeldBecause);
        // The grace over: the stretch is still easy going, but it's no longer the director's to sit out.
        world.Run.Resume(grace + 10, -1, train.Boiler.Tender, 0);
        director.Decide(world, 101, [], 500);
        Assert.NotEqual("banned (grace)", director.HeldBecause);
    }
}
