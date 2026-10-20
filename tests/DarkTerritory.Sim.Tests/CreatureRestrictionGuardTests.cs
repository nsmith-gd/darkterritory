using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The spawn director overhaul's guard tests (queue #602, note 887; the brief's §8 "Creature-specific spawn restrictions remain
/// valid", §1 D "Preserve monster identity"): each creature's structural rules, the ones its identity rests on (the train's
/// speed, its cars and gaps, someone on the ground, a facility or a stop, the crew it needs, its tier, once a night), held
/// against a director pressed as hard as it goes with that creature alone on its roster, so that none of its channels (the
/// spend, the free spawns that set its situational rule aside, the stops' own, the hound runs) can send it where its rule
/// says it can't come. The overhaul may loosen a creature's situational gates (#612, from #605's report); these stay.
/// </summary>
public class CreatureRestrictionGuardTests
{
    static readonly EnemyTuning E = Tuning.Enemies;
    const double Seconds = 45;

    /// <summary>The case's night: its creature alone on a pressed director's roster, the night set up so its rule says no.</summary>
    sealed record Case(EnemyKind Kind, Func<EnemyTuning, GuardNight> Night, Route.Route? Route = null);

    static Route.Route Plain(RouteTier tier) =>
        new("guard", tier, 1, new LineDefinition("guard", [new TrackSegment(40_000)]), [], new RouteWeather(0.01, false, 0, 0), 3600);

    /// <summary>Stood on the open line, the run under way, one in the cab and the rest afoot beside the train.</summary>
    static GuardNight Stood(EnemyTuning e, int crew = 4, int cars = 6, bool afoot = true, RouteTier tier = RouteTier.Frontier)
    {
        var n = new GuardNight(e, Plain(tier), front: 3_000, speed: 0, cars: cars, run: true, crew: crew);
        n.Controls = new TrainControls { Reverser = 1, Brake = 1 };
        n.Underway(900);
        if (afoot)
        {
            n.Aboard(1);
            for (int i = 2; i <= crew; i++)
                n.Crew[i] = n.Ground(3_000 - 15 * i, lateral: 4);
        }
        else
            n.Aboard(crew);
        return n;
    }

    /// <summary>Under way at <paramref name="speed"/>, the crew aboard (the cab and the roofs).</summary>
    static GuardNight Moving(EnemyTuning e, double speed, int crew = 4, int cars = 6)
    {
        var n = new GuardNight(e, Plain(RouteTier.Frontier), front: 3_000, speed: speed, cars: cars, run: true, crew: crew);
        n.Underway(900);
        n.Aboard(crew);
        return n;
    }

    static readonly Dictionary<string, Case> Cases = new()
    {
        // The train's speed: what boards or runs a moving train never comes to a standing one...
        ["hounds: a standing train"] = new(EnemyKind.CinderHound, e => Stood(e)),
        ["hotbox: a standing train"] = new(EnemyKind.Hotbox, e => Stood(e)),
        ["knotter: a standing train"] = new(EnemyKind.Knotter, e => Stood(e)),
        ["gannet: a standing train"] = new(EnemyKind.Gannet, e => Stood(e)),
        ["brakeman: a standing train"] = new(EnemyKind.Brakeman, e => Stood(e)),
        // ...and what comes to a standing one never to one on the move.
        ["whistler: a moving train"] = new(EnemyKind.Whistler, e => Moving(e, 14)),
        ["moose: a moving train"] = new(EnemyKind.Moose, e => Moving(e, 14)),
        ["tippy toesie: a moving train"] = new(EnemyKind.TippyToesie, e => Moving(e, 14)),
        ["freight beetle: a moving train"] = new(EnemyKind.FreightBeetle, e => Moving(e, 14)),
        ["mouse moose: a moving train"] = new(EnemyKind.MouseMoose, e => Moving(e, 14)),
        ["draggers: a train run faster than they board"] = new(EnemyKind.Dragger, e => Moving(e, E.Draggers.BoardBelow + 4)),
        // The cars and the gaps between them.
        ["car hugger: too few cars"] = new(EnemyKind.CarHugger, e => Moving(e, 10, cars: E.CarHugger.MinCars - 1)),
        ["draggers: too few cars"] = new(EnemyKind.Dragger, e => Moving(e, 8, cars: E.Draggers.MinCars - 1)),
        ["whistler: too few cars"] = new(EnemyKind.Whistler, e => Stood(e, cars: E.Whistler.MinCars - 1)),
        ["brakeman: too few cars"] = new(EnemyKind.Brakeman, e => Moving(e, 10, cars: E.Brakeman.MinCars - 1)),
        ["knotter: too few cars"] = new(EnemyKind.Knotter, e => Moving(e, 14, cars: E.Knotter.MinCars - 1)),
        ["gannet: too few cars"] = new(EnemyKind.Gannet, e => Moving(e, E.Gannet.ArriveAbove + 2, cars: E.Gannet.MinCars - 1)),
        ["climbers: too few gaps"] = new(EnemyKind.Climber, e => Moving(e, 14, cars: E.Climbers.MinGaps - 1)),
        // Someone on the ground: what comes at a crew afoot doesn't come to one all aboard.
        ["ribbits: nobody on the ground"] = new(EnemyKind.Ribbit, e => Stood(e, afoot: false)),
        ["soot children: nobody on the ground"] = new(EnemyKind.SootChildren, e => Stood(e, afoot: false)),
        ["gaunt: nobody on the ground"] = new(EnemyKind.Gaunt, e => Stood(e, afoot: false)),
        // A facility or a stop: what lives at one doesn't come out on the open line.
        ["mouse moose: no facility"] = new(EnemyKind.MouseMoose, e => Stood(e)),
        ["freight beetle: no stop"] = new(EnemyKind.FreightBeetle, e => Stood(e)),
        ["passenger: no stop"] = new(EnemyKind.Passenger, e => Stood(e, tier: RouteTier.DeepTerritory)),
        ["grumbler: no stop"] = new(EnemyKind.Grumbler, e => Stood(e)),
        // The crew it needs.
        ["tippy toesie: too small a crew"] = new(EnemyKind.TippyToesie, e => Stood(e, crew: E.TippyToesie.MinCrew - 1)),
        ["brakeman: too small a crew"] = new(EnemyKind.Brakeman, e => Moving(e, 10, crew: E.Brakeman.MinCrew - 1)),
        ["passenger: too small a crew"] = new(EnemyKind.Passenger, e => Stood(e, crew: E.Passenger.MinCrew - 1, tier: RouteTier.DeepTerritory)),
        // Its tier.
        ["gaunt: below the frontier"] = new(EnemyKind.Gaunt, e => Stood(e, tier: RouteTier.Local)),
        ["passenger: above the dead lines"] = new(EnemyKind.Passenger, e => Stood(e, tier: RouteTier.Frontier)),
    };

