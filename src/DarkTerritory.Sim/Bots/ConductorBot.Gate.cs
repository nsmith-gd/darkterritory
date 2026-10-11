using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Bots;

public sealed partial class ConductorBot
{
    /// <summary>The gate's stand is over for the night: the train's clear, or it gave up on it, or ran in out of time.</summary>
    bool _gateDone;
    /// <summary>Seconds stood short of the gate for the crew to clear the train (for <c>crewBots.gateClearGiveUp</c>).</summary>
    double _gateStood;

    /// <summary>Standing short of a town's gate for the crew to clear the train (note 605; for tests and the harness's trace).</summary>
    public bool ClearingForTheGate { get; private set; }

    /// <summary>
    /// Something aboard that a town's gate would charge the brigade for (note 589, <see cref="Run.Run.BringIn"/>): a creature on
    /// the train, not a hazard (the Wakers) nor an incident (a fire, a loose load).
    /// </summary>
    public static bool Aboard(World world) =>
        world.ActiveEnemies.Any(e => !e.Gone && !e.Hazard && e is not Incident && e.Attached >= 0);

    /// <summary>
    /// Note 605 (queue #337; the director, 9 Oct 2026: "plan a little extra time at the end of the run to stop the train, clear
    /// the monsters, and then bring it in"): with something aboard the gate would charge for and the time to clear it, down to
    /// a stand <c>crewBots.gateClearShort</c> m short of a town's gate, in the quiet zone (note 590: nothing new comes there),
    /// and stand while the crew clears the train (they fight what's aboard a standing train as anywhere). Then on in, the
    /// driver's home rule (note 600) taking it from there. Not with no time in hand (<see cref="GateTimeInHand"/>): it runs
    /// straight in, and the brigade's fee beats being eaten at dawn. Null when there's nothing to stand for.
    /// </summary>
    PlayerIntent? ForTheGate(World world, ref double cruise)
    {
        ClearingForTheGate = false;
        var train = world.Train;
        var d = train.Dynamics;
        if (_gateDone || world.Enemies is not { } et || world.Run is not { Phase: Run.RunPhase.Underway } run
            || run.Route.Plan?.Terminus is not { Silent: false } town || d.Path != Rail.RailLine.MainPath)
            return null;
        var b = et.CrewBots;
        double stand = town.GateM - b.GateClearShort, ahead = stand - d.Distance;
        if (ahead > b.GateClearWithin || ahead < -b.GateClearShort)
            return null;
        // Clear, or out of time, or stood long enough: on in, and not again tonight.
        if (!Aboard(world) || _gateStood > b.GateClearGiveUp || GateTimeInHand(world, run, et, stand) <= 0)
        {
            if (_gateStood > 0 || !Aboard(world))
                _gateDone = _gateStood > 0;
            return null;
        }
        ClearingForTheGate = true;
        if (ahead > 0.5 && !(d.Speed < 0.05 && ahead < 5))
        {
            cruise = Math.Min(cruise, Math.Max(0, Math.Sqrt(2 * DollBraking * Math.Max(0, ahead))));
            return null;
        }
        if (d.Speed < 0.05)
            _gateStood += SimConstants.TickSeconds;
        return new PlayerIntent { Buttons = PlayerButtons.Brake, ThrottleNotch = -4 };
    }

    /// <summary>
    /// How long the train can still stand at <paramref name="stand"/> (its front there) and come to no harm at dawn (s). With
    /// the Wakers (note 588): a rear past their wall (<c>wakers.stopShort</c> short of the gate) is out of their reach. So a
    /// stand whose rear is past it has all the time there is (only the give-up bounds it), once the rear's got past the wall:
    /// short of it still, the time in hand is to their catch (they rise <c>riseBehind</c> m behind the rear at dawn and come at
    /// their speeds, <see cref="Waker.Covered"/>) less the run to the wall from here, and up already, none. A stand short of
    /// the wall (a train longer than the gap) has to the catch less the run in past it. All less <c>crewBots.gateClearMargin</c>.
    /// Without the Wakers, dawn and <c>run.json dawnGraceSeconds</c>.
    /// </summary>
    public static double GateTimeInHand(World world, Run.Run run, EnemyTuning et, double stand)
    {
        var train = world.Train;
        var d = train.Dynamics;
        double margin = et.CrewBots.GateClearMargin;
        if (!et.Wakers.Enabled)
            return run.DawnIn + run.Tuning.DawnGraceSeconds - margin;
        var w = et.Wakers;
        double wall = (run.Route.Plan?.Terminus.GateM ?? run.Route.Length - run.Tuning.TerminusZone) - w.StopShort;
        double length = d.Consist.LengthMetres, rear = d.Distance - length, standRear = stand - length;
        bool up = world.ActiveEnemies.Any(e => e is Waker { Gone: false });
        if (rear > wall && standRear > wall)
            return double.PositiveInfinity;
        if (up)
            return 0;
        double catchAt = run.DawnIn + w.RiseSeconds + SecondsToCover(w.RiseBehind, w);
        // To the wall: from here at the pace it's making (no less than the run in's), or from the stand at the run in's.
        double toWall = standRear > wall
            ? Math.Max(0, wall - rear) / Math.Max(GateRunIn, d.Speed)
            : (wall - standRear) / GateRunIn;
        return catchAt - toWall - margin;
    }

    /// <summary>The speed the train's reckoned to make getting in from the gate's stand (m/s): a cautious one, off a stand.</summary>
    const double GateRunIn = 4;

    /// <summary>The seconds a Waker takes to come <paramref name="metres"/> (the inverse of <see cref="Waker.Covered"/>).</summary>
    static double SecondsToCover(double metres, WakersTuning t)
    {
        double lo = 0, hi = 3600;
        for (int i = 0; i < 40; i++)
        {
            double mid = (lo + hi) / 2;
            if (Waker.Covered(mid, t) < metres)
                lo = mid;
            else
                hi = mid;
        }
        return hi;
    }
}
