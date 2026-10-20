using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The protections made one (queue #629, note 912), closing what #602's guard tests found (note 887): the aftermath holds the
/// director's own specials as it holds its decisions, the terminus ban is one check for every spawn (the truss drops and the
/// stops' houses included), and the final approach is route.json's.
/// </summary>
public class ProtectionGapTests
{
    static readonly EnemyTuning E = Tuning.Enemies;
    static readonly PlayerTuning P = Tuning.Player;

    /// <summary>A crew of four in the cab of a fast train on <paramref name="enemies"/>, the director quiet but for the hound run.</summary>
    static Night Fast(EnemyTuning enemies)
    {
        var n = new Night(4, 21, enemies: enemies);
        for (int i = 0; i < 4; i++)
            n.Crew[i] = PlayerMotor.SpawnInCab(n.Train, P);
        return n;
    }

    /// <summary>The second the night's first hound run starts, run a second at a time.</summary>
    static int FirstRunAt(EnemyTuning enemies, int crisisAt = -1, int within = 300)
    {
        var n = Fast(enemies);
        var d = n.World.Director!;
        for (int s = 0; s < within; s++)
        {
            if (s == crisisAt)
                d.Crisis("death");
            n.Run(1);
            if (d.HoundRuns.Count > 0)
                return s;
        }
        return -1;
    }

    [Fact]
    public void TheHoundRunWaitsOutTheAftermath()
    {
        Assert.True(E.Director.Aftermath.Specials);
        int at = FirstRunAt(HoundRunTests.Quiet);
        Assert.True(at > 5, $"the first run at {at} s");
        // The same night with a crisis five seconds before it: the run waits the aftermath out (its count banked), then comes.
        int crisis = at - 5;
        int held = FirstRunAt(HoundRunTests.Quiet, crisis);
        Assert.True(held >= crisis + E.Director.Aftermath.Seconds - 1, $"the run at {held} s, the aftermath from {crisis} s");
        Assert.True(held <= crisis + E.Director.Aftermath.Seconds + 2, $"the run at {held} s, banked count lost?");
        // With the specials let through, it comes when it would have.
        var through = HoundRunTests.Quiet with
        {
            Director = HoundRunTests.Quiet.Director with { Aftermath = HoundRunTests.Quiet.Director.Aftermath with { Specials = false } },
        };
        Assert.Equal(at, FirstRunAt(through, crisis));
    }

    [Fact]
    public void TheFinalApproachIsTheRoutesAsRouteJsonSetsIt()
    {
        var t = RouteTuning.Load(DataFile.FindContentRoot());
        var route = RouteGenerator.Generate(t with { NoSpawnFinalApproach = 800 }, RouteTier.Frontier, 3);
        Assert.Equal(800, route.NoSpawnFinalApproach);
        var n = new Night(4, 0, route: route, enemies: E);
        Assert.Equal(800, n.World.NoSpawnFinalApproach);
        // The home stretch is the one check: 790 m from the end is in it, 810 m isn't (no terminus_safe tag on a hand-laid line).
        Assert.True(n.World.InHomeStretch(route.RunLength - 790));
        Assert.False(n.World.InHomeStretch(route.RunLength - 810));
        // A world with no route keeps App. B.1's 500 m.
        Assert.Equal(500, new Night(4, 0, enemies: E).World.NoSpawnFinalApproach);
    }

    /// <summary>
    /// A stop in the home stretch keeps its houses empty (dwellings.inHomeStretch false, note 912): frontier:7's last yard
    /// (6130–6830 m of a run of 8630) put in the final approach by widening it to 2600 m, the train come up to the yard.
    /// </summary>
    [Fact]
    public void AStopOnTheRunInToTheTerminusKeepsItsHousesEmpty()
    {
        Assert.False(E.Dwellings.InHomeStretch);
        var route = DirectorGuardTests.Frontier7.Value;
        var yard = route.Features.Last(f => f.Kind == FeatureKind.Facility && f.Stop is not null);
        Assert.True(yard.Start < route.RunLength - 1_500, $"the last yard at {yard.Start}");
        var near = route with { NoSpawnFinalApproach = route.RunLength - yard.Start + 100 };
        int Housed(Route.Route r, EnemyTuning e)
        {
            var g = DirectorGuardTests.Moving(DirectorGuardTests.Pressed() with { Dwellings = e.Dwellings }, yard.Start - 100, 0, r);
            g.Run(3);
            return g.Arrivals.Count(a => a.Kind is EnemyKind.Hanger or EnemyKind.HollowHouse or EnemyKind.Householder or EnemyKind.Lodger);
        }
        // Where the yard's out of the home stretch, its houses have their own; in it, none.
        int housed = Housed(route, E);
        Assert.True(housed > 0, "frontier:7's last yard has nobody in its houses");
        Assert.Equal(0, Housed(near, E));
        // Let have their own there, they come.
        Assert.Equal(housed, Housed(near, E with { Dwellings = E.Dwellings with { InHomeStretch = true } }));
    }
}
