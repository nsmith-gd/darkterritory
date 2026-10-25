using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Stops;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Creatures in houses with floors (queue #726, ARCHITECTURE §8 note 1023; #710's houses, note 1005; the director, 23 Oct 2026:
/// a basement is "high risk, high reward"). The Lodger waits in the dark of a basement and comes up its hatch and the flight
/// after whoever it shrieked at; a creature hunting a crewmate into a house with floors follows them up the flight, never
/// through the boards; nothing reaches through a floor; and which houses with floors something lives in.
/// </summary>
[Collection(nameof(LineGenTests))]
public class HouseFloorsTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly Route.Route Frontier = Routes.Generate(Content, "frontier:7", 6);
    static readonly EnemyTuning Quiet = Tuning.Enemies with
    {
        Director = Tuning.Enemies.Director with { Roster = ["lodger", "householder", "hollowHouse", "hanger"] },
        Dwellings = Tuning.Enemies.Dwellings with { Enabled = false },
    };

    /// <summary>The train stood at the first stop with a house with both floors, the stops' walls up; and that house.</summary>
    static (Night Night, StopWalls.OpenHouse House) AtAHouseWithFloors()
    {
        var all = StopWalls.Of(Frontier, Frontier.Build(), tuning: Tuning.Run.Walls);
        var first = all.OpenHouses.First(h => StopWalls.Storeyed(h) && h.B.Basement && h.B.Upstairs);
        var f = Frontier.Features[first.Feature];
        var n = new Night(4, speed: 0, route: Frontier, enemies: Quiet, front: f.Start + 60);
        n.World.EnableBodies();
        var walls = StopWalls.Of(Frontier, n.Train.Line, tuning: Tuning.Run.Walls);
        n.Train.Walls = walls;
        return (n, walls.OpenHouseAt(first.Index));
    }

    static StopWalls Walls(Night n) => n.Train.Walls!;

    /// <summary>A crewmate standing at a world point (on a floor's boards: the motor stands them on whatever's under them).</summary>
    static PlayerState Standing(Night n, Double3 at) =>
        new() { Parent = PlayerState.World, Position = at, Surface = Surface.Ground, Health = Tuning.Player.Health, Kit = Tuning.Player.StartingKit, LineHint = n.Train.Dynamics.Distance };

    /// <summary>A point of a house's plan on one of its floors, in the world.</summary>
    static Double3 On(StopWalls.OpenHouse h, double x, double y, int level) => h.World(x, y) with { Y = StopWalls.FloorY(h, level) };

    /// <summary>The upper floor's far end from the flight's top, clear of the stairwell (the end the flight's foot is under).</summary>
    static Double3 UpstairsFarEnd(StopWalls.OpenHouse h)
    {
        var (x0, dir, y0, y1) = StopWalls.Stair(h.B);
        double ix = h.B.Length / 2 - StopWalls.WallThickness - 0.6, across = -Math.Sign(y0) * (h.B.Width / 2 - StopWalls.WallThickness - 0.8);
        return On(h, -dir * ix, across, 1);
    }

    static void Run(Night n, double seconds, Action? each = null)
    {
        for (int i = 0; i < seconds * SimConstants.TickRate; i++)
        {
            n.Run(SimConstants.TickSeconds);
            n.World.StepBodies([.. n.Crew.Select(c => (c.Key, c.Value))]);
            each?.Invoke();
        }
    }

    /// <summary>
    /// Where a creature is in a house with floors, each tick: on a floor's boards, on the flight, or in the hatch (straight up
    /// and down its ladder); anything else (in a floor, under the boards, in the air) is a fault, said.
    /// </summary>
    sealed class Watch(StopWalls walls, StopWalls.OpenHouse h, Enemy e)
    {
        public bool Flight, Hatch, Upstairs, Basement;
        public string? Fault;

        public void Look()
        {
            if (!walls.InHouse(h.Index, e.Local + Double3.Up * 0.3))
                return;
            int level = StopWalls.LevelOf(h, e.Local.Y);
            bool onFloor = Math.Abs(e.Local.Y - StopWalls.FloorY(h, level)) < 0.02;
            bool onFlight = StopWalls.OnFlight(h, e.Local);
            var (top, foot, _, _) = StopWalls.Hatchway(h)!.Value;
            bool inHatch = HouseWays.Flat(e.Local - foot) < StopWalls.FloorWayAt + 0.05 && e.Local.Y > foot.Y && e.Local.Y < top.Y;
            Flight |= onFlight;
            Hatch |= inHatch;
            Upstairs |= onFloor && level == 1;
            Basement |= onFloor && level == -1;
            if (!onFloor && !onFlight && !inHatch)
                Fault ??= $"{e.Kind} at {e.Local.Y - h.Origin.Y:0.00} m over the house's ground, on no floor, flight or hatch";
        }
    }

    [Fact]
    public void TheLodgerWaitsInTheBasementAndComesUpTheHatchAndTheFlightAfterWhoeverItShriekedAt()
    {
        var (n, house) = AtAHouseWithFloors();
        var lodger = n.World.AddEnemy(id => Lodger.In(id, house, Walls(n), Tuning.Enemies.Lodger));
        // Its corner's in the basement, on its floor.
        Assert.Equal(-1, StopWalls.LevelOf(house, lodger.Hide.Y));
        Assert.Equal(StopWalls.FloorY(house, -1), lodger.Hide.Y, 6);
        // On the ground floor over it, a while: not on its floor, and it doesn't stir (lodger.ownFloor).
        var (hatchTop, hatchFoot, inward, onto) = StopWalls.Hatchway(house)!.Value;
        n.Crew[1] = Standing(n, hatchTop + inward * (onto + 0.3));
        Run(n, Tuning.Enemies.Lodger.NoticeSeconds + 2);
        Assert.Equal(LodgerMode.Hidden, lodger.Mode);
        // Down in its basement, by the ladder: it shrieks. Then they're up the ladder, up the flight and at the far end upstairs.
        n.Crew[1] = Standing(n, hatchFoot + inward * 0.4);
        Run(n, Tuning.Enemies.Lodger.NoticeSeconds + 0.2);
        Assert.Equal(LodgerMode.Shriek, lodger.Mode);
        n.Crew[1] = Standing(n, UpstairsFarEnd(house));
        var watch = new Watch(Walls(n), house, lodger);
        Run(n, 30, () =>
        {
            watch.Look();
            // (Never through the boards: nothing reaches a crewmate on another floor.)
            Assert.False(lodger.Phase == SpinePhase.Grab && Walls(n).FloorsApart(lodger.Local, n.Crew[1].Position), "grabbed through a floor");
        });
        Assert.Null(watch.Fault);
        Assert.True(watch.Hatch, "never came up the hatch");
        Assert.True(watch.Flight, "never took the flight");
        Assert.True(watch.Upstairs, "never got upstairs");
        // Up there with them, it had them.
        Assert.False(n.Crew[1].Alive);
        Assert.Equal(DeathCause.Lodger, n.Crew[1].Death);
        n.AssertFair();
    }

    [Fact]
    public void AHouseholderHuntingItsThingFollowsTheCarrierIntoAHouseWithFloorsAndUpTheFlight()
    {
        var (n, upstairs) = AtAHouseWithFloors();
        var walls = Walls(n);
        // A Householder at its table in the nearest house of one floor, and one of its things.
        var home = walls.OpenHouses.Where(h => !StopWalls.Storeyed(h) && h.Feature == upstairs.Feature && walls.HouseDoors.Count(d => d.House == h.Index) == 1)
            .OrderBy(h => HouseWays.Flat(h.Middle - upstairs.Middle)).First();
        var it = n.World.AddEnemy(id => Householder.In(id, home, walls, Tuning.Enemies.Householder));
        var door = walls.HouseDoors.Single(d => d.House == home.Index);
        var thing = n.World.Bodies.SpawnItem(home.Middle - (door.At - home.Middle).Normalized * 1.0, n.Train.Dynamics.Distance, BodyKind.RepairKit);
        Run(n, 1);
        Assert.Contains(thing.Id, it.Mine);
        // Carried out unpaid, it rises; the carrier's gone up the flight of the house with floors, to its far end.
        n.Crew[1] = Standing(n, door.At + door.Out * 3);
        thing.Carrier = 1;
        Run(n, Tuning.Enemies.Householder.RiseSeconds + 0.5);
        Assert.Equal(HouseholderMode.Hunt, it.Mode);
        n.Crew[1] = Standing(n, UpstairsFarEnd(upstairs));
        var watch = new Watch(walls, upstairs, it);
        bool grabbed = false;
        Run(n, 60, () =>
        {
            watch.Look();
            grabbed |= it.Phase == SpinePhase.Grab;
            Assert.False(it.Phase == SpinePhase.Grab && walls.FloorsApart(it.Local, n.Crew[1].Position), "grabbed through a floor");
        });
        Assert.Null(watch.Fault);
        Assert.True(watch.Flight, $"never took the flight: {it.Mode} at {it.Local.Y - upstairs.Origin.Y:0.00} m, {HouseWays.Flat(it.Local - n.Crew[1].Position):0.0} m off");
        Assert.True(grabbed, $"never had them: {it.Mode} at {it.Local.Y - upstairs.Origin.Y:0.00} m, {HouseWays.Flat(it.Local - n.Crew[1].Position):0.0} m off");
        n.AssertFair();
    }

    [Fact]
    public void NothingReachesThroughAFloor()
    {
        var (n, house) = AtAHouseWithFloors();
        var walls = Walls(n);
        var (hatchTop, hatchFoot, _, _) = StopWalls.Hatchway(house)!.Value;
        // Straight over each other, a floor between: blocked; on one floor, not.
        Assert.True(HouseWays.Blocked(walls, hatchFoot, hatchFoot with { Y = StopWalls.FloorY(house, 0) }));
        Assert.True(walls.FloorsApart(On(house, 0, 0, 0), On(house, 0, 0, 1)));
        Assert.False(walls.FloorsApart(On(house, 0, 0, 1), On(house, 0.5, 0.5, 1)));
        // The way from upstairs to the basement: to the flight's top, down it, to the hatch, down its ladder.
        var (foot, top) = StopWalls.Flight(house)!.Value;
        var from = UpstairsFarEnd(house);
        Assert.Equal(new FloorWay(FloorLeg.Walk, top, house.Index), walls.Way(from, hatchFoot));
        Assert.Equal(FloorLeg.Stairs, walls.Way(top, hatchFoot)!.Value.Leg);
        Assert.Equal(new FloorWay(FloorLeg.Walk, hatchTop, house.Index), walls.Way(foot, hatchFoot));
        Assert.Equal(new FloorWay(FloorLeg.Hatch, hatchFoot, house.Index), walls.Way(hatchTop, hatchFoot));
        // On one floor, nothing in the way.
        Assert.Null(walls.Way(from, UpstairsFarEnd(house) + new Double3(0.3, 0, 0)));
    }

    [Fact]
    public void AHouseWithFloorsHasOnlyItsFloorsKindsAndTheOthersAsOften()
    {
        // Note 1023: a house with floors draws as a house of one floor does; the kinds that don't live on floors leave it empty.
        var t = Tuning.Enemies.Dwellings;
        int lodgers = 0, floors = 0;
        foreach (var spec in new[] { "frontier:1", "frontier:2", "frontier:3", "frontier:7", "deadLines:1", "deadLines:2", "deepTerritory:1" })
        {
            var route = Routes.Generate(Content, spec, 6);
            var walls = StopWalls.Of(route, route.Build(), tuning: Tuning.Run.Walls);
            foreach (var h in walls.OpenHouses.Where(h => h.Feature >= 0 && route.Features[h.Feature].Stop is not null && StopWalls.Storeyed(h)))
            {
                floors++;
                var kind = Dwelling.Lives(route, route.Features[h.Feature].Stop!, h, t, null);
                Assert.True(kind is null || Dwelling.OnFloors(kind.Value, t), $"{spec}: a {kind} in a house with floors");
                lodgers += kind == EnemyKind.Lodger ? 1 : 0;
            }
        }
        Assert.True(floors > 10 && lodgers > 0, $"{lodgers} Lodgers in {floors} houses with floors");
    }
}
