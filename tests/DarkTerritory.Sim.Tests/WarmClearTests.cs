using Ballast;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 537 (queue #279; #208's patrol meeting the warm-up, D1's frontier:7 seed 3): the gunner, warming up in car 7, was
/// mauled by a hound that dropped in at an open side door. A crewmate warming up does it clear of the pack.
/// </summary>
public class WarmClearTests
{
    static readonly PlayerTuning P = Tuning.Player;

    /// <summary>A pack aboard <paramref name="car"/> as a run from behind boards it: at its rear end, each on its own spot.</summary>
    static List<CinderHound> PackOn(Night n, int car, int size)
    {
        var shape = n.Train.Frames[car].Shape;
        var pack = new List<CinderHound>();
        for (int i = 0; i < size; i++)
        {
            var h = n.World.AddEnemy(id => new CinderHound(id, id));
            h.Restore(SpinePhase.Commit, 0, Tuning.Enemies.CinderHounds.Health, car, new Double3(i % 2 == 0 ? 0.6 : -0.6, shape.RoofHeight, shape.HalfLength - 2.5 - i * 0.8), 0, 0, 0.6, h.Id, 0);
            pack.Add(h);
        }
        return pack;
    }

    static int SideDoor(Night n, int car) =>
        n.Train.Frames[car].Shape.DoorList.First(d => Math.Abs(d.Box.Centre.Z) < 1 && Math.Abs(d.Box.Centre.X) > 0.5).Index;

    static bool Indoors(Night n, int id) => PlayerMotor.Indoors(n.Crew[id], n.Train);

    [Fact]
    public void AlreadyWarmingInACarThePackBoardsNoHoundDropsInOnIt()
    {
        // In and warming behind its shut doors (it shut the side door left open for loading on its way in), and a pack boards
        // its car: in it stays while they're on its ground, warm or not, not out onto the roof among them. No hound drops in
        // on it (note 472: only at an open side door), and nothing bites it in there. (Their fire drives it out, note 537.)
        var n = new Night(4, 6, enemies: HoundRunTests.Quiet);
        n.Crew[0] = PlayerMotor.SpawnInCab(n.Train, P);
        int car = n.Train.Dynamics.Consist.Vehicles[2].Id;
        n.Train.Vehicles[car].ToggleDoor(SideDoor(n, car));
        var bot = new RoofWalkerBot(7, P.Cold) { Me = 1 };
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, car, 2, P) with { Cold = P.Cold.OnsetSeconds * 0.9 };
        for (int i = 0; i < 60 * 4 && bot.WarmUpStep is not "Warm/cold"; i++)
            n.Run(0.25, id => id == 1 ? bot.Decide(n.Crew[1], n.World, n.World.Tick, out _) : default);
        Assert.Equal("Warm/cold", bot.WarmUpStep);
        int warmCar = n.Crew[1].Parent;
        Assert.False(n.Train.Vehicles[warmCar].DoorOpen(SideDoor(n, warmCar)));
        var pack = PackOn(n, warmCar, 4);
        var steps = new List<string>();
        for (int i = 0; i < 25 * 4; i++)
        {
            int was = n.Crew[1].Health;
            bool inside = Indoors(n, 1);
            n.Run(0.25, id => id == 1 ? bot.Decide(n.Crew[1], n.World, n.World.Tick, out _) : default);
            if (bot.WarmUpStep is { } w && (steps.Count == 0 || steps[^1] != w)) steps.Add(w);
            Assert.False(pack.Any(h => h.Attached == warmCar && h.Inside(n.Train)), $"a hound in at {i / 4.0:0.0} s: {string.Join(" > ", steps)}; side door open {n.Train.Vehicles[warmCar].DoorOpen(SideDoor(n, warmCar))}; walker at z {n.Crew[1].Position.Z:0.0}");
            Assert.False(inside && n.Crew[1].Health < was, $"bitten indoors: {string.Join(" > ", steps)}");
        }
        Assert.True(Indoors(n, 1) && n.Crew[1].Parent == warmCar, $"came out among them: {string.Join(" > ", steps)}; {n.Crew[1].Surface} on {n.Crew[1].Parent}");
        Assert.False(n.Train.Vehicles[warmCar].DoorOpen(SideDoor(n, warmCar)));
    }

    [Fact]
    public void ColdOnThePacksGroundItWarmsUpInTheCarAheadOfIt()
    {
        // Cold on car 2's roof, a pack on car 3 (its ground runs from car 2 back, note 472): not into car 2, where a hound drops
        // in at the first open side door, but forward into car 1, ahead of the pack's ground.
        var n = new Night(4, 6, enemies: HoundRunTests.Quiet);
        n.Crew[0] = PlayerMotor.SpawnInCab(n.Train, P);
        var cars = n.Train.Dynamics.Consist.Vehicles;
        int first = cars[1].Id, second = cars[2].Id, third = cars[3].Id;
        PackOn(n, third, 3);
        n.Run(SimConstants.TickSeconds);
        var bot = new RoofWalkerBot(7, P.Cold) { Me = 1 };
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, second, 3, P) with { Cold = P.Cold.OnsetSeconds * 0.9 };
        var steps = new List<string>();
        for (int i = 0; i < 60 * 4 && bot.WarmUpStep is not "Warm/cold"; i++)
        {
            n.Run(0.25, id => id == 1 ? bot.Decide(n.Crew[1], n.World, n.World.Tick, out _) : default);
            if (bot.WarmUpStep is { } w && (steps.Count == 0 || steps[^1] != w)) steps.Add(w);
        }
        Assert.True(bot.WarmUpStep == "Warm/cold", $"never warming: {string.Join(" > ", steps)}; {n.Crew[1].Surface} on {n.Crew[1].Parent}");
        Assert.Equal(first, n.Crew[1].Parent);
        Assert.Equal(P.Health, n.Crew[1].Health);
    }
}
