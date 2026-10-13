using Ballast;
using DarkTerritory.Dev.Gym;
using DarkTerritory.Game;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Enemies;

namespace DarkTerritory.Dev.Tests;

/// <summary>
/// The creature gym (GDD §34.1, ARCHITECTURE §8 note 676): what's called comes, off you; a death stands you up again in the
/// safe ring; the ring keeps creatures out; and nothing comes by itself, the dawn included.
/// </summary>
public class CreatureGymTests
{
    static readonly string Content = DataFile.FindContentRoot();

    [Fact]
    public void EveryKindCalledComesOffYouAndADeathStandsYouUpInTheRing()
    {
        // One of each way in: the director's rules (hounds), a rule from the stop's own places (the Ribbits' warren), the
        // condition kinds (the Stoker, the Choir), a house, the props the gym supplies (crates, a body, a lit car), and the
        // rules that look further along the line (the Switchman's points, Tower Jaw's crane, the Grumbler's castings), within 200 m.
        EnemyKind[] kinds = [EnemyKind.CinderHound, EnemyKind.Ribbit, EnemyKind.Stoker, EnemyKind.Choir, EnemyKind.Lodger, EnemyKind.Pickers,
            EnemyKind.Mourners, EnemyKind.FreightBeetle, EnemyKind.FireFlies, EnemyKind.Switchman, EnemyKind.TowerJaw, EnemyKind.Grumbler, EnemyKind.Moose];
        var report = GymCheck.Run(Content, kinds, seconds: 20, shots: null);

        Assert.All(report.Kinds.Where(k => k.Kind != "switchman"), k => Assert.True(k.Came, $"{k.Kind}: {k.Where}"));
        // The gym's points ahead are past its 200 m: the Switchman doesn't come, and says how far they are.
        var switchman = report.Kinds.Single(k => k.Kind == "switchman");
        Assert.False(switchman.Came);
        Assert.Contains("past the gym's 200 m", switchman.Where);
        // The Choir gathers before it comes: by twenty seconds it's here.
        Assert.True(report.Kinds.Single(k => k.Kind == "choir").Count > 0);
        Assert.Empty(report.TooNear);
        // And nothing farther than 200 m (the director, 13 Oct: "nothing should spawn more than 200m off").
        Assert.Empty(report.TooFar);
        Assert.Equal(GymTuning.Load(Content).Lamps.Count, report.Lamps);
        Assert.True(report.Respawn.Alive);
        Assert.True(report.Respawn.Seconds <= GymTuning.Load(Content).RespawnSeconds + 0.5);
        Assert.True(report.Respawn.FromRing < 1, $"stood up {report.Respawn.FromRing} m from the ring's middle");
        Assert.False(report.Respawn.RunOver);
        Assert.True(report.Ring.SentOff);
    }

    [Fact]
    public void NothingComesByItselfAndTheNightNeverEnds()
    {
        var gym = CreatureGym.Load(Content);
        using var night = NetPlaySession.HostGame(Content, gym.Setup(), port: null, bots: 0, listed: false);
        var host = night.Host!;
        gym.Begin(host);
        // (The night's first ticks, hosting it, were before the gym had it.)
        double clock = host.World.Run!.Seconds;
        // Three minutes standing at the stop with the fire in: the director's grace long gone, the Stoker's heat clock run.
        for (int t = 0; t < 180 * SimConstants.TickRate; t++)
        {
            night.Step(default);
            gym.Step(host, host.Tick * SimConstants.TickSeconds);
        }
        Assert.True(host.World.CallOnly);
        Assert.DoesNotContain(host.World.ActiveEnemies, e => !e.Gone);
        Assert.False(host.World.Run!.Over);
        Assert.Equal(clock, host.World.Run.Seconds);
        Assert.All(host.Players, p => Assert.True(p.State.Alive && p.State.Cold == 0));
    }

    /// <summary>
    /// The pointer (note 676, part 2): what's in view is marked where it is; what isn't has an arrow at the edge the way to
    /// turn to it (behind and to the left: the left edge; straight behind: the bottom).
    /// </summary>
    [Theory]
    [InlineData(0, 0, -20, true, 0, 0)]      // straight ahead: the middle
    [InlineData(200, 0, -20, false, 1, 0)]   // far off to the right, ahead: the right edge
    [InlineData(-30, 0, 20, false, -1, 0)]   // behind, to the left: the left edge
    [InlineData(30, 0, 20, false, 1, 0)]     // behind, to the right: the right edge
    [InlineData(0, 0, 20, false, 0, 1)]      // straight behind: the bottom
    public void ThePointerMarksWhatsInViewAndPointsTheWayToWhatIsnt(double x, double y, double z, bool onScreen, int sideX, int sideY)
    {
        var camera = new Ballast.Render.Camera { Position = new Double3(0, 1.6, 0), FovYDegrees = 60, Near = 0.1f, Far = 2000 };
        const float W = 480, H = 270;
        var p = CreatureGym.Point(camera, new Double3(x, 1.6 + y, z), W, H, 14);
        Assert.Equal(onScreen, p.OnScreen);
        Assert.InRange(p.X, 13.9f, W - 13.9f);
        Assert.InRange(p.Y, 13.9f, H - 13.9f);
        if (onScreen)
        {
            Assert.Equal(W / 2, p.X, 1f);
            Assert.Equal(H / 2, p.Y, 1f);
            return;
        }
        if (sideX != 0)
            Assert.Equal(sideX > 0 ? W - 14 : 14, p.X, 1f);
        if (sideY != 0)
            Assert.Equal(sideY > 0 ? H - 14 : 14, p.Y, 1f);
    }
}

