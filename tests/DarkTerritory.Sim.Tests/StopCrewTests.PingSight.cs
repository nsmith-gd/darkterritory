using Ballast;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Stops;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 1010 (the director's playtest of 23 Oct, `docs/playtest/2026-10-23.md` lines 9 and 10, one bot crewmate): "even if I
/// just sort of like point in the direction of a village, like an abandoned village ... he should get the concept of like,
/// okay, we're gonna go loot this place"; "I can just, like, throw my ping around to find random loot. It should check a cast
/// to be like, can you actually see this or not?"
/// </summary>
public partial class StopCrewTests
{
    /// <summary>The stop's village: its open houses, and the stop's place among the run's stops.</summary>
    static (List<StopWalls.OpenHouse> Houses, int Stop) Village(Night night)
    {
        var run = night.World.Run!;
        var (k, feature) = run.StopOf(night.Site.Feature);
        return (night.Train.Walls!.OpenHouses.Where(h => h.Feature == feature).ToList(), k);
    }

    [Fact]
    public void AVillagePointedAtBeforeTheTrainsStoppedThereIsTheTownToSearch()
    {
        // "point in the direction of a village": the train not yet stood at the stop (no current site), the one playing beside
        // it, looking towards the middle of the village's houses. Before, a town was only pinged at the stop the train stood at.
        var night = new Night(cars: 6, walkers: 0, hands: 0, ids: true, loot: true, from: 60, modules: ModuleKind.Crates);
        var train = night.Train;
        var world = night.World;
        Assert.Null(world.Run!.CurrentSite);
        var (houses, k) = Village(night);
        Assert.NotEmpty(houses);
        var middle = houses.Aggregate(Double3.Zero, (a, h) => a + h.Middle) * (1.0 / houses.Count) + Double3.Up * 2;
        var engine = train.Frames[0].Origin;
        // Stood on the ground 100 m off the village's middle (or beside the engine, if that's nearer).
        var flat = (engine - middle) with { Y = 0 };
        var stand = flat.Length > 100 ? middle + flat.Normalized * 100 : engine + new Double3(4, 0, 0);
        var ping = Pings.Resolve(world, LookingFrom(train, stand with { Y = engine.Y }, middle), P.Pings);
        Assert.Equal((PingKind.Town, k), (ping?.Kind, ping?.Target));
    }

    /// <summary>
    /// A plain open house of the stop's village (one room, a door in its front), its doors open, and a find just inside its
    /// door; where to stand outside its back wall, and outside its door, to look at the find.
    /// </summary>
    static (Physics.Body Find, Double3 Behind, Double3 InFront, Double3 At) FindInAHouse(Night night)
    {
        var train = night.Train;
        var walls = train.Walls!;
        var (houses, _) = Village(night);
        var house = houses.Where(h => !StopWalls.Composite(h.B) && h.Part < 0).OrderBy(h => (h.Middle - train.Frames[2].Origin).Length).First();
        foreach (int key in house.Doors)
            walls.SetShut(key, false);
        var (fx, fy) = StopWalls.Front(house.B);
        var front = (house.Ex * fx + house.Ey * fy).Normalized;
        double half = fx != 0 ? house.B.Length / 2 : house.B.Width / 2;
        var at = house.Middle + front * (half - 1.0) + Double3.Up * 0.3;
        var find = night.World.Bodies.SpawnLoot(at, train.Dynamics.Distance, -1, 0.2);
        var centre = Physics.Bodies.WorldCentre(find, train);
        return (find, house.Middle - front * (half + 6), house.Middle + front * (half + 5), centre);
    }

    [Fact]
    public void AFindBehindAWallIsntPingedButItsHouseIs()
    {
        // "throw my ping around to find random loot": a find inside a house, looked at from outside its back wall. Not the find
        // (it can't be seen), but the place: the town to search.
        var night = new Night(cars: 6, walkers: 0, hands: 0, ids: true, loot: true, modules: ModuleKind.Crates);
        ToTheLoading(night);
        var (find, behind, _, at) = FindInAHouse(night);
        var ping = Pings.Resolve(night.World, LookingFrom(night.Train, behind, at), P.Pings);
        Assert.NotEqual((PingKind.Loot, find.Id), (ping?.Kind, ping?.Target ?? -1));
        Assert.Equal(PingKind.Town, ping?.Kind);
    }

    [Fact]
    public void AFindSeenThroughTheDoorIsPinged()
    {
        // The same find, looked at through the open door: seen, so it's the find.
        var night = new Night(cars: 6, walkers: 0, hands: 0, ids: true, loot: true, modules: ModuleKind.Crates);
        ToTheLoading(night);
        var (find, _, inFront, at) = FindInAHouse(night);
        var ping = Pings.Resolve(night.World, LookingFrom(night.Train, inFront, at), P.Pings);
        Assert.Equal((PingKind.Loot, find.Id), (ping?.Kind, ping?.Target ?? -1));
    }

    [Fact]
    public void ATownPingedAheadWaitsForTheTrainToStandThereAndIsThenTaken()
    {
        // The village pinged on the way in, from within townRange of it: nobody takes it (the lone driver doesn't stand the
        // train out on the line for it, with nothing it can do there), the ping stays up, and once the train stands at the
        // stop the driver takes it. (The stop the lone driver makes is its own choice: a first night finds it.)
        static Night Make() => new(cars: 6, walkers: 0, hands: 0, ids: true, loot: true, modules: ModuleKind.Crates);
        var first = Make();
        ToTheLoading(first);
        int k = first.World.Run!.StopOf(first.World.Run.CurrentSite!.Feature).Stop;
        var night = Make();
        var world = night.World;
        var run = world.Run!;
        int feature = run.StopOf(run.Stops[k]).Feature;
        var houses = night.Train.Walls!.OpenHouses.Where(h => h.Feature == feature).ToList();
        double Off() => houses.Min(h => (h.Middle - night.Train.Frames[0].Origin).Length);
        night.Until(() => Off() < P.Pings.TownRange - 10, 900);
        Assert.Null(run.CurrentSite);
        world.Pings.Add(new PingEvent(12, world.Tick, 99, PingKind.Town, k, houses[0].Middle + Double3.Up * 2));
        Roles(night);
        bool takenEarly = false;
        night.Until(() => run.CurrentSite is not null, 900, () =>
        {
            Take(night);
            takenEarly |= night.Calls.OrderOf(1) is not null && run.CurrentSite is null;
        });
        Assert.False(takenEarly, "the driver took the town before the train stood at it");
        Assert.NotNull(run.CurrentSite);
        Assert.Contains(world.Pings, p => p.Id == 12);
        night.Until(() => night.Calls.OrderOf(1) is not null, 120, () => Take(night));
        Assert.Equal((PingKind.Town, k), night.Calls.OrderOf(1));
    }

    [Fact]
    public void AFindPingedOutOnTheLineIsntTakenByTheDriverWhoCantFetchItThere()
    {
        // Line 8's cause, from the ping's side (note 995 handed it on): a find pinged away from any stop. The lone driver
        // can only fetch at a stop the train stands at, so it doesn't take it ("I'll fetch it") and then sit in the cab.
        var night = new Night(cars: 6, walkers: 0, hands: 0, ids: true, loot: true, from: 400, modules: ModuleKind.Crates);
        var train = night.Train;
        var world = night.World;
        Assert.Null(world.Run!.CurrentSite);
        var frame = train.Frames[2];
        var at = frame.ToWorld(new Double3(frame.Shape.Bounds.Max.X + 5, 0.3, 0));
        var find = world.Bodies.SpawnLoot(at, train.Dynamics.Distance, -1, 0.2);
        world.Pings.Add(new PingEvent(13, world.Tick, 99, PingKind.Loot, find.Id, at));
        Roles(night);
        Take(night);
        Assert.Null(night.Calls.OrderOf(1));
    }
}
