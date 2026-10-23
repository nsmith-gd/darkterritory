using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Enemies;

/// <summary>What the Head Waiter is doing (replicated in <see cref="Enemy.Height"/>), for its clips and its glass.</summary>
public enum HeadWaiterMode : byte { Climb, Serve, Wait, Clear, Dismissed, Dropped }

/// <summary>
/// THE HEAD WAITER · sight · aboard, the cosmetics (GDD §21; ARCHITECTURE §8 notes 1004, 1008; docs/design/creatures/head-waiter.md;
/// the director, 23 Oct 2026, line 36: "we have this whole unlock the patio with the chairs and the beers and the barbecue ...
/// But if you spend too much time up there, something should come and get you"). It foils lazing (note 999 §2b): it comes up
/// the side of the car for a crewmate who's lounged a long while (a deck chair, the hot tub, the hammock) with the train
/// running, rings a spoon on a glass behind them, and waits. Up and off to work, and it bows and goes over the side; still
/// sat when the wait's out, it clears the table, and them with it: held in the seat, then thrown over the rail (at speed,
/// their death; slow, left behind). A friend's Use or blow frees them; three blows and it drops its tray and goes; six
/// kill it. Rule: when the glass rings, back to work. A laugh first, a threat second.
/// </summary>
/// <remarks>
/// Off unless enemies.json <c>headWaiter.on</c> (built behind a flag, note 1008: awaiting the director's verdict). On the
/// lounger's car: <see cref="Enemy.Attached"/> the car, <see cref="Enemy.Local"/> its feet beside the seat; <see cref="Enemy.Extra"/>
/// the lounger; <see cref="Enemy.Height"/> its <see cref="HeadWaiterMode"/>. Host only, as the spine is.
/// </remarks>
public sealed class HeadWaiter(int id) : Enemy(id)
{
    double _modeSeconds, _upSeconds;
    int _blows;
    Double3 _seat, _stand;
    int _side = 1;

    public override EnemyKind Kind => EnemyKind.HeadWaiter;
    public override PressureZone Zone => PressureZone.Interior;
    public override Sense Sense => Sense.Sight;
    public override Want Want => Want.Split;
    public override string Called => "the Head Waiter";
    /// <summary>It's come for its diner: the director never dismisses it for want of company.</summary>
    public override bool StaysAboard => true;
    /// <summary>A friend holding Use beside the one it's clearing pulls them free.</summary>
    public override bool PullsFree => true;
    public override double MeleeRadius => 1.0;

    public HeadWaiterMode Mode => (HeadWaiterMode)(int)Height;
    /// <summary>The crewmate it's come to wait on.</summary>
    public int Diner => (int)Extra;
    /// <summary>The glass is ringing: the tell, for the mixer and the bots.</summary>
    public bool Ringing => Mode == HeadWaiterMode.Serve;
    /// <summary>Seconds of the wait left before it clears the table (−1 when it isn't waiting).</summary>
    public double WaitLeft(HeadWaiterTuning t) => Phase == SpinePhase.Telegraph ? Math.Max(0, t.WaitSeconds - PhaseSeconds) : -1;

    /// <summary>
    /// Whether <paramref name="s"/> is lounging on one of the train's cosmetics (a deck chair, the hot tub, the hammock:
    /// the deck's chairs and the train's lounges), as the Head Waiter counts it. A cab bench or the band isn't its table.
    /// </summary>
    public static bool Lounging(in PlayerState s, TrainOnLine train)
    {
        if (!s.Alive || !s.Has(PlayerFlags.Lounging) || s.Parent < 0 || s.Parent >= train.Frames.Count)
            return false;
        var feet = s.Position;
        var shape = train.Frames[s.Parent].Shape;
        return shape.Bbq is { } deck && deck.Chairs.Any(c => (c - feet).Length < 0.05)
            || shape.Lounges.Any(l => l.Kind != LoungeKind.Bench && (l.At - feet).Length < 0.05);
    }

    /// <summary>
    /// Who it comes for now, if anyone (the spawn rule's, note 1008): of the crewmates lounging on a cosmetic, the one the
    /// behaviour reader has seen lazing longest without a break, at least <see cref="HeadWaiterTuning.ComeSeconds"/>, with
    /// the train running; ties to the lowest id. Null for nobody.
    /// </summary>
    public static int? Comes(World world, Behaviours seen, HeadWaiterTuning t)
    {
        if (!t.On || world.Train.Dynamics.Speed < t.MinSpeed || world.TrainInFort)
            return null;
        int? best = null;
        double longest = 0;
        foreach (var c in world.CrewThisTick.OrderBy(c => c.Id))
        {
            if (!Lounging(c.State, world.Train))
                continue;
            double lazed = seen.LazedSeconds(c.Id);
            if (lazed >= t.ComeSeconds && lazed > longest)
                (best, longest) = (c.Id, lazed);
        }
        return best;
    }

    /// <summary>Up the side of <paramref name="diner"/>'s car beside their seat, on the side they sit nearer.</summary>
    public static HeadWaiter For(int id, TrainOnLine train, int diner, in PlayerState state, HeadWaiterTuning t)
    {
        var shape = train.Frames[state.Parent].Shape;
        var seat = state.Position;
        var (stand, side) = StandBeside(shape, seat, t);
        return new HeadWaiter(id)
        {
            Attached = state.Parent,
            Local = new Double3(side * (shape.HalfWidth + 0.3), seat.Y - 1.2, stand.Z),
            Extra = diner,
            Health = t.KillBlows,
            Height = (double)HeadWaiterMode.Climb,
            _seat = seat,
            _stand = stand,
            _side = side,
        };
    }

