using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The Drift (T63, App. A.4, B.4): over marsh, a mass over the train that surges at whoever moves in it and eats at them on
/// contact. Counter: stand stock still until it loses you.
/// </summary>
public class DriftTests
{
    static readonly DriftTuning D = Tuning.Enemies.Drift;
    static readonly LineDefinition Straight = new("t", [new TrackSegment(80_000)]);
    static readonly RailLine Line = new(Straight);

    static Route.Route Marsh(double from, double to) =>
        new("t", RouteTier.Frontier, 1, Straight, [new RouteFeature(FeatureKind.Marsh, from, to)], new RouteWeather(0.01, false, 0, 0), 3600);

    sealed class Night
    {
        public readonly World World;
        public readonly List<PlayerState> Crew = [];
        public readonly List<PlayerIntent> Intents = [];
        public readonly List<EnemyEvent> Events = [];
        public double Speed = 8;

        public Night(Route.Route? route = null, EnemyTuning? enemies = null, double at = 2_000)
        {
            var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 5, 1)), Line, at);
            train.Dynamics.Velocity = Speed;
            World = new World(train, Tuning.Combat);
            var quiet = Tuning.Enemies with { Director = Tuning.Enemies.Director with { GraceMinSeconds = 1e9, GraceMaxSeconds = 1e9 } };
            World.EnableEnemies(enemies ?? quiet, route ?? Marsh(0, 80_000), 1, crew: 3, authority: true);
        }

        public TrainOnLine Train => World.Train;

        public int OnRoof(int car, double z = 0)
        {
            Crew.Add(PlayerMotor.SpawnOnRoof(Train, car, z, Tuning.Player));
            Intents.Add(default);
            return Crew.Count;
        }

        public Drift Over(int car, double z = 0) => World.AddEnemy(id =>
        {
            var d = Drift.Over(id, Train, car, D);
            d.Local = d.Local with { Z = z };
            return d;
        });

        /// <summary>Walks a player along their car's roof at this pace (m/s), back and forth over a couple of metres.</summary>
        public void Pace(int player, double speed)
        {
            var s = Crew[player - 1];
            double z = s.Position.Z + speed * SimConstants.TickSeconds * (World.Tick / 60 % 2 == 0 ? 1 : -1);
            Crew[player - 1] = s with { Position = s.Position with { Z = z } };
        }

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
    }

    [Fact]
    public void StandingStillInItNobodyDrawsIt()
    {
        var night = new Night();
        int a = night.OnRoof(2, 1), b = night.OnRoof(2, -1);
        var d = night.Over(2);
        night.Run(20);
        Assert.Equal(SpinePhase.Dormant, d.Phase);
        Assert.Equal(0, d.Target);
        Assert.True(d.Radius > D.StartRadius);
        Assert.Equal(Tuning.Player.Health, night.Crew[a - 1].Health);
        Assert.Equal(Tuning.Player.Health, night.Crew[b - 1].Health);
    }

    [Fact]
    public void MovingInItItSurgesAtThemAndKeepsEatingWhileTheyMove()
    {
        var night = new Night();
        int walker = night.OnRoof(2, 2);
        var d = night.Over(2, -1);
        night.Run(1, () => night.Pace(walker, 1.2));
        Assert.Equal(SpinePhase.Telegraph, d.Phase);
        Assert.Equal(walker, d.Target);
        night.Run(60, () => night.Pace(walker, 1.2));
        Assert.False(night.Crew[walker - 1].Alive);
        Assert.Equal(DeathCause.Drift, night.Crew[walker - 1].Death);
        var commit = Assert.Single(night.Events, e => e.EnemyId == d.Id && e.To == SpinePhase.Commit);
        Assert.True(commit.SecondsInFrom >= Tuning.Enemies.MinReactionSeconds);
    }

    [Fact]
    public void StockStillForFourSecondsItLosesThem()
    {
        var night = new Night();
        int walker = night.OnRoof(2, 2);
        var d = night.Over(2, -1);
        night.Run(4, () => night.Pace(walker, 1.2));
        Assert.Equal(SpinePhase.Punish, d.Phase);
        int hurt = night.Crew[walker - 1].Health;
        Assert.True(hurt < Tuning.Player.Health);
        // Still now. It eats a little more while it's losing them, and then it has.
        night.Run(D.StillSeconds + 0.5);
        Assert.Equal(SpinePhase.Dormant, d.Phase);
        Assert.Equal(0, d.Target);
        Assert.Contains(night.Events, e => e.EnemyId == d.Id && e.To == SpinePhase.BreakOff);
        int after = night.Crew[walker - 1].Health;
        night.Run(10);
        Assert.Equal(after, night.Crew[walker - 1].Health);
        Assert.True(night.Crew[walker - 1].Alive);
    }

    [Fact]
    public void ShutInACarNobodyIsFelt()
    {
        var night = new Night();
        int inside = night.OnRoof(2);
        var room = night.Train.Frames[2].Shape.Interior!.Value;
        night.Crew[inside - 1] = night.Crew[inside - 1] with { Position = room.Centre with { Y = room.Min.Y }, Surface = Surface.Deck };
        var d = night.Over(2);
        night.Run(10, () => night.Pace(inside, 1.2));
        Assert.Equal(SpinePhase.Dormant, d.Phase);
        Assert.Equal(Tuning.Player.Health, night.Crew[inside - 1].Health);
    }

    [Fact]
    public void OffTheMarshItsGone()
    {
        var night = new Night(Marsh(1_000, 2_100));
        var d = night.Over(2);
        night.Run(2);
        Assert.False(d.Gone);
        // At 8 m/s the train's rear is clear of the marsh (and its margin) inside a minute.
        night.Run(60);
        Assert.True(d.Gone);
    }

    [Fact]
    public void ABotItsAfterStandsStill()
    {
        var night = new Night();
        int walker = night.OnRoof(2, 2);
        var d = night.Over(2, -1);
        night.Run(1, () => night.Pace(walker, 1.2));
        Assert.Equal(walker, d.Target);
        var told = Bots.Heed.Drift(new PlayerIntent { MoveZ = 1, Buttons = PlayerButtons.Run }, night.Crew[walker - 1], night.World, walker);
        Assert.Equal(0, told.MoveZ);
        Assert.False(told.Has(PlayerButtons.Run));
        // Someone else it isn't after walks on.
        var other = Bots.Heed.Drift(new PlayerIntent { MoveZ = 1 }, night.Crew[walker - 1], night.World, walker + 1);
        Assert.Equal(1, other.MoveZ);
    }

    [Fact]
    public void OverAMarshItComesUpOnceAndNowhereElse()
    {
        // A hazard (GDD v1.1 §22), not a spawn: over the marsh it's there, whatever the director's doing (it's quiet here),
        // and it costs the director nothing. Once a marsh.
        Assert.False(Tuning.Enemies.Director.Costs.ContainsKey("drift"));
        var dry = new Night(Marsh(40_000, 41_000));
        dry.Run(10);
        Assert.DoesNotContain(dry.World.ActiveEnemies, e => e.Kind == EnemyKind.Drift);

        var wet = new Night(Marsh(1_900, 2_300));
        wet.Run(2);
        var d = Assert.IsType<Drift>(Assert.Single(wet.World.ActiveEnemies, e => e.Kind == EnemyKind.Drift));
        Assert.Contains(d.Attached, wet.Train.Dynamics.Consist.Vehicles.Skip(1).Select(v => v.Id));
        Assert.DoesNotContain(wet.World.Director!.Log, l => l.Kind == EnemyKind.Drift);
        // Through it and out: gone, and not back.
        wet.Run(90);
        Assert.DoesNotContain(wet.World.ActiveEnemies, e => e.Kind == EnemyKind.Drift);
    }

    [Fact]
    public void AGeneratedLineMarksItsBogsAndTarPondsForIt()
    {
        // The line plan's marsh water on the main line (the bog and the tar ponds, linegen biomes) becomes the route's Marsh.
        string content = DataFile.FindContentRoot();
        foreach (var spec in new[] { "frontier:7", "frontier:3", "deadLines:3", "deadLines:5", "deepTerritory:2", "frontier:11" })
        {
            var route = LineGen.Routes.Generate(content, spec, 6);
            var bogs = route.Plan!.Water.Where(w => w.Edge == "main" && w.Type is "marsh" or "contaminatedMarsh").ToList();
            var marsh = route.Of(FeatureKind.Marsh).ToList();
            Assert.Equal(bogs.Count, marsh.Count);
            Assert.All(bogs, b => Assert.Contains(marsh, m => m.Start == b.S0 && m.End == b.S1));
            if (bogs.Count > 0)
                return;
        }
        Assert.Fail("none of those lines crosses a bog");
    }

    [Fact]
    public void AClientSeesHowFarItsSpreadAndWhoItsAfter()
    {
        var night = new Night();
        int walker = night.OnRoof(2, 2);
        var d = night.Over(2, -1);
        night.Run(1, () => night.Pace(walker, 1.2));
        var client = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 5, 1)), Line, 2_000), Tuning.Combat);
        client.EnableEnemies(Tuning.Enemies, route: null, 1, crew: 1, authority: false);
        var controls = new TrainControls();
        WorldRecords.Apply(WorldRecords.Capture(night.World, controls, []), client, ref controls, []);
        var seen = Assert.IsType<Drift>(Assert.Single(client.ActiveEnemies));
        Assert.Equal(walker, seen.Target);
        Assert.Equal(d.Radius, seen.Radius, 2);
        Assert.Equal(d.Attached, seen.Attached);
    }
}
