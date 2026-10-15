using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Enemies;

/// <summary>What the Mouse Moose is doing (replicated in <see cref="Enemy.Height"/>), for its clips and cues.</summary>
public enum MouseMooseMode : byte { Creep, Bite, Drag, Flee, Hide }

/// <summary>
/// THE MOUSE MOOSE · sight · outside (GDD §21; the director, 15 Oct 2026; ARCHITECTURE §8 note 790;
/// docs/design/creatures/mouse-moose.md). A mutant rat the size of a dog with little moose antlers, that steals a yard's
/// castings. At a yard where a crane still has castings to load, it creeps in out of the dark, fastens its teeth on one and
/// drags it away from the gantry, very slowly. Out of the hook's reach, the casting is <see cref="CastingState.Taken"/>:
/// nobody can load it (the director: "once it gets out of position so that the crane can't reach it, then you can't get it as
/// a player"); dragged all the way to its hole, it's lost. A crewmate within <c>scareWithin</c> (6 m), or a blow, and it lets
/// go and bolts for its hole, hides <c>hideSeconds</c> and comes back for the same casting ("when you know what piece of loot
/// it's targeting, you can start to guard that piece of loot"); after <c>scaresToGiveUp</c> (5) scares it goes for another. It
/// never hurts anyone and nothing kills it. Rule: keep an eye on the castings, and stand by the one it wants.
/// </summary>
/// <remarks>
/// Loose in the world (<see cref="Enemy.Local"/> its world position). <see cref="Enemy.Extra"/> is the casting it's after (its
/// index in its crane's, −1 none), <see cref="Enemy.Extra2"/> how many times it's been scared off that one,
/// <see cref="Enemy.Lateral"/> its heading, <see cref="Enemy.Height"/> its <see cref="MouseMooseMode"/>. Its crane and hole are
/// the host's; what it does to the casting replicates in the Crane record.
/// </remarks>
public sealed class MouseMoose(int id) : Enemy(id)
{
    Crane? _crane;
    Double3 _hole;
    double _modeSeconds, _hidden;
    bool _leaving;
    int _reachCheck;

    public override EnemyKind Kind => EnemyKind.MouseMoose;
    public override PressureZone Zone => PressureZone.Outside;
    public override Sense Sense => Sense.Sight;
    /// <summary>What it takes is the yard's castings.</summary>
    public override Want Want => Want.Cargo;
    /// <summary>A blow reaches it anywhere but down its hole (and scares it off; it never kills it).</summary>
    public override double MeleeRadius => Mode == MouseMooseMode.Hide ? 0 : 0.6;
    /// <summary>It does its work when nobody's near: the director never dismisses it for want of company.</summary>
    public override bool StaysAboard => true;

    public MouseMooseMode Mode => (MouseMooseMode)(int)Height;
    public int Casting => (int)Math.Round(Extra);
    public int Scares => (int)Math.Round(Extra2);
    /// <summary>Where it hides (host).</summary>
    public Double3 Hole => _hole;
    /// <summary>The crane whose castings it's after (host).</summary>
    public Crane? Crane => _crane;

    /// <summary>
    /// One come for <paramref name="crane"/>'s casting <paramref name="casting"/>, out of its hole at <paramref name="hole"/>
    /// (where it bolts back to). It sets off creeping in.
    /// </summary>
    public static MouseMoose At(int id, Crane crane, int casting, Double3 hole, double lineHint) =>
        new(id) { Attached = Loose, Local = hole, _hole = hole, _crane = crane, LineDistance = lineHint, Extra = casting, Height = (double)MouseMooseMode.Creep };

    /// <summary>
    /// Its hole for a casting at <paramref name="at"/>: <c>holeOut</c> out from it, away from the nearest track (the side the
    /// stack lies on, out of the gantry's reach), out of any wall.
    /// </summary>
    public static (Double3 At, double Hint) HoleFor(World world, Double3 at, MouseMooseTuning t)
    {
        var train = world.Train;
        double hint = train.Dynamics.Distance;
        var (path, d) = train.Line.Nearest(at, ref hint);
        var on = train.Line.Sample(path, d);
        var away = (at - on.Position) with { Y = 0 };
        away = away.Length > 1e-3 ? away.Normalized : Double3.Cross(on.Tangent, Double3.Up).Normalized;
        var spot = at + away * t.HoleOut;
        spot = spot with { Y = PlayerMotor.GroundAt(spot, train.Line, ref hint) };
        if (world.Enemies is { } et)
            spot = Solidity.Clear(train, et, EnemyKind.MouseMoose, spot);
        return (spot, hint);
    }

