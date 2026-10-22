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
    public void ACouplingWorkingLooseIsTheWalkersToMakeFast()
    {
        // Note 356: made fast in its gap. Anyone on either car of the gap, and any walker; not a rider on another car.
        var n = OnTheRun();
        var layout = Tuning.Train.Geometry.Interior!;
        n.Crew[3] = PlayerMotor.SpawnOnRoof(n.Train, 1, 0, P) with { Surface = Surface.Deck, Position = new Double3(0, layout.FloorHeight, 0) };
        var d = n.World.Director!;
        n.Run(d.Grace + 20);
        Assert.True(Of(n, 2).Slack > 10);
        // The gap far down the train from the walker: still the walker's; not the rider's in car 1.
        n.Train.Vehicles[n.Train.Dynamics.Consist.Vehicles[^2].Id].Loose = 1;
        n.Run(1.1);
        Assert.Equal(0, Of(n, 2).Slack);
        Assert.True(Of(n, 3).Slack > 10);
    }

    [Fact]
    public void AGutteringLampIsARidersToTrim()
    {
        // Note 346: trimmed inside the car. A rider's, wherever it is on the train; not a walker's on another car.
        var n = OnTheRun();
        var layout = Tuning.Train.Geometry.Interior!;
        n.Crew[3] = PlayerMotor.SpawnOnRoof(n.Train, 2, 0, P) with { Surface = Surface.Deck, Position = new Double3(0, layout.FloorHeight, 0) };
        var d = n.World.Director!;
        n.Run(d.Grace + 20);
        Assert.True(Of(n, 3).Slack > 10);
        n.Train.Vehicles[4].Gutter = 1;
        n.Run(1.1);
        Assert.Equal(0, Of(n, 3).Slack);
        Assert.True(Of(n, 2).Slack > 10);
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

    /// <summary>
    /// A foundry's crane (note 753's stop), crewmate 1 pressed in at its controls, crewmate 2 on the roof of the car nearest
    /// them (or <paramref name="walkerCar"/>); the census counted, the director spending nothing of its own.
    /// </summary>
    static (FacilityTests.Stop Stop, Director D) AtTheCrane(EnemyTuning? enemies = null, int? walkerCar = null, bool walker = true)
    {
        var stop = new FacilityTests.Stop(Run.ModuleKind.Crane);
        stop.World.EnableEnemies(enemies ?? Q, null, 1, crew: 4, authority: true);
        var crane = stop.Site.Crane!;
        stop.Crew.Add(stop.OnTheGround(crane.Controls));
        if (walker)
        {
            int car = walkerCar ?? stop.Train.Frames.MinBy(f => (f.Origin - crane.Controls).Length)!.Index;
            stop.Crew.Add(PlayerMotor.SpawnOnRoof(stop.Train, car, 0, P));
        }
        PlayerIntent[] none = [.. stop.Crew.Select(_ => default(PlayerIntent))];
        stop.Step(SimConstants.TickSeconds, [new PlayerIntent { Actions = PlayerActions.Seat }, .. none.Skip(1)]);
        Assert.True(stop.Crew[0].Has(PlayerFlags.Operating));
        stop.Step(1.1, none);
        return (stop, stop.World.Director!);
    }

    static CensusEntry Of(Director d, int player) => d.Posts.Entries.Single(e => e.Player == player);

    [Fact]
    public void ACraneOperatorIsCommittedAtTheStopAndTheFacilityIsLoading()
    {
        // Note 753: the census counts at a stop too (only slack waits for the run), and reads who's committed to a job.
        var (stop, d) = AtTheCrane();
        Assert.True(O.Commitment);
        Assert.Equal(Post.Ground, Of(d, 1).Post);
        Assert.True(Of(d, 1).Committed);
        Assert.Equal(Post.Walker, Of(d, 2).Post);
        Assert.False(Of(d, 2).Committed);
        // "facilityLoading" is the loading going on: someone at the crane's controls.
        Assert.True(Director.Loading(stop.World));
        // Heavy, a timed job, a reload: committed too; standing about, not.
        var idle = stop.Crew[1];
        Assert.False(Census.IsCommitted(stop.World, 2, idle));
        Assert.True(Census.IsCommitted(stop.World, 2, idle with { Flags = PlayerFlags.Heavy }));
        Assert.True(Census.IsCommitted(stop.World, 2, idle with { ActionProgress = 1.2 }));
        // Off, nobody's committed (the census as before note 753).
        var (_, off) = AtTheCrane(Q with { Director = Q.Director with { Orchestrator = O with { Commitment = false } } });
        Assert.False(Of(off, 1).Committed);
    }

    [Fact]
    public void AThreatNearABusyCrewmateThatAFreeOneAnswersWeighsUp()
    {
        // The operator's committed at the crane on the ground; the walker on the roof of the car beside them is free. Draggers
        // (the walkers') come to the walker, beside the operator's work: the walker chooses. The Freight Beetle (the ground's)
        // comes at the operator, and nobody free on the ground answers it: no weight (no pile-on).
        var (stop, d) = AtTheCrane();
        Assert.True((Of(d, 2).At - Of(d, 1).At).Length <= O.CommittedNearM);
        Assert.Null(d.Next); // no slack at a fresh stop: only the collision weighs
        List<(EnemyKind Kind, double Weight)> options = [(EnemyKind.Dragger, 1), (EnemyKind.FreightBeetle, 1), (EnemyKind.TrackDoll, 1)];
        var w = d.WhoseNext(options).ToDictionary(o => o.Kind, o => o.Weight);
        Assert.Equal(O.CollisionWeight, w[EnemyKind.Dragger]);
        Assert.Equal(1, w[EnemyKind.FreightBeetle]);
        Assert.Equal(1, w[EnemyKind.TrackDoll]); // nobody busy where it comes, and its rule's the empty cab: as before
        // The walker far down the train (out of reach of the operator): nothing weighs up.
        var (_, far) = AtTheCrane(walkerCar: stop.Train.Frames.MaxBy(f => (f.Origin - stop.Site.Crane!.Controls).Length)!.Index);
        Assert.True((Of(far, 2).At - Of(far, 1).At).Length > O.CommittedNearM);
        w = far.WhoseNext(options).ToDictionary(o => o.Kind, o => o.Weight);
        Assert.Equal(1, w[EnemyKind.Dragger]);
        Assert.Equal(1, w[EnemyKind.FreightBeetle]);
        // Off: as before.
        var (_, off) = AtTheCrane(Q with { Director = Q.Director with { Orchestrator = O with { Commitment = false } } });
        Assert.Equal(1, off.WhoseNext(options).Single(o => o.Kind == EnemyKind.Dragger).Weight);
    }

    /// <summary>Two at a winch's handles, cranking (note 867's pair), the census counted.</summary>
    static (FacilityTests.Stop Stop, Director D) AtTheWinch(EnemyTuning? enemies = null)
    {
        var stop = new FacilityTests.Stop(Run.ModuleKind.Winch);
        stop.World.EnableEnemies(enemies ?? Q, null, 1, crew: 4, authority: true);
        foreach (var handle in stop.Site.Handles)
            stop.Crew.Add(stop.OnTheGround(handle - Double3.Up * 0.9));
        var crank = new PlayerIntent { Buttons = PlayerButtons.Use };
        stop.Step(1.1, [crank, crank]);
        Assert.True(stop.Site.Turning);
        return (stop, stop.World.Director!);
    }

    [Fact]
    public void OneOfAPairIsFreeForTheOthersCollision()
    {
        // Note 867 (the director, 19 Oct, on note 861's call 2): a crew of two both at the winch. Each is the other's partner;
        // the ground's kind coming at one weighs up, for the other can let go of their handle and come. Off (note 753): a pair
        // is two busy and nobody free, nothing weighs up.
        Assert.True(O.PairFree);
        var (_, d) = AtTheWinch();
        Assert.True(Of(d, 1).Committed && Of(d, 2).Committed);
        Assert.Equal(2, Of(d, 1).Partner);
        Assert.Equal(1, Of(d, 2).Partner);
        List<(EnemyKind Kind, double Weight)> options = [(EnemyKind.FreightBeetle, 1), (EnemyKind.Dragger, 1)];
        var w = d.WhoseNext(options).ToDictionary(o => o.Kind, o => o.Weight);
        var (_, off) = AtTheWinch(Q with { Director = Q.Director with { Orchestrator = O with { PairFree = false } } });
        var before = off.WhoseNext(options).ToDictionary(o => o.Kind, o => o.Weight);
        Assert.Equal(before[EnemyKind.FreightBeetle] * O.CollisionWeight, w[EnemyKind.FreightBeetle], 9);
        Assert.Equal(before[EnemyKind.Dragger], w[EnemyKind.Dragger], 9); // the walkers': nobody on the roofs to come
        // Not a pair: a crane's operator and a walker on the roof aren't partners.
        var (_, crane) = AtTheCrane();
        Assert.Equal(-1, Of(crane, 1).Partner);
        Assert.Equal(-1, Of(crane, 2).Partner);
    }

    [Fact]
    public void ABusyCrewmateAloneIsLeftAsAppBHasIt()
    {
        // A crew of one at the crane: committed, but there's nobody free, and a crew of one is App. B.1's (minCrew 2).
        var (_, d) = AtTheCrane(walker: false);
        Assert.True(Of(d, 1).Committed);
        List<(EnemyKind Kind, double Weight)> options = [(EnemyKind.Dragger, 1), (EnemyKind.FreightBeetle, 1)];
        Assert.Equal(options, d.WhoseNext(options));
    }

    [Fact]
    public void APairMadeTonightWeighsLessToBeMadeAgain()
    {
        // Note 753 (seed 2 frontier drew whistler+facilityStop three times): ÷pairRepeat for each time it's been made.
        var (stop, d) = AtTheCrane();
        Assert.Equal(2, O.PairRepeat);
        List<(EnemyKind Kind, double Weight)> options = [(EnemyKind.Whistler, 1), (EnemyKind.Ribbit, 1)];
        Assert.Equal(options, d.PairRepeats(options, stop.World, []));
        d.Pairs.Add("whistler+facilityStop");
        var w = d.PairRepeats(options, stop.World, []).ToDictionary(o => o.Kind, o => o.Weight);
        Assert.Equal(1 / O.PairRepeat, w[EnemyKind.Whistler]);
        Assert.Equal(1, w[EnemyKind.Ribbit]);
        d.Pairs.Add("whistler+facilityStop");
        Assert.Equal(1 / (O.PairRepeat * O.PairRepeat), d.PairRepeats(options, stop.World, []).First().Weight);
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
