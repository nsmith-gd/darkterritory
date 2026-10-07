using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Boarding-first across the roster (the director's decisions of 6 Oct 2026, GDD App. F.1; ARCHITECTURE §8 note 286):
/// "Nothing acts inside the train unless it boarded." "Slowing opens the doors. Stops, facilities and tight curves are where
/// things board." "Shut doors stop some boarders, not all." One test, or a few, for each rule note 286 changed.
/// </summary>
public class BoardingFirstTests
{
    static readonly EnemyTuning E = Tuning.Enemies;

    static double? Weight(Night n, EnemyKind kind) => Spawns.For(kind)!.Weight(new SpawnContext(n.World, E, n.World.Director!));

    // ---- Climbers: a grip only on a slow train; more on a tight bend --------------------------------------------------

    [Fact]
    public void ClimbersPacingATrainTooFastToMountRunAlongsideAndGiveItUp()
    {
        var n = new Night(5, speed: E.Climbers.MountBelow + 2);
        var c = n.World.AddEnemy(id => Climber.Pacing(id, n.Train, 3, 1, E.Climbers));
        bool scrabbled = false;
        for (int s = 0; s < E.Climbers.PaceSeconds + E.Climbers.WaitForSlowSeconds + 2; s++)
        {
            n.Run(1);
            scrabbled |= c.Phase is SpinePhase.Telegraph or SpinePhase.Commit;
        }
        Assert.False(scrabbled, "it got a grip at speed");
        Assert.True(c.Gone);
        Assert.True(c.Attached < 0);
    }

    [Fact]
    public void ClimbersPacingATrainThatSlowsGetOn()
    {
        // The telegraph is the pack beside the train; the driver who eases under the speed opens the door.
        var n = new Night(5, speed: E.Climbers.MountBelow + 2);
        var c = n.World.AddEnemy(id => Climber.Pacing(id, n.Train, 3, 1, E.Climbers));
        n.Run(E.Climbers.PaceSeconds + 4);
        Assert.Equal(SpinePhase.Dormant, c.Phase);
        n.Train.Dynamics.Velocity = E.Climbers.MountBelow - 3;
        n.Run(E.Climbers.ScrabbleSeconds + 3);
        Assert.True(c.Attached >= 0, $"{c.Phase}");
        n.AssertFair();
    }

    [Fact]
    public void TheDirectorSendsClimbersRarelyAtSpeedAndMoreSlowOnATightBend()
    {
        int gaps = 4;
        double full = E.Climbers.PerGapWeight * gaps;
        Assert.Equal(full * E.Climbers.AtSpeedWeight, Weight(new Night(5, speed: E.Climbers.MountBelow + 1), EnemyKind.Climber));
        Assert.True(E.Climbers.AtSpeedWeight < 1);
        Assert.Equal(full, Weight(new Night(5, speed: E.Climbers.MountBelow - 2), EnemyKind.Climber));
        // A line with a bend sharper than tightBendRadius just ahead of the train.
        var bent = new Night(5, speed: E.Climbers.MountBelow - 2, line: new RailLine(new LineDefinition("bend",
            [new TrackSegment(2_100), new TrackSegment(400, E.Climbers.TightBendRadius - 50), new TrackSegment(20_000)])));
        Assert.True(Boarding.OnTightBend(bent.Train, E.Climbers.TightBendRadius, E.Climbers.BendAheadM));
        Assert.Equal(full * E.Climbers.BendWeight, Weight(bent, EnemyKind.Climber));
        Assert.True(E.Climbers.BendWeight > 1);
    }

    /// <summary>Lit cars, a Climber mounting at the gap behind car 3 on a slow train, nobody aboard: where does it get in?</summary>
    static Climber MountsAndGoesIn(Night n)
    {
        var c = n.World.AddEnemy(id => Climber.Pacing(id, n.Train, 3, 1, E.Climbers));
        for (int s = 0; s < 120 && !c.Inside && !c.Gone; s++)
            n.Run(1);
        return c;
    }

    [Fact]
    public void WithTheDirectorsFlagALitCarShutUpTightKeepsClimbersOutAndAnOpenDoorLetsThemIn()
    {
        // enemies.json climbers.litShutCarKeepsOut, off by default (the director's call, note 286).
        var on = E with { Climbers = E.Climbers with { LitShutCarKeepsOut = true } };
        var shut = new Night(5, speed: 8, enemies: on);
        var over = MountsAndGoesIn(shut);
        Assert.True(over.Inside, $"{over.Phase}");
        Assert.Equal(0, over.Attached); // over every lit, shut car to the cab
        Assert.False(shut.Train.Vehicles[3].Breached);

        var open = new Night(5, speed: 8, enemies: on);
        open.Train.Vehicles[3].ToggleDoor(0);
        var inside = MountsAndGoesIn(open);
        Assert.True(inside.Inside);
        Assert.Equal(3, inside.Attached);
    }

    [Fact]
    public void ADarkCarShutUpTheyStillForceThroughTheRoof()
    {
        var n = new Night(5, speed: 8);
        n.Train.Vehicles[3].LampLit = false;
        var c = MountsAndGoesIn(n);
        Assert.Equal(3, c.Attached);
        Assert.True(n.Train.Vehicles[3].Breached);
    }

    // ---- Draggers: under a car only at a stop or a slow bend --------------------------------------------------------

    [Fact]
    public void DraggersGetUnderTheCarsOnlyOnASlowTrainAndWaitForSomeoneUpTop()
    {
        var fast = new Night(5, speed: E.Draggers.BoardBelow + 2);
        fast.Crew[1] = PlayerMotor.SpawnOnRoof(fast.Train, 2, 0, Tuning.Player);
        fast.Run(0.2);
        Assert.Null(Weight(fast, EnemyKind.Dragger));

        var slow = new Night(5, speed: E.Draggers.BoardBelow - 4);
        slow.Run(0.2);
        Assert.Equal(1, Weight(slow, EnemyKind.Dragger)); // nobody up yet: it can still get on, to wait
        Assert.True(Spawns.For(EnemyKind.Dragger)!.Spawn(new SpawnContext(slow.World, E, slow.World.Director!)));
        var d = Assert.Single(slow.World.ActiveEnemies.OfType<Dragger>());
        slow.Run(5);
        Assert.Equal(SpinePhase.Dormant, d.Phase);
        Assert.True(d.Attached > 0);
    }

    // ---- Tippy Toesie: aboard at a stop ------------------------------------------------------------------------------

    [Fact]
    public void TippyToesieSlipsAboardOnlyAtAStop()
    {
        var n = new Night(5, speed: 0);
        n.Crew[1] = InCar(n, 1);
        n.Crew[2] = InCar(n, 4);
        n.Run(E.TippyToesie.IdleSeconds + 1);
        Assert.NotNull(Weight(n, EnemyKind.TippyToesie));
        n.Train.Dynamics.Velocity = 5;
        n.Run(0.5);
        Assert.Null(Weight(n, EnemyKind.TippyToesie));
    }

    static PlayerState InCar(Night n, int car)
    {
        var room = n.Train.Frames[car].Shape.Interior!.Value;
        var s = PlayerMotor.SpawnOnRoof(n.Train, car, 0, Tuning.Player);
        s.Position = room.Centre with { Y = room.Min.Y };
        s.Surface = Surface.Deck;
        return s;
    }

    // ---- Fire Flies: a shut car keeps them off its lamp --------------------------------------------------------------

    [Fact]
    public void AStoppedTrainShutUpTightDrawsNoFireFliesAndAnOpenLitCarDoes()
    {
        var n = new Night(5, speed: 0);
        Assert.True(n.Train.Vehicles[3].LampLit);
        Assert.Null(Weight(n, EnemyKind.FireFlies));
        n.Train.Vehicles[3].ToggleDoor(1);
        Assert.Equal(E.FireFlies.StoppedWeight, Weight(n, EnemyKind.FireFlies));
        Assert.True(Spawns.For(EnemyKind.FireFlies)!.Spawn(new SpawnContext(n.World, E, n.World.Director!)));
        Assert.Equal(3, Assert.Single(n.World.ActiveEnemies.OfType<FireFlies>()).Attached);
    }
}
