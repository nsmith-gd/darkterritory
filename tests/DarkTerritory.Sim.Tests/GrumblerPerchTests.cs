using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Stops;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The Grumbler on its perch (level-design H.2, ARCHITECTURE §8 note 848; the director's playtest of 16 Oct evening, line 7:
/// "Monsters are positioned and ready at yards"). A perch is laid under every yard gantry, but the Grumbler came only to a
/// facility's own crane: in 18 nights, 2 of 45 stops would ever have it (`dt afoot census`). Now any of the stop's cranes with
/// castings stacked will do, the one whose perch is nearest the crew.
/// </summary>
public class GrumblerPerchTests
{
    static readonly EnemyTuning E = Tuning.Enemies;
    static readonly PlayerTuning P = Tuning.Player;
    static readonly LootTuning L = DataFile.Load<LootTuning>(Path.Combine(DataFile.FindContentRoot(), LootTuning.File));

    /// <summary>A Frontier night's first stop whose cranes are all yard gantries (none of the facility's own).</summary>
    static (Route.Route Route, int Index, RouteFeature Yard) GantryOnly()
    {
        for (ulong seed = 1; seed < 40; seed++)
        {
            var route = RouteGenerator.Generate(Tuning.Route, RouteTier.Frontier, seed);
            var world = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 4, 1)), route.Build(), 400), Tuning.Combat);
            world.EnableRun(Tuning.Run, route, 600, authority: true, FacilityTests.F, L);
            for (int i = 0; i < world.Run!.Sites.Count; i++)
                if (world.Run.Sites[i] is { Crane: null, YardCranes.Count: > 0 } site && site.Feature.Stop!.Lairs.Any(l => l.Kind == LairKind.GrumblerPerch))
                    return (route, i, site.Feature);
        }
        throw new InvalidOperationException("no Frontier night in 40 with a stop craned only by yard gantries");
    }

    [Fact]
    public void ItComesToAYardGantrysCastingsTheNearestPerchToTheCrewAndRidesThemAboard()
    {
        var (route, index, yard) = GantryOnly();
        var n = new Night(4, speed: 0, route, front: yard.Start + 30);
        n.Controls = new TrainControls { Reverser = 1, Brake = 1 };
        n.World.EnableRun(Tuning.Run, route, 600, authority: true, FacilityTests.F, L);
        n.World.Run!.Resume(600, -1, 0, 0);
        var site = n.World.Run.Sites[index]!;
        // The crewmate walks up to the last gantry's first casting.
        var gantry = site.YardCranes[^1];
        n.Crew[1] = PlayerMotor.SpawnOnGround(gantry.Castings[0].At + new Double3(4, 0, 4), n.Train.Line, n.Train.Dynamics.Distance, P);
        n.Run(1);
        Assert.Equal(index, n.World.Run.StandsAt(n.Train));

        // There was no crane of the facility's own, so it never came before; now the gantries are its.
        var ctx = new SpawnContext(n.World, E, n.World.Director!);
        var rule = Spawns.For(EnemyKind.Grumbler)!;
        Assert.NotNull(rule.Weight(ctx));
        Assert.True(rule.Spawn(ctx).Placed);
        var g = Assert.Single(n.World.ActiveEnemies.OfType<Grumbler>());
        // On a casting of the gantry whose perch is nearest the crewmate.
        var perches = site.Feature.Stop!.Lairs.Where(l => l.Kind == LairKind.GrumblerPerch).Select(l => Run.Run.StopWorld(n.Train.Line, site.Feature, l.At)).ToList();
        var me = PlayerMotor.WorldPosition(n.Crew[1], n.Train);
        var nearest = site.CraneNear(perches.MinBy(p => ((p - me) with { Y = 0 }).Length));
        Assert.Same(gantry, nearest);
        Assert.InRange(g.Casting, 0, gantry.Castings.Length - 1);
        Assert.True(((g.Local - gantry.Castings[g.Casting].At) with { Y = 0 }).Length < 0.01);

        // Craned aboard on its casting: it's in that car, eating the cargo, whichever crane lifted it.
        int car = n.Train.Vehicles.First(v => v.Kind == VehicleKind.Cargo).Id;
        gantry.Castings[g.Casting].State = CastingState.Loaded;
        gantry.Castings[g.Casting].Car = car;
        gantry.Castings[g.Casting].At = new Double3(0, 1, 0);
        n.Run(0.2);
        Assert.Equal(car, g.Attached);
    }

    [Fact]
    public void NoCastingsStackedNoGrumbler()
    {
        var (route, index, yard) = GantryOnly();
        var n = new Night(4, speed: 0, route, front: yard.Start + 30);
        n.Controls = new TrainControls { Reverser = 1, Brake = 1 };
        n.World.EnableRun(Tuning.Run, route, 600, authority: true, FacilityTests.F, L);
        n.World.Run!.Resume(600, -1, 0, 0);
        foreach (var crane in n.World.Run.Sites[index]!.Cranes)
            for (int i = 0; i < crane.Castings.Length; i++)
                crane.Castings[i].State = CastingState.Loaded;
        n.Crew[1] = PlayerMotor.SpawnOnGround(n.Train.Frames[1].Origin + Double3.Cross(n.Train.Line.Sample(n.Train.Dynamics.Distance).Tangent, Double3.Up).Normalized * 8,
            n.Train.Line, n.Train.Dynamics.Distance, P);
        n.Run(1);
        Assert.Null(Spawns.For(EnemyKind.Grumbler)!.Weight(new SpawnContext(n.World, E, n.World.Director!)));
    }
}
