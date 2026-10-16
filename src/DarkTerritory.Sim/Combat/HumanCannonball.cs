using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Combat;

/// <summary>
/// The human cannonball (note 746; docs/design/upgrades.md §8.3; the director, 15 Oct 2026: "the reload flow to be powder,
/// pack, then either a ball or a player jumps in"). On the guard van's gun, with <c>dress:humanCannonball</c> fitted, the
/// reload's last step is a ball or a crewmate: packed (one step left), a crewmate at the muzzle holds Use and climbs in. Only a
/// living crewmate, and only themselves; Jump climbs out any time before the shot. Run in each crewmate's own act, so their
/// client predicts it as the host has it.
/// </summary>
public static class HumanCannonball
{
    /// <summary>The upgrade that fits it (campaign.json <c>humanCannonball</c>).</summary>
    public const string Dress = "humanCannonball";

    /// <summary>Whether this vehicle's gun takes a crewmate: the guard van's, mounted, with the upgrade fitted.</summary>
    public static bool Takes(TrainOnLine train, int vehicle) =>
        vehicle > 0 && vehicle < train.Vehicles.Count && train.Vehicles[vehicle] is { Kind: VehicleKind.Guard, HasGun: true, Gun.Mounted: true }
        && train.Dynamics.Tuning.Composition.Dressed(Dress);

    /// <summary>The gun's muzzle in its car's frame: <see cref="CannonballTuning.Muzzle"/> m out along the barrel from the pivot.</summary>
    public static Double3 Muzzle(TrainOnLine train, int vehicle, CannonballTuning t)
    {
        var mount = Guns.Mount(train, vehicle)!.Value;
        return mount.Position + Guns.BarrelAim(mount, train.Vehicles[vehicle].Gun) * t.Muzzle;
    }

    /// <summary>
    /// The gun this crewmate is climbing into (or could, holding Use): at the guard van's gun's muzzle, nearer it than the
    /// pivot and within <see cref="CannonballTuning.ClimbReach"/>, standing on the car, with the gun packed and waiting on its
    /// ball. Null otherwise.
    /// </summary>
    public static int? AtTheMuzzle(in PlayerState s, TrainOnLine train, GunTuning guns, CannonballTuning t)
    {
        if (!s.Alive || s.Parent <= 0 || !s.Grounded || s.Has(PlayerFlags.Seated) || s.Has(PlayerFlags.Held) || s.Has(PlayerFlags.Heavy)
            || s.Has(PlayerFlags.InCannon) || !Takes(train, s.Parent))
            return null;
        var gun = train.Vehicles[s.Parent].Gun;
        if (gun.Jammed || gun.ReloadNeeded != 1 || guns.ReloadSteps < 2)
            return null;
        var mount = Guns.Mount(train, s.Parent)!.Value;
        var muzzle = Muzzle(train, s.Parent, t);
        double toMuzzle = Flat(s.Position - muzzle).Length, toPivot = Flat(s.Position - mount.Position).Length;
        return toMuzzle <= t.ClimbReach && toMuzzle < toPivot ? s.Parent : null;
    }

    /// <summary>
    /// A crewmate's act at the guard van's gun (note 746): in the barrel, held there (Jump, or the gun gone, climbs out and the
    /// ball's wanted again); at its muzzle holding Use, the climb, on the gun's reload clock (<see cref="GunState.ReloadProgress"/>,
    /// so a let-go starts it again as the ball's step would). Returns the gun whose clock it worked this tick, for the world to
    /// count as worked (and not as the ball's step), or null.
    /// </summary>
    public static int? Climb(ref PlayerState s, in PlayerIntent intent, TrainOnLine train, GunTuning guns, CannonballTuning t, double dt)
    {
        if (s.Has(PlayerFlags.InCannon))
        {
            if (!s.Alive || intent.Has(PlayerButtons.Jump) || !Takes(train, s.Parent))
            {
                ClimbOut(ref s, train, t);
                return null;
            }
            // In the barrel, behind the muzzle, facing out along it (feet on the roof under it: the art sits them in the barrel).
            var mount = Guns.Mount(train, s.Parent)!.Value;
            var aim = Guns.BarrelAim(mount, train.Vehicles[s.Parent].Gun);
            var inside = mount.Position + aim * (t.Muzzle * 0.5);
            s.Position = new Double3(inside.X, mount.Position.Y - 0.9, inside.Z);
            s.Velocity = default;
            s.Yaw = DMath.Atan2(-aim.X, -aim.Z);
            return null;
        }
        if (!intent.Has(PlayerButtons.Use) || Math.Abs(intent.MoveX) > 0.5 || Math.Abs(intent.MoveZ) > 0.5 || AtTheMuzzle(s, train, guns, t) is not { } g)
            return null;
        ref var gun = ref train.Vehicles[g].Gun;
        gun.ReloadProgress += dt;
        if (gun.ReloadProgress >= t.ClimbSeconds)
        {
            gun.ReloadProgress = 0;
            gun.ReloadNeeded = 0;
            s.Flags |= PlayerFlags.InCannon;
            s.Velocity = default;
        }
        return g;
    }

