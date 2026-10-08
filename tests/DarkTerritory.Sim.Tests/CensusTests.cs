using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The orchestrator's census of posts and slack (ARCHITECTURE §8 note 345; docs/design/orchestrator.md §3.1, §3.2 items 2–4;
/// GDD App. F.3): each crewmate's post, who each threat is on, the seconds on the run between stops since each had anything
/// to answer; the director's pick steered to the slackest crewmate's post, slack pressing it, and one threat on any one player.
/// </summary>
public class CensusTests
{
    static readonly EnemyTuning Q = HoundRunTests.Quiet;
    static readonly OrchestratorTuning O = Tuning.Enemies.Director.Orchestrator;
    static readonly PlayerTuning P = Tuning.Player;

    /// <summary>A night on the line with the director spending nothing of its own, past its grace.</summary>
    static Night OnTheRun(double speed = 14, EnemyTuning? enemies = null)
    {
        var n = new Night(4, speed, enemies: enemies ?? Q);
        n.Crew[1] = PlayerMotor.SpawnInCab(n.Train, P);
        n.Crew[2] = PlayerMotor.SpawnOnRoof(n.Train, 3, 0, P);
        return n;
    }

    static CensusEntry Of(Night n, int player) => n.World.Director!.Posts.Entries.Single(e => e.Player == player);

    static Double3 Beside(Night n, double along, double lateral)
    {
        var t = n.Train.Line.Sample(along);
        return t.Position + Double3.Cross(t.Tangent, Double3.Up).Normalized * lateral;
    }

    [Fact]
    public void EachCrewmatesPostIsWhereTheyAreAndWhatTheyreDoing()
    {
        var n = OnTheRun();
        var layout = Tuning.Train.Geometry.Interior!;
        n.Crew[3] = PlayerMotor.SpawnOnRoof(n.Train, 2, 0, P) with { Surface = Surface.Deck, Position = new Double3(0, layout.FloorHeight, 0) };
        double rear = n.Train.Dynamics.RearDistance;
        n.Crew[5] = PlayerMotor.SpawnOnGround(Beside(n, rear + 10, 4), n.Train.Line, rear + 10, P);
        n.Crew[6] = PlayerMotor.SpawnOnGround(Beside(n, rear - 400, 4), n.Train.Line, rear - 400, P);
        n.Run(1.1, holdSpeed: true);
        Assert.Equal(Post.Cab, Of(n, 1).Post);
        Assert.Equal(Post.Walker, Of(n, 2).Post);
        Assert.Equal(Post.Rider, Of(n, 3).Post);
        // Seated at a gun (the motor keeps the seat only at a gun, so read straight off the state).
        Assert.Equal(Post.Gunner, Census.PostOf(PlayerMotor.SpawnOnRoof(n.Train, 1, 0, P) with { Flags = PlayerFlags.Seated }, n.Train, 150));
        Assert.Equal(Post.Ground, Of(n, 5).Post);
        Assert.Equal(Post.LeftBehind, Of(n, 6).Post);
    }

    [Fact]
    public void SlackCountsOnTheRunAndAThreatOnThemClearsIt()
    {
        var n = OnTheRun();
        var d = n.World.Director!;
        n.Run(d.Grace + 40);
        // The walker's had nothing to answer since the grace; the driver has the line while it's moving.
        Assert.InRange(Of(n, 2).Slack, 38, 42);
        Assert.Equal(0, Of(n, 1).Slack);
        // Standing, nobody's slack grows (a stop is the crew's work, not the run between).
        n.Train.Dynamics.Velocity = 0;
        double was = Of(n, 2).Slack;
        n.Run(10);
        Assert.Equal(was, Of(n, 2).Slack);
        // A hound on the walker's car, beside them: it's on them, and their slack is gone.
        n.Train.Dynamics.Velocity = 14;
        int car = n.Crew[2].Parent;
        var hound = n.World.AddEnemy(id => new CinderHound(id, id) { Health = Q.CinderHounds.Health });
        hound.Restore(SpinePhase.Commit, 0, Q.CinderHounds.Health, car, new Double3(0.6, n.Train.Frames[car].Shape.RoofHeight, 2), 0, 0, 0, 0, 0);
        n.Run(1.1);
        Assert.Equal(1, Of(n, 2).On);
        Assert.Equal(0, Of(n, 2).Slack);
    }

