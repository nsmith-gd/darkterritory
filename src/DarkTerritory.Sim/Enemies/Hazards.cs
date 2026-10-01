using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Enemies;

/// <summary>
/// TRACK DEBRIS (GDD v1.1 §22, a hazard: formerly the Sleepers). Level content, placed at line generation, not a spawn: only
/// the forward lamp reveals it, and hit fast it derails the train. It keeps v1.0's name in the code.
/// <para>SLEEPERS · vibration · forward (v1.0 App. A.2). Lie across the rail shaped like ties. The forward lamp reveals
/// them at 120 m; at 60 m they brace with a wet writhe you can hear. Rule: watch the road.
/// </summary>
public sealed class Sleepers(int id) : Enemy(id)
{
    public override EnemyKind Kind => EnemyKind.Sleepers;
    public override bool Hazard => true;
    public override PressureZone Zone => PressureZone.Forward;
    public override Sense Sense => Sense.Vibration;
    public override bool OnMainLine => true;

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.Sleepers;
        var engine = ctx.Train.Dynamics;
        // Off down a branch past their stretch of main line, the engine can't reach them (it will when it backs out).
        if (!ctx.Train.Line.OnMain(engine.Path, LineDistance))
            return;
        double ahead = LineDistance - engine.Distance;
        if (Phase == SpinePhase.Dormant && (ctx.World.LampShining && ahead <= t.LampRevealDistance || ahead <= t.BraceDistance) && ahead > 0)
            Enter(ctx, SpinePhase.Telegraph);
        if (ahead > 0.5)
            return;

        // The engine is on them. Speed decides.
        double speed = engine.Speed;
        bool fair = Enter(ctx, SpinePhase.Commit);
        var front = ctx.Train.Vehicles[engine.Consist.Vehicles[0].Id];
        if (fair && speed > t.DerailAbove)
        {
            Enter(ctx, SpinePhase.Punish);
            ctx.World.Derail($"onto the Sleepers at {speed:0.0} m/s");
        }
        else if (fair && speed > t.HeavyDamageAbove)
        {
            Enter(ctx, SpinePhase.Punish);
            front.Integrity = Math.Max(0, front.Integrity - t.HeavyDamage);
        }
        else
        {
            front.Integrity = Math.Max(0, front.Integrity - t.MinorDamage);
        }
        Enter(ctx, SpinePhase.Gone);
    }
}

/// <summary>
/// MARSH (GDD v1.1 §22, a hazard: formerly the Drift): "something in the reeds surges toward motion; stand still for ~4s and it
/// loses you." It keeps v1.0's name in the code, and like the fire it's the Territory's own danger: it can kill.
/// <para>THE DRIFT · movement · flank (v1.0 App. A.4). Not a creature but the ground coming up: over marsh and contaminated ground a
/// dark mass rides alongside and over the train, spreading. Anyone moving within it (out on the roofs, on the platforms, on
/// the ballast at a stop; not someone shut in a car) draws it: it surges at them, creeping and rustling, and once it's on
/// them it eats at them for as long as it stays. "Complete stillness ~4s" and it loses them, and drifts on. It "compounds
/// brutally with anything that demands movement", which is most of the roster.
/// </summary>
/// <remarks>Over a car (<see cref="Enemy.Attached"/>, its centre <see cref="Enemy.Local"/> in that car's frame).
/// <see cref="Enemy.Extra"/> is how far it's spread (m); <see cref="Enemy.Extra2"/> who it's after (a player id, 0 for none).</remarks>
public sealed class Drift(int id) : Enemy(id)
{
    public override EnemyKind Kind => EnemyKind.Drift;
    public override bool Hazard => true;
    public override PressureZone Zone => PressureZone.Flank;
    public override Sense Sense => Sense.Movement;

    public double Radius => Extra;
    /// <summary>Who it's surging at or on, or 0.</summary>
    public int Target => (int)Math.Round(Extra2);

    double _age, _still, _bite;
    readonly Dictionary<int, (int Parent, Double3 At)> _last = new();

    /// <summary>Over a car of the train, just spreading.</summary>
    public static Drift Over(int id, TrainOnLine train, int car, DriftTuning t) =>
        new(id) { Attached = car, Local = new Double3(0, train.Frames[car].Shape.RoofHeight, 0), Extra = t.StartRadius };

