using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Something at the stops, alone too (GDD §9; the director, 15 Oct 2026: "Even when testing alone I'm not running into monsters
/// in yards or abandoned villages"; ARCHITECTURE §8 note 795). A crew of one meets one thing at a time, and that place was
/// held all night by what nobody was near or what only lay in wait; a crew that stood on the main line beside a yard wasn't at
/// its stop at all; and the yard's creatures wanted a crew of two.
/// </summary>
public class StopsAloneTests
{
    static readonly EnemyTuning E = Tuning.Enemies;
    static readonly PlayerTuning P = Tuning.Player;
    static readonly Route.Route Frontier = RouteGenerator.Generate(Tuning.Route, RouteTier.Frontier, 7);

    [Theory]
    [InlineData(EnemyKind.Lodger)]
    [InlineData(EnemyKind.Householder)]
    [InlineData(EnemyKind.Hanger)]
    [InlineData(EnemyKind.HollowHouse)]
    public void WhatLiesInWaitInAHouseHoldsNoCap(EnemyKind kind)
    {
        // Asleep in its house it's after nobody: a Hanger in a roof held a crew of one's single place for a whole village walk.
        var asleep = Enemy.Blank(kind, 1);
        Assert.Equal(SpinePhase.Dormant, asleep.Phase);
        Assert.False(Director.Engaged(asleep));
    }

    [Fact]
    public void OffTheTrainWhatNobodyIsNearHoldsNoCapAndAboardItDoes()
    {
        var n = new Night(4, speed: 0);
        // A hound pack aboard the rear car, at its work.
        var hound = n.World.AddEnemy(id => new CinderHound(id, 0));
        hound.Restore(SpinePhase.Commit, 5, 2, 4, new Double3(0, 2.4, 0), n.Train.Dynamics.Distance, 0, 0.6, 0, 0);
        Assert.True(Director.Engaged(hound));
        var d = n.World.Director!;

        // Its crew of one walked off 100 m out into a village: it holds no place, and what's there can come.
        var side = Double3.Cross(n.Train.Line.Sample(n.Train.Dynamics.Distance).Tangent, Double3.Up).Normalized;
        n.Crew[1] = PlayerMotor.SpawnOnGround(n.Train.Frames[0].Origin + side * 100, n.Train.Line, n.Train.Dynamics.Distance, P);
        n.Run(1.0 / SimConstants.TickRate);
        Assert.DoesNotContain(hound, d.Holding(n.World, n.World.ActiveEnemies));

        // Back aboard, in the cab, it counts as ever (App. B.1: one thing at a time on the train).
        n.Crew[1] = PlayerMotor.SpawnInCab(n.Train, P);
        n.Run(1.0 / SimConstants.TickRate);
        Assert.Contains(hound, d.Holding(n.World, n.World.ActiveEnemies));

        // Near it, off the train, it counts too.
        n.Crew[1] = PlayerMotor.SpawnOnGround(n.Train.Frames[4].Origin + side * 6, n.Train.Line, n.Train.Dynamics.Distance, P);
        n.Run(1.0 / SimConstants.TickRate);
        Assert.Contains(hound, d.Holding(n.World, n.World.ActiveEnemies));
    }

    [Fact]
    public void StandingOnTheMainBesideAYardIsBeingAtItsStopAndItsCreaturesComeToACrewOfOne()
    {
        // A yard with a spur: its loading waits for the spur, but what lives there doesn't.
        var run0 = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 4, 1)), Frontier.Build(), 400), Tuning.Combat);
        run0.EnableRun(Tuning.Run, Frontier, 600, authority: true);
        var facilities = run0.Run!.Facilities.ToList();
        int i = facilities.FindIndex(f => f.Facility is not FacilityKind.CoalingTower && Frontier.Branches.Any(b => b.Kind == Rail.BranchKind.Spur && f.Contains(b.Toe)));
        Assert.True(i >= 0);
        var yard = facilities[i];

        var n = new Night(4, speed: 0, Frontier, front: yard.Start + 30);
        n.Controls = new TrainControls { Reverser = 1, Brake = 1 };
        n.World.EnableRun(Tuning.Run, Frontier, 600, authority: true);
        n.World.Run!.Resume(600, -1, 0, 0);
        n.Crew[1] = PlayerMotor.SpawnOnGround(n.Train.Frames[1].Origin + Double3.Cross(n.Train.Line.Sample(n.Train.Dynamics.Distance).Tangent, Double3.Up).Normalized * 8,
            n.Train.Line, n.Train.Dynamics.Distance, P);
        n.Run(1);

        Assert.True(n.Train.OnMain);
        Assert.NotEqual(RunPhase.AtFacility, n.World.Run.Phase);
        Assert.Equal(i, n.World.Run.StandsAt(n.Train));
        var ctx = new SpawnContext(n.World, E, n.World.Director!) { CrewAs = 1 };
        Assert.True(ctx.AtStop);
        Assert.False(ctx.AtFacility);
        // A crew of one on the ground there: a lone Ribbit can come (it wanted two).
        Assert.NotNull(Spawns.For(EnemyKind.Ribbit)!.Weight(ctx));

        // Away from the stop, it isn't.
        var far = new Night(4, speed: 0, Frontier, front: yard.End + 2_000);
        far.World.EnableRun(Tuning.Run, Frontier, 600, authority: true);
        far.World.Run!.Resume(600, -1, 0, 0);
        far.Run(0.2);
        Assert.Equal(-1, far.World.Run.StandsAt(far.Train));
    }
}
