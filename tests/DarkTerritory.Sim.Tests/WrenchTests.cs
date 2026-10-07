using Ballast;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// T109 playtest: the vent in the cab, the wrench on its rack, and a ruptured boiler mended with the repair kit, fetched from
/// car 1 (GDD §12: the engineer is whoever has it).
/// </summary>
public class WrenchTests
{
    static World World()
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(50_000)]));
        return new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 3, 1)), line, 1_000, Tuning.Boiler));
    }

    static PlayerState At(World world, InteractableKind kind)
    {
        var at = world.Train.Frames[0].Shape.Interactables.First(i => i.Kind == kind).Position;
        var s = PlayerMotor.SpawnInCab(world.Train, Tuning.Player);
        s.Position = s.Position with { X = Math.Clamp(at.X, -1.05, 1.05), Z = at.Z };
        return s;
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
    public void TheVentIsInTheCab()
    {
        var world = World();
        var s = At(world, InteractableKind.Vent);
        Assert.True(PlayerMotor.InCab(s, world.Train));
        world.Train.Boiler.Pressure = 90;
        Hold(world, ref s, 2);
        Assert.True(world.Train.Boiler.Pressure < 80);
    }

    [Fact]
    public void TheWrenchComesOffItsRackButItsTheRepairKitThatMendsTheBoiler()
    {
        var world = World();
        var train = world.Train;
        world.EnableBodies();
        world.Stock();
        train.Boiler.Ruptured = true;
        var s = At(world, InteractableKind.ToolRack);
        Hold(world, ref s, 0.5);
        Assert.Equal(Tool.Wrench, Kit.Held(s));
        Assert.True(train.Boiler.WrenchOut);
        // Only the one: a second hand at the rack gets nothing.
        var other = At(world, InteractableKind.ToolRack);
        Hold(world, ref other, 0.5);
        Assert.False(Kit.Has(other.Kit, Tool.Wrench));
        // At the firebox with the wrench, held as long as a mend takes: nothing. It's a tool to swing (GDD §12).
        var firebox = train.Frames[0].Shape.Interactables.First(i => i.Kind == InteractableKind.Firebox).Position;
        s.Position = s.Position with { X = firebox.X + 0.15, Z = firebox.Z + 0.45 }; // behind the fire door (note 280: at the cab's front)
        Hold(world, ref s, Tuning.Boiler.RepairSeconds + 1);
        Assert.True(train.Boiler.Ruptured);
        // With the repair kit in hand there: mended, cold and empty, and the kit still in hand.
        var kit = world.Bodies.All.Single(b => b.Kind == Physics.BodyKind.RepairKit);
        kit.Carrier = 1;
        Hold(world, ref s, Tuning.Boiler.RepairSeconds - 1);
        Assert.True(train.Boiler.Ruptured);
        Hold(world, ref s, 1.2);
        Assert.False(train.Boiler.Ruptured);
        Assert.Equal(0, train.Boiler.Pressure);
        Assert.Equal(1, kit.Carrier);
    }

    [Fact]
    public void TheRepairKitRidesInTheFittersLockerInCarOne()
    {
        var world = World();
        world.EnableBodies();
        world.Stock();
        var kit = Assert.Single(world.Bodies.All, b => b.Kind == Physics.BodyKind.RepairKit);
        Assert.Equal(1, Tuning.Train.Kit.RepairKitCar);
        Assert.Equal(1, kit.Parent);
        var shape = world.Train.Frames[1].Shape;
        Assert.True(shape.Interior!.Value.Contains(kit.Centre));
        // Note 151: on the fitter's bottom shelf, its door shut, ahead of the side door and clear of the aisle.
        var bay = shape.Lockers[kit.Locker];
        Assert.Equal("8", bay.Name);
        Assert.Equal(0, kit.Slot);
        Assert.True(bay.Box.Contains(kit.Centre));
        Assert.False(world.Train.Vehicles[1].LockerOpen(bay.Index));
        Assert.True(bay.Box.Max.Z < -Tuning.Train.Geometry.Interior!.SideDoorWidth / 2);
        Assert.True(bay.Box.Max.X < Tuning.Train.Geometry.Interior.DoorX - Tuning.Train.Geometry.Doorway.Width / 2 + 1e-9);
    }

    [Fact]
    public void TheFiremanFetchesTheKitFromCarOneAndMendsIt()
    {
        var world = World();
        var train = world.Train;
        world.EnableBodies();
        world.Stock();
        train.Boiler.Ruptured = true;
        var bot = new ConductorBot(new CrewCalls(), 1) { Fireman = true };
        var s = PlayerMotor.SpawnInCab(train, Tuning.Player, -0.5);
        bool wentForIt = false;
        for (uint tick = 0; tick < (Tuning.Boiler.RepairSeconds + 90) * SimConstants.TickRate && train.Boiler.Ruptured; tick++)
        {
            var intent = bot.Decide(s, world, tick, out _);
            world.BeginTick();
            world.CrewAct(ref s, intent, 1);
            world.Step(new TrainControls { Reverser = 1, Brake = 1 });
            PlayerMotor.Step(ref s, intent, train, Tuning.Player, Tuning.Train, SimConstants.TickSeconds, applyLook: false);
            world.StepBodies([(1, s)]);
            wentForIt |= s.Parent == 1;
        }
        Assert.True(wentForIt);
        Assert.False(train.Boiler.Ruptured);
        Assert.True(PlayerMotor.InCab(s, train));
    }

    [Fact]
    public void ABodysToolsReplicate()
    {
        var host = World();
        host.EnableBodies();
        var dead = PlayerMotor.SpawnInCab(host.Train, Tuning.Player) with { Kit = Kit.Of([Tool.Wrench]), Death = DeathCause.Mauled };
        host.StepBodies([(1, dead)]);
        var client = World();
        var controls = new TrainControls();
        WorldRecords.Apply(WorldRecords.Capture(host, controls, []), client, ref controls, []);
        Assert.True(client.Bodies.All.Single().HasTool(Tool.Wrench));
    }

    [Fact]
    public void WhereTheWrenchIsReplicates()
    {
        var host = World();
        host.Train.Boiler.WrenchOut = true;
        var client = World();
        var controls = new TrainControls();
        WorldRecords.Apply(WorldRecords.Capture(host, controls, []), client, ref controls, []);
        Assert.True(client.Train.Boiler.WrenchOut);
    }
}
