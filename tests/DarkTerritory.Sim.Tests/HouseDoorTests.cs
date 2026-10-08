using Ballast;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Stops;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Doors that shut in the village houses (queue #137, ARCHITECTURE §8 note 401; note 326's "not yet"): an open house's door
/// hangs open until a crewmate holds Use at it; shut, it stands in the doorway as a wall, and a house with every door shut
/// is a space of its own, as a shut car is (GDD §21: the Choir takes anyone "not behind a closed door").
/// </summary>
[Collection(nameof(LineGenTests))]
public class HouseDoorTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly LootTuning L = DataFile.Load<LootTuning>(Path.Combine(Content, LootTuning.File));
    static readonly WallTuning W = Tuning.Run.Walls;
    static readonly PlayerIntent Use = new() { Buttons = PlayerButtons.Use };

    static World Night(bool authority)
    {
        var route = Routes.Generate(Content, "frontier:7", 6);
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 4, 0)), route.Build(), 3_000, Tuning.Boiler);
        var world = new World(train, Tuning.Combat);
        if (authority)
            world.EnableBodies();
        world.EnableRun(Tuning.Run, route, route.GateOr(Tuning.Route.YardLength), authority: authority, loot: L);
        train.Walls = StopWalls.Of(route, train.Line, tuning: W);
        return world;
    }

    /// <summary>A house of one door at the first stop with a village: its door.</summary>
    static HouseDoor Door(World world)
    {
        var walls = world.Train.Walls!;
        var stop = world.Train.Line.Sample(world.Run!.Stops.First(f => f.Stop!.Buildings.Any(b => b.Open)).Start).Position;
        return walls.HouseDoors.Where(d => walls.HouseDoors.Count(o => o.House == d.House) == 1)
            .OrderBy(d => (d.At - stop).Length).First();
    }

    static PlayerState At(World world, Double3 at)
    {
        double hint = world.Run!.Stops[0].Start;
        return PlayerMotor.SpawnOnGround(at with { Y = PlayerMotor.GroundAt(at, world.Train.Line, ref hint) }, world.Train.Line, hint, Tuning.Player);
    }

    static void Act(World world, ref PlayerState me, PlayerIntent intent, double seconds, int id = 1)
    {
        for (int i = 0; i < Math.Round(seconds * SimConstants.TickRate); i++)
        {
            world.BeginTick();
            world.CrewAct(ref me, intent, id);
        }
    }

    static bool InAWall(StopWalls walls, Double3 p) =>
        walls.Near(p).Any(w => Math.Abs(w.ToLocal(p).X) <= w.HalfLength && Math.Abs(w.ToLocal(p).Z) <= w.HalfWidth);

    [Fact]
    public void AHeldUseShutsAHouseDoorAndItStandsInTheDoorwayTillItsOpenedAgain()
    {
        var world = Night(authority: true);
        var walls = world.Train.Walls!;
        var door = Door(world);
        // Every open house's doorway has a door, hanging open.
        Assert.NotEmpty(walls.HouseDoors);
        Assert.Empty(walls.ShutDoors);
        var doorway = door.At - door.Out * (StopWalls.WallThickness / 2);
        Assert.False(InAWall(walls, doorway));

        // On the step: held short of houseDoorSeconds, nothing; held through, it's shut, and it stands in the doorway.
        var me = At(world, door.At + door.Out * 0.6);
        Assert.Equal(door, world.DoorInReach(me));
        Act(world, ref me, Use, W.HouseDoorSeconds - 0.2);
        Assert.False(walls.Shut(door.Key));
        Act(world, ref me, Use, 0.3);
        Assert.True(walls.Shut(door.Key));
        Assert.True(InAWall(walls, doorway));
        // Held on, it stays shut (once a hold); let go and held again, it's open, and the doorway's clear.
        Act(world, ref me, Use, W.HouseDoorSeconds * 2);
        Assert.True(walls.Shut(door.Key));
        Act(world, ref me, default, 0.1);
        Act(world, ref me, Use, W.HouseDoorSeconds + 0.1);
        Assert.False(walls.Shut(door.Key));
        Assert.False(InAWall(walls, doorway));

        // Not from further off than houseDoorReachM, and not with something in your arms.
        var far = At(world, door.At + door.Out * (W.HouseDoorReachM + 0.5));
        Assert.Null(world.DoorInReach(far));
        Act(world, ref far, Use, W.HouseDoorSeconds + 0.1);
        Assert.False(walls.Shut(door.Key));
    }

    [Fact]
    public void AHouseShutUpIsASpaceOfItsOwnAsAShutCarIs()
    {
        var world = Night(authority: true);
        var walls = world.Train.Walls!;
        var door = Door(world);
        var inside = At(world, door.At - door.Out * 1.5);
        var outside = At(world, door.At + door.Out * 2);
        // Open, inside's as outside as the street: the Choir's (GDD §21, Structural's exposed crew).
        Assert.Equal(PlayerMotor.Outside, PlayerMotor.Space(inside, world.Train));
        Assert.True(walls.SetShut(door.Key, true));
        // Shut, it's the house's own, and the street's still outside.
        Assert.Equal(PlayerMotor.HouseSpace(door.House), PlayerMotor.Space(inside, world.Train));
        Assert.True(PlayerMotor.Space(inside, world.Train) < PlayerMotor.Outside);
        Assert.Equal(PlayerMotor.Outside, PlayerMotor.Space(outside, world.Train));
        // A pair of cottages (a building of two doors) is two homes, each shut up behind its own door (note 453).
        var pair = walls.HouseDoors.GroupBy(d => (d.Key - 1) >> 2).First(g => g.Count() == 2).ToList();
        var (one, other) = (pair[0], pair[1]);
        Assert.NotEqual(one.House, other.House);
        var inOne = At(world, one.At - one.Out * 1.5);
        var inOther = At(world, other.At - other.Out * 1.5);
        walls.SetShut(one.Key, true);
        Assert.Equal(PlayerMotor.HouseSpace(one.House), PlayerMotor.Space(inOne, world.Train));
        Assert.Equal(PlayerMotor.Outside, PlayerMotor.Space(inOther, world.Train));
        walls.SetShut(other.Key, true);
        Assert.Equal(PlayerMotor.HouseSpace(other.House), PlayerMotor.Space(inOther, world.Train));
        Assert.Equal(PlayerMotor.HouseSpace(one.House), PlayerMotor.Space(inOne, world.Train));
    }

    [Fact]
    public void ABotsWayIntoAHouseIsByItsDoorOpen()
    {
        var world = Night(authority: true);
        var walls = world.Train.Walls!;
        var door = Door(world);
        var from = door.At + door.Out * 6;
        var into = door.At - door.Out * 1.5;
        Assert.NotNull(FootPath.Plan(world.Train, from, into));
        walls.SetShut(door.Key, true);
        Assert.Null(FootPath.Plan(world.Train, from, into));
    }

    [Fact]
    public void EveryCrewmateSeesTheDoorsShut()
    {
        var host = Night(authority: true);
        var client = Night(authority: false);
        var door = Door(host);
        var me = At(host, door.At + door.Out * 0.6);
        var controls = new TrainControls();
        var players = new List<PlayerSnapshot>();
        Act(host, ref me, Use, W.HouseDoorSeconds + 0.1);
        WorldRecords.Apply(WorldRecords.Capture(host, controls, players), client, ref controls, players);
        Assert.True(client.Train.Walls!.Shut(door.Key));
        Assert.Equal(host.Train.Walls!.ShutDoors, client.Train.Walls.ShutDoors);
        // And so does the client's own crewmate inside: the house is shut up there too.
        var inside = At(client, door.At - door.Out * 1.5);
        Assert.Equal(PlayerMotor.HouseSpace(door.House), PlayerMotor.Space(inside, client.Train));
        Act(host, ref me, default, 0.1);
        Act(host, ref me, Use, W.HouseDoorSeconds + 0.1);
        WorldRecords.Apply(WorldRecords.Capture(host, controls, players), client, ref controls, players);
        Assert.False(client.Train.Walls.Shut(door.Key));
    }
}
