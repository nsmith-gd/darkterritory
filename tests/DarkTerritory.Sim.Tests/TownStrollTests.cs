using Ballast;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Towns;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The bots in a town's streets between nights (ARCHITECTURE §8 note 1031; level-design T.4): a bot hand off duty in a
/// town walks out among its streets, round its houses, and when it's no longer free walks back to the train's side; the
/// same on every run.
/// </summary>
public class TownStrollTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TownContent Towns = TownContent.Load(Content)!;
    static readonly PlayerTuning P = Tuning.Player;

    sealed record Walked(double FurthestFromLine, double EndFromTrain, bool Strolling, List<Double3> Track);

    /// <summary>
    /// frontier:7 at its departure gate, its town built, a bot hand stood down beside car 2 on the square's side: free in the
    /// town for <paramref name="out_"/> s, then not for <paramref name="back"/> s.
    /// </summary>
    static Walked Stroll(double out_, double back)
    {
        var route = Routes.Generate(Content, "frontier:7", 6);
        double gate = route.GateOr(RouteTuning.Load(Content).YardLength);
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 4, 1)), route.Build(), Tuning.Run.DepartFrom(gate, 80));
        var world = new World(train, Tuning.Combat);
        world.EnableRun(Tuning.Run, route, gate, authority: true, FacilityTests.F);
        world.EnableTown(Towns, route, gate, [], arrival: TownAt.Terminus(route, LineGenContent.Cached(Content).Config.Tiers.Fortress.Identities));
        var town = world.Town!;
        int side = town.Plan.Square.Side == 0 ? 1 : town.Plan.Square.Side;
        var car = train.Frames[2];
        var self = PlayerMotor.SpawnOnGround(car.ToWorld(new Double3(side * (car.Shape.Bounds.Max.X + 2.6), 0, 0)), train.Line, train.Dynamics.Distance, P);
        var hand = new StopHand(StopJob.Crates, new CrewCalls(), 2) { PlayerId = 2 };
        var c = new TrainControls { Brake = 1 };
        double furthest = 0;
        var track = new List<Double3>();
        for (int i = 0; i < (out_ + back) * SimConstants.TickRate; i++)
        {
            bool free = i < out_ * SimConstants.TickRate;
            var intent = hand.Town(self, world, free) ?? default;
            world.BeginTick();
            world.Step(c);
            PlayerMotor.Step(ref self, intent, train, P, Tuning.Train, SimConstants.TickSeconds);
            var at = PlayerMotor.WorldPosition(self, train);
            double hint = train.Dynamics.Distance;
            var (path, along) = train.Line.Nearest(at, ref hint);
            var near = train.Line.Sample(path, along).Position;
            if (free)
                furthest = Math.Max(furthest, (new Double3(at.X, 0, at.Z) - new Double3(near.X, 0, near.Z)).Length);
            if (i % SimConstants.TickRate == 0)
                track.Add(at);
        }
        var end = PlayerMotor.WorldPosition(self, train);
        double fromTrain = train.Frames.Skip(1).Min(f => (new Double3(end.X, 0, end.Z) - new Double3(f.Origin.X, 0, f.Origin.Z)).Length);
        return new Walked(furthest, fromTrain, hand.Strolling, track);
    }

    [Fact]
    public void ABotOffDutyInTheTownWalksOutAmongItsStreetsAndBackToTheTrain()
    {
        Assert.True(FacilityTests.F.Crew.TownStroll > 0, "the game's default has the bots stroll the towns");
        var w = Stroll(90, 90);
        Assert.True(w.FurthestFromLine > 15, $"it never got out into the town: {w.FurthestFromLine:0.0} m from the line at most");
        Assert.True(w.EndFromTrain < 6, $"it never came back to the train: {w.EndFromTrain:0.0} m from a car's middle");
        Assert.False(w.Strolling, "still out in the town once it was back");
    }

    [Fact]
    public void TheStrollIsTheSameEveryTime()
    {
        var a = Stroll(40, 0);
        var b = Stroll(40, 0);
        Assert.Equal(a.Track, b.Track);
    }
}
