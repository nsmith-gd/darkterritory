using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The v1.1 demo five (GDD v1.1 §21: Track Doll, Car Hugger, Whistler, Tippy Toesie, Ribbits), each against its rule, and
/// App. A.1's GRAB: a held player is freed by a friend, or, alone, by struggling.
/// </summary>
public class DemoRosterTests
{
    static readonly EnemyTuning E = Tuning.Enemies;
    static readonly PlayerTuning P = Tuning.Player;

    static PlayerState OnGroundBeside(Night n, int car, double outward, double along = 0)
    {
        var frame = n.Train.Frames[car];
        var at = frame.ToWorld(new Double3(frame.Shape.HalfWidth + outward, 0, along));
        double s = n.Train.Dynamics.Distance - frame.Shape.HalfLength;
        return PlayerMotor.SpawnOnGround(at, n.Train.Line, s, P);
    }

    /// <summary>Runs until <paramref name="e"/> has hold of someone (at most <paramref name="within"/> seconds).</summary>
    static void UntilGrab(Night n, Enemy e, double within, Func<int, PlayerIntent>? intent = null)
    {
        for (double t = 0; t < within && e.Phase != SpinePhase.Grab; t += 0.1)
            n.Run(0.1, intent);
    }

    /// <summary>Stood in the coupling gap behind a car, on the coupler plate.</summary>
    static PlayerState InGap(Night n, int car, double x = 0)
    {
        var s = PlayerMotor.SpawnOnRoof(n.Train, car, 0, P);
        s.Position = CrewSense.GapLocal(n.Train, car) with { X = x, Y = 0.9 };
        s.Surface = Surface.Coupler;
        return s;
    }

    // ---- Track Doll (App. A.2): stop before you hit the doll.

    [Fact]
    public void StopShortOfTheDollAndItsGoneForTheRun()
    {
        var n = new Night(4, speed: 10);
        var doll = n.World.AddEnemy(id => TrackDoll.Ahead(id, n.Train, 150, E.TrackDoll));
        n.Run(1);
        Assert.Equal(SpinePhase.Telegraph, doll.Phase);
        n.Controls = new TrainControls { Brake = 1, Reverser = 1 };
        n.Run(25, holdSpeed: false);
        Assert.True(doll.Gone);
        Assert.DoesNotContain(n.Events, e => e.Kind == EnemyKind.TrackDoll && e.To == SpinePhase.Punish);
        n.AssertFair();
    }

    [Fact]
    public void HitTheDollAndItHauntsTheTrainAndTakesAnEmptyCab()
    {
        var n = new Night(4, speed: 10);
        var doll = n.World.AddEnemy(id => TrackDoll.Ahead(id, n.Train, 60, E.TrackDoll));
        n.Run(8);
        Assert.True(doll.Haunting);
        Assert.True(doll.Attached > 0, $"in car {doll.Attached}");
        // Nobody in the cab: before long it's at the controls.
        n.Run(E.TrackDoll.TamperAfterEmpty + 1);
        Assert.True(doll.Tampering);
        // Someone gets back in the cab and it's off to a car again.
        n.Crew[1] = PlayerMotor.SpawnInCab(n.Train, P);
        n.Run(0.5);
        Assert.False(doll.Tampering);
        Assert.True(doll.Attached > 0);
        n.AssertFair();
    }

    // ---- Car Hugger (App. A.3): cut the caboose or kill it.

    static (Night Night, CarHugger Hugger, int Rear) Latched()
    {
        var n = new Night(4, speed: 8);
        var hugger = n.World.AddEnemy(id => CarHugger.Lurking(id, n.Train.Dynamics.RearDistance + 1, 1, E.CarHugger));
        n.Run(0.5);
        return (n, hugger, n.Train.Dynamics.Consist.Vehicles[^1].Id);
    }

    [Fact]
    public void TheCarHuggerLatchesOnTheRearCarCapsTheSpeedAndEatsIt()
    {
        var (n, hugger, rear) = Latched();
        Assert.True(hugger.Latched);
        Assert.Equal(rear, hugger.Attached);
        Assert.Equal(rear, hugger.Drags);
        double shell = n.Train.Vehicles[rear].Integrity, loot = n.Train.Vehicles[rear].CargoIntegrity;
        n.Run(10);
        Assert.True(n.Train.Vehicles[rear].Integrity < shell);
        Assert.True(n.Train.Vehicles[rear].CargoIntegrity < loot);
        n.AssertFair();
    }

    [Fact]
    public void CutTheCabooseAndTheCarHuggerGoesWithIt()
    {
        var (n, hugger, rear) = Latched();
        n.Train.Uncouple(n.Train.VehicleAhead(rear));
        n.Run(0.2);
        Assert.True(hugger.Gone);
    }

