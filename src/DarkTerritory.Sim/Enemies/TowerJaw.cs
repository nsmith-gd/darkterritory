using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Enemies;

/// <summary>What Tower Jaw is doing (replicated in <see cref="Enemy.Height"/>), for its clips, the structure's lean and cues.</summary>
public enum TowerJawMode : byte { Gnaw, Threat, Lunge, Away, Wreck }

/// <summary>Which structure it's at: the coaling tower over the line, or the loading crane's gantry.</summary>
public enum TowerJawTarget : byte { CoalingTower, Gantry }

/// <summary>
/// TOWER JAW · vibration · outside (GDD §21, App. A.6, B.6; ARCHITECTURE §8 note 363; the director's brief of 8 Oct 2026,
/// docs/design/creatures/tower-jaw.md). A corrupted beaver the size of a bear, gnawing through the supports of the railway's
/// timber and light structures: the coaling tower over the line and the loading crane's gantry (water towers and signal
/// frames join when they do something; never a bridge). Its chiselling carries; the structure creaks and leans from half
/// gnawed and groans its last seconds. Come near and it rears and slaps its tail; come close and it lunges (a hurt, never a
/// kill). Four blows in a short while drive it off (it comes back to finish the job, its gnawing kept); twelve kill it.
/// Ignored, the structure comes down: whoever's under it is crushed (a hurt), the coaling tower's wreck blocks the line
/// (a train is stopped against it and damaged by its speed) and its chute's coal is gone; the gantry's is the crane's end.
/// The crew clears the line's wreck by hand (Use held at it, crew-seconds). Rule: hear the chewing, find the tower, get it
/// off before it falls.
/// </summary>
/// <remarks>
/// Loose in the world (<see cref="Enemy.Loose"/>), at the support it's gnawing. <see cref="Enemy.Extra"/> is how far through
/// the support it's gnawed (0 to 1), <see cref="Enemy.Extra2"/> the wreck's clearing (crew-seconds), <see cref="Enemy.Lateral"/>
/// its heading, <see cref="Enemy.Height"/> its <see cref="TowerJawMode"/>. Its structure's fixed at its making: the facility,
/// which, where the support and the fall are. Once the structure's down, this is the wreck (it holds the line closed) and the
/// beaver's gone off.
/// </remarks>
public sealed class TowerJaw(int id) : Enemy(id)
{
    double _modeSeconds, _awayFor;
    readonly List<uint> _blows = [];
    // Its structure: which facility, what kind, its support (where it gnaws), and where it falls (the line's distance the
    // wreck closes, on the main path; NaN for one that closes no line).
    int _facility = -1;
    TowerJawTarget _target;
    Double3 _support, _fallsAt;
    double _blocks = double.NaN;
    Double3 _home;

    public override EnemyKind Kind => EnemyKind.TowerJaw;
    public override PressureZone Zone => PressureZone.Outside;
    public override Sense Sense => Sense.Vibration;
    public override Want Want => Want.Cargo;
    public override double MeleeRadius => Mode is TowerJawMode.Away or TowerJawMode.Wreck ? 0 : 1.6;
    /// <summary>The wreck lies across the line until it's cleared: not a creature to be shot.</summary>
    public override bool Exposed => !Gone && Mode != TowerJawMode.Wreck;
    public override bool Hazard => Mode == TowerJawMode.Wreck;
    public override bool OnMainLine => Mode == TowerJawMode.Wreck;
    public override bool StaysAboard => true;

    public TowerJawMode Mode => (TowerJawMode)(int)Height;
    public double Gnawed => Extra;
    public double Cleared => Extra2;
    public TowerJawTarget Target => _target;
    public int FacilityIndex => _facility;
    /// <summary>Where the structure comes down (the wreck), in the world.</summary>
    public Double3 FallsAt => _fallsAt;
    /// <summary>The line's distance its wreck closes on the main path, if it closes one.</summary>
    public double? Blocks => double.IsNaN(_blocks) ? null : _blocks;

