using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Stops;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Searching the open houses (GDD App. F.3 "searchable for finds"; level-design P14 "hiding spots ... that take time to
/// search"; ARCHITECTURE §8 note 326): an open house's cupboard, cabinet, cellar and boards keep their finds until a
/// crewmate has held Use there for the kind's seconds, and every crewmate sees what's been searched.
/// </summary>
[Collection(nameof(LineGenTests))]
public class SearchTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly LootTuning L = DataFile.Load<LootTuning>(Path.Combine(Content, LootTuning.File));
    static readonly PlayerTuning P = Tuning.Player;
    static readonly PlayerIntent Use = new() { Buttons = PlayerButtons.Use };
    const double Crewmate = 0.3;

    static World Night(bool authority)
    {
        var route = Routes.Generate(Content, "frontier:7", 6);
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 4, 0)), route.Build(), 3_000, Tuning.Boiler);
        var world = new World(train, Tuning.Combat);
        if (authority)
            world.EnableBodies();
        world.EnableRun(Tuning.Run, route, route.GateOr(Tuning.Route.YardLength), authority: authority, loot: L);
        train.Walls = StopWalls.Of(route, train.Line);
        return world;
    }

    static PlayerState At(World world, Double3 at)
    {
        double hint = world.Run!.Stops[0].Start;
        return PlayerMotor.SpawnOnGround(at with { Y = PlayerMotor.GroundAt(at, world.Train.Line, ref hint) }, world.Train.Line, hint, P);
    }

    static void Act(World world, ref PlayerState me, PlayerIntent intent, double seconds, int id = 1)
    {
        for (int i = 0; i < Math.Round(seconds * SimConstants.TickRate); i++)
        {
            world.BeginTick();
            world.CrewAct(ref me, intent, id);
        }
    }

    static Body? FindOf(World world, HidingSpot spot) =>
        world.Bodies.All.FirstOrDefault(b => b.Kind == BodyKind.Loot && b.Owner == spot.Key);

    [Fact]
    public void AnOpenHousesFindsAreThereOnlyOnceItsBeenSearched()
    {
        var world = Night(authority: true);
        var run = world.Run!;
        Assert.NotEmpty(run.HidingSpots);
        // Every spot is a cupboard, a cabinet, a cellar or the boards, in an open house; its stop's other finds lie out.
        var spot = run.HidingSpots[0];
        int k = spot.Stop;
        Assert.All(run.HidingSpots, h => Assert.True(L.Search!.Of(h.Container.Kind) > 0));
        run.Stock(world.Bodies, k);
        foreach (var h in run.HidingSpots.Where(h => h.Stop == k))
            Assert.Null(FindOf(world, h));
        var layout = run.Stops[k].Stop!;
        int shut = layout.Containers.Count(c => c.Zone == StopZone.Village && !run.HidingSpots.Any(h => h.Stop == k && h.Container.Index == c.Index));
        Assert.Equal(shut, world.Bodies.All.Count(b => b.Kind == BodyKind.Loot));

        // At it, holding Use not quite long enough, and letting go: nothing, and it starts again.
        var me = At(world, spot.At);
        Assert.Equal(spot, run.SpotInReach(me, world.Train));
        Act(world, ref me, Use, spot.Seconds - 0.2);
        Assert.Null(FindOf(world, spot));
        Assert.InRange(run.SearchProgress(spot), 0.8, 1);
        Act(world, ref me, default, 0.1);
        Assert.Equal(0, run.SearchProgress(spot));
        Act(world, ref me, Use, spot.Seconds - 0.2);
        Assert.Null(FindOf(world, spot));
        // Held through: the find's out where it was kept, the spot's searched, and there's nothing more to search in it.
        Act(world, ref me, Use, 0.3);
        var find = FindOf(world, spot);
        Assert.NotNull(find);
        Assert.True(((find.Pbd.Particles[0].Position - spot.At) with { Y = 0 }).Length < 0.3);
        Assert.True(run.Searched(k, spot.Container.Index));
        Assert.NotEqual(spot, run.SpotInReach(me, world.Train));
        int finds = world.Bodies.All.Count(b => b.Kind == BodyKind.Loot);
        Act(world, ref me, Use, spot.Seconds + 0.5, id: 2);
        Assert.Equal(finds, world.Bodies.All.Count(b => b.Kind == BodyKind.Loot));
    }

    [Fact]
    public void YouSearchEmptyHandedAndFromInside()
    {
        var world = Night(authority: true);
        var run = world.Run!;
        var spot = run.HidingSpots[0];
        run.Stock(world.Bodies, spot.Stop);
        // Carrying something, Use is the thing's (put down, used): no search.
        var me = At(world, spot.At);
        var toy = world.Bodies.SpawnItem(spot.At, run.Stops[spot.Stop].Start, BodyKind.Toy);
        toy.Carrier = 1;
        toy.Claimed = true;
        for (int i = 0; i < Math.Round((spot.Seconds + 0.5) * SimConstants.TickRate); i++)
        {
            world.BeginTick();
            toy.Carrier = 1;
            world.CrewAct(ref me, Use, 1);
        }
        Assert.Null(FindOf(world, spot));

        // Nowhere a crewmate can stand outside a house is within reach of what's kept in it: you go in to search.
        var walls = world.Train.Walls!;
        foreach (var h in run.HidingSpots)
        {
            var b = run.Stops[h.Stop].Stop!.Buildings[h.Container.Building];
            for (double x = -L.Search!.Reach; x <= L.Search.Reach; x += 0.05)
                for (double z = -L.Search.Reach; z <= L.Search.Reach; z += 0.05)
                {
                    var p = h.At + new Double3(x, 0, z);
                    if ((p - h.At).Length > L.Search.Reach || walls.Near(p).Any(w => Math.Abs(w.ToLocal(p).X) <= w.HalfLength + Crewmate && Math.Abs(w.ToLocal(p).Z) <= w.HalfWidth + Crewmate))
                        continue;
                    Assert.True(Inside(world, run, h, b, p), $"{h.Container.Kind} in house {h.Container.Building} at {h.Stop} is in reach from outside it at {p}");
                }
        }
    }

    [Fact]
    public void WhatsKeptStandsInTheHouseFacingTheRoomWithItsFindInFront()
    {
        // StopWalls.Kept: where the art stands the cupboard or cabinet (and draws it opened once searched), and the hatch and
        // boards: in the house, facing into the room, with a cupboard's or cabinet's find out in front of it.
        var world = Night(authority: true);
        var run = world.Run!;
        foreach (var h in run.HidingSpots)
        {
            var b = run.Stops[h.Stop].Stop!.Buildings[h.Container.Building];
            Assert.True(Inside(world, run, h, b, h.Kept), $"{h.Container.Kind} in house {h.Container.Building} stands outside it");
            Assert.Equal(1, h.Facing.Length, 6);
            Assert.Equal(0, h.Facing.Y);
            var ahead = (h.At - h.Kept) with { Y = 0 };
            if (h.Container.Kind is ContainerKind.Cupboard or ContainerKind.Cabinet or ContainerKind.Bench or ContainerKind.Hayloft)
            {
                Assert.True(ahead.Length > 0.3, $"{h.Container.Kind}'s find is in it");
                Assert.True(Double3.Dot(ahead.Normalized, h.Facing) > 0.999, $"{h.Container.Kind}'s find isn't in front of it");
                // Its back to a wall: within half a metre behind it is a wall, or out of the house (an inner wall has the
                // next room just behind it).
                bool Walled(Double3 p) => !Inside(world, run, h, b, p)
                    || world.Train.Walls!.Near(p).Any(w => Math.Abs(w.ToLocal(p).X) <= w.HalfLength && Math.Abs(w.ToLocal(p).Z) <= w.HalfWidth);
                Assert.True(Enumerable.Range(5, 6).Any(k => Walled(h.Kept - h.Facing * (k * 0.05))), $"{h.Container.Kind} stands out from its wall");
            }
            else
                Assert.True(ahead.Length < 1e-6, $"a {h.Container.Kind}'s find lies on it");
        }
    }

    /// <summary>Whether a world point stands within a building's footprint (its parts: an L's notch is outside it).</summary>
    static bool Inside(World world, Run.Run run, HidingSpot h, StopBuilding b, Double3 p)
    {
        var f = run.Stops[h.Stop];
        var o = Run.Run.StopWorld(world.Train.Line, f, StopWalls.InHouse(b, 0, 0));
        var ex = (Run.Run.StopWorld(world.Train.Line, f, StopWalls.InHouse(b, 1, 0)) - o) with { Y = 0 };
        var ey = (Run.Run.StopWorld(world.Train.Line, f, StopWalls.InHouse(b, 0, 1)) - o) with { Y = 0 };
        var d = (p - o) with { Y = 0 };
        return StopWalls.InParts(b, Double3.Dot(d, ex), Double3.Dot(d, ey));
    }

    [Fact]
    public void EveryCrewmateSeesWhatsBeenSearched()
    {
        var host = Night(authority: true);
        var client = Night(authority: false);
        var run = host.Run!;
        var spot = run.HidingSpots[0];
        run.Stock(host.Bodies, spot.Stop);
        var me = At(host, spot.At);
        var controls = new TrainControls();
        var players = new List<PlayerSnapshot>();

        // Halfway through, the client's HUD has it halfway; done, it's searched there too, and nobody's at it now.
        Act(host, ref me, Use, spot.Seconds / 2);
        var records = WorldRecords.Capture(host, controls, players);
        WorldRecords.Apply(records, client, ref controls, players);
        Assert.InRange(client.Run!.SearchProgress(spot), 0.45, 0.55);
        Assert.False(client.Run.Searched(spot.Stop, spot.Container.Index));
        Assert.Equal(spot, client.Run.SpotInReach(me, client.Train));
        Act(host, ref me, Use, spot.Seconds / 2 + 0.1);
        WorldRecords.Apply(WorldRecords.Capture(host, controls, players), client, ref controls, players);
        Assert.True(client.Run.Searched(spot.Stop, spot.Container.Index));
        Assert.Equal(0, client.Run.SearchProgress(spot));
        Assert.NotEqual(spot, client.Run.SpotInReach(me, client.Train));
    }
}