    public static TheoryData<string> Names => [.. Cases.Keys];

    [Theory]
    [MemberData(nameof(Names))]
    public void APressedDirectorNeverSendsACreatureWhereItsRuleSaysItCantCome(string name)
    {
        var c = Cases[name];
        var n = c.Night(DirectorGuardTests.Pressed([Director.Key(c.Kind)]));
        Assert.True(n.Crew.Count >= 1, $"{name}: no crew");
        n.Run(Seconds);
        Assert.DoesNotContain(n.Director.Log, l => l.Kind == c.Kind);
        Assert.DoesNotContain(n.Arrivals, a => a.Kind == c.Kind);
        if (c.Kind == EnemyKind.CinderHound)
            Assert.Empty(n.Director.HoundRuns);
    }

    [Fact]
    public void TheCasesBreakTheRuleTheyNameAndNothingElseIsWrongWithTheirNights()
    {
        // The crews the cases cut short are real crews (one or more), and the trains the cases cut short real trains.
        Assert.All(new[] { E.TippyToesie.MinCrew, E.Brakeman.MinCrew, E.Passenger.MinCrew }, m => Assert.True(m >= 2));
        Assert.All(new[] { E.CarHugger.MinCars, E.Draggers.MinCars, E.Whistler.MinCars, E.Brakeman.MinCars, E.Knotter.MinCars, E.Gannet.MinCars, E.Climbers.MinGaps },
            m => Assert.True(m >= 2));
        Assert.True(E.Draggers.BoardBelow < 40);
        // The contrasts: the same pressed director, the creature's rule met, sends it (so the cases aren't held by something else).
        var hounds = Moving(DirectorGuardTests.Pressed([Director.Key(EnemyKind.CinderHound)]), E.CinderHounds.MinTrainSpeed + 4);
        hounds.Run(Seconds);
        Assert.True(hounds.Arrivals.Any(a => a.Kind == EnemyKind.CinderHound), $"no hounds: {hounds.Director.HeldBecause}");
        var ribbits = Stood(DirectorGuardTests.Pressed([Director.Key(EnemyKind.Ribbit)]));
        ribbits.Run(Seconds);
        Assert.True(ribbits.Arrivals.Any(a => a.Kind == EnemyKind.Ribbit), $"no ribbits: {ribbits.Director.HeldBecause}");
        var draggers = Moving(DirectorGuardTests.Pressed([Director.Key(EnemyKind.Dragger)]), 8);
        draggers.Run(Seconds);
        Assert.True(draggers.Arrivals.Any(a => a.Kind == EnemyKind.Dragger), $"no draggers: {draggers.Director.HeldBecause}");
    }

    [Theory]
    [InlineData(EnemyKind.TrackDoll, 14.0)]
    [InlineData(EnemyKind.Gannet, 22.0)]
    public void WhatComesOnceANightComesOnceUnlessDrivenOff(EnemyKind kind, double speed)
    {
        var n = Moving(DirectorGuardTests.Pressed([Director.Key(kind)]), speed);
        n.Run(240);
        int came = n.Arrivals.Count(a => a.Kind == kind);
        Assert.True(came <= 1 || n.World.DrivenOff.Contains(kind), $"{kind} came {came} times");
        Assert.True(n.Director.Log.Count(l => l.Kind == kind) <= 1 || n.World.DrivenOff.Contains(kind));
    }
}
