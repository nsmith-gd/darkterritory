using Ballast;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Game;

public static partial class Staging
{
    /// <summary>Who answers the village's ping in <see cref="PingVillage"/>: a bot crewmate's id and name.</summary>
    public const int PingTaker = 91;

    /// <summary>
    /// Note 1010 (`dt screenshot --hud --ping-village pinged|answered|done|hidden`; the director's playtest of 23 Oct, lines 9
    /// and 10): a solo night on <paramref name="route"/>, the train on the main line abreast of the first stop with a village it
    /// hasn't stood at, the one playing up on car 1's roof looking at the village, and the ping pressed (through the intent, as
    /// the app sends it). pinged: the village's mark, nobody's yet. answered: Dunmore on it ("SEARCHING THE HOUSE"). done: his
    /// job done (he's stopped answering), pings.doneSeconds and a little gone by: no mark. hidden: looking instead at a find
    /// put inside the village's nearest house, behind its wall: it isn't pinged (it can't be seen), the village is.
    /// </summary>
    public static PrototypeSession PingVillage(string content, Sim.Route.Route route, int cars, string stage)
    {
        var probe = new PrototypeSession(content, route, cars, enemies: false);
        var run = probe.World.Run ?? throw new InvalidOperationException($"{route.Name} has no run");
        var walls = probe.Train.Walls ?? throw new InvalidOperationException($"{route.Name} has no stops' walls");
        // The first stop with a village: its houses, and where the line passes nearest their middle.
        var (stop, houses) = Enumerable.Range(0, run.Stops.Count)
            .Select(k => (k, Houses: walls.OpenHouses.Where(h => run.StopAt(h.Feature) == k).ToList()))
            .FirstOrDefault(v => v.Houses.Count >= 3);
        if (houses is null)
            throw new InvalidOperationException($"{route.Name} has no stop with a village");
        var middle = houses.Aggregate(Double3.Zero, (a, h) => a + h.Middle) * (1.0 / houses.Count);
        double hint = run.Stops[stop].Start;
        var (_, along) = probe.Train.Line.Nearest(middle, ref hint);
        var session = new PrototypeSession(content, route, cars, enemies: false, at: along + 20);
        session.Controls = new Sim.Train.TrainControls { Reverser = 1, Brake = 1 };
        session.World.Names[PingTaker] = "Dunmore";
        for (int t = 0; t < SimConstants.TickRate / 2; t++)
        {
            session.Train.Dynamics.Velocity = 0;
            session.Step(default);
        }
        var train = session.Train;
        var at = middle + Double3.Up * 2;
        if (stage == "hidden")
        {
            // A find just inside the house nearest the train, looked at from the line, its back wall (or a side) between.
            var house = houses.OrderBy(h => (h.Middle - train.Frames[1].Origin).Length).First();
            at = house.Middle + Double3.Up * 0.3;
            session.World.Bodies.SpawnLoot(at, train.Dynamics.Distance, -1, 0.2);
        }
        var roof = PlayerMotor.SpawnOnRoof(train, 1, 0, session.PlayerTuning);
        var frame = train.Frames[1];
        var to = frame.ToLocal(at) - (roof.Position + Double3.Up * train.Dynamics.Tuning.Pick.EyeHeight);
        to *= 1 / to.Length;
        session.Player = roof with { Yaw = Math.Atan2(-to.X, -to.Z), Pitch = Math.Asin(to.Y) };
        session.Step(new PlayerIntent { Moves = PlayerMoves.Ping });
        session.Step(default);
        if (stage is "answered" or "done" && session.World.Pings.LastOrDefault() is { Id: var id })
        {
            // Dunmore answers it (a bot's answer is its intent, note 936), as long as he's at it.
            var bot = session.Player;
            session.World.CrewAct(ref bot, new PlayerIntent { Moves = PlayerMoves.Answer, Answer = id }, PingTaker);
            if (stage == "done")
                for (int t = 0; t < (session.World.PingTuning.DoneSeconds + 0.5) * SimConstants.TickRate; t++)
                    session.Step(default);
        }
        return session;
    }
}
