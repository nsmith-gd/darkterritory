using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Dave, the wandering painter (GDD §3.2; the director, 8 Oct 2026; ARCHITECTURE §8 note 526). Some nights he's at his easel
/// out past a stop; nothing hurts him and the director never counts him; a crewmate's first four blows are his warnings
/// (the last of them turns him: his telegraph) and the fifth is their death, by the spine like every kill; each crewmate's
/// blows are their own.
/// </summary>
public class DaveTests
{
    static readonly EnemyTuning E = Tuning.Enemies;
    static readonly DaveTuning D = Tuning.Enemies.Dave;
    static readonly PlayerTuning P = Tuning.Player;
    static readonly string Content = DataFile.FindContentRoot();

    static Double3 Ground(Night n, double lateral, double along = 0)
    {
        var at = n.Train.Frames[2].ToWorld(new Double3(lateral, 0, along));
        double hint = n.Train.Dynamics.Distance;
        return at with { Y = PlayerMotor.GroundAt(at, n.Train.Line, ref hint) };
    }

    static PlayerState Stood(Night n, Double3 at, Double3 toward)
    {
        var s = PlayerMotor.SpawnOnGround(at, n.Train.Line, n.Train.Dynamics.Distance, P);
        var d = toward - at;
        s.Yaw = DMath.Atan2(-d.X, -d.Z);
        return s;
    }

    /// <summary>Dave 30 m off the train, and crewmate <paramref name="id"/> stood 1.2 m behind him (a quarter off), facing him.</summary>
    static Dave Painting(Night n, params int[] ids)
    {
        var at = Ground(n, 30);
        var dave = n.World.AddEnemy(id => Dave.At(id, at, n.Train.Dynamics.Distance, 0, D));
        for (int i = 0; i < ids.Length; i++)
        {
            double a = 0.6 + i * 0.9;
            n.Crew[ids[i]] = Stood(n, at + new Double3(Math.Sin(a) * 1.2, 0, Math.Cos(a) * 1.2), at);
        }
        return dave;
    }

    /// <summary>Crewmate <paramref name="id"/> swings at him until they've struck him <paramref name="blows"/> times (or 20 s).</summary>
    static void Strike(Night n, Dave dave, int id, int blows)
    {
        for (double t = 0; t < 20 && dave.BlowsBy(id) < blows; t += 0.1)
            n.Run(0.1, who => who == id ? new PlayerIntent { Actions = PlayerActions.Swing } : default);
        Assert.Equal(blows, dave.BlowsBy(id));
    }

    [Fact]
    public void FourBlowsAreHisWarningsAndTheFifthTakesYouByTheNeck()
    {
        var n = new Night(4, speed: 0);
        var dave = Painting(n, 1);
        // A friend far off, doing nothing (so it isn't a crew of one, where the held may struggle free).
        n.Crew[2] = Stood(n, Ground(n, -60), Ground(n, -70));
        Strike(n, dave, 1, D.Blows - 2);
        Assert.Equal(SpinePhase.Dormant, dave.Phase);
        // The last but one: he sets his brush down and turns to them.
        Strike(n, dave, 1, D.Blows - 1);
        Assert.Equal(SpinePhase.Telegraph, dave.Phase);
        Assert.Equal(1, dave.Striker);
        Assert.Equal(D.Blows - 1, dave.StrikerBlows);
        n.Run(3);
        Assert.True(n.Crew[1].Alive, "four blows are only warnings");
        Assert.Equal(SpinePhase.Telegraph, dave.Phase);
        // The fifth.
        Strike(n, dave, 1, D.Blows);
        n.Run(E.MinReactionSeconds + D.GrabSeconds + 0.5);
        Assert.Equal(DeathCause.Dave, n.Crew[1].Death);
        Assert.Contains(n.Events, e => e.Kind == EnemyKind.Dave && e.To == SpinePhase.Grab);
        // Then back to his painting: nothing hurts him, and he's never gone.
        n.Run(1);
        Assert.False(dave.Gone);
        Assert.Equal(SpinePhase.Dormant, dave.Phase);
        Assert.True(n.Crew[2].Alive);
        n.AssertFair();
    }

    [Fact]
    public void EachCrewmatesBlowsAreTheirOwn()
    {
        var n = new Night(4, speed: 0);
        var dave = Painting(n, 1, 2, 3);
        // Three of them strike him four times each, twelve blows between them: three warned, none taken.
        foreach (int id in (int[])[1, 2, 3])
            Strike(n, dave, id, D.Blows - 1);
        n.Run(3);
        Assert.All((int[])[1, 2, 3], id => Assert.True(n.Crew[id].Alive));
        Assert.Equal(SpinePhase.Telegraph, dave.Phase);
        Assert.Equal(P.Health, n.Crew[1].Health);
        n.AssertFair();
    }

    [Fact]
    public void TheDirectorNeverCountsHimAndNothingHuntsHim()
    {
        var dave = new Dave(1);
        Assert.True(dave.Hazard);
        Assert.False(dave.GunAnswers);
        Assert.False(dave.Exposed);
        Assert.True(dave.Far);
        Assert.Equal("Dave", dave.Called);
        Assert.Equal("Struck Dave once too often", Run.IncidentLog.What(DeathCause.Dave));
    }

    [Fact]
    public void SomeNightsHesAtHisEaselOutPastAStopTheSameEveryTime()
    {
        int found = 0, nights = 24;
        for (int seed = 1; seed <= nights; seed++)
        {
            var route = Routes.Generate(Content, $"frontier:{seed}", 6);
            var n = new Night(6, speed: 0, route: route);
            var a = Dave.Site(n.World, route, D);
            var b = Dave.Site(n.World, route, D);
            Assert.Equal(a, b);
            if (a is not { } site)
                continue;
            found++;
            Assert.True(Moose.TrackOff(n.Train, site.At, site.Along) >= D.TrackClearance - 1e-6, $"seed {seed}: {Moose.TrackOff(n.Train, site.At, site.Along):0.0} m off a track");
            Assert.True(site.Along > route.Gate + D.PastGate, $"seed {seed}: before the gate's {D.PastGate} m");
        }
        // About D.Chance of nights, give or take a few (a night with no ground that'll do has none).
        Assert.InRange(found, 1, nights * Math.Min(1, D.Chance * 2.2));
    }
}
