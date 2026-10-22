using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// A halt's own (queue #609, note 895; GDD D.15 question 3, the director: "Needs a dedicated response", and on 20 Oct: "What's
/// best for gameplay?"): stood on the main line at an abandoned village's halt, after its lull, one encounter, counted as the
/// stop's; none before the lull, no second one, and none with the flag off (it's off until measured: "test creature spawn first").
/// </summary>
public class HaltEncounterTests
{
    static readonly EnemyTuning E = Tuning.Enemies;

    /// <summary>
    /// The director quiet but for the halt (no pressure, no free spawns, the stops' own off), so whatever comes at the halt is
    /// the halt's; <paramref name="on"/> its flag.
    /// </summary>
    static EnemyTuning Halt(bool on) => HoundRunTests.Quiet with
    {
        Director = HoundRunTests.Quiet.Director with
        {
            GraceMinSeconds = 5,
            GraceMaxSeconds = 5,
            NightChance = new Dictionary<string, double>(),
            Stops = HoundRunTests.Quiet.Director.Stops with { On = false },
            Encounters = HoundRunTests.Quiet.Director.Encounters with { Halt = HoundRunTests.Quiet.Director.Encounters.Halt with { On = on } },
        },
    };

    /// <summary>A train of three stood on the main line over the first halt of the first route that has one, crew afoot by it.</summary>
    static GuardNight AtAHalt(EnemyTuning enemies)
    {
        double length = Consist.Uniform(Tuning.Train, 3, 1).LengthMetres;
        for (ulong seed = 1; seed < 40; seed++)
        {
            var route = RouteGenerator.Generate(Tuning.Route, RouteTier.Frontier, seed);
            if (route.Features.FirstOrDefault(f => f.Kind == FeatureKind.Village && f.Stop?.Halt is not null && f.End - f.Start > length + 20) is not { } halt)
                continue;
            double front = (halt.Start + halt.End) / 2 + length / 2;
            var n = new GuardNight(enemies, route, front, speed: 0, cars: 3, run: true);
            n.Controls = new TrainControls { Reverser = 1, Brake = 1 };
            n.Underway(900);
            n.Aboard(1);
            for (int i = 2; i <= 4; i++)
                n.Crew[i] = n.Ground(front - 10 * i, lateral: 4);
            return n;
        }
        throw new InvalidOperationException("no route with a halt long enough for a train of three");
    }

    static int HaltSends(GuardNight n) => n.Director.Log.Count(l => l.Stop);

    [Fact]
    public void ItsOnAsTheDirectorChose() => Assert.True(E.Director.Encounters.Halt.On, "the director, 21 Oct (n1-A3-halt-own-creature), measured in note 970");

    [Fact]
    public void StoodAtAHaltOneEncounterComesAfterTheLullAndNoSecond()
    {
        var t = Halt(on: true).Director.Encounters.Halt;
        var n = AtAHalt(Halt(on: true));
        Assert.True(Director.StopAt(n.World) >= 1000, "not stood at a village stop");
        // Before the lull: nothing of the halt's (the grace's 5 s and the lull's first seconds run together).
        n.Run(t.LullSeconds - 2);
        Assert.Equal(0, HaltSends(n));
        // After it, one...
        n.Run(10);
        Assert.Equal(1, HaltSends(n));
        Assert.True(n.Arrivals.Count > 0, "logged but nothing came");
        // ...and no more however long they stand there.
        n.Run(60);
        Assert.Equal(1, HaltSends(n));
    }

    [Fact]
    public void WithTheFlagOffAHaltIsAsItWas()
    {
        var n = AtAHalt(Halt(on: false));
        n.Run(60);
        Assert.Equal(0, HaltSends(n));
        Assert.Empty(n.Director.Log);
    }
}
