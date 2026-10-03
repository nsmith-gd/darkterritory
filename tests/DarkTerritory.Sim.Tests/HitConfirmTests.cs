using Ballast;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Enemies;
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

    /// <summary>Fires the engine's gun once at an enemy's hit volume (where the last tick's targets have it), loaded or not.</summary>
    static void FireAt(Night n, Enemy e)
    {
        ref var gun = ref n.Train.Vehicles[0].Gun;
        (gun.Cooldown, gun.ReloadNeeded) = (0, 0);
        // Where the gunner saw it: the targets as the world last had them (as it's just been put there, without a tick of its
        // own first: a Switchman with no junction to wait at, or a hound with no train to chase, wouldn't stay).
        var target = new HitTarget(e.Id, e.HitCentre(n.Train), e.HitRadius);
        n.World.Targets.Add(target);
        // Laid on its upper half: the stack's in the way of anything lower on the rail nearer than about 60 m (the engine's
        // gun stands behind it, GunTests' RoundsStopAtTheTrainsOwnBody).
        n.Crew[1] = LaidOn(n, n.Crew.GetValueOrDefault(1, Gunner(n)), target.Position + Double3.Up * (target.Radius * 0.5));
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
            n.Crew[1] = LaidOn(n, n.Crew[1], doll.HitCentre(n.Train));
            var muzzle = n.Train.Frames[0].ToWorld(Guns.Mount(n.Train, 0)!.Value.Position);
            // (A few ticks in: the world's targets are last tick's, and she's only just been put there.)
            bool inRange = (doll.HitCentre(n.Train) - muzzle).Length < G.Range - 2 && i >= 3;
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
        Assert.Equal(0, doll.HitRadius);
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
        EnemyKind.TippyToesie, EnemyKind.FireFlies, EnemyKind.Ribbit, EnemyKind.Grumbler, EnemyKind.Choir,
        EnemyKind.ShyThing, EnemyKind.Huddle, EnemyKind.Mimic,
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
        EnemyKind.ShyThing => new ShyThing(id),
        EnemyKind.Huddle => new Huddle(id),
        EnemyKind.Mimic => new Mimic(id),
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
    /// come out of its gap, a Shy Thing unhinged on someone, a Mimic its crate).
    /// </summary>
    static (Night Night, Enemy Enemy) Staged(EnemyKind kind)
    {
        var n = new Night(4, speed: 0);
        const int car = 2;
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, car, 0, P);
        var s = n.Crew[1];
        var at = s.Position + new Double3(0, 0, -1.5);
        var phase = kind switch
        {
            EnemyKind.TrackDoll => SpinePhase.Punish,
            EnemyKind.Whistler => SpinePhase.Commit,
            EnemyKind.ShyThing => SpinePhase.Grab,
            _ => SpinePhase.Telegraph,
        };
        // A Shy Thing is struck only once it shows (App. A.6 UNHINGE): on a crewmate past it, the one it has.
        int holding = -1;
        if (kind == EnemyKind.ShyThing)
        {
            n.Crew[2] = PlayerMotor.SpawnOnRoof(n.Train, car, -2.5, P);
            holding = 2;
        }
        double extra = kind switch
        {
            EnemyKind.TrackDoll => 1,
            EnemyKind.Follower or EnemyKind.Ribbit or EnemyKind.Gaunt or EnemyKind.Choir or EnemyKind.TippyToesie or EnemyKind.Huddle => -1,
            EnemyKind.ShyThing => holding,
            // A Mimic is a crate (its Extra the crate's id), lying there.
            EnemyKind.Mimic => n.World.Bodies.SpawnCrate(n.Train, car, at, Physics.BodyKind.Cargo).Id,
            _ => 0,
        };
        double extra2 = kind == EnemyKind.SootChildren ? 1 : 0;
        var e = n.World.AddEnemy(id =>
        {
            var made = Make(kind, id);
            made.Restore(phase, 0.5, 5, car, at, 0, 0, 0, extra, extra2, holding, holding >= 0 ? 10 : 0);
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
    public void TheCarrierCantClubTheFollowerOnTheirOwnBack()
    {
        // App. A.6: only a friend can. A swing that can't land isn't confirmed (nor picked).
        var (n, e) = Staged(EnemyKind.Follower);
        e.Extra = 1;
        Assert.False(e.Strikable(1));
        n.Run(1.0 / SimConstants.TickRate, id => new PlayerIntent { Actions = PlayerActions.Swing });
        Assert.Empty(n.World.Hits);
    }

    /// <summary>The creatures a ball can find (the rest are aboard, under the cars, in the gaps or out of the arcs: §21).</summary>
    public static TheoryData<EnemyKind> Shootable() => [EnemyKind.CinderHound, EnemyKind.Switchman, EnemyKind.Climber, EnemyKind.TrackDoll];

    [Theory]
    [MemberData(nameof(Shootable))]
    public void EveryCreatureABallCanFindConfirmsTheHit(EnemyKind kind)
    {
        var n = new Night(4, speed: 6);
        var e = n.World.AddEnemy(id =>
        {
            var made = Make(kind, id);
            // On the line ahead of the engine, in the lamp, as each is when it can be shot.
            made.Restore(SpinePhase.Telegraph, 0.5, 2, -1, default, n.Train.Dynamics.Distance + 64, 0, kind == EnemyKind.TrackDoll ? 0 : 0.6, 0, 0);
            return made;
        });
        Assert.True(e.HitRadius > 0, $"{kind} has no hit volume where it's staged");
        FireAt(n, e);
        var shot = Assert.Single(n.Shots);
        Assert.Equal(e.Id, shot.HitTargetId);
        var hit = Assert.Single(n.World.Hits);
        Assert.Equal((e.Id, kind, 1, HitSource.Cannon), (hit.EnemyId, hit.Kind, hit.By, hit.Source));
        var impact = Assert.Single(n.World.Impacts);
        Assert.Equal((ImpactSurface.Creature, kind), (impact.Surface, impact.Struck));
        Assert.InRange((impact.At - hit.At).Length, 0, 1e-9);
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
        // As GunTests' RoundsStopAtTheTrainsOwnBody: down over the boiler.
        var n = new Night(4, speed: 0);
        var s = Gunner(n);
        n.Crew[1] = s with { Pitch = -11 * Math.PI / 180 };
        n.Train.Vehicles[0].Gun.Elevation = -11 * Math.PI / 180;
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
}