    [Fact]
    public void AnOpenHotBoxOnTheirCarIsSomethingToAnswer()
    {
        var n = OnTheRun();
        var d = n.World.Director!;
        n.Run(d.Grace + 20);
        Assert.True(Of(n, 2).Slack > 10);
        n.Train.Vehicles[n.Crew[2].Parent].HotBox = 1;
        n.Run(1.1);
        Assert.Equal(0, Of(n, 2).Slack);
    }

    [Fact]
    public void SlackPressesTheDirector()
    {
        // With slackPress low, the walker's slack adds slackPerSecond a second past it; census off, it doesn't.
        EnemyTuning With(bool census) => Q with { Director = Q.Director with { Orchestrator = O with { Census = census, SlackPress = 5 } } };
        var on = OnTheRun(enemies: With(true));
        var off = OnTheRun(enemies: With(false));
        double seconds = on.World.Director!.Grace + 30;
        on.Run(seconds);
        off.Run(seconds);
        double gained = on.World.Director!.Pressure - off.World.Director!.Pressure;
        Assert.InRange(gained, O.SlackPerSecond * 20, O.SlackPerSecond * 30);
    }

    [Fact]
    public void WhosNextIsTheSlackestPostAndATakenPostsKindsWeighNothing()
    {
        var n = OnTheRun();
        var d = n.World.Director!;
        n.Run(d.Grace + 40);
        Assert.Equal(2, d.Next?.Player);
        List<(EnemyKind Kind, double Weight)> options = [(EnemyKind.Dragger, 1), (EnemyKind.Ribbit, 1), (EnemyKind.TrackDoll, 1), (EnemyKind.CinderHound, 1)];
        var w = d.WhoseNext(options).ToDictionary(o => o.Kind, o => o.Weight);
        Assert.Equal(O.SlackWeight, w[EnemyKind.Dragger]); // the walker's
        Assert.Equal(O.SlackWeight, w[EnemyKind.CinderHound]); // the walker's (and the gunner's: nobody's at a gun)
        Assert.Equal(O.EmptyPostWeight, w[EnemyKind.Ribbit]); // nobody on the ground
        Assert.Equal(1, w[EnemyKind.TrackDoll]); // the cab's, and the driver isn't slack
        // A hound on the walker: what only a walker answers can't come for them now.
        int car = n.Crew[2].Parent;
        var hound = n.World.AddEnemy(id => new CinderHound(id, id) { Health = Q.CinderHounds.Health });
        hound.Restore(SpinePhase.Commit, 0, Q.CinderHounds.Health, car, new Double3(0.6, n.Train.Frames[car].Shape.RoofHeight, 2), 0, 0, 0, 0, 0);
        n.Run(1.1);
        w = d.WhoseNext(options).ToDictionary(o => o.Kind, o => o.Weight);
        Assert.Equal(0, w[EnemyKind.Dragger]);
        Assert.Equal(0, w[EnemyKind.CinderHound]);
        Assert.Equal(1, w[EnemyKind.TrackDoll]);
        Assert.Null(d.Next);
    }

    [Fact]
    public void ACrewOfOneIsAppBsAsItWas()
    {
        var n = new Night(4, 14, enemies: Q);
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, 3, 0, P);
        var d = n.World.Director!;
        n.Run(d.Grace + 40);
        Assert.True(Of(n, 1).Slack > 30); // counted
        Assert.Null(d.Next); // but it steers nothing
        List<(EnemyKind Kind, double Weight)> options = [(EnemyKind.Dragger, 1), (EnemyKind.Ribbit, 1)];
        Assert.Equal(options, d.WhoseNext(options));
    }
}
