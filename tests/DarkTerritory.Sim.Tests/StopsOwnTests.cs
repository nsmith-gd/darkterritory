using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The stops' own (GDD App. B.1 "The stops' own"; ARCHITECTURE §8 note 860; the director's playtest of 16 Oct evening, line 7:
/// "Monsters are positioned and ready at yards and abandoned villages, or use those stops to get onto the train"). What a stop
/// met was whatever the pressure could spend while the train stood there; now, as the train stands at a yard, the director
/// puts in place one of what lives there, and once the crew is busy with the stop one that boards there, beside the pressure,
/// once a stop.
/// </summary>
public class StopsOwnTests
{
    static readonly DirectorTuning D = Tuning.Enemies.Director;

    /// <summary>The director with a pressure that never spends and no free spawns: the stops' own are all that's left. Every kind about.</summary>
    static EnemyTuning Own(StopsTuning? stops = null) => Tuning.Enemies with
    {
        Director = D with
        {
            Pressure = D.Pressure with { Threshold = 1e9, Max = 1e9, PressAt = 1e9 },
            Curve = D.Curve with { Free = false, Fill = false },
            Stops = stops ?? D.Stops,
            NightChance = [],
        },
    };

    /// <summary>A Frontier night stood braked at its first yard, the run underway; its director's next decisions.</summary>
    static Night AtTheFirstYard(EnemyTuning tuning, ulong seed = 1)
    {
        var route = RouteGenerator.Generate(Tuning.Route, RouteTier.Frontier, 1);
        var yard = route.Of(FeatureKind.Facility).First(f => f.Stop is not null);
        var n = new Night(4, speed: 0, route, seed: seed, enemies: tuning, front: yard.Start + 30)
        {
            Controls = new TrainControls { Reverser = 1, Brake = 1 },
        };
        n.World.EnableRun(Tuning.Run, route, 600, authority: true, FacilityTests.F);
        n.World.Run!.Resume(600, -1, 0, 0);
        n.Run(1);
        return n;
    }

    static List<DirectorSpawn> Think(World world, int seconds)
    {
        for (int i = 0; i < seconds; i++)
            world.Director!.Decide(world, 1_000 + i, world.ActiveEnemies, 500);
        return [.. world.Director!.Log.Where(l => l.Stop)];
    }

    [Fact]
    public void AsTheTrainStandsAtAYardWhatLivesThereComesAtOnceAndTheBoardersOnceTheCrewIsBusyOnceAStop()
    {
        var n = AtTheFirstYard(Own());
        Assert.True(Director.StopAt(n.World) >= 0);
        // A Frontier night for a crew of four: three, two waiting and one boarding (note 868).
        var (waiting, boarding) = D.Stops.Split(D.Stops.Count(RouteTier.Frontier, 4));
        Assert.Equal((2, 1), (waiting, boarding));
        // In the first seconds stood there, inside the facility lull, what lives there: it was there before the train came.
        var first = Think(n.World, 5);
        Assert.Equal(waiting, first.Count);
        Assert.All(first, l => Assert.DoesNotContain(Director.Key(l.Kind), D.Stops.Boarders));
        Assert.Equal(first.Count, first.Select(l => l.Kind).Distinct().Count());
        // The boarder once the crew's busy with the stop (not while they cut the train at its points).
        Assert.Equal(waiting, Think(n.World, (int)D.Stops.BoardingAfterSeconds - 10).Count);
        var sent = Think(n.World, 10);
        Assert.Equal(waiting + boarding, sent.Count);
        Assert.Contains(Director.Key(sent[^1].Kind), D.Stops.Boarders);
        Assert.All(sent, l => Assert.False(l.Free));
        // Once a stop: standing on there sends nothing more of the stop's own.
        Assert.Equal(waiting + boarding, Think(n.World, 300).Count);
        Assert.DoesNotContain(n.World.Director!.Log, l => !l.Stop);
    }

    /// <summary>
    /// Note 868 (the director, 19 Oct 2026): "Easy difficulty and low player count means one creature. High difficulty high
    /// player count should be at least 4+ creatures to deal with per stop."
    /// </summary>
    [Fact]
    public void HowManyComeGoesByTheTierAndTheCrewOneForALocalCrewOfOneFourOrMoreDeepWithFourOrMore()
    {
        var st = D.Stops;
        Assert.Equal(1, st.Count(RouteTier.Local, 1));
        Assert.Equal((1, 0), st.Split(1));
        for (int crew = 4; crew <= 8; crew++)
            Assert.True(st.Count(RouteTier.DeepTerritory, crew) >= 4, $"deep, crew of {crew}: {st.Count(RouteTier.DeepTerritory, crew)}");
        Assert.True(st.Count(RouteTier.DeadLines, 8) >= 4);
        // More with a bigger crew and a deeper tier, never fewer; a boarder among them from two on.
        foreach (var tier in new[] { RouteTier.Local, RouteTier.Frontier, RouteTier.DeadLines, RouteTier.DeepTerritory })
            for (int crew = 1; crew < 8; crew++)
            {
                Assert.True(st.Count(tier, crew + 1) >= st.Count(tier, crew));
                Assert.True(st.Count(tier + 1 > RouteTier.DeepTerritory ? tier : tier + 1, crew) >= st.Count(tier, crew));
            }
        Assert.Equal((1, 1), st.Split(2));
        Assert.Equal((3, 2), st.Split(5));
    }

