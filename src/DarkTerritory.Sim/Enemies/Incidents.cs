using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Enemies;

/// <summary>
/// Trouble inside the cars (after the 100-night playtest: "more problems players need to face in cars, and reasons not
/// to roof walk the whole time; defeatable, but able to hurt or kill"). Each takes a cargo car, is told by sound through
/// its walls, and is answered from inside it, on the car's floor, by holding Use at it. Cut the car loose and it goes
/// with it. None can be shot. They run on the shared spine, so the fairness rule holds: the commit waits on a telegraph.
/// </summary>
public abstract class Incident(int id) : Enemy(id)
{
    public override PressureZone Zone => PressureZone.Interior;

    /// <summary>Where on the car's floor it is answered from: the cargo stack's face, beside the aisle.</summary>
    public static Double3 At(TrainOnLine train, int car, double along)
    {
        var layout = train.Dynamics.Tuning.Geometry.Interior!;
        var shape = train.Frames[car].Shape;
        double half = shape.HalfLength;
        // The cargo's stacked down the right-hand side; its face is in reach of the aisle.
        double face = shape.HalfWidth - layout.WallThickness - layout.CargoDepth;
        return new Double3(face, layout.FloorHeight, Math.Clamp(along, -half + 2, half - 2));
    }

    /// <summary>The crew on this car's floor, inside its walls, and whether they're holding Use within reach of it.</summary>
    protected IEnumerable<(Net.PlayerSnapshot Player, bool Working)> Inside(EnemyContext ctx, double reach)
    {
        foreach (var (p, intent) in ctx.Crew)
        {
            var s = p.State;
            if (!s.Alive || s.Parent != Attached || !PlayerMotor.Indoors(s, ctx.Train) || PlayerMotor.InCab(s, ctx.Train))
                continue;
            double dx = s.Position.X - Local.X, dz = s.Position.Z - Local.Z;
            bool near = dx * dx + dz * dz <= reach * reach;
            yield return (p, near && intent.Has(PlayerButtons.Use) && intent.MoveZ <= 0.5);
        }
    }

    /// <summary>Still coupled to the engine: cut loose, it's the Territory's problem now.</summary>
    protected bool Aboard(EnemyContext ctx) =>
        Attached > 0 && Attached < ctx.Train.Frames.Count && ctx.Train.Dynamics.Consist.Vehicles.Any(v => v.Id == Attached);

    /// <summary>The cargo car either side of this one without one of these in it already, if there is one.</summary>
    protected int? Neighbour(EnemyContext ctx)
    {
        var train = ctx.Train;
        foreach (int car in new[] { train.VehicleBehind(Attached), train.VehicleAhead(Attached) })
            if (car > 0 && train.Vehicles[car].Kind == VehicleKind.Cargo && !ctx.World.ActiveEnemies.Any(e => !e.Gone && e.Kind == Kind && e.Attached == car))
                return car;
        return null;
    }

    protected void Out(EnemyContext ctx)
    {
        Enter(ctx, SpinePhase.BreakOff);
        Enter(ctx, SpinePhase.Gone);
    }
}

/// <summary>
/// CAR FIRE · heat · interior. Cinders from the stack, or a lamp gone over, and a cargo car catches: smoke and a crackle
/// through its walls (the tell, 5–9 kHz), then flames. It burns the cargo and the car, and whoever's in it; left at full
/// blaze, it takes the next car. Beaten out from inside (Use at it: faster with two), which scorches whoever does it once
/// it's well alight. Rule: get in there while it's smoke.
/// </summary>
/// <remarks><see cref="Enemy.Extra"/> is how far it's gone, 0 to 1 (replicated: the flames and the sound follow it).</remarks>
public sealed class CarFire(int id) : Incident(id)
{
    double _burnTimer, _blaze;

    public override EnemyKind Kind => EnemyKind.CarFire;
    public override Sense Sense => Sense.Heat;

    public static CarFire In(int id, TrainOnLine train, int car, double along, CarFireTuning t) =>
        new(id) { Attached = car, Local = At(train, car, along), Extra = t.StartIntensity };

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.CarFire;
        if (!Aboard(ctx))
        {
            Enter(ctx, SpinePhase.Gone);
            return;
        }
        double dt = SimConstants.TickSeconds;
        if (Phase == SpinePhase.Dormant)
            Enter(ctx, SpinePhase.Telegraph); // smoke through the boards, and the crackle
        var crew = Inside(ctx, t.BeatReach).ToList();
        int beating = crew.Count(c => c.Working);
        Extra = Math.Clamp(Extra + (t.GrowPerSecond - t.BeatPerSecond * beating) * dt, 0, 1);
        if (Extra <= 0)
        {
            Out(ctx);
            return;
        }
        if (Phase == SpinePhase.Telegraph && Extra >= t.BurnFrom && Enter(ctx, SpinePhase.Commit))
            Enter(ctx, SpinePhase.Punish); // alight
        if (Phase != SpinePhase.Punish)
            return;
        var car = ctx.Train.Vehicles[Attached];
        car.CargoIntegrity = Math.Max(0, car.CargoIntegrity - t.CargoPerSecond * Extra * dt);
        car.Integrity = Math.Max(0, car.Integrity - t.IntegrityPerSecond * Extra * dt);
        _burnTimer += dt;
        if (_burnTimer >= t.BurnEverySeconds)
        {
            _burnTimer = 0;
            foreach (var (p, working) in crew)
            {
                // Beating it back you're at its edge; standing about in the car, you're in the smoke and the heat.
                int amount = working ? (Extra >= t.ScorchAbove ? t.ScorchDamage : 0) : (int)Math.Round(t.BurnDamage * Extra);
                if (amount > 0)
                    ctx.Bite(p.Id, amount, DeathCause.Burned);
            }
        }
        _blaze = Extra >= 1 ? _blaze + dt : 0;
        Extra2 = _blaze;
        if (_blaze >= t.SpreadSeconds && Neighbour(ctx) is { } next)
        {
            _blaze = 0;
            ctx.World.AddEnemy(i => In(i, ctx.Train, next, Local.Z, t));
        }
    }
}

