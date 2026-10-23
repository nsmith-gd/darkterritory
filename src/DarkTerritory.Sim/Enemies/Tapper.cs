using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Enemies;

/// <summary>What the Tapper is doing (replicated in <see cref="Enemy.Height"/>), for its clips and cues.</summary>
public enum TapperMode : byte { Crawl, Tap, Flinch, Lift }

/// <summary>
/// THE TAPPER · sight · flank (GDD §21; the director's playtest of 23 Oct 2026, line 36: "I am sitting on the back gun alone for
/// too long ... every creature's purpose is to foil some sort of behavior ... a clear tell and a clear counter"; Sloe's
/// proposal, docs/design/creatures/tapper.md, note 1004; built by Rook, queue #714, note 1011). A long, folded thing with arms
/// twice its length. With the train at speed and a crewmate on a gun with nobody within the behaviour reader's
/// <c>aloneM</c> for <c>dwell</c> seconds (note 999 §2b's gun alone), it comes over that car's roof from in front, where a
/// gun's eyes aren't, crawls up behind them and taps twice on the gun (the telegraph). Then, <c>tapSeconds</c> on, it lifts
/// them out of the seat over the roof's edge (a grab, <c>liftSeconds</c>) and drops them off the train. The gunner looking
/// at it (<c>lookDegrees</c>, held <c>lookSeconds</c>) and it flinches flat and backs off <c>holdSeconds</c>, then comes
/// again; a second crewmate on the car or within <c>companyM</c> and it folds and goes over the side; a blow knocks it off
/// (and a friend's drops whoever it's lifting on the roof); <c>health</c> of blows kills it. The train slowing under
/// <c>leaveBelow</c>, or the gunner off their car, and it goes. Rule: when it taps, look round. Or have someone at your back.
/// </summary>
/// <remarks>
/// On its car (<see cref="Enemy.Attached"/>, <see cref="Enemy.Local"/> on the roof in the car's frame). <see cref="Enemy.Extra"/>
/// is the gunner it's after, <see cref="Enemy.Lateral"/> which way along the car is behind them (±1),
/// <see cref="Enemy.Height"/> its <see cref="TapperMode"/>. Its look clock and its mode's seconds
/// are the host's.
/// </remarks>
public sealed class Tapper(int id) : Enemy(id)
{
    double _modeSeconds, _looked;

    public override EnemyKind Kind => EnemyKind.Tapper;
    public override PressureZone Zone => PressureZone.Flank;
    public override Sense Sense => Sense.Sight;
    public override Want Want => Want.Split;
    public override double MeleeRadius => Gone ? 0 : 1.2;
    /// <summary>It's there for the gunner alone, nobody else near: the director never dismisses it for want of company.</summary>
    public override bool StaysAboard => true;

    public TapperMode Mode => (TapperMode)(int)Height;
    public int Gunner => (int)Math.Round(Extra);

    /// <summary>
    /// One come for <paramref name="gunner"/> (on <paramref name="car"/>'s gun at <paramref name="seat"/>, the car's frame): on
    /// the roof <c>comeFromM</c> in front of the seat, crawling.
    /// </summary>
    public static Tapper Over(int id, int car, Double3 seat, int gunner, TrainOnLine train, TapperTuning t)
    {
        var shape = train.Frames[car].Shape;
        // Behind the gunner is against the way their gun faces (the rear gun faces back down the line: behind is the car's front).
        double behind = shape.Gun is { } gun && gun.Facing.Z < 0 ? 1 : -1;
        double z = Math.Clamp(seat.Z + behind * t.ComeFromM, -shape.HalfLength + 0.3, shape.HalfLength - 0.3);
        return new(id)
        {
            Attached = car, Local = new Double3(0, RoofY(shape), z), Extra = gunner, Lateral = behind, Height = (double)TapperMode.Crawl, Health = t.Health,
        };
    }

    static double RoofY(CarShape shape) => shape.Bounds.Max.Y;

    /// <summary>
    /// The gunner the train's at-speed state offers it (note 999 §2b's gun alone, at the Tapper's own dwell), with their car and
    /// seat: the longest on their gun first, then the lowest id. Null for none.
    /// </summary>
    public static (int Gunner, int Car, Double3 Seat)? Alone(World world, Behaviours read, double aloneM, double dwell)
    {
        var train = world.Train;
        (int, int, Double3)? best = null;
        double longest = -1;
        foreach (var (id, s) in world.CrewThisTick.OrderBy(c => c.Id))
        {
            if (!s.Alive || !s.Has(PlayerFlags.Seated) || s.Parent < 1 || s.Parent >= train.Frames.Count || train.Frames[s.Parent].Shape.Gun is null)
                continue;
            if (read.At(id) is not { Doing: Doing.Gunning } at || at.Seconds < dwell || at.Nearest <= aloneM)
                continue;
            if (at.Seconds > longest)
            {
                longest = at.Seconds;
                best = (id, s.Parent, s.Position);
            }
        }
        return best;
    }