    [Fact]
    public void SwallowedAtTheRearAFriendPullsThemOut()
    {
        var (n, hugger, rear) = Latched();
        // Out on the rear platform, in front of its mouth.
        var s = PlayerMotor.SpawnOnRoof(n.Train, rear, 0, P);
        s.Position = hugger.Local - new Double3(0, 0.1, 0.9);
        s.Surface = Surface.Deck;
        n.Crew[1] = s;
        UntilGrab(n, hugger, E.MinReactionSeconds + 1);
        Assert.Equal(SpinePhase.Grab, hugger.Phase);
        Assert.True(n.Crew[1].Has(PlayerFlags.Held));
        n.Crew[2] = n.Crew[1] with { Position = n.Crew[1].Position + new Double3(0.4, 0, -0.6) };
        n.Run(0.3, id => id == 2 ? new PlayerIntent { Buttons = PlayerButtons.Use } : default);
        Assert.NotEqual(SpinePhase.Grab, hugger.Phase);
        Assert.True(n.Crew[1].Alive);
    }

    // ---- Whistler (App. A.4): check the gaps after the whistle; move in pairs at stops.

    [Fact]
    public void AtAStopTheWhistleBlowsWithNobodyOnTheCord()
    {
        var n = new Night(4, speed: 0);
        var w = n.World.AddEnemy(id => Whistler.InGap(id, n.Train, 2, E.Whistler));
        n.Run(E.Whistler.WhistleAfterStop + 0.2);
        Assert.Equal(SpinePhase.Telegraph, w.Phase);
        Assert.True(n.World.WhistleSeconds > 0);
        Assert.True(w.Whistling);
        n.Run(E.Whistler.WhistleSeconds + 0.5);
        Assert.Equal(SpinePhase.Commit, w.Phase);
        n.AssertFair();
    }

    [Fact]
    public void AloneAtItsGapYoureCarriedOffAndInAPairYoureNot()
    {
        var n = new Night(4, speed: 0);
        var w = n.World.AddEnemy(id => Whistler.InGap(id, n.Train, 2, E.Whistler));
        n.Run(E.Whistler.WhistleAfterStop + E.Whistler.WhistleSeconds + 1);
        Assert.Equal(SpinePhase.Commit, w.Phase);
        var gap = n.Train.Frames[2].ToWorld(CrewSense.GapLocal(n.Train, 2));
        // Two together at the gap (not looking at it): it takes nobody.
        n.Crew[1] = InGap(n, 2, -0.4);
        n.Crew[2] = InGap(n, 2, 0.4);
        n.Run(1);
        Assert.Equal(SpinePhase.Commit, w.Phase);
        // One of them wanders off: the other's alone at the gap, and snatched.
        n.Crew.Remove(2);
        n.Run(0.5);
        Assert.Equal(SpinePhase.Grab, w.Phase);
        Assert.Equal(1, w.Holding);
        n.Run(5);
        var carried = PlayerMotor.WorldPosition(n.Crew[1], n.Train);
        Assert.True((carried - gap).Length > 10, $"only {(carried - gap).Length:0.0} m from the gap");
        n.AssertFair();
    }

    // ---- Tippy Toesie (App. A.5): don't stand still alone.

