using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Combat;

/// <summary>Something a round can hit: an enemy's hit volume at the time the shooter saw it.</summary>
public readonly record struct HitTarget(int Id, Double3 Position, double Radius);

/// <summary>One round fired this tick.</summary>
/// <param name="HitTargetId">The target struck, or −1.</param>
/// <param name="BlockedByTrain">The round hit the train's own body first (the flank is out of arc by geometry).</param>
public readonly record struct GunShot(int GunVehicle, int Shooter, Double3 Muzzle, Double3 Direction, double Distance, int HitTargetId, bool BlockedByTrain);

public enum AimResult : byte { Ok, OutOfTraverse, DeadZone, PitchLimit }

/// <summary>
/// Mounted guns (spec B.7): stand at the gun and your view is its aim. Fire rate, traverse and a dead
/// zone along the train's own body limit it, and the train's cars stop rounds, so the middle of a long
/// train can't be covered from either end. That's a geometry fact, not a balance number (spec B.7).
/// </summary>
public static class Guns
{
    /// <summary>
    /// Where a vehicle's gun is now, in its frame (T93): on its roof rail at the gun's Z, standing on whatever roof is
    /// there, facing the way it was mounted. Null for a vehicle without one.
    /// </summary>
    public static GunMount? Mount(TrainOnLine train, int vehicle)
    {
        if (vehicle < 0 || vehicle >= train.Vehicles.Count || !train.Vehicles[vehicle].HasGun)
            return null;
        return Mount(train.Frames[vehicle].Shape, train.Vehicles[vehicle].Gun);
    }

    /// <summary>Where a gun in this state stands on a car of this shape (null if it isn't mounted).</summary>
    public static GunMount? Mount(CarShape shape, in GunState g)
    {
        if (!g.Mounted)
            return null;
        double top = shape.TopAt(0, g.Z)?.Top ?? shape.RoofHeight;
        return new GunMount(new Double3(0, top + PivotHeight, g.Z), new Double3(0, 0, g.Facing < 0 ? -1 : 1));
    }

    /// <summary>The pivot's height over the roof it stands on.</summary>
    const double PivotHeight = 0.9;

    /// <summary>The gun this player is standing at, if any: the gun on their car, within reach.</summary>
    public static int? MannedGun(in PlayerState s, TrainOnLine train, GunTuning t)
    {
        if (!s.Alive || s.Parent == PlayerState.World || !s.Grounded)
            return null;
        if (Mount(train, s.Parent) is not { } mount)
            return null;
        double dx = s.Position.X - mount.Position.X, dz = s.Position.Z - mount.Position.Z;
        return dx * dx + dz * dz <= t.Reach * t.Reach && Math.Abs(s.Position.Y - (mount.Position.Y - PivotHeight)) < 0.6 ? s.Parent : null;
    }

    /// <summary>
    /// Pushing the gun you're at along its rail (T93): Use held at it while you walk, and it isn't waiting on a reload (then
    /// Use works the reload). The motor moves it with you (<see cref="Slide"/>).
    /// </summary>
    public static bool Pushing(in PlayerState s, in PlayerIntent intent, TrainOnLine train, GunTuning t) =>
        intent.Has(PlayerButtons.Use) && (Math.Abs(intent.MoveX) > 0.5 || Math.Abs(intent.MoveZ) > 0.5) && !s.Has(PlayerFlags.Held)
        && !s.Has(PlayerFlags.Seated)
        && MannedGun(s, train, t) is { } g && train.Vehicles[g].Gun.ReloadNeeded <= 0;

    /// <summary>
    /// Moves a vehicle's gun along its roof rail by <paramref name="dz"/> (T93). Past the rail's end it goes over the
    /// coupling onto the next car's rail if the two are coupled and that car has no gun; otherwise it stops at the end.
    /// </summary>
    public static void Slide(TrainOnLine train, int vehicle, double dz)
    {
        if (dz == 0 || vehicle < 0 || vehicle >= train.Vehicles.Count || !train.Vehicles[vehicle].HasGun
            || train.Frames[vehicle].Shape.RoofRail is not { } rail)
            return;
        var v = train.Vehicles[vehicle];
        double z = v.Gun.Z + dz;
        if (z >= rail.Front && z <= rail.Back)
        {
            v.Gun.Z = z;
            return;
        }
        // Over the end: the neighbour that way along the rake, coupled, with a rail and room on it.
        var rake = train.RakeOf(vehicle).Consist.Vehicles;
        int at = -1;
        for (int i = 0; i < rake.Count; i++)
            if (rake[i].Id == vehicle)
                at = i;
        int next = z > rail.Back ? at + 1 : at - 1;
        if (next >= 0 && next < rake.Count && rake[next] is { HasGun: false } to && train.Frames[to.Id].Shape.RoofRail is { } onto)
        {
            to.Gun = v.Gun;
            to.Gun.Z = z > rail.Back ? onto.Front : onto.Back;
            v.Gun = default;
            return;
        }
        v.Gun.Z = Math.Clamp(z, rail.Front, rail.Back);
    }

