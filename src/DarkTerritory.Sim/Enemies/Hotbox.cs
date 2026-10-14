using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Enemies;

/// <summary>What Hotbox is doing (replicated in <see cref="Enemy.Height"/>), for its clips, its glow and its cues.</summary>
public enum HotboxMode : byte { Knock, Glow, Seized, Unfolded, Prised }

/// <summary>
/// HOTBOX · vibration · structural (GDD §21, App. A.3, B.3; ARCHITECTURE §8 note 367; the director's brief of 8 Oct 2026,
/// docs/design/creatures/hotbox.md). A low armoured axle parasite folded into one truck of a freight car, one side, feeding
/// on the bearing's grease, friction and heat. First a knock, once a wheel turn (faster the faster you run), from that truck
/// and side; then the bearing glows and smokes there; then the axle seizes and the car drags badly (the train held to a
/// crawl's top speed while it's in the train). Never fire, never a derailment. Only at a stand does it half unfold out of
/// the truck, where it can be killed or prised out (Use held with a crowbar or a wrench), and it bites whoever's at its
/// head; the train moving again, it folds back in. A seized axle stays seized once it's out: repaired with a wrench, or the
/// car cut. Grease does nothing to it. Rule: hear the knock, find the wheel, stop to pull it.
/// </summary>
/// <remarks>
/// It rides its car's frame (<see cref="Enemy.Attached"/>), <see cref="Enemy.Local"/> at its truck, its side's wheels.
/// <see cref="Enemy.Extra"/> is its heat (seconds of feeding at the reference speed), <see cref="Enemy.Lateral"/> its side
/// (−1 left, +1 right), <see cref="Enemy.Height"/> its <see cref="HotboxMode"/>.
/// </remarks>
public sealed class Hotbox(int id) : Enemy(id)
{
    double _modeSeconds, _stoodFor;
    // Who's prising at it, and how long they've held Use there: by id, in order.
    readonly SortedDictionary<int, double> _prise = [];

    public override EnemyKind Kind => EnemyKind.Hotbox;
    /// <summary>The train's own running gear (App. B.1's structural zone: the train itself under pressure).</summary>
    public override PressureZone Zone => PressureZone.Structural;
    public override Sense Sense => Sense.Vibration;
    /// <summary>It costs the train a car (a seized axle drags it, or it's cut loose).</summary>
    public override Want Want => Want.Cargo;
    /// <summary>A blow reaches it only half out of the truck, at a stand.</summary>
    public override double MeleeRadius => Mode == HotboxMode.Unfolded ? 1.4 : 0;
    /// <summary>In the running gear it's out of anyone's sight and any ball's line; unfolded at a stand, it's in the open.</summary>
    public override bool Exposed => !Gone && Mode == HotboxMode.Unfolded;
    /// <summary>It stays in its truck until it's dealt with: the director never sends it off for want of company.</summary>
    public override bool StaysAboard => true;

    public HotboxMode Mode => (HotboxMode)(int)Height;
    public double Heat => Extra;
    public int Side => Lateral >= 0 ? 1 : -1;

    /// <summary>
    /// Taking <paramref name="car"/>'s truck, <paramref name="rear"/> or front, on <paramref name="side"/> (−1 left, +1 right):
    /// clamped in among the wheels, knocking.
    /// </summary>
    public static Hotbox In(int id, TrainOnLine train, int car, bool rear, int side, HotboxTuning t)
    {
        var shape = train.Frames[car].Shape;
        double z = (rear ? 1 : -1) * Math.Max(0, shape.HalfLength - t.BogieInset);
        return new Hotbox(id)
        {
            Attached = car,
            Local = new Double3(side * Math.Max(0.2, shape.HalfWidth - t.Inboard), t.AxleHeight, z),
            Lateral = side,
            Height = (double)HotboxMode.Knock,
            Health = t.Health,
        };
    }

    /// <summary>The cars it could take: the engine's rake's, bar the engine, whose axles aren't already seized (by index).</summary>
    public static List<int> Cars(TrainOnLine train) =>
        [.. train.Dynamics.Consist.Vehicles.Where(v => v.Id > 0 && v.Id < train.Frames.Count && !v.Seized).Select(v => v.Id)];

    /// <summary>The stage its heat has it at, clamped in its truck.</summary>
    public static HotboxMode Stage(HotboxTuning t, double heat) =>
        heat < t.KnockSeconds ? HotboxMode.Knock : heat < t.KnockSeconds + t.GlowSeconds ? HotboxMode.Glow : HotboxMode.Seized;

    /// <summary>Knocks a second, once a turn of its wheel (for the cue): the car's speed over the wheel's round.</summary>
    public static double KnocksPerSecond(HotboxTuning t, double speed) => Math.Abs(speed) / (Math.PI * t.WheelDiameter);

