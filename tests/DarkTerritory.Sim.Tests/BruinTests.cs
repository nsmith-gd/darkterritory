using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The Bruin (GDD §21; the director's playtest of 23 Oct 2026, line 36; Sloe's docs/design/creatures/bruin.md; ARCHITECTURE §8
/// note 1007). Rule: never alone with it; never face it. It comes only to a train stood in the wilderness, out of the trees
/// to the car with the most cargo, climbs in and feeds; whoever stays in front of it is charged and mauled to death; a blow
/// from the side or behind staggers it, drops its victim and turns it on the hitter, while one from the front it barely
/// feels; enough blows drive it out, more kill it.
/// </summary>
public class BruinTests
{
    static readonly EnemyTuning E = Tuning.Enemies;
    static readonly BruinTuning B = E.Bruin;
    static readonly PlayerTuning P = Tuning.Player;
    static readonly EnemyTuning On = HoundRunTests.Quiet with { Bruin = B with { On = true } };

    /// <summary>A train of four stood still (the wilderness: no route, no stop), and one come out of the trees on the right of its cargo car.</summary>
    static (Night N, Bruin Bruin) Out(double speed = 0)
    {
        var n = new Night(4, speed, enemies: On);
        int car = Bruin.CarFor(n.Train);
        var home = Bruin.TreeLine(n.World, car, 1, B);
        var bruin = n.World.AddEnemy(id => Bruin.Out(id, home, car, B.Health));
        return (n, bruin);
    }

    /// <summary>Into its car: its walk in and the huff.</summary>
    static void In(Night n, Bruin b)
    {
        for (int i = 0; i < 40 && !b.InCar; i++)
            n.Run(1);
        Assert.True(b.InCar, $"in its car after its walk: {b.Mode}, {b.Phase}");
    }

    /// <summary>Someone on the ground at its car's door on the side it came in by, <paramref name="out"/> m off the car's side.</summary>
    static PlayerState AtDoor(Night n, Bruin b, double @out = 1, double along = 0)
    {
        var frame = n.Train.Frames[b.Car];
        var at = frame.ToWorld(new Double3(frame.Shape.HalfWidth + @out, 0, along));
        return PlayerMotor.SpawnOnGround(at, n.Train.Line, n.Train.Dynamics.Distance, P);
    }

    static EnemyContext Ctx(Night n)
    {
        var ctx = new EnemyContext { Tuning = n.World.Enemies!, World = n.World };
        foreach (var (id, s) in n.Crew)
            ctx.Crew.Add((new PlayerSnapshot((byte)id, s), default));
        return ctx;
    }

    [Fact]
    public void ItIsOffUntilTheDirectorSaysAndItsCarIsACarOfCargo()
    {
        Assert.False(B.On, "nathaniel2-sloe-forest-heavies: on with the director's answer (the r712-bruin edition until then)");
        var n = new Night(4, 0);
        int car = Bruin.CarFor(n.Train);
        Assert.True(car > 0);
        Assert.NotEqual(Train.CargoKind.None, n.Train.Dynamics.Consist.Vehicles.First(v => v.Id == car).Cargo);
        Assert.NotNull(n.Train.Frames[car].Shape.Interior);
        // Nothing left to eat anywhere: no car for it.
        foreach (var v in n.Train.Vehicles)
            v.CargoIntegrity = 0;
        Assert.Equal(-1, Bruin.CarFor(n.Train));
    }

    [Fact]
    public void ItsRuleWaitsForAWildernessStandAndNeverMeetsAMovingTrain()
    {
        var rule = Spawns.Rules.Single(r => r.Kind == EnemyKind.Bruin);
        double? Weight(Night n) => rule.Weight(new SpawnContext(n.World, n.World.Enemies!, n.World.Director!));
        // At speed: never.
        var moving = new Night(4, 15, enemies: On);
        moving.Run(B.StandSeconds + 30);
        Assert.Null(Weight(moving));
        Assert.DoesNotContain(moving.World.ActiveEnemies, e => e.Kind == EnemyKind.Bruin);
        // Stood: not before standSeconds, then met.
        var stood = new Night(4, 0, enemies: On);
        stood.Run(B.StandSeconds - 5);
        Assert.Equal(StanceKind.Wilderness, stood.World.Director!.Stance);
        Assert.Null(Weight(stood));
        stood.Run(10);
        Assert.True(Weight(stood) > 0 || stood.World.ActiveEnemies.Any(e => e.Kind == EnemyKind.Bruin));
        // Off: never.
        var off = new Night(4, 0, enemies: HoundRunTests.Quiet);
        off.Run(B.StandSeconds + 10);
        Assert.Null(rule.Weight(new SpawnContext(off.World, off.World.Enemies!, off.World.Director!)));
    }

    [Fact]
    public void ItComesOutOfTheTreesHuffingClimbsIntoTheCargoCarAndFeeds()
    {
        var (n, b) = Out();
        Assert.Equal(BruinMode.Come, b.Mode);
        n.Run(1);
        Assert.Equal(SpinePhase.Telegraph, b.Phase);
        // Not in before its huff's had its seconds.
        n.Run(B.HuffSeconds - 2);
        Assert.False(b.InCar);
        In(n, b);
        Assert.Equal(b.Car, b.Attached);
        double cargo = n.Train.Vehicles[b.Car].CargoIntegrity;
        n.Run(10);
        Assert.Equal(cargo - 10 * B.FeedPerSecond, n.Train.Vehicles[b.Car].CargoIntegrity, 3);
        Assert.Equal(BruinMode.Feed, b.Mode);
        Assert.True(b.Working(n.World));
        n.AssertFair();
    }

    [Fact]
    public void AloneInFrontOfItYouAreChargedAndMauledToDeath()
    {
        var (n, b) = Out();
        In(n, b);
        n.Crew[1] = AtDoor(n, b);
        n.Run(B.WindupSeconds + 2);
        Assert.Equal(SpinePhase.Grab, b.Phase);
        Assert.Equal(1, b.Holding);
        Assert.Equal(BruinMode.Maul, b.Mode);
        n.Run(B.MaulSeconds + 1);
        Assert.False(n.Crew[1].Alive);
        Assert.Equal(DeathCause.Mauled, n.Crew[1].Death);
        n.AssertFair();
    }

    [Fact]
    public void FromTheFrontItBarelyFeelsABlowFromTheSideItIsStaggeredAndTurnsOnTheHitter()
    {
        var (n, b) = Out();
        In(n, b);
        n.Crew[1] = AtDoor(n, b);
        // It came in facing into the car: it turns round to them at its turn rate, short of its charge.
        n.Run(2.5);
        Assert.NotEqual(SpinePhase.Grab, b.Phase);
        var me = b.WorldPosition(n.Train);
        Assert.False(Bruin.Flank(me, b.Yaw, PlayerMotor.WorldPosition(n.Crew[1], n.Train), B));
        // Behind it (the car's far end inside).
        n.Crew[2] = PlayerMotor.SpawnOnGround(me - Bruin.Facing(b.Yaw) * 2.5, n.Train.Line, n.Train.Dynamics.Distance, P);
        Assert.True(Bruin.Flank(me, b.Yaw, PlayerMotor.WorldPosition(n.Crew[2], n.Train), B));
        var ctx = Ctx(n);
        double health = b.Health;
        b.Struck(ctx, by: 1, damage: 1);
        Assert.Equal(health - B.FrontFactor, b.Health, 9);
        Assert.Equal(1, b.Target);
        b.Struck(ctx, by: 2, damage: 1);
        Assert.Equal(health - B.FrontFactor - 1, b.Health, 9);
        Assert.Equal(2, b.Target);
        n.Run(0.1);
        Assert.Equal(BruinMode.Stagger, b.Mode);
    }

    [Fact]
    public void AFriendsBlowFromBehindDropsTheMauledAndOneFromTheFrontDoesNot()
    {
        var (n, b) = Out();
        In(n, b);
        n.Crew[1] = AtDoor(n, b);
        n.Run(B.WindupSeconds + 2);
        Assert.Equal(SpinePhase.Grab, b.Phase);
        var me = b.WorldPosition(n.Train);
        // A friend in front of it too (beside the victim): their blow does next to nothing and it keeps hold.
        n.Crew[2] = AtDoor(n, b, along: 0.8);
        b.Struck(Ctx(n), by: 2, damage: 1);
        Assert.Equal(SpinePhase.Grab, b.Phase);
        // From behind: it drops them.
        n.Crew[3] = PlayerMotor.SpawnOnGround(me - Bruin.Facing(b.Yaw) * 2.5, n.Train.Line, n.Train.Dynamics.Distance, P);
        b.Struck(Ctx(n), by: 3, damage: 1);
        Assert.NotEqual(SpinePhase.Grab, b.Phase);
        Assert.Equal(-1, b.Holding);
        Assert.Equal(3, b.Target);
        n.Run(B.MaulSeconds);
        Assert.True(n.Crew[1].Alive);
    }

    [Fact]
    public void EnoughBlowsDriveItOutToTheTreesAndHealthsWorthKillIt()
    {
        var (n, b) = Out();
        In(n, b);
        var ctx = Ctx(n);
        for (int i = 0; i < B.DriveOffBlows; i++)
            b.Struck(ctx, by: 1, damage: 0.01);
        Assert.False(b.InCar);
        Assert.Equal(BruinMode.Leave, b.Mode);
        n.Run(B.ComeFromM / B.Come + 5);
        Assert.True(b.Gone);
        Assert.DoesNotContain(EnemyKind.Bruin, n.World.Slain);
        // A second, struck to death from behind (no crew in the context: a blow it can't place counts as from the front).
        var (m, k) = Out();
        In(m, k);
        var c = new EnemyContext { Tuning = m.World.Enemies!, World = m.World };
        int blows = 0;
        while (!k.Gone && blows < 1000)
        {
            k.Struck(c, by: 1, damage: 1);
            blows++;
        }
        Assert.True(k.Gone);
        Assert.Contains(EnemyKind.Bruin, m.World.Slain);
    }

    [Fact]
    public void TheTrainMovingOffBeforeItsInLeavesItAndAboardItRidesAlongFeeding()
    {
        var (n, b) = Out();
        n.Run(2);
        n.Train.Dynamics.Velocity = 6;
        n.Run(2);
        Assert.Equal(BruinMode.Leave, b.Mode);
        Assert.False(b.InCar);
        var (m, k) = Out();
        In(m, k);
        double cargo = m.Train.Vehicles[k.Car].CargoIntegrity;
        m.Train.Dynamics.Velocity = 12;
        m.Run(10);
        Assert.True(k.InCar);
        Assert.True(m.Train.Vehicles[k.Car].CargoIntegrity < cargo);
    }

    [Fact]
    public void TheSameNightGivesTheSameBruin()
    {
        (Double3, double, double, BruinMode) Night()
        {
            var (n, b) = Out();
            n.Crew[1] = PlayerMotor.SpawnOnGround(b.WorldPosition(n.Train) + new Double3(3, 0, 0), n.Train.Line, n.Train.Dynamics.Distance, P);
            n.Run(30);
            return (b.WorldPosition(n.Train), b.Yaw, n.Train.Vehicles[b.Car].CargoIntegrity, b.Mode);
        }
        Assert.Equal(Night(), Night());
    }
}