/// <summary>
/// LOOSE LOAD · vibration · interior. A lashing's parted in a cargo car: the stack creaks and groans against its straps
/// in time with the rail joints (the tell, 700 Hz–1.4 kHz, from inside that car). Brake hard, snatch the slack, or give
/// it a minute, and it comes down across the aisle: whoever's beside it is crushed, and the cargo's broken. Re-lashed from
/// inside (Use at the stack for a few seconds). Rule: don't brake on a loose load, or get in and lash it.
/// </summary>
/// <remarks><see cref="Enemy.Extra"/>: seconds of lashing done. <see cref="Enemy.Extra2"/>: the train's speed last tick.</remarks>
public sealed class LooseLoad(int id) : Incident(id)
{
    public override EnemyKind Kind => EnemyKind.LooseLoad;
    public override Sense Sense => Sense.Vibration;

    public static LooseLoad In(int id, TrainOnLine train, int car, double along) =>
        new(id) { Attached = car, Local = At(train, car, along), Extra2 = train.Dynamics.Speed };

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.LooseLoad;
        if (!Aboard(ctx))
        {
            Enter(ctx, SpinePhase.Gone);
            return;
        }
        double dt = SimConstants.TickSeconds;
        if (Phase == SpinePhase.Dormant)
            Enter(ctx, SpinePhase.Telegraph); // the creak against the straps
        var crew = Inside(ctx, t.LashReach).ToList();
        int lashing = crew.Count(c => c.Working);
        Extra = lashing > 0 ? Extra + lashing * dt : Math.Max(0, Extra - dt);
        if (Extra >= t.LashSeconds)
        {
            Out(ctx);
            return;
        }
        double speed = ctx.Train.Dynamics.Speed;
        double lurch = Math.Abs(speed - Extra2) / dt;
        Extra2 = speed;
        if (Phase == SpinePhase.Telegraph && (lurch >= t.LurchAccel || PhaseSeconds >= t.SnapSeconds) && Enter(ctx, SpinePhase.Commit))
        {
            Enter(ctx, SpinePhase.Punish);
            // Down across the aisle: anyone beside it.
            foreach (var (p, _) in crew)
                if (Math.Abs(p.State.Position.Z - Local.Z) <= t.SlideReach)
                    ctx.Bite(p.Id, t.CrushDamage, DeathCause.Crushed);
            var car = ctx.Train.Vehicles[Attached];
            car.CargoIntegrity = Math.Max(0, car.CargoIntegrity - t.Breakage);
            Out(ctx);
        }
    }
}

/// <summary>
/// GNAWERS · scent · interior. Something's nested in a cargo car's load: skittering and chittering high up through the
/// walls (the tell, 7–10 kHz, nothing else is up there). They eat the cargo and they breed; once they're out of the
/// crates they go for anyone in the car. Stamped out from inside (Use at the nest: faster with two), and they bite
/// whoever's doing it. Left at their full number they get into the next car. Rule: stamp them out early.
/// </summary>
/// <remarks><see cref="Enemy.Health"/> is how many, 0 to 1 (replicated).</remarks>
public sealed class Gnawers(int id) : Incident(id)
{
    double _biteTimer, _full;

    public override EnemyKind Kind => EnemyKind.Gnawers;
    public override Sense Sense => Sense.Scent;

    public static Gnawers In(int id, TrainOnLine train, int car, double along, GnawerTuning t) =>
        new(id) { Attached = car, Local = At(train, car, along), Health = t.StartSwarm };

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.Gnawers;
        if (!Aboard(ctx))
        {
            Enter(ctx, SpinePhase.Gone);
            return;
        }
        double dt = SimConstants.TickSeconds;
        if (Phase == SpinePhase.Dormant)
            Enter(ctx, SpinePhase.Telegraph); // the chittering in the load
        var crew = Inside(ctx, t.Reach).ToList();
        int stamping = crew.Count(c => c.Working);
        Health = Math.Clamp(Health + (t.BreedPerSecond - t.StampPerSecond * stamping) * dt, 0, 1);
        if (Health <= 0)
        {
            Out(ctx);
            return;
        }
        var car = ctx.Train.Vehicles[Attached];
        car.CargoIntegrity = Math.Max(0, car.CargoIntegrity - t.EatPerSecond * Health * dt);
        if (Phase == SpinePhase.Telegraph && PhaseSeconds >= t.OutAfterSeconds && Enter(ctx, SpinePhase.Commit))
            Enter(ctx, SpinePhase.Punish); // out of the crates
        if (Phase == SpinePhase.Punish)
        {
            _biteTimer += dt;
            if (_biteTimer >= t.BiteEverySeconds)
            {
                _biteTimer = 0;
                foreach (var (p, _) in crew)
                    if ((int)Math.Round(t.BiteDamage * Health) is > 0 and var amount)
                        ctx.Bite(p.Id, amount, DeathCause.Gnawed);
            }
        }
        _full = Health >= 1 ? _full + dt : 0;
        if (_full >= t.SpreadSeconds && Neighbour(ctx) is { } next)
        {
            _full = 0;
            ctx.World.AddEnemy(i => In(i, ctx.Train, next, Local.Z, t));
        }
    }
}
