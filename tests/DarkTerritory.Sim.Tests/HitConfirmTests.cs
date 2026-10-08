using Ballast;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// T121 playtest: "I shot the track doll and it did nothing", "all creatures need hit confirm feedback", "all cannonballs
/// should have an impact explosion". The cannon answers the Track Doll on the rail, every creature a blow or a ball lands on
/// says so to every client, and every ball comes down somewhere that's told.
/// </summary>
public class HitConfirmTests
{
    static readonly EnemyTuning E = Tuning.Enemies;
    static readonly PlayerTuning P = Tuning.Player;
    static readonly GunTuning G = Tuning.Combat.Guns;

    static readonly PlayerIntent Fire = new() { Buttons = PlayerButtons.Fire };

    /// <summary>Sat in the engine's gun seat (T112), loaded.</summary>
    static PlayerState Gunner(Night n)
    {
        var mount = n.Train.Frames[0].Shape.Gun!.Value;
        var s = PlayerMotor.SpawnOnRoof(n.Train, 0, mount.Position.Z - mount.Facing.Z * 0.7, P);
        s.Yaw = mount.Facing.Z < 0 ? 0 : Math.PI;
        s.Flags |= PlayerFlags.Seated;
        return s;
    }

    /// <summary>The seated gunner's view, and the gun with it, laid straight on a point (world), as a steady hand has it.</summary>
    static PlayerState LaidOn(Night n, PlayerState s, Double3 at)
    {
        var mount = Guns.Mount(n.Train, 0)!.Value;
        var frame = n.Train.Frames[0];
        var d = frame.DirToLocal(at - frame.ToWorld(mount.Position)).Normalized;
        double yaw = DMath.Atan2(-d.X, -d.Z), pitch = DMath.Asin(d.Y);
        ref var gun = ref n.Train.Vehicles[0].Gun;
        (gun.Traverse, gun.Elevation) = (yaw - Guns.FacingYaw(mount), pitch);
        return s with { Yaw = yaw, Pitch = pitch };
    }

    /// <summary>Fires the engine's gun once at an enemy's body (where the last tick's targets have it), loaded or not.</summary>
    static void FireAt(Night n, Enemy e)
    {
        ref var gun = ref n.Train.Vehicles[0].Gun;
        (gun.Cooldown, gun.ReloadNeeded) = (0, 0);
        // Where the gunner saw it: the targets as the world last had them (as it's just been put there, without a tick of its
        // own first: a Switchman with no junction to wait at, or a hound with no train to chase, wouldn't stay).
        var body = e.Body(n.Train, E).ToList();
        n.World.Targets.AddRange(body);
        // Laid on the top of it: the stack's in the way of anything lower on the rail nearer than about 60 m (the engine's
        // gun stands behind it, GunTests' RoundsStopAtTheTrainsOwnBody).
        var top = body.MaxBy(t => t.Position.Y);
        n.Crew[1] = LaidOn(n, n.Crew.GetValueOrDefault(1, Gunner(n)), top.Position + Double3.Up * (top.Radius * 0.5));
        n.Run(1.0 / SimConstants.TickRate, id => id == 1 ? Fire : default);
    }

    // ---- The Track Doll and the forward cannon.

