using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Enemies;

/// <summary>What the Knotter is doing (replicated in <see cref="Enemy.Height"/>), for its clips and cues.</summary>
public enum KnotterMode : byte { Creep, Force, Taut, Slack, Coil }

/// <summary>
/// THE KNOTTER · vibration · structural (GDD §21, App. A.3, B.3; ARCHITECTURE §8 note 365; the director's brief of 8 Oct
/// 2026, docs/design/creatures/knotter.md). A pale parasite like a length of hawser come alive. At speed it crawls up into
/// a coupling of the engine's rake (the creak at the gap is its tell, and cutting the coupling then shakes it off), clamps
/// both cars, forces them apart and becomes the coupling: the train still pulls as one, but the gap is far too wide to
/// jump, and the crew is split in two. Walking its back is possible, with a very high chance of slipping under the train: a
/// slip is a grab (it coils round them), broken by a friend at the gap; else they're pulled under. While the train runs it's
/// taut and shrugs off blows; at a stand it slackens, its back is safe, and it can be killed, which leaves the cars standing
/// uncoupled where it was, to be coupled up again. Rule: don't walk the rope. Stop, kill it, couple up.
/// </summary>
/// <remarks>
/// It rides the front car of its coupling (<see cref="Enemy.Attached"/>; <see cref="Enemy.Local"/> mid-gap). The gap
/// itself is the car's <see cref="Vehicle.Knot"/> (replicated with the train, so every client lays the cars out so).
/// <see cref="Enemy.Height"/> is its <see cref="KnotterMode"/>.
/// </remarks>
public sealed class Knotter(int id) : Enemy(id)
{
    double _modeSeconds;

    public override EnemyKind Kind => EnemyKind.Knotter;
    public override PressureZone Zone => PressureZone.Structural;
    public override Sense Sense => Sense.Vibration;
    public override Want Want => Want.Split;
    /// <summary>A blow reaches it from either car's end; it lands only at a stand (<see cref="Struck"/>).</summary>
    public override double MeleeRadius => 2.0;
    /// <summary>It's the coupling until it's dealt with: the director never sends it off for want of company.</summary>
    public override bool StaysAboard => true;
    /// <summary>A friend's hand (Use held at whoever it's coiled round) pulls them back up.</summary>
    public override bool PullsFree => true;

    public KnotterMode Mode => (KnotterMode)(int)Height;

    /// <summary>Coming up into the coupling behind <paramref name="car"/>.</summary>
    public static Knotter Into(int id, TrainOnLine train, int car, KnotterTuning t)
    {
        var k = new Knotter(id) { Attached = car, Health = t.Health };
        k.Middle(train);
        k.SetMode(KnotterMode.Creep);
        return k;
    }

    void SetMode(KnotterMode mode)
    {
        if (Mode != mode)
            _modeSeconds = 0;
        Height = (double)mode;
    }

    /// <summary>Mid-gap behind its car, on the plate's line.</summary>
    void Middle(TrainOnLine train)
    {
        if (Attached < 0 || Attached >= train.Frames.Count)
            return;
        var g = train.Dynamics.Tuning.Geometry;
        double l = train.Frames[Attached].Shape.HalfLength;
        Local = new Double3(g.PlateX, g.CouplerHeight, l + (g.CouplingGap + train.Vehicles[Attached].Knot) / 2);
    }

    /// <summary>
    /// The couplings it could take: behind each car of the engine's rake but the engine (its own stays one with it) and the
    /// last, none already forced, and nobody at it (within 3 m of the gap). Front cars' ids, in order.
    /// </summary>
    public static List<int> Joints(TrainOnLine train, IEnumerable<(int Id, PlayerState State)> crew)
    {
        var near = crew.Where(c => c.State.Alive).Select(c => PlayerMotor.WorldPosition(c.State, train)).ToList();
        var list = new List<int>();
        var vehicles = train.Dynamics.Consist.Vehicles;
        for (int i = 1; i < vehicles.Count - 1; i++)
        {
            var v = vehicles[i];
            if (v.Knot > 0 || v.Id >= train.Frames.Count)
                continue;
            var frame = train.Frames[v.Id];
            var gap = frame.ToWorld(new Double3(0, 1, frame.Shape.HalfLength + train.Dynamics.Tuning.Geometry.CouplingGap / 2));
            if (near.Any(p => (p - gap).Length < 3))
                continue;
            list.Add(v.Id);
        }
        return list;
    }

