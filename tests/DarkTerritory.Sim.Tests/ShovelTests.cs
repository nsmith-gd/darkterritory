using Ballast;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// GDD §12 and App. C.2 (WP19, note 275): coal goes on with the fireman's shovel in hand, and there's the one, by the cab's
/// tool rack. Use at the firebox takes it into hand; taken off to fight with, the fire waits for whoever has it.
/// </summary>
public class ShovelTests
{
    static World World()
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(50_000)]));
        return new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 3, 1)), line, 1_000, Tuning.Boiler));
    }

    /// <summary>
    /// Stood in the cab a step short of <paramref name="kind"/> (towards the cab's middle) and looking at it, as note 264's
    /// pick by look wants; the cab forward (note 276) has the firebox door in the back wall and the rack on the right.
    /// </summary>
    static PlayerState At(World world, InteractableKind kind)
    {
        var thing = world.Train.Frames[0].Shape.Interactables.First(i => i.Kind == kind);
        var s = PlayerMotor.SpawnInCab(world.Train, Tuning.Player);
        var stand = kind == InteractableKind.Firebox ? thing.Position + new Double3(0.2, 0, -0.6)
            : thing.Position + new Double3(-Math.Sign(thing.Position.X) * 0.55, 0, 0);
        s.Position = stand with { Y = s.Position.Y };
        var to = thing.Position + Double3.Up * thing.Aim - (s.Position + Double3.Up * Tuning.Train.Pick.EyeHeight);
        s.Yaw = Math.Atan2(-to.X, -to.Z);
        s.Pitch = Math.Atan2(to.Y, Math.Sqrt(to.X * to.X + to.Z * to.Z));
        return s;
    }

    static void Hold(World world, ref PlayerState s, double seconds, int id = 1)
    {
        for (int i = 0; i < seconds * SimConstants.TickRate; i++)
        {
            world.BeginTick();
            world.CrewAct(ref s, new PlayerIntent { Buttons = PlayerButtons.Use }, id);
            world.Step(new TrainControls { Reverser = 1, Brake = 1 });
        }
    }

    [Fact]
    public void TheStartingKitIsTheCrowbarAndTheShovelIsTheBoilers()
    {
        // player.json: everyone's crowbar. The shovel isn't anyone's: it starts the night home, on the cab's rack.
        Assert.Equal(Kit.Of([Tool.Crowbar]), Tuning.Player.StartingKit);
        Assert.True(Tuning.Boiler.ShovelInHand);
        Assert.False(World().Train.Boiler.ShovelOut);
    }

    [Fact]
    public void UseAtTheFireboxTakesTheShovelIntoHandAndFires()
    {
        var world = World();
        var train = world.Train;
        train.Boiler.Firebox = 0;
        var s = At(world, InteractableKind.Firebox);
        Assert.Equal(Tool.Crowbar, Kit.Held(s));
        Hold(world, ref s, Tuning.Boiler.ShovelSeconds * 2 + 0.1);
        Assert.Equal(Tool.Shovel, Kit.Held(s));
        Assert.Equal(Kit.Of([Tool.Crowbar, Tool.Shovel]), s.Kit);
        Assert.True(train.Boiler.ShovelOut);
        Assert.True(train.Boiler.Firebox >= 1.5, $"firebox {train.Boiler.Firebox}");
    }

    /// <summary>The whole crew acting each tick, as a host has them: crewmate i is player i + 1.</summary>
    static void Crew(World world, PlayerState[] crew, Func<int, PlayerIntent> intent, double seconds)
    {
        for (int t = 0; t < seconds * SimConstants.TickRate; t++)
        {
            world.BeginTick();
            for (int i = 0; i < crew.Length; i++)
                world.CrewAct(ref crew[i], intent(i), i + 1);
            world.Step(new TrainControls { Reverser = 1, Brake = 1 });
        }
    }

    [Fact]
    public void WithTheShovelOutTheFireWaitsForWhoeverHasIt()
    {
        var world = World();
        var train = world.Train;
        var use = new PlayerIntent { Buttons = PlayerButtons.Use };
        PlayerState[] crew = [At(world, InteractableKind.Firebox), At(world, InteractableKind.Firebox)];
        Crew(world, crew, i => i == 0 ? use : default, 0.1);
        Assert.True(Kit.Has(crew[0].Kit, Tool.Shovel));
        // Off to fight with it, the crowbar back in hand: a second crewmate at the firebox gets nothing on.
        crew[0].HeldSlot = 0;
        double fire = train.Boiler.Firebox;
        Crew(world, crew, i => i == 1 ? use : default, Tuning.Boiler.ShovelSeconds * 3);
        Assert.False(Kit.Has(crew[1].Kit, Tool.Shovel));
        Assert.True(train.Boiler.ShovelOut);
        Assert.True(train.Boiler.Firebox <= fire, $"firebox {fire} → {train.Boiler.Firebox}");
        Assert.False(CrewActions.HasShovel(crew[1], train));
        // The one who has it, back at the firebox: it comes out of their own kit into hand.
        Assert.True(CrewActions.HasShovel(crew[0], train));
        Crew(world, crew, i => i == 0 ? use : default, Tuning.Boiler.ShovelSeconds + 0.1);
        Assert.Equal(Tool.Shovel, Kit.Held(crew[0]));
        Assert.True(train.Boiler.Firebox > fire - 0.2);
    }

    [Fact]
    public void TheShovelHangsBackOnTheRackForTheNextFireman()
    {
        var world = World();
        var train = world.Train;
        var s = At(world, InteractableKind.Firebox);
        Hold(world, ref s, 0.1);
        Assert.True(train.Boiler.ShovelOut);
        var atRack = At(world, InteractableKind.ToolRack);
        (s.Position, s.Yaw, s.Pitch) = (atRack.Position, atRack.Yaw, atRack.Pitch);
        Hold(world, ref s, 0.5);
        Assert.False(Kit.Has(s.Kit, Tool.Shovel));
        Assert.False(train.Boiler.ShovelOut);
        // Hung back, not the wrench taken in its place.
        Assert.False(Kit.Has(s.Kit, Tool.Wrench));
        var next = At(world, InteractableKind.Firebox);
        Hold(world, ref next, 0.1, id: 2);
        Assert.Equal(Tool.Shovel, Kit.Held(next));
    }

    [Fact]
    public void AShovelNobodyHasIsBackOnItsRackAndOneOnABodyIsNot()
    {
        var world = World();
        world.EnableBodies();
        var train = world.Train;
        train.Boiler.ShovelOut = true;
        var s = At(world, InteractableKind.Vent);
        // On the dead fireman's body, it's out: lifting the body takes it.
        var dead = PlayerMotor.SpawnInCab(train, Tuning.Player) with { Kit = Kit.Of([Tool.Crowbar, Tool.Shovel]), Death = DeathCause.Mauled };
        world.StepBodies([(3, dead)]);
        var body = world.Bodies.All.Single(b => b.Kind == BodyKind.Ragdoll);
        Assert.True(body.HasTool(Tool.Shovel));
        world.BeginTick();
        world.CrewAct(ref s, default, 1);
        world.Step(new TrainControls { Reverser = 1, Brake = 1 });
        Assert.True(train.Boiler.ShovelOut);
        var lifter = s with { Kit = Kit.Of([Tool.Crowbar]) };
        Bodies.TakeTools(ref lifter, body);
        Assert.True(Kit.Has(lifter.Kit, Tool.Shovel));
        // Nowhere (its carrier's left the session, its body gone with a car): it's home again.
        body.Tools = 0;
        world.BeginTick();
        world.CrewAct(ref s, default, 1);
        world.Step(new TrainControls { Reverser = 1, Brake = 1 });
        Assert.False(train.Boiler.ShovelOut);
    }

    [Fact]
    public void WhereTheShovelIsReplicates()
    {
        var host = World();
        host.Train.Boiler.ShovelOut = true;
        var client = World();
        var controls = new TrainControls();
        WorldRecords.Apply(WorldRecords.Capture(host, controls, []), client, ref controls, []);
        Assert.True(client.Train.Boiler.ShovelOut);
    }

    [Fact]
    public void WithTheRuleOffCoalGoesOnByHand()
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(50_000)]));
        var world = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 3, 1)), line, 1_000, Tuning.Boiler with { ShovelInHand = false }));
        world.Train.Boiler.Firebox = 0;
        world.Train.Boiler.ShovelOut = true;
        var s = At(world, InteractableKind.Firebox);
        Hold(world, ref s, Tuning.Boiler.ShovelSeconds + 0.1);
        Assert.Equal(Kit.Of([Tool.Crowbar]), s.Kit);
        Assert.True(world.Train.Boiler.Firebox > 0.5);
    }
}