    /// <summary>The marsh the engine's in or coming to (within the train's own length), if any.</summary>
    public static RouteFeature? Ground(World world, DriftTuning t)
    {
        if (world.Route is not { } route)
            return null;
        var d = world.Train.Dynamics;
        return route.Of(FeatureKind.Marsh).FirstOrDefault(f => d.Distance >= f.Start && d.RearDistance <= f.End + t.ExitMargin);
    }

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.Drift;
        var train = ctx.Train;
        double dt = SimConstants.TickSeconds;
        _age += dt;
        if (Attached < 0 || Attached >= train.Frames.Count || train.Dynamics.Consist.IndexOf(Attached) < 0 || _age >= t.LingerSeconds
            || ctx.World.Route is not null && Ground(ctx.World, t) is null)
        {
            Enter(ctx, SpinePhase.Gone);
            return;
        }
        var centre = WorldPosition(train);
        // Who's moving, in their own frame, this tick (a player riding the train still isn't going anywhere).
        var moving = new HashSet<int>();
        foreach (var (player, _) in ctx.Crew)
        {
            var s = player.State;
            if (!s.Alive)
            {
                _last.Remove(player.Id);
                continue;
            }
            if (_last.TryGetValue(player.Id, out var was) && (was.Parent != s.Parent || ((s.Position - was.At) with { Y = 0 }).Length / dt > t.StillSpeed))
                moving.Add(player.Id);
            _last[player.Id] = (s.Parent, s.Position);
        }
        switch (Phase)
        {
            case SpinePhase.Dormant:
                {
                    // SPREAD, and DETECT: whoever's nearest of those moving within it, and not shut in a car.
                    double spread = t.SpreadSpeed * (Director.Aboard(ctx.World).Contains(CargoKind.Chemicals) ? t.ChemicalSpread : 1);
                    Extra = Math.Min(t.MaxRadius, Extra + spread * dt);
                    var felt = ctx.LivingCrew().Where(c => moving.Contains(c.Player.Id) && PlayerMotor.Space(c.Player.State, train) <= 0
                            && ((c.World - centre) with { Y = 0 }).Length <= Radius)
                        .OrderBy(c => ((c.World - centre) with { Y = 0 }).Length).ThenBy(c => c.Player.Id).FirstOrDefault();
                    if (felt.Player.Id != 0)
                    {
                        Extra2 = felt.Player.Id;
                        _still = 0;
                        Enter(ctx, SpinePhase.Telegraph); // the creeping advance, the rustle
                    }
                    break;
                }
            case SpinePhase.Telegraph:
            case SpinePhase.Punish:
                {
                    int found = ctx.Crew.FindIndex(c => c.Player.Id == Target);
                    if (found < 0 || !ctx.Crew[found].Player.State.Alive || PlayerMotor.Space(ctx.Crew[found].Player.State, train) > 0)
                    {
                        LoseThem(ctx);
                        break;
                    }
                    var at = PlayerMotor.WorldPosition(ctx.Crew[found].Player.State, train);
                    double away = ((at - centre) with { Y = 0 }).Length;
                    // COUNTER: stock still for long enough, and it loses them. Outrun it, and it has.
                    _still = moving.Contains(Target) ? 0 : _still + dt;
                    if (_still >= t.StillSeconds || away > 2 * Math.Max(Radius, t.StartRadius))
                    {
                        LoseThem(ctx);
                        break;
                    }
                    // Toward them (and, once it's on them, with them).
                    Surge(train, at, t.SurgeSpeed * dt);
                    if (Phase == SpinePhase.Telegraph)
                    {
                        if (away <= t.ContactReach && Enter(ctx, SpinePhase.Commit) && Enter(ctx, SpinePhase.Punish))
                            _bite = 0;
                        break;
                    }
                    // CONSUME: while it's on them, a bite each so often; off them (they've got clear), back to coming.
                    if (away > t.ContactReach * 1.5)
                    {
                        Enter(ctx, SpinePhase.BreakOff);
                        Enter(ctx, SpinePhase.Dormant);
                        break;
                    }
                    _bite += dt;
                    if (_bite >= t.DamageSeconds)
                    {
                        _bite = 0;
                        ctx.Harm(Target, t.Damage, DeathCause.Drift);
                    }
                    break;
                }
            default:
                Enter(ctx, SpinePhase.Dormant);
                break;
        }
    }

    void LoseThem(EnemyContext ctx)
    {
        Extra2 = 0;
        _still = 0;
        if (Phase == SpinePhase.Punish)
            Enter(ctx, SpinePhase.BreakOff);
        Enter(ctx, SpinePhase.Dormant);
    }

    /// <summary>Across the ground (and the roofs) toward a point, re-homed onto whichever car of the train it's now nearest.</summary>
    void Surge(TrainOnLine train, Double3 to, double step)
    {
        var from = WorldPosition(train);
        var d = (to - from) with { Y = 0 };
        double len = d.Length;
        var world = len <= step ? from + d : from + d * (step / len);
        int best = Attached;
        double bestD = double.MaxValue;
        foreach (var v in train.Dynamics.Consist.Vehicles)
        {
            double far = ((train.Frames[v.Id].Origin - world) with { Y = 0 }).Length;
            if (far < bestD)
                (best, bestD) = (v.Id, far);
        }
        Attached = best;
        var local = train.Frames[best].ToLocal(world);
        Local = local with { Y = train.Frames[best].Shape.RoofHeight };
    }
}