    /// <summary>
    /// At the coaling tower of facility <paramref name="facility"/>: gnawing the leg on the lever's side, <paramref name="gnawed"/>
    /// of the way through. The tower straddles the line, so it falls across it at the spout.
    /// </summary>
    public static TowerJaw AtCoalingTower(int id, World world, int facility, TowerJawTuning t, double gnawed = 0)
    {
        var run = world.Run!;
        var f = run.Facilities[facility];
        var (spout, lever) = run.ChuteAt(f, world.Train.Line);
        var on = world.Train.Line.Sample(spout);
        var right = Double3.Cross(on.Tangent, Double3.Up).Normalized;
        var leg = on.Position + right * (f.Side * t.TowerLegOut);
        return Make(id, world, t, facility, TowerJawTarget.CoalingTower, leg, on.Position, spout, gnawed);
    }

    /// <summary>At the loading crane's gantry of <paramref name="site"/>'s facility: one of its legs; it falls on the facility.</summary>
    public static TowerJaw AtGantry(int id, World world, int facility, Run.Crane crane, TowerJawTuning t, double gnawed = 0)
    {
        var leg = crane.Corner(1, 1);
        var middle = (crane.Corner(0, 0) + crane.Corner(1, 1)) * 0.5;
        return Make(id, world, t, facility, TowerJawTarget.Gantry, leg, middle, double.NaN, gnawed);
    }

    static TowerJaw Make(int id, World world, TowerJawTuning t, int facility, TowerJawTarget target, Double3 leg, Double3 falls, double blocks, double gnawed)
    {
        double hint = world.Train.Dynamics.Distance;
        var away = (leg - falls) with { Y = 0 };
        away = away.Length > 1e-6 ? away.Normalized : new Double3(1, 0, 0);
        // Beside the leg, on the side away from what it'll fall on, facing it.
        var at = leg + away * t.GnawAt;
        at = at with { Y = PlayerMotor.GroundAt(at, world.Train.Line, ref hint) };
        var j = new TowerJaw(id)
        {
            Attached = Loose,
            Local = at,
            LineDistance = hint,
            Health = t.Health,
            Extra = Math.Clamp(gnawed, 0, 0.95),
            Lateral = Yaw(away * -1),
            Height = (double)TowerJawMode.Gnaw,
        };
        j._facility = facility;
        j._target = target;
        j._support = leg;
        j._fallsAt = falls;
        j._blocks = blocks;
        j._home = at;
        return j;
    }

    /// <summary>
    /// The structure it'd be at: the facility the train's stopped at, or the next ahead within <c>approachReach</c>, with a
    /// coaling tower standing (its chute not emptied) or a crane gantry standing; null, none.
    /// </summary>
    public static (int Facility, Run.Crane? Crane)? Structure(World world, TowerJawTuning t)
    {
        if (world.Run is not { } run)
            return null;
        double front = world.Train.Dynamics.Distance;
        var down = world.ActiveEnemies.OfType<TowerJaw>().Where(j => j.Mode == TowerJawMode.Wreck || j.Gone).Select(j => j.FacilityIndex).ToHashSet();
        for (int i = 0; i < run.Facilities.Count; i++)
        {
            var f = run.Facilities[i];
            bool here = run.Facility == i, ahead = f.Start > front && f.Start - front <= t.ApproachReach;
            if (!(here || ahead) || down.Contains(i))
                continue;
            if (f.Facility == FacilityKind.CoalingTower && run.ChuteLeft(i) > 0)
                return (i, null);
            if (run.Sites.ElementAtOrDefault(i)?.Crane is { Wrecked: false } crane)
                return (i, crane);
        }
        return null;
    }

    void SetMode(TowerJawMode mode)
    {
        if (Mode != mode)
            _modeSeconds = 0;
        Height = (double)mode;
    }

