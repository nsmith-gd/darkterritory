using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The Moose (GDD §21, App. A.6, B.6; the director's decisions of 7 Oct 2026; ARCHITECTURE §8 note 332). Rule: give it
/// room, keep it quiet. Docile left be; crowded, talked near or hit it warns, squares up and charges; a charge is a heavy
/// hit and pins the hurt; a friend's blow takes it off them; out of sight it searches and gives up; lost at a car it rams
/// it (only a ram); the train pulling away ends it; nothing kills it; it's never on the rail.
/// </summary>
public class MooseTests
{
    static readonly EnemyTuning E = Tuning.Enemies;
    static readonly MooseTuning M = Tuning.Enemies.Moose;
    static readonly PlayerTuning P = Tuning.Player;

    /// <summary>A point on the ground beside car <paramref name="car"/>, <paramref name="lateral"/> m off its centre line.</summary>
    static Double3 Beside(Night n, int car, double lateral, double along = 0)
    {
        var frame = n.Train.Frames[car];
        var at = frame.ToWorld(new Double3(lateral, 0, along));
        double hint = n.Train.Dynamics.Distance;
        return at with { Y = PlayerMotor.GroundAt(at, n.Train.Line, ref hint) };
    }

    static PlayerState Ground(Night n, Double3 at, Double3? toward = null)
    {
        var s = PlayerMotor.SpawnOnGround(at, n.Train.Line, n.Train.Dynamics.Distance, P);
        if (toward is { } to)
        {
            var d = to - at;
            s.Yaw = DMath.Atan2(-d.X, -d.Z);
        }
        return s;
    }

    static Moose Graze(Night n, Double3 at) => n.World.AddEnemy(id => Moose.Grazing(id, at, n.Train.Dynamics.Distance, 0));

    [Fact]
    public void LeftBeItGrazesAndNeverCharges()
    {
        var n = new Night(4, speed: 0);
        var moose = Graze(n, Beside(n, 2, 30));
        // Beyond its crowding, and quiet, all through a long stop.
        n.Crew[1] = Ground(n, Beside(n, 2, 30, M.CrowdAt + 3));
        n.Run(60);
        Assert.Equal(SpinePhase.Dormant, moose.Phase);
        Assert.DoesNotContain(n.Events, e => e.Kind == EnemyKind.Moose && e.To == SpinePhase.Telegraph);
        Assert.Equal(P.Health, n.Crew[1].Health);
    }

    [Fact]
    public void CrowdItAndItWarnsSquaresUpAndChargesAHeavyHit()
    {
        var n = new Night(4, speed: 0);
        var at = Beside(n, 2, 30);
        var moose = Graze(n, at);
        n.Crew[1] = Ground(n, Beside(n, 2, 30, 8));
        n.Run(12);
        var events = n.Events.Where(e => e.Kind == EnemyKind.Moose).ToList();
        // Listening first, then the square-up, then the charge.
        int alert = events.FindIndex(e => e.To == SpinePhase.Alert);
        int squareUp = events.FindIndex(e => e.To == SpinePhase.Telegraph);
        int charge = events.FindIndex(e => e.To == SpinePhase.Commit);
        Assert.True(alert >= 0 && squareUp > alert && charge > squareUp);
        Assert.True(events[charge].SecondsInFrom >= M.SquareUpSeconds - 1e-9);
        Assert.Equal(P.Health - M.ChargeDamage, n.Crew[1].Health);
        Assert.Equal(1, moose.Target);
        n.AssertFair();
    }

    [Fact]
    public void BackOffAndHushAtTheWarningAndItSettles()
    {
        var n = new Night(4, speed: 0);
        var moose = Graze(n, Beside(n, 2, 30));
        n.Crew[1] = Ground(n, Beside(n, 2, 30, 15));
        // Within its crowding long enough to warn (past warnAt), then well back.
        n.Run(M.WarnAt / M.CrowdPerSecond + 0.3);
        Assert.Equal(SpinePhase.Alert, moose.Phase);
        Assert.True(moose.Aggro >= M.WarnAt);
        n.Crew[1] = Ground(n, Beside(n, 2, 30, 40));
        n.Run(15);
        Assert.Equal(SpinePhase.Dormant, moose.Phase);
        Assert.Equal(0, moose.Aggro);
        Assert.DoesNotContain(n.Events, e => e.Kind == EnemyKind.Moose && e.To == SpinePhase.Telegraph);
    }

    [Fact]
    public void TalkingNearItRilesItAndTheTalkerIsItsTarget()
    {
        var n = new Night(4, speed: 0);
        var moose = Graze(n, Beside(n, 2, 30));
        // Two beyond its crowding but in its hearing (on the ground, round the far side of its crowding): one talks.
        n.Crew[1] = Ground(n, Beside(n, 2, 30 + M.HearVoice - 1, 0));
        n.Crew[2] = Ground(n, Beside(n, 2, 30, -(M.HearVoice - 1)));
        Assert.True(M.HearVoice - 1 > M.CloseAt);
        n.Run(8, id => id == 2 ? new PlayerIntent { Voice = 160 } : default);
        Assert.Equal(2, moose.Target);
        Assert.Contains(n.Events, e => e.Kind == EnemyKind.Moose && e.To == SpinePhase.Telegraph);
    }

    [Fact]
    public void ItPinsTheHurtAndAFriendsBlowTakesItOffThem()
    {
        var n = new Night(4, speed: 0);
        var moose = Graze(n, Beside(n, 2, 30));
        n.Crew[1] = Ground(n, Beside(n, 2, 30, 8)) with { Health = (int)M.GrabBelowHealth };
        for (double t = 0; t < 12 && moose.Phase != SpinePhase.Grab; t += 0.1)
            n.Run(0.1);
        Assert.Equal(SpinePhase.Grab, moose.Phase);
        Assert.Equal(1, moose.Holding);
        Assert.True(n.Crew[1].Has(PlayerFlags.Held));
        // A friend comes up behind it and swings.
        var mooseAt = moose.Local;
        var friendAt = mooseAt + Moose.Facing(moose.Yaw) * -1.6 + new Double3(0.1, 0, 0);
        n.Crew[2] = Ground(n, friendAt with { Y = mooseAt.Y }, toward: mooseAt);
        n.Run(0.5, id => id == 2 ? new PlayerIntent { Actions = PlayerActions.Swing } : default);
        Assert.NotEqual(SpinePhase.Grab, moose.Phase);
        Assert.True(n.Crew[1].Alive);
        Assert.False(n.Crew[1].Has(PlayerFlags.Held));
        Assert.Equal(2, moose.Target);
        n.AssertFair();
    }

    [Fact]
    public void PinnedWithNobodyComingYoureTrampled()
    {
        var n = new Night(4, speed: 0);
        var moose = Graze(n, Beside(n, 2, 30));
        n.Crew[1] = Ground(n, Beside(n, 2, 30, 8)) with { Health = (int)M.GrabBelowHealth };
        // A friend far off (so it isn't a crew of one, where the victim could struggle free), doing nothing.
        n.Crew[2] = Ground(n, Beside(n, 2, -60, 0));
        for (double t = 0; t < 12 && moose.Phase != SpinePhase.Grab; t += 0.1)
            n.Run(0.1);
        Assert.Equal(SpinePhase.Grab, moose.Phase);
        n.Run(M.PinSeconds + 1);
        Assert.Equal(DeathCause.Trampled, n.Crew[1].Death);
        Assert.NotEqual(SpinePhase.Grab, moose.Phase);
        n.AssertFair();
    }

    [Fact]
    public void NothingKillsIt()
    {
        var n = new Night(4, speed: 0);
        var moose = Graze(n, Beside(n, 2, 30));
        var at = moose.Local;
        n.Crew[1] = Ground(n, at + new Double3(1.8, 0, 0), toward: at);
        n.Run(6, id => new PlayerIntent { Actions = PlayerActions.Swing });
        Assert.False(moose.Gone);
        Assert.Equal(1, moose.Target);
    }

    [Fact]
    public void OutOfSightAndQuietItSearchesThenGivesUp()
    {
        var n = new Night(4, speed: 0);
        var moose = n.World.AddEnemy(id => Moose.Enraged(id, Beside(n, 2, 30), n.Train.Dynamics.Distance, 1));
        // Behind the train from it, on the ground on the far side: the car's between them.
        n.Crew[1] = Ground(n, Beside(n, 2, -4));
        // It searches where it lost them, up against the line it won't cross; the car still hides them (it doesn't count as
        // crowding it), and in a search's time it's given up and gone home.
        n.Run(M.SearchSeconds * 2 + 5);
        Assert.Null(moose.Target);
        Assert.Equal(P.Health, n.Crew[1].Health);
    }

    [Fact]
    public void LostAtACarItRamsItAWhileAndARamIsOnlyARam()
    {
        var n = new Night(4, speed: 0);
        var moose = n.World.AddEnemy(id => Moose.Enraged(id, Beside(n, 2, 12), n.Train.Dynamics.Distance, 1));
        // Its target's got up into car 2's room, right by where it lost them.
        var room = n.Train.Frames[2].Shape.Interior!.Value;
        var inside = PlayerMotor.SpawnOnRoof(n.Train, 2, 0, P);
        inside.Position = new Double3(0, room.Min.Y + 0.05, 0);
        inside.Surface = Surface.Deck;
        n.Crew[1] = inside;
        double integrity = n.Train.Vehicles[2].Integrity;
        n.Run(M.RamSeconds + 6);
        Assert.True(moose.Rams >= 3, $"{moose.Rams} rams");
        Assert.Null(moose.Target);
        Assert.Equal(P.Health, n.Crew[1].Health);
        Assert.True(n.Crew[1].Alive);
        Assert.Equal(integrity, n.Train.Vehicles[2].Integrity);
        Assert.Equal(2, n.Crew[1].Parent);
    }

    [Fact]
    public void TheTrainPullingAwayEndsIt()
    {
        var n = new Night(4, speed: 0);
        var moose = n.World.AddEnemy(id => Moose.Enraged(id, Beside(n, 2, 30), n.Train.Dynamics.Distance, 1));
        n.Crew[1] = PlayerMotor.SpawnInCab(n.Train, P);
        n.Run(1);
        n.Train.Dynamics.Velocity = 4;
        n.Run(1);
        Assert.Null(moose.Target);
        n.Run(5);
        Assert.DoesNotContain(n.Events, e => e.Kind == EnemyKind.Moose && e.To is SpinePhase.Telegraph or SpinePhase.Commit);
    }

    [Fact]
    public void ItNeverSetsFootOnTheRail()
    {
        // Riled by someone right by the track and chasing them up and down beside it: it never comes inside the clearance.
        var n = new Night(4, speed: 0);
        var moose = n.World.AddEnemy(id => Moose.Enraged(id, Beside(n, 2, 20), n.Train.Dynamics.Distance, 1));
        n.Crew[1] = Ground(n, Beside(n, 0, 2.2, -30));
        double closest = double.MaxValue;
        for (int i = 0; i < 300; i++)
        {
            n.Run(0.1, _ => new PlayerIntent { MoveZ = -1 });
            closest = Math.Min(closest, Moose.TrackOff(n.Train, moose.Local, n.Train.Dynamics.Distance));
        }
        Assert.True(closest >= M.TrackClearance - 1e-6, $"came within {closest:0.00} m of the track");
    }

    [Fact]
    public void APassingTrainRilesItALittleButNeverEnough()
    {
        var n = new Night(4, speed: 8);
        var ahead = n.Train.Line.Sample(n.Train.Dynamics.Distance + 60);
        var right = Double3.Cross(ahead.Tangent, Double3.Up).Normalized;
        var at = ahead.Position + right * (M.MovingClearance + 2);
        var moose = Graze(n, at);
        double most = 0;
        for (int i = 0; i < 150; i++)
        {
            n.Run(0.1);
            most = Math.Max(most, moose.Aggro);
        }
        Assert.InRange(most, M.ListenAt, M.WarnAt - 1);
        Assert.DoesNotContain(n.Events, e => e.Kind == EnemyKind.Moose && e.To == SpinePhase.Telegraph);
    }

    [Fact]
    public void ItIsInEveryTierAndMoreTheHarderTheTier()
    {
        var tiers = Enum.GetValues<Route.RouteTier>();
        var weights = tiers.Select(t => MooseTuning.ByTier(M.TierWeights, t)).ToList();
        Assert.All(weights, w => Assert.True(w > 0));
        for (int i = 1; i < weights.Count; i++)
            Assert.True(weights[i] > weights[i - 1]);
        var lineside = tiers.Select(t => MooseTuning.ByTier(M.Lineside, t)).ToList();
        for (int i = 1; i < lineside.Count; i++)
            Assert.True(lineside[i] >= lineside[i - 1]);
        // And no tier gate in the spawn rule: a Local night at a stop may have one (the tier weight is Local's).
        Assert.Equal(1, MooseTuning.ByTier(M.TierWeights, Route.RouteTier.Local));
    }

    [Fact]
    public void ItsChargeIsAHitNotChip()
    {
        Assert.True(M.ChargeDamage >= E.Damage.MinHit);
        Assert.True(M.PinSeconds is >= 8 and <= 20);
        Assert.True(M.SquareUpSeconds >= E.MinReactionSeconds);
        Assert.True(M.RackSpan / 2 + M.HitReach < M.TrackClearance + 1.5);
    }
}
