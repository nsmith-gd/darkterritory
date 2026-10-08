using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 472 (queue #208; the director, 8 Oct 2026: "It matters that they dont just stand there and howl, they should either
/// partrol between cars that have doors open or patrol the roofs of the cars, jumping between them if they can make the
/// jump"): a hound aboard with nobody it can reach walks the roofs, leaps the gaps (never onto the engine), drops into a car at
/// an open side door and climbs back out, and goes after whoever's out on the train further along.
/// </summary>
public class HoundPatrolTests
{
    static readonly PlayerTuning P = Tuning.Player;

    static CinderHound OnRoof(Night n, int car, double z, double x = 0.6)
    {
        var hound = n.World.AddEnemy(id => new CinderHound(id, id));
        hound.Restore(SpinePhase.Commit, 0, Tuning.Enemies.CinderHounds.Health, car, new Double3(x, n.Train.Frames[car].Shape.RoofHeight, z), 0, 0, 0.6, hound.Id, 0);
        return hound;
    }

    static int SideDoor(Night n, int car) =>
        n.Train.Frames[car].Shape.DoorList.First(d => Math.Abs(d.Box.Centre.Z) < 1 && Math.Abs(d.Box.Centre.X) > 0.5).Index;

    static bool Inside(Night n, CinderHound h) => h.Attached >= 0 && h.Local.Y < n.Train.Frames[h.Attached].Shape.RoofHeight - 1;

    [Fact]
    public void WithNobodyToReachItWalksTheRoofsAndLeapsTheGapsButNeverOntoTheEngine()
    {
        var n = new Night(4, 10, enemies: HoundRunTests.Quiet);
        n.Crew[0] = PlayerMotor.SpawnInCab(n.Train, P);
        int engine = n.Train.Dynamics.Consist.Vehicles[0].Id, first = n.Train.Dynamics.Consist.Vehicles[1].Id;
        var hound = OnRoof(n, first, 0);
        var cars = new HashSet<int>();
        var modes = new HashSet<HoundMode>();
        for (int s = 0; s < 120 * 4; s++)
        {
            n.Run(0.25);
            Assert.False(hound.Gone);
            Assert.NotEqual(engine, hound.Attached);
            Assert.False(Inside(n, hound), "in a car with its doors shut");
            if (hound.Aboard != HoundMode.Leap)
                Assert.Equal(n.Train.Frames[hound.Attached].Shape.RoofHeight, hound.Local.Y, 6);
            cars.Add(hound.Attached);
            modes.Add(hound.Aboard);
        }
        Assert.True(cars.Count >= 3, $"stayed on {cars.Count} car(s)");
        Assert.Contains(HoundMode.Patrol, modes);
        Assert.Contains(HoundMode.Leap, modes);
        Assert.Contains(HoundMode.Sniff, modes);
        Assert.Equal(P.Health, n.Crew[0].Health);
    }

    [Fact]
    public void AtAnOpenSideDoorItDropsInAndLaterClimbsBackOut()
    {
        var n = new Night(4, 10, enemies: HoundRunTests.Quiet);
        n.Crew[0] = PlayerMotor.SpawnInCab(n.Train, P);
        int car = n.Train.Dynamics.Consist.Vehicles[2].Id;
        n.Train.Vehicles[car].ToggleDoor(SideDoor(n, car));
        var hound = OnRoof(n, car, -3);
        bool dropped = false, inside = false, climbed = false, outAgain = false;
        for (int s = 0; s < 240 * 4 && !outAgain; s++)
        {
            n.Run(0.25);
            dropped |= hound.Aboard == HoundMode.Drop;
            inside |= hound.Attached == car && Inside(n, hound);
            climbed |= inside && hound.Aboard == HoundMode.Climb;
            outAgain = climbed && hound.Aboard != HoundMode.Climb && !Inside(n, hound);
        }
        Assert.True(dropped && inside, "never dropped in at the open door");
        Assert.True(climbed && outAgain, "never climbed back out");
        Assert.Equal(n.Train.Frames[hound.Attached].Shape.RoofHeight, hound.Local.Y, 6);
    }

    [Fact]
    public void InACarItBitesWhoeversInThereWithItAndNotWhoeversOnTheRoof()
    {
        var n = new Night(4, 10, enemies: HoundRunTests.Quiet);
        int car = n.Train.Dynamics.Consist.Vehicles[2].Id;
        var shape = n.Train.Frames[car].Shape;
        var room = shape.Interior!.Value;
        n.Train.Vehicles[car].ToggleDoor(SideDoor(n, car));
        var hound = n.World.AddEnemy(id => new CinderHound(id, id));
        hound.Restore(SpinePhase.Commit, 0, Tuning.Enemies.CinderHounds.Health, car, new Double3(room.Centre.X, room.Min.Y, room.Centre.Z), 0, 0, 0.6, hound.Id, 0);
        // Up on its roof, over it: out of its reach now it's in the car.
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, car, room.Centre.Z, P);
        n.Run(Tuning.Enemies.CinderHounds.BiteEverySeconds * 3);
        Assert.Equal(P.Health, n.Crew[1].Health);
        // In there with it, it has them.
        n.Crew[2] = new PlayerState { Parent = car, Position = new Double3(room.Centre.X, room.Min.Y, room.Centre.Z + 1.5), Surface = Surface.Deck, Health = P.Health };
        n.Run(Tuning.Enemies.CinderHounds.BiteEverySeconds + 1);
        Assert.True(n.Crew[2].Health < P.Health, "not bitten in the car with it");
    }

    [Fact]
    public void ItGoesAfterACrewmateOutOnTheNextCarsRoof()
    {
        var n = new Night(4, 10, enemies: HoundRunTests.Quiet);
        var consist = n.Train.Dynamics.Consist.Vehicles;
        int from = consist[^1].Id, to = consist[^2].Id;
        var hound = OnRoof(n, from, 0);
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, to, 0, P);
        double start = (hound.WorldPosition(n.Train) - PlayerMotor.WorldPosition(n.Crew[1], n.Train)).Length;
        Assert.InRange(start, Tuning.Enemies.CinderHounds.Reach + 1, Tuning.Enemies.CinderHounds.Patrol.ChaseFrom);
        n.Run(20);
        Assert.True(n.Crew[1].Health < P.Health, $"never got to them ({start:0.0} m off)");
    }
}
