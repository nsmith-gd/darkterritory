using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The Tapper (GDD §21; the director's playtest of 23 Oct 2026, line 36: "I am sitting on the back gun alone for too long";
/// Sloe's docs/design/creatures/tapper.md; ARCHITECTURE §8 note 1011). Rule: when it taps, look round; or have someone at your
/// back. It comes only at speed for a gunner alone on their gun its dwell (the behaviour reader's gun alone, note 999 §2b), over
/// the roof behind them; it taps, and a few seconds on lifts them off the train. A look holds it off, company sends it over
/// the side, a blow knocks it off and a friend's drops whoever it's lifting.
/// </summary>
public class TapperTests
{
    static readonly EnemyTuning E = Tuning.Enemies;
    static readonly TapperTuning T = E.Tapper;
    static readonly PlayerTuning P = Tuning.Player;
    static readonly EnemyTuning On = HoundRunTests.Quiet with { Tapper = T with { On = true } };

    static int GuardCar(Night n) => n.Train.Vehicles.First(v => v.Kind == VehicleKind.Guard).Id;

    /// <summary>Crewmate <paramref name="id"/> sat at the guard car's gun, facing the way it faces (back down the line).</summary>
    static void Gunner(Night n, int id = 1)
    {
        int car = GuardCar(n);
        var mount = n.Train.Frames[car].Shape.Gun!.Value;
        var s = PlayerMotor.SpawnOnRoof(n.Train, car, mount.Position.Z - mount.Facing.Z * 0.7, P);
        s.Yaw = mount.Facing.Z < 0 ? 0 : Math.PI;
        s.Flags |= PlayerFlags.Seated;
        n.Crew[id] = s;
    }

    /// <summary>A train at 15 m/s with a gunner on the rear gun, and a Tapper come over the guard car's roof for them.</summary>
    static (Night N, Tapper Tapper) Over(double speed = 15)
    {
        var n = new Night(4, speed, enemies: On);
        Gunner(n);
        int car = GuardCar(n);
        var tapper = n.World.AddEnemy(id => Tapper.Over(id, car, n.Crew[1].Position, 1, n.Train, T));
        return (n, tapper);
    }

    /// <summary>Keeps the gunner seated through the motor's steps (a test's crewmate presses nothing).</summary>
    static void Run(Night n, double seconds, bool keepSeated = true)
    {
        for (int i = 0; i < seconds * SimConstants.TickRate; i++)
        {
            if (keepSeated && n.Crew.TryGetValue(1, out var s) && s.Alive && !s.Has(PlayerFlags.Held))
                n.Crew[1] = s with { Flags = s.Flags | PlayerFlags.Seated };
            n.Run(SimConstants.TickSeconds);
        }
    }

    [Fact]
    public void ItIsOffUntilTheDirectorSaysAndTheGuardsGunFacesBack()
    {
        Assert.False(T.On, "nathaniel2-sloe-forest-heavies: on with the director's answer (the r714-tapper edition until then)");
        var n = new Night(4, 15);
        Assert.True(n.Train.Frames[GuardCar(n)].Shape.Gun!.Value.Facing.Z > 0);
    }

    [Fact]
    public void ItComesOverTheRoofFromBehindTheGunnerTapsAndThenLiftsThemOffTheTrain()
    {
        var (n, t) = Over();
        // Behind a gunner facing back down the line is toward the car's front (−Z).
        Assert.True(t.Local.Z < n.Crew[1].Position.Z);
        Assert.Equal(TapperMode.Crawl, t.Mode);
        Run(n, T.ComeFromM / T.Crawl + 1);
        Assert.Equal(TapperMode.Tap, t.Mode);
        Assert.Equal(SpinePhase.Telegraph, t.Phase);
        Run(n, MooseTuning.ByTier(T.TapSeconds, Route.RouteTier.Frontier) + 0.5);
        Assert.Equal(SpinePhase.Grab, t.Phase);
        Assert.Equal(1, t.Holding);
        Run(n, T.LiftSeconds + 1, keepSeated: false);
        Assert.False(n.Crew[1].Alive);
        Assert.Equal(DeathCause.Dropped, n.Crew[1].Death);
        Assert.True(t.Gone);
        n.AssertFair();
    }

    [Fact]
    public void LookingRoundAtItHoldsItOff()
    {
        var (n, t) = Over();
        Run(n, T.ComeFromM / T.Crawl + 1);
        Assert.Equal(TapperMode.Tap, t.Mode);
        // The gunner gets up out of the seat (seated, the view's held inside the gun's arc, and the rear gun's doesn't reach
        // behind) and turns round to it.
        var s = n.Crew[1];
        n.Crew[1] = s with { Yaw = s.Yaw + Math.PI, Flags = s.Flags & ~PlayerFlags.Seated };
        Run(n, T.LookSeconds + 0.5, keepSeated: false);
        Assert.Equal(TapperMode.Flinch, t.Mode);
        Assert.NotEqual(SpinePhase.Grab, t.Phase);
        Run(n, 8, keepSeated: false);
        Assert.NotEqual(SpinePhase.Grab, t.Phase);
        Assert.True(n.Crew[1].Alive);
    }

    [Fact]
    public void CompanyOnTheCarSendsItOverTheSide()
    {
        var (n, t) = Over();
        Run(n, 2);
        int car = GuardCar(n);
        n.Crew[2] = PlayerMotor.SpawnOnRoof(n.Train, car, 0, P);
        Run(n, 0.5);
        Assert.True(t.Gone);
        Assert.DoesNotContain(EnemyKind.Tapper, n.World.Slain);
    }

    [Fact]
    public void ABlowKnocksItOffAndAFriendsDropsTheGunnerOnTheRoof()
    {
        var (n, t) = Over();
        Run(n, T.ComeFromM / T.Crawl + 1 + MooseTuning.ByTier(T.TapSeconds, Route.RouteTier.Frontier) + 0.5);
        Assert.Equal(SpinePhase.Grab, t.Phase);
        var ctx = new EnemyContext { Tuning = n.World.Enemies!, World = n.World };
        foreach (var (id, s) in n.Crew)
            ctx.Crew.Add((new PlayerSnapshot((byte)id, s), default));
        t.Struck(ctx, by: 2, damage: 1);
        Assert.True(t.Gone);
        Assert.Equal(-1, t.Holding);
        Assert.DoesNotContain(EnemyKind.Tapper, n.World.Slain);
        Run(n, T.LiftSeconds, keepSeated: false);
        Assert.True(n.Crew[1].Alive);
        // Health's worth of blow at once kills it.
        var (m, u) = Over();
        u.Struck(new EnemyContext { Tuning = m.World.Enemies!, World = m.World }, by: 2, damage: T.Health);
        Assert.True(u.Gone);
        Assert.Contains(EnemyKind.Tapper, m.World.Slain);
    }

    [Fact]
    public void TheTrainSlowingSendsItOff()
    {
        var (n, t) = Over();
        Run(n, 1);
        n.Train.Dynamics.Velocity = T.LeaveBelow - 1;
        Run(n, 0.5);
        Assert.True(t.Gone);
    }

    [Fact]
    public void ItsRuleWantsSpeedAndAGunnerAloneItsDwellAndNeverAStoodTrain()
    {
        var rule = Spawns.Rules.Single(r => r.Kind == EnemyKind.Tapper);
        double? Weight(Night n) => rule.Weight(new SpawnContext(n.World, n.World.Enemies!, n.World.Director!));
        double dwell = MooseTuning.ByTier(T.Dwell, Route.RouteTier.Frontier);
        var moving = new Night(4, 15, enemies: On);
        Gunner(moving);
        Run(moving, dwell - 5);
        Assert.Null(Weight(moving));
        Run(moving, 10);
        Assert.True(Weight(moving) > 0 || moving.World.ActiveEnemies.Any(e => e.Kind == EnemyKind.Tapper));
        // Stood: never.
        var stood = new Night(4, 0, enemies: On);
        Gunner(stood);
        Run(stood, dwell + 10);
        Assert.Null(Weight(stood));
        // Company on the gun car's roof: not alone, never.
        var two = new Night(4, 15, enemies: On);
        Gunner(two);
        two.Crew[2] = PlayerMotor.SpawnOnRoof(two.Train, GuardCar(two), 0, P);
        Run(two, dwell + 10);
        Assert.Null(Weight(two));
        // Off: never.
        var off = new Night(4, 15, enemies: HoundRunTests.Quiet);
        Gunner(off);
        Run(off, dwell + 10);
        Assert.Null(rule.Weight(new SpawnContext(off.World, off.World.Enemies!, off.World.Director!)));
    }

    [Fact]
    public void TheSameNightGivesTheSameTapper()
    {
        (double, TapperMode, SpinePhase) Night()
        {
            var (n, t) = Over();
            Run(n, 12);
            return (t.Local.Z, t.Mode, t.Phase);
        }
        Assert.Equal(Night(), Night());
    }
}
