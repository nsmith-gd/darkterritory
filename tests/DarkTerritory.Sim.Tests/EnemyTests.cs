using Ballast;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>A host-side night with a crew driven by scripted intent.</summary>
sealed class Night
{
    public readonly World World;
    public readonly Dictionary<int, PlayerState> Crew = new();
    public readonly List<EnemyEvent> Events = new();
    public readonly List<GunShot> Shots = new();
    public TrainControls Controls = new() { Reverser = 1 };

    public Night(int cars, double speed, Route.Route? route = null, bool boiler = false, ulong seed = 1)
    {
        var line = route?.Build() ?? new RailLine(new LineDefinition("t", [new TrackSegment(40_000)]));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, cars, 1)), line, route is null ? 2_000 : 400, boiler ? Tuning.Boiler : null);
        train.Dynamics.Velocity = speed;
        World = new World(train, Tuning.Combat);
        World.EnableEnemies(Tuning.Enemies, route, seed, crew: 4, authority: true);
    }

    public TrainOnLine Train => World.Train;

    public void Run(double seconds, Func<int, PlayerIntent>? intent = null, bool holdSpeed = true)
    {
        double speed = Train.Dynamics.Velocity;
        for (int i = 0; i < seconds * SimConstants.TickRate; i++)
        {
            if (holdSpeed && !World.Derailed)
                Train.Dynamics.Velocity = speed;
            World.BeginTick();
            // Intents first: a scripted player may turn to aim before acting.
            var intents = Crew.Keys.ToList().ToDictionary(id => id, id => intent?.Invoke(id) ?? default);
            foreach (var id in Crew.Keys.ToList())
            {
                var s = Crew[id];
                World.CrewAct(ref s, intents[id], id);
                Crew[id] = s;
            }
            Shots.AddRange(World.Shots);
            World.Step(Controls);
            World.ApplyDamage(id => Crew.TryGetValue(id, out var s) ? s : null, (id, s) => Crew[id] = s, Crew.Keys);
            foreach (var id in Crew.Keys.ToList())
            {
                var s = Crew[id];
                PlayerMotor.Step(ref s, intents[id], Train, Tuning.Player, Tuning.Train, SimConstants.TickSeconds, applyLook: false);
                Crew[id] = s;
            }
            Events.AddRange(World.EnemyEvents);
        }
    }

    /// <summary>App. A.1 fairness contract: every commit comes out of a telegraph at least the reaction window long.</summary>
    public void AssertFair()
    {
        foreach (var e in Events.Where(e => e.To == SpinePhase.Commit))
        {
            Assert.Equal(SpinePhase.Telegraph, e.From);
            Assert.True(e.SecondsInFrom >= Tuning.Enemies.MinReactionSeconds - 1e-9, $"{e.Kind} {e.EnemyId} committed after {e.SecondsInFrom:0.00} s");
        }
        Assert.DoesNotContain(Events, e => e.To == SpinePhase.Punish && e.From != SpinePhase.Commit);
    }
}

public class EnemyTests
{
    static readonly EnemyTuning E = Tuning.Enemies;
    static readonly PlayerTuning P = Tuning.Player;

    static Sleepers SleepersAhead(Night n, double metres) =>
        n.World.AddEnemy(id => new Sleepers(id) { LineDistance = n.Train.Dynamics.Distance + metres, Height = 0.2 });

    [Fact]
    public void SleepersAtSpeedDerailTheTrainAndKillEveryone()
    {
        var n = new Night(6, speed: 20);
        n.Crew[1] = PlayerMotor.SpawnInCab(n.Train, P);
        n.Crew[2] = PlayerMotor.SpawnOnRoof(n.Train, 3, 0, P);
        SleepersAhead(n, 400);
        n.Run(25);
        var telegraph = n.Events.First(e => e.To == SpinePhase.Telegraph);
        var commit = n.Events.First(e => e.To == SpinePhase.Commit);
        // The lamp found them 120 m out: six seconds at 20 m/s.
        Assert.InRange((commit.Tick - telegraph.Tick) * SimConstants.TickSeconds, 5.5, 6.5);
        Assert.True(n.World.Derailed);
        Assert.All(n.Crew.Values, s => Assert.Equal(DeathCause.Derailed, s.Death));
        n.AssertFair();
    }

