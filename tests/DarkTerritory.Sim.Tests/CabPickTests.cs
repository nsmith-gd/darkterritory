using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The director's notes on build 1121 (ARCHITECTURE §8 note 264): the cab's things picked by where you look, not which is
/// nearer (the whistle cord hung over the firebox, and reaching for it got the shovel); the whistle cord a thing in the cab,
/// pulled with Use, in the puller's name; the vent on one key, held, anywhere in the cab.
/// </summary>
public class CabPickTests
{
    static World World()
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(50_000)]));
        return new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 3, 1)), line, 1_000, Tuning.Boiler));
    }

    static Interactable Thing(World world, InteractableKind kind) => world.Train.Frames[0].Shape.Interactables.First(i => i.Kind == kind);

    /// <summary>Stood at <paramref name="at"/> in the cab, looking at <paramref name="thing"/>'s aim point.</summary>
    static PlayerState Looking(World world, Double3 at, Interactable thing)
    {
        var s = PlayerMotor.SpawnInCab(world.Train, Tuning.Player);
        s.Position = at with { Y = s.Position.Y };
        var to = thing.Position + Double3.Up * thing.Aim - (s.Position + Double3.Up * Tuning.Train.Pick.EyeHeight);
        s.Yaw = Math.Atan2(-to.X, -to.Z);
        s.Pitch = Math.Atan2(to.Y, Math.Sqrt(to.X * to.X + to.Z * to.Z));
        return s;
    }

    static void Hold(World world, ref PlayerState s, double seconds, PlayerIntent intent)
    {
        for (int i = 0; i < seconds * SimConstants.TickRate; i++)
        {
            world.BeginTick();
            world.CrewAct(ref s, intent, 1);
            world.Step(new TrainControls { Reverser = 1, Brake = 1 });
        }
    }

    [Fact]
    public void TheWhistleCordHangsInTheDriversCornerOutOfTheFireboxsReach()
    {
        var world = World();
        var cord = Thing(world, InteractableKind.Whistle);
        var firebox = Thing(world, InteractableKind.Firebox);
        var cab = world.Train.Frames[0].Shape.Cab!.Value;
        // In the cab, on the driver's side (+X), up over the head where it's seen from the driver's place.
        Assert.True(cab.ContainsXZ(cord.Position));
        Assert.True(cord.Position.X > 0.5);
        Assert.True(cord.Aim > Tuning.Train.Pick.EyeHeight);
        double apart = ((cord.Position - firebox.Position) with { Y = 0 }).Length;
        Assert.True(apart > firebox.Radius, $"the cord's {apart:0.00} m from the firebox, inside its reach");
    }

    [Fact]
    public void WhereTwoAreInReachTheOneLookedAtIsWorked()
    {
        // Cab forward (note 276) the cord's at the driver's end and the firebox at the fireman's, a cab apart: the
        // neighbours now are the tool rack and the points lever on the driver's wall.
        var world = World();
        var rack = Thing(world, InteractableKind.ToolRack);
        var points = Thing(world, InteractableKind.Points);
        var at = new Double3(rack.Position.X - 0.2, 0, (rack.Position.Z + points.Position.Z) / 2 + 0.15);
        Assert.True(((at - rack.Position) with { Y = 0 }).Length < rack.Radius);
        Assert.True(((at - points.Position) with { Y = 0 }).Length < points.Radius);
        Assert.Equal(InteractableKind.ToolRack, CrewActions.Nearest(Looking(world, at, rack), world.Train));
        Assert.Equal(InteractableKind.Points, CrewActions.Nearest(Looking(world, at, points), world.Train));
    }

    [Fact]
    public void TheCordIsOnlyEverPulledLookedAt()
    {
        // In the cord's reach at the driver's place but looking down the cab, Use works whatever's nearest, never the cord.
        var world = World();
        var cord = Thing(world, InteractableKind.Whistle);
        var at = new Double3(cord.Position.X - 0.3, 0, cord.Position.Z + 0.4);
        Assert.True(((at - cord.Position) with { Y = 0 }).Length < cord.Radius);
        Assert.Equal(InteractableKind.Whistle, CrewActions.Nearest(Looking(world, at, cord), world.Train));
        var away = Looking(world, at, cord);
        away.Yaw += Math.PI;
        away.Pitch = 0;
        Assert.NotEqual(InteractableKind.Whistle, CrewActions.Nearest(away, world.Train));
    }

    [Fact]
    public void UseOnTheCordBlowsTheWhistleInThePullersNameAndTheShovelIsLeftAlone()
    {
        var world = World();
        var cord = Thing(world, InteractableKind.Whistle);
        var firebox = Thing(world, InteractableKind.Firebox);
        var s = Looking(world, new Double3(cord.Position.X - 0.3, 0, cord.Position.Z + 0.4), cord);
        double fire = world.Train.Boiler.Firebox, tender = world.Train.Boiler.Tender;
        Hold(world, ref s, 0.5, new PlayerIntent { Buttons = PlayerButtons.Use });
        Assert.True(world.WhistleSeconds > 0);
        Assert.Equal(1, world.WhistleBy);
        Assert.Equal(tender, world.Train.Boiler.Tender);
        // The puller's name rides the wire, so a client's HUD can say whose hand it is.
        var client = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 3, 1)), world.Train.Line, 1_000, Tuning.Boiler));
        var controls = new TrainControls();
        WorldRecords.Apply(WorldRecords.Capture(world, controls, []), client, ref controls, []);
        Assert.Equal(1, client.WhistleBy);
        // At the firebox instead (cab forward, note 276: across the cab at the fireman's end), the same press shovels, and
        // the whistle's let go.
        for (int i = 0; i < 2 * SimConstants.TickRate; i++)
        {
            world.BeginTick();
            world.Step(new TrainControls { Reverser = 1, Brake = 1 });
        }
        Assert.Equal(0, world.WhistleSeconds);
        s = Looking(world, firebox.Position + new Double3(0, 0, -0.45), firebox);
        Hold(world, ref s, 2 * Tuning.Boiler.ShovelSeconds + 0.1, new PlayerIntent { Buttons = PlayerButtons.Use });
        Assert.Equal(0, world.WhistleSeconds);
        Assert.True(world.Train.Boiler.Tender < tender);
        _ = fire;
    }

    [Fact]
    public void TheWhistlersWhistleHasNoHandOnTheCord()
    {
        // GDD App. A.4: "the whistle sounds with no hand on the cord". It blows in nobody's name, which is how the HUD tells
        // it from a crewmate's (note 264, the director's "the whistle went off for some reason").
        var world = World();
        world.Whistled(Tuning.Enemies.Whistler.WhistleSeconds);
        Assert.Equal(-1, world.WhistleBy);
        Assert.True(world.WhistleSeconds > 0);
    }

    [Fact]
    public void TheVentKeyHeldAnywhereInTheCabVentsAndNowhereElse()
    {
        var world = World();
        var s = PlayerMotor.SpawnInCab(world.Train, Tuning.Player);
        world.Train.Boiler.Pressure = 90;
        Hold(world, ref s, 2, new PlayerIntent { Actions = PlayerActions.Vent });
        Assert.True(world.Train.Boiler.Pressure < 80, $"pressure {world.Train.Boiler.Pressure:0}");
        Assert.True(world.Train.Boiler.Vented);
        // Let go, it shuts.
        Hold(world, ref s, 0.2, default);
        Assert.False(world.Train.Boiler.Vented);
        // Out of the cab (on a roof) the key does nothing to the boiler.
        var roof = PlayerMotor.SpawnOnRoof(world.Train, 1, 0, Tuning.Player);
        Hold(world, ref roof, 0.5, new PlayerIntent { Actions = PlayerActions.Vent });
        Assert.False(world.Train.Boiler.Vented);
    }
}