    [Fact]
    public void TheForwardCannonShattersTheDollOnTheRailAndTheTrainPassesHerSpotClean()
    {
        // App. A.2: stop short and she's gone for the run; strike her and she haunts the train. A forward cannon's ball on
        // the rail is the first outcome (T121), not nothing: the train no longer strikes her.
        var n = new Night(4, speed: 10);
        var doll = n.World.AddEnemy(id => TrackDoll.Ahead(id, n.Train, 110, E.TrackDoll));
        double spot = doll.LineDistance;
        n.Crew[1] = Gunner(n);
        // Up to her in the lamp, the gunner laying on her as she comes into range, firing once laid.
        bool fired = false;
        for (int i = 0; i < 10 * SimConstants.TickRate && !doll.Gone; i++)
        {
            n.Crew[1] = LaidOn(n, n.Crew[1], doll.AimPoint(n.Train, E));
            var muzzle = n.Train.Frames[0].ToWorld(Guns.Mount(n.Train, 0)!.Value.Position);
            // (A few ticks in: the world's targets are last tick's, and she's only just been put there.)
            bool inRange = (doll.AimPoint(n.Train, E) - muzzle).Length < G.Range - 2 && i >= 3;
            n.Run(1.0 / SimConstants.TickRate, id => id == 1 && inRange ? Fire : default);
            fired |= n.Shots.Count > 0;
        }
        Assert.True(fired);
        var shot = n.Shots.First();
        Assert.Equal(doll.Id, shot.HitTargetId);
        Assert.False(shot.BlockedByTrain);
        Assert.True(doll.Gone);
        Assert.True(doll.Shattered);
        // Gone the way stopping short goes (BREAK OFF, then gone), never aboard.
        Assert.Contains(n.Events, e => e.EnemyId == doll.Id && e.To == SpinePhase.BreakOff);
        // Every client's told: the ball landed on her, and it was a kill.
        Assert.Contains(n.World.Hits, h => h.EnemyId == doll.Id && h.Kind == EnemyKind.TrackDoll && h.Source == HitSource.Cannon && h.By == 1 && h.Killed);
        Assert.Contains(n.World.Impacts, i => i.Surface == ImpactSurface.Creature && i.Struck == EnemyKind.TrackDoll && i.Shooter == 1);
        // On past where she stood: no strike, no haunting, nothing at the controls.
        double ahead = spot - n.Train.Dynamics.Distance;
        Assert.True(ahead > 0, "fired before the train reached her");
        n.Run(ahead / 10 + 6);
        Assert.True(n.Train.Dynamics.Distance > spot + 40);
        Assert.DoesNotContain(n.Events, e => e.EnemyId == doll.Id && e.To is SpinePhase.Commit or SpinePhase.Punish);
        Assert.DoesNotContain(n.World.ActiveEnemies, e => e.Kind == EnemyKind.TrackDoll);
        n.AssertFair();
    }

    [Fact]
    public void AboardTheCannonCantReachTheDoll()
    {
        // Haunting the train, she's in a car: only cornered and clubbed (App. A.2), never shot.
        var n = new Night(4, speed: 10);
        var doll = n.World.AddEnemy(id => TrackDoll.Ahead(id, n.Train, 60, E.TrackDoll));
        n.Run(8);
        Assert.True(doll.Haunting);
        Assert.False(doll.Exposed);
        Assert.DoesNotContain(n.World.Targets, t => t.Id == doll.Id);
    }

    // ---- Hit confirm for every creature.

    /// <summary>
    /// The creatures in the sim (GDD v1.1 §21's roster, the Choir's ghosts among them), each made as the host makes it.
    /// Not the hazards (§22's track debris and marsh) nor a car fire: those aren't struck, they're braked for, crossed and
    /// put out.
    /// </summary>
    public static readonly EnemyKind[] Creatures =
    [
        EnemyKind.CinderHound, EnemyKind.Switchman, EnemyKind.SootChildren, EnemyKind.Dragger, EnemyKind.Stoker, EnemyKind.Climber,
        EnemyKind.Gaunt, EnemyKind.Passenger, EnemyKind.Follower, EnemyKind.TrackDoll, EnemyKind.CarHugger, EnemyKind.Whistler,
        EnemyKind.TippyToesie, EnemyKind.FireFlies, EnemyKind.Ribbit, EnemyKind.Grumbler, EnemyKind.Choir, EnemyKind.Moose,
    ];

