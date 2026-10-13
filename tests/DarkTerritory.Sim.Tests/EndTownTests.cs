using System.Text.Json;
using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Towns;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The end town is the town (queue #332, ARCHITECTURE §8 note 600; the director, 9 Oct 2026: "the town that we end in in a
/// run should be the same town that we begin in in the next run"): the terminus is built as the town the next night
/// departs from, turned round to face the train coming in, solid and safe; its yard the departure's length, home once the
/// whole train's in through its gate.
/// </summary>
public class EndTownTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TownContent Towns = TownContent.Load(Content)!;
    static readonly IReadOnlyList<string> Identities = LineGenContent.Cached(Content).Config.Tiers.Fortress.Identities;

    /// <summary>A night on <paramref name="spec"/> with both its towns, as the sessions stand them.</summary>
    static (World World, Route.Route Route, double Gate) Night(string spec, int people = 0)
    {
        var content = people > 0 ? Towns with { Tuning = Towns.Tuning with { Population = [people, people] } } : Towns;
        var route = Routes.Generate(Content, spec, 6);
        double gate = route.GateOr(RouteTuning.Load(Content).YardLength);
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), route.Build(), gate - 8);
        var world = new World(train, Tuning.Combat);
        world.EnableRun(Tuning.Run, route, gate, authority: true);
        world.EnableTown(content, route, gate, [], arrival: TownAt.Terminus(route, Identities));
        return (world, route, gate);
    }

    [Theory]
    [InlineData("frontier:7", "frontier:3")]
    [InlineData("local:2", "frontier:5")]
    public void TheTownPulledIntoIsTheTownTheNextNightLeavesFrom(string tonight, string tomorrow)
    {
        var (world, route, gate) = Night(tonight);
        var arrival = Assert.IsType<Town>(world.Arrival);
        // The next night, departing from it (note 591), builds its town as World.EnableTown does, last night's custom the
        // one it won't share.
        var next = Routes.Generate(Content, TownAt.With(tomorrow, TownAt.Terminus(route, Identities)), 6);
        double nextGate = next.GateOr(RouteTuning.Load(Content).YardLength);
        Assert.Equal(gate, nextGate, 6);
        var departing = new Town(TownGenerator.Generate(Towns, TownSite.Of(next, nextGate, [], Towns, world.Town!.Plan.Culture)), Towns.Tuning, next.Build(), Towns.Looks);
        Assert.Equal(next.Plan!.Fortress.Name, arrival.Plan.Name);
        // The same plan to the last person and paper.
        Assert.Equal(JsonSerializer.Serialize(departing.Plan), JsonSerializer.Serialize(arrival.Plan));
        // And the same solids, each where it stands in its own town's frame: so the same streets underfoot either way round.
        Assert.Equal(departing.Walls.Count, arrival.Walls.Count);
        var dOrigin = departing.Line.Sample(0);
        var aOrigin = arrival.Line.Sample(0);
        static (double S, double D) Frame(TrackSample o, Double3 p)
        {
            var along = new Double3(o.Tangent.X, 0, o.Tangent.Z).Normalized;
            var right = Double3.Cross(along, Double3.Up).Normalized;
            var v = (p - o.Position) with { Y = 0 };
            return (Double3.Dot(v, along), Double3.Dot(v, right));
        }
        for (int i = 0; i < arrival.Walls.Count; i += 7)
        {
            var (ds, dd) = Frame(dOrigin, departing.Walls[i].Centre);
            var (s, d) = Frame(aOrigin, arrival.Walls[i].Centre);
            Assert.True(Math.Abs(ds - s) < 0.05 && Math.Abs(dd - d) < 0.05, $"wall {i}: ({s:0.00}, {d:0.00}) in the arrival, ({ds:0.00}, {dd:0.00}) departing");
        }
    }

    [Fact]
    public void ItStandsTurnedRoundItsGateAtTheTerminusGateSolidAndSafe()
    {
        var (world, route, gate) = Night("frontier:7", 3000);
        var arrival = world.Arrival!;
        var terminus = route.Plan!.Terminus;
        var line = world.Train.Line;
        var b = Assert.IsType<TownBounds>(arrival.Plan.Bounds);
        // Its gate on the terminus's, its rear wall toward the end of the line; the train comes in through the gate.
        Assert.True((arrival.World(b.Gate, 0) - line.Sample(terminus.GateM).Position).Length < 0.5);
        Assert.True((arrival.World(b.Rear + 10, 0) - line.Sample(terminus.GateM + gate - b.Rear - 10).Position).Length < 0.5);
        // Its square across the line from where the departure's is: the same side of the town, the train facing the other way.
        Assert.Equal(-world.Town!.Plan.Square.Side, world.Forts![1].Square!.Side);
        // Its walls are the world's, and the fort's walled reach runs out to its wall.
        foreach (var w in arrival.Walls.Take(40))
            Assert.Contains(w, world.Train.Walls!.All);
        var street = b.Streets.OrderByDescending(s => Math.Abs(s.D)).First();
        var far = arrival.World((street.S0 + street.S1) / 2, street.At((street.S0 + street.S1) / 2));
        Assert.True(world.InFort(far), "the town's outermost street is inside the fort");
        Assert.False(world.InFort(arrival.World(b.Gate + 120, street.D)), "outside its front wall isn't");
        Assert.Equal(arrival.Turn, world.Forts[1].Turn);
        // Its rear wall, across the line just past the end of the yard (Fortresses.Round, straight on from its frame's start):
        // the fort's own solid.
        var o = arrival.Line.Sample(0);
        var back = new Double3(o.Tangent.X, 0, o.Tangent.Z).Normalized;
        var rear = (o.Position + back * b.Rear + Double3.Cross(back, Double3.Up).Normalized * ((b.Right - b.Left) / 2)) with { Y = 0 };
        Assert.True((o.Position - line.Sample(line.Length).Position).Length < 1, "its frame starts at the end of the line");
        Assert.Contains(Fortresses.Solids(world.Forts[1], line), w => (w.Centre - rear).Length < 1);
    }

    [Fact]
    public void ASilentSettlementHasNoTownAndItsYardStaysShort()
    {
        var (world, route, _) = Night("deadLines:3");
        Assert.True(route.Plan!.Terminus.Silent);
        Assert.Null(world.Arrival);
        Assert.Equal(LineGenContent.Cached(Content).Config.Tiers.Terminus.ArrivalYardM, route.Length - route.Plan.Terminus.GateM, 0);
    }

    [Fact]
    public void ATownTerminusYardIsTheDeparturesAndTheTrainIsHomeInsideItsGate()
    {
        var (world, route, gate) = Night("frontier:7");
        var terminus = route.Plan!.Terminus;
        Assert.Equal(gate, route.Length - terminus.GateM, 0);
        // Home with the whole train in through the gate, not the end of line a kilometre on; the driver's to pull up there.
        double length = world.Train.Dynamics.Consist.LengthMetres;
        Assert.Equal(terminus.GateM + Tuning.Run.HomeInsideGateM!.Value + length, world.Run!.HomeFront(world.Train), 6);
        Assert.True(world.Run.HomeFront(world.Train) < route.Length - Tuning.Run.TerminusZone);
    }
}
