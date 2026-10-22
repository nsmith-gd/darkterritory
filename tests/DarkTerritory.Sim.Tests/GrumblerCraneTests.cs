using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 991 (GDD §21: the Grumbler "scuttles like a spider over the crane, gnawing food crates"): between bouts of gnawing
/// it goes up the gantry's nearest leg and along a rail girder, and back down onto its casting; rigged or hooked meanwhile,
/// it hurries back to it; struck up there, it drops.
/// </summary>
public class GrumblerCraneTests
{
    static readonly EnemyTuning E = Tuning.Enemies with
    {
        Director = Tuning.Enemies.Director with { GraceMinSeconds = 1e9, GraceMaxSeconds = 1e9 },
    };

    static (FacilityTests.Stop Stop, Crane Crane, Grumbler Grumbler) OnTheStack()
    {
        var stop = new FacilityTests.Stop(ModuleKind.Crane);
        stop.World.EnableEnemies(E, stop.World.Route, 1, crew: 1, authority: true);
        var crane = stop.Site.Crane!;
        var g = stop.World.AddEnemy(id => Grumbler.OnCrates(id, crane.Castings[0].At + Double3.Up * 0.8, 0, E.Grumbler, crane));
        return (stop, crane, g);
    }

    [Fact]
    public void BetweenBoutsOfGnawingItGoesOverTheCraneAndBackOntoItsCasting()
    {
        Assert.True(E.Grumbler.RoamEvery > 0);
        var (stop, crane, g) = OnTheStack();
        var home = crane.Castings[0].At + Double3.Up * 0.8;
        stop.Step(E.Grumbler.RoamEvery - 1, []);
        Assert.False(g.Roaming);
        Assert.Equal(home.Y, g.Local.Y, 3);
        // Off it, up a leg and along the rail girder's flange: as high as the girder's top.
        double highest = 0;
        bool went = false;
        for (int i = 0; i < 120 && (!went || g.Roaming); i++)
        {
            stop.Step(0.5, []);
            went |= g.Roaming;
            highest = Math.Max(highest, g.Local.Y - crane.Corner(0, 0).Y);
        }
        Assert.True(went);
        Assert.Equal(crane.Top + Crane.RailFlange, highest, 2);
        // And back on its casting, gnawing, the casting where it was.
        Assert.False(g.Roaming);
        Assert.Equal(SpinePhase.Telegraph, g.Phase);
        Assert.True((g.Local - home).Length < 0.01, $"{g.Local} vs {home}");
    }

    [Fact]
    public void ItsCastingHookedWhileItsUpThereItComesBackDownToItWhereverItsGone()
    {
        var (stop, crane, g) = OnTheStack();
        stop.Step(E.Grumbler.RoamEvery + 5, []);
        Assert.True(g.Roaming);
        crane.Castings[0].State = CastingState.Hooked;
        crane.Hook = 3;
        stop.Step(20, []);
        Assert.False(g.Roaming);
        Assert.True((g.Local - (crane.HookedBase + Double3.Up * 0.8)).Length < 0.01);
    }

    [Fact]
    public void StruckUpThereItDropsToTheGroundAndHunts()
    {
        var (stop, crane, g) = OnTheStack();
        stop.Step(E.Grumbler.RoamEvery + 8, []);
        Assert.True(g.Roaming);
        Assert.True(g.Local.Y - crane.Corner(0, 0).Y > 2);
        g.Struck(new EnemyContext { Tuning = E, World = stop.World }, 0, 1);
        Assert.False(g.Roaming);
        Assert.Equal(crane.Corner(0, 0).Y, g.Local.Y, 3);
        Assert.True(g.Feral);
    }
}
