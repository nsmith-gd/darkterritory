using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The director's contradiction seeding and saving up (T64, App. B.1): it draws pairs from the conflict table, and it keeps
/// enough back for the rare, expensive threats that they come at all.
/// </summary>
public class ConflictSeedingTests
{
    static readonly LineDefinition Straight = new("t", [new TrackSegment(80_000)]);
    static readonly RailLine Line = new(Straight);

    static Route.Route Frontier(params RouteFeature[] features) =>
        new("t", RouteTier.Frontier, 1, Straight, features, new RouteWeather(0.01, false, 0, 0), 3600);

    static World Night(EnemyTuning tuning, Route.Route route, double at, double speed, int crew = 3, ulong seed = 1)
    {
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 5, 1)), Line, at);
        train.Dynamics.Velocity = speed;
        var world = new World(train, Tuning.Combat);
        world.EnableEnemies(tuning, route, seed, crew, authority: true);
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

    /// <summary>Only these kinds priced (the rest out of reach), no grace, a decision a second.</summary>
    static EnemyTuning Priced(Dictionary<string, double> costs, Func<DirectorTuning, DirectorTuning>? with = null)
    {
        var d = Tuning.Enemies.Director with
        {
            GraceSeconds = 0,
            CooldownSeconds = [1, 1],
            Costs = Tuning.Enemies.Director.Costs.ToDictionary(c => c.Key, c => costs.GetValueOrDefault(c.Key, 1e9)),
        };
        return Tuning.Enemies with { Director = with?.Invoke(d) ?? d };
    }

    [Fact]
    public void ItSavesUpForTheGauntRatherThanSpendingOnWhatsCheaper()
    {
        // Stopped halfway (the Gaunt's "during a stop"), with a lamp lit (a Lamplighter's). Priced so there's enough for
        // either, but not both: saving for the Gaunt keeps the Lamplighter off until it's been.
        var costs = new Dictionary<string, double> { ["lamplighters"] = 20, ["gaunt"] = 30 };
        var saving = Night(Priced(costs), Frontier(), at: 40_000, speed: 0);
        Run(saving, 2, 0);
        Assert.Equal(EnemyKind.Gaunt, saving.Director!.Log[0].Kind);

        // Not saving: over a handful of nights, the cheaper one comes first at least once.
        bool cheaperFirst = false;
        for (ulong seed = 1; seed <= 8 && !cheaperFirst; seed++)
        {
            var spending = Night(Priced(costs, d => d with { SaveFor = [] }), Frontier(), at: 40_000, speed: 0, seed: seed);
            Run(spending, 2, 0);
            cheaperFirst = spending.Director!.Log[0].Kind == EnemyKind.Lamplighter;
        }
        Assert.True(cheaperFirst);
    }

    [Fact]
    public void NoSavingOnARunThatCantHaveIt()
    {
        // A crew of two never gets the Gaunt (App. B.4): nothing's kept back for it.
        var costs = new Dictionary<string, double> { ["lamplighters"] = 20, ["gaunt"] = 30 };
        var two = Night(Priced(costs), Frontier(), at: 40_000, speed: 0, crew: 2);
        Run(two, 2, 0);
        Assert.Equal(EnemyKind.Lamplighter, two.Director!.Log[0].Kind);
    }

    [Fact]
    public void ALamplighterWithSleepersAheadIsAPairAndItsWeightedUp()
    {
        // Sleepers lying ahead: a Lamplighter now is "lamps down vs. lamps needed to see the track".
        var route = Frontier(new RouteFeature(FeatureKind.Sleepers, 41_000, 41_020));
        var costs = new Dictionary<string, double> { ["lamplighters"] = 1, ["clingers"] = 1 };
        int lamplighterFirst = 0;
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var world = Night(Priced(costs, d => d with { SaveFor = [] }), route, at: 40_000, speed: 10, seed: seed);
            Run(world, 1.5, 10);
            var d = world.Director!;
            if (d.Log[0].Kind == EnemyKind.Lamplighter)
            {
                lamplighterFirst++;
                Assert.Contains("lamplighters+sleepers", d.Pairs);
            }
        }
        // Even odds without the table; three to one with it.
        Assert.True(lamplighterFirst >= 7, $"{lamplighterFirst} of 12");
    }

    [Fact]
    public void HoundsWithTheChoirComingAreAPairAndWithoutItTheyreNot()
    {
        var tuning = Priced(new() { ["cinderHounds"] = 1 }, d => d with { SaveFor = [] });
        var quiet = Night(tuning, Frontier(), at: 40_000, speed: 12);
        Run(quiet, 2, 12);
        Assert.Equal(EnemyKind.CinderHound, quiet.Director!.Log[0].Kind);
        Assert.Empty(quiet.Director.Pairs);

        var coming = Night(tuning, Frontier(), at: 40_000, speed: 12);
        for (int i = 0; i < 2 * SimConstants.TickRate; i++)
        {
            coming.Choir = coming.Choir with { Aggro = Tuning.Combat.Choir.ApproachThreshold + 5 };
            coming.Train.Dynamics.Velocity = 12;
            coming.BeginTick();
            coming.Step(new TrainControls { Reverser = 1 });
        }
        Assert.Contains("choir+cinderHounds", coming.Director!.Pairs);
    }

    [Fact]
    public void OnceTheRunHasItsPairTheyreNotWeightedAnyMore()
    {
        // Frontier wants one: after that, a Lamplighter with Sleepers ahead is an even pick again.
        var route = Frontier(new RouteFeature(FeatureKind.Sleepers, 41_000, 41_020));
        var costs = new Dictionary<string, double> { ["lamplighters"] = 1, ["clingers"] = 1 };
        var world = Night(Priced(costs, d => d with { SaveFor = [], CooldownSeconds = [0.5, 0.5] }), route, at: 40_000, speed: 10);
        Run(world, 30, 10);
        Assert.Single(world.Director!.Pairs.Distinct());
        Assert.Contains(world.Director.Log, l => l.Kind == EnemyKind.Clinger);
    }

    [Fact]
    public void TheTableIsInTheTuningAndOnlyNamesThingsThereAre()
    {
        var d = Tuning.Enemies.Director;
        Assert.NotEmpty(d.Conflicts);
        var known = d.Costs.Keys.Concat(["choir", "grade", "facilityLoading", "drift", "followers"]).ToHashSet();
        Assert.All(d.Conflicts.SelectMany(p => p), side => Assert.Contains(side, known));
        Assert.Equal(1, d.PairsPerRun["frontier"]);
        Assert.Equal(2, d.PairsPerRun["deepTerritory"]);
    }

    [Fact]
    public void ADraggerNobodyWalksOverLetsGoAndFreesItsPlaceInTheFlank()
    {
        // Under the lip all night, two Draggers held the flank's two places for the whole run (T64): no Climbers, no Gaunt.
        var world = Night(Tuning.Enemies with { Director = Tuning.Enemies.Director with { GraceSeconds = 1e9 } }, Frontier(), at: 40_000, speed: 12);
        var dragger = world.AddEnemy(id => Dragger.Under(id, world.Train, 2, 1, 0));
        Run(world, Tuning.Enemies.Draggers.LingerSeconds - 5, 12);
        Assert.False(dragger.Gone);
        Run(world, 10, 12);
        Assert.True(dragger.Gone);
    }
}
