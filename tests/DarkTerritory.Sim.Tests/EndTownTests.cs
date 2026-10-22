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
/// departs from, solid and safe; its yard the departure's length, home once the whole train's in through its gate. Since
/// note 692 (queue #424; the director, 13 Oct: "a town needs a gate that is the entrance from the previous run, and then it
/// also needs a gate that is the exit to the next run ... make it so that if I do want to do seamless loading between the
/// two, we can") it stands the same way round as when the next night leaves it: the train comes in at its way in from the
/// last night, its rear wall's gate, and its way out to the next is at the end of the line.
/// </summary>
public class EndTownTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TownContent Towns = TownContent.Load(Content)!;
    static readonly IReadOnlyList<string> Identities = LineGenContent.Cached(Content).Config.Tiers.Fortress.Identities;

    /// <summary>A night on <paramref name="spec"/> with both its towns, as the sessions stand them.</summary>
    static (World World, Route.Route Route, double Gate) Night(string spec, int people = 0, string? form = null)
    {
        var content = people > 0 ? Towns with { Tuning = Towns.Tuning with { Population = [people, people] } } : Towns;
        if (form is not null)
            content = content with { Tuning = content.Tuning with { Forms = content.Tuning.Forms with { Force = form } } };
        var route = Routes.Generate(Content, spec, 6);
        double gate = route.GateOr(RouteTuning.Load(Content).YardLength);
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), route.Build(), gate - 8);
        var world = new World(train, Tuning.Combat);
        world.EnableRun(Tuning.Run, route, gate, authority: true);
        world.EnableTown(content, route, gate, [], arrival: TownAt.Terminus(route, Identities));
        return (world, route, gate);
    }

    [Fact]
    public void TheGunsHoldFireInsideTheTown()
    {
        // Note 846 (the director's playtest of 16 Oct, line 7: "the cannon lets me fire when I'm in a town, and I just fired
        // at a house"): stood at the departure's gate, every gun's car is in the walled town and holds its fire.
        var (world, _, _) = Night("frontier:7");
        var guns = world.Train.Vehicles.Where(v => v.HasGun).Select(v => v.Id).ToList();
        Assert.NotEmpty(guns);
        Assert.All(guns, g => Assert.True(world.GunHeld(g), $"the gun on car {g} would fire in the town"));
        // Out past its walls it isn't the town's.
        var front = world.Train.Frames[0];
        Assert.False(world.InTown(front.Origin + front.Right * 400));
        // And with combat.json holdInTowns off, they fire as before.
        world.Combat = world.Combat! with { Guns = world.Combat.Guns with { HoldInTowns = false } };
        Assert.All(guns, g => Assert.False(world.GunHeld(g)));
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
        // And the same solids, each where it stands in its own town's frame: so the same streets underfoot either night.
        Assert.Equal(departing.Walls.Count, arrival.Walls.Count);
        var dOrigin = departing.Anchor();
        var aOrigin = arrival.Anchor();
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

    [Theory]
    [InlineData("")]
    [InlineData("quarry")]
    public void ItStandsTheSameWayRoundItsWayInAtTheTerminusGateSolidAndSafe(string form)
    {
        var (world, route, gate) = Night("frontier:7", 3000, form);
        var arrival = world.Arrival!;
        var terminus = route.Plan!.Terminus;
        var line = world.Train.Line;
        var b = Assert.IsType<TownBounds>(arrival.Plan.Bounds);
        // Its way in from the last night on the terminus's gate, its way out to the next toward the end of the line: the train
        // comes in through its rear wall's gate (note 692).
        Assert.True((arrival.World(b.Rear, 0) - line.Sample(terminus.GateM).Position).Length < 0.5);
        Assert.True((arrival.World(b.Gate - 10, 0) - line.Sample(terminus.GateM + b.Gate - b.Rear - 10).Position).Length < 0.5);
        Assert.True(terminus.GateM + b.Gate - b.Rear <= line.Length + 1e-6, "its way out is on the line");
        // Its square on the same side of the line as the departure's: the same town, the same way round.
        Assert.Equal(world.Town!.Plan.Square.Side, world.Forts![1].Square!.Side);
        // Its walls are the world's, and the fort's walled reach runs out to its wall.
        foreach (var w in arrival.Walls.Take(40))
            Assert.Contains(w, world.Train.Walls!.All);
        var street = b.Streets.Count > 0 ? b.Streets.OrderByDescending(s => Math.Abs(s.D)).First() : new TownStreet(-b.Left + 15, b.Rear + 40, b.Gate - 40, 6);
        var far = arrival.World((street.S0 + street.S1) / 2, street.At((street.S0 + street.S1) / 2));
        Assert.True(world.InFort(far), "the town's outermost street is inside the fort");
        // (Outside its way in, on its straight frame back out along the line: its own frame starts there.)
        var o = arrival.Anchor();
        var along = new Double3(o.Tangent.X, 0, o.Tangent.Z).Normalized;
        var right = Double3.Cross(along, Double3.Up).Normalized;
        Assert.False(world.InFort(o.Position + along * (b.Rear - 120) + right * street.D), "outside its way in isn't");
        Assert.Equal(arrival.Origin, world.Forts[1].Origin);
        // Its rear wall, across the line at its way in, out from the gatehouse there (Fortresses.Round from its straight frame):
        // the fort's own solids, the gatehouse's arch over the line.
        var solids = Fortresses.Solids(world.Forts[1], line).ToList();
        foreach (double d in new[] { -b.Left / 2, b.Right / 2 })
        {
            var rear = (o.Position + along * b.Rear + right * d) with { Y = 0 };
            Assert.Contains(solids, w => (w.Centre - rear).Length < 6 && w.HalfWidth == Fortresses.WallHalf);
        }
        var arch = (o.Position + along * b.Rear) with { Y = 0 };
        Assert.Contains(solids, w => (w.Centre - arch).Length < 0.5 && w.Bottom > o.Position.Y + 5);
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