    /// <summary>
    /// Where it stands to wait on <paramref name="seat"/> (in the car's frame): at the seat's shoulder on its outer side, a
    /// little behind it, where a waiter stands to pour; and that side (−1, +1).
    /// </summary>
    public static (Double3 Stand, int Side) StandBeside(CarShape shape, Double3 seat, HeadWaiterTuning t)
    {
        int side = seat.X >= 0 ? 1 : -1;
        return (new Double3(Math.Clamp(seat.X + side * t.StandOff, -shape.HalfWidth + 0.3, shape.HalfWidth - 0.3), seat.Y, seat.Z + 0.4), side);
    }

    void SetMode(HeadWaiterMode mode)
    {
        if (Mode != mode)
            _modeSeconds = 0;
        Height = (double)mode;
    }

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.HeadWaiter;
        double dt = SimConstants.TickSeconds;
        _modeSeconds += dt;
        if (Mode is HeadWaiterMode.Dismissed or HeadWaiterMode.Dropped)
        {
            // A bow (or the tray let go), and over the side.
            Enter(ctx, SpinePhase.BreakOff);
            if (_modeSeconds >= t.BowSeconds)
                Enter(ctx, SpinePhase.Gone);
            return;
        }
        if (Phase is SpinePhase.BreakOff)
        {
            SetMode(HeadWaiterMode.Dropped);
            return;
        }
        var diner = ctx.Crew.FirstOrDefault(c => c.Player.Id == Diner);
        // Its diner gone (dead, off the train, or the car let go): nobody to wait on.
        if (diner.Player.State is not { Alive: true } s || s.Parent != Attached || Attached < 0 || Attached >= ctx.Train.Frames.Count)
        {
            SetMode(HeadWaiterMode.Dismissed);
            return;
        }
        if (Mode == HeadWaiterMode.Climb)
        {
            Enter(ctx, SpinePhase.Alert);
            double k = Math.Min(1, _modeSeconds / t.ClimbSeconds);
            var from = new Double3(_side * (ctx.Train.Frames[Attached].Shape.HalfWidth + 0.3), _seat.Y - 1.2, _stand.Z);
            Local = from + (_stand - from) * k;
            if (k >= 1)
            {
                // The plate down beside them, and the glass rung: the telegraph, and the wait begins.
                Enter(ctx, SpinePhase.Telegraph);
                SetMode(HeadWaiterMode.Serve);
            }
            return;
        }
        if (Mode == HeadWaiterMode.Serve && _modeSeconds >= t.RingSeconds)
            SetMode(HeadWaiterMode.Wait);
        if (Phase == SpinePhase.Telegraph)
        {
            // Back to work: up out of the seat and away from it, for a while; sat back down, the count starts again.
            bool up = !Lounging(s, ctx.Train) && !s.Has(PlayerFlags.Playing) && (s.Position - _seat).Length >= t.AwayM;
            _upSeconds = up ? _upSeconds + dt : 0;
            if (_upSeconds >= t.BackToWorkSeconds)
            {
                SetMode(HeadWaiterMode.Dismissed);
                return;
            }
            if (PhaseSeconds >= t.WaitSeconds && Lounging(s, ctx.Train) && Enter(ctx, SpinePhase.Commit) && Grab(ctx, Diner, t.ClearSeconds))
                SetMode(HeadWaiterMode.Clear);
        }
    }

    /// <summary>Cleared: thrown over the rail on the side it stood (at speed, their death; slow, left behind).</summary>
    protected override void Punish(EnemyContext ctx, int victim)
    {
        var frame = ctx.Train.Frames[Attached];
        ctx.Pull(victim, frame.DirToWorld(new Double3(_side, 0.3, 0)) * ctx.Tuning.HeadWaiter.ThrowSpeed, DeathCause.Thrown);
        Enter(ctx, SpinePhase.BreakOff);
        SetMode(HeadWaiterMode.Dismissed);
    }

    /// <summary>Pulled free of it by a friend: it drops its tray and goes.</summary>
    protected override void Rescued(EnemyContext ctx, int by)
    {
        base.Rescued(ctx, by);
        SetMode(HeadWaiterMode.Dropped);
    }

    /// <summary>Each blow counts: <see cref="HeadWaiterTuning.GoBlows"/> and it drops the tray and goes; <see cref="HeadWaiterTuning.KillBlows"/> kill it.</summary>
    public override void Struck(EnemyContext ctx, int by, double damage)
    {
        Marked(ctx, by);
        var t = ctx.Tuning.HeadWaiter;
        _blows++;
        Health = Math.Max(0, t.KillBlows - _blows);
        if (_blows >= t.KillBlows)
        {
            Slay(ctx);
            return;
        }
        if (Phase == SpinePhase.Grab && by != Holding)
        {
            Rescued(ctx, by);
            return;
        }
        if (_blows >= t.GoBlows)
            SetMode(HeadWaiterMode.Dropped);
    }
}
