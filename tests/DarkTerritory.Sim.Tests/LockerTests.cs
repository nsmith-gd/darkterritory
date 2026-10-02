using Ballast;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Stops;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The crew lockers in the repair kit's car (ARCHITECTURE §8 note 172): named for the crew's grades, opened and shut by
/// holding Use, stowed in and taken from by tapping it, holding what's in them out of the physics, and replicated. The
/// repair kit starts in the fitter's; spares from the fortress (GDD v1.4 App. E.12 question 4) beside it.
/// </summary>
public class LockerTests
{
    static readonly PlayerTuning P = Tuning.Player;
    static readonly RailLine Line = new(new LineDefinition("t", [new TrackSegment(50_000)]));
    static readonly double Tap = 1.0 / SimConstants.TickRate;

    static World Stocked(TrainTuning? tuning = null, int cars = 4, RailLine? line = null, double start = 1_000)
    {
        var world = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(tuning ?? Tuning.Train, cars, 1)), line ?? Line, start));
        world.EnableBodies();
        world.Stock();
        return world;
    }

    static CarShape KitCarShape(World world) => world.Train.Frames[World.RepairKitCar(world.Train)!.Value].Shape;

    /// <summary>Standing in the aisle in front of a locker, facing its door.</summary>
    static PlayerState Facing(World world, LockerBay bay)
    {
        int car = World.RepairKitCar(world.Train)!.Value;
        var front = bay.Front;
        return new PlayerState
        {
            Parent = car,
            Position = new Double3(front.X + bay.Facing * 0.42, front.Y, front.Z),
            Yaw = bay.Facing * Math.PI / 2,
            Surface = Surface.Deck,
            Health = P.Health,
        };
    }

    /// <summary>Use held for so long, then let go; the world and its bodies stepped each tick.</summary>
    static void Use(World world, ref PlayerState s, double seconds, int id = 1)
    {
        int ticks = Math.Max(1, (int)Math.Round(seconds * SimConstants.TickRate));
        for (int i = 0; i <= ticks; i++)
        {
            world.BeginTick();
            world.CrewAct(ref s, i < ticks ? new PlayerIntent { Buttons = PlayerButtons.Use } : default, id);
            world.Step(new TrainControls { Reverser = 1, Brake = 1 });
            world.StepBodies([(id, s)]);
        }
    }

    [Fact]
    public void TheKitsCarHasARowOfAtLeastTenNamedLockersClearOfTheAisleAndTheDoors()
    {
        var world = Stocked();
        var shape = KitCarShape(world);
        var names = Tuning.Train.Kit.Lockers!.Names;
        Assert.True(names.Count >= 10);
        Assert.Equal(names.Count, shape.Lockers.Count);
        Assert.Equal(names, shape.Lockers.Select(b => b.Name));
        Assert.Contains("FITTER", names);
        var room = shape.Interior!.Value;
        var g = Tuning.Train.Geometry;
        foreach (var bay in shape.Lockers)
        {
            // Against the left wall, inside the room, left of the end doors' doorway, and ahead of the side door.
            Assert.Equal(room.Min.X, bay.Box.Min.X, 6);
            Assert.True(bay.Box.Max.X <= g.Interior!.DoorX - g.Doorway.Width / 2 + 1e-9);
            Assert.True(bay.Box.Min.Z >= room.Min.Z + 0.5 && bay.Box.Max.Z <= -g.Interior.SideDoorWidth / 2);
            Assert.Contains(shape.Solids, x => x.Part == PartKind.CrewLocker && x.Box == bay.Box);
            Assert.Contains(shape.Interactables, i => i.Kind == InteractableKind.Locker && i.Index == bay.Index);
        }
        // The aisle beside them is a person wide, and the car's extinguisher still has its board, clear of them.
        double load = shape.Solids.Where(x => x.Part == PartKind.Cargo).Min(x => x.Box.Min.X);
        Assert.True(load - shape.Lockers[0].Box.Max.X > 2 * P.Radius + 0.4);
        var mount = World.ExtinguisherMount(shape, room);
        Assert.DoesNotContain(shape.Solids, x => x.Part == PartKind.CrewLocker && x.Box.ContainsXZ(mount));
        // Only that car: the rest are as they were.
        Assert.All(world.Train.Frames.Where(f => f.Index != World.RepairKitCar(world.Train)), f => Assert.Empty(f.Shape.Lockers));
    }

    [Fact]
    public void HoldingUseOpensAndShutsALockerAndATapStowsAndTakes()
    {
        var world = Stocked();
        int car = World.RepairKitCar(world.Train)!.Value;
        var bay = KitCarShape(world).Lockers.First(b => b.Name == "LAMPMAN");
        var s = Facing(world, bay);
        Assert.Equal(InteractableKind.Locker, CrewActions.Nearest(s, world.Train));
        // A tap at a shut locker does nothing; held, it opens.
        Use(world, ref s, Tap);
        Assert.False(world.Train.Vehicles[car].LockerOpen(bay.Index));
        Use(world, ref s, Lockers.DoorSeconds(world.Train) + 0.1);
        Assert.True(world.Train.Vehicles[car].LockerOpen(bay.Index));

        // The guard van's lamp, carried here and tapped in: on the bottom shelf, out of the hands, out of the physics.
        var lamp = world.Bodies.All.First(b => b.Kind == BodyKind.Lamp);
        lamp.Carrier = 1;
        world.StepBodies([(1, s)]);
        Use(world, ref s, Tap);
        Assert.Null(world.Bodies.CarriedBy(1));
        Assert.Equal(bay.Index, lamp.Locker);
        Assert.Equal(0, lamp.Slot);
        Assert.Equal(car, lamp.Parent);
        Assert.True(bay.Box.Contains(lamp.Centre));
        var at = lamp.Centre;
        // It isn't to be reached for, only taken: and it stays put on a hard stop.
        Assert.Null(world.Bodies.InReach(s, world.Train));
        world.Train.Dynamics.Velocity = 15;
        for (int i = 0; i < 30; i++)
        {
            world.Step(new TrainControls { Reverser = 1, Brake = 1 });
            world.StepBodies([(1, s)]);
        }
        Assert.Equal(at, lamp.Centre);

        // Shut, it neither gives nor takes; open, a tap takes it back into the hands.
        Use(world, ref s, Lockers.DoorSeconds(world.Train) + 0.1);
        Assert.False(world.Train.Vehicles[car].LockerOpen(bay.Index));
        Use(world, ref s, Tap);
        Assert.Equal(bay.Index, lamp.Locker);
        Use(world, ref s, Lockers.DoorSeconds(world.Train) + 0.1);
        Use(world, ref s, Tap);
        Assert.Same(lamp, world.Bodies.CarriedBy(1));
        Assert.False(lamp.Stowed);
    }

    [Fact]
    public void ALockerHoldsItsShelvesWorthOfHandSizedThingsAndNoFreight()
    {
        var world = Stocked();
        int car = World.RepairKitCar(world.Train)!.Value;
        var bay = KitCarShape(world).Lockers.First(b => b.Name == "PORTER");
        var toys = world.Bodies.All.Where(b => b.Kind == BodyKind.Toy).ToList();
        var radio = world.Bodies.All.First(b => b.Kind == BodyKind.Radio && b.Parent != 0);
        Assert.True(world.Bodies.Stow(toys[0], world.Train, car, bay.Index));
        Assert.True(world.Bodies.Stow(toys[1], world.Train, car, bay.Index));
        Assert.Equal(Lockers.Slots(world.Train), Lockers.Contents(world.Bodies, car, bay.Index).Count());
        Assert.False(world.Bodies.Stow(radio, world.Train, car, bay.Index));
        Assert.False(radio.Stowed);
        // The train's crates don't go in a locker at all.
        var crate = world.Bodies.All.First(b => b.Kind == BodyKind.Crate);
        Assert.False(world.Bodies.Stow(crate, world.Train, car, 0));
        // Brought from another car, a stowed thing takes the locker's car's frame, on its shelf.
        Assert.Equal(car, toys[1].Parent);
        Assert.Equal(1, toys[1].Slot);
        Assert.True(Lockers.SlotAt(bay, 1, Lockers.Slots(world.Train), toys[1].Pbd.Particles[0].Radius).Y > Lockers.SlotAt(bay, 0, 2, 0).Y + 0.5);
    }

    [Fact]
    public void TheLockersAndWhatsInThemReplicate()
    {
        var host = Stocked();
        int car = World.RepairKitCar(host.Train)!.Value;
        var kit = host.Bodies.All.Single(b => b.Kind == BodyKind.RepairKit);
        host.Train.Vehicles[car].ToggleLocker(kit.Locker);
        var client = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 4, 1)), Line, 1_000));
        var controls = new TrainControls();
        WorldRecords.Apply(WorldRecords.Capture(host, controls, []), client, ref controls, []);
        Assert.True(client.Train.Vehicles[car].LockerOpen(kit.Locker));
        var mirrored = client.Bodies.All.Single(b => b.Kind == BodyKind.RepairKit);
        Assert.Equal(kit.Locker, mirrored.Locker);
        Assert.Equal(kit.Slot, mirrored.Slot);
        Assert.Equal(car, mirrored.Parent);
        // A thing out of a locker says so too.
        var lamp = host.Bodies.All.First(b => b.Kind == BodyKind.Lamp);
        WorldRecords.Apply(WorldRecords.Capture(host, controls, []), client, ref controls, []);
        Assert.False(client.Bodies.All.Single(b => b.Id == lamp.Id).Stowed);
    }

    [Fact]
    public void TheRepairKitStartsInTheFittersLockerAndTheBotFetchesItFromThere()
    {
        var world = Stocked();
        var kit = world.Bodies.All.Single(b => b.Kind == BodyKind.RepairKit);
        var (car, bay) = World.KitLocker(world.Train)!.Value;
        Assert.Equal("FITTER", bay.Name);
        Assert.Equal(car, kit.Parent);
        Assert.Equal(bay.Index, kit.Locker);
        Assert.True(kit.Claimed);
        Assert.True(bay.Box.Contains(World.RepairKitStowage(world.Train.Frames[car].Shape, world.Train.Frames[car].Shape.Interior!.Value)));
    }

    [Fact]
    public void ASpareKitBoughtAtTheFortressStartsTheNightInALocker()
    {
        var c = DataFile.Load<CampaignTuning>(Path.Combine(DataFile.FindContentRoot(), CampaignTuning.File));
        var s = Campaign.Campaign.New(c, 1, "Spares", 3) with { Scrip = 10_000 };
        var bought = Campaign.Campaign.BuySpareKit(c, s);
        Assert.True(bought.Ok);
        Assert.Equal(1, bought.State.SpareKits);
        Assert.Equal(s.Scrip - c.SpareKit!.Cost, bought.State.Scrip);
        // No more than the lockers keep, and not without the scrip.
        var full = bought.State with { SpareKits = c.SpareKit.Most };
        Assert.False(Campaign.Campaign.BuySpareKit(c, full).Ok);
        Assert.False(Campaign.Campaign.BuySpareKit(c, s with { Scrip = 0 }).Ok);

        var loadout = Campaign.Campaign.WithSpareKits(new Loadout(Tuning.Train, Tuning.Boiler, Tuning.Combat, null), bought.State.SpareKits);
        var world = Stocked(loadout.Train);
        var kits = world.Bodies.All.Where(b => b.Kind == BodyKind.RepairKit).OrderBy(b => b.Id).ToList();
        Assert.Equal(2, kits.Count);
        var shape = KitCarShape(world);
        // The spare on the fitter's other shelf.
        Assert.All(kits, k => Assert.Equal("FITTER", shape.Lockers[k.Locker].Name));
        Assert.Equal([0, 1], kits.Select(k => k.Slot));
        // With more spares than his shelves, the next lockers along take them.
        var many = Stocked(Tuning.Train with { Kit = Tuning.Train.Kit with { SpareKits = 3 } });
        var stowed = many.Bodies.All.Where(b => b.Kind == BodyKind.RepairKit).ToList();
        Assert.Equal(4, stowed.Count);
        Assert.All(stowed, k => Assert.True(k.Stowed));
        Assert.Equal(2, stowed.Select(k => k.Locker).Distinct().Count());
    }

    [Fact]
    public void TheCampaignKeepsTheSparesThatComeHome()
    {
        var c = DataFile.Load<CampaignTuning>(Path.Combine(DataFile.FindContentRoot(), CampaignTuning.File));
        var s = Campaign.Campaign.New(c, 1, "Spares", 3) with { SpareKits = 2 };
        RunReport Report(int spares) => new(RunEnd.Delivered, 1500, 20, 2, 0, 2, 900, 30, 0, 20, 850, 4, 0) { SpareKitsHome = spares };
        Assert.Equal(1, Campaign.Campaign.Settle(s, Report(1)).SpareKits);
        Assert.Equal(3, Campaign.Campaign.Settle(s, Report(3)).SpareKits);
        // A report that doesn't say leaves them be.
        Assert.Equal(2, Campaign.Campaign.Settle(s, Report(-1)).SpareKits);
    }

    [Fact]
    public void KitsTurnUpAsLootAtTheStopsRarely()
    {
        var loot = DataFile.Load<LootTuning>(Path.Combine(DataFile.FindContentRoot(), LootTuning.File));
        Assert.InRange(loot.RepairKitChance, 0.005, 0.1);
        int stops = 0, containers = 0, found = 0;
        for (ulong seed = 1; seed <= 6; seed++)
        {
            var route = RouteGenerator.Generate(Tuning.Route, RouteTier.Frontier, seed);
            foreach (var (f, i) in route.Features.Select((f, i) => (f, i)).Where(x => x.f.Stop is not null))
            {
                stops++;
                containers += f.Stop!.Containers.Count(x => x.Kind != ContainerKind.CraneBay);
                found += StopLoot.Kits(loot, f.Stop, route.Seed, i).Count;
                // Every container may have one, and none with the chance at nothing; the same each time from the seed.
                Assert.Equal(f.Stop.Containers.Count(x => x.Kind != ContainerKind.CraneBay), StopLoot.Kits(loot with { RepairKitChance = 1 }, f.Stop, route.Seed, i).Count);
                Assert.Empty(StopLoot.Kits(loot with { RepairKitChance = 0 }, f.Stop, route.Seed, i));
                Assert.Equal(StopLoot.Kits(loot, f.Stop, route.Seed, i), StopLoot.Kits(loot, f.Stop, route.Seed, i));
            }
        }
        Assert.True(stops > 0 && containers > 0);
        Assert.True(found <= containers * 0.15, $"{found} kits in {containers} containers");
    }

    [Fact]
    public void AFoundKitLyingUnclaimedCantStrandANight()
    {
        var route = RouteGenerator.Generate(Tuning.Route, RouteTier.Frontier, 1);
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 4, 0)), route.Build(), 3_000, Tuning.Boiler);
        var world = new World(train, Tuning.Combat);
        world.EnableBodies();
        world.EnableRun(Tuning.Run, route, 600, authority: true);
        world.Run!.Resume(900, -1, train.Boiler.Tender, 0);
        // A train that left without one, and a kit lying at a stop it's passing: not the crew's, so nothing to lose yet.
        var at = train.Line.Sample(train.Dynamics.Distance - 20).Position + new Double3(4, 0.1, 0);
        var found = world.Bodies.SpawnItem(at, train.Dynamics.Distance - 20, BodyKind.RepairKit);
        Assert.False(found.Claimed);
        train.Boiler.Ruptured = true;
        for (int i = 0; i < SimConstants.TickRate * 2; i++)
        {
            world.BeginTick();
            world.Step(new TrainControls { Reverser = 1 });
            world.StepBodies([]);
            world.StepRun([]);
        }
        Assert.Equal(KitPlace.None, world.Run.Kit.Place);
        Assert.False(world.Run.Over);
    }
}
