using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>The Gaunt (T60, App. A.4, B.4): still while anyone's looking, moving when nobody is. Counter: keep watching.</summary>
public class GauntTests
{
    static readonly GauntTuning G = Tuning.Enemies.Gaunt;
    static readonly LineDefinition Straight = new("t", [new TrackSegment(80_000)]);
    static readonly RailLine Line = new(Straight);

    sealed class Night
    {
        public readonly World World;
        public readonly List<PlayerState> Crew = [];
        public readonly List<PlayerIntent> Intents = [];
        public readonly List<EnemyEvent> Events = [];
        public double Speed;

        public Night(double speed = 0, int crew = 3, EnemyTuning? enemies = null, Route.Route? route = null, double at = 2_000)
        {
            Speed = speed;
            var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 5, 1)), Line, at);
            train.Dynamics.Velocity = speed;
            World = new World(train, Tuning.Combat);
            var quiet = Tuning.Enemies with { Director = Tuning.Enemies.Director with { GraceSeconds = 1e9 } };
            World.EnableEnemies(enemies ?? quiet, route, 1, crew, authority: true);
        }

        public TrainOnLine Train => World.Train;

        /// <summary>On a car's roof, looking forward (yaw 0, toward the engine) or back (yaw π).</summary>
        public int OnRoof(int car, double z = 0, double yaw = 0)
        {
            var s = PlayerMotor.SpawnOnRoof(Train, car, z, Tuning.Player);
            s.Yaw = yaw;
            Crew.Add(s);
            Intents.Add(default);
            return Crew.Count;
        }

        public Gaunt On(int car, double z = 0) => World.AddEnemy(id => Gaunt.OnRoof(id, Train, car, z));

        public void Run(double seconds, Action? each = null)
        {
            for (int i = 0; i < Math.Max(1, seconds * SimConstants.TickRate); i++)
            {
                each?.Invoke();
                Train.Dynamics.Velocity = Speed;
                World.BeginTick();
                for (int c = 0; c < Crew.Count; c++)
                {
                    var s = Crew[c];
                    World.CrewAct(ref s, Intents[c], c + 1);
                    Crew[c] = s;
                }
                World.Step(new TrainControls { Reverser = 1 });
                Events.AddRange(World.EnemyEvents);
                World.ApplyDamage(id => id <= Crew.Count ? Crew[id - 1] : null, (id, s) => Crew[id - 1] = s, Enumerable.Range(1, Crew.Count));
            }
        }

        public double Between(Gaunt g, int player) => (g.WorldPosition(Train) - PlayerMotor.WorldPosition(Crew[player - 1], Train)).Length;
    }

    [Fact]
    public void WatchedItDoesntMoveAtAll()
    {
        var night = new Night();
        int watcher = night.OnRoof(2, yaw: Math.PI); // looking back along the train at it
        var g = night.On(4);
        night.Run(0.1);
        var before = g.Local;
        night.Run(10);
        Assert.True(g.Observed);
        Assert.Equal(before, g.Local);
        Assert.Equal(4, g.Attached);
        Assert.Equal(Tuning.Player.Health, night.Crew[watcher - 1].Health);
    }

    [Fact]
    public void UnwatchedItComesAndTakesThem()
    {
        var night = new Night();
        int victim = night.OnRoof(2, yaw: 0); // looking forward, away from it
        var g = night.On(4);
        double far = night.Between(g, victim);
        night.Run(2);
        Assert.False(g.Observed);
        Assert.True(night.Between(g, victim) < far - 5);
        night.Run(15);
        Assert.False(night.Crew[victim - 1].Alive);
        Assert.Equal(DeathCause.Gaunt, night.Crew[victim - 1].Death);
        Assert.True(g.Gone);
        var commit = Assert.Single(night.Events, e => e.EnemyId == g.Id && e.To == SpinePhase.Commit);
        Assert.True(commit.SecondsInFrom >= Tuning.Enemies.MinReactionSeconds);
    }

    [Fact]
    public void WatchedForAMinuteWithoutABreakItWithdraws()
    {
        var night = new Night();
        night.OnRoof(2, yaw: Math.PI);
        var g = night.On(4);
        night.Run(G.RetreatSeconds - 1);
        Assert.False(g.Gone);
        night.Run(2);
        Assert.True(g.Gone);
        Assert.Contains(night.Events, e => e.EnemyId == g.Id && e.To == SpinePhase.BreakOff);
        Assert.DoesNotContain(night.Events, e => e.EnemyId == g.Id && e.To == SpinePhase.Commit);
    }

    [Fact]
    public void LookingAwayEvenBrieflyStartsTheMinuteAgainAndLetsItCloser()
    {
        var night = new Night();
        int watcher = night.OnRoof(1, yaw: Math.PI);
        var g = night.On(4);
        night.Run(40);
        var held = g.Local;
        // A glance away.
        night.Crew[watcher - 1] = night.Crew[watcher - 1] with { Yaw = 0 };
        night.Run(0.5);
        night.Crew[watcher - 1] = night.Crew[watcher - 1] with { Yaw = Math.PI };
        night.Run(G.RetreatSeconds - 25);
        Assert.False(g.Gone);
        Assert.NotEqual(held, g.Local);
    }

    [Fact]
    public void SomeoneShutInACarCantWatchIt()
    {
        var night = new Night();
        int inside = night.OnRoof(3, yaw: Math.PI);
        var room = night.Train.Frames[3].Shape.Interior!.Value;
        night.Crew[inside - 1] = night.Crew[inside - 1] with { Position = room.Centre with { Y = room.Min.Y }, Surface = Surface.Deck };
        int prey = night.OnRoof(1, yaw: 0);
        var g = night.On(4);
        night.Run(1);
        Assert.False(g.Observed);
        // Unwatched, it's moved off where it stood.
        Assert.True(g.Attached != 4 || Math.Abs(g.Local.Z) > 0.5);
        Assert.True(night.Crew[prey - 1].Alive);
    }

    static Route.Route Frontier(params RouteFeature[] features) => new("t", RouteTier.Frontier, 1, Straight, features, new RouteWeather(0.01, false, 0, 0), 3600);

    static EnemyTuning Only()
    {
        var d = Tuning.Enemies.Director;
        return Tuning.Enemies with
        {
            Director = d with { GraceSeconds = 0, CooldownSeconds = [1, 1], Costs = d.Costs.ToDictionary(c => c.Key, c => c.Key == "gaunt" ? 0.5 : 1e9) },
        };
    }

    [Fact]
    public void TheDirectorSendsItOnceAtAStopOrATunnelMouthToACrewOfThree()
    {
        Assert.Equal(5, Tuning.Enemies.Director.Costs["gaunt"]);
        var stopped = new Night(speed: 0, crew: 3, Only(), Frontier());
        stopped.OnRoof(1);
        stopped.Run(30);
        Assert.Single(stopped.World.Director!.Log, l => l.Kind == EnemyKind.Gaunt);

        var two = new Night(speed: 0, crew: 2, Only(), Frontier());
        two.Run(10);
        Assert.DoesNotContain(two.World.Director!.Log, l => l.Kind == EnemyKind.Gaunt);

        var running = new Night(speed: 12, crew: 3, Only(), Frontier());
        running.Run(10);
        Assert.DoesNotContain(running.World.Director!.Log, l => l.Kind == EnemyKind.Gaunt);

        var tunnel = new Night(speed: 12, crew: 3, Only(), Frontier(new RouteFeature(FeatureKind.Tunnel, 1_500, 1_990)));
        tunnel.Run(3);
        Assert.Single(tunnel.World.Director!.Log, l => l.Kind == EnemyKind.Gaunt);

        var local = new Night(speed: 0, crew: 3, Only(), Frontier() with { Tier = RouteTier.Local });
        local.Run(10);
        Assert.DoesNotContain(local.World.Director!.Log, l => l.Kind == EnemyKind.Gaunt);
    }

    [Fact]
    public void ARoofWalkerBotKeepsItsEyesOnIt()
    {
        var night = new Night();
        int bot = night.OnRoof(2, yaw: 0);
        var walker = new RoofWalkerBot(seed: 3);
        var g = night.On(4);
        night.Run(20, () => night.Intents[bot - 1] = walker.Decide(night.Crew[bot - 1], night.World, night.World.Tick, out _));
        Assert.True(g.Observed);
        Assert.True(night.Crew[bot - 1].Alive);
        night.Run(G.RetreatSeconds, () => night.Intents[bot - 1] = walker.Decide(night.Crew[bot - 1], night.World, night.World.Tick, out _));
        Assert.True(g.Gone);
        Assert.True(night.Crew[bot - 1].Alive);
    }

    [Fact]
    public void AClientSeesItWhereItStandsAndWhichWayItFaces()
    {
        var night = new Night();
        night.OnRoof(2, yaw: 0);
        var g = night.On(4);
        night.Run(1);
        var client = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 5, 1)), Line, 2_000), Tuning.Combat);
        client.EnableEnemies(Tuning.Enemies, route: null, 1, crew: 1, authority: false);
        var controls = new TrainControls();
        WorldRecords.Apply(WorldRecords.Capture(night.World, controls, []), client, ref controls, []);
        var seen = Assert.IsType<Gaunt>(Assert.Single(client.ActiveEnemies));
        Assert.Equal(g.Attached, seen.Attached);
        Assert.Equal(g.Local.Z, seen.Local.Z, 1);
        Assert.Equal(g.Extra2, seen.Extra2, 2);
    }
}
