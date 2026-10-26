using Ballast;
using DarkTerritory.Sim.Audit;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 1051 (queue #750): `dt social`'s failures taken up (GDD §20; note 1038). The Knotter's taut hold and the Whistler's
/// carry behind their flags, off (the director's questions, note 1050 Q1 and Q2), and the bots' errand to fight the Stoker
/// from the firebox door while the driver keeps the controls.
/// </summary>
public class SocialFailsTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly AuditContent C = new(Tuning.Train, Tuning.Player, Tuning.Boiler, Tuning.Combat, Tuning.Enemies, Tuning.Run,
        DataFile.Load<BalanceTuning>(Path.Combine(Content, BalanceTuning.File)));
    static readonly PlayerTuning P = Tuning.Player;

    [Fact]
    public void TheFlagsAreOffInTheGame()
    {
        Assert.False(Tuning.Enemies.Knotter.TautHold.On);
        Assert.False(Tuning.Enemies.Whistler.Carry.On);
        Assert.Equal(Tuning.Enemies.Knotter.CoilSeconds, Tuning.Enemies.Knotter.HoldSeconds);
    }

    [Fact]
    public void TheKnottersTautHoldGivesAFriendEightSeconds()
    {
        // Off, a slip is pulled under coilSeconds (1.5 s) after it coils; on, the line holds them tautHold.seconds first.
        var off = GrabAudit.Check(C, EnemyKind.Knotter, 1);
        Assert.True(off.Grabbed);
        Assert.Equal(Tuning.Enemies.Knotter.CoilSeconds, off.WindowSeconds, 2);
        var on = C with { Enemies = Tuning.Enemies with { Knotter = Tuning.Enemies.Knotter with { TautHold = Tuning.Enemies.Knotter.TautHold with { On = true } } } };
        var alone = GrabAudit.Check(on, EnemyKind.Knotter, 1);
        Assert.Equal(8, alone.WindowSeconds, 2);
        Assert.False(alone.Freed);
        Assert.InRange(alone.HeldSeconds, 7.9, 8.1);
        // A friend at the gap still pulls them up.
        Assert.True(GrabAudit.Check(on, EnemyKind.Knotter, 2).Freed);
    }

    [Fact]
    public void TheWhistlersCarryTakesTwentySeconds()
    {
        var w = Tuning.Enemies.Whistler;
        // Off, the run and the nest's seconds (the audit's 30 m run: 26.7 s, past §20's 20 s).
        var off = GrabAudit.Check(C, EnemyKind.Whistler, 1);
        Assert.True(off.WindowSeconds > 20);
        Assert.Equal(w.NestDistance / w.RunSpeed + w.NestSeconds, off.WindowSeconds, 1);
        var on = C with { Enemies = Tuning.Enemies with { Whistler = w with { Carry = w.Carry with { On = true } } } };
        var alone = GrabAudit.Check(on, EnemyKind.Whistler, 1);
        Assert.Equal(20, alone.WindowSeconds, 2);
        Assert.False(alone.Freed);
        Assert.True(GrabAudit.Check(on, EnemyKind.Whistler, 2).Freed);
        // A run longer than the carry: it's eaten on arrival, never short of the nest.
        var carry = w with { Carry = w.Carry with { On = true } };
        Assert.Equal(25, carry.CarrySeconds(25));
        Assert.Equal(20, carry.CarrySeconds(6));
        Assert.Equal(6 + w.NestSeconds, w.CarrySeconds(6));
    }

    [Fact]
    public void AWalkerGoesForwardAndHosesTheStokerFromTheFireboxDoor()
    {
        // Note 1051: the Stoker in the fire and a driver at the controls; the walker on car 2's roof goes forward over the
        // cars, down the cab's hatch, to the fireman's side of the door, and puts the cab's hose on it.
        var n = new Night(4, speed: 8, boiler: true);
        n.Train.Boiler.Pressure = 80;
        n.Train.Boiler.Firebox = 5;
        var calls = new CrewCalls();
        var cab = PlayerMotor.SpawnInCab(n.Train, P);
        calls.Say(0, StopJob.Driver, cab);
        n.Crew[1] = cab;
        n.Crew[2] = PlayerMotor.SpawnOnRoof(n.Train, 2, 0, P);
        var walker = new RoofWalkerBot(21) { Me = 2, Calls = calls, Crew = [(1, cab)] };
        var stoker = n.World.AddEnemy(id => Stoker.InFirebox(id, n.Train, false, Tuning.Enemies.Stoker));
        bool went = false;
        for (int s = 0; s < 60 * SimConstants.TickRate && !stoker.Gone; s++)
        {
            n.Run(SimConstants.TickSeconds, id => id == 2 ? walker.Decide(n.Crew[2], n.World, n.World.Tick, out _) : default);
            went |= walker.Stokering;
        }
        var me = n.Crew[2];
        Assert.True(went);
        Assert.True(stoker.Gone, $"{stoker.Phase}; the walker at {me.Parent}/{me.Surface}, in the cab {PlayerMotor.InCab(me, n.Train)}");
        Assert.True(PlayerMotor.InCab(me, n.Train));
        Assert.Equal(P.Health, me.Health);
        Assert.False(n.Train.Boiler.Ruptured);
        Assert.Equal(2, calls.StokerHand);
        // Once it's gone the claim's let go, and the walker heads back out of the cab.
        n.Run(SimConstants.TickSeconds, id => id == 2 ? walker.Decide(n.Crew[2], n.World, n.World.Tick, out _) : default);
        Assert.Null(calls.StokerHand);
        Assert.False(walker.Stokering);
    }

    [Fact]
    public void TheDriverLeavesTheStokerToAHandAtTheDoorAndKeepsTheControls()
    {
        // The Stoker crossing the footplate from the tender: alone, the driver goes to the door to club it, the train on the
        // brake; with a crewmate at the fireman's side of the door, it keeps driving (and brakes only once it's in the fire).
        foreach (bool hand in new[] { false, true })
        {
            var n = new Night(4, speed: 8, boiler: true);
            n.Train.Boiler.Pressure = 80;
            n.Train.Boiler.Firebox = 5;
            var cab = PlayerMotor.SpawnInCab(n.Train, P);
            n.Crew[1] = cab;
            var spot = Heed.FiremansSpot(n.Train);
            var mate = cab with { Position = spot with { Y = cab.Position.Y } };
            Assert.True(PlayerMotor.InCab(mate, n.Train));
            if (hand)
                n.Crew[2] = mate;
            var driver = new ConductorBot(null, 0) { Crewmates = hand ? [mate] : [] };
            var stoker = n.World.AddEnemy(id => Stoker.AtTender(id, n.Train, Tuning.Enemies.Stoker));
            bool braked = false, swung = false;
            for (int s = 0; s < 3 * SimConstants.TickRate && !stoker.Gone && stoker.Phase != SpinePhase.Commit; s++)
                n.Run(SimConstants.TickSeconds, id =>
                {
                    if (id != 1)
                        return default;
                    var i = driver.Decide(n.Crew[1], n.World, n.World.Tick, out _);
                    braked |= i.Buttons.HasFlag(PlayerButtons.Brake);
                    swung |= i.Actions.HasFlag(PlayerActions.Swing);
                    return i;
                }, holdSpeed: false);
            Assert.Equal(!hand, braked);
            Assert.False(hand && swung);
        }
    }
}
