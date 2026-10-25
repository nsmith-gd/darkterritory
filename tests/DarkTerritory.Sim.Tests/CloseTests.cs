using Ballast;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The Close (GDD §21; the director's playtest of 23 Oct 2026, line 36: "if everybody's crowded into one car ... a clear tell
/// and a clear counter"; Sloe's docs/design/creatures/the-close.md; ARCHITECTURE §8 note 1013). Rule: when the roof bows,
/// someone get out. A crowd shut in one car its settle and it lies on the roof and bows it (the tell); it pours in and pins
/// them, crushes them, and the weakest dies at its kill. A blow from outside or a gun's round bursts it; a door opened lets
/// them out one a second, and fewer than the crowd and it lets go. Off, nothing.
/// </summary>
public class CloseTests
{
    static readonly EnemyTuning E = Tuning.Enemies;
    static readonly CloseTuning T = E.Close;
    static readonly PlayerTuning P = Tuning.Player;
    static readonly EnemyTuning On = HoundRunTests.Quiet with { Close = T with { On = true } };
    const int Car = 2;

    /// <summary>Crewmate <paramref name="id"/> on <see cref="Car"/>'s floor, <paramref name="z"/> along it.</summary>
    static PlayerState Inside(Night n, double z, double x = 0, int health = 0)
    {
        var room = n.Train.Frames[Car].Shape.Interior!.Value;
        return new PlayerState
        {
            Parent = Car,
            Position = new Double3(x, room.Min.Y, z),
            Surface = Surface.Deck,
            Health = health > 0 ? health : P.Health,
            LineHint = n.Train.Cars[Car].FrontDistance,
        };
    }

    /// <summary>A train at 15 m/s with three of a crew of four shut in car 2 (crewmate 2 the weakest), their doors shut.</summary>
    static Night Crowded(EnemyTuning? enemies = null)
    {
        var n = new Night(4, 15, enemies: enemies ?? On);
        n.Train.Vehicles[Car].DoorsOpen = 0;
        n.Crew[1] = Inside(n, -1);
        n.Crew[2] = Inside(n, 0, health: P.Health / 2);
        n.Crew[3] = Inside(n, 1);
        Assert.All(n.Crew.Values, s => Assert.True(Close.InCar(s, n.Train, Car)));
        return n;
    }

    static (Night N, Close Close) Settled()
    {
        var n = Crowded();
        var close = n.World.AddEnemy(id => Close.Over(id, Car, n.Train, T));
        return (n, close);
    }

    static EnemyContext Ctx(Night n)
    {
        var ctx = new EnemyContext { Tuning = n.World.Enemies!, World = n.World };
        foreach (var (id, s) in n.Crew)
            ctx.Crew.Add((new PlayerSnapshot((byte)id, s), default));
        return ctx;
    }

    static double? Weight(Night n) =>
        Spawns.Rules.Single(r => r.Kind == EnemyKind.Close).Weight(new SpawnContext(n.World, n.World.Enemies!, n.World.Director!));

    static double Settle => MooseTuning.ByTier(T.Settle, Route.RouteTier.Frontier);
    static double CrushAfter => MooseTuning.ByTier(T.CrushAfter, Route.RouteTier.Frontier);

    [Fact]
    public void ItIsOffUntilTheDirectorSaysAndOffNothingComes()
    {
        Assert.False(T.On, "Sloe's design (note 1004) awaits the director's verdict: on with it (the r716-close edition until then)");
        var n = Crowded(HoundRunTests.Quiet);
        n.Run(Settle + 10);
        Assert.Null(Weight(n));
        Assert.Equal(0, n.World.Director!.Crowding.Of(Car));
        Assert.DoesNotContain(n.World.ActiveEnemies, e => e.Kind == EnemyKind.Close);
    }

    [Fact]
    public void ACrowdShutInOneCarItsSettleCallsItAndAnOpenDoorOrAThinnedCarDoesNot()
    {
        var n = Crowded();
        n.Run(Settle - 3);
        Assert.Null(Weight(n));
        n.Run(5);
        Assert.True(Weight(n) > 0, $"crowded {n.World.Director!.Crowding.Of(Car)} s");
        // A door open: not shut in, and the count starts again.
        n.Train.Vehicles[Car].ToggleDoor(0);
        n.Run(2);
        Assert.Null(Weight(n));
        Assert.Equal(0, n.World.Director!.Crowding.Of(Car));
        // Two of a crew of four in one car aren't a crowd (the third up on the roof).
        var two = Crowded();
        two.Crew[3] = PlayerMotor.SpawnOnRoof(two.Train, Car, 0, P);
        two.Crew[4] = PlayerMotor.SpawnOnRoof(two.Train, Car, 2, P);
        two.Run(Settle + 5);
        Assert.Null(Weight(two));
        // Stood: never.
        var stood = Crowded();
        stood.Train.Dynamics.Velocity = 0;
        stood.Run(Settle + 5);
        Assert.Null(Weight(stood));
    }

    [Fact]
    public void ItBowsTheRoofItsTellThenPoursInAndPinsThemTheWeakestHeld()
    {
        var (n, c) = Settled();
        n.Run(0.1);
        Assert.Equal((CloseMode.Bow, SpinePhase.Telegraph), (c.Mode, c.Phase));
        n.Run(T.BowSeconds - 0.6);
        Assert.Equal(CloseMode.Bow, c.Mode);
        Assert.DoesNotContain(n.Crew.Values, s => s.Has(PlayerFlags.Held));
        n.Run(1);
        Assert.Equal((CloseMode.Pour, SpinePhase.Commit), (c.Mode, c.Phase));
        n.Run(T.PourSeconds);
        Assert.Equal((CloseMode.Pin, SpinePhase.Grab), (c.Mode, c.Phase));
        Assert.Equal(2, c.Holding);
        n.Run(0.2);
        Assert.All(n.Crew.Values, s => Assert.True(s.Has(PlayerFlags.Held)));
        n.AssertFair();
    }

    [Fact]
    public void PinnedTheyCantWalkTheCrushComesAndTheWeakestDiesAtItsKill()
    {
        var (n, c) = Settled();
        n.Run(T.BowSeconds + T.PourSeconds + 0.2);
        Assert.Equal(CloseMode.Pin, c.Mode);
        var at = n.Crew[1].Position;
        n.Run(CrushAfter - 0.5, _ => new PlayerIntent { MoveZ = 1 });
        Assert.True((n.Crew[1].Position - at).Length < 0.05, "pinned, nobody walks");
        Assert.Equal(P.Health, n.Crew[1].Health);
        n.Run(2);
        Assert.True(n.Crew[1].Health < P.Health, "crushed past crushAfter");
        n.Run(T.KillSeconds - CrushAfter);
        Assert.False(n.Crew[2].Alive);
        Assert.Equal(DeathCause.Crushed, n.Crew[2].Death);
        Assert.True(n.Crew[1].Alive && n.Crew[3].Alive);
        Assert.True(c.Gone);
        n.Run(0.2);
        Assert.DoesNotContain(n.Crew.Values, s => s.Alive && s.Has(PlayerFlags.Held));
        n.AssertFair();
    }

    [Fact]
    public void AFriendOnTheRoofBurstsItWithBlowsAndBlowsFromInsideDoNothing()
    {
        var (n, c) = Settled();
        n.Run(T.BowSeconds + T.PourSeconds + 1);
        Assert.Equal(CloseMode.Pin, c.Mode);
        // From inside: into its thickness.
        c.Struck(Ctx(n), by: 1, damage: 1);
        Assert.Equal(T.Health, c.Health);
        // The fourth, on the roof, a bot's legs and arms: up to it, and clubbing.
        n.Crew[4] = PlayerMotor.SpawnOnRoof(n.Train, Car, n.Train.Frames[Car].Shape.HalfLength - 1, P);
        n.Run(8, id => id == 4 ? Heed.TheClose(n.Crew[4], n.World) ?? default : default);
        Assert.True(c.Gone);
        Assert.Equal(CloseMode.Burst, c.Mode);
        Assert.DoesNotContain(EnemyKind.Close, n.World.Slain);
        Assert.All(n.Crew.Values, s => Assert.True(s.Alive));
        n.Run(0.2);
        Assert.DoesNotContain(n.Crew.Values, s => s.Has(PlayerFlags.Held));
    }

    [Fact]
    public void AGunsRoundBurstsIt()
    {
        var (n, c) = Settled();
        n.Run(T.BowSeconds + T.PourSeconds + 1);
        Assert.True(c.Hit(Ctx(n), by: 4, damage: 1));
        Assert.True(c.Gone);
        n.Run(0.2);
        Assert.DoesNotContain(n.Crew.Values, s => s.Has(PlayerFlags.Held));
    }

    [Fact]
    public void ThePinnedAgainstTheDoorOpenItAndGetOutAndItLetsGo()
    {
        var n = Crowded();
        // Crewmate 3 stood at the front end door, where the bot's own way out would take it.
        var shape = n.Train.Frames[Car].Shape;
        int front = shape.DoorList.Single(d => d.Box.Centre.Z < -1).Index;
        var (stand, _) = WarmUp.Inside(shape, front);
        n.Crew[3] = Inside(n, stand.Z, stand.X);
        var c = n.World.AddEnemy(id => Close.Over(id, Car, n.Train, T));
        n.Run(T.BowSeconds + T.PourSeconds + 0.5);
        Assert.Equal(CloseMode.Pin, c.Mode);
        Assert.True(n.Crew[3].Has(PlayerFlags.Held));
        // Use still works the door it's against (and nothing else: Use there anywhere else is the struggle's).
        Assert.True(Close.DoorInReach(n.Crew[3], n.World));
        Assert.False(Close.DoorInReach(n.Crew[1], n.World));
        n.Run(4, id => id == 3 ? Heed.TheClose(n.Crew[3], n.World) ?? default : default);
        Assert.True(n.Train.Vehicles[Car].DoorOpen(front));
        Assert.True(c.Gone);
        Assert.All(n.Crew.Values, s => Assert.True(s.Alive));
        n.Run(0.2);
        Assert.DoesNotContain(n.Crew.Values, s => s.Has(PlayerFlags.Held));
        n.AssertFair();
    }

    [Fact]
    public void WhenTheRoofBowsBotsGetOutAndItSlidesOffWithoutPinningAnyone()
    {
        var (n, c) = Settled();
        n.Run(T.BowSeconds - 0.5, id => Heed.TheClose(n.Crew[id], n.World) ?? default);
        Assert.True(c.Gone);
        Assert.Equal(SpinePhase.Gone, c.Phase);
        Assert.DoesNotContain(n.Events, e => e.EnemyId == c.Id && e.To is SpinePhase.Commit or SpinePhase.Grab);
        Assert.True(n.Crew.Values.Count(s => Close.InCar(s, n.Train, Car)) < T.CrowdFor(4));
        Assert.All(n.Crew.Values, s => Assert.True(s.Alive));
    }

    [Fact]
    public void TheSameNightGivesTheSameClose()
    {
        (CloseMode, SpinePhase, int, int) Night()
        {
            var (n, c) = Settled();
            n.Run(T.BowSeconds + T.PourSeconds + CrushAfter + 2);
            return (c.Mode, c.Phase, c.Holding, n.Crew[1].Health);
        }
        Assert.Equal(Night(), Night());
    }
}