    /// <summary>Out of the barrel (Jump, or the gun gone): stood at the muzzle again, and the gun's waiting on its ball.</summary>
    static void ClimbOut(ref PlayerState s, TrainOnLine train, CannonballTuning t)
    {
        s.Flags &= ~PlayerFlags.InCannon;
        if (s.Parent > 0 && s.Parent < train.Vehicles.Count && Guns.Mount(train, s.Parent) is { } mount)
        {
            ref var gun = ref train.Vehicles[s.Parent].Gun;
            if (gun.ReloadNeeded == 0)
                gun.ReloadNeeded = 1;
            var muzzle = Muzzle(train, s.Parent, t);
            s.Position = new Double3(muzzle.X, mount.Position.Y - 0.9, muzzle.Z);
        }
        s.Velocity = default;
    }

    /// <summary>A crewmate fired (note 746): who, by whom, from where (the world), at what velocity, to land where.</summary>
    public readonly record struct Launch(int Who, int By, int Gun, Double3 From, Double3 Velocity, Double3 Landing);

    /// <summary>
    /// The seated gunner's Fire at the guard van's gun with a crewmate in it (note 746): the same gates as a round (ready, cooled,
    /// steam, the aim in its arc) and the same reload after, but no ball: the crewmate, to a landing point along the aim
    /// (<see cref="LandingPoint"/>). Null otherwise; a gun with nobody in it fires its ball (<see cref="Guns.TryFire"/>).
    /// </summary>
    public static Launch? TryFire(in PlayerState s, in PlayerIntent intent, TrainOnLine train, GunTuning guns, CannonballTuning t,
        ref ChoirState choir, ChoirTuning ct, uint tick, int shooter, IEnumerable<(int Id, PlayerState State)> crew, double gravity)
    {
        if (!intent.Has(PlayerButtons.Fire) || !s.Has(PlayerFlags.Seated) || Guns.MannedGun(s, train, guns) is not { } g || !Takes(train, g)
            || Inside(g, crew) is not { } who)
            return null;
        ref var gun = ref train.Vehicles[g].Gun;
        if (gun.Jammed || Guns.Ready(gun, guns) <= 0 || gun.Cooldown > 0 || gun.ReloadNeeded > 0)
            return null;
        if (train.BoilerTuning is not null && train.Boiler.Pressure < guns.MinPressure)
            return null;
        var frame = train.Frames[g];
        var mount = Guns.Mount(train, g)!.Value;
        var aimLocal = Guns.BarrelAim(mount, gun);
        if (Guns.CheckAim(mount, aimLocal, guns) != AimResult.Ok)
            return null;
        gun.Cooldown = guns.TicksPerRound;
        gun.LastShotTick = tick;
        gun.ReloadNeeded = Guns.Ready(gun, guns) > 0 ? guns.ReloadSteps : 0;
        gun.ReloadProgress = 0;
        choir.RoundFired(ct, 1);
        var from = frame.ToWorld(Muzzle(train, g, t));
        var aim = frame.DirToWorld(aimLocal);
        var level = new Double3(aim.X, 0, aim.Z);
        level = level.Length > 1e-6 ? level.Normalized : frame.DirToWorld(mount.Facing);
        var landing = LandingPoint(train, from, level, t);
        // Level at speed to it, and up enough that gravity alone (the motor's air step) brings them down there.
        double across = Flat(landing - from).Length, time = Math.Max(0.4, across / t.Speed);
        var velocity = Flat(landing - from) * (1 / time) + Double3.Up * ((landing.Y - from.Y + 0.5 * gravity * time * time) / time);
        return new Launch(who, shooter, g, from, velocity, landing);
    }