    void SetMode(HotboxMode mode)
    {
        if (Mode != mode)
            _modeSeconds = 0;
        Height = (double)mode;
    }

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.Hotbox;
        var train = ctx.Train;
        double dt = SimConstants.TickSeconds;
        _modeSeconds += dt;
        // Off any car (staged loose by a test or an audit), there's no truck to feed on: it waits where it is.
        if (Attached < 0 || Attached >= train.Frames.Count)
            return;
        if (Mode == HotboxMode.Prised)
        {
            // Out of the truck and off into the dark.
            if (_modeSeconds >= t.ScuttleSeconds)
                Enter(ctx, SpinePhase.Gone);
            return;
        }
        double speed = Math.Abs(train.RakeOf(Attached).Speed);
        _stoodFor = speed < t.StoodBelow ? _stoodFor + dt : 0;

        if (Mode == HotboxMode.Unfolded)
        {
            // The train moving again: it folds back into the truck, its heat where it was.
            if (speed >= t.StoodBelow)
            {
                Enter(ctx, SpinePhase.Dormant);
                SetMode(Stage(t, Heat));
                _prise.Clear();
                return;
            }
            if (Prised(ctx, t))
            {
                SetMode(HotboxMode.Prised);
                Enter(ctx, SpinePhase.BreakOff);
                return;
            }
            Snap(ctx, t);
            return;
        }

        // Clamped in its truck, feeding: the faster the wheels turn, the faster it heats.
        if (speed > t.StoodBelow)
            Extra = Heat + dt * speed / t.RefSpeed;
        var stage = Stage(t, Heat);
        SetMode(stage);
        if (stage == HotboxMode.Seized && ctx.World.Authority)
            train.Vehicles[Attached].Seized = true;
        // At a stand a while: half out of the truck, in the open (its bite's wind-up is the telegraph).
        if (_stoodFor >= t.ExposeAfter)
        {
            SetMode(HotboxMode.Unfolded);
            Enter(ctx, SpinePhase.Telegraph);
        }
    }

    /// <summary>
    /// Its bite at whoever's at its head, every <c>snapEvery</c> s (the wind-up the reaction window long, at least): a hurt,
    /// never a kill (App. A.1).
    /// </summary>
    void Snap(EnemyContext ctx, HotboxTuning t)
    {
        if (Phase != SpinePhase.Telegraph || PhaseSeconds < t.SnapEvery)
            return;
        var at = WorldPosition(ctx.Train);
        var near = ctx.LivingCrew().Where(c => (c.World - at).Length <= t.SnapReach).OrderBy(c => (c.World - at).Length).ThenBy(c => c.Player.Id)
            .Select(c => (int?)c.Player.Id).FirstOrDefault();
        if (near is not { } victim || !Enter(ctx, SpinePhase.Commit))
            return;
        ctx.Bite(victim, t.SnapDamage, DeathCause.Taken);
        Enter(ctx, SpinePhase.Telegraph);
    }

    /// <summary>Whether a crewmate has held Use at it, a crowbar or a wrench in hand, for <c>priseSeconds</c>.</summary>
    bool Prised(EnemyContext ctx, HotboxTuning t)
    {
        var at = WorldPosition(ctx.Train);
        foreach (var (p, intent) in ctx.Crew)
        {
            bool prising = p.State.Alive && intent.Has(PlayerButtons.Use) && (PlayerMotor.WorldPosition(p.State, ctx.Train) - at).Length <= t.PriseReach
                && Player.Kit.Held(p.State) is Player.Tool.Crowbar or Player.Tool.Wrench;
            if (!prising)
            {
                _prise.Remove(p.Id);
                continue;
            }
            double held = _prise.GetValueOrDefault(p.Id) + SimConstants.TickSeconds;
            _prise[p.Id] = held;
            if (held >= t.PriseSeconds)
                return true;
        }
        return false;
    }

    /// <summary>Struck: only half out at a stand does a blow land (health in blows); killed, it drops out of the truck.</summary>
    public override void Struck(EnemyContext ctx, int by, double damage)
    {
        if (Mode != HotboxMode.Unfolded)
            return;
        Marked(ctx, by);
        Health -= damage;
        if (Health <= 0)
            Slay(ctx, forTheNight: false);
    }
}

/// <summary>A seized axle repaired (note 367): a wrench held at one of the car's trucks, from beside it.</summary>
public static class Axles
{
    /// <summary>The seized car whose truck this player's at (within <c>repairReach</c>, beside it or under its end), or null.</summary>
    public static int? At(in PlayerState s, TrainOnLine train, HotboxTuning t)
    {
        if (!s.Alive)
            return null;
        var me = PlayerMotor.WorldPosition(s, train);
        for (int car = 1; car < train.Frames.Count; car++)
        {
            if (!train.Vehicles[car].Seized)
                continue;
            var frame = train.Frames[car];
            var shape = frame.Shape;
            foreach (double end in new[] { -1.0, 1.0 })
                foreach (double side in new[] { -1.0, 1.0 })
                {
                    var truck = frame.ToWorld(new Double3(side * shape.HalfWidth, t.AxleHeight, end * Math.Max(0, shape.HalfLength - t.BogieInset)));
                    if ((truck - me).Length <= t.RepairReach)
                        return car;
                }
        }
        return null;
    }
}
