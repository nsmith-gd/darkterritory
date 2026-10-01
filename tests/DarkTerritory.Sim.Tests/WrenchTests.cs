using Ballast;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// T109 playtest: the vent in the cab, and a ruptured boiler mended with the engineering kit (a wrench) from its rack in the
/// cab, so one player can do it all from the footplate.
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
    public void TheWrenchFromItsRackMendsARupturedBoilerAtTheFirebox()
    {
        var world = World();
        var train = world.Train;
        train.Boiler.Ruptured = true;
        var s = At(world, InteractableKind.ToolRack);
        Hold(world, ref s, 0.5);
        Assert.Equal(Tool.Wrench, Kit.Held(s));
        Assert.True(train.Boiler.WrenchOut);
        // Only the one: a second hand at the rack gets nothing.
        var other = At(world, InteractableKind.ToolRack);
        Hold(world, ref other, 0.5);
        Assert.False(Kit.Has(other.Kit, Tool.Wrench));
        // At the firebox with it, held: mended, cold and empty.
        var firebox = train.Frames[0].Shape.Interactables.First(i => i.Kind == InteractableKind.Firebox).Position;
        s.Position = s.Position with { X = 0.35, Z = firebox.Z + 0.5 };
        Hold(world, ref s, Tuning.Boiler.RepairSeconds - 1);
        Assert.True(train.Boiler.Ruptured);
        Hold(world, ref s, 1.2);
        Assert.False(train.Boiler.Ruptured);
        Assert.Equal(0, train.Boiler.Pressure);
        // A crowbar does nothing for it: the wrench it is.
        train.Boiler.Ruptured = true;
        var crowbar = s with { HeldSlot = 0 };
        Hold(world, ref crowbar, Tuning.Boiler.RepairSeconds + 1);
        Assert.True(train.Boiler.Ruptured);
    }

    [Fact]
    public void TheFiremanMendsItHimself()
    {
        var world = World();
        var train = world.Train;
        train.Boiler.Ruptured = true;
        var bot = new ConductorBot(new CrewCalls(), 1) { Fireman = true };
        var s = PlayerMotor.SpawnInCab(train, Tuning.Player, -0.5);
        for (uint tick = 0; tick < (Tuning.Boiler.RepairSeconds + 20) * SimConstants.TickRate && train.Boiler.Ruptured; tick++)
        {
            var intent = bot.Decide(s, world, tick, out _);
            world.BeginTick();
            world.CrewAct(ref s, intent, 1);
            world.Step(new TrainControls { Reverser = 1, Brake = 1 });
            PlayerMotor.Step(ref s, intent, train, Tuning.Player, Tuning.Train, SimConstants.TickSeconds, applyLook: false);
        }
        Assert.False(train.Boiler.Ruptured);
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