    /// <summary>The yaw (the player's convention, car frame) of a gun's facing: 0 forward along −Z, π back.</summary>
    public static double FacingYaw(GunMount mount) => mount.Facing.Z < 0 ? 0 : Math.PI;

    /// <summary>Which way the barrel points now, in its car's frame: its facing, turned by the traverse, lifted by the elevation.</summary>
    public static Double3 BarrelAim(GunMount mount, in GunState g) => Aim(FacingYaw(mount) + g.Traverse, g.Elevation);

    static Double3 Aim(double yaw, double pitch) =>
        new(-Math.Sin(yaw) * Math.Cos(pitch), Math.Sin(pitch), -Math.Cos(yaw) * Math.Cos(pitch));

    /// <summary>Where the gunner sits (feet, car frame): behind the breech, on the carriage, so it turns with the gun.</summary>
    public static Double3 SeatAt(GunMount mount, in GunState g, GunTuning t, double floor)
    {
        double yaw = FacingYaw(mount) + g.Traverse;
        return new Double3(mount.Position.X + Math.Sin(yaw) * t.SeatBehind, floor, mount.Position.Z + Math.Cos(yaw) * t.SeatBehind);
    }

    /// <summary>
    /// The gun's seat (T112 playtest: "a gun seat with its own controls"), each tick before the gun fires. The Seat press at
    /// a gun sits you in it (only that: a host repeating a lost intent mustn't stand you back up); Jump gets you up (the
    /// motor), and so does the gun going (pushed away, its car cut, you seized). Seated, your
    /// view is held inside the gun's arc and the gun is laid after it, no faster than its carriage turns and its barrel
    /// lifts, and your feet stay on the seat as the carriage turns. Worked alike on host and client, so a client predicts it.
    /// </summary>
    public static void Sit(ref PlayerState s, in PlayerIntent intent, TrainOnLine train, GunTuning t, double dt)
    {
        bool seated = s.Has(PlayerFlags.Seated);
        var gunVehicle = MannedGun(s, train, t);
        bool canSit = gunVehicle is not null && s.Alive && !s.Has(PlayerFlags.Held) && !s.Has(PlayerFlags.Heavy);
        if (seated && !canSit)
        {
            s.Flags &= ~PlayerFlags.Seated;
            return;
        }
        if (!seated && !(canSit && intent.Has(PlayerActions.Seat)))
            return;
        s.Flags |= PlayerFlags.Seated;
        ref var gun = ref train.Vehicles[gunVehicle!.Value].Gun;
        var mount = Mount(train, gunVehicle.Value)!.Value;
        // The view stays inside what the gun can be laid on: its traverse either side of its facing, its pitch limits.
        double half = t.TraverseDegrees / 2 * Math.PI / 180, face = FacingYaw(mount);
        double off = Math.Clamp(Wrap(s.Yaw - face), -half, half);
        s.Yaw = face + off;
        s.Pitch = Math.Clamp(s.Pitch, t.MinPitchDegrees * Math.PI / 180, t.MaxPitchDegrees * Math.PI / 180);
        // The gun after it, at its pace.
        double turn = t.TraverseDegreesPerSecond * Math.PI / 180 * dt, lift = t.ElevateDegreesPerSecond * Math.PI / 180 * dt;
        gun.Traverse += Math.Clamp(off - gun.Traverse, -turn, turn);
        gun.Elevation += Math.Clamp(s.Pitch - gun.Elevation, -lift, lift);
        var seat = SeatAt(mount, gun, t, s.Position.Y);
        s.Position = seat;
        s.Velocity = default;
    }