    static Enemy Make(EnemyKind kind, int id) => kind switch
    {
        EnemyKind.CinderHound => new CinderHound(id, 0),
        EnemyKind.Switchman => new Switchman(id),
        EnemyKind.SootChildren => new SootChildren(id),
        EnemyKind.Dragger => new Dragger(id),
        EnemyKind.Stoker => new Stoker(id),
        EnemyKind.Climber => new Climber(id),
        EnemyKind.Gaunt => new Gaunt(id),
        EnemyKind.Passenger => new Passenger(id),
        EnemyKind.Follower => new Follower(id),
        EnemyKind.TrackDoll => new TrackDoll(id),
        EnemyKind.CarHugger => new CarHugger(id),
        EnemyKind.Whistler => new Whistler(id),
        EnemyKind.TippyToesie => new TippyToesie(id),
        EnemyKind.FireFlies => new FireFlies(id),
        EnemyKind.Ribbit => new Ribbit(id, 0),
        EnemyKind.Grumbler => new Grumbler(id),
        EnemyKind.Choir => new ChoirGhost(id),
        EnemyKind.Moose => new Moose(id),
        _ => throw new ArgumentException($"{kind} isn't a creature"),
    };

    [Fact]
    public void TheCreatureListIsTheWholeRosterBarTheHazardsAndFire()
    {
        // A kind added to the roster has to come here too, with a way to be struck.
        var all = Enum.GetValues<EnemyKind>().Except([EnemyKind.Sleepers, EnemyKind.Drift, EnemyKind.CarFire]);
        Assert.Equal(all.OrderBy(k => k), Creatures.OrderBy(k => k));
    }

