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
        // rules that look further along the line (the Switchman's points, Tower Jaw's crane, the Grumbler's castings), within 200 m;
        // and what the director couldn't try at the standing train (App. F.5: Tippy Toesie, the Dragger, the Gannet).
        EnemyKind[] kinds = [EnemyKind.CinderHound, EnemyKind.Ribbit, EnemyKind.Stoker, EnemyKind.Choir, EnemyKind.Lodger, EnemyKind.Pickers,
            EnemyKind.Mourners, EnemyKind.FreightBeetle, EnemyKind.FireFlies, EnemyKind.Switchman, EnemyKind.TowerJaw, EnemyKind.Grumbler, EnemyKind.Moose,
            EnemyKind.TippyToesie, EnemyKind.Dragger, EnemyKind.Gannet];
        var report = GymCheck.Run(Content, kinds, seconds: 20, shots: null);

        // The yard's crane and castings are a walk from the engine, past the 200 m from the ring: they say so, to be called nearer.
        string[] walk = ["towerJaw", "grumbler"];
        Assert.All(report.Kinds.Where(k => !walk.Contains(k.Kind)), k => Assert.True(k.Came, $"{k.Kind}: {k.Where}"));
        Assert.All(report.Kinds.Where(k => walk.Contains(k.Kind)), k => Assert.True(k.Came || k.Where.Contains("call again", StringComparison.Ordinal), $"{k.Kind}: {k.Where}"));
        // The engine stands short of the stop's first points (App. F.5: "Switchman cannot be tested that far away").
        var switchman = report.Kinds.Single(k => k.Kind == "switchman");
        Assert.InRange(switchman.NearestAtCall ?? 0, 45, 200);
        // The hounds come up behind the standing train and board it; Tippy Toesie comes for whoever stands still; the Dragger
        // waits under a named car's edge; the Gannet stays over a standing train in the gym.
        var hounds = report.Kinds.Single(k => k.Kind == "cinderHound");
        Assert.True(hounds.AboardAfter, $"hounds: {hounds.Phase}, {hounds.NearestAfter} m");
        var tippy = report.Kinds.Single(k => k.Kind == "tippyToesie");
        Assert.Null(tippy.NearestAtCall);
        Assert.True(tippy.NearestAfter < 5, $"Tippy Toesie {tippy.NearestAfter} m off after 20 s standing still");
        Assert.Matches("under car [0-9]+'s (left|right) roof edge", report.Kinds.Single(k => k.Kind == "dragger").Where);
        Assert.Equal(1, report.Kinds.Single(k => k.Kind == "gannet").About);
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
    public void TheGannetStaysOverAStandingTrainInTheGym()
    {
        // App. F.5 (the director, 13 Oct: "gannet doesn't dive consistently or loop its attacks"): on a real night a train
        // under 43 km/h sends it away within seconds; the gym's train stands, so there it stays until it's cleared.
        var gym = CreatureGym.Load(Content);
        using var night = NetPlaySession.HostGame(Content, gym.Setup(), port: null, bots: 0, listed: false);
        var host = night.Host!;
        gym.Begin(host);
        var called = gym.Call(host, (byte)night.PlayerId, EnemyKind.Gannet);
        Assert.True(called.Came, called.Where);
        for (int t = 0; t < 40 * SimConstants.TickRate; t++)
        {
            night.Step(default);
            gym.Step(host, host.Tick * SimConstants.TickSeconds);
        }
        var gannet = Assert.IsType<Gannet>(host.World.ActiveEnemies.Single(e => e.Id == called.Ids[0]));
        Assert.NotEqual(GannetMode.Away, gannet.Mode);
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