    /// <summary>Whether a gun's barrel is laid on <paramref name="aim"/> (car frame), within the tuning's tolerance (bots fire on it).</summary>
    public static bool Laid(GunMount mount, in GunState g, Double3 aim, GunTuning t) =>
        Math.Acos(Math.Clamp(Double3.Dot(BarrelAim(mount, g), aim.Normalized), -1, 1)) * 180 / Math.PI <= t.LaidDegrees;

    static double Wrap(double a)
    {
        a %= 2 * Math.PI;
        return a > Math.PI ? a - 2 * Math.PI : a < -Math.PI ? a + 2 * Math.PI : a;
    }

    /// <summary>The player's view direction in their car's frame.</summary>
    public static Double3 AimLocal(in PlayerState s) =>
        new(-Math.Sin(s.Yaw) * Math.Cos(s.Pitch), Math.Sin(s.Pitch), -Math.Cos(s.Yaw) * Math.Cos(s.Pitch));

    /// <summary>Whether a gun can point this way (car frame).</summary>
    public static AimResult CheckAim(GunMount mount, Double3 aim, GunTuning t)
    {
        double pitch = Math.Asin(Math.Clamp(aim.Y, -1, 1)) * 180 / Math.PI;
        if (pitch < t.MinPitchDegrees || pitch > t.MaxPitchDegrees)
            return AimResult.PitchLimit;
        var flat = new Double3(aim.X, 0, aim.Z);
        if (flat.Length < 1e-9)
            return AimResult.PitchLimit;
        flat = flat.Normalized;
        double bearing = Math.Acos(Math.Clamp(Double3.Dot(flat, mount.Facing), -1, 1)) * 180 / Math.PI;
        if (bearing > t.TraverseDegrees / 2)
            return AimResult.OutOfTraverse;
        // The train's body runs away from the gun opposite its facing; nothing fires along it.
        double alongBody = Math.Acos(Math.Clamp(Double3.Dot(flat, mount.Facing * -1), -1, 1)) * 180 / Math.PI;
        return alongBody < t.DeadZoneDegrees ? AimResult.DeadZone : AimResult.Ok;
    }

    /// <summary>
    /// Fires the gun this player is manning, if they're seated at it (T112), holding Fire, and it's ready: where the barrel
    /// points, which the gunner's view leads. Hits the nearest target along it within range, unless the train's own body
    /// is in the way first.
    /// </summary>
    public static GunShot? TryFire(in PlayerState s, in PlayerIntent intent, TrainOnLine train, GunTuning t, ref ChoirState choir, ChoirTuning ct,
        IReadOnlyList<HitTarget> targets, uint tick, int shooterId)
    {
        if (!intent.Has(PlayerButtons.Fire) || !s.Has(PlayerFlags.Seated) || MannedGun(s, train, t) is not { } gunVehicle)
            return null;
        var vehicle = train.Vehicles[gunVehicle];
        ref var gun = ref vehicle.Gun;
        if (gun.Jammed || gun.Ammo <= 0 || gun.Cooldown > 0 || gun.ReloadNeeded > 0)
            return null;
        if (train.BoilerTuning is not null && train.Boiler.Pressure < t.MinPressure)
            return null;
        var frame = train.Frames[gunVehicle];
        var mount = Mount(train, gunVehicle)!.Value;
        var aimLocal = BarrelAim(mount, gun);
        if (CheckAim(mount, aimLocal, t) != AimResult.Ok)
            return null;

        gun.Ammo--;
        gun.Cooldown = t.TicksPerRound;
        gun.LastShotTick = tick;
        // The cannon's full manual reload before the next (GDD v1.1 App. C.3): powder, ball, ram.
        gun.ReloadNeeded = gun.Ammo > 0 ? t.ReloadSteps : 0;
        gun.ReloadProgress = 0;
        choir.RoundFired(ct);

        var muzzle = frame.ToWorld(mount.Position);
        var dir = frame.DirToWorld(aimLocal).Normalized;
        double blocked = TrainRaycast(train, muzzle, dir, t.Range);
        double best = Math.Min(blocked, t.Range);
        int hit = -1;
        foreach (var target in targets)
        {
            if (RaySphere(muzzle, dir, target.Position, target.Radius) is { } d && d < best)
            {
                best = d;
                hit = target.Id;
            }
        }
        return new GunShot(gunVehicle, shooterId, muzzle, dir, best, hit, hit < 0 && blocked <= t.Range);
    }

