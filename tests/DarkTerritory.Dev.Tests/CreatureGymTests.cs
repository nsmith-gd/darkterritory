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
        // rules that look further along the line (the Switchman's points, Tower Jaw's crane, the Grumbler's castings).
        EnemyKind[] kinds = [EnemyKind.CinderHound, EnemyKind.Ribbit, EnemyKind.Stoker, EnemyKind.Choir, EnemyKind.Lodger, EnemyKind.Pickers,
            EnemyKind.Mourners, EnemyKind.FreightBeetle, EnemyKind.FireFlies, EnemyKind.Switchman, EnemyKind.TowerJaw, EnemyKind.Grumbler, EnemyKind.Moose];
        var report = GymCheck.Run(Content, kinds, seconds: 20, shots: null);

        Assert.All(report.Kinds, k => Assert.True(k.Came, $"{k.Kind}: {k.Where}"));
        // The Choir gathers before it comes: by twenty seconds it's here.
        Assert.True(report.Kinds.Single(k => k.Kind == "choir").Count > 0);
        Assert.Empty(report.TooNear);
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
}