    [Fact]
    public void StandStillAloneAndItTiptoesUpAndCoversYourMouth()
    {
        var n = new Night(4, speed: 8);
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, 2, 0, P);
        n.Crew[2] = PlayerMotor.SpawnInCab(n.Train, P);
        var tippy = n.World.AddEnemy(id => TippyToesie.Hiding(id, E.TippyToesie));
        n.Run(E.TippyToesie.IdleSeconds + 0.5, id => id == 2 ? new PlayerIntent { MoveX = 0.01f } : default);
        Assert.Equal(SpinePhase.Telegraph, tippy.Phase);
        UntilGrab(n, tippy, E.TippyToesie.StartBehind / E.TippyToesie.ApproachSpeed + 3, id => id == 2 ? new PlayerIntent { MoveX = 0.01f } : default);
        Assert.Equal(SpinePhase.Grab, tippy.Phase);
        Assert.True(n.World.VoiceEffect(1).Muffled);
        n.Run(E.TippyToesie.SuffocateSeconds + 0.5, id => id == 2 ? new PlayerIntent { MoveX = 0.01f } : default);
        Assert.Equal(DeathCause.Suffocated, n.Crew[1].Death);
        n.AssertFair();
    }

    [Fact]
    public void TurnAndLookAtItAndItFlees()
    {
        var n = new Night(4, speed: 8);
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, 2, 0, P);
        n.Crew[2] = PlayerMotor.SpawnInCab(n.Train, P);
        var tippy = n.World.AddEnemy(id => TippyToesie.Hiding(id, E.TippyToesie));
        n.Run(E.TippyToesie.IdleSeconds + 2, id => id == 2 ? new PlayerIntent { MoveX = 0.01f } : default);
        Assert.True(tippy.Phase is SpinePhase.Telegraph or SpinePhase.Commit, $"{tippy.Phase}");
        n.Crew[1] = n.Crew[1] with { Yaw = n.Crew[1].Yaw + Math.PI };
        n.Run(0.2, id => id == 2 ? new PlayerIntent { MoveX = 0.01f } : default);
        Assert.True(tippy.Hidden);
        Assert.True(n.Crew[1].Alive);
    }

    // ---- Ribbits (App. A.6): never be outnumbered.

    [Fact]
    public void OutnumberedOnTheGroundTheRibbitsFreezeAndEatYou()
    {
        var n = new Night(4, speed: 0);
        var me = OnGroundBeside(n, 2, 4);
        n.Crew[1] = me;
        var at = PlayerMotor.WorldPosition(me, n.Train);
        var a = n.World.AddEnemy(id => Ribbit.At(id, id, at + new Double3(9, 0, 0), E.Ribbits));
        n.World.AddEnemy(id => Ribbit.At(id, a.Id, at + new Double3(9, 0, 1.5), E.Ribbits));
        UntilGrab(n, a, 8);
        Assert.Contains(n.Events, e => e.Kind == EnemyKind.Ribbit && e.To == SpinePhase.Telegraph);
        Assert.Equal(SpinePhase.Grab, a.Phase);
        n.Run(E.Ribbits.DevourSeconds + 1);
        Assert.Equal(DeathCause.Devoured, n.Crew[1].Death);
        n.AssertFair();
    }

    [Fact]
    public void AFriendEvensTheCountAndTheTongueLetsGo()
    {
        var n = new Night(4, speed: 0);
        var me = OnGroundBeside(n, 2, 4);
        n.Crew[1] = me;
        var at = PlayerMotor.WorldPosition(me, n.Train);
        var a = n.World.AddEnemy(id => Ribbit.At(id, id, at + new Double3(9, 0, 0), E.Ribbits));
        n.World.AddEnemy(id => Ribbit.At(id, a.Id, at + new Double3(9, 0, 1.5), E.Ribbits));
        UntilGrab(n, a, 8);
        Assert.Equal(SpinePhase.Grab, a.Phase);
        n.Crew[2] = me with { Position = me.Position + new Double3(0, 0, 3) };
        n.Run(0.5);
        Assert.NotEqual(SpinePhase.Grab, a.Phase);
        Assert.True(n.Crew[1].Alive);
    }

    [Fact]
    public void TwoTogetherAreIgnoredByAPackOfTwo()
    {
        var n = new Night(4, speed: 0);
        var me = OnGroundBeside(n, 2, 4);
        n.Crew[1] = me;
        n.Crew[2] = me with { Position = me.Position + new Double3(0, 0, 2) };
        var at = PlayerMotor.WorldPosition(me, n.Train);
        var a = n.World.AddEnemy(id => Ribbit.At(id, id, at + new Double3(9, 0, 0), E.Ribbits));
        n.World.AddEnemy(id => Ribbit.At(id, a.Id, at + new Double3(9, 0, 1.5), E.Ribbits));
        n.Run(10);
        Assert.DoesNotContain(n.Events, e => e.Kind == EnemyKind.Ribbit && e.To == SpinePhase.Telegraph);
    }

    // ---- Solo (T89): with nobody to help, the held can struggle free.

    [Fact]
    public void AloneAndHeldYouCanStruggleFree()
    {
        var n = new Night(4, speed: 0);
        var me = OnGroundBeside(n, 2, 4);
        n.Crew[1] = me;
        var at = PlayerMotor.WorldPosition(me, n.Train);
        var a = n.World.AddEnemy(id => Ribbit.At(id, id, at + new Double3(9, 0, 0), E.Ribbits));
        n.World.AddEnemy(id => Ribbit.At(id, a.Id, at + new Double3(9, 0, 1.5), E.Ribbits));
        UntilGrab(n, a, 8);
        Assert.Equal(SpinePhase.Grab, a.Phase);
        Assert.True(E.Grab.SoloStruggle < E.Ribbits.DevourSeconds);
        n.Run(E.Grab.SoloStruggle + 0.3, _ => new PlayerIntent { Buttons = PlayerButtons.Use });
        Assert.NotEqual(SpinePhase.Grab, a.Phase);
        Assert.True(n.Crew[1].Alive);
        Assert.False(n.Crew[1].Has(PlayerFlags.Held));
    }
}
