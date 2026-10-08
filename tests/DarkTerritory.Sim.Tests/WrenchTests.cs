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

    /// <summary>Where the repair kit mends the boiler (note 301's <c>repair.wrench</c> off, the T109 rule).</summary>
    static World KitWorld()
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(50_000)]));
        var t = Tuning.Train with { Repair = Tuning.Train.Repair with { Wrench = false } };
        return new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(t, 3, 1)), line, 1_000, Tuning.Boiler));
    }

    /// <summary>In front of the fire door (cab forward, note 276).</summary>
    static void AtTheFireDoor(World world, ref PlayerState s)
    {
        var firebox = world.Train.Frames[0].Shape.Interactables.First(i => i.Kind == InteractableKind.Firebox).Position;
        s.Position = s.Position with { X = 0.35, Z = firebox.Z - 0.45 };
    }

    [Fact]
    public void WithoutTheWrenchRuleTheWrenchComesOffItsRackButItsTheRepairKitThatMendsTheBoiler()
    {
        var world = KitWorld();
        var train = world.Train;
        world.EnableBodies();
        world.Stock();
        train.Boiler.Ruptured = true;
        var s = At(world, InteractableKind.ToolRack) with { Kit = Kit.Of([Tool.Crowbar]) };
        Hold(world, ref s, 0.5);
        Assert.Equal(Tool.Wrench, Kit.Held(s));
        Assert.True(train.Boiler.WrenchOut);
        // Only the one: a second hand at the rack gets nothing.
        var other = At(world, InteractableKind.ToolRack) with { Kit = Kit.Of([Tool.Crowbar]) };
        Hold(world, ref other, 0.5);
        Assert.False(Kit.Has(other.Kit, Tool.Wrench));
        // At the firebox with the wrench, held as long as a mend takes: nothing. It's a tool to swing (GDD §12).
        AtTheFireDoor(world, ref s);
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
    public void TheWrenchEveryoneCarriesMendsTheBoilerAndTheKitNoLonger()
    {
        // Note 301 (queue #39): the wrench is the repair tool. Everyone has one; in hand at the fire door, Use mends it.
        Assert.True(Tuning.Train.Repair.Wrench);
        var world = World();
        var train = world.Train;
        world.EnableBodies();
        world.Stock();
        train.Boiler.Ruptured = true;
        var s = At(world, InteractableKind.Firebox);
        AtTheFireDoor(world, ref s);
        Assert.True(Kit.Has(s.Kit, Tool.Wrench));
        Assert.Equal(BreakKind.Rupture, Repairs.At(s, train));
        // The kit's gone (slice 2): none rides in the fitter's locker. And the crowbar in hand mends nothing.
        Assert.DoesNotContain(world.Bodies.All, b => b.Kind == Physics.BodyKind.RepairKit);
        Hold(world, ref s, Tuning.Boiler.RepairSeconds + 1);
        Assert.True(train.Boiler.Ruptured);
        // The wrench in hand (its number key, Repairs.WrenchKey).
        Assert.Equal(2, Repairs.WrenchKey(s));
        s.HeldSlot = 1;
        Assert.Equal(0, Repairs.WrenchKey(s));
        Hold(world, ref s, Tuning.Boiler.RepairSeconds - 1);
        Assert.True(train.Boiler.Ruptured);
        Hold(world, ref s, 1.2);
        Assert.False(train.Boiler.Ruptured);
        Assert.Equal(0, train.Boiler.Pressure);
        // Held on past it, the shovel's next: into hand to fire her back up from nothing.
        Assert.Equal(Tool.Shovel, Kit.Held(s));
    }

    [Fact]
    public void AFewPressesMendItAsWellAsAHold()
    {
        // Note 301, Sea of Thieves style: let go and press again, still at it with the wrench, and the work's kept.
        var world = World();
        var train = world.Train;
        train.Boiler.Ruptured = true;
        var s = At(world, InteractableKind.Firebox) with { HeldSlot = 1 };
        AtTheFireDoor(world, ref s);
        double held = 0;
        while (held < Tuning.Boiler.RepairSeconds + 0.2 && train.Boiler.Ruptured)
        {
            Hold(world, ref s, 1);
            held += 1;
            Hold(world, ref s, 0.5, new PlayerIntent());
        }
        Assert.False(train.Boiler.Ruptured);
        // Walk off half done, and it's started over.
        train.Boiler.Ruptured = true;
        Hold(world, ref s, 3);
        Assert.True(s.ActionProgress > 2.5);
        var away = s;
        away.Position = away.Position with { Z = away.Position.Z + 3 };
        Hold(world, ref away, 0.1, new PlayerIntent());
        Assert.Equal(0, away.ActionProgress);
    }

    [Fact]
    public void TheRepairKitRidesInTheFittersLockerInCarOne()
    {
        var world = KitWorld();
        world.EnableBodies();
        world.Stock();
        var kit = Assert.Single(world.Bodies.All, b => b.Kind == Physics.BodyKind.RepairKit);
        Assert.Equal(1, Tuning.Train.Kit.RepairKitCar);
        Assert.Equal(1, kit.Parent);
        var shape = world.Train.Frames[1].Shape;
        Assert.True(shape.Interior!.Value.Contains(kit.Centre));
        // Note 151: on the fitter's bottom shelf, its door shut, ahead of the side door and clear of the aisle.
        var bay = shape.Lockers[kit.Locker];
        Assert.Equal("FITTER", bay.Name);
        Assert.Equal(0, kit.Slot);
        Assert.True(bay.Box.Contains(kit.Centre));
        Assert.False(world.Train.Vehicles[1].LockerOpen(bay.Index));
        Assert.True(bay.Box.Max.Z < -Tuning.Train.Geometry.Interior!.SideDoorWidth / 2);
        Assert.True(bay.Box.Max.X < Tuning.Train.Geometry.Interior.DoorX - Tuning.Train.Geometry.Doorway.Width / 2 + 1e-9);
    }

    [Fact]
    public void TheFiremanMendsItWithItsWrenchWithoutLeavingTheCab()
    {
        // Note 301: nothing to fetch. The wrench into hand, at the fire door, and worked till it's whole.
        var world = World();
        var train = world.Train;
        world.EnableBodies();
        world.Stock();
        train.Boiler.Ruptured = true;
        var bot = new ConductorBot(new CrewCalls(), 1) { Fireman = true };
        var s = PlayerMotor.SpawnInCab(train, Tuning.Player, -0.5);
        bool leftTheCab = false;
        for (uint tick = 0; tick < (Tuning.Boiler.RepairSeconds + 10) * SimConstants.TickRate && train.Boiler.Ruptured; tick++)
        {
            var intent = bot.Decide(s, world, tick, out _);
            world.BeginTick();
            world.CrewAct(ref s, intent, 1);
            world.Step(new TrainControls { Reverser = 1, Brake = 1 });
            PlayerMotor.Step(ref s, intent, train, Tuning.Player, Tuning.Train, SimConstants.TickSeconds, applyLook: false);
            world.StepBodies([(1, s)]);
            leftTheCab |= !PlayerMotor.InCab(s, train);
        }
        Assert.False(train.Boiler.Ruptured);
        Assert.False(leftTheCab);
    }

    [Fact]
    public void WithoutTheWrenchRuleTheFiremanFetchesTheKitFromCarOneAndMendsIt()
    {
        var world = KitWorld();
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
