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

    /// <summary>
    /// The cargo car either side of this one without one of these in it already, if there is one, and if there's room for
    /// another of its kind (<paramref name="max"/> about at once: one left to itself doesn't take the whole train).
    /// </summary>
    protected int? Neighbour(EnemyContext ctx, int max)
    {
        var train = ctx.Train;
        if (ctx.World.ActiveEnemies.Count(e => !e.Gone && e.Kind == Kind) >= max)
            return null;
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
/// FIRE (GDD v1.1 App. C.5, and what the Fire Flies start, App. A.5). A cargo car catches: smoke and a crackle through its
/// walls (the tell), then flames. It burns the cargo and the car, and whoever's in it, and it grows and jumps the couplings
/// over time. Every car has a wall-mounted extinguisher: grab it, spray it (fire held with it in your hands, inside the
/// car), put it back to recharge. Each holds a limited charge. A big fire grows faster than one extinguisher can put out,
/// so it takes several at once. Chemicals spread it faster; gunpowder, coal and timber escalate it faster (App. B.9). Or
/// cut the car loose. Rule: grab the extinguishers or abandon it. A car of gunpowder and shot at full blaze goes up (§19
/// "explodes"; note 182): the car's gone, everyone near is hurt or killed, and the cars either side catch.
/// </summary>
/// <remarks><see cref="Enemy.Extra"/> is how far it's gone, 0 to 1 (replicated: the flames and the sound follow it).</remarks>
public sealed class CarFire(int id) : Incident(id)
{
    double _burnTimer, _blaze;
    bool _spread, _exploded;

    public override EnemyKind Kind => EnemyKind.CarFire;
    public override Sense Sense => Sense.Heat;
    public override Want Want => Want.Cargo;

    /// <summary>
    /// Alight: what set it is the record (the Fire Flies', note 190), and what it does after is the deaths and the cars lost.
    /// </summary>
    protected override Run.Incident? Punished(EnemyContext ctx) => null;

    public static CarFire In(int id, TrainOnLine train, int car, double along, CarFireTuning t) =>
        new(id) { Attached = car, Local = At(train, car, along), Extra = t.StartIntensity };

    /// <summary>Crew spraying it: inside, in reach, an extinguisher with charge in their hands, fire held.</summary>
    List<(int Player, Physics.Body Extinguisher)> Spraying(EnemyContext ctx, double reach)
    {
        var list = new List<(int, Physics.Body)>();
        foreach (var (p, intent) in ctx.Crew)
        {
            var s = p.State;
            if (!s.Alive || s.Parent != Attached || !PlayerMotor.Indoors(s, ctx.Train) || !intent.Has(PlayerButtons.Fire))
                continue;
            if (ctx.World.Bodies.CarriedBy(p.Id) is not { Kind: Physics.BodyKind.Extinguisher, Charge: > 0 } ext)
                continue;
            double dx = s.Position.X - Local.X, dz = s.Position.Z - Local.Z;
            if (dx * dx + dz * dz <= reach * reach)
                list.Add((p.Id, ext));
        }
        return list;
    }

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
        var car = ctx.Train.Vehicles[Attached];
        var spraying = Spraying(ctx, t.SprayReach);
        foreach (var (_, ext) in spraying)
            ext.Charge = Math.Max(0, ext.Charge - dt / t.ChargeSeconds);
        // It grows, faster the bigger it is and the worse the cargo; nothing left in the car to burn, it dies down.
        double fuel = car.Cargo is CargoKind.Ammunition ? t.PowderGrowth : car.Cargo is CargoKind.Chemicals ? t.ChemicalGrowth
            : Cargoes.Fuel(car.Cargo) ? t.FuelGrowth : 1;
        double grow = car.CargoIntegrity <= 0.02 ? -t.BurnOutPerSecond : (t.GrowPerSecond + t.GrowWithSize * Extra) * fuel;
        Extra = Math.Clamp(Extra + (grow - t.SprayPerSecond * spraying.Count) * dt, 0, 1);
        if (Extra <= 0)
        {
            Out(ctx);
            return;
        }
        if (Phase == SpinePhase.Telegraph && Extra >= t.BurnFrom && Enter(ctx, SpinePhase.Commit))
            Enter(ctx, SpinePhase.Punish); // alight
        if (Phase != SpinePhase.Punish)
            return;
        if (!_exploded && car.Cargo == CargoKind.Ammunition && car.Load > 0.01 && car.CargoIntegrity > 0.02 && Extra >= t.ExplodeAt - 1e-9)
        {
            Explode(ctx, car, t);
            return;
        }
        car.CargoIntegrity = Math.Max(0, car.CargoIntegrity - t.CargoPerSecond * Extra * dt);
        car.Integrity = Math.Max(0, car.Integrity - t.IntegrityPerSecond * Extra * dt);
        _burnTimer += dt;
        if (_burnTimer >= t.BurnEverySeconds)
        {
            _burnTimer = 0;
            foreach (var (p, _) in Inside(ctx, t.SprayReach))
            {
                // The fire's the train's own danger (like a fall): it can kill. Down the far end of the car you're clear of it.
                if (Math.Abs(p.State.Position.Z - Local.Z) > t.BurnReach)
                    continue;
                int amount = (int)Math.Round(t.BurnDamage * Extra);
                if (amount > 0)
                    ctx.Harm(p.Id, amount, DeathCause.Burned);
            }
        }
        // Chemicals spread it faster (B.9), and so do coal and timber: "fire cascades escalate faster".
        _blaze = Extra >= t.SpreadFrom ? _blaze + dt * (car.Cargo == CargoKind.Chemicals ? t.ChemicalSpread : Cargoes.Fuel(car.Cargo) ? t.FuelSpread : 1) : 0;
        Extra2 = _blaze;
        if (!_spread && _blaze >= t.SpreadSeconds && Neighbour(ctx, t.MaxActive) is { } next)
        {
            _spread = true;
            ctx.World.AddEnemy(i => In(i, ctx.Train, next, Local.Z, t));
        }
    }

    /// <summary>
    /// A powder car at full blaze goes up (GDD §19 "explodes", App. B.9 "every fire is worse"; note 182). Its cargo and the car
    /// are gone; everyone within <see cref="CarFireTuning.ExplodeKillRadius"/> of the fire takes the full blast, out to
    /// <see cref="CarFireTuning.ExplodeRadius"/> less with distance (walls or not: it's a powder car); the cars either side
    /// catch whatever's already burning. Every client sees and hears it as a blast where it was.
    /// </summary>
    void Explode(EnemyContext ctx, Vehicle car, CarFireTuning t)
    {
        _exploded = true;
        var train = ctx.Train;
        var at = train.Frames[Attached].ToWorld(Local + Double3.Up * 1.0);
        car.CargoIntegrity = 0;
        car.Integrity = 0;
        foreach (var c in ctx.LivingCrew())
        {
            double d = (c.World + Double3.Up * 1.0 - at).Length;
            if (d > t.ExplodeRadius)
                continue;
            double share = d <= t.ExplodeKillRadius ? 1 : 1 - (d - t.ExplodeKillRadius) / Math.Max(1e-6, t.ExplodeRadius - t.ExplodeKillRadius);
            int amount = (int)Math.Round(t.ExplodeDamage * share);
            if (amount > 0)
                ctx.Harm(c.Player.Id, amount, DeathCause.Exploded);
        }
        foreach (int next in new[] { train.VehicleBehind(Attached), train.VehicleAhead(Attached) })
            if (next > 0 && train.Vehicles[next].Kind == VehicleKind.Cargo && train.Dynamics.Consist.IndexOf(next) >= 0
                && !ctx.World.ActiveEnemies.Any(e => !e.Gone && e.Kind == Kind && e.Attached == next))
                ctx.World.AddEnemy(i => In(i, train, next, Local.Z, t));
        ctx.World.Blast(at);
        Extra = Math.Min(Extra, t.SpreadFrom);
    }
}
