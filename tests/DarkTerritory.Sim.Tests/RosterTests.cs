using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// An edition's roster (T79): the demo has GDD §21's five, and the director neither sends anything outside it nor saves up
/// for it, and nothing condition-triggered outside it comes up.
/// </summary>
public class RosterTests
{
    static readonly LineDefinition Straight = new("t", [new TrackSegment(80_000)]);
    static readonly RailLine Line = new(Straight);

    static Route.Route Frontier(params RouteFeature[] features) =>
        new("t", RouteTier.Frontier, 1, Straight, features, new RouteWeather(0.01, false, 0, 0), 3600);

    static World Night(EnemyTuning tuning, Route.Route route, double at, double speed, bool boiler = false)
    {
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 5, 1)), Line, at, boiler ? Tuning.Boiler : null);
        train.Dynamics.Velocity = speed;
        var world = new World(train, Tuning.Combat);
        world.EnableEnemies(tuning, route, 1, crew: 3, authority: true);
        return world;
    }

    static void Run(World world, double seconds, double speed)
    {
        for (int i = 0; i < seconds * SimConstants.TickRate; i++)
        {
            world.Train.Dynamics.Velocity = speed;
            world.BeginTick();
            world.Step(new TrainControls { Reverser = 1 });
        }
    }

    static EnemyTuning With(string[] roster, Dictionary<string, double>? costs = null) => Tuning.Enemies with
    {
        Director = Tuning.Enemies.Director with
        {
            GraceSeconds = 0,
            CooldownSeconds = [1, 1],
            Roster = roster,
            Costs = costs is null ? Tuning.Enemies.Director.Costs : Tuning.Enemies.Director.Costs.ToDictionary(c => c.Key, c => costs.GetValueOrDefault(c.Key, 1e9)),
        },
    };

    [Fact]
    public void TheDirectorSendsNothingOutsideTheRosterAndSavesUpForNothingOutsideIt()
    {
        // As ConflictSeedingTests' saving up: stopped halfway, a lamp lit, enough for the Lamplighter or the Gaunt but not
        // both. With every kind about, the Gaunt comes first; without the Gaunt on the roster, the Lamplighter comes at once.
        var costs = new Dictionary<string, double> { ["lamplighters"] = 20, ["gaunt"] = 30 };
        var all = Night(With([], costs), Frontier(), at: 40_000, speed: 0);
        Run(all, 2, 0);
        Assert.Equal(EnemyKind.Gaunt, all.Director!.Log[0].Kind);
        var demo = Night(With(["lamplighters"], costs), Frontier(), at: 40_000, speed: 0);
        Run(demo, 2, 0);
        Assert.Equal(EnemyKind.Lamplighter, demo.Director!.Log[0].Kind);
        Assert.All(demo.Director.Log, l => Assert.Equal(EnemyKind.Lamplighter, l.Kind));
    }

    [Fact]
    public void ADemoNightSendsOnlyTheDemosFive()
    {
        var roster = new[] { "sleepers", "cinderHounds", "clingers", "hollow", "carFire", "looseLoad", "gnawers" };
        var world = Night(With(roster), Frontier(new RouteFeature(FeatureKind.Marsh, 10_500, 11_500)), at: 10_000, speed: 12);
        Run(world, 600, 12);
        Assert.NotEmpty(world.Director!.Log);
        Assert.All(world.Director.Log, l => Assert.Contains(Director.Key(l.Kind), roster));
    }

    [Fact]
    public void WhatsOffTheRosterDoesntComeUpOnItsConditionEither()
    {
        // The Deadman's empty cab and the Hollow's low fire, stopped (nobody aboard at all): without them, nothing comes.
        var world = Night(With(["clingers"], new Dictionary<string, double>()), Frontier(), at: 10_000, speed: 0, boiler: true);
        var b = world.Train.Boiler;
        b.Firebox = 0;
        world.Train.Boiler = b;
        Run(world, 90, 0);
        Assert.DoesNotContain(world.ActiveEnemies, e => e.Kind is EnemyKind.Deadman or EnemyKind.Hollow);
        var every = Night(With([], new Dictionary<string, double>()), Frontier(), at: 10_000, speed: 0, boiler: true);
        b = every.Train.Boiler;
        b.Firebox = 0;
        every.Train.Boiler = b;
        Run(every, 90, 0);
        Assert.Contains(every.ActiveEnemies, e => e.Kind is EnemyKind.Deadman or EnemyKind.Hollow);
    }
}