    [Fact]
    public void BrakingWhenTheLampFindsThemStopsShort()
    {
        var n = new Night(6, speed: 10);
        var sleepers = SleepersAhead(n, 300);
        n.Run(19);
        Assert.Equal(SpinePhase.Telegraph, sleepers.Phase);
        n.Controls = new TrainControls { Brake = 1, Reverser = 1 };
        n.Run(20, holdSpeed: false);
        Assert.Equal(0, n.Train.Dynamics.Velocity);
        Assert.False(n.World.Derailed);
        Assert.Equal(SpinePhase.Telegraph, sleepers.Phase);
    }

    [Fact]
    public void RollingOverThemAtWalkingPaceJustCrushesThem()
    {
        var n = new Night(3, speed: 4);
        var sleepers = SleepersAhead(n, 40);
        n.Run(15);
        Assert.True(sleepers.Gone);
        Assert.False(n.World.Derailed);
        Assert.Equal(1 - E.Sleepers.MinorDamage, n.Train.Vehicles[0].Integrity, 6);
    }

    [Fact]
    public void WithTheLampDownYouOnlyHearThemAtSixtyMetres()
    {
        var n = new Night(6, speed: 20);
        n.World.LampLit = false;
        SleepersAhead(n, 300);
        n.Run(20);
        var telegraph = n.Events.First(e => e.To == SpinePhase.Telegraph);
        var commit = n.Events.First(e => e.To == SpinePhase.Commit);
        Assert.InRange((commit.Tick - telegraph.Tick) * SimConstants.TickSeconds, 2.8, 3.3);
        Assert.True(n.World.Derailed);
        n.AssertFair();
    }

    static List<CinderHound> Pack(Night n, int size = 3)
    {
        var list = new List<CinderHound>();
        for (int i = 0; i < size; i++)
            list.Add(n.World.AddEnemy(id => new CinderHound(id, 1)
            {
                LineDistance = n.Train.Dynamics.RearDistance - E.CinderHounds.SpawnBehind - i * 6,
                Lateral = i % 2 == 0 ? 4 : -4,
                Height = 0.6,
                Health = E.CinderHounds.Health,
            }));
        return list;
    }

