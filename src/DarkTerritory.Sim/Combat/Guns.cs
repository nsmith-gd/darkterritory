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
    /// <summary>The gun this player is standing at, if any: a vehicle with a mount, within reach.</summary>
    public static int? MannedGun(in PlayerState s, TrainOnLine train, GunTuning t)
    {
        if (!s.Alive || s.Parent == PlayerState.World || !s.Grounded)
            return null;
        var shape = train.Frames[s.Parent].Shape;
        if (shape.Gun is not { } mount)
            return null;
        double dx = s.Position.X - mount.Position.X, dz = s.Position.Z - mount.Position.Z;
        return dx * dx + dz * dz <= t.Reach * t.Reach && Math.Abs(s.Position.Y - (mount.Position.Y - 0.9)) < 0.6 ? s.Parent : null;
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
    /// Fires the gun this player is manning, if they're holding Fire and it's ready. Hits the nearest target
    /// along the aim within range, unless the train's own body is in the way first.
    /// </summary>
    public static GunShot? TryFire(in PlayerState s, in PlayerIntent intent, TrainOnLine train, GunTuning t, ref ChoirState choir, ChoirTuning ct,
        IReadOnlyList<HitTarget> targets, uint tick, int shooterId)
    {
        if (!intent.Has(PlayerButtons.Fire) || MannedGun(s, train, t) is not { } gunVehicle)
            return null;
        var vehicle = train.Vehicles[gunVehicle];
        ref var gun = ref vehicle.Gun;
        if (gun.Jammed || gun.Ammo <= 0 || gun.Cooldown > 0)
            return null;
        if (train.BoilerTuning is not null && train.Boiler.Pressure < t.MinPressure)
            return null;
        var frame = train.Frames[gunVehicle];
        var mount = frame.Shape.Gun!.Value;
        var aimLocal = AimLocal(s);
        if (CheckAim(mount, aimLocal, t) != AimResult.Ok)
            return null;

        gun.Ammo--;
        gun.Cooldown = t.TicksPerRound;
        gun.LastShotTick = tick;
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
                v.Gun = new GunState { Ammo = t.Ammo };
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
                if (solid.Part != PartKind.GunMount && RayBox(o, d, solid.Box) is { } t && t < best)
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
