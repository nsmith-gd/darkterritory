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

    /// <summary>Who's in this vehicle's gun, of these crewmates, or null.</summary>
    public static int? Inside(int vehicle, IEnumerable<(int Id, PlayerState State)> crew) =>
        crew.Where(c => c.State.Has(PlayerFlags.InCannon) && c.State.Parent == vehicle).Select(c => (int?)c.Id).FirstOrDefault();

    static Double3 Flat(Double3 v) => new(v.X, 0, v.Z);
}