    [Fact]
    public void HoundsHowlThenRunTheTrainDownAndMaulTheRearGuard()
    {
        var n = new Night(6, speed: 14);
        int guard = n.Train.Dynamics.Consist.Vehicles[^1].Id;
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, guard, 3, P);
        var pack = Pack(n);
        n.Run(E.CinderHounds.HowlSeconds + 0.5);
        Assert.All(pack, h => Assert.Equal(SpinePhase.Commit, h.Phase));
        n.Run(90);
        Assert.Contains(n.Events, e => e.To == SpinePhase.Punish && e.Kind == EnemyKind.CinderHound);
        Assert.Equal(DeathCause.Mauled, n.Crew[1].Death);
        n.AssertFair();
    }

    [Fact]
    public void AtFullSpeedTheTrainOutrunsThem()
    {
        var n = new Night(6, speed: 22);
        var pack = Pack(n);
        n.Run(120);
        Assert.All(pack, h => Assert.True(h.Gone));
        Assert.Contains(n.Events, e => e.To == SpinePhase.BreakOff && e.Kind == EnemyKind.CinderHound);
        Assert.DoesNotContain(n.Events, e => e.To == SpinePhase.Punish);
    }

    /// <summary>A gunner at the guard gun who aims at the nearest running hound, and fires only when told to.</summary>
    static (Night Night, List<CinderHound> Pack, int Guard) GunnerVersusPack(Func<double, bool> fireAtRange)
    {
        var n = new Night(6, speed: 14);
        int guard = n.Train.Dynamics.Consist.Vehicles[^1].Id;
        var mount = n.Train.Frames[guard].Shape.Gun!.Value;
        var gunner = PlayerMotor.SpawnOnRoof(n.Train, guard, mount.Position.Z - 0.7, P);
        gunner.Yaw = Math.PI;
        n.Crew[1] = gunner;
        var pack = Pack(n);
        n.Run(60, id =>
        {
            var target = pack.Where(h => !h.Gone && h.Attached < 0).OrderByDescending(h => h.LineDistance).FirstOrDefault();
            if (target is null)
                return default;
            var frame = n.Train.Frames[guard];
            var offset = target.WorldPosition(n.Train) - frame.ToWorld(mount.Position);
            var d = frame.DirToLocal(offset).Normalized;
            n.Crew[id] = n.Crew[id] with { Yaw = Math.Atan2(-d.X, -d.Z), Pitch = Math.Asin(d.Y) };
            return fireAtRange(offset.Length) ? new PlayerIntent { Buttons = PlayerButtons.Fire } : default;
        });
        return (n, pack, guard);
    }

    [Fact]
    public void AGunnerWhoWaitsForRangeKillsThePackWithoutBringingTheChoir()
    {
        var (n, pack, _) = GunnerVersusPack(range => range <= Tuning.Combat.Guns.Range);
        Assert.All(pack, h => Assert.True(h.Gone, $"hound {h.Id} {h.Phase}"));
        Assert.True(n.Crew[1].Alive);
        Assert.True(n.World.Choir.Aggro > 0);
        Assert.NotEqual(ChoirPhase.Swarm, n.World.Choir.Phase(Tuning.Combat.Choir));
    }

    [Fact]
    public void AGunnerWhoSpraysFromTooFarBringsTheChoirDownOnHimself()
    {
        // GDD §14: "The gunner's job is less about accuracy than restraint." Firing at 180 m, out of range,
        // drives the Choir to a swarm, and the swarm kills the one person standing exposed on the roof.
        var (n, _, _) = GunnerVersusPack(_ => true);
        Assert.Equal(DeathCause.Choir, n.Crew[1].Death);
    }

    [Fact]
    public void SustainedFireInRangeDrivesThePackOffEvenWithoutKills()
    {
        // App. A.3 break off: "sustained rear gun fire". Rounds over their heads still turn them.
        var n = new Night(6, speed: 14);
        int guard = n.Train.Dynamics.Consist.Vehicles[^1].Id;
        var mount = n.Train.Frames[guard].Shape.Gun!.Value;
        var gunner = PlayerMotor.SpawnOnRoof(n.Train, guard, mount.Position.Z - 0.7, P);
        n.Crew[1] = gunner with { Yaw = Math.PI, Pitch = 0.6 };
        var pack = Pack(n);
        var muzzle = () => n.Train.Frames[guard].ToWorld(mount.Position);
        n.Run(60, _ => pack.Any(h => !h.Gone && (h.WorldPosition(n.Train) - muzzle()).Length <= Tuning.Combat.Guns.Range)
            ? new PlayerIntent { Buttons = PlayerButtons.Fire } : default);
        Assert.All(pack, h => Assert.True(h.Gone && h.Health == E.CinderHounds.Health, $"hound {h.Id} {h.Phase} hp {h.Health}"));
        Assert.Equal(pack.Count, n.Events.Count(e => e.Kind == EnemyKind.CinderHound && e.To == SpinePhase.BreakOff));
        Assert.DoesNotContain(n.Events, e => e.To == SpinePhase.Punish);
        n.AssertFair();
    }

    [Fact]
    public void AClingerDrillsThenBreachesAndTheCargoGoes()
    {
        var n = new Night(6, speed: 14);
        var clinger = n.World.AddEnemy(id => new Clinger(id) { Attached = 2, Local = new Double3(1.65, 2, 0) });
        n.Run(E.Clingers.DrillSeconds - 1);
        Assert.Equal(SpinePhase.Telegraph, clinger.Phase);
        Assert.Equal(1, n.Train.Vehicles[2].CargoIntegrity);
        n.Run(21);
        Assert.Equal(SpinePhase.Punish, clinger.Phase);
        Assert.InRange(n.Train.Vehicles[2].CargoIntegrity, 1 - 20 * E.Clingers.BreachCargoLossPerSecond - 0.01, 1 - 19 * E.Clingers.BreachCargoLossPerSecond);
        n.AssertFair();
    }

    [Fact]
    public void GoingOutOnTheRoofAndPryingItOffWorks()
    {
        var n = new Night(6, speed: 14);
        var clinger = n.World.AddEnemy(id => new Clinger(id) { Attached = 2, Local = new Double3(1.65, 2, 3) });
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, 2, 3, P, localX: 0.8);
        n.Run(E.Clingers.PrySeconds + 0.2, _ => new PlayerIntent { Buttons = PlayerButtons.Use });
        Assert.True(clinger.Gone);
        Assert.Contains(n.Events, e => e.To == SpinePhase.BreakOff && e.Kind == EnemyKind.Clinger);
    }

    [Fact]
    public void NeglectTheFireAndTheHollowComesDownTheStack()
    {
        var n = new Night(3, speed: 0, boiler: true);
        n.Train.Boiler.Firebox = 0.3;
        n.Crew[1] = PlayerMotor.SpawnInCab(n.Train, P);
        n.Run(E.Hollow.LowFireSeconds + 2);
        var hollow = Assert.Single(n.World.ActiveEnemies, e => e.Kind == EnemyKind.Hollow);
        n.Run(E.Hollow.DescendSeconds + E.Hollow.BiteEverySeconds + 0.5);
        Assert.Equal(SpinePhase.Punish, hollow.Phase);
        Assert.True(n.Crew[1].Health < P.Health);

        // Fire it back up and it leaves at once.
        for (int i = 0; i < 3; i++)
            n.Train.Boiler.Shovel(Tuning.Boiler);
        n.Run(0.1);
        Assert.True(hollow.Gone);
        n.AssertFair();
    }

    [Fact]
    public void TheSwarmingChoirHurtsOnlyWhoeverIsExposed()
    {
        var n = new Night(6, speed: 10);
        n.Crew[1] = PlayerMotor.SpawnInCab(n.Train, P);
        n.Crew[2] = PlayerMotor.SpawnOnRoof(n.Train, 3, 0, P);
        n.World.Choir.Deafening(Tuning.Combat.Choir);
        n.Run(10.1);
        Assert.Equal(P.Health, n.Crew[1].Health);
        Assert.Equal(P.Health - 5 * E.ChoirSwarm.ExposedDamage, n.Crew[2].Health);
    }

    [Fact]
    public void AShutCarIsCoverFromTheChoirAndAnOpenDoorIsNot()
    {
        var n = new Night(6, speed: 10);
        var inside = new PlayerState { Parent = 3, Position = new Double3(-0.45, Tuning.Train.Geometry.Interior!.FloorHeight, 0), Surface = Surface.Deck, Health = P.Health };
        n.Crew[1] = inside;
        n.Crew[2] = inside with { Parent = 4 };
        n.Train.Vehicles[4].ToggleDoor(0);
        n.World.Choir.Deafening(Tuning.Combat.Choir);
        n.Run(10.1);
        Assert.Equal(P.Health, n.Crew[1].Health);
        Assert.True(n.Crew[2].Health < P.Health);
    }

    [Fact]
    public void TheDirectorKeepsItsPacingRules()
    {
        // App. B.9: grace period, troughs, caps and terminus silence, over whole generated nights.
        foreach (ulong seed in new ulong[] { 1, 2, 3 })
        {
            var route = RouteGenerator.Generate(Tuning.Route, RouteTier.DeadLines, seed);
            var n = new Night(10, speed: 14, route, seed: seed);
            n.Run(route.Length / 14 - 40);
            var d = n.World.Director!;
            Assert.NotEmpty(d.Log);
            // The condition-triggered ones (App. B.5) come whenever their condition holds, grace or no: here, nobody's
            // minding the fire or the cab.
            var spawns = d.Log.Where(l => l.Kind is not (EnemyKind.Hollow or EnemyKind.Deadman)).ToList();
            Assert.All(spawns, l => Assert.True(l.Tick * SimConstants.TickSeconds >= E.Director.GraceSeconds, $"spawn at {l.Tick / 30} s"));
            for (int i = 1; i < spawns.Count; i++)
                Assert.True((spawns[i].Tick - spawns[i - 1].Tick) * SimConstants.TickSeconds >= E.Director.CooldownSeconds[0] - 1);
            Assert.All(d.Log, l => Assert.True(l.ActiveInZone <= E.Director.MaxConcurrentZone && l.ActiveTotal <= E.Director.MaxConcurrentSmallCrew));
            Assert.All(d.Log, l => Assert.True(l.TrainDistance <= route.Length - 500));
            Assert.True(d.Spent <= d.Budget + 1e-9);
            n.AssertFair();
        }
    }
}
