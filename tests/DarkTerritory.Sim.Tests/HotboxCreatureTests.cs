using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Hotbox (GDD §21, App. A.3, B.3; the director's brief of 8 Oct 2026; ARCHITECTURE §8 note 367). Rule: hear the knock, find
/// the wheel, stop to pull it. It knocks, glows, then seizes the axle (the car drags the train to a crawl; never fire, never a
/// derailment); only at a stand is it out where it can be killed or prised; it bites, never kills; a seized axle stays
/// seized until it's repaired with a wrench.
/// </summary>
public class HotboxCreatureTests
{
    static readonly HotboxTuning H = Tuning.Enemies.Hotbox;
    static readonly PlayerTuning P = Tuning.Player;

    static Hotbox In(Night n, int car = 2, int side = 1) => n.World.AddEnemy(id => Hotbox.In(id, n.Train, car, rear: true, side, H));

    /// <summary>Standing on the ground beside its truck, facing it.</summary>
    static PlayerState AtIt(Night n, Hotbox h, double off = 1.0, Tool tool = Tool.Crowbar)
    {
        var at = h.WorldPosition(n.Train);
        var frame = n.Train.Frames[h.Attached];
        var stand = at + frame.Right * (h.Side * off);
        var s = PlayerMotor.SpawnOnGround(stand, n.Train.Line, n.Train.Dynamics.Distance, P);
        var d = at - stand;
        s.Yaw = DMath.Atan2(-d.X, -d.Z);
        return s with { Kit = Kit.Of([tool]), HeldSlot = 0 };
    }

    [Fact]
    public void ItKnocksThenGlowsThenSeizesTheAxle()
    {
        var n = new Night(4, speed: H.RefSpeed);
        var h = In(n);
        n.Run(H.KnockSeconds - 5);
        Assert.Equal(HotboxMode.Knock, h.Mode);
        Assert.False(n.Train.Vehicles[2].Seized);
        n.Run(10);
        Assert.Equal(HotboxMode.Glow, h.Mode);
        n.Run(H.GlowSeconds);
        Assert.Equal(HotboxMode.Seized, h.Mode);
        Assert.True(n.Train.Vehicles[2].Seized);
        Assert.False(n.World.Derailed);
        Assert.DoesNotContain(n.World.ActiveEnemies, e => e.Kind == EnemyKind.CarFire && !e.Gone);
        // Its knock is once a turn of the wheel: faster the faster you run.
        Assert.True(Hotbox.KnocksPerSecond(H, 20) > Hotbox.KnocksPerSecond(H, 10));
    }

    [Fact]
    public void ASeizedAxleHoldsTheTrainToACrawlsTopSpeed()
    {
        var n = new Night(4, speed: 15);
        n.Train.Vehicles[2].Seized = true;
        n.Controls = new TrainControls { Throttle = 1, Reverser = 1 };
        n.Run(90, holdSpeed: false);
        Assert.InRange(n.Train.Dynamics.Speed, 0, H.SeizedTopSpeed + 0.5);
        // Freed, it runs up again.
        n.Train.Vehicles[2].Seized = false;
        n.Run(30, holdSpeed: false);
        Assert.True(n.Train.Dynamics.Speed > H.SeizedTopSpeed + 2);
    }

    [Fact]
    public void OnlyAtAStandIsItOutWhereABlowLands()
    {
        var n = new Night(4, speed: 12);
        var h = In(n);
        n.Run(5);
        Assert.Equal(0, h.MeleeRadius);
        Assert.False(h.Exposed);
        n.Train.Dynamics.Velocity = 0;
        n.Run(H.ExposeAfter + 0.5);
        Assert.Equal(HotboxMode.Unfolded, h.Mode);
        Assert.True(h.Exposed);
        // The train moving again: it folds back in, its heat kept.
        double heat = h.Heat;
        n.Train.Dynamics.Velocity = 8;
        n.Run(0.5);
        Assert.Equal(HotboxMode.Knock, h.Mode);
        Assert.True(h.Heat >= heat);
    }

    [Fact]
    public void KilledByBlowsAtAStand()
    {
        var n = new Night(4, speed: 0);
        var h = In(n);
        n.Run(H.ExposeAfter + 0.5);
        n.Crew[1] = AtIt(n, h, off: 1.6);
        for (int i = 0; i < 12 && !h.Gone; i++)
            n.Run(Tuning.Enemies.Melee.SwingSeconds + 0.05, id => id == 1 ? new PlayerIntent { Actions = PlayerActions.Swing } : default);
        Assert.True(h.Gone);
        Assert.True(n.Crew[1].Alive);
    }

    [Fact]
    public void PrisedOutWithACrowbarHeldAtIt()
    {
        var n = new Night(4, speed: 0);
        var h = In(n);
        n.Run(H.ExposeAfter + 0.5);
        n.Crew[1] = AtIt(n, h, off: 1.0);
        n.Run(H.PriseSeconds + 0.2, id => id == 1 ? new PlayerIntent { Buttons = PlayerButtons.Use } : default);
        Assert.Equal(HotboxMode.Prised, h.Mode);
        n.Run(H.ScuttleSeconds + 0.5);
        Assert.True(h.Gone);
    }

    [Fact]
    public void ItBitesWhoeverIsAtItsHeadButNeverKills()
    {
        var n = new Night(4, speed: 0);
        var h = In(n);
        n.Run(H.ExposeAfter + 0.5);
        n.Crew[1] = AtIt(n, h, off: 0.8) with { Health = 20 };
        n.Run(30);
        Assert.True(n.Crew[1].Alive);
        Assert.Equal(1, n.Crew[1].Health);
        n.AssertFair();
    }

    [Fact]
    public void ASeizedAxleStaysSeizedUntilAWrenchFreesIt()
    {
        var n = new Night(4, speed: 0);
        n.Train.Vehicles[2].Seized = true;
        var h = In(n);
        n.Run(H.ExposeAfter + 0.5);
        n.Crew[1] = AtIt(n, h, off: 1.0);
        n.Run(H.PriseSeconds + H.ScuttleSeconds + 1, id => id == 1 ? new PlayerIntent { Buttons = PlayerButtons.Use } : default);
        Assert.True(h.Gone);
        Assert.True(n.Train.Vehicles[2].Seized);
        // A wrench held at the truck.
        n.Crew[1] = n.Crew[1] with { Kit = Kit.Of([Tool.Wrench]), HeldSlot = 0 };
        n.Run(H.RepairSeconds + 0.5, id => id == 1 ? new PlayerIntent { Buttons = PlayerButtons.Use } : default);
        Assert.False(n.Train.Vehicles[2].Seized);
    }
}