    /// <summary>
    /// The night's floor (note 868; the director, 19 Oct 2026: "It should be impossible to have a night with no creatures").
    /// Seed 1's chances roll 22 of the 28 kinds out, every creature that lives at a yard but the Followers among them.
    /// </summary>
    [Fact]
    public void NoNightRollsOutEveryCreatureOrEveryStopResident()
    {
        var off = new Director(D with { Stops = D.Stops with { On = false } }, null, 1, 6, 4);
        var residents = D.Stops.Residents;
        // Only the Followers about, and they come only to whoever stands at a yard's loading faces.
        Assert.Equal(["followers"], residents.Where(r => !off.OutTonight(Kind(r))));
        for (ulong seed = 1; seed <= 300; seed++)
            foreach (var (tier, crew) in new[] { (RouteTier.Local, 1), (RouteTier.Frontier, 2), (RouteTier.DeepTerritory, 8) })
            {
                var route = new Route.Route("t", tier, seed, new Rail.LineDefinition("t", [new Rail.TrackSegment(1000)]), [], new RouteWeather(0, false, 0, 0), 1440);
                var d = new Director(D, route, seed, 6, crew);
                int about = Spawns.Rules.Select(r => r.Kind).Distinct().Count(k => !d.OutTonight(k));
                Assert.True(about >= D.Stops.FloorKinds, $"seed {seed}: {about} about");
                int wants = Math.Min(D.Stops.Split(D.Stops.Count(tier, crew)).Waiting, residents.Length);
                Assert.True(residents.Count(r => !d.OutTonight(Kind(r))) >= wants, $"seed {seed}, {tier}, crew {crew}");
            }
        // What the roll left about stays about: the floor only brings kinds back.
        var floored = new Director(D, null, 1, 6, 4);
        Assert.All(D.NightChance.Keys.Where(k => !off.OutTonight(Kind(k))), k => Assert.False(floored.OutTonight(Kind(k))));
        Assert.NotEmpty(floored.FloorBrought);
    }

    static EnemyKind Kind(string key) => Enum.GetValues<EnemyKind>().First(k => Director.Key(k) == key);

    /// <summary>
    /// Note 868: a stop's own that placed nothing (the Ribbits, with warrens at the stop but none near the crew) is taken back,
    /// and the stop tries again: frontier:7's 4-bot seeds 2 and 3 had spent a waiting slot on it.
    /// </summary>
    [Fact]
    public void OneThatPlacedNothingIsTakenBackAndTheStopTriesAgain()
    {
        var n = AtTheFirstYard(Own());
        var d = n.World.Director!;
        var (waiting, _) = D.Stops.Split(D.Stops.Count(RouteTier.Frontier, 4));
        // The fixture's first second stood there sent (and placed) the first; the next is decided here, and placed nothing.
        int before = d.Log.Count(l => l.Stop);
        Assert.True(before < waiting);
        // Note 885: chosen, it's not yet on the log; placing nothing, it never is, and the failure's kept with its reason.
        var attempt = d.Choose(n.World, 1_000, n.World.ActiveEnemies, 500);
        Assert.NotNull(attempt);
        Assert.Equal(SpawnChannel.Stop, attempt.Channel);
        Assert.Equal(before, d.Log.Count(l => l.Stop));
        d.Commit(n.World, attempt, SpawnOutcome.Failed(SpawnFailure.NoSite), n.World.ActiveEnemies);
        Assert.Equal(before, d.Log.Count(l => l.Stop));
        Assert.Equal(SpawnFailure.NoSite, d.Attempts[^1].Failure);
        // The stop still gets its whole count.
        Assert.Equal(waiting, Think(n.World, 5).Count);
    }

    [Fact]
    public void NothingOfTheStopsOwnStoodAwayFromAStopOrWithItOff()
    {
        // Mid-line, stood: no stop.
        var line = new Night(4, speed: 0, enemies: Own());
        Assert.Equal(-1, Director.StopAt(line.World));
        Assert.Empty(Think(line.World, 60));
        // At the yard with it off.
        var off = AtTheFirstYard(Own(D.Stops with { On = false }));
        Assert.Empty(Think(off.World, 60));
    }

    [Fact]
    public void TheSameNightSendsTheSameAndItsDiceAreItsOwn()
    {
        var a = Think(AtTheFirstYard(Own(), seed: 7).World, 100).Select(l => l.Kind).ToList();
        var b = Think(AtTheFirstYard(Own(), seed: 7).World, 100).Select(l => l.Kind).ToList();
        Assert.Equal(a, b);
        Assert.Equal(D.Stops.Count(RouteTier.Frontier, 4), a.Count);
    }

    [Fact]
    public void EveryBoarderIsAKindTheDirectorSends()
    {
        var kinds = Spawns.Rules.Select(r => Director.Key(r.Kind)).ToHashSet();
        Assert.NotEmpty(D.Stops.Boarders);
        Assert.All(D.Stops.Boarders, b => Assert.Contains(b, kinds));
        Assert.True(D.Stops is { On: true, BoardingAfterSeconds: 40, TrySeconds: 240 });
    }
}
