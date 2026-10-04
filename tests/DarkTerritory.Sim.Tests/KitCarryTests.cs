using Ballast;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The repair kit brought up from further back than car 1 (GDD §12, §23 "the kit left in car four"; note 186): which kit
/// is a crewmate's to bring, and who goes for it, worked out alike by every bot. The bringing itself is the cascade audit's
/// (<c>AuditTests.TheCrewComesBackFromIt("rupture-kit-in-car-four")</c>).
/// </summary>
public class KitCarryTests
{
    static readonly RailLine Line = new(new LineDefinition("t", [new TrackSegment(50_000)]));

    static World Stocked()
    {
        var world = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), Line, 1_000));
        world.EnableBodies();
        world.Stock();
        return world;
    }

    static int Car(World world, int index) => world.Train.Dynamics.Consist.Vehicles[index].Id;

    /// <summary>The kit out of its locker onto a car's floor, as the cascade audit leaves it.</summary>
    static Body KitTo(World world, int index)
    {
        var kit = world.Bodies.All.Single(b => b.Kind == BodyKind.RepairKit);
        int car = Car(world, index);
        var room = world.Train.Frames[car].Shape.Interior!.Value;
        world.Bodies.Remove(kit);
        return world.Bodies.SpawnCrate(world.Train, car, new Double3(0.4, room.Min.Y + 0.1, room.Centre.Z), BodyKind.RepairKit);
    }

    static PlayerState OnRoof(World world, int index, double z, PlayerFlags flags = PlayerFlags.None) => new()
    {
        Parent = Car(world, index),
        Surface = Surface.Roof,
        Position = new Double3(0, Tuning.Train.Geometry.CarHeight, z),
        Health = 100,
        Flags = flags,
    };

    static PlayerState InCar(World world, int index) => new()
    {
        Parent = Car(world, index),
        Surface = Surface.Deck,
        Position = new Double3(Tuning.Train.Geometry.Interior!.DoorX, Tuning.Train.Geometry.Interior.FloorHeight, 0),
        Health = 100,
    };

    /// <summary>Who of these says it's theirs: each asks with itself as "me" and the rest as its crew.</summary>
    static List<int> Going(World world, Body kit, params (int Id, PlayerState State)[] crew) =>
        [.. crew.Where(c => KitCarry.Mine(c.State, c.Id, [.. crew.Where(o => o.Id != c.Id)], world.Train, kit)).Select(c => c.Id)];

    [Fact]
    public void OnlyAKitBackDownTheTrainIsACrewmatesToBring()
    {
        var world = Stocked();
        // In the fitter's locker in car 1: the cab's (KitRun).
        Assert.Null(KitCarry.Lying(world));
        var kit = KitTo(world, 4);
        Assert.Same(kit, KitCarry.Lying(world));
        // In someone's hands, it's theirs.
        kit.Carrier = 3;
        Assert.Null(KitCarry.Lying(world));
        kit.Carrier = -1;
        // Brought as far as car 1, the cab fetches it the rest of the way.
        KitTo(world, 1);
        Assert.Null(KitCarry.Lying(world));
    }

    [Fact]
    public void ExactlyOneGoesForIt()
    {
        var world = Stocked();
        var kit = KitTo(world, 4);
        // Two side by side on its roof: the lower id, and only them (each sees the other a snapshot late).
        Assert.Equal([2], Going(world, kit, (3, OnRoof(world, 4, -2)), (2, OnRoof(world, 4, -1.9))));
        // On the roofs, fewest cars off: the car ahead of the kit's counts as its own (its rear plate is the way in).
        Assert.Equal([5], Going(world, kit, (2, OnRoof(world, 1, 0)), (5, OnRoof(world, 3, 0)), (4, OnRoof(world, 6, 0))));
        // Anyone already down on the floors beats anyone up on the roofs, however near.
        Assert.Equal([4], Going(world, kit, (2, OnRoof(world, 4, 0)), (4, InCar(world, 2))));
        // Not the gunner at its gun while there's anyone else; it goes when there isn't.
        Assert.Equal([3], Going(world, kit, (1, OnRoof(world, 4, 0, PlayerFlags.Seated)), (3, OnRoof(world, 6, 0))));
        Assert.Equal([1], Going(world, kit, (1, OnRoof(world, 6, 0, PlayerFlags.Seated))));
        // Never the cab's crew, nor the dead.
        var cab = new PlayerState { Parent = 0, Surface = Surface.Deck, Health = 100 };
        var dead = OnRoof(world, 4, 0) with { Death = DeathCause.Thrown };
        Assert.Equal([3], Going(world, kit, (0, cab), (2, dead), (3, OnRoof(world, 6, 0))));
    }
}
