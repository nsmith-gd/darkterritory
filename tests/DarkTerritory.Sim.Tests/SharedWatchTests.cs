using Ballast;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 861 (the director's playtest of 16 Oct evening, line 8: "we didn't get any of the challenges to share together"): the
/// harness's count of problems two crewmates are in together. A hold is shared once a friend is within voice's reach of it;
/// held alone it's a grab, not a shared one. A watch only: it changes nothing.
/// </summary>
public class SharedWatchTests
{
    static readonly PlayerTuning P = Tuning.Player;
    static readonly RailLine Line = new(new LineDefinition("t", [new TrackSegment(50_000)]));
    static readonly SharedTuning T = new();

    static World Night() => new(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 8, 1)), Line, 2_000), Tuning.Combat);

    static PlayerState Held(in PlayerState s) => s with { Flags = s.Flags | PlayerFlags.Held };

    [Fact]
    public void TheHarnessCountsWhatBalanceJsonSays()
    {
        var t = DataFile.Load<BalanceTuning>(Path.Combine(DataFile.FindContentRoot(), BalanceTuning.File)).Shared;
        // Shared within proximity voice's reach: they hear it, and can come.
        Assert.Equal(VoiceRouting.ProximityCutoff, t.NearMetres);
        Assert.Equal(Tuning.Player.CrewNews.BeyondMetres, t.NearMetres);
        Assert.True(t.PairMetres > 0 && t.PairMetres < t.NearMetres);
    }

    [Fact]
    public void AHoldWithAFriendInReachIsSharedAndOneAloneIsnt()
    {
        var world = Night();
        var me = PlayerMotor.SpawnOnRoof(world.Train, 1, 0, P);
        var near = PlayerMotor.SpawnOnRoof(world.Train, 1, 3, P);
        var far = PlayerMotor.SpawnOnRoof(world.Train, 6, 0, P);
        var watch = new SharedWatch(T);
        // Held alone (the friend six cars off): a grab, not a shared one; however long it lasts, it's one.
        for (uint t = 0; t < 30; t++)
            watch.Watch(t, world, [(1, Held(me)), (2, far)]);
        var alone = watch.Report(world).Moments;
        Assert.Equal(1, alone["grab"]);
        Assert.Equal(1, alone["grabAlone"]);
        Assert.False(alone.ContainsKey("grabNear"));
        // Let go; held again with a friend beside: shared, once a hold.
        watch.Watch(30, world, [(1, me), (2, far)]);
        for (uint t = 31; t < 60; t++)
            watch.Watch(t, world, [(1, Held(me)), (2, near)]);
        var both = watch.Report(world);
        Assert.Equal(2, both.Moments["grab"]);
        Assert.Equal(1, both.Moments["grabNear"]);
        Assert.Equal(1, both.Moments["grabAlone"]);
        // No run: all of it on the run, no stops.
        Assert.Equal(2, both.OnTheRun["grab"]);
        Assert.Empty(both.Stops);
    }

    [Fact]
    public void AFriendWhoComesWithinReachDuringTheHoldSharesIt()
    {
        var world = Night();
        var me = PlayerMotor.SpawnOnRoof(world.Train, 1, 0, P);
        var watch = new SharedWatch(T);
        for (uint t = 0; t < 10; t++)
            watch.Watch(t, world, [(1, Held(me)), (2, PlayerMotor.SpawnOnRoof(world.Train, 6, 0, P))]);
        watch.Watch(10, world, [(1, Held(me)), (2, PlayerMotor.SpawnOnRoof(world.Train, 2, 0, P))]);
        var m = watch.Report(world).Moments;
        Assert.Equal(1, m["grab"]);
        Assert.Equal(1, m["grabNear"]);
        Assert.False(m.ContainsKey("grabAlone"));
    }

    [Fact]
    public void TheDeadDontShareAHold()
    {
        var world = Night();
        var me = PlayerMotor.SpawnOnRoof(world.Train, 1, 0, P);
        var dead = PlayerMotor.SpawnOnRoof(world.Train, 1, 3, P) with { Health = 0, Death = DeathCause.Mauled };
        var watch = new SharedWatch(T);
        watch.Watch(0, world, [(1, Held(me)), (2, dead)]);
        Assert.Equal(1, watch.Report(world).Moments["grabAlone"]);
    }
}
