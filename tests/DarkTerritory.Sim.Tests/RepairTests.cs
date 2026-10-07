using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 301 (queue #39; the director, 7 Oct, GDD App. F.3): the wrench is the repair tool, Sea of Thieves style, and every
/// break is called out where it is.
/// </summary>
public class RepairTests
{
    static readonly TrainTuning T = Tuning.Train;
    static readonly PlayerTuning P = Tuning.Player;

    static World World(int cars = 4)
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(50_000)]));
        return new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(T, cars, 1)), line, 1_000, Tuning.Boiler));
    }

    /// <summary>On a car's floor in the aisle, abreast of its dent, the wrench in hand.</summary>
    static PlayerState AtTheDent(World world, int car)
    {
        var dent = Repairs.DentAt(world.Train.Frames[car].Shape)!.Value;
        return new PlayerState
        {
            Parent = car,
            Position = new Double3(T.Geometry.Interior!.DoorX, T.Geometry.Interior.FloorHeight, dent.Z),
            Surface = Surface.Deck,
            Health = P.Health,
            LineHint = world.Train.Cars[car].FrontDistance,
            Kit = P.StartingKit,
            HeldSlot = 1,
        };
    }

    static void Hold(World world, ref PlayerState s, double seconds, PlayerIntent? intent = null)
    {
        for (int i = 0; i < seconds * SimConstants.TickRate; i++)
        {
            world.BeginTick();
            world.CrewAct(ref s, intent ?? new PlayerIntent { Buttons = PlayerButtons.Use }, 1);
            world.Step(new TrainControls { Reverser = 1, Brake = 1 });
        }
    }

    [Fact]
    public void ABatteredCarIsMendedAtItsDentWithTheWrench()
    {
        var world = World();
        var train = world.Train;
        const int car = 2;
        train.Vehicles[car].Integrity = 0.5;
        Assert.True(Repairs.Dented(train, car));
        var s = AtTheDent(world, car);
        Assert.Equal(BreakKind.Dent, Repairs.At(s, train));
        // The crowbar in hand: nothing.
        s.HeldSlot = 0;
        Hold(world, ref s, 2);
        Assert.Equal(0.5, train.Vehicles[car].Integrity);
        // The wrench: integrityPerSecond back while it's worked, and whole in the end.
        s.HeldSlot = 1;
        Hold(world, ref s, 2);
        Assert.Equal(0.5 + 2 * T.Repair.IntegrityPerSecond, train.Vehicles[car].Integrity, 2);
        Hold(world, ref s, 0.5 / T.Repair.IntegrityPerSecond);
        Assert.Equal(1, train.Vehicles[car].Integrity, 6);
        Assert.False(Repairs.Dented(train, car));
        Assert.Equal(BreakKind.None, Repairs.At(s, train));
    }

    [Fact]
    public void WhatTheCarHuggerAteIsntMended()
    {
        var world = World();
        var v = world.Train.Vehicles[2];
        v.Eaten = 0.3;
        v.Integrity = 0.4;
        var s = AtTheDent(world, 2);
        Hold(world, ref s, 1 / T.Repair.IntegrityPerSecond);
        Assert.Equal(0.7, v.Integrity, 6);
    }

    [Fact]
    public void FarFromTheDentNothingsMended()
    {
        var world = World();
        world.Train.Vehicles[2].Integrity = 0.5;
        var s = AtTheDent(world, 2);
        s.Position = s.Position with { Z = s.Position.Z - 4 };
        Hold(world, ref s, 2);
        Assert.Equal(0.5, world.Train.Vehicles[2].Integrity);
    }

    [Fact]
    public void EveryBreakIsCalledOutWhereItIs()
    {
        var world = World(5);
        var train = world.Train;
        Assert.Empty(RepairCallouts.Of(train));
        train.Boiler.Ruptured = true;
        train.Vehicles[1].Integrity = 0.5;
        train.Vehicles[2].Integrity = T.Repair.DentedBelow + 0.05; // scuffed, not called out
        train.Vehicles[3].Breach(Breaches.EndWall(train.Frames[3].Shape)!.Value);
        var callouts = RepairCallouts.Of(train);
        Assert.Equal(
            [(BreakKind.Rupture, 0), (BreakKind.Dent, 1), (BreakKind.Breach, 3)],
            callouts.Select(c => (c.Kind, c.Vehicle)).ToArray());
        Assert.Equal(train.Vehicles[3].BreachAt, callouts[2].At);
        Assert.True(train.Frames[1].Shape.Interior!.Value.Contains(callouts[1].At));
    }
}
