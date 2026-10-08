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

    /// <summary>Priced so what's there to spend at this point covers the Track Doll or the hounds, but not both.</summary>
    internal static Dictionary<string, double> Either(World world)
    {
        double available = world.Director!.Allowance(world.Train.Dynamics.Distance);
        return new() { ["trackDoll"] = 0.4 * available, ["cinderHounds"] = 0.7 * available };
    }

    /// <summary>Only these kinds priced (the rest out of reach), no grace, a decision a second.</summary>
    static EnemyTuning Priced(Dictionary<string, double> costs, Func<DirectorTuning, DirectorTuning>? with = null)
    {
        var d = Tuning.Enemies.Director with
        {
            GraceMinSeconds = 0,
            GraceMaxSeconds = 0,
            Pressure = Tuning.Eager,
            Draw = Tuning.Unheld,
            CooldownSeconds = [1, 1],
            Costs = Tuning.Enemies.Director.Costs.ToDictionary(c => c.Key, c => costs.GetValueOrDefault(c.Key, 1e9)),
        };
        return Tuning.Enemies with { Director = with?.Invoke(d) ?? d };
    }

    [Fact]
    public void ItSavesUpForWhatItsSavingForRatherThanSpendingOnWhatsCheaper()
    {
        // Running on a straight at 12 m/s: the Track Doll (a straight ahead) and the Cinder Hounds (sustained speed) can both
        // come. Priced so there's enough for either but not both: saving for the hounds keeps the doll off until they've been.
        var costs = Either(Night(Priced([]), Frontier(), at: 40_000, speed: 12));
        var saving = Night(Priced(costs, d => d with { SaveFor = ["cinderHounds"] }), Frontier(), at: 40_000, speed: 12);
        Run(saving, 2, 12);
        Assert.Equal(EnemyKind.CinderHound, saving.Director!.Log[0].Kind);

        // Not saving: over a handful of nights, the cheaper one comes first at least once.
        bool cheaperFirst = false;
        for (ulong seed = 1; seed <= 8 && !cheaperFirst; seed++)
        {
            var spending = Night(Priced(costs, d => d with { SaveFor = [] }), Frontier(), at: 40_000, speed: 12, seed: seed);
            Run(spending, 2, 12);
            cheaperFirst = spending.Director!.Log[0].Kind == EnemyKind.TrackDoll;
        }
        Assert.True(cheaperFirst);
    }

    [Fact]
    public void NoSavingOnARunThatCantHaveIt()
    {
        // Too slow for the hounds (App. B.3 sustained speed): nothing's kept back for them.
        var costs = Either(Night(Priced([]), Frontier(), at: 40_000, speed: 5));
        var slow = Night(Priced(costs, d => d with { SaveFor = ["cinderHounds"] }), Frontier(), at: 40_000, speed: 5);
        Run(slow, 2, 5);
        Assert.Equal(EnemyKind.TrackDoll, slow.Director!.Log[0].Kind);
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
            coming.Choir = coming.Choir with { Build = 0.3 };
            coming.Train.Dynamics.Velocity = 12;
            coming.BeginTick();
            coming.Step(new TrainControls { Reverser = 1 });
        }
        Assert.Contains("choir+cinderHounds", coming.Director!.Pairs);
    }

    [Fact]
    public void TheTableIsInTheTuningAndOnlyNamesThingsThereAre()
    {
        var d = Tuning.Enemies.Director;
        Assert.NotEmpty(d.Conflicts);
        var known = d.Costs.Keys.Concat(["choir", "grade", "facilityLoading", "facilityStop"]).ToHashSet();
        // GDD v1.1 App. B.1's eight pairs, the Moose's four (note 339) and the Gannet's three (note 340).
        Assert.Equal(15, d.Conflicts.Length);
        Assert.All(d.Conflicts.SelectMany(p => p), side => Assert.Contains(side, known));
        Assert.Equal(1, d.PairsPerRun["frontier"]);
        Assert.Equal(2, d.PairsPerRun["deepTerritory"]);
    }

    [Fact]
    public void ADraggerNobodyWalksOverLetsGoAndFreesItsPlaceInTheFlank()
    {
        // Under the lip all night, two Draggers held the flank's two places for the whole run (T64): no Climbers, no Gaunt.
        var world = Night(Tuning.Enemies with { Director = Tuning.Enemies.Director with { GraceMinSeconds = 1e9, GraceMaxSeconds = 1e9 } }, Frontier(), at: 40_000, speed: 12);
        var dragger = world.AddEnemy(id => Dragger.Under(id, world.Train, 2, 1, 0));
        Run(world, Tuning.Enemies.Draggers.LingerSeconds - 5, 12);
        Assert.False(dragger.Gone);
        Run(world, 10, 12);
        Assert.True(dragger.Gone);
    }
}
