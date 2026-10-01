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
        // GDD v1.1 App. A.1: a grab comes only out of a commit, and a kill only out of a grab (or a commit, for what
        // punishes the train rather than a player).
        Assert.DoesNotContain(Events, e => e.To == SpinePhase.Grab && e.From != SpinePhase.Commit);
        Assert.DoesNotContain(Events, e => e.To == SpinePhase.Punish && e.From is not (SpinePhase.Commit or SpinePhase.Grab));
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
        // (The director's own spawns, in 20 s of grace now, may punish; the pack never does.)
        Assert.DoesNotContain(n.Events, e => e.To == SpinePhase.Punish && e.Kind == EnemyKind.CinderHound);
    }

    /// <summary>A gunner at the guard gun who aims at the nearest running hound, and fires only when told to.</summary>
    static (Night Night, List<CinderHound> Pack, int Guard) GunnerVersusPack(Func<double, bool> fireAtRange)
    {
        var n = new Night(6, speed: 14);
        int guard = n.Train.Dynamics.Consist.Vehicles[^1].Id;
        var mount = n.Train.Frames[guard].Shape.Gun!.Value;
        var gunner = PlayerMotor.SpawnOnRoof(n.Train, guard, mount.Position.Z - 0.7, P);
        gunner.Yaw = Math.PI;
        gunner.Flags |= PlayerFlags.Seated; // T112: in the gun's seat
        n.Crew[1] = gunner;
        var pack = Pack(n);
        n.Run(90, id =>
        {
            // Held, they do nothing (a Use held would be a solo struggle).
            if (n.Crew[id].Has(PlayerFlags.Held))
                return default;
            // GDD v1.1 App. C.3: powder, ball, ram between shots (Use held at the gun).
            if (n.Train.Vehicles[guard].Gun.ReloadNeeded > 0)
                return new PlayerIntent { Buttons = PlayerButtons.Use };
            var target = pack.Where(h => !h.Gone && h.Attached < 0).OrderByDescending(h => h.LineDistance).FirstOrDefault();
            // Nothing to shoot at: a gunner with no restraint fires anyway.
            if (target is null)
                return fireAtRange(double.PositiveInfinity) ? new PlayerIntent { Buttons = PlayerButtons.Fire } : default;
            var frame = n.Train.Frames[guard];
            var offset = target.WorldPosition(n.Train) - frame.ToWorld(mount.Position);
            var d = frame.DirToLocal(offset).Normalized;
            n.Crew[id] = n.Crew[id] with { Yaw = Math.Atan2(-d.X, -d.Z), Pitch = Math.Asin(d.Y) };
            // The gun follows the view at its own pace (T112): fire once it's laid on the mark.
            bool laid = Guns.Laid(Guns.Mount(n.Train, guard)!.Value, n.Train.Vehicles[guard].Gun, d, Tuning.Combat.Guns);
            return laid && fireAtRange(offset.Length) ? new PlayerIntent { Buttons = PlayerButtons.Fire } : default;
        });
        return (n, pack, guard);
    }

    [Fact]
    public void AGunnerWhoWaitsForRangeKillsThePackWithoutBringingTheChoir()
    {
        var (n, pack, _) = GunnerVersusPack(range => range <= Tuning.Combat.Guns.Range);
        Assert.All(pack, h => Assert.True(h.Gone, $"hound {h.Id} {h.Phase}"));
        Assert.True(n.Crew[1].Alive);
        Assert.True(n.World.Choir.Loudness > 0);
        Assert.False(n.World.Choir.Present);
    }

    [Fact]
    public void AGunnerWhoSpraysFromTooFarBringsTheChoirDownOnHimself()
    {
        // GDD §14: "The gunner's job is less about accuracy than restraint." Firing at everything, out of range and in,
        // loads the meter (App. C.7) until the Choir gathers (App. A.7), and it seizes the one exposed on the roof.
        var (n, _, guard) = GunnerVersusPack(_ => true);
        Assert.True(n.Crew[1].Death == DeathCause.Seized, $"{n.Crew[1].Death}: build {n.World.Choir.Build:0.00} present {n.World.Choir.Present} spent {n.World.Choir.Spent} loud {n.World.Choir.Loudness:0.00} ammo {n.Train.Vehicles[guard].Gun.Ammo} ghosts {n.World.ActiveEnemies.Count(e => e is ChoirGhost)}");
    }

    [Fact]
    public void SustainedFireInRangeDrivesThePackOffEvenWithoutKills()
    {
        // GDD v1.1 App. A.3 break off: "rear cannon hit". A round over their heads still turns them.
        var n = new Night(6, speed: 14);
        int guard = n.Train.Dynamics.Consist.Vehicles[^1].Id;
        var mount = n.Train.Frames[guard].Shape.Gun!.Value;
        var gunner = PlayerMotor.SpawnOnRoof(n.Train, guard, mount.Position.Z - 0.7, P);
        n.Crew[1] = gunner with { Yaw = Math.PI, Pitch = 0.6, Flags = gunner.Flags | PlayerFlags.Seated };
        var pack = Pack(n);
        var muzzle = () => n.Train.Frames[guard].ToWorld(mount.Position);
        n.Run(60, _ => n.Train.Vehicles[guard].Gun.ReloadNeeded > 0 ? new PlayerIntent { Buttons = PlayerButtons.Use }
            : pack.Any(h => !h.Gone && (h.WorldPosition(n.Train) - muzzle()).Length <= Tuning.Combat.Guns.Range)
            ? new PlayerIntent { Buttons = PlayerButtons.Fire } : default);
        Assert.All(pack, h => Assert.True(h.Gone && h.Health == E.CinderHounds.Health, $"hound {h.Id} {h.Phase} hp {h.Health}"));
        Assert.Equal(pack.Count, n.Events.Count(e => e.Kind == EnemyKind.CinderHound && e.To == SpinePhase.BreakOff));
        // The pack never lands (the director's paced sends, a Climber say, are their own business).
        Assert.DoesNotContain(n.Events, e => e.Kind == EnemyKind.CinderHound && e.To is SpinePhase.Grab or SpinePhase.Punish);
        n.AssertFair();
    }

    /// <summary>The Choir gathered to its last tick: the next one brings the swarm.</summary>
    static void Gathered(Night n) => n.World.Choir = new ChoirState { Build = 0.9999, Loudness = Tuning.Combat.Choir.MaxLoudness };

    [Fact]
    public void TheChoirSeizesOnlyWhoeverIsExposed()
    {
        // GDD v1.1 App. A.7: it seizes "anyone outside, on the roofs, or behind no closed door"; a seize is a grab, and
        // nobody kills the one holding them, so it's a death.
        var n = new Night(6, speed: 10);
        var inside = new PlayerState { Parent = 3, Position = new Double3(-0.45, Tuning.Train.Geometry.Interior!.FloorHeight, 0), Surface = Surface.Deck, Health = P.Health };
        n.Crew[1] = inside;
        n.Crew[2] = PlayerMotor.SpawnOnRoof(n.Train, 4, 0, P);
        Gathered(n);
        n.Run(0.2);
        Assert.True(n.World.Choir.Present);
        Assert.Equal(E.Choir.Ghosts, n.World.ActiveEnemies.Count(e => e is ChoirGhost));
        n.Run(E.Choir.SeizeSeconds + 15);
        Assert.Equal(P.Health, n.Crew[1].Health);
        Assert.Equal(DeathCause.Seized, n.Crew[2].Death);
        // One taken and it's gone for the run.
        Assert.True(n.World.Choir.Spent);
        Assert.DoesNotContain(n.World.ActiveEnemies, e => e is ChoirGhost && !e.Gone);
    }

    [Fact]
    public void HushedAndShutInTheChoirDispersesHavingTakenNobody()
    {
        var n = new Night(6, speed: 10);
        var inside = new PlayerState { Parent = 3, Position = new Double3(-0.45, Tuning.Train.Geometry.Interior!.FloorHeight, 0), Surface = Surface.Deck, Health = P.Health };
        n.Crew[1] = inside;
        Gathered(n);
        n.Run(E.Choir.DisperseQuietSeconds + 8);
        Assert.False(n.World.Choir.Present);
        Assert.False(n.World.Choir.Spent);
        Assert.True(n.Crew[1].Alive);
    }

    [Fact]
    public void ADirectorPlannedForFourGoesByWhoIsActuallyThere()
    {
        // T115 playtest ("I'll be in the cab piloting the train and suddenly I can't move, and a few seconds later I die"): a
        // solo host was planned for four, and Tippy Toesie (minCrew 2: it needs a friend to pull it off) came for them.
        var n = new Night(6, speed: 14);
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, 2, 0, P);
        Assert.Equal(4, n.World.Director!.Crew);
        n.Run(2);
        Assert.Equal(1, n.World.Director.Crew);
        Assert.True(n.World.Director.Crew < Tuning.Enemies.TippyToesie.MinCrew);
    }

    [Fact]
    public void DrivenOffTheChoirRestsBeforeItCanGatherAgain()
    {
        // T113 playtest ("too frequent, no counterplay"): hushing it off buys the crew a long stretch where noise is free.
        var t = Tuning.Combat.Choir;
        var choir = new ChoirState { Present = true, Build = 1 };
        choir.Disperse(took: false, t.RestSeconds);
        double dt = SimConstants.TickSeconds;
        for (double s = 0; s < t.RestSeconds - 1; s += dt)
            Assert.False(choir.Step(t, t.MaxLoudness, dt));
        Assert.Equal(0, choir.Build);
        // Rested, the same din gathers it again, over the build's long telegraph and no sooner.
        double gathered = 0;
        for (double s = 0; s < t.BuildSeconds * 3 && gathered == 0; s += dt)
            if (choir.Step(t, t.MaxLoudness, dt))
                gathered = s;
        Assert.InRange(gathered, t.BuildSeconds * 0.9, t.BuildSeconds * 1.5);
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
            // minding the fire.
            // Paced spawns (quiet too long) come when they must, cooldown or not.
            var spawns = d.Log.Where(l => l.Kind is not EnemyKind.Stoker && !l.Paced).ToList();
            Assert.All(spawns, l => Assert.True(l.Tick * SimConstants.TickSeconds >= E.Director.GraceSeconds, $"spawn at {l.Tick / 30} s"));
            for (int i = 1; i < spawns.Count; i++)
                Assert.True((spawns[i].Tick - spawns[i - 1].Tick) * SimConstants.TickSeconds >= E.Director.CooldownSeconds[0] - 1);
            // The caps are on what's engaged; the condition-triggered ones aren't capped (App. B.5).
            Assert.All(d.Log.Where(l => l.Kind is not EnemyKind.Stoker),
                l => Assert.True(l.ActiveInZone <= E.Director.MaxConcurrentZone && l.ActiveTotal <= E.Director.MaxConcurrentSmallCrew, $"{l}"));
            Assert.All(d.Log, l => Assert.True(l.TrainDistance <= route.Length - 500));
            // The budget holds for what's spent on its curve; a paced spawn may overdraw it (a quiet night's worse).
            Assert.True(d.Log.Where(l => !l.Paced).Sum(l => l.Cost) <= d.Budget + 1e-9);
            n.AssertFair();
        }
    }
}
