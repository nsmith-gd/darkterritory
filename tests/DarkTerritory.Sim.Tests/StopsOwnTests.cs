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
    public void AsTheTrainStandsAtAYardOneOfWhatLivesThereAndOneThatBoardsThereAreSentOnceAStop()
    {
        var n = AtTheFirstYard(Own());
        Assert.True(Director.StopAt(n.World) >= 0);
        // In the first seconds stood there, inside the facility lull, what lives there: it was there before the train came.
        var waiting = Assert.Single(Think(n.World, 5));
        Assert.DoesNotContain(Director.Key(waiting.Kind), D.Stops.Boarders);
        // The boarder once the crew's busy with the stop (not while they cut the train at its points).
        Assert.Single(Think(n.World, (int)D.Stops.BoardingAfterSeconds - 10));
        var sent = Think(n.World, 10);
        Assert.Equal(2, sent.Count);
        Assert.Contains(Director.Key(sent[1].Kind), D.Stops.Boarders);
        Assert.All(sent, l => Assert.False(l.Free));
        // Once a stop: standing on there sends nothing more of the stop's own.
        Assert.Equal(2, Think(n.World, 300).Count);
        Assert.DoesNotContain(n.World.Director!.Log, l => !l.Stop);
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
        Assert.Equal(2, a.Count);
    }

    [Fact]
    public void EveryBoarderIsAKindTheDirectorSends()
    {
        var kinds = Spawns.Rules.Select(r => Director.Key(r.Kind)).ToHashSet();
        Assert.NotEmpty(D.Stops.Boarders);
        Assert.All(D.Stops.Boarders, b => Assert.Contains(b, kinds));
        Assert.True(D.Stops is { On: true, Waiting: 1, Boarding: 1, BoardingAfterSeconds: 90, TrySeconds: 240 });
    }
}
