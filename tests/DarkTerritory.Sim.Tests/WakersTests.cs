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

    /// <summary>
    /// Note 902 (the director's playtest of 16 Oct 2026, line 29): he stops behind the train, not over it, and takes it a car at
    /// a time in his hands: the last car goes up in his grab, each after it in its turn as the one before is eaten, three bites
    /// a car. Whoever's come down off the car in his hands once it's up, on the ground where it stood, fell from it; down
    /// beside a car still on the rails, nobody did.
    /// </summary>
    [Fact]
    public void HeStopsBehindTheTrainLiftsACarAtATimeAndAFallFromItKills()
    {
        var n = new Night(front: 4000);
        while (!(n.Lead?.Holds ?? false) && n.Run.Seconds < 600)
            n.Step(0.05);
        var lead = n.Lead!;
        Assert.True(lead.Holds);
        Assert.Equal(n.World.Train.RearDistance - W.CatchShort, lead.LineDistance, 1.5); // (the train dragged on a tick since)
        int cars = n.World.Train.Dynamics.Consist.Vehicles.Count;
        // His hands on the last car, then it up: nothing else off the rails.
        Assert.Equal(0, lead.Lifted(W, cars));
        Assert.Equal(1, Waker.LiftedAt(W.EatAfter - 0.01, W, cars));
        Assert.Equal(1, Waker.LiftedAt(Waker.TurnStart(1, W) + 0.01, W, cars)); // the last eaten, his hands going down for the next
        Assert.Equal(2, Waker.LiftedAt(Waker.TurnStart(1, W) + W.ScoopShare * W.EatSeconds, W, cars));
        Assert.Equal(3, Waker.BitesACar(W));
        // Down beside the last car, now, while it's still on the rails: no fall.
        var last = n.World.Train.Frames[n.World.Train.Dynamics.Consist.Vehicles[^1].Id];
        var below = last.ToWorld(new Ballast.Double3(last.Shape.HalfWidth + 1, 0, 0));
        n.Player = PlayerMotor.SpawnOnGround(below, n.World.Train.Line, n.World.Train.Dynamics.Distance, Tuning.Player);
        n.Step(0.2);
        Assert.True(n.Player.Alive, $"{n.Player.Death}");
        // Once it's up, they fell from it.
        n.Step(W.ScoopFrom + (W.EatAfter - W.ScoopHold - W.ScoopFrom) * W.FallFrom + 0.5);
        Assert.False(n.Player.Alive);
        Assert.Equal(DeathCause.Fell, n.Player.Death);
    }

    [Fact]
    public void DownBesideACarStillOnTheRailsNobodyFalls()
    {
        var n = new Night(front: 4000);
        while (!(n.Lead?.Holds ?? false) && n.Run.Seconds < 600)
            n.Step(0.05);
        n.Step(W.EatAfter - W.ScoopHold);
        var f = n.World.Train.Frames[n.World.Train.Dynamics.Consist.Vehicles[^3].Id];
        n.Player = PlayerMotor.SpawnOnGround(f.ToWorld(new Ballast.Double3(f.Shape.HalfWidth + 1, 0, 0)), n.World.Train.Line,
            n.World.Train.Dynamics.Distance, Tuning.Player);
        n.Step(1);
        Assert.NotEqual(DeathCause.Fell, n.Player.Death);
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

    /// <summary>Holds the train for up to <paramref name="seconds"/>: stepped till the lead has it.</summary>
    static bool CaughtWithin(Night n, double seconds)
    {
        for (double s = 0; s < seconds && !(n.Lead?.Holds ?? false) && !n.Run.Over; s += 0.1)
            n.Step(0.1);
        return n.Lead?.Holds ?? false;
    }

    [Fact]
    public void HomeThroughTheGateTheyStopShortAndTheCrewIsHome()
    {
        // In through the gate, the last car and all (note 940: the walls the only safety): they come no closer than stopShort.
        var probe = new Night(front: 4000);
        double gate = Waker.Gate(probe.World, W), length = probe.World.Train.Dynamics.Consist.LengthMetres;
        var n = new Night(front: gate + length + 20);
        Assert.True(n.World.Train.RearDistance > gate);
        n.Step(W.RiseSeconds + 150);
        Assert.False(n.Run.Over);
        Assert.True(n.Player.Alive);
        var lead = n.Lead!;
        Assert.False(lead.Holds);
        Assert.Equal(gate - W.StopShort, lead.LineDistance, 0.5);
    }

    /// <summary>
    /// Note 940 (the director's playtest of 20 Oct 2026, evening, line 24: "unless I'm behind those walls fully, it should
    /// really make a good effort to try and get me"; "it sees me 158 meters behind, and it's not coming any closer"): stood
    /// outside the gate, inside where they used to stop, the train's still theirs.
    /// </summary>
    [Fact]
    public void StoodJustShortOfTheGateTheyComeOnAndTakeIt()
    {
        var probe = new Night(front: 4000);
        double gate = Waker.Gate(probe.World, W), length = probe.World.Train.Dynamics.Consist.LengthMetres;
        var n = new Night(front: gate + length - 30);
        Assert.True(n.World.Train.RearDistance > gate - W.StopShort && n.World.Train.RearDistance < gate);
        Assert.True(CaughtWithin(n, W.RiseSeconds + 150), $"{n.World.Train.RearDistance - n.Lead!.LineDistance:0} m behind");
        Assert.Equal(n.World.Train.RearDistance - W.CatchShort, n.Lead!.LineDistance, 1.5);
        Assert.True(Waker.SecondsToShelter(n.World) > 0);
    }

    /// <summary>
    /// Note 940 (line 26: "pick up the train while it's moving"): a train run flat out is run down, taken going, and brought to
    /// a stand in his hands before the last car's up off the rails.
    /// </summary>
    [Fact]
    public void ATrainTakenGoingIsBroughtToAStandInHisHands()
    {
        var n = new Night(front: 2500) { Controls = new TrainControls { Reverser = 1, Throttle = 1, Brake = 0 } };
        Assert.True(CaughtWithin(n, W.RiseSeconds + 400));
        double at = n.World.Train.Dynamics.Speed;
        Assert.True(at > 5, $"taken at {at:0.0} m/s");
        n.Step(W.ScoopFrom + 0.4 * (W.EatAfter - W.ScoopHold - W.ScoopFrom));
        Assert.True(Math.Abs(n.World.Train.Dynamics.Speed) < 0.3, $"{n.World.Train.Dynamics.Speed:0.0} m/s, its last car on its way up");
    }

    /// <summary>
    /// Note 940 (line 26: "I'm going straight between its legs. It does not know how to deal with me"): backed into him, the
    /// train's his as it comes, not past him; and at the walls, a train backed out of the gate again is his too.
    /// </summary>
    [Fact]
    public void BackedIntoHimOrBetweenHisLegsTheTrainIsHis()
    {
        var n = new Night(front: 4000);
        n.Step(W.RiseSeconds + 5);
        Assert.False(n.Lead!.Holds);
        n.Controls = new TrainControls { Reverser = -1, Throttle = 1, Brake = 0 };
        Assert.True(CaughtWithin(n, 200));
        n.Step(3);
        Assert.True(Math.Abs(n.World.Train.Dynamics.Speed) < 0.3, $"backing at {n.World.Train.Dynamics.Speed:0.0} m/s in his hands");

        var probe = new Night(front: 4000);
        double gate = Waker.Gate(probe.World, W), length = probe.World.Train.Dynamics.Consist.LengthMetres;
        var walls = new Night(front: gate + length + 10);
        walls.Step(W.RiseSeconds + 150);
        Assert.False(walls.Lead!.Holds);
        Assert.Equal(gate - W.StopShort, walls.Lead.LineDistance, 0.5);
        walls.Controls = new TrainControls { Reverser = -1, Throttle = 0.5, Brake = 0 };
        Assert.True(CaughtWithin(walls, 120), $"rear {walls.World.Train.RearDistance - walls.Lead.LineDistance:0} m from him");
        Assert.True(walls.World.Train.RearDistance > gate - W.StopShort - 20, "taken as it came back to him, not run on past");
    }

    /// <summary>
    /// Note 940 (line 26: "if I shoot it, will it... And nothing happens when I shoot it"): up out of the land, his body's there
    /// for a ball to find, as tall as he is; it lands and costs him nothing.
    /// </summary>
    [Fact]
    public void ShotHeIsHitAndNothingHurtsHim()
    {
        var n = new Night(front: 4000);
        n.Step(W.RiseSeconds + 5);
        var lead = n.Lead!;
        Assert.True(lead.Exposed);
        var body = lead.Body(n.World.Train, E).ToList();
        Assert.NotEmpty(body);
        Assert.True(body.Max(b => b.Position.Y + b.Radius) - n.World.Train.Line.Sample(lead.LineDistance).Position.Y > 50, "his head 50 m up and more");
        double health = lead.Health;
        var phase = lead.Phase;
        lead.Struck(null!, 1, 100);
        Assert.Equal(health, lead.Health);
        Assert.Equal(phase, lead.Phase);
        Assert.False(lead.Gone);
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
