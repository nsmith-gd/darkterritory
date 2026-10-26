using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Stops;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The open houses' windows (queue #751, ARCHITECTURE §8 note 1053; GDD App. F.5: "The village's houses have real doors and
/// windows, which open and shut. In the Hollow House, a door slamming shut is what's coming, and a crewmate can still get out
/// through a door or a window while they slam"): each ground-floor window an opening through the wall with a shutter that
/// stands only while it's shut; climbed out of by walking into it, and back in; shut and opened by Use held at it; slammed by
/// the Hollow House after its doors, one still open a way out; on the doors' record; a jackpot house's barred while it's locked.
/// </summary>
[Collection(nameof(LineGenTests))]
public class HouseWindowTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly Route.Route Frontier = Routes.Generate(Content, "frontier:7", 6);
    static readonly EnemyTuning Quiet = Tuning.Enemies with
    {
        Director = Tuning.Enemies.Director with { Roster = ["hollowHouse"] },
        Dwellings = Tuning.Enemies.Dwellings with { Enabled = false },
    };

    static StopWalls Walls(RailLine? line = null) => StopWalls.Of(Frontier, line ?? Frontier.Build(), tuning: Tuning.Run.Walls);

    /// <summary>Whether a world point's inside a wall standing there now.</summary>
    static bool InAWall(StopWalls walls, Double3 p) =>
        walls.Near(p).Any(w => w.ToLocal(p) is var l && Math.Abs(l.X) <= w.HalfLength && Math.Abs(l.Z) <= w.HalfWidth && p.Y >= w.Bottom && p.Y <= w.Top);

    /// <summary>A window with room to stand at it inside and out: nothing in the way of the ladder's foot either side, nor of the step past the wall.</summary>
    static IEnumerable<HouseDoor> Clear(StopWalls walls, Func<StopWalls.OpenHouse, bool>? house = null) =>
        walls.HouseWindows.Where(w => (house is null || house(walls.OpenHouses.ElementAt(w.House)))
            && new[] { -1.0, -0.6, 0.6, 1.0 }.All(o => new[] { -0.35, 0, 0.35 }.All(s =>
                !InAWall(walls, w.At + w.Out * o + Double3.Cross(Double3.Up, w.Out) * s + Double3.Up * 0.5))));

    [Fact]
    public void AnOpenHousesWindowsAreOpeningsThroughItsWallsTheirShuttersStandingOnlyWhileShut()
    {
        var walls = Walls();
        Assert.NotEmpty(walls.HouseWindows);
        // Every open house of one floor has some, and every house's are its own (by its building).
        Assert.All(walls.OpenHouses.Where(h => !StopWalls.Storeyed(h.B)), h => Assert.Contains(walls.HouseWindows, w => w.House == h.Index));
        foreach (var w in walls.HouseWindows)
        {
            Assert.True(StopWalls.IsWindow(w.Key));
            // The opening, mid-wall at the middle of its height: nothing but its shutter in it.
            var house = walls.OpenHouses.ElementAt(w.House);
            var mid = w.At + Double3.Up * ((StopWalls.HouseWindowSill + StopWalls.HouseWindowHead) / 2);
            Assert.False(InAWall(walls, mid), $"window {w.Key} of house {w.House} ({house.B.Shape}, {house.B.Form}) is walled up");
            // And under its sill, and over its head, the wall.
            Assert.True(InAWall(walls, w.At + Double3.Up * (StopWalls.HouseWindowSill - 0.2)), $"window {w.Key}: no wall under its sill");
            Assert.True(InAWall(walls, w.At + Double3.Up * (StopWalls.HouseWindowHead + 0.2)), $"window {w.Key}: no wall over its head");
            Assert.True(walls.SetShut(w.Key, true));
            Assert.True(InAWall(walls, mid), $"window {w.Key} shut and still open");
            Assert.True(walls.SetShut(w.Key, false));
            Assert.False(InAWall(walls, mid));
        }
        // Most are climbed through, inside and out: all but those the house's own furniture stands against inside.
        int climbed = walls.HouseWindows.Count(w => walls.LaddersNear(w.At).Count(l => ((l.Foot - w.At) with { Y = 0 }).Length < 0.5) == 2);
        Assert.True(climbed > walls.HouseWindows.Count * 0.6, $"{climbed} of {walls.HouseWindows.Count} windows climbed through");
        // The same on every machine.
        var again = Walls();
        Assert.Equal(walls.All, again.All);
        Assert.Equal(walls.HouseWindows, again.HouseWindows);
        Assert.Equal(walls.Ladders, again.Ladders);
    }

    [Fact]
    public void AStoreyedHousesGroundFloorWindowsGoThroughItsShellOverItsCellarWindows()
    {
        var walls = Walls();
        var storeyed = walls.HouseWindows.Where(w => StopWalls.Storeyed(walls.OpenHouses.ElementAt(w.House).B)).ToList();
        Assert.NotEmpty(storeyed);
        var raised = storeyed.Where(w => walls.OpenHouses.ElementAt(w.House).B.Basement).ToList();
        Assert.NotEmpty(raised);
        foreach (var w in storeyed)
        {
            var mid = w.At + Double3.Up * ((StopWalls.HouseWindowSill + StopWalls.HouseWindowHead) / 2);
            Assert.False(InAWall(walls, mid), $"storeyed window {w.Key} walled up");
            Assert.True(InAWall(walls, w.At + Double3.Up * (StopWalls.HouseWindowSill - 0.2)), $"storeyed window {w.Key}: no wall under its sill");
        }
        // Never in a storeyed house's back end: its back door's there.
        foreach (var h in walls.OpenHouses.Where(h => StopWalls.Storeyed(h.B)))
            Assert.DoesNotContain(StopWalls.GroundWindows(h.B), x => !x.Run.AlongX && Math.Sign(x.Run.At) == StopWalls.BackEnd(h.B));
    }

    static PlayerState Standing(Double3 at, Double3 facing) => new()
    {
        Parent = PlayerState.World,
        Position = at,
        Surface = Surface.Ground,
        Health = Tuning.Player.Health,
        Kit = Tuning.Player.StartingKit,
        Yaw = Math.Atan2(-facing.X, -facing.Z),
    };

    static void Walk(ref PlayerState s, TrainOnLine train, double seconds, float move = 1)
    {
        for (int i = 0; i < seconds * SimConstants.TickRate; i++)
            PlayerMotor.Step(ref s, new PlayerIntent { MoveZ = s.Surface == Surface.Ladder ? 1 : move }, train, Tuning.Player, Tuning.Train, SimConstants.TickSeconds);
    }

    [Fact]
    public void AnOpenWindowIsClimbedOutOfByWalkingIntoItAndBackInButNotAShutOne()
    {
        var line = Frontier.Build();
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), line, 600);
        var walls = train.Walls = Walls(line);
        // One a floor of a house of one floor, and one of a raised house's (note 1005: its sill the plinth higher outside).
        foreach (var w in new[] { Clear(walls, h => !StopWalls.Floored(h.B)).First(), Clear(walls, h => StopWalls.Storeyed(h.B) && h.B.Basement).First() })
        {
            var house = walls.OpenHouses.ElementAt(w.House);
            double floor = w.At.Y;
            // Inside, a step from it, walking at it: up over the sill and out, on the ground outside.
            var s = Standing(w.At - w.Out * 0.8 + Double3.Up * 0.05, w.Out);
            Walk(ref s, train, 4);
            Assert.True(s.Surface == Surface.Ground && !walls.InHouse(w.House, s.Position + Double3.Up * 0.5)
                && Double3.Dot(s.Position - w.At, w.Out) > 0.3, $"{house.B.Form} house's window: at {Double3.Dot(s.Position - w.At, w.Out):0.00} m out, {s.Surface}");
            Assert.True(s.Position.Y < floor - 0.1 || !house.B.Basement, "out on the ground under a raised house's floor");
            // And back in from outside: over the sill, on the floor inside.
            s = s with { Yaw = Math.Atan2(w.Out.X, w.Out.Z) };
            Walk(ref s, train, 5);
            Assert.True(walls.InHouse(w.House, s.Position + Double3.Up * 0.5) && Math.Abs(s.Position.Y - floor) < 0.1,
                $"back in: {Double3.Dot(s.Position - w.At, w.Out):0.00} m out, {s.Position.Y - floor:0.00} m over the floor, {s.Surface}");
            // Shut, it's a wall: walk at it and you're still inside.
            walls.SetShut(w.Key, true);
            s = Standing(w.At - w.Out * 0.8 + Double3.Up * 0.05, w.Out);
            Walk(ref s, train, 4);
            Assert.True(walls.InHouse(w.House, s.Position + Double3.Up * 0.5) && s.Surface == Surface.Ground, "through a shut window");
            walls.SetShut(w.Key, false);
        }
    }

    /// <summary>The train stood at frontier:7's first stop with a one-door house of one floor and a window clear to stand at; that house.</summary>
    /// <param name="last">The window the Hollow House slams last of its house's (the lowest key).</param>
    static (Night Night, StopWalls.OpenHouse House, HouseDoor Window) AtAHouse(bool last = false)
    {
        var all = Walls();
        bool Fits(StopWalls.OpenHouse h) => all.HouseDoors.Count(d => d.House == h.Index) == 1 && !StopWalls.Floored(h.B);
        var window = Clear(all, Fits).First(w => all.DoorInReach(w.At, 2.5) is null
            && (!last || all.HouseWindows.Where(x => x.House == w.House).Min(x => x.Key) == w.Key && all.HouseWindows.Count(x => x.House == w.House) >= 3));
        var first = all.OpenHouses.ElementAt(window.House);
        var f = Frontier.Features[first.Feature];
        var n = new Night(4, speed: 0, route: Frontier, enemies: Quiet, front: f.Start + 60);
        var walls = StopWalls.Of(Frontier, n.Train.Line, tuning: Tuning.Run.Walls);
        n.Train.Walls = walls;
        return (n, walls.OpenHouses.ElementAt(first.Index), walls.HouseWindows.Single(w => w.Key == window.Key));
    }

    static void Run(Night n, double seconds, Func<int, PlayerIntent>? intent = null) => n.Run(seconds, intent);

    [Fact]
    public void UseHeldAtAWindowShutsItAndHeldAgainOpensIt()
    {
        var (n, house, w) = AtAHouse();
        var walls = n.Train.Walls!;
        n.Crew[1] = Standing(w.At - w.Out * 0.7 + Double3.Up * 0.05, w.Out);
        Assert.Equal(w.Key, n.World.DoorInReach(n.Crew[1])?.Key);
        var use = new PlayerIntent { Buttons = PlayerButtons.Use };
        Run(n, Tuning.Run.Walls.HouseDoorSeconds + 0.1, _ => use);
        Assert.True(walls.Shut(w.Key));
        // Still held, it's done; let go and hold again, and it's open.
        Run(n, 0.5, _ => use);
        Assert.True(walls.Shut(w.Key));
        Run(n, 0.1);
        Run(n, Tuning.Run.Walls.HouseDoorSeconds + 0.1, _ => use);
        Assert.False(walls.Shut(w.Key));
    }

    [Fact]
    public void TheHollowHouseSlamsItsWindowsAfterItsDoorsAndOneStillOpenIsAWayOut()
    {
        var (n, house, w) = AtAHouse(last: true);
        var walls = n.Train.Walls!;
        var t = Tuning.Enemies.HollowHouse;
        var hollow = n.World.AddEnemy(id => HollowHouse.In(id, house));
        var windows = walls.HouseWindows.Where(x => x.House == house.Index).OrderByDescending(x => x.Key).ToList();
        Assert.Equal(w.Key, windows[^1].Key);
        var door = walls.HouseDoors.Single(d => d.House == house.Index);
        // In at a window, standing still, till the rumble's over.
        n.Crew[1] = Standing(w.At - w.Out * 0.8 + Double3.Up * 0.05, w.Out);
        Run(n, t.WaitSeconds + t.RumbleSeconds + 0.05);
        Assert.Equal(HollowMode.Shut, hollow.Mode);
        Assert.True(walls.Shut(door.Key));
        // The door's slammed first, the windows after it, one at a time: the last still open.
        Assert.False(walls.Shut(windows[^1].Key));
        // Out through it, quick, before it slams.
        Run(n, 3, id => new PlayerIntent { MoveZ = 1 });
        bool out_ = !walls.InHouse(house.Index, n.Crew[1].Position + Double3.Up * 0.5);
        Run(n, t.SinkSeconds + 1.5);
        // All of them shut, and held shut.
        Assert.All(windows, x => Assert.True(walls.Shut(x.Key)));
        // Got out by its window (slammed after the door and the rest, ShutEvery apart: time enough to climb), alive.
        Assert.True(out_, $"still inside, its last window of {windows.Count}");
        Assert.True(n.Crew[1].Alive);
        Assert.Contains(hollow.Mode, new[] { HollowMode.Collapse, HollowMode.Settled });
    }

    [Fact]
    public void AClientSeesTheHostsWindowsShutOnTheDoorsRecord()
    {
        var host = Walls();
        var client = Walls();
        var w = host.HouseWindows[3];
        var d = host.HouseDoors[2];
        host.SetShut(w.Key, true);
        host.SetShut(d.Key, true);
        client.MirrorShut(host.ShutDoors);
        Assert.True(client.Shut(w.Key) && client.Shut(d.Key));
        Assert.Equal(host.ShutDoors, client.ShutDoors);
        // A key it doesn't know (a window from a later build's walls) is let be.
        client.MirrorShut([.. host.ShutDoors, StopWalls.WindowKey(4000, 1, 9)]);
        Assert.Equal(host.ShutDoors, client.ShutDoors);
        // And opened again.
        host.SetShut(w.Key, false);
        client.MirrorShut(host.ShutDoors);
        Assert.False(client.Shut(w.Key));
        // Its key names its stop, building and window.
        var (feature, building, _) = StopWalls.WindowOf(w.Key);
        var house = host.OpenHouses.ElementAt(w.House);
        Assert.Equal((house.Feature, house.Building), (feature, building));
    }

    [Fact]
    public void AJackpotHousesWindowsAreBarredWhileItsDoorsLocked()
    {
        var walls = Walls();
        var w = walls.HouseWindows.First(x => walls.HouseDoors.Count(d => d.House == x.House) == 1);
        var door = walls.HouseDoors.Single(d => d.House == w.House);
        walls.Lock([door.Key]);
        Assert.True(walls.Shut(w.Key) && walls.Barred(w.Key));
        Assert.False(walls.SetShut(w.Key, false));
        Assert.True(walls.Shut(w.Key));
        // Its lock picked and the door open: a window like any other.
        Assert.True(walls.SetShut(door.Key, false));
        Assert.False(walls.Barred(w.Key));
        Assert.True(walls.SetShut(w.Key, false));
    }
}