    /// <summary>
    /// The castings it could go for: on the ground (stacked: in the hook's reach, as a casting the hook can't reach is
    /// <see cref="CastingState.Taken"/>), nearest <paramref name="from"/> first (by distance, then index), but
    /// <paramref name="except"/>.
    /// </summary>
    public static List<int> Takeable(Crane crane, Double3 from, int except = -1) =>
        [.. Enumerable.Range(0, crane.Castings.Length)
            .Where(i => i != except && crane.Castings[i].State == CastingState.Stacked)
            .OrderBy(i => Flat(crane.Castings[i].At - from)).ThenBy(i => i)];

    void SetMode(MouseMooseMode mode)
    {
        if (Mode != mode)
            _modeSeconds = 0;
        Height = (double)mode;
    }

    /// <summary>The train's gone from its yard: it goes back to its hole, and is gone once it's there.</summary>
    public void Leave() => _leaving = true;

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.MouseMoose;
        var train = ctx.Train;
        double dt = SimConstants.TickSeconds;
        _modeSeconds += dt;
        // Seen creeping in out of the dark, squeaking: the telegraph. Its teeth in a casting is the commit.
        if (Phase == SpinePhase.Dormant)
            Enter(ctx, SpinePhase.Telegraph);
        if (_crane is not { } crane || crane.Wrecked || !Near(train, crane, t.LeaveBeyond))
            _leaving = true;
        if (_leaving)
        {
            if (Mode == MouseMooseMode.Hide || Bolt(ctx, _hole, t.Run))
                Enter(ctx, SpinePhase.Gone);
            else
                SetMode(MouseMooseMode.Flee);
            return;
        }
        crane = _crane!;

        switch (Mode)
        {
            case MouseMooseMode.Hide:
                _hidden -= dt;
                if (_hidden > 0)
                    return;
                // Back out for the same casting, or, given up on it, another; none left to take, it's done here.
                if (!Wants(crane, Casting))
                {
                    if (Next(crane, -1) is not { } next)
                    {
                        Enter(ctx, SpinePhase.Gone);
                        return;
                    }
                    Extra = next;
                    Extra2 = 0;
                }
                Enter(ctx, SpinePhase.Telegraph);
                SetMode(MouseMooseMode.Creep);
                return;
            case MouseMooseMode.Flee:
                if (Bolt(ctx, _hole, t.Run))
                {
                    SetMode(MouseMooseMode.Hide);
                    _hidden = t.HideSeconds;
                }
                return;
        }

        // Someone near: it lets go and bolts (a crewmate stood over its casting keeps it off). Near as the crow flies: the
        // crane's operator, up in the cab over the stack, is no guard.
        var me = Local;
        if (ctx.LivingCrew().Any(c => (c.World - me).Length <= t.ScareWithin))
        {
            Scared(ctx);
            return;
        }

        // Its casting gone (craned, loaded, lost): another, or it's done here.
        if (!Wants(crane, Casting))
        {
            if (Next(crane, Casting) is not { } other)
            {
                _leaving = true;
                return;
            }
            Extra = other;
            Extra2 = 0;
        }
        var c = crane.Castings[Casting];
        var way = (_hole - c.At) with { Y = 0 };
        way = way.Length > 1e-3 ? way.Normalized : new Double3(1, 0, 0);
        var grip = c.At + way * (crane.Tuning.CastingSize[0] * 0.5 + t.Grip);

