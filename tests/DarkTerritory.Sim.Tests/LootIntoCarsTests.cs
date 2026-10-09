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
/// put down anywhere in a car's aisle is stowed; one put down at an open door from outside is taken in; and at a door with
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
                    Assert.False(world.Bodies.All.Contains(body), $"car {f.Index} at ({x:0.0}, {z:0.0}): put down and never stowed");
                    tried++;
                }
        }
        Assert.True(tried > 40, $"only {tried} spots tried");
        Assert.True(world.Run.Scavenged > stowed);
    }

    [Fact]
    public void AFindPutDownOutsideAnOpenDoorIsTakenIn()
    {
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
        Assert.Equal(f.Index, body.Parent);
        Assert.True(f.Shape.Interior!.Value.Contains(body.Centre), $"set down at {body.Centre}, not in the car");
        Wait(world, ref s, L.SettleSeconds + 1);
        Assert.DoesNotContain(body, world.Bodies.All);
        Assert.True(world.Run.Scavenged > before);

        // Shut, it isn't: it's outside, on the steps or the ground.
        train.Vehicles[f.Index].ToggleDoor(door.Index);
        var again = Carried(world, model, s);
        Wait(world, ref s, 0.1);
        Use(world, ref s, Tap);
        Assert.False(again.Parent == f.Index && f.Shape.Interior!.Value.Contains(again.Centre), "taken in through a shut door");
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

    static class Unsafe
    {
        public static double Hint;
    }
}
