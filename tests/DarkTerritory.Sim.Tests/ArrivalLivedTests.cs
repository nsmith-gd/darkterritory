using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Towns;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The arrival town lived in (queue #335, ARCHITECTURE §8 note 603): once the night's delivered the terminus's town is as
/// the departure's is, its people to talk to and its board to read, whichever town the crewmate's standing in.
/// </summary>
public class ArrivalLivedTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TownContent Towns = TownContent.Load(Content)!;

    static World Night()
    {
        var route = Routes.Generate(Content, "frontier:7", 6);
        double gate = route.GateOr(RouteTuning.Load(Content).YardLength);
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), route.Build(), route.Plan!.Terminus.GateM + 400);
        var world = new World(train, Tuning.Combat);
        world.EnableRun(Tuning.Run, route, gate, authority: true);
        world.EnableTown(Towns, route, gate, [], arrival: TownAt.Terminus(route, LineGenContent.Cached(Content).Config.Tiers.Fortress.Identities));
        return world;
    }

    /// <summary>A crewmate on the ground at <paramref name="feet"/>, looking at <paramref name="at"/>.</summary>
    static PlayerState Facing(World world, Double3 feet, Double3 at)
    {
        var s = PlayerMotor.SpawnOnGround(feet, world.Train.Line, world.Train.Dynamics.Distance, Tuning.Player);
        var to = at - (feet + Double3.Up * Tuning.Train.Pick.EyeHeight);
        return s with { Yaw = Math.Atan2(-to.X, -to.Z), Pitch = Math.Atan2(to.Y, Math.Sqrt(to.X * to.X + to.Z * to.Z)) };
    }

    [Fact]
    public void TheTownYouStandInIsTheOneThatAnswers()
    {
        var world = Night();
        var arrival = world.Arrival!;
        var departure = world.Town!;
        // In each town's square, that town.
        var there = arrival.Plan.Square;
        var here = departure.Plan.Square;
        Assert.Same(arrival, world.TownAt(arrival.World((there.S0 + there.S1) / 2, there.Side * 10)));
        Assert.Same(departure, world.TownAt(departure.World((here.S0 + here.S1) / 2, here.Side * 10)));
        // Out on the line between them, the departure's (nothing of the arrival's is in reach there).
        Assert.Same(departure, world.TownAt(world.Train.Line.Sample(world.Train.Line.Length / 2).Position));
    }

    [Fact]
    public void ItsPeopleAndItsBoardAreThereToTalkToAndRead()
    {
        var world = Night();
        var arrival = world.Arrival!;
        // Somebody in the arrival town, stood in front of: they're who the crewmate's facing, in that town.
        var p = arrival.Plan.People.First(x => x.House < 0);
        var feet = arrival.Feet(p);
        var me = Facing(world, feet + arrival.Direction(p.S, p.FaceS, p.FaceD) * 1.5, feet + Double3.Up * 1.5);
        Assert.Same(arrival, world.TownOf(me));
        var t = Assert.IsType<TownTarget>(world.TownOf(me)!.Target(me, Tuning.Train.Pick.EyeHeight));
        Assert.Equal(TownTargetKind.Person, t.Kind);
        // Its board, read.
        var b = arrival.Plan.Fixtures.First(f => f.Kind == "board");
        var reader = Facing(world, arrival.World(b.S, b.D) + arrival.Direction(b.S, b.FaceS, b.FaceD) * 1.6, arrival.World(b.S, b.D, 1.55));
        var read = Assert.IsType<TownTarget>(world.TownOf(reader)!.Target(reader, Tuning.Train.Pick.EyeHeight));
        Assert.Equal(TownTargetKind.Board, read.Kind);
    }
}