    /// <summary>The gunner its rule offers now (<see cref="Alone"/> at the tier's dwell and the reader's aloneM).</summary>
    public static (int Gunner, int Car, Double3 Seat)? For(SpawnContext c) =>
        Alone(c.World, c.Director.Behaviours, c.Tuning.Director.Encounters.Target.Behaviours.AloneM, MooseTuning.ByTier(c.Tuning.Tapper.Dwell, c.Tier));

    void SetMode(TapperMode mode)
    {
        if (Mode != mode)
            _modeSeconds = 0;
        Height = (double)mode;
    }

    void Leave(EnemyContext ctx)
    {
        if (Phase == SpinePhase.Grab)
            Release(ctx);
        Enter(ctx, SpinePhase.BreakOff);
        Enter(ctx, SpinePhase.Gone);
    }

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.Tapper;
        var train = ctx.Train;
        double dt = SimConstants.TickSeconds;
        _modeSeconds += dt;
        if (Phase == SpinePhase.Dormant)
            Enter(ctx, SpinePhase.Alert);
        // Staged without a car (a look, a test): it stands where it's put.
        if (Attached < 0)
            return;
        if (Attached < 1 || Attached >= train.Frames.Count || train.Dynamics.Consist.IndexOf(Attached) < 0 || train.Dynamics.Speed < t.LeaveBelow)
        {
            Leave(ctx);
            return;
        }
        var gunner = ctx.Crew.FirstOrDefault(c => c.Player.Id == Gunner).Player;
        if (gunner.State is not { Alive: true } g || g.Parent != Attached && Phase != SpinePhase.Grab)
        {
            Leave(ctx);
            return;
        }
        var frame = train.Frames[Attached];
        var me = WorldPosition(train);
        // Company: anyone else on its car, or within companyM of it, and it folds and goes over the side.
        if (ctx.LivingCrew().Any(c => c.Player.Id != Gunner && (c.Player.State.Parent == Attached || (c.World - me).Length <= t.CompanyM)))
        {
            Leave(ctx);
            return;
        }
        if (Phase == SpinePhase.Grab)
        {
            SetMode(TapperMode.Lift);
            ctx.Lift(Gunner, frame.ToWorld(new Double3(frame.Shape.HalfWidth + 0.8, RoofY(frame.Shape) + 1.2, Local.Z)));
            return;
        }
        // The look: the gunner facing it, held, and it flinches flat and backs off.
        _looked = CrewSense.Facing(g, train, me, t.LookDegrees) ? _looked + dt : 0;
        if (_looked >= t.LookSeconds && Mode is TapperMode.Crawl or TapperMode.Tap)
        {
            _looked = 0;
            SetMode(TapperMode.Flinch);
            if (Phase != SpinePhase.Alert)
                Enter(ctx, SpinePhase.Alert);
            return;
        }
        var tier = ctx.World.Route?.Tier ?? Route.RouteTier.Frontier;
        switch (Mode)
        {
            case TapperMode.Flinch:
                // Flat on the roof, backed off the way it came; back again after its hold.
                Local = Local with { Z = Math.Clamp(Local.Z + Lateral * t.Crawl * dt, -frame.Shape.HalfLength + 0.3, frame.Shape.HalfLength - 0.3) };
                if (_modeSeconds >= MooseTuning.ByTier(t.HoldSeconds, tier))
                    SetMode(TapperMode.Crawl);
                return;
            case TapperMode.Crawl:
            {
                // Up behind them along the roof (the seat's in the car's frame; Lateral is which way's behind them).
                double to = g.Position.Z + Lateral * t.TapBehindM;
                double step = t.Crawl * dt;
                Local = Local with { Z = Local.Z + Math.Clamp(to - Local.Z, -step, step) };
                if (Math.Abs(to - Local.Z) <= 0.05)
                {
                    SetMode(TapperMode.Tap);
                    Enter(ctx, SpinePhase.Telegraph);
                }
                return;
            }
            case TapperMode.Tap:
                // TAP: the telegraph; tapSeconds on (and never before the reaction window), the lift.
                if (Phase == SpinePhase.Telegraph && _modeSeconds >= Math.Max(ctx.Tuning.MinReactionSeconds, MooseTuning.ByTier(t.TapSeconds, tier)))
                {
                    Enter(ctx, SpinePhase.Commit);
                    if (Grab(ctx, Gunner, t.LiftSeconds))
                        SetMode(TapperMode.Lift);
                }
                return;
        }
    }

    /// <summary>A blow (App. C.2): it's knocked off the roof (a friend's drops whoever it's lifting first); <c>health</c> of them kills it.</summary>
    public override void Struck(EnemyContext ctx, int by, double damage)
    {
        Marked(ctx, by);
        Health -= damage;
        if (Phase == SpinePhase.Grab && by != Holding)
            Rescued(ctx, by);
        if (Health <= 0)
        {
            Slay(ctx);
            return;
        }
        Leave(ctx);
    }

    /// <summary>Lifted out the whole while: dropped off the train.</summary>
    protected override void Punish(EnemyContext ctx, int victim)
    {
        Kill(ctx, victim, DeathCause.Dropped);
        Enter(ctx, SpinePhase.BreakOff);
        Enter(ctx, SpinePhase.Gone);
    }
}
