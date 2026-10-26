using Ballast;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The Head Waiter (GDD §21; ARCHITECTURE §8 notes 1004, 1008; docs/design/creatures/head-waiter.md; the director, 23 Oct 2026,
/// line 36: "if you spend too much time up there, something should come and get you"). Rule: when the glass rings, back to
/// work. It never comes for someone working, only for a long lounge on the cosmetics; the glass rings before anything
/// happens; up and off to work, it bows and goes; still sat, the table's cleared, and you with it. Off unless headWaiter.on.
/// </summary>
public class HeadWaiterTests
{
    static readonly HeadWaiterTuning H = Tuning.Enemies.HeadWaiter with { On = true };
    static readonly EnemyTuning E = Tuning.Enemies with { HeadWaiter = H };
    static readonly PlayerTuning P = Tuning.Player;
    static readonly CampaignTuning C = DataFile.Load<CampaignTuning>(Path.Combine(DataFile.FindContentRoot(), CampaignTuning.File));
    static readonly TrainTuning Hammocked = Campaign.Campaign.Apply(C, ["hammock"], new Loadout(Tuning.Train, Tuning.Boiler, Tuning.Combat, Tuning.Enemies)).Train;

    static Night Train(double speed = 20) => new(4, speed, enemies: E, trainTuning: Hammocked);

    /// <summary>In the last guard van's hammock (note 744), lounging.</summary>
    static PlayerState InTheHammock(Night n)
    {
        int van = n.Train.Frames.Count - 1;
        var hammock = n.Train.Frames[van].Shape.Lounges.Single(l => l.Kind == LoungeKind.Hammock);
        return PlayerMotor.SpawnInCab(n.Train, P) with { Parent = van, Position = hammock.At, Yaw = hammock.Yaw, Flags = PlayerFlags.Lounging };
    }

    static HeadWaiter Comes(Night n, int diner = 1) => n.World.AddEnemy(id => HeadWaiter.For(id, n.Train, diner, n.Crew[diner], H));

    static readonly PlayerIntent GetUp = new() { Actions = PlayerActions.Seat };
    static readonly PlayerIntent Walk = new() { MoveZ = 1 };

    [Fact]
    public void ItsOffUntilTheDirectorsSaid() => Assert.False(Tuning.Enemies.HeadWaiter.On);

    [Fact]
    public void ItNeverComesForSomeoneWorkingAndOnlyAfterTheLongLounge()
    {
        var n = Train();
        n.Crew[1] = InTheHammock(n);
        n.Crew[2] = PlayerMotor.SpawnInCab(n.Train, P);
        n.Run(0.1);
        var seen = new Behaviours();
        var t = Tuning.Enemies.Director.Encounters.Target.Behaviours;
        void Seconds(int s)
        {
            for (int i = 0; i < s; i++)
                seen.Second([new CrewSeen(1, Post.Rider, Doing.Riding, 30, false, true, false), new CrewSeen(2, Post.Cab, Doing.Driving, 30, false, false, false)], true, t);
        }
        Seconds((int)H.ComeSeconds - 1);
        Assert.Null(HeadWaiter.Comes(n.World, seen, H));
        Seconds(1);
        Assert.Equal(1, HeadWaiter.Comes(n.World, seen, H));
        // Off (the shipping tuning), or the train standing: nobody.
        Assert.Null(HeadWaiter.Comes(n.World, seen, Tuning.Enemies.HeadWaiter));
        Assert.Null(HeadWaiter.Comes(n.World, seen, H with { MinSpeed = 30 }));
        // The driver, however long they've been at it, never: they're working.
        seen.Second([new CrewSeen(1, Post.Rider, Doing.Riding, 30, false, false, false), new CrewSeen(2, Post.Cab, Doing.Driving, 30, false, false, false)], true, t);
        Assert.Null(HeadWaiter.Comes(n.World, seen, H));
        Assert.Equal(0, seen.LazedSeconds(2));
    }

    [Fact]
    public void TheGlassRingsBeforeItLaysAHandOnAnyone()
    {
        var n = Train();
        n.Crew[1] = InTheHammock(n);
        var w = Comes(n);
        bool rang = false;
        double ringAt = -1, clearedAt = -1;
        for (double s = 0; s < H.ClimbSeconds + H.WaitSeconds + 2 && clearedAt < 0; s += SimConstants.TickSeconds)
        {
            n.Run(SimConstants.TickSeconds);
            if (w.Ringing && !rang)
                (rang, ringAt) = (true, s);
            if (w.Mode == HeadWaiterMode.Clear)
                clearedAt = s;
        }
        Assert.True(rang, "the glass never rang");
        Assert.True(clearedAt - ringAt >= H.WaitSeconds - 0.1, $"cleared {clearedAt - ringAt:0.0} s after the glass");
        Assert.True(n.Crew[1].Has(PlayerFlags.Held));
        n.AssertFair();
    }

    [Fact]
    public void BackToWorkAndItBowsAndGoes()
    {
        var n = Train();
        n.Crew[1] = InTheHammock(n);
        var w = Comes(n);
        n.Run(H.ClimbSeconds + H.RingSeconds + 0.5);
        Assert.Equal(HeadWaiterMode.Wait, w.Mode);
        n.Run(SimConstants.TickSeconds, _ => GetUp);
        Assert.False(n.Crew[1].Has(PlayerFlags.Lounging));
        // (The hammock's slung across the van: turned down it, toward the front, and off up the train.)
        n.Crew[1] = n.Crew[1] with { Yaw = 0 };
        n.Run(H.BackToWorkSeconds + 2, _ => Walk);
        Assert.True(w.Gone || w.Mode == HeadWaiterMode.Dismissed, $"still {w.Mode}");
        n.Run(H.BowSeconds + 0.2);
        Assert.True(w.Gone);
        Assert.True(n.Crew[1].Alive);
        Assert.Equal(n.Train.Frames.Count - 1, n.Crew[1].Parent);
        Assert.DoesNotContain(n.Events, e => e.EnemyId == w.Id && e.To == SpinePhase.Commit);
    }

    [Fact]
    public void StaySatAndTheTableIsClearedYouWithIt()
    {
        var n = Train(speed: 20);
        n.Crew[1] = InTheHammock(n);
        Comes(n);
        n.Run(H.ClimbSeconds + H.WaitSeconds + H.ClearSeconds + 1);
        // Over the rail at 20 m/s: past a survivable jump (spec B.3).
        Assert.False(n.Crew[1].Alive);
        Assert.Equal(DeathCause.Thrown, n.Crew[1].Death);
        n.AssertFair();
    }

    [Fact]
    public void AFriendsBlowsSendItOff()
    {
        var n = Train();
        n.Crew[1] = InTheHammock(n);
        var w = Comes(n);
        n.Run(H.ClimbSeconds + 1);
        var ctx = new EnemyContext { Tuning = E, World = n.World };
        for (int i = 0; i < H.GoBlows; i++)
            w.Struck(ctx, 2, 1);
        n.Run(H.BowSeconds + 0.5);
        Assert.True(w.Gone);
        Assert.True(n.Crew[1].Alive);
    }

    [Fact]
    public void TheDirectorSendsItToTheLongLoungeAsLazingsFoil()
    {
        // As editions/a713-headwaiter has it: on, and a foil of lazing (note 999 §2b's table). Nothing before comeSeconds;
        // then the director sends it, at the lounger, not the driver.
        var b = Tuning.Enemies.Director.Encounters.Target.Behaviours;
        var foils = new Dictionary<string, List<string>>(b.Foils) { ["lazing"] = ["gannet", "headWaiter"] };
        // The kinds about are the night's roll alone (the want floor off, note 1048): the foil is pinned on one weighted pick,
        // and three more kinds about move that pick's dice to the Knotter.
        var e = E with
        {
            Director = E.Director with
            {
                Encounters = E.Director.Encounters with
                {
                    Target = E.Director.Encounters.Target with { Behaviours = b with { Foils = foils } }
                },
                WantHold = new(),
            }
        };
        var n = new Night(4, 15, enemies: e, trainTuning: Hammocked);
        n.Crew[1] = InTheHammock(n);
        n.Crew[2] = PlayerMotor.SpawnInCab(n.Train, P);
        HeadWaiter? came = null;
        double at = 0;
        for (; at < H.ComeSeconds + 120 && came is null; at++)
        {
            n.Run(1);
            came = n.World.ActiveEnemies.OfType<HeadWaiter>().FirstOrDefault();
        }
        Assert.NotNull(came);
        Assert.True(at >= H.ComeSeconds, $"it came at {at} s");
        Assert.Equal(1, came.Diner);
    }
}