    /// <summary>
    /// Where a crewmate fired along <paramref name="level"/> from <paramref name="origin"/> comes down (note 746, the director: "Don't
    /// ever allow a player to be fired off the map"): <see cref="CannonballTuning.Range"/> m out, stepped back toward the gun
    /// until it's dry ground beside the line (not water, within the map's width and the line's room there, between its ends).
    /// The ground's height there.
    /// </summary>
    public static Double3 LandingPoint(TrainOnLine train, Double3 origin, Double3 level, CannonballTuning t)
    {
        var line = train.Line;
        double hint = train.Dynamics.Distance;
        for (double d = t.Range; d >= t.Step; d -= t.Step)
        {
            var p = origin + level * d;
            var (path, along) = line.Nearest(p, ref hint);
            if (along <= 1 || along >= line.PathLength(path) - 1)
                continue;
            var rail = line.Sample(path, along).Position;
            double off = Flat(p - rail).Length;
            if (off > t.MapHalfWidth || line.Conditions is { } c && off > c.LateralRoom(path, along))
                continue;
            double ground = PlayerMotor.GroundAt(p, line, ref hint);
            if (Guns.Water(train, p) is { } w && w > ground)
                continue;
            return p with { Y = ground };
        }
        // Nowhere out there: down at the gun's foot, beside the van.
        double g0 = PlayerMotor.GroundAt(origin, line, ref hint);
        return origin with { Y = g0 };
    }

    /// <summary>
    /// The host's: a launch put on its crewmate (note 746), if they're still in the gun: out of it, in the world's frame at the
    /// muzzle, flying, <see cref="PlayerFlags.Fired"/>.
    /// </summary>
    public static bool Fire(ref PlayerState s, in Launch launch)
    {
        if (!s.Alive || !s.Has(PlayerFlags.InCannon))
            return false;
        s.Flags = (s.Flags & ~(PlayerFlags.InCannon | PlayerFlags.Seated | PlayerFlags.Crouched)) | PlayerFlags.Fired;
        s.Parent = PlayerState.World;
        s.Position = launch.From;
        s.Velocity = launch.Velocity;
        s.Surface = Surface.Air;
        return true;
    }

    /// <summary>
    /// In the air from the cannon (note 746, the director: "The fired player needs to be facing the impact location when they
    /// are flying through the air"): the view on where they'll come down, from where they are and how they're going, the
    /// ground under them for its height; the same on every machine.
    /// </summary>
    public static void Face(ref PlayerState s, TrainOnLine train, double gravity)
    {
        var at = s.Parent == PlayerState.World ? s.Position : train.Frames[s.Parent].ToWorld(s.Position);
        var v = s.Velocity;
        double hint = train.Dynamics.Distance;
        double ground = PlayerMotor.GroundAt(at, train.Line, ref hint);
        // When it reaches that height again: y + vy t - g t² / 2 = ground.
        double a = 0.5 * gravity, b = -v.Y, c = ground - at.Y;
        double disc = b * b - 4 * a * c;
        double time = disc > 0 ? (-b + Math.Sqrt(disc)) / (2 * a) : 0.5;
        var landing = new Double3(at.X + v.X * time, ground, at.Z + v.Z * time);
        var to = landing - at;
        if (Flat(to).Length < 1e-3)
            return;
        s.Yaw = DMath.Atan2(-to.X, -to.Z);
        s.Pitch = DMath.Atan2(to.Y, Flat(to).Length);
    }

    /// <summary>Who's in this vehicle's gun, of these crewmates, or null.</summary>
    public static int? Inside(int vehicle, IEnumerable<(int Id, PlayerState State)> crew) =>
        crew.Where(c => c.State.Has(PlayerFlags.InCannon) && c.State.Parent == vehicle).Select(c => (int?)c.Id).FirstOrDefault();

    static Double3 Flat(Double3 v) => new(v.X, 0, v.Z);
}
