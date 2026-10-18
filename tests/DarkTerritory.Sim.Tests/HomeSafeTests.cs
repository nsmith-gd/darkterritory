using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Towns;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 851 (queue #579; the director's playtest of 16 Oct, line 24: "My entire car is exploding at the very end of the run.
/// Why? ... I'm inside the yard ... I don't know why my car is on fire after I've gotten into a safe area"): in through the
/// arrival's gates (note 847) or delivered, nothing on the train burns, bursts or takes fire damage: a fire goes out, no hound
/// lights it, no hot box catches, no powder car goes up. Short of the gates, it burns as ever.
/// </summary>
public class HomeSafeTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TownContent Towns = TownContent.Load(Content)!;
    static readonly EnemyTuning E = Tuning.Enemies;
    static readonly PlayerTuning P = Tuning.Player;
    static readonly HotBoxTuning H = DataFile.Load<UpkeepTuning>(Path.Combine(DataFile.FindContentRoot(), UpkeepTuning.File)).HotBox;

    sealed class Night
    {
        public readonly World World;
        public readonly Dictionary<int, PlayerState> Crew = new();
        public TrainOnLine Train => World.Train;

        /// <param name="pastGate">Where the engine's front stands, past the arrival's gate (m; negative: short of it).</param>
        public Night(double pastGate, EnemyTuning? enemies = null)
        {
            var route = Routes.Generate(Content, "frontier:7", 6);
            double gate = route.GateOr(RouteTuning.Load(Content).YardLength);
            var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), route.Build(), route.Plan!.Terminus.GateM + pastGate);
            World = new World(train, Tuning.Combat);
            var quiet = enemies ?? HoundRunTests.Quiet;
            World.EnableEnemies(quiet with { Director = quiet.Director with { GraceMinSeconds = 1e9, GraceMaxSeconds = 1e9 } }, route, 1, crew: 4, authority: true);
            World.EnableRun(Tuning.Run, route, gate, authority: true);
            World.EnableTown(Towns, route, gate, [], arrival: TownAt.Terminus(route, LineGenContent.Cached(Content).Config.Tiers.Fortress.Identities));
            World.Upkeep = new UpkeepTuning { HotBox = H, Lamp = new() { Enabled = false } };
        }

        public void Run(double seconds)
        {
            for (int i = 0; i < seconds * SimConstants.TickRate; i++)
            {
                World.BeginTick();
                foreach (var id in Crew.Keys.ToList())
                {
                    var s = Crew[id];
                    World.CrewAct(ref s, default, id);
                    Crew[id] = s;
                }
                World.Step(new TrainControls { Reverser = 1 });
                World.ApplyDamage(id => Crew.TryGetValue(id, out var s) ? s : null, (id, s) => Crew[id] = s, Crew.Keys);
                World.StepRun([.. Crew.Values]);
            }
        }

        public CarFire Ablaze(int car, double heat) =>
            (CarFire)World.AddEnemy(id => CarFire.In(id, Train, car, 0, E.CarFire, FireCause.Set).Ablaze(heat));

        public bool Burning(int car) => World.ActiveEnemies.Any(e => e is CarFire f && !f.Gone && f.Attached == car);
    }

    [Fact]
    public void InThroughTheArrivalsGatesAFireGoesOutAndTheCarStopsTakingDamage()
    {
        var n = new Night(400);
        Assert.True(n.World.HomeSafe(3));
        var fire = n.Ablaze(3, 1.0);
        n.Run(1);
        Assert.True(fire.Gone);
        Assert.False(n.Burning(3));
        Assert.All(fire.Heat, h => Assert.Equal(0, h));
        Assert.Equal(1, n.Train.Vehicles[3].Integrity);
        Assert.Equal(1, n.Train.Vehicles[3].CargoIntegrity);
    }

    [Fact]
    public void ShortOfTheGatesItBurnsAsEver()
    {
        var n = new Night(-120);
        Assert.False(n.World.HomeSafe(3));
        var fire = n.Ablaze(3, 1.0);
        n.Run(3);
        Assert.False(fire.Gone);
        Assert.True(n.Train.Vehicles[3].Integrity < 1);
    }

    [Fact]
    public void HomeNoHoundLightsItAndNoHotBoxCatches()
    {
        // A hound that rode in is fought in the yard a while (note 589) and feeds meanwhile; a hot box already hot keeps
        // counting. Neither starts a fire now, and nothing's told.
        var n = new Night(400, HoundRunTests.Quiet with { CinderHounds = HoundRunTests.Quiet.CinderHounds with { IgniteEverySeconds = 1 } });
        int car = 3;
        var hound = n.World.AddEnemy(id => new CinderHound(id, id));
        hound.Restore(SpinePhase.Commit, 0, E.CinderHounds.Health, car, new Double3(0.6, n.Train.Frames[car].Shape.RoofHeight, 0), 0, 0, 0.6, hound.Id, 0);
        n.Train.Vehicles[2].HotBox = H.FireAfter - 1;
        n.Run(4);
        Assert.DoesNotContain(n.World.ActiveEnemies, e => e is CarFire);
        Assert.DoesNotContain(n.World.NewsLog, e => e.Kind == NewsKind.Fire);
        Assert.Empty(n.World.Attribution.Of(IncidentKind.Fire));
        Assert.Equal(1, n.Train.Vehicles[2].Integrity);
    }

    [Fact]
    public void HomeAPowderCarAlightDoesntGoUp()
    {
        var n = new Night(400);
        int car = 3;
        n.Train.Vehicles[car].Cargo = CargoKind.Ammunition;
        var room = n.Train.Frames[car].Shape.Interior!.Value;
        n.Crew[1] = new PlayerState { Parent = car, Position = new Double3(room.Centre.X, room.Min.Y, 1), Surface = Surface.Deck, Health = P.Health };
        n.Ablaze(car, 0.97);
        n.Run(5);
        Assert.False(n.Burning(car));
        Assert.DoesNotContain(n.World.Impacts, i => i.Shooter == -1);
        Assert.Equal(1, n.Train.Vehicles[car].Integrity);
        Assert.Equal(P.Health, n.Crew[1].Health);
        Assert.True(n.Crew[1].Alive);
    }

    [Fact]
    public void TheGymStillCallsAFireAtAStandingTrainInItsRing()
    {
        // Note 676: the gym's train stands in the yard's safe ring and a fire's called there on purpose; it burns.
        var n = new Night(400);
        n.World.CallOnly = true;
        Assert.False(n.World.HomeSafe(3));
        var fire = n.Ablaze(3, 1.0);
        n.Run(1);
        Assert.False(fire.Gone);
    }
}