    /// <summary>Whether the car behind its car is still coupled to it (in the same rake).</summary>
    static bool Coupled(TrainOnLine train, int car) => car >= 0 && car < train.Frames.Count && train.VehicleBehind(car) >= 0;

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.Knotter;
        var train = ctx.Train;
        double dt = SimConstants.TickSeconds;
        _modeSeconds += dt;
        // Off any car (staged loose by a test or an audit): nothing for it to hold.
        if (Attached < 0 || Attached >= train.Frames.Count)
            return;
        var vehicle = train.Vehicles[Attached];
        if (!Coupled(train, Attached))
        {
            // The coupling cut under it (its tell's answer), or it's let go: it drops off under the train.
            vehicle.Knot = 0;
            Enter(ctx, SpinePhase.Gone);
            return;
        }
        double speed = Math.Abs(train.Dynamics.Speed);
        double full = Math.Max(0, t.Gap - train.Dynamics.Tuning.Geometry.CouplingGap);
        switch (Mode)
        {
            case KnotterMode.Creep:
                // Its tell: the creak at the gap, its claws over the plate.
                if (Phase == SpinePhase.Dormant)
                    Enter(ctx, SpinePhase.Alert);
                if (_modeSeconds >= t.CreepSeconds)
                    SetMode(KnotterMode.Force);
                break;
            case KnotterMode.Force:
                // Forcing the cars apart: the gap opens to its length over forceSeconds.
                if (ctx.World.Authority)
                    vehicle.Knot = Math.Min(full, vehicle.Knot + full / t.ForceSeconds * dt);
                if (vehicle.Knot >= full - 1e-9)
                {
                    SetMode(KnotterMode.Taut);
                    Enter(ctx, SpinePhase.Telegraph);
                }
                break;
            case KnotterMode.Taut:
                if (speed < t.SlackBelow)
                {
                    SetMode(KnotterMode.Slack);
                    break;
                }
                if (Phase != SpinePhase.Telegraph)
                    Enter(ctx, SpinePhase.Telegraph);
                Slips(ctx, t, speed);
                break;
            case KnotterMode.Slack:
                if (speed >= t.SlackBelow)
                    SetMode(KnotterMode.Taut);
                break;
        }
        Middle(train);
    }

    /// <summary>
    /// Whoever's on its back at speed may slip (a chance a second, by a hash of the tick and who: the same on every run): it
    /// coils round them (the grab), dragging them under over <c>coilSeconds</c> unless a friend pulls them up.
    /// </summary>
    void Slips(EnemyContext ctx, KnotterTuning t, double speed)
    {
        if (speed < t.SlipAbove || PhaseSeconds < ctx.Tuning.MinReactionSeconds)
            return;
        var train = ctx.Train;
        double l = train.Frames[Attached].Shape.HalfLength;
        foreach (var (p, _) in ctx.Crew.OrderBy(c => c.Player.Id))
        {
            var s = p.State;
            if (!s.Alive || s.Has(PlayerFlags.Held) || !OnItsBack(s, train, l))
                continue;
            double roll = (Hash(ctx.Tick * 2654435761u ^ (uint)p.Id * 40503u ^ (uint)Id) & 0xFFFFFF) / (double)0xFFFFFF;
            if (roll >= t.SlipPerSecond * SimConstants.TickSeconds)
                continue;
            if (!Enter(ctx, SpinePhase.Commit))
                return;
            SetMode(KnotterMode.Coil);
            Grab(ctx, p.Id, t.CoilSeconds);
            return;
        }
    }

    /// <summary>On the rope: on its car's plate line past the car's end (or the next car's, before its front).</summary>
    bool OnItsBack(in PlayerState s, TrainOnLine train, double halfLength)
    {
        if (s.Surface != Surface.Coupler)
            return false;
        if (s.Parent == Attached)
            return s.Position.Z > halfLength;
        int behind = train.VehicleBehind(Attached);
        return s.Parent == behind && s.Position.Z < -train.Frames[behind].Shape.HalfLength;
    }

    protected override void Rescued(EnemyContext ctx, int by)
    {
        base.Rescued(ctx, by);
        SetMode(KnotterMode.Taut);
        Enter(ctx, SpinePhase.Telegraph);
    }

    /// <summary>Pulled under the train between the cars.</summary>
    protected override void Punish(EnemyContext ctx, int victim)
    {
        Kill(ctx, victim, DeathCause.PulledUnder);
        SetMode(KnotterMode.Taut);
        Enter(ctx, SpinePhase.Telegraph);
    }

    /// <summary>
    /// Struck: taut at speed it shrugs a blow off; slack at a stand, it's hurt (health in blows), and killed it unlays, leaving
    /// the cars standing uncoupled where it held them (the rear ones' brakes on, as any cut at a stand).
    /// </summary>
    public override void Struck(EnemyContext ctx, int by, double damage)
    {
        Marked(ctx, by);
        if (Mode != KnotterMode.Slack)
            return;
        Health -= damage;
        if (Health > 0)
            return;
        var train = ctx.Train;
        if (ctx.World.Authority && Coupled(train, Attached))
            train.Uncouple(Attached);
        if (Attached >= 0 && Attached < train.Vehicles.Count)
            train.Vehicles[Attached].Knot = 0;
        Slay(ctx, forTheNight: false);
    }

    static uint Hash(uint x)
    {
        x ^= x >> 16;
        x *= 0x7FEB352Du;
        x ^= x >> 15;
        x *= 0x846CA68Bu;
        x ^= x >> 16;
        return x;
    }
}
