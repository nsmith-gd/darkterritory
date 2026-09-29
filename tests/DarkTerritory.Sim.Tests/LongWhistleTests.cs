using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>The Long Whistle (T57, App. A.2, B.2): a horn on the line ahead, and no train. Rule: don't trust the horn.</summary>
public class LongWhistleTests
{
    static readonly LongWhistleTuning W = Tuning.Enemies.LongWhistle;
    // Straight to 2.6 km, then a curve: the Long Whistle sounds from just short of it.
    static readonly LineDefinition Bend = new("t", [new TrackSegment(2_600), new TrackSegment(800, 600), new TrackSegment(40_000)]);
    static readonly RailLine BendLine = new(Bend);
    static readonly RailLine StraightLine = new(new LineDefinition("s", [new TrackSegment(80_000)]));

    sealed class Night
    {
        public readonly World World;
        public readonly List<EnemyEvent> Events = [];
        public double Speed;

        public Night(double speed, RailLine? line = null, double at = 2_000, EnemyTuning? enemies = null, Route.Route? route = null)
        {
            Speed = speed;
            var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 5, 1)), line ?? BendLine, at);
            train.Dynamics.Velocity = speed;
            World = new World(train, Tuning.Combat);
            var quiet = Tuning.Enemies with { Director = Tuning.Enemies.Director with { GraceSeconds = 1e9 } };
            World.EnableEnemies(enemies ?? quiet, route, 1, crew: 2, authority: true);
        }

        public TrainOnLine Train => World.Train;

        public LongWhistle Ahead() => World.AddEnemy(id => LongWhistle.At(id, Train, LongWhistle.Spot(Train, W)!.Value));

        public void Run(double seconds, Action? each = null)
        {
            for (int i = 0; i < Math.Max(1, seconds * SimConstants.TickRate); i++)
            {
                each?.Invoke();
                Train.Dynamics.Velocity = Speed;
                World.BeginTick();
                World.Step(new TrainControls { Reverser = 1 });
                Events.AddRange(World.EnemyEvents);
            }
        }
    }

    [Fact]
    public void ItSoundsFromJustShortOfTheBendAheadAndNowhereOnAStraight()
    {
        var night = new Night(speed: 14);
        var spot = LongWhistle.Spot(night.Train, W);
        Assert.NotNull(spot);
        Assert.InRange(spot!.Value, 2_600 - W.ShortOf - 10, 2_600 - W.ShortOf + 1);
        Assert.InRange(spot.Value - night.Train.Dynamics.Distance, W.AheadMin, W.AheadMax);
        Assert.Null(LongWhistle.Spot(new Night(speed: 14, StraightLine).Train, W));
    }

    [Fact]
    public void IgnoredItEscalatesTwiceThenAbandons()
    {
        var night = new Night(speed: 3);
        var w = night.Ahead();
        night.Run(SimConstants.TickSeconds);
        Assert.Equal(SpinePhase.Telegraph, w.Phase);
        Assert.Equal(1, w.Blasts);
        var blasts = new List<int>();
        night.Run(W.BlastEvery * (W.Escalations + 1) + 1, () => blasts.Add(w.Blasts));
        Assert.Equal([1, 2, 3], blasts.Distinct());
        Assert.True(w.Gone);
        Assert.DoesNotContain(night.Events, e => e.EnemyId == w.Id && e.To is SpinePhase.Commit or SpinePhase.Punish);
        Assert.False(night.World.BrakedForFalseAlarm);
    }

    [Fact]
    public void BrakeHardForItAndTheStopIsThePunishment()
    {
        var night = new Night(speed: 14);
        var w = night.Ahead();
        night.Run(2);
        // For a train that isn't there.
        night.Run(8, () => night.Speed = Math.Max(0, night.Speed - 14 * SimConstants.TickSeconds / 6));
        Assert.Contains(night.Events, e => e.EnemyId == w.Id && e.To == SpinePhase.Commit);
        Assert.True(night.World.BrakedForFalseAlarm);
        Assert.Contains(night.Events, e => e.EnemyId == w.Id && e.To == SpinePhase.Punish);
        night.Run(1);
        Assert.True(w.Gone);
    }

    [Fact]
    public void BrakingAtTheFirstBlastStillGetsTheReactionWindow()
    {
        var night = new Night(speed: 14);
        var w = night.Ahead();
        night.Run(SimConstants.TickSeconds);
        night.Speed = 0;
        night.Run(3);
        var commit = Assert.Single(night.Events, e => e.EnemyId == w.Id && e.To == SpinePhase.Commit);
        Assert.True(commit.SecondsInFrom >= Tuning.Enemies.MinReactionSeconds);
    }

    [Fact]
    public void ReachingWhereItWasItsGone()
    {
        var night = new Night(speed: 20);
        var w = night.Ahead();
        night.Run((w.LineDistance - night.Train.Dynamics.Distance) / 20 + 1);
        Assert.True(w.Gone);
    }

    static Route.Route Frontier() => new("t", RouteTier.Frontier, 1, Bend, [], new RouteWeather(0.01, false, 0, 0), 3600);

    [Fact]
    public void TheDirectorNeverSendsItAlone()
    {
        var d = Tuning.Enemies.Director;
        var only = Tuning.Enemies with
        {
            // Cheap enough for the budget this early in the line (the gate is what's under test, not the budget).
            Director = d with { GraceSeconds = 0, CooldownSeconds = [1, 1], Costs = d.Costs.ToDictionary(c => c.Key, c => c.Key == "longWhistle" ? 0.5 : 1e9) },
        };
        // App. B.1's table: it costs 3.
        Assert.Equal(3, d.Costs["longWhistle"]);
        var alone = new Night(speed: 3, enemies: only, route: Frontier());
        alone.Run(20);
        Assert.DoesNotContain(alone.World.Director!.Log, l => l.Kind == EnemyKind.LongWhistle);

        // Something else about (a Lamplighter pacing the engine): now the horn has company to do its killing.
        var company = new Night(speed: 3, enemies: only, route: Frontier());
        company.World.LampLit = false;
        company.World.AddEnemy(id => Lamplighter.Beside(id, company.Train, 1, Tuning.Enemies.Lamplighters));
        company.Run(20);
        var sent = Assert.Single(company.World.Director!.Log, l => l.Kind == EnemyKind.LongWhistle);
        Assert.Equal(0.5, sent.Cost);
        var whistle = Assert.Single(company.World.ActiveEnemies.OfType<LongWhistle>());
        Assert.InRange(whistle.LineDistance, 2_600 - W.ShortOf - 10, 2_600);
    }

    [Fact]
    public void ItsHeardByEveryClientWhereverItIs()
    {
        // Past the interest radius, but its horn carries: it goes to everyone.
        var night = new Night(speed: 14);
        var w = night.Ahead();
        Assert.True(w.Far);
        Assert.True((w.WorldPosition(night.Train) - night.Train.Frames[^1].Origin).Length > Tuning.Enemies.InterestRadius);
        night.Run(1);
        var client = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 5, 1)), BendLine, 2_000), Tuning.Combat);
        client.EnableEnemies(Tuning.Enemies, route: null, 1, crew: 1, authority: false);
        var controls = new TrainControls();
        WorldRecords.Apply(WorldRecords.Capture(night.World, controls, []), client, ref controls, []);
        var heard = Assert.IsType<LongWhistle>(Assert.Single(client.ActiveEnemies));
        Assert.Equal(1, heard.Blasts);
    }

    [Fact]
    public void BrakingForItWeightsTheFerrymanUp()
    {
        Assert.True(Tuning.Enemies.Ferryman.FalsePositiveWeight > 1);
        var night = new Night(speed: 14);
        night.Ahead();
        night.Run(2);
        night.Speed = 0;
        night.Run(2);
        Assert.True(night.World.BrakedForFalseAlarm);
    }
}