        switch (Mode)
        {
            case MouseMooseMode.Creep:
                if (!Step(ctx, grip, t.Creep))
                    return;
                SetMode(MouseMooseMode.Bite);
                Lateral = Yaw(c.At - Local);
                return;
            case MouseMooseMode.Bite:
                // Its teeth in it: the commit, once it's been seen long enough (App. A.1's window).
                Lateral = Yaw(c.At - Local);
                if (_modeSeconds >= t.BiteSeconds && (Phase == SpinePhase.Commit || Enter(ctx, SpinePhase.Commit)))
                    SetMode(MouseMooseMode.Drag);
                return;
            case MouseMooseMode.Drag:
                {
                    // Backwards to its hole with the casting in its teeth, slowly; faster once it's out of the hook's reach.
                    bool taken = c.State == CastingState.Taken;
                    double speed = taken ? t.DragAway : t.Drag;
                    double step = speed * dt;
                    double left = Flat(_hole - c.At);
                    if (left <= step + t.AtHole)
                    {
                        c.State = CastingState.Lost;
                        ctx.World.MouseMooseTook++;
                        Extra = -1;
                        Extra2 = 0;
                        Local = _hole;
                        SetMode(MouseMooseMode.Hide);
                        _hidden = t.HideSeconds;
                        Enter(ctx, SpinePhase.BreakOff);
                        return;
                    }
                    var to = c.At + way * step;
                    double hint = LineDistance;
                    c.At = to with { Y = PlayerMotor.GroundAt(to, train.Line, ref hint) };
                    var at = c.At + way * (crane.Tuning.CastingSize[0] * 0.5 + t.Grip);
                    Local = at with { Y = PlayerMotor.GroundAt(at, train.Line, ref hint) };
                    LineDistance = hint;
                    Lateral = Yaw(way * -1);
                    // Out of the hook's reach: nobody's to load it now (asked a few times a second: the crane's search is dear).
                    if (!taken && ++_reachCheck % t.ReachEvery == 0 && !crane.Reaches(c.At))
                        c.State = CastingState.Taken;
                    return;
                }
        }
    }

    /// <summary>
    /// Struck: a blow scares it off as coming near does (it never hurts it: nothing kills the Mouse Moose).
    /// </summary>
    public override void Struck(EnemyContext ctx, int by, double damage)
    {
        if (Mode is MouseMooseMode.Hide or MouseMooseMode.Flee)
            return;
        Scared(ctx);
    }

    /// <summary>
    /// Scared off: it lets go where the casting lies and bolts for its hole. The <c>scaresToGiveUp</c>-th time it's scared off
    /// one casting, it gives that one up and comes back for another (the nearest it can take; the same one, if there's none).
    /// </summary>
    void Scared(EnemyContext ctx)
    {
        var t = ctx.Tuning.MouseMoose;
        Extra2 = Scares + 1;
        if (Scares >= t.ScaresToGiveUp && _crane is { } crane)
        {
            if (Next(crane, Casting) is { } other)
                Extra = other;
            Extra2 = 0;
        }
        SetMode(MouseMooseMode.Flee);
        if (Phase != SpinePhase.BreakOff)
            Enter(ctx, SpinePhase.BreakOff);
    }

    /// <summary>Whether casting <paramref name="i"/> is still its to drag: on the ground, in reach or already taken out of it.</summary>
    static bool Wants(Crane crane, int i) =>
        i >= 0 && i < crane.Castings.Length && crane.Castings[i].State is CastingState.Stacked or CastingState.Taken;

    /// <summary>The next casting for it: the nearest it can take but <paramref name="except"/>; the one it had, if there's no other.</summary>
    int? Next(Crane crane, int except)
    {
        var from = Local;
        var takeable = Takeable(crane, from, except);
        if (takeable.Count > 0)
            return takeable[0];
        return Wants(crane, except) ? except : null;
    }

    /// <summary>Whether the train's still at the crane: any car within <paramref name="beyond"/> of its control stand.</summary>
    static bool Near(TrainOnLine train, Crane crane, double beyond) =>
        train.Frames.Any(f => Flat(f.Origin - crane.Controls) <= beyond);

    /// <summary>Runs toward <paramref name="to"/>: true once it's there.</summary>
    bool Bolt(EnemyContext ctx, Double3 to, double speed)
    {
        if (Flat(to - Local) <= ctx.Tuning.MouseMoose.AtHole)
            return true;
        Step(ctx, to, speed);
        return false;
    }

    /// <summary>A step toward <paramref name="to"/> along the ground: true once it's there.</summary>
    bool Step(EnemyContext ctx, Double3 to, double speed)
    {
        var way = (to - Local) with { Y = 0 };
        double step = speed * SimConstants.TickSeconds;
        double hint = LineDistance;
        if (way.Length <= step)
        {
            Local = to with { Y = PlayerMotor.GroundAt(to, ctx.Train.Line, ref hint) };
            LineDistance = hint;
            return true;
        }
        var next = Local + way.Normalized * step;
        Local = next with { Y = PlayerMotor.GroundAt(next, ctx.Train.Line, ref hint) };
        LineDistance = hint;
        Lateral = Yaw(way);
        return false;
    }

    static double Flat(Double3 v) => (v with { Y = 0 }).Length;

    /// <summary>A heading for a way along the ground, as a player's yaw (−Z forward).</summary>
    static double Yaw(Double3 way) => DMath.Atan2(-way.X, -way.Z);
}
