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
            GraceMinSeconds = 0, GraceMaxSeconds = 0, Pressure = Tuning.Eager,
            CooldownSeconds = [1, 1],
            Roster = roster,
            Costs = costs is null ? Tuning.Enemies.Director.Costs : Tuning.Enemies.Director.Costs.ToDictionary(c => c.Key, c => costs.GetValueOrDefault(c.Key, 1e9)),
        },
    };

    [Fact]
    public void TheDirectorSendsNothingOutsideTheRosterAndSavesUpForNothingOutsideIt()
    {
        // As ConflictSeedingTests' saving up: running on a straight, enough for the Track Doll or the hounds but not both,
        // saving for the hounds. With every kind about, the hounds come first; without them on the roster, the doll at once.
        var costs = ConflictSeedingTests.Either(Night(With([]), Frontier(), at: 40_000, speed: 12));
        EnemyTuning Saving(string[] roster) => With(roster, costs) is var t ? t with { Director = t.Director with { SaveFor = ["cinderHounds"] } } : t;
        var all = Night(Saving([]), Frontier(), at: 40_000, speed: 12);
        Run(all, 2, 12);
        Assert.Equal(EnemyKind.CinderHound, all.Director!.Log[0].Kind);
        var demo = Night(Saving(["trackDoll"]), Frontier(), at: 40_000, speed: 12);
        Run(demo, 2, 12);
        Assert.Equal(EnemyKind.TrackDoll, demo.Director!.Log[0].Kind);
        Assert.All(demo.Director.Log, l => Assert.Equal(EnemyKind.TrackDoll, l.Kind));
    }

    [Fact]
    public void ADemoNightSendsOnlyTheDemosFive()
    {
        // GDD v1.1 §21: the Track Doll, Car Hugger, Whistler, Tippy Toesie and Ribbits (and the hazards and fire).
        var roster = new[] { "trackDoll", "carHugger", "whistler", "tippyToesie", "ribbits", "sleepers", "drift", "carFire" };
        var world = Night(With(roster), Frontier(new RouteFeature(FeatureKind.Marsh, 10_500, 11_500)), at: 10_000, speed: 12);
        Run(world, 600, 12);
        Assert.NotEmpty(world.Director!.Log);
        Assert.All(world.Director.Log, l => Assert.Contains(Director.Key(l.Kind), roster));
    }

    [Fact]
    public void WhatsOffTheRosterDoesntComeUpOnItsConditionEither()
    {
        // The Stoker's hot fire (note 263, the director's decision of 6 Oct 2026: a firebox run hot draws it), stopped: off the
        // roster, it never comes.
        World Low(string[] roster)
        {
            var world = Night(With(roster, new Dictionary<string, double>()), Frontier(), at: 10_000, speed: 0, boiler: true);
            for (int s = 0; s < 90; s++)
            {
                var b = world.Train.Boiler;
                b.Firebox = Tuning.Boiler.FireboxCapacity;
                b.Pressure = 70;
                world.Train.Boiler = b;
                Run(world, 1, 0);
            }
            return world;
        }
        Assert.DoesNotContain(Low(["trackDoll"]).ActiveEnemies, e => e.Kind == EnemyKind.Stoker);
        Assert.Contains(Low([]).ActiveEnemies, e => e.Kind == EnemyKind.Stoker);
    }
}
