using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The Gannet (GDD §21, App. A.4, B.4; the director's decisions of 7 Oct 2026; ARCHITECTURE §8 note 340). Rule: when it
/// folds, break your stride. Over a fast train it hangs over a roof-walker and folds; holding the line is a stab, breaking
/// stride a miss (stuck in the planks); whoever hits it is its mark, and it comes down on them and pecks four times unless
/// friends drive it off; inside is shelter; a slow train and it peels off; killed, its head is a trophy.
/// </summary>
public class GannetTests
{
    static readonly EnemyTuning E = Tuning.Enemies;
    static readonly GannetTuning G = Tuning.Enemies.Gannet;
    static readonly PlayerTuning P = Tuning.Player;

    /// <summary>On car <paramref name="car"/>'s roof at <paramref name="z"/>, facing forward (−Z, the way the train goes).</summary>
    static PlayerState Roof(Night n, int car, double z, double x = 0) =>
        PlayerMotor.SpawnOnRoof(n.Train, car, z, P) is var s ? s with { Yaw = 0, Position = s.Position with { X = x } } : default;

    static Gannet Over(Night n) => n.World.AddEnemy(id => Gannet.Arriving(id, n.Train, G, G.SoarHeight[0]));

    static readonly PlayerIntent Walk = new() { MoveZ = 1 };

    /// <summary>Runs until the Gannet's folded (its telegraph), with everyone doing <paramref name="intent"/>.</summary>
    static void UntilFold(Night n, Gannet g, Func<int, PlayerIntent> intent, double within = 12)
    {
        for (double t = 0; t < within && g.Mode != GannetMode.Fold; t += SimConstants.TickSeconds)
            n.Run(SimConstants.TickSeconds, intent);
    }

    [Fact]
    public void HoldYourLineAfterTheFoldAndItStabsYou()
    {
        var n = new Night(4, speed: 20);
        n.Crew[1] = Roof(n, 2, 6);
        var g = Over(n);
        UntilFold(n, g, _ => Walk);
        Assert.Equal(GannetMode.Fold, g.Mode);
        n.Run(G.FoldSeconds + 0.2, _ => Walk);
        Assert.Equal(P.Health - G.StabDamage, n.Crew[1].Health);
        Assert.True(n.Crew[1].Alive);
        Assert.NotEqual(GannetMode.Stuck, g.Mode);
        n.AssertFair();
    }

    [Fact]
    public void BreakStrideOnTheFoldAndItMissesAndSticks()
    {
        var n = new Night(4, speed: 20);
        n.Crew[1] = Roof(n, 2, 6);
        var g = Over(n);
        UntilFold(n, g, _ => Walk);
        // Stopped dead the moment it folds.
        n.Run(G.FoldSeconds + 0.2);
        Assert.Equal(P.Health, n.Crew[1].Health);
        Assert.Equal(GannetMode.Stuck, g.Mode);
        Assert.True(g.MeleeRadius > 0);
        n.AssertFair();
    }

    [Fact]
    public void ABotWalkingTheRoofStopsWhenItHangsOverItAndIsLetBe()
    {
        // Note 454: a crew bot heeds it (Heed.Gannet): walking car 2's roof, it stops dead as the Gannet hangs over it, the
        // Gannet lets it be and climbs away, and it walks on, unhurt.
        var n = new Night(4, speed: 20);
        n.Crew[1] = Roof(n, 2, 6);
        var g = Over(n);
        double hung = -1, walkedOn = -1;
        var stopped = n.Crew[1].Position;
        for (double t = 0; t < 30 && walkedOn < 0; t += SimConstants.TickSeconds)
        {
            n.Run(SimConstants.TickSeconds, id => Bots.Heed.Gannet(Walk, n.Crew[id], n.World, id));
            if (hung < 0 && g.Mode == GannetMode.Hang && g.Prey == 1)
                (hung, stopped) = (t, n.Crew[1].Position);
            else if (hung >= 0 && g.Mode is GannetMode.Climb or GannetMode.Soar && (n.Crew[1].Position - stopped).Length > 0.5)
                walkedOn = t;
            Assert.NotEqual(GannetMode.Fold, g.Mode);
        }
        Assert.True(hung >= 0, "it never hung over the walker");
        Assert.True(walkedOn > hung, "it never walked on");
        Assert.Equal(P.Health, n.Crew[1].Health);
        n.AssertFair();
    }

    [Fact]
    public void WhoeverStandsStillIsNeverItsPrey()
    {
        var n = new Night(4, speed: 20);
        n.Crew[1] = Roof(n, 2, 0);
        var g = Over(n);
        n.Run(20);
        Assert.DoesNotContain(n.Events, e => e.Kind == EnemyKind.Gannet && e.To == SpinePhase.Telegraph);
        Assert.Equal(P.Health, n.Crew[1].Health);
    }

    /// <summary>A miss, and a friend beside where it struck clubs it: they're its mark.</summary>
    static (Night N, Gannet G) Marked(int friendCar = 2)
    {
        var n = new Night(4, speed: 20);
        n.Crew[1] = Roof(n, 2, 6);
        var g = Over(n);
        UntilFold(n, g, _ => Walk);
        n.Run(G.FoldSeconds + 0.2);
        Assert.Equal(GannetMode.Stuck, g.Mode);
        var stuck = g.Local;
        n.Crew[2] = Roof(n, 2, stuck.Z + 1.2, stuck.X) with { Yaw = 0 };
        n.Run(0.3, id => id == 2 ? new PlayerIntent { Actions = PlayerActions.Swing } : default);
        Assert.Equal(2, g.Mark);
        Assert.True(g.Health < G.Health);
        return (n, g);
    }

    [Fact]
    public void HitItAndItComesDownOnYouAndPecks()
    {
        var (n, g) = Marked();
        // Its next pass is for its mark: the bank (the telegraph), then down on them.
        for (double t = 0; t < 25 && g.Phase != SpinePhase.Grab; t += 0.1)
            n.Run(0.1);
        Assert.Equal(SpinePhase.Grab, g.Phase);
        Assert.Equal(2, g.Holding);
        Assert.Contains(n.Events, e => e.Kind == EnemyKind.Gannet && e.From == SpinePhase.Telegraph && e.To == SpinePhase.Commit
            && e.SecondsInFrom >= G.BankSeconds - 1e-9);
        n.Run(G.PeckEvery * 2 + 0.1);
        Assert.Equal(2, g.PecksLanded(G));
        n.Run(G.PeckEvery * 2);
        Assert.Equal(DeathCause.Pecked, n.Crew[2].Death);
        n.AssertFair();
    }

    [Fact]
    public void FriendsDriveItOffAPinAndTheLastOneIsItsMark()
    {
        var (n, g) = Marked();
        for (double t = 0; t < 25 && g.Phase != SpinePhase.Grab; t += 0.1)
            n.Run(0.1);
        Assert.Equal(SpinePhase.Grab, g.Phase);
        var at = n.Crew[2].Position;
        n.Crew[1] = Roof(n, 2, at.Z + 1.3, at.X);
        n.Crew[3] = Roof(n, 2, at.Z - 1.3, at.X) with { Yaw = Math.PI };
        // Two friends swinging: three blows between them.
        for (double t = 0; t < 4 && g.Phase == SpinePhase.Grab; t += SimConstants.TickSeconds)
            n.Run(SimConstants.TickSeconds, id => id is 1 or 3 ? new PlayerIntent { Actions = PlayerActions.Swing } : default);
        Assert.NotEqual(SpinePhase.Grab, g.Phase);
        Assert.True(n.Crew[2].Alive);
        Assert.False(n.Crew[2].Has(PlayerFlags.Held));
        Assert.True(g.Mark is 1 or 3);
        n.AssertFair();
    }

    [Fact]
    public void InsideACarItCantComeDownOnYou()
    {
        var (n, g) = Marked();
        // The mark gets down into car 2's room before the bank's done.
        var room = n.Train.Frames[2].Shape.Interior!.Value;
        var inside = n.Crew[2] with { Position = new Double3(0, room.Min.Y + 0.05, 0), Surface = Surface.Deck, Velocity = default };
        n.Crew[2] = inside;
        n.Crew[1] = inside with { Position = inside.Position + new Double3(0, 0, 1) };
        n.Run(25);
        Assert.DoesNotContain(n.Events, e => e.Kind == EnemyKind.Gannet && e.To == SpinePhase.Grab);
        Assert.True(n.Crew[2].Alive);
    }

    [Fact]
    public void ASlowTrainAndItPeelsOff()
    {
        var n = new Night(4, speed: 20);
        n.Crew[1] = Roof(n, 2, 0);
        var g = Over(n);
        n.Run(1);
        n.Train.Dynamics.Velocity = G.StallBelow - 2;
        n.Run(G.StallSeconds + 0.5);
        Assert.Equal(GannetMode.Away, g.Mode);
        Assert.False(g.Exposed);
        Assert.False(Director.Engaged(g));
    }

    [Fact]
    public void KilledItLeavesItsHeadAsATrophy()
    {
        var (n, g) = Marked();
        // A gun's worth of blows, straight off: dead (the gun is one round of four blows).
        g.Health = 0.5;
        // (The swing that marked it has to recover first: one tool swing every melee.swingSeconds.)
        n.Run(E.Melee.SwingSeconds + 0.2, id => id == 2 ? new PlayerIntent { Actions = PlayerActions.Swing } : default);
        Assert.True(g.Gone);
        var head = Assert.Single(n.World.Bodies.All, b => b.Owner == Run.Run.TrophyOwner(EnemyKind.Gannet));
        Assert.Equal(Physics.BodyKind.Loot, head.Kind);
        Assert.Equal(2, head.Parent);
    }

    [Fact]
    public void HurtBelowAThirdItLeavesForTheRun()
    {
        var (n, g) = Marked();
        g.Health = G.GiveUpBelow + 0.5;
        n.Run(E.Melee.SwingSeconds + 0.2, id => id == 2 ? new PlayerIntent { Actions = PlayerActions.Swing } : default);
        Assert.True(g.Gone);
        Assert.DoesNotContain(n.World.Bodies.All, b => b.Owner == Run.Run.TrophyOwner(EnemyKind.Gannet));
    }

    [Fact]
    public void ItsNumbersKeepTheFairnessContract()
    {
        Assert.True(G.FoldSeconds >= E.MinReactionSeconds);
        Assert.True(G.BankSeconds >= E.MinReactionSeconds);
        Assert.True(G.StabDamage >= E.Damage.MinHit);
        Assert.InRange(G.Pecks * G.PeckEvery, 8, 20);
        Assert.True(G.ArriveAbove > G.StallBelow);
        var tiers = Enum.GetValues<Route.RouteTier>().Select(t => MooseTuning.ByTier(G.TierWeights, t)).ToList();
        Assert.All(tiers, w => Assert.True(w > 0));
        for (int i = 1; i < tiers.Count; i++)
            Assert.True(tiers[i] > tiers[i - 1]);
    }
}
