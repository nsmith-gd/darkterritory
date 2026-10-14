using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;
using RunState = DarkTerritory.Sim.Run.Run;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The Wakers (GDD §8; docs/design/creatures/wakers.md; note 588; the director, 9 Oct 2026: "The gargantuan creatures, the
/// picking up of the train, especially with you in it"): still out at dawn, they get up behind the train, run it down, lift it
/// and eat it from the rear, crew and all; in under the terminus's guns, the crew's home.
/// </summary>
public class WakersTests
{
    static readonly EnemyTuning E = Tuning.Enemies;
    static readonly WakersTuning W = E.Wakers;
    static readonly EnemyTuning Quiet = E with { Director = E.Director with { GraceMinSeconds = 1e9, GraceMaxSeconds = 1e9 } };
    static readonly Route.Route Frontier = RouteGenerator.Generate(Tuning.Route, RouteTier.Frontier, 7) with { DawnSeconds = 3 };

    sealed class Night
    {
        public readonly World World;
        public readonly RunState Run;
        public PlayerState Player;
        public TrainControls Controls = new() { Reverser = 1, Brake = 1 };

        public Night(double front)
        {
            var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), Frontier.Build(), front, Tuning.Boiler);
            World = new World(train, Tuning.Combat);
            World.EnableRun(Tuning.Run, Frontier, 600, authority: true);
            World.EnableEnemies(Quiet, null, 1, crew: 1, authority: true);
            Run = World.Run!;
            Player = PlayerMotor.SpawnInCab(train, Tuning.Player);
        }

        public Waker? Lead => World.ActiveEnemies.OfType<Waker>().FirstOrDefault(w => w.Lead);

        public void Step(double seconds)
        {
            for (int i = 0; i < seconds * SimConstants.TickRate && !Run.Over; i++)
            {
                World.BeginTick();
                World.CrewAct(ref Player, default, 1);
                World.Step(Controls);
                World.ApplyDamage(id => Player, (_, s) => Player = s, [1]);
                PlayerMotor.Step(ref Player, default, World.Train, Tuning.Player, Tuning.Train, SimConstants.TickSeconds, applyLook: false);
                World.StepRun([Player]);
            }
        }
    }

    [Fact]
    public void ATrainStillOutAtDawnIsRunDownLiftedAndEatenWithItsCrew()
    {
        Assert.True(W.Enabled);
        var n = new Night(front: 4000);
        n.Step(4);
        var lead = Assert.Single(n.World.ActiveEnemies.OfType<Waker>(), w => w.Lead);
        Assert.Equal(n.World.Train.RearDistance - W.RiseBehind, lead.LineDistance, 0.5);
        // Up out of the land, then after the train: faster than any engine once it's going.
        Assert.True(W.TopSpeed > Tuning.Train.MaxSpeed);
        n.Step(W.RiseSeconds + 75);
        Assert.True(n.Lead!.Holds, $"{n.World.Train.RearDistance - n.Lead.LineDistance:0} m short of the train");
        Assert.True(n.Player.Alive); // in the cab: the engine goes last
        n.Step(W.EatAfter + 10 * W.EatSeconds);
        Assert.True(n.Run.Over);
        Assert.Equal(RunEnd.DawnMissed, n.Run.End);
        Assert.False(n.Player.Alive);
        Assert.Equal(DeathCause.Woken, n.Player.Death);
    }

    [Fact]
    public void WhoeverRanIsPickedUpByAnotherRisenBesideThemAndEaten()
    {
        // Note 710 (the director, 14 Oct 2026: "It should capture players who escape the train and eat them too"): the train
        // held, a crewmate off it out in the field has a Waker rise beside them (the telegraph), stride over, take them in its
        // hand, lift them to its mouth and eat them, long before the train's eaten down to the engine.
        var n = new Night(front: 4000);
        n.Step(W.RiseSeconds + 80);
        Assert.True(n.Lead!.Holds);
        var f = n.World.Train.Frames[0];
        var out_ = f.ToWorld(new Ballast.Double3(f.Shape.HalfWidth + 30, 0, 0));
        n.Player = PlayerMotor.SpawnOnGround(out_, n.World.Train.Line, n.World.Train.Dynamics.Distance, Tuning.Player);
        n.Step(0.5);
        var picker = Assert.Single(n.World.ActiveEnemies.OfType<Waker>(), w => w.Picker);
        Assert.Equal(1, picker.Quarry);
        Assert.Equal(SpinePhase.Telegraph, picker.Phase);
        Assert.True(((picker.Local - out_) with { Y = 0 }).Length >= W.PickAside - 1, "it rises beside them, out in the field");
        Assert.True(n.Player.Alive);
        // Up (no sooner than anyone can react), across, and in its hand.
        n.Step(Math.Max(E.MinReactionSeconds, W.PickRiseSeconds) + (W.PickAside - W.PickReach) / W.PickSpeed + 1);
        Assert.Equal(SpinePhase.Grab, picker.Phase);
        Assert.Equal(1, picker.Holding);
        Assert.True(n.Player.Has(PlayerFlags.Held));
        // Lifted to its mouth...
        n.Step(W.PickLiftSeconds * 0.9);
        double ground = n.World.Train.Line.Sample(n.World.Train.Dynamics.Path, n.World.Train.Dynamics.Distance).Position.Y;
        Assert.True(n.Player.Position.Y - ground > W.PickMouthHeight * 0.6, $"{n.Player.Position.Y - ground:0} m up");
        // ...and eaten, while the train's still being eaten from the rear.
        n.Step(W.PickLiftSeconds * 0.2);
        Assert.False(n.Player.Alive);
        Assert.Equal(DeathCause.Woken, n.Player.Death);
        Assert.False(n.World.TakenAtDawn);
    }

    [Fact]
    public void NobodyOffTheTrainBeforeItsHeldIsTakenAndNoneRiseForThem()
    {
        // Note 710: the pickers are the held train's: off it with the Wakers still coming, nothing rises for you yet.
        var n = new Night(front: 4000);
        n.Step(W.RiseSeconds + 10);
        Assert.False(n.Lead!.Holds);
        var f = n.World.Train.Frames[0];
        n.Player = PlayerMotor.SpawnOnGround(f.ToWorld(new Ballast.Double3(f.Shape.HalfWidth + 30, 0, 0)), n.World.Train.Line,
            n.World.Train.Dynamics.Distance, Tuning.Player);
        n.Step(5);
        Assert.DoesNotContain(n.World.ActiveEnemies.OfType<Waker>(), w => w.Picker);
        Assert.True(n.Player.Alive);
    }

    [Fact]
    public void TheyCatchAStandingTrainInAboutAMinuteAndNotBefore()
    {
        // wakers.md §6: stood still at dawn, caught in about 70 s; a lead that's honest, since a crew can see it coming.
        var n = new Night(front: 4000);
        double caught = -1;
        for (int i = 0; i < 200 && caught < 0; i++)
        {
            n.Step(0.5);
            if (n.Lead is { Holds: true })
                caught = n.Run.Seconds - Frontier.DawnSeconds;
        }
        Assert.InRange(caught, 55, 95);
    }

    [Fact]
    public void UnderTheTerminusGunsTheyStopShortAndTheCrewIsHome()
    {
        // Stood just outside the gate (clearing the train before it goes in, #322/#323): they come no closer than stopShort.
        double gate = Frontier.Length - Tuning.Run.TerminusZone;
        var n = new Night(front: gate - 20);
        Assert.True(n.World.Train.RearDistance > gate - W.StopShort);
        n.Step(W.RiseSeconds + 150);
        Assert.False(n.Run.Over);
        Assert.True(n.Player.Alive);
        var lead = n.Lead!;
        Assert.False(lead.Holds);
        Assert.Equal(gate - W.StopShort, lead.LineDistance, 0.5);
    }

    [Fact]
    public void TheirPaceBuildsToTopSpeedAndHoldsIt()
    {
        // startSpeed rising evenly to topSpeed over rampSeconds, then topSpeed: a crew can reckon it, and so can the bots.
        Assert.Equal(0, Covered(0));
        Assert.Equal(W.StartSpeed * W.RampSeconds + 0.5 * (W.TopSpeed - W.StartSpeed) * W.RampSeconds, Covered(W.RampSeconds), 1e-6);
        Assert.Equal(Covered(W.RampSeconds) + W.TopSpeed * 10, Covered(W.RampSeconds + 10), 1e-6);
    }

    [Fact]
    public void TheSecondsInHandAreWhenTheyCatchIt()
    {
        // Note 607 (shared with D1.5's #337): the reckoning a driver and the hands go by. A standing train, from just after
        // dawn: the seconds it gives are when the lead Waker takes it, to within a tick or two.
        var n = new Night(front: 4000);
        n.Step(1);
        double inHand = Waker.SecondsInHand(n.World);
        Assert.True(inHand > W.RiseSeconds && inHand < 600, $"{inHand:0.0}");
        double before = n.Run.Seconds;
        while (!(n.Lead?.Holds ?? false) && n.Run.Seconds < before + 600)
            n.Step(0.1);
        Assert.Equal(inHand, n.Run.Seconds - before, 0.5);
        Assert.Equal(0, Waker.SecondsInHand(n.World));
    }

    [Fact]
    public void ATrainStoodAtDawnIsCaughtWhenTheGraceSaysAndARunningOneLater()
    {
        // Note 621: the driver's deadline is the dawn and the Wakers' catch of a stood train after it, not the dawn alone. It's
        // the same reckoning as a stood train's seconds in hand (they're seen from dawn here, not from now).
        var n = new Night(front: 4000);
        n.Step(1);
        double fromDawn = Waker.SecondsInHand(n.World) - n.Run.DawnIn;
        Assert.Equal(fromDawn, Waker.CatchAfterDawn(W, 0), 0.5);
        Assert.InRange(Waker.CatchAfterDawn(W, 0), 60, 90);
        Assert.True(Waker.CatchAfterDawn(W, 10) > Waker.CatchAfterDawn(W, 0) + 20);
        Assert.True(double.IsPositiveInfinity(Waker.CatchAfterDawn(W, W.TopSpeed)));
    }

    [Fact]
    public void TheSecondsInHandReckonedAtAStandAreTheSoonest()
    {
        // Note 631: the harness's closest call is a train's seconds in hand had it stood just then (speed 0), the soonest they
        // catch it; reckoned running, later, and never at their top speed.
        var n = new Night(front: 4000);
        n.Step(1);
        double stood = Waker.SecondsInHand(n.World);
        Assert.Equal(stood, Waker.SecondsInHand(n.World, speed: 0), 1e-9);
        Assert.True(Waker.SecondsInHand(n.World, speed: 10) > stood + 20);
        Assert.True(double.IsPositiveInfinity(Waker.SecondsInHand(n.World, speed: W.TopSpeed)));
    }

    [Fact]
    public void UnderTheGunsTheShelterIsReachedAndOutOnTheLineItIsnt()
    {
        var home = new Night(front: Frontier.Length - 20);
        Assert.Equal(0, Waker.SecondsToShelter(home.World));
        var out_ = new Night(front: 4000);
        Assert.True(double.IsPositiveInfinity(Waker.SecondsToShelter(out_.World))); // stood: it never gets there
    }

    static double Covered(double s) => Waker.Covered(s, W);
}
