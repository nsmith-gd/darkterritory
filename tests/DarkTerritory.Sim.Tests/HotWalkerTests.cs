using Ballast;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 380 (queue #117): walkers who live through a hot run. The express driver (note 376) keeps the hounds' runs coming and
/// the Car Hugger's grip holds a car under them, so a car can be held and boarded both: the Hugger's rule keeps the crew off it,
/// the pack's sends the fit at it, and on frontier:3 the walkers went back and forth between the two while the pack mauled the
/// gunner. Both go with the car cut loose (App. A.3).
/// </summary>
public class HotWalkerTests
{
    static readonly PlayerTuning P = Tuning.Player;

    [Fact]
    public void AWalkerCutsLooseACarTheCarHuggerHoldsWithAPackAboard()
    {
        var n = new Night(6, speed: 8);
        int rear = n.Train.Dynamics.Consist.Vehicles[^1].Id, ahead = n.Train.VehicleAhead(rear);
        var hugger = n.World.AddEnemy(id => CarHugger.Lurking(id, n.Train.Dynamics.RearDistance + 1, 1, Tuning.Enemies.CarHugger));
        n.Run(0.5);
        Assert.True(hugger.Latched);
        var shape = n.Train.Frames[rear].Shape;
        var pack = new List<CinderHound>();
        for (int i = 0; i < 3; i++)
        {
            var h = n.World.AddEnemy(e => new CinderHound(e, 900) { Health = Tuning.Enemies.CinderHounds.Health });
            h.Restore(SpinePhase.Commit, 0, Tuning.Enemies.CinderHounds.Health, rear, new Double3(i % 2 == 0 ? 0.6 : -0.6, shape.RoofHeight, shape.HalfLength - 2.5 - i * 0.8), 0, 0, 0, 900, 0);
            pack.Add(h);
        }
        var bot = new RoofWalkerBot(5, P.Cold, new StopHand(StopJob.None, new CrewCalls(), 1, P.Cold)) { Me = 1 };
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, 2, 0, P);
        int cars = n.Train.Dynamics.Consist.Vehicles.Count;
        for (int i = 0; i < 60 && n.Train.Dynamics.Consist.Vehicles.Count == cars; i++)
        {
            bot.Crew = [(1, n.Crew[1])];
            n.Run(0.5, id => bot.Decide(n.Crew[id], n.World, n.World.Tick, out _));
        }
        var s = n.Crew[1];
        Assert.True(s.Alive, $"died of {s.Death}");
        Assert.True(n.Train.Dynamics.Consist.Vehicles.Count < cars, $"never cut it: walker on {s.Parent} {s.Surface}");
        Assert.Equal(ahead, n.Train.Dynamics.Consist.Vehicles[^1].Id);
        n.Run(1);
        Assert.All(pack, h => Assert.True(h.Gone));
    }
}
