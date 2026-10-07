using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The director's per-creature boarding rules (GDD App. F, 6 Oct 2026; ARCHITECTURE §8 note 269): Cinder Hounds that board
/// stay aboard, eating the supplies and setting the car alight; Fire Flies come only to a stopped train.
/// </summary>
public class BoardingRuleTests
{
    static readonly EnemyTuning E = Tuning.Enemies;

    /// <summary>A hound just behind the rear car of a train it can catch: it howls, then boards.</summary>
    static CinderHound Boarded(Night n)
    {
        var h = n.World.AddEnemy(id => new CinderHound(id, 1)
        {
            LineDistance = n.Train.Dynamics.RearDistance - 10,
            Lateral = 4,
            Height = 0.6,
            Health = E.CinderHounds.Health,
        });
        n.Run(E.CinderHounds.HowlSeconds + 1);
        Assert.Equal(n.Train.Dynamics.Consist.Vehicles[^1].Id, h.Attached);
        return h;
    }

    [Fact]
    public void CinderHoundsThatBoardStayAboardEatingTheSuppliesAndSettingTheCarAlight()
    {
        // "Cinder Hounds that board stay aboard. They keep setting the car alight while they eat the supplies." Nobody comes
        // for it: past v1.1's drop-off, and past the director's linger and stuck dismissals, it's still there.
        var n = new Night(4, speed: 14);
        var hound = Boarded(n);
        int rear = hound.Attached;
        bool alight = false;
        double seconds = Math.Max(E.CinderHounds.BoredSeconds, E.Director.LingerSeconds * 1.5) + 30;
        for (int s = 0; s < seconds; s++)
        {
            n.Run(1);
            alight |= n.World.ActiveEnemies.Any(e => e is CarFire { Gone: false } f && f.Attached == rear);
        }
        Assert.False(hound.Gone, $"{hound.Phase}");
        Assert.Equal(rear, hound.Attached);
        Assert.True(hound.StaysAboard);
        Assert.True(alight);
        Assert.True(n.Train.Vehicles[rear].CargoIntegrity < 1);
    }

    [Fact]
    public void ABoardedHoundStartsTheFireOnlyOnceItsBeenLeftAloneThatLong()
    {
        var n = new Night(4, speed: 14);
        var hound = Boarded(n);
        n.Run(E.CinderHounds.IgniteEverySeconds - 2);
        Assert.DoesNotContain(n.World.ActiveEnemies, e => e is CarFire { Gone: false } f && f.Attached == hound.Attached);
        n.Run(3);
        Assert.Contains(n.World.ActiveEnemies, e => e is CarFire { Gone: false } f && f.Attached == hound.Attached);
    }

    [Fact]
    public void CutLooseWithItsCarTheHoundGoes()
    {
        // The counters: kill it (the pack fight's blows), or cut its car loose.
        var n = new Night(4, speed: 14);
        var hound = Boarded(n);
        Assert.True(n.Train.Uncouple(n.Train.VehicleAhead(hound.Attached)));
        n.Run(1);
        Assert.True(hound.Gone);
    }

    [Fact]
    public void WithStayAboardOffItDropsOffAsInV11()
    {
        var t = E with { CinderHounds = E.CinderHounds with { StayAboard = false } };
        var n = new Night(4, speed: 14);
        n.World.EnableEnemies(t, null, 1, crew: 4, authority: true);
        var hound = Boarded(n);
        n.Run(E.CinderHounds.BoredSeconds + 1);
        Assert.True(hound.Gone);
        Assert.False(hound.StaysAboard);
    }

    [Fact]
    public void TheDirectorSendsFireFliesOnlyToAStoppedTrain()
    {
        // "Car lamps start lit. Their pull on Fire Flies is rare, and only while the car is stopped."
        var rule = Spawns.For(EnemyKind.FireFlies)!;
        var moving = new Night(5, speed: E.FireFlies.StoppedBelow + 0.5);
        Assert.True(moving.Train.Vehicles[3].LampLit);
        Assert.Null(rule.Weight(new SpawnContext(moving.World, E, moving.World.Director!)));
        var stopped = new Night(5, speed: 0);
        Assert.Equal(E.FireFlies.StoppedWeight, rule.Weight(new SpawnContext(stopped.World, E, stopped.World.Director!)));
        Assert.True(E.FireFlies.StoppedWeight < 1); // rare
    }

    [Fact]
    public void FireFliesOnALampLeaveOnceTheTrainGetsUnderWay()
    {
        var n = new Night(5, speed: 0);
        var flies = n.World.AddEnemy(id => FireFlies.OnLamp(id, n.Train, 3));
        n.Run(E.FireFlies.IgniteSeconds / 2);
        Assert.Equal(SpinePhase.Telegraph, flies.Phase);
        n.Train.Dynamics.Velocity = E.FireFlies.PullAwaySpeed + 0.5;
        n.Run(E.FireFlies.PullAwaySeconds + 0.5);
        Assert.True(flies.Gone);
        Assert.DoesNotContain(n.World.ActiveEnemies, e => e is CarFire { Gone: false });
        Assert.True(n.Train.Vehicles[3].LampLit);
    }

    [Fact]
    public void LeftStoppedTheFliesSetTheCarAlight()
    {
        var n = new Night(5, speed: 0);
        var flies = n.World.AddEnemy(id => FireFlies.OnLamp(id, n.Train, 3));
        n.Run(E.FireFlies.IgniteSeconds + 1);
        Assert.True(flies.Gone);
        Assert.Contains(n.World.ActiveEnemies, e => e is CarFire { Gone: false } f && f.Attached == 3);
    }
}
