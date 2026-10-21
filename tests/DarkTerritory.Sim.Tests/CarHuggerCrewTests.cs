using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// #633 (note 933): the crew bots answer a Car Hugger the GDD's way (§21: "cut the caboose or kill it. A group kills it fast;
/// one player can, slowly"). Before, every walker kept clear of it and nobody went, so with B.3's low ground on (note 915) it
/// held a twelve-car train under 9 m/s for minutes and two of four eight-crew nights missed dawn.
/// </summary>
public class CarHuggerCrewTests
{
    static readonly PlayerTuning P = Tuning.Player;

    /// <summary>A Hugger latched on the rear car of a six-car train at 8 m/s, and these walkers (one crew's calls) on these cars.</summary>
    static (Night Night, CarHugger Hugger, List<RoofWalkerBot> Bots) Held(params int[] cars)
    {
        var n = new Night(6, speed: 8);
        var hugger = n.World.AddEnemy(id => CarHugger.Lurking(id, n.Train.Dynamics.RearDistance + 1, 1, Tuning.Enemies.CarHugger));
        n.Run(0.5);
        Assert.True(hugger.Latched);
        var calls = new CrewCalls();
        var bots = new List<RoofWalkerBot>();
        for (int i = 0; i < cars.Length; i++)
        {
            int id = i + 1;
            calls.Role(id, BotRole.Walker);
            bots.Add(new RoofWalkerBot(5 + i, new StopHand(StopJob.None, calls, id)) { Me = id, Calls = calls });
            n.Crew[id] = PlayerMotor.SpawnOnRoof(n.Train, cars[i], 0, P);
        }
        return (n, hugger, bots);
    }

    static void Play(Night n, List<RoofWalkerBot> bots, double seconds, Func<bool> done)
    {
        for (int t = 0; t < seconds * 2 && !done(); t++)
        {
            foreach (var b in bots)
                b.Crew = [.. n.Crew.Where(c => c.Key != b.Me).Select(c => (c.Key, c.Value))];
            // As BotCrew.Step plays them: the bot's own intent, then the rescue (v1.1 App. A.1: a friend held, haul them out).
            n.Run(0.5, id => Heed.Rescue(bots[id - 1].Decide(n.Crew[id], n.World, n.World.Tick, out _), n.Crew[id], n.World, id, bots[id - 1].Crew));
        }
    }

    [Fact]
    public void TwoWalkersGoToTheCarHuggerAndDealWithItInsideTwoMinutes()
    {
        var (n, hugger, bots) = Held(2, 3);
        int rear = n.Train.Dynamics.Consist.Vehicles[^1].Id;
        Assert.NotNull(n.Train.Frames[rear].Shape.Platform);
        bool beside = false;
        Play(n, bots, 120, () =>
        {
            // Both of them came down the train to it (before, each walked away from it).
            beside |= n.Crew.Values.All(s => s.Parent == rear || s.Parent == n.Train.VehicleAhead(rear));
            return hugger.Gone;
        });
        Assert.True(beside, "the pair never came to the held car");
        Assert.True(hugger.Gone, $"still on car {hugger.Attached} after two minutes ({hugger.Phase}, health {hugger.Health:0.0})");
        Assert.All(n.Crew.Values, s => Assert.True(s.Alive, $"died of {s.Death}"));
    }

    [Fact]
    public void AWalkerAloneCutsTheHeldCarLooseRatherThanGoIntoItsMouth()
    {
        // One can't kill it fast and nobody's near to pull them from its mouth (note 305): the car goes.
        var (n, hugger, bots) = Held(2);
        int rear = n.Train.Dynamics.Consist.Vehicles[^1].Id, kept = n.Train.VehicleAhead(rear);
        int cars = n.Train.Dynamics.Consist.Vehicles.Count;
        bots[0].Calls!.FreeForHugger(1, n.World.Tick);
        var order = bots[0].Calls!.Hugger(n.World, [], 1, n.Crew[1], n.World.Tick);
        Assert.Equal((rear, 1), (order!.Car, order.Cutter));
        Assert.Empty(order.Party);
        Play(n, bots, 60, () => n.Train.Dynamics.Consist.Vehicles.Count < cars);
        Assert.Equal(kept, n.Train.Dynamics.Consist.Vehicles[^1].Id);
        Assert.True(n.Crew[1].Alive, $"died of {n.Crew[1].Death}");
        n.Run(1);
        Assert.True(hugger.Gone);
    }

    [Fact]
    public void TheOrderIsTheTwoFitWalkersNearestTheHeldCarAndNoneWithoutAHugger()
    {
        var (n, _, bots) = Held(1, 4, 5);
        var calls = bots[0].Calls!;
        uint tick = n.World.Tick;
        foreach (var b in bots)
            calls.FreeForHugger(b.Me, tick);
        var others = n.Crew.Where(c => c.Key != 1).Select(c => (c.Key, c.Value)).ToList();
        var order = calls.Hugger(n.World, others, 1, n.Crew[1], tick)!;
        Assert.Equal([3, 2], order.Party);
        Assert.Equal(-1, order.Cutter);
        // Hurt past the pack fight's health, a walker isn't sent to it: the next nearest goes.
        n.Crew[3] = n.Crew[3] with { Health = Heed.PackFightHealth - 1 };
        others = [.. n.Crew.Where(c => c.Key != 1).Select(c => (c.Key, c.Value))];
        Assert.Equal([2, 1], calls.Hugger(n.World, others, 1, n.Crew[1], tick)!.Party);
        Assert.Null(calls.Hugger(new Night(6, speed: 8).World, others, 1, n.Crew[1], tick));
    }
}