    /// <summary>Its gnawing a second (by tier): through in <c>gnawSeconds</c>.</summary>
    static double Rate(TowerJawTuning t, World world) => 1 / Math.Max(1, t.GnawFor(world.Route?.Tier ?? RouteTier.Frontier));

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.TowerJaw;
        var world = ctx.World;
        double dt = SimConstants.TickSeconds;
        _modeSeconds += dt;
        if (Mode == TowerJawMode.Wreck)
        {
            Wreck(ctx, t);
            return;
        }
        if (Attached != Loose)
        {
            Local = WorldPosition(ctx.Train);
            Attached = Loose;
        }
        var me = Local;
        var living = ctx.LivingCrew().ToList();

        if (Mode == TowerJawMode.Away)
        {
            _awayFor -= dt;
            var off = (me - _support) with { Y = 0 };
            if (_awayFor > 0)
                Walk(ctx, off.Length > 1e-6 ? off : new Double3(1, 0, 0), t.Lope, t.AwayTo - off.Length);
            else if (StepTo(ctx, _home, t.Lope))
                SetMode(TowerJawMode.Gnaw);
            return;
        }

        // Come near: it turns, rears, slaps its tail (the telegraph); come close and it lunges, a bite, then back to the post.
        var near = living.OrderBy(c => Flat(c.World - me)).ThenBy(c => c.Player.Id).FirstOrDefault();
        double d = living.Count > 0 ? Flat(near.World - me) : double.MaxValue;
        if (d <= t.Notice)
        {
            Lateral = Yaw(near.World - me);
            if (Mode != TowerJawMode.Threat)
            {
                SetMode(TowerJawMode.Threat);
                Enter(ctx, SpinePhase.Telegraph);
            }
            if (d <= t.LungeWithin && PhaseSeconds >= t.ThreatSeconds && Phase == SpinePhase.Telegraph && Enter(ctx, SpinePhase.Commit))
            {
                ctx.Bite(near.Player.Id, t.Bite, DeathCause.Taken);
                SetMode(TowerJawMode.Lunge);
                Enter(ctx, SpinePhase.Telegraph);
            }
            return;
        }
        if (Mode is TowerJawMode.Threat or TowerJawMode.Lunge)
        {
            SetMode(TowerJawMode.Gnaw);
            Enter(ctx, SpinePhase.Alert);
            Lateral = Yaw(_support - me);
        }
        // At the post: gnawing through it.
        if (Phase == SpinePhase.Dormant)
            Enter(ctx, SpinePhase.Alert);
        Extra = Math.Min(1, Gnawed + Rate(t, world) * dt);
        if (Gnawed >= 1)
            Collapse(ctx, t);
    }

    /// <summary>Gnawed through: the structure comes down. Whoever's under it is crushed (a hurt); the beaver's off; this is the wreck.</summary>
    void Collapse(EnemyContext ctx, TowerJawTuning t)
    {
        foreach (var (p, at) in ctx.LivingCrew())
            if (Flat(at - _fallsAt) <= t.CrushRadius)
                ctx.Bite(p.Id, t.Crush, DeathCause.Taken);
        var world = ctx.World;
        if (world.Authority && world.Run is { } run)
        {
            if (_target == TowerJawTarget.CoalingTower)
                run.TowerDown(_facility);
            else if (run.Sites.ElementAtOrDefault(_facility)?.Crane is { } crane)
                crane.Wreck();
        }
        world.TowersDown++;
        SetMode(TowerJawMode.Wreck);
        Enter(ctx, SpinePhase.Punish);
        Local = _fallsAt;
        if (Blocks is { } along)
            LineDistance = along;
        Extra2 = 0;
    }

    /// <summary>
    /// The wreck: it holds the line closed (a train run against it is stopped there, and damaged by its speed), until the crew
    /// clears it by hand: Use held at it, each crewmate's seconds counted, <c>clearCrewSeconds</c> in all.
    /// </summary>
    void Wreck(EnemyContext ctx, TowerJawTuning t)
    {
        var train = ctx.Train;
        foreach (var (p, intent) in ctx.Crew)
            if (p.State.Alive && intent.Has(PlayerButtons.Use) && Flat(PlayerMotor.WorldPosition(p.State, train) - _fallsAt) <= t.ClearReach)
                Extra2 = Cleared + SimConstants.TickSeconds;
        if (Cleared >= t.ClearCrewSeconds)
        {
            ctx.World.TowersCleared++;
            Enter(ctx, SpinePhase.Gone);
            return;
        }
        if (Blocks is not { } along || !ctx.World.Authority)
            return;
        foreach (var rake in train.Rakes)
        {
            if (rake.Path != RailLine.MainPath)
                continue;
            double front = rake.Distance, rear = rake.RearDistance;
            if (front < along - t.WreckHalf || rear > along + t.WreckHalf)
                continue;
            // Run into it: stopped short of it, on the side it came from, and hurt by its speed (as a hard coupling, App. B.4).
            bool fromBehind = rake.PreviousDistance - rake.Consist.LengthMetres < along;
            double speed = Math.Abs(rake.Velocity);
            double shift = fromBehind ? along - t.WreckHalf - front : along + t.WreckHalf - rear;
            rake.Distance += shift;
            rake.PreviousDistance += shift;
            rake.Velocity = 0;
            if (speed > t.SafeSpeed)
            {
                var hit = rake.Consist.Vehicles[fromBehind ? 0 : ^1];
                hit.Integrity = Math.Max(0, hit.Integrity - (speed - t.SafeSpeed) * (speed - t.SafeSpeed) * t.DamagePerSpeed);
            }
            train.RefreshFrames();
        }
    }

    /// <summary>
    /// Struck: hurt by every blow (health in blows); <c>driveOffBlows</c> inside <c>driveOffSeconds</c> drive it off its post
    /// for <c>awaySeconds</c>, its gnawing kept for when it comes back. The wreck isn't struck.
    /// </summary>
    public override void Struck(EnemyContext ctx, int by, double damage)
    {
        if (Mode is TowerJawMode.Wreck or TowerJawMode.Away)
            return;
        var t = ctx.Tuning.TowerJaw;
        Marked(ctx, by);
        Health -= damage;
        if (Health <= 0)
        {
            Slay(ctx);
            return;
        }
        uint window = (uint)Math.Round(t.DriveOffSeconds * SimConstants.TickRate);
        _blows.RemoveAll(b => ctx.Tick - b > window);
        _blows.Add(ctx.Tick);
        if (_blows.Count >= t.DriveOffBlows)
        {
            _blows.Clear();
            _awayFor = t.AwaySeconds;
            SetMode(TowerJawMode.Away);
            Enter(ctx, SpinePhase.BreakOff);
            ctx.World.DrivenOff.Add(Kind);
        }
    }

    void Walk(EnemyContext ctx, Double3 way, double speed, double most)
    {
        way = way with { Y = 0 };
        if (way.Length < 1e-6 || most <= 0)
            return;
        var next = Local + way.Normalized * Math.Min(speed * SimConstants.TickSeconds, most);
        double hint = LineDistance;
        Local = next with { Y = PlayerMotor.GroundAt(next, ctx.Train.Line, ref hint) };
        LineDistance = hint;
        Lateral = Yaw(way);
    }

    /// <summary>A step toward <paramref name="to"/>; whether it's there.</summary>
    bool StepTo(EnemyContext ctx, Double3 to, double speed)
    {
        var way = (to - Local) with { Y = 0 };
        if (way.Length <= speed * SimConstants.TickSeconds)
        {
            Local = to;
            return true;
        }
        Walk(ctx, way, speed, way.Length);
        return false;
    }

    static double Flat(Double3 v) => (v with { Y = 0 }).Length;

    static double Yaw(Double3 way) => DMath.Atan2(-way.X, -way.Z);
}
