using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>The Weight (T59, App. A.3, B.3): it takes hold of the rear car and drags. Cut the car, or beat it off.</summary>
public class WeightTests
{
    static readonly WeightTuning W = Tuning.Enemies.Weight;
    static readonly LineDefinition Straight = new("t", [new TrackSegment(80_000)]);
    static readonly RailLine Line = new(Straight);

    sealed class Night
    {
        public readonly World World;
        public readonly List<PlayerState> Crew = [];
        public readonly List<PlayerIntent> Intents = [];
        public readonly List<EnemyEvent> Events = [];
        public TrainControls Controls = new() { Reverser = 1, Throttle = 1 };

        public Night(double speed, int cars = 5, EnemyTuning? enemies = null, Route.Route? route = null, double at = 2_000)
        {
            var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, cars, 1)), Line, at);
            train.Dynamics.Velocity = speed;
            World = new World(train, Tuning.Combat);
            var quiet = Tuning.Enemies with { Director = Tuning.Enemies.Director with { GraceSeconds = 1e9 } };
            World.EnableEnemies(enemies ?? quiet, route, 1, crew: 2, authority: true);
        }

        public TrainOnLine Train => World.Train;
        public int Rear => Train.Dynamics.Consist.Vehicles[^1].Id;

        /// <summary>Just behind the train's rear, so the rear car's over it already: it takes hold at once.</summary>
        public Weight Under() => World.AddEnemy(id => Weight.Buried(id, Train.Dynamics.RearDistance + 1, 1));

        /// <summary>On the rear car's back end, within reach of the coupling below.</summary>
        public int OnRearPlatform()
        {
            var s = PlayerMotor.SpawnOnRoof(Train, Rear, 0, Tuning.Player);
            s.Position = new Ballast.Double3(0, 1.2, Train.Frames[Rear].Shape.HalfLength - 0.3);
            s.Surface = Surface.Deck;
            Crew.Add(s);
            Intents.Add(default);
            return Crew.Count;
        }

        public void Run(double seconds, Action? each = null)
        {
            for (int i = 0; i < Math.Max(1, seconds * SimConstants.TickRate); i++)
            {
                each?.Invoke();
                World.BeginTick();
                for (int c = 0; c < Crew.Count; c++)
                {
                    var s = Crew[c];
                    World.CrewAct(ref s, Intents[c], c + 1);
                    Crew[c] = s;
                }
                World.Step(Controls);
                Events.AddRange(World.EnemyEvents);
                World.ApplyDamage(id => id <= Crew.Count ? Crew[id - 1] : null, (id, s) => Crew[id - 1] = s, Enumerable.Range(1, Crew.Count));
            }
        }
    }

    [Fact]
    public void BuriedItWaitsForTheRearCarThenTakesHoldOfIt()
    {
        var night = new Night(speed: 12);
        var w = night.World.AddEnemy(id => Weight.Buried(id, night.Train.Dynamics.Distance + 60, 1));
        night.Run(1);
        Assert.Equal(SpinePhase.Dormant, w.Phase);
        Assert.Equal(-1, w.Attached);
        night.Run(20);
        Assert.True(w.Holding);
        Assert.Equal(night.Rear, w.Attached);
    }

    [Fact]
    public void ItDragsHarderThanTheEngineCanPullAndTheSpeedDecays()
    {
        var free = new Night(speed: 12);
        free.Run(10);
        Assert.True(free.Train.Dynamics.Speed > 12);

        var night = new Night(speed: 12);
        var w = night.Under();
        night.Run(10);
        Assert.True(w.Holding);
        // Full throttle all the while, and still slowing.
        Assert.True(night.Train.Dynamics.Speed < 12 - 0.3, $"{night.Train.Dynamics.Speed}");
    }

    [Fact]
    public void DraggedToAStandItPullsTheRearCarOffTheRails()
    {
        var night = new Night(speed: 4);
        int rider = night.OnRearPlatform();
        int rear = night.Rear;
        var w = night.Under();
        night.Run(120, () => night.Controls.Throttle = 0);
        Assert.Contains(night.Events, e => e.EnemyId == w.Id && e.To == SpinePhase.Punish);
        Assert.True(w.Gone);
        // Off the train and wrecked, and whoever was on it hurt.
        Assert.DoesNotContain(night.Train.Dynamics.Consist.Vehicles, v => v.Id == rear);
        Assert.Equal(0, night.Train.Vehicles[rear].Integrity);
        Assert.Equal(Tuning.Player.Health - W.TearOffDamage, night.Crew[rider - 1].Health);
        var commit = Assert.Single(night.Events, e => e.EnemyId == w.Id && e.To == SpinePhase.Commit);
        Assert.True(commit.SecondsInFrom >= Tuning.Enemies.MinReactionSeconds);
    }

    [Fact]
    public void CutTheRearCarAndItTakesTheCarAndGoes()
    {
        var night = new Night(speed: 12);
        int rear = night.Rear;
        var w = night.Under();
        night.Run(3);
        Assert.True(w.Holding);
        Assert.True(night.Train.Uncouple(night.Train.VehicleAhead(rear)));
        night.Run(1);
        Assert.True(w.Gone);
        Assert.Contains(night.Events, e => e.EnemyId == w.Id && e.To == SpinePhase.BreakOff);
        // The train's free of it: full throttle picks it up again.
        double before = night.Train.Dynamics.Speed;
        night.Run(5);
        Assert.True(night.Train.Dynamics.Speed > before);
    }

    [Fact]
    public void FiveBlowsFromTheRearPlatformAndItLetsGo()
    {
        var night = new Night(speed: 12);
        int hitter = night.OnRearPlatform();
        var w = night.Under();
        night.Run(1);
        Assert.True(w.Holding);
        // Holding Use is one blow, not one a tick.
        night.Intents[hitter - 1] = new PlayerIntent { Buttons = PlayerButtons.Use };
        night.Run(1);
        Assert.Equal(1, w.Blows);
        for (int i = 1; i < W.BlowsToRelease; i++)
        {
            night.Intents[hitter - 1] = default;
            night.Run(0.2);
            night.Intents[hitter - 1] = new PlayerIntent { Buttons = PlayerButtons.Use };
            night.Run(0.2);
        }
        Assert.True(w.Gone);
        Assert.DoesNotContain(night.Events, e => e.EnemyId == w.Id && e.To == SpinePhase.Punish);
    }

    [Fact]
    public void TheGunsCantTakeIt()
    {
        var night = new Night(speed: 12);
        var w = night.Under();
        night.Run(1);
        Assert.Equal(0, w.HitRadius);
        Assert.DoesNotContain(night.World.Targets, t => t.Id == w.Id);
    }

    static Route.Route WithBridge(double at) =>
        new("t", RouteTier.Frontier, 1, Straight, [new RouteFeature(FeatureKind.Bridge, at, at + 60)], new RouteWeather(0.01, false, 0, 0), 3600);

    [Fact]
    public void TheDirectorLaysItAtAWaterCrossingAheadAndNowhereElse()
    {
        var d = Tuning.Enemies.Director;
        var only = Tuning.Enemies with
        {
            Director = d with { GraceSeconds = 0, CooldownSeconds = [1, 1], Costs = d.Costs.ToDictionary(c => c.Key, c => c.Key == "weight" ? 0.5 : 1e9) },
        };
        Assert.Equal(3, d.Costs["weight"]);
        var dry = new Night(speed: 12, enemies: only, route: WithBridge(40_000));
        dry.Run(10);
        Assert.DoesNotContain(dry.World.Director!.Log, l => l.Kind == EnemyKind.Weight);

        var night = new Night(speed: 12, enemies: only, route: WithBridge(2_600));
        night.Run(3);
        Assert.Single(night.World.Director!.Log, l => l.Kind == EnemyKind.Weight);
        var w = Assert.Single(night.World.ActiveEnemies.OfType<Weight>());
        Assert.Equal(2_600 + W.IntoCrossing, w.LineDistance, 1);

        // A lone engine and one car: "train length ≥2".
        var shortTrain = new Night(speed: 12, cars: 1, enemies: only, route: WithBridge(2_600));
        shortTrain.Run(3);
        Assert.DoesNotContain(shortTrain.World.Director!.Log, l => l.Kind == EnemyKind.Weight);
    }

    [Fact]
    public void AClientDragsAsTheHostDoes()
    {
        var night = new Night(speed: 12);
        var w = night.Under();
        night.Run(1);
        var client = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 5, 1)), Line, 2_000), Tuning.Combat);
        client.EnableEnemies(Tuning.Enemies, route: null, 1, crew: 1, authority: false);
        var controls = night.Controls;
        WorldRecords.Apply(WorldRecords.Capture(night.World, controls, []), client, ref controls, []);
        var seen = Assert.IsType<Weight>(Assert.Single(client.ActiveEnemies));
        Assert.True(seen.Holding);
        client.BeginTick();
        client.Step(night.Controls);
        Assert.Equal(night.Rear, client.Train.DraggedVehicle);
    }
}