    /// <summary>
    /// The reload (GDD v1.1 App. C.3 "powder, ball, ram, fire"): Use held at a gun that's been fired works it, a step at a
    /// time, each <see cref="GunTuning.ReloadStepSeconds"/>. Let go and the step starts again. Whoever's at the gun does it;
    /// DESIGN-TODO (Part Eleven Q1): whether a reload needs two players, or is only slower alone.
    /// </summary>
    public static void Reload(in PlayerState s, in PlayerIntent intent, TrainOnLine train, GunTuning t, double dt)
    {
        if (MannedGun(s, train, t) is not { } gunVehicle)
            return;
        ref var gun = ref train.Vehicles[gunVehicle].Gun;
        if (gun.ReloadNeeded <= 0)
            return;
        // Walking with Use held is pushing the gun, not reloading it (T93); but a gun waiting on a reload won't be pushed.
        if (!intent.Has(PlayerButtons.Use) || Math.Abs(intent.MoveX) > 0.5 || Math.Abs(intent.MoveZ) > 0.5)
        {
            gun.ReloadProgress = 0;
            return;
        }
        gun.ReloadProgress += dt;
        if (gun.ReloadProgress >= t.ReloadStepSeconds)
        {
            gun.ReloadProgress = 0;
            gun.ReloadNeeded--;
        }
    }

    /// <summary>Counts down every gun's cooldown. Once per tick.</summary>
    public static void Step(TrainOnLine train)
    {
        foreach (var v in train.Vehicles)
            if (v.Gun.Cooldown > 0)
                v.Gun.Cooldown--;
    }

    /// <summary>Loads every gun with a full belt (spec B.7: 200 rounds, resupply at a POI).</summary>
    public static void Arm(TrainOnLine train, GunTuning t)
    {
        foreach (var v in train.Vehicles)
            if (v.HasGun)
                v.Gun = new GunState { Mounted = true, Z = v.Gun.Z, Facing = v.Gun.Facing, Ammo = t.Ammo };
    }

    /// <summary>Distance along the ray to the first solid of any car, or +∞.</summary>
    public static double TrainRaycast(TrainOnLine train, Double3 origin, Double3 dir, double max)
    {
        double best = double.PositiveInfinity;
        foreach (var frame in train.Frames)
        {
            if ((frame.Origin - origin).Length > max + 40)
                continue;
            var o = frame.ToLocal(origin);
            var d = frame.DirToLocal(dir);
            foreach (var solid in frame.Shape.Solids)
                if (solid.Part != PartKind.GunMount && solid.Present(train.Vehicles[frame.Index]) && RayBox(o, d, solid.Box) is { } t && t < best)
                    best = t;
        }
        return best;
    }

    static double? RayBox(Double3 o, Double3 d, Box b)
    {
        double tmin = 0, tmax = double.PositiveInfinity;
        for (int axis = 0; axis < 3; axis++)
        {
            double oa = axis == 0 ? o.X : axis == 1 ? o.Y : o.Z;
            double da = axis == 0 ? d.X : axis == 1 ? d.Y : d.Z;
            double lo = axis == 0 ? b.Min.X : axis == 1 ? b.Min.Y : b.Min.Z;
            double hi = axis == 0 ? b.Max.X : axis == 1 ? b.Max.Y : b.Max.Z;
            if (Math.Abs(da) < 1e-12)
            {
                if (oa < lo || oa > hi)
                    return null;
                continue;
            }
            double t1 = (lo - oa) / da, t2 = (hi - oa) / da;
            if (t1 > t2)
                (t1, t2) = (t2, t1);
            tmin = Math.Max(tmin, t1);
            tmax = Math.Min(tmax, t2);
            if (tmin > tmax)
                return null;
        }
        return tmin;
    }

    static double? RaySphere(Double3 o, Double3 d, Double3 c, double r)
    {
        var oc = o - c;
        double b = Double3.Dot(oc, d);
        double disc = b * b - (Double3.Dot(oc, oc) - r * r);
        if (disc < 0)
            return null;
        double t = -b - Math.Sqrt(disc);
        return t >= 0 ? t : null;
    }
}