    /// <summary>
    /// A creature where a blow can land on it (App. C.2): on car 2's roof, a pace and a half in front of a crewmate there,
    /// in the phase its rule says it can be struck in (the Track Doll cornered, a Soot Child a Soot Child, the Whistler
    /// come out of its gap).
    /// </summary>
    internal static (Night Night, Enemy Enemy) Staged(EnemyKind kind)
    {
        var n = new Night(4, speed: 0);
        const int car = 2;
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, car, 0, P);
        var s = n.Crew[1];
        var phase = kind switch
        {
            EnemyKind.TrackDoll => SpinePhase.Punish,
            EnemyKind.Whistler => SpinePhase.Commit,
            _ => SpinePhase.Telegraph,
        };
        double extra = kind switch { EnemyKind.TrackDoll => 1, EnemyKind.Follower or EnemyKind.Ribbit or EnemyKind.Gaunt or EnemyKind.Choir or EnemyKind.TippyToesie or EnemyKind.Moose => -1, _ => 0 };
        // A Stoker on its way in from the tender (note 263): in the open, where a blow lands (in the fire, only with the door open).
        double extra2 = kind is EnemyKind.SootChildren or EnemyKind.Stoker ? 1 : 0;
        var e = n.World.AddEnemy(id =>
        {
            var made = Make(kind, id);
            made.Restore(phase, 0.5, 5, car, s.Position + new Double3(0, 0, -1.5), 0, 0, 0, extra, extra2);
            return made;
        });
        return (n, e);
    }

    [Theory]
    [MemberData(nameof(CreatureKinds))]
    public void EveryCreatureConfirmsABlowThatLandsToEveryClient(EnemyKind kind)
    {
        var (n, e) = Staged(kind);
        Assert.True(e.Strikable(1), $"{kind} can't be struck where it's staged");
        var stood = e.WorldPosition(n.Train);
        n.Run(1.0 / SimConstants.TickRate, id => new PlayerIntent { Actions = PlayerActions.Swing });
        var hit = Assert.Single(n.World.Hits);
        Assert.Equal((e.Id, kind, 1, HitSource.Melee), (hit.EnemyId, hit.Kind, hit.By, hit.Source));
        Assert.InRange((hit.At - stood).Length, 0, 1);
        // And it reaches a client: through the wire as a record, rebuilt on the other side.
        var client = new World(n.Train);
        var controls = new TrainControls();
        client.EnableEnemies(E, null, 1, 1, authority: false);
        WorldRecords.Apply(WorldRecords.Capture(n.World, controls, []), client, ref controls, []);
        var mirrored = Assert.Single(client.Hits);
        Assert.Equal((hit.Id, hit.Tick, hit.EnemyId, hit.Kind, hit.By, hit.Source, hit.Killed), (mirrored.Id, mirrored.Tick, mirrored.EnemyId, mirrored.Kind, mirrored.By, mirrored.Source, mirrored.Killed));
        Assert.InRange((mirrored.At - hit.At).Length, 0, 1e-3);
    }

    public static TheoryData<EnemyKind> CreatureKinds() => [.. Creatures];

    [Fact]
    public void ASwingAtNothingConfirmsNothing()
    {
        var n = new Night(4, speed: 0);
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, 2, 0, P);
        n.Run(0.5, id => new PlayerIntent { Actions = PlayerActions.Swing });
        Assert.Empty(n.World.Hits);
    }

    [Fact]
    public void ASwingAtNothingIsStillSeenByEveryClient()
    {
        // Note 146's "the swing has a clip but no reader" (note 197): a blow that lands has its HitConfirm, but one at
        // nothing was never sent, so nobody else saw the crewmate swing. Every swing is its own record now, one per
        // recovery while Swing is held, landed or not.
        var n = new Night(4, speed: 0);
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, 2, 0, P);
        // (Just past the second: swings are kept on the wire as long as hits are, combat.json hits.keepSeconds.)
        n.Run(E.Melee.SwingSeconds + 0.1, id => new PlayerIntent { Actions = PlayerActions.Swing });
        Assert.Empty(n.World.Hits);
        Assert.Equal(2, n.World.Swings.Count);
        Assert.All(n.World.Swings, w => Assert.Equal(1, w.By));
        var gap = n.World.Swings[1].Tick - n.World.Swings[0].Tick;
        Assert.Equal((uint)Math.Round(E.Melee.SwingSeconds * SimConstants.TickRate), gap);
        var client = new World(n.Train);
        var controls = new TrainControls();
        client.EnableEnemies(E, null, 1, 1, authority: false);
        WorldRecords.Apply(WorldRecords.Capture(n.World, controls, []), client, ref controls, []);
        Assert.Equal(n.World.Swings, client.Swings);
    }

    [Fact]
    public void TheCarrierCantClubTheFollowerOnTheirOwnBack()
    {
        // App. A.6: only a friend can. A swing that can't land isn't confirmed (nor picked).
        var (n, e) = Staged(EnemyKind.Follower);
        e.Extra = 1;
        Assert.False(e.Strikable(1));
        n.Run(1.0 / SimConstants.TickRate, id => new PlayerIntent { Actions = PlayerActions.Swing });
        Assert.Empty(n.World.Hits);
    }

    /// <summary>
    /// Every creature with a body (note 290: GDD App. F.1, "the guns do nothing"): all but a swarm and the ghosts. Where a ball
    /// can reach it is the gun's arcs and the train's own body (§13, §21): under the cars, in the gaps and aboard it can't.
    /// </summary>
    public static TheoryData<EnemyKind> Shootable() => [.. Creatures.Except([EnemyKind.FireFlies, EnemyKind.Choir])];

    /// <summary>On the line ahead of the engine, in the lamp, in the open: the phase each is out in (the Whistler come out of its gap).</summary>
    static Enemy Ahead(Night n, EnemyKind kind, double health = 2)
    {
        var phase = kind == EnemyKind.Whistler ? SpinePhase.Commit : SpinePhase.Telegraph;
        double extra = kind is EnemyKind.Follower or EnemyKind.Ribbit or EnemyKind.Gaunt or EnemyKind.TippyToesie or EnemyKind.Moose ? -1 : 0;
        double extra2 = kind is EnemyKind.SootChildren or EnemyKind.Stoker ? 1 : 0;
        return n.World.AddEnemy(id =>
        {
            var made = Make(kind, id);
            made.Restore(phase, 0.5, health, -1, default, n.Train.Dynamics.Distance + 64, 0, 0, extra, extra2);
            return made;
        });
    }

    [Theory]
    [MemberData(nameof(Shootable))]
    public void EveryCreatureABallCanFindConfirmsTheHit(EnemyKind kind)
    {
        // Build 1121: "Rounds don't collide where they land and have no visible effect on the monsters". In the open, every
        // creature's body stops a ball, and the hit is told to every client (the flinch, the flash, the marker).
        var n = new Night(4, speed: 6);
        var e = Ahead(n, kind);
        Assert.True(e.Exposed, $"{kind} isn't in the open where it's staged");
        Assert.NotEmpty(e.Body(n.Train, E));
        FireAt(n, e);
        var shot = Assert.Single(n.Shots);
        Assert.Equal(e.Id, shot.HitTargetId);
        var hit = Assert.Single(n.World.Hits);
        Assert.Equal((e.Id, kind, 1, HitSource.Cannon), (hit.EnemyId, hit.Kind, hit.By, hit.Source));
        var impact = Assert.Single(n.World.Impacts);
        Assert.Equal((ImpactSurface.Creature, kind), (impact.Surface, impact.Struck));
        Assert.InRange((impact.At - hit.At).Length, 0, 1e-9);
        // On its body, not past it: within its tallest reach of where it stands.
        var stood = e.WorldPosition(n.Train);
        Assert.InRange((impact.At - stood).Length, 0, E.Body(kind).Max(s => s.Height + s.Radius) + 0.05);
    }

    [Theory]
    [InlineData(EnemyKind.FireFlies)]
    [InlineData(EnemyKind.Choir)]
    public void ABallGoesThroughASwarmAndAGhost(EnemyKind kind)
    {
        // No body to stop at (note 290): the Fire Flies part round it, a ghost isn't there to it. Every round still feeds the
        // Choir's meter.
        var n = new Night(4, speed: 6);
        var e = Ahead(n, kind);
        Assert.False(e.Exposed);
        Assert.Empty(E.Body(kind));
        n.Run(1.0 / SimConstants.TickRate);
        Assert.DoesNotContain(n.World.Targets, t => t.Id == e.Id);
    }

    [Fact]
    public void EveryBodyIsUprightOverWhereItStands()
    {
        // enemies.json bodies: a stack of spheres over the creature's feet, none below the ground it stands on, each its own size.
        Assert.Equal(Shootable().Select(r => r.Data).OrderBy(k => k), E.Bodies.Keys.Select(k => Enum.Parse<EnemyKind>(k, true)).OrderBy(k => k));
        foreach (var kind in Creatures)
            foreach (var (radius, height) in E.Body(kind))
            {
                Assert.InRange(radius, 0.25, 1.5);
                Assert.InRange(height, 0, 3.5);
            }
    }

    [Fact]
    public void ASleepingGauntIsCurledUpLowAndWakingItStandsTall()
    {
        var n = new Night(4, speed: 0);
        var g = n.World.AddEnemy(id => Gaunt.Asleep(id, n.Train.Line.Sample(n.Train.Dynamics.Distance + 60).Position, E.Gaunt));
        Assert.True(g.Exposed);
        double asleep = g.Body(n.Train, E).Max(t => t.Position.Y);
        g.Restore(SpinePhase.Telegraph, 0, g.Health, g.Attached, g.Local, 0, 0, 0, 1, 0);
        double awake = g.Body(n.Train, E).Max(t => t.Position.Y);
        Assert.InRange(asleep - g.Local.Y, 0.5, 1.5);
        Assert.InRange(awake - g.Local.Y, 2.5, 3.5);
    }

    [Fact]
    public void ABallOnAGrumblerMaulingACrewmateFreesThemAndTurnsItOnTheGunner()
    {
        // A ball lands as a friend's blow (note 290), answered by the Grumbler's own rule (App. A.8): the maul is broken, and
        // it's FERAL now, after whoever struck it: the gunner. Its health (6) takes more than one ball (combat.json 4).
        var n = new Night(4, speed: 0);
        n.Crew[1] = Gunner(n);
        var sample = n.Train.Line.Sample(n.Train.Dynamics.Distance + 60);
        var right = Double3.Cross(sample.Tangent, Double3.Up).Normalized;
        n.Crew[2] = PlayerMotor.SpawnOnGround(sample.Position + right * 6, n.Train.Line, n.Train.Dynamics.Distance + 60, P);
        n.Run(0.2);
        var grumbler = n.World.AddEnemy(id => Grumbler.OnCrates(id, sample.Position + right * 6.8, -1, E.Grumbler));
        // Its maul: committed on the crewmate, and holding them.
        grumbler.Restore(SpinePhase.Grab, 0.5, E.Grumbler.Health, Enemy.Loose, grumbler.Local, 0, 0, 0, -1, 1, holding: 2, grabWindow: 8);
        n.Run(1.0 / SimConstants.TickRate);
        FireAt(n, grumbler);
        var hit = Assert.Single(n.World.Hits, h => h.Source == HitSource.Cannon);
        Assert.Equal((grumbler.Id, 1, false), (hit.EnemyId, hit.By, hit.Killed));
        Assert.NotEqual(SpinePhase.Grab, grumbler.Phase);
        Assert.Equal(-1, grumbler.Holding);
        Assert.Equal(1, grumbler.LastHitBy);
        Assert.False(grumbler.Gone);
        // Note 288: a gunner alone is one striker, and only a gang wears a Grumbler down; the old rule's ball took 4 of its 6.
        Assert.Equal(E.Grumbler.Health, grumbler.Health, 1);
    }

    // ---- Where every ball comes down.

    [Fact]
    public void ABallAimedAtTheGroundComesDownOnItAndOneAimedHighComesDownWhereItsRangeRunsOut()
    {
        var n = new Night(4, speed: 0);
        n.Crew[1] = Gunner(n);
        var engine = n.Train.Frames[0];
        // Down at the ground 50 m ahead, off to the side of the stack (dead ahead, that low, it's in the way).
        var sample = n.Train.Line.Sample(n.Train.Dynamics.Distance + 50);
        var ground = sample.Position + Double3.Cross(sample.Tangent, Double3.Up).Normalized * 4;
        n.Crew[1] = LaidOn(n, n.Crew[1], ground);
        n.Run(1.0 / SimConstants.TickRate, id => Fire);
        var low = Assert.Single(n.World.Impacts);
        Assert.Equal(ImpactSurface.Ground, low.Surface);
        Assert.InRange((low.At - ground).Length, 0, 0.5);
        // Up over everything: it comes down under the end of its range, on the ground.
        (n.Train.Vehicles[0].Gun.Cooldown, n.Train.Vehicles[0].Gun.ReloadNeeded) = (0, 0);
        n.Crew[1] = LaidOn(n, n.Crew[1], engine.ToWorld(new Double3(0, 40, -engine.Shape.HalfLength - 60)));
        n.Run(1.0 / SimConstants.TickRate, id => Fire);
        var high = n.World.Impacts[^1];
        Assert.Equal(ImpactSurface.Ground, high.Surface);
        Assert.InRange(high.At.Y - n.Train.Line.Sample(n.Train.Dynamics.Distance).Position.Y, -0.5, 0.5);
        Assert.InRange((high.At - engine.Origin).Length, 50, G.Range + 20);
    }

    [Fact]
    public void ABallIntoTheTrainsOwnBodyLandsOnTheTrain()
    {
        // As GunTests' RoundsStopAtTheTrainsOwnBody: the rear gun at the front of its rail, laid back down along its roof.
        var n = new Night(4, speed: 0);
        int last = n.Train.Frames.Count - 1;
        n.Train.Vehicles[last].Gun.Z = n.Train.Frames[last].Shape.RoofRail!.Value.Front;
        var mount = Guns.Mount(n.Train, last)!.Value;
        var s = PlayerMotor.SpawnOnRoof(n.Train, last, mount.Position.Z - mount.Facing.Z * 0.7, P);
        s.Yaw = Math.PI;
        s.Flags |= PlayerFlags.Seated;
        n.Crew[1] = s with { Pitch = -11 * Math.PI / 180 };
        n.Train.Vehicles[last].Gun.Elevation = -11 * Math.PI / 180;
        n.Run(1.0 / SimConstants.TickRate, id => Fire);
        var shot = Assert.Single(n.Shots);
        Assert.True(shot.BlockedByTrain);
        Assert.Equal(ImpactSurface.Train, Assert.Single(n.World.Impacts).Surface);
    }

    [Fact]
    public void ImpactsAndHitsAreKeptAWhileThenGo()
    {
        var n = new Night(4, speed: 0);
        n.Crew[1] = Gunner(n);
        var sample = n.Train.Line.Sample(n.Train.Dynamics.Distance + 50);
        n.Crew[1] = LaidOn(n, n.Crew[1], sample.Position + Double3.Cross(sample.Tangent, Double3.Up).Normalized * 4);
        n.Run(1.0 / SimConstants.TickRate, id => Fire);
        Assert.Single(n.World.Impacts);
        n.Run(Tuning.Combat.Hits.ImpactKeepSeconds - 1);
        Assert.Single(n.World.Impacts);
        n.Run(2);
        Assert.Empty(n.World.Impacts);
    }

    [Fact]
    public void ABallFiredAtTheFortWallStopsAtItWithAnImpact()
    {
        // T124 (build 1121: "gun shots hit nothing"): in the home fortress's yard, laid on the wall 50 m ahead and to the side
        // (a house between may take it first): the ball strikes a building, short of the wall's far face, where a ball's
        // impact is told to everyone, and the stone isn't passed through to the ground beyond.
        var route = Routes.Generate(DataFile.FindContentRoot(), "frontier:7", 6);
        var n = new Night(4, speed: 0, route);
        n.World.EnableRun(Tuning.Run, route, 600, authority: true);
        Assert.InRange(n.Train.Dynamics.Distance, 100, 500);
        n.Crew[1] = Gunner(n);
        foreach (int side in new[] { -1, 1 })
        {
            var t = n.Train.Line.Sample(n.Train.Dynamics.Distance + 50);
            var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
            var wall = t.Position + right * (side * Run.Fortresses.WallOut) + Double3.Up * 3;
            (n.Train.Vehicles[0].Gun.Cooldown, n.Train.Vehicles[0].Gun.ReloadNeeded) = (0, 0);
            int before = n.World.Impacts.Count;
            n.Crew[1] = LaidOn(n, n.Crew[1], wall);
            n.Run(1.0 / SimConstants.TickRate, id => Fire);
            var impact = Assert.Single(n.World.Impacts.Skip(before));
            Assert.Equal(ImpactSurface.Structure, impact.Surface);
            var muzzle = n.Train.Frames[0].ToWorld(Guns.Mount(n.Train, 0)!.Value.Position);
            Assert.True((impact.At - muzzle).Length <= (wall - muzzle).Length + Run.Fortresses.WallHalf / Math.Abs(Double3.Dot((wall - muzzle).Normalized, right)) + 0.1,
                $"the ball went {(impact.At - muzzle).Length:0.0} m, past the wall at {(wall - muzzle).Length:0.0} m");
            Assert.Contains(n.Train.Walls!.Near(impact.At), w =>
            {
                var l = w.ToLocal(impact.At);
                return Math.Abs(l.X) <= w.HalfLength + 0.05 && Math.Abs(l.Z) <= w.HalfWidth + 0.05;
            });
        }
    }
}
