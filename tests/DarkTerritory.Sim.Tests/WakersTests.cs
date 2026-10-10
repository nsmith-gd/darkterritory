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

    static double Covered(double s) => Waker.Covered(s, W);
}
