using Ballast;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Stops;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 581 (queue #313; the director, 9 Oct 2026: "There's no way it seems to actually unhook loot when its in the car
/// finally. Also it's extremely difficult to get loot into the cars, the game should be more forgiving with this"): a find
/// put down anywhere in a car's aisle is stowed; one put down outside an open door stays outside (note 659); and at a door with
/// your arms full, a held Use works the door (a tap still puts it down). The crane's half is HatchTests'.
/// </summary>
public class LootIntoCarsTests
{
    static readonly LootTuning L = DataFile.Load<LootTuning>(Path.Combine(DataFile.FindContentRoot(), LootTuning.File));
    static readonly double Tap = 1.0 / SimConstants.TickRate;

    /// <summary>A standing train at a village halt with its finds out, and a find to copy.</summary>
    static (World World, Body Model) AtAHalt()
    {
        for (ulong seed = 1; seed < 100; seed++)
        {
            var route = RouteGenerator.Generate(Tuning.Route, RouteTier.Local, seed);
            if (route.Of(FeatureKind.Village).FirstOrDefault(v => v.Stop!.Containers.Count(c => c.Zone == StopZone.Village) > 1) is not { } halt)
                continue;
            var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 4, 1)), route.Build(), (halt.Start + halt.End) / 2, Tuning.Boiler);
            var world = new World(train, Tuning.Combat);
            world.EnableBodies();
            world.Stock();
            world.EnableRun(Tuning.Run, route, 600, authority: true, FacilityTests.F, L);
            world.Run!.Stock(world.Bodies, world.Run.Stops.ToList().IndexOf(halt), searched: true);
            return (world, world.Bodies.All.First(b => b.Kind == BodyKind.Loot));
        }
        throw new InvalidOperationException("no local route has a village halt with finds");
    }

    static Body Carried(World world, Body model, in PlayerState s)
    {
        var body = world.Bodies.SpawnLoot(PlayerMotor.WorldPosition(s, world.Train) + Double3.Up, world.Train.Dynamics.Distance, model.Owner, model.Pbd.Particles[0].Radius);
        body.Carrier = 1;
        body.Claimed = true;
        return body;
    }

    /// <summary>Use held for so long, then let go (0: not pressed at all); the world, its bodies and the run stepped each tick.</summary>
    static void Use(World world, ref PlayerState s, double seconds)
    {
        int ticks = seconds <= 0 ? 0 : Math.Max(1, (int)Math.Round(seconds * SimConstants.TickRate));
        for (int i = 0; i <= ticks; i++)
            Tick(world, ref s, i < ticks ? new PlayerIntent { Buttons = PlayerButtons.Use } : default);
    }

    static void Tick(World world, ref PlayerState s, PlayerIntent intent)
    {
        world.Train.Dynamics.Velocity = 0;
        world.BeginTick();
        world.CrewAct(ref s, intent, 1);
        world.Step(new TrainControls { Reverser = 1, Brake = 1 });
        world.StepBodies([(1, s)]);
        world.StepRun([]);
    }

    static void Wait(World world, ref PlayerState s, double seconds)
    {
        for (int i = 0; i < seconds * SimConstants.TickRate; i++)
            Tick(world, ref s, default);
    }

    [Fact]
    public void AFindPutDownAnywhereInACarsAisleIsStowed()
    {
        // The director's "no way to unhook it": every spot of every car's aisle, a find in the hands, a tap of Use.
        var (world, model) = AtAHalt();
        var train = world.Train;
        int tried = 0;
        double stowed = world.Run!.Scavenged;
        foreach (var f in train.Frames)
        {
            if (f.Shape.Interior is not { } room)
                continue;
            for (double z = room.Min.Z + 0.5; z < room.Max.Z - 0.4; z += 1.5)
                for (double x = room.Min.X + 0.35; x < room.Max.X - 0.3; x += 0.45)
                {
                    var s = new PlayerState { Parent = f.Index, Position = new Double3(x, 1.1, z), Surface = Surface.Deck, Health = Tuning.Player.Health };
                    if (f.Shape.Solids.Any(b => b.Box.ContainsXZ(s.Position) && b.Box.Max.Y > 1.4 && b.Box.Min.Y < 2.6))
                        continue;
                    var body = Carried(world, model, s);
                    Wait(world, ref s, 0.1);
                    Use(world, ref s, Tap);
                    Wait(world, ref s, L.SettleSeconds + 1);
                    Assert.True(world.Bodies.CarriedBy(1) is null, $"car {f.Index} at ({x:0.0}, {z:0.0}): still in the hands");
                    // At a crew locker the tap puts it on a shelf, where it stays and pays at the gates (note 649).
                    // Note 659: banked and still lying there (or on a locker's shelf, to pay at the gates, note 649).
                    Assert.True(body.Banked || body.Stowed, $"car {f.Index} at ({x:0.0}, {z:0.0}): put down and never stowed");
                    Assert.Contains(body, world.Bodies.All);
                    world.Bodies.Remove(body);
                    tried++;
                }
        }
        Assert.True(tried > 40, $"only {tried} spots tried");
        Assert.True(world.Run.Scavenged > stowed);
    }

    [Fact]
    public void ATapAtARunOnATrainAtLineSpeedStillPutsItDown()
    {
        // D1, from the director's screenshot (a find carried into a car's aisle, "no way ... to actually unhook" it): the train
        // was likely running, and they were moving. A tap while running down the aisle at line speed puts it down, and stowed.
        var (world, model) = AtAHalt();
        var train = world.Train;
        var f = train.Frames.First(c => c.Index > 0 && c.Shape.Interior is not null);
        var room = f.Shape.Interior!.Value;
        var s = new PlayerState { Parent = f.Index, Position = new Double3(-0.3, 1.1, room.Max.Z - 1), Yaw = 0, Surface = Surface.Deck, Health = Tuning.Player.Health };
        var body = Carried(world, model, s);
        double before = world.Run!.Scavenged;
        const double LineSpeed = 15;
        var run = new PlayerIntent { MoveZ = 1, Buttons = PlayerButtons.Run };
        void Step(PlayerIntent intent)
        {
            train.Dynamics.Velocity = LineSpeed;
            world.BeginTick();
            world.CrewAct(ref s, intent, 1);
            PlayerMotor.Step(ref s, intent, train, Tuning.Player, Tuning.Train, SimConstants.TickSeconds);
            world.Step(new TrainControls { Reverser = 1, Throttle = 1 });
            world.StepBodies([(1, s)]);
            world.StepRun([]);
        }
        for (int i = 0; i < SimConstants.TickRate; i++)
            Step(run);
        Assert.Same(body, world.Bodies.CarriedBy(1));
        Assert.True(PlayerMotor.Indoors(s, train), "ran out of the car");
        Step(run with { Buttons = PlayerButtons.Run | PlayerButtons.Use });
        Step(run);
        Assert.Null(world.Bodies.CarriedBy(1));
        for (int i = 0; i < (L.SettleSeconds + 1.5) * SimConstants.TickRate; i++)
            Step(default);
        Assert.True(body.Banked, "never stowed"); // note 659: banked, and still lying where it was put
        Assert.True(world.Run.Scavenged > before, "put down at a run on a moving train, and never stowed");
    }

    [Fact]
    public void AFindPutDownOutsideAnOpenDoorStaysWhereItWasPut()
    {
        // Note 659 (the director, 13 Oct 2026: "we want the loot we put in the cars to persist once in there"): nothing is
        // pulled in through the doorway any more (note 581's take-in is gone). Set down outside, it lies outside, there.
        var (world, model) = AtAHalt();
        var train = world.Train;
        var f = train.Frames.First(c => c.Index > 0 && c.Shape.Interior is not null && c.Shape.DoorList.Any(d => d.Box.HalfSize.Z > d.Box.HalfSize.X));
        var door = f.Shape.DoorList.First(d => d.Box.HalfSize.Z > d.Box.HalfSize.X && d.Box.Centre.X < 0);
        if (!train.Vehicles[f.Index].DoorOpen(door.Index))
            train.Vehicles[f.Index].ToggleDoor(door.Index);
        // On the ground beside the door, facing it: what's in the hands is over the sill's edge, not in the car.
        var feet = f.ToWorld(new Double3(door.Box.Min.X - 0.9, 0, door.Box.Centre.Z));
        feet = feet with { Y = PlayerMotor.GroundAt(feet, train.Line, ref Unsafe.Hint) };
        var facing = f.DirToWorld(new Double3(1, 0, 0));
        var s = new PlayerState { Parent = PlayerState.World, Position = feet, Yaw = Math.Atan2(-facing.X, -facing.Z), Surface = Surface.Ground, Health = Tuning.Player.Health };
        var body = Carried(world, model, s);
        double before = world.Run!.Scavenged;
        Wait(world, ref s, 0.1);
        Use(world, ref s, Tap);
        Assert.Null(world.Bodies.CarriedBy(1));
        Wait(world, ref s, L.SettleSeconds + 1);
        Assert.Contains(body, world.Bodies.All);
        Assert.False(body.Parent == f.Index && f.Shape.Interior!.Value.Contains(body.Centre), $"taken in, to {body.Centre}");
        Assert.False(body.Banked, "paid for, lying outside");
        Assert.Equal(before, world.Run.Scavenged);
    }

    [Fact]
    public void AtADoorWithYourArmsFullAHeldUseWorksItAndATapPutsItDown()
    {
        var (world, model) = AtAHalt();
        var train = world.Train;
        var f = train.Frames.First(c => c.Index > 0 && c.Shape.Interior is not null && c.Shape.DoorList.Any(d => d.Box.HalfSize.Z > d.Box.HalfSize.X));
        var door = f.Shape.DoorList.First(d => d.Box.HalfSize.Z > d.Box.HalfSize.X && d.Box.Centre.X < 0);
        if (train.Vehicles[f.Index].DoorOpen(door.Index))
            train.Vehicles[f.Index].ToggleDoor(door.Index);
        // Inside, at the shut side door, facing it.
        var s = new PlayerState
        {
            Parent = f.Index,
            Position = new Double3(door.Box.Max.X + 0.45, 1.1, door.Box.Centre.Z),
            Yaw = Math.PI / 2,
            Surface = Surface.Deck,
            Health = Tuning.Player.Health,
        };
        Assert.Equal(InteractableKind.Door, CrewActions.NearestInteractable(s, train)?.Thing.Kind);
        var body = Carried(world, model, s);
        Wait(world, ref s, 0.1);
        double doorSeconds = train.Dynamics.Tuning.Geometry.Interior!.DoorSeconds;
        Use(world, ref s, doorSeconds + 0.1);
        Assert.True(train.Vehicles[f.Index].DoorOpen(door.Index), "the door didn't open with the arms full");
        Assert.Same(body, world.Bodies.CarriedBy(1));
        Use(world, ref s, Tap);
        Assert.Null(world.Bodies.CarriedBy(1));
    }

    [Fact]
    public void AFindPutDownAboardStaysThereBankedAndPickedUpAgainItsUnbanked()
    {
        // Note 659 (the director, 13 Oct: "I tried clicking to let go of a piece of loot before it was in the car and it just
        // disappeared"): it's banked once it lies still, and it stays lying there to be seen and taken up again; taken up, the
        // bank gives it back, and put down again it's banked again. Creatures that take loot pass a banked one over.
        var (world, model) = AtAHalt();
        var train = world.Train;
        var f = train.Frames.First(c => c.Index > 0 && c.Shape.Interior is not null);
        var room = f.Shape.Interior!.Value;
        var s = new PlayerState { Parent = f.Index, Position = new Double3(0, 1.1, (room.Min.Z + room.Max.Z) / 2), Surface = Surface.Deck, Health = Tuning.Player.Health };
        var body = Carried(world, model, s);
        double before = world.Run!.Scavenged;
        Wait(world, ref s, 0.1);
        Use(world, ref s, Tap);
        Wait(world, ref s, L.SettleSeconds + 1);
        Assert.Contains(body, world.Bodies.All);
        Assert.True(body.Banked);
        Assert.Equal(0, Bodies.Prey(body));
        double worth = world.Run.Scavenged - before;
        Assert.True(worth > 0);
        // Taken up again: no longer banked.
        body.Carrier = 1;
        Wait(world, ref s, 0.2);
        Assert.False(body.Banked);
        Assert.Equal(before, world.Run.Scavenged, 6);
        Assert.True(Bodies.Prey(body) > 0);
        // And down again: banked again, once.
        Use(world, ref s, Tap);
        Wait(world, ref s, L.SettleSeconds + 1);
        Assert.True(body.Banked);
        Assert.Equal(before + worth, world.Run.Scavenged, 6);
    }

    [Fact]
    public void AFindOnALockersShelfStaysThereAndPaysAtTheGatesWithItsCar()
    {
        // Note 649 (the director's `D1.5-loot-slots`: "a find in a crew locker stays there, on its shelf, and pays at the gates
        // with the rest, like Lethal Company's ship. A locker lost with its car is lost"): stowed, it isn't cashed as one put down
        // in the aisle is; it's counted at the gates if its car's still on the engine.
        var (world, model) = AtAHalt();
        var train = world.Train;
        int car = World.RepairKitCar(train)!.Value;
        var bay = train.Frames[car].Shape.Lockers.First(b => Lockers.FreeSlot(world.Bodies, train, car, b.Index) >= 0);
        var s = new PlayerState { Parent = car, Position = new Double3(0, 1.1, 0), Surface = Surface.Deck, Health = Tuning.Player.Health };
        var find = Carried(world, model, s);
        Assert.True(world.Bodies.Stow(find, train, car, bay.Index));
        double before = world.Run!.Scavenged;
        Wait(world, ref s, L.SettleSeconds + 1);
        Assert.Contains(find, world.Bodies.All);
        Assert.True(find.Stowed);
        Assert.Equal(before, world.Run.Scavenged);
        double worth = world.Run.FindOf(find) is { } f ? f.Value : 0;
        Assert.True(worth > 0);
        var all = train.Vehicles.Select(v => v.Id).ToHashSet();
        Assert.Equal(worth, world.Run.LockerFinds(world, all));
        // Its car lost (not on the engine at the gates): it's lost with it.
        all.Remove(car);
        Assert.Equal(0, world.Run.LockerFinds(world, all));
    }

    static class Unsafe
    {
        public static double Hint;
    }
}
