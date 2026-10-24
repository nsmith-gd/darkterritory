using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game;

/// <summary>The Head Waiter staged for a look (note 1008).</summary>
public static partial class Staging
{
    public const int HeadWaiterId = 1008;

    /// <summary>
    /// The staged Head Waiter (<c>dt screenshot --headwaiter serve|wait|clear</c>, the headwaiter views; note 1008) on the BBQ
    /// deck (with <c>--upgrades bbqDeck</c>): a crewmate sat back in the near chair, and it at their shoulder on the deck's
    /// outer side, the glass ringing (<c>serve</c>), waiting (<c>wait</c>) or clearing them (<c>clear</c>, the one sat held).
    /// Nothing without the deck.
    /// </summary>
    public static (List<Crewmate> Crew, Enemy? Waiter) HeadWaiter(TrainOnLine train, string content, string mode)
    {
        var engine = train.Frames[0];
        if (engine.Shape.Bbq is not { } deck)
            return ([], null);
        var t = DataFile.Load<EnemyTuning>(Path.Combine(content, EnemyTuning.File)).HeadWaiter;
        var seat = deck.Chairs[0];
        var (stand, _) = Sim.Enemies.HeadWaiter.StandBeside(engine.Shape, seat, t);
        var (m, phase, seconds) = mode switch
        {
            "wait" => (HeadWaiterMode.Wait, SpinePhase.Telegraph, t.RingSeconds + 4),
            "clear" => (HeadWaiterMode.Clear, SpinePhase.Grab, 2.0),
            _ => (HeadWaiterMode.Serve, SpinePhase.Telegraph, 0.4),
        };
        var w = new HeadWaiter(HeadWaiterId);
        w.Restore(phase, seconds, t.KillBlows, 0, stand, train.Dynamics.Distance, 0, 0, 40, (double)m, holding: phase == SpinePhase.Grab ? 40 : -1, grabWindow: t.ClearSeconds);
        var diner = new Crewmate(40, engine.ToWorld(seat), engine.Heading, true, Act: Art.CrewPose.Lounge, Car: 0, Local: seat);
        return ([diner], w);
    }

    /// <summary>The <c>headwaiterdeck</c> view: off the deck's front corner, back at the chair and it; <c>headwaiter</c> close on it.</summary>
    public static Camera HeadWaiterCamera(TrainOnLine train, string view)
    {
        var engine = train.Frames[0];
        if (engine.Shape.Bbq is not { } deck)
            return Camera.LookAt(engine.ToWorld(new Double3(4, 3, -8)), engine.ToWorld(new Double3(0, 2, -4)), 60);
        var seat = deck.Chairs[0];
        int side = seat.X >= 0 ? 1 : -1;
        if (view == "headwaiter")
            return Camera.LookAt(engine.ToWorld(seat + new Double3(-side * 0.6, 1.7, -2.4)), engine.ToWorld(seat + new Double3(side * 0.6, 1.5, 0.3)), 50);
        return Camera.LookAt(engine.ToWorld(seat + new Double3(-side * 1.4, 2.1, -4.2)), engine.ToWorld(seat + new Double3(side * 0.4, 1.0, 0.3)), 62);
    }
}
