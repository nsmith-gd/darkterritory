using Ballast;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Bots;

/// <summary>
/// Bots bring the powder (note 377, orchestrator.md §5.1 U4: "a gunner and a walker bringing the powder"). A gun fires what's
/// in its ready rack (note 374); the rest is down in the guard van's powder locker. A manned gun whose rack is low
/// (<see cref="GunTuning.FeedAt"/>) sends one walker for a charge, the engine's forward gun too (the long walk down the
/// train), and the walker fills it from beside the gun while the gunner keeps the seat.
/// <para>
/// <b>Who goes</b> is worked out alike by every bot from what each sees, with nobody calling it (as <see cref="KitCarry"/>):
/// whoever already has a charge in hand, or else the lowest player id among the bots free to go (out on the roofs, or already
/// at the locker; never the cab's, nor a gunner in its seat), so two never take turns at it.
/// </para>
/// </summary>
public static class PowderCarry
{
    /// <summary>
    /// The gun that wants powder brought to it: a crewmate seated at it, its rack at <see cref="GunTuning.FeedAt"/> or fewer,
    /// and powder in the lockers. Of several, the emptier, then the nearer the engine. Null with none.
    /// </summary>
    /// <param name="firing">Whether a gun's in action (a shot in the last <see cref="GunTuning.FeedWhileFiring"/> s, as the
    /// bot asking has seen it: <see cref="ShotWatch"/>); without it, none is.</param>
    public static int? Wanting(World world, IEnumerable<PlayerState> crew, GunTuning t, Func<int, bool>? firing = null)
    {
        var train = world.Train;
        if (t.Rack <= 0 || t.FeedAt < 0 || Guns.Stowed(train, t) <= 0)
            return null;
        int? best = null;
        int bestReady = int.MaxValue, bestAt = int.MaxValue;
        foreach (var s in crew)
        {
            if (!s.Alive || !s.Has(PlayerFlags.Seated) || Guns.MannedGun(s, train, t) is not { } gun)
                continue;
            int ready = Guns.Ready(train.Vehicles[gun].Gun, t), at = train.Dynamics.Consist.IndexOf(gun);
            // In action, any round short; otherwise low (feedAt; feedAtFar further off).
            int low = firing?.Invoke(gun) == true ? t.Rack - 1 : gun == LockerCar(train) ? t.FeedAt : t.FeedAtFar;
            if (ready > low || at < 0 || (ready, at).CompareTo((bestReady, bestAt)) >= 0)
                continue;
            (best, bestReady, bestAt) = (gun, ready, at);
        }
        return best;
    }

    /// <summary>
    /// Who brings it (see the class): a crewmate with a charge already in hand, or, with a gun <see cref="Wanting"/> one, the
    /// lowest id among the bots (<paramref name="isBot"/>; all of them without it) alive and free to go (out on the roofs, or at the locker) and not in a gun's
    /// seat. Null with nobody to go, or nothing wanted.
    /// </summary>
    public static int? Carrier(World world, IReadOnlyList<(int Id, PlayerState State)> crew, GunTuning t, Func<int, bool>? isBot = null,
        Func<int, bool>? firing = null)
    {
        if (t.Rack <= 0 || t.FeedAt < 0)
            return null;
        foreach (var (id, s) in crew.OrderBy(c => c.Id))
            if (s.Alive && world.Bodies.CarriedBy(id) is { Kind: Physics.BodyKind.Powder })
                return id;
        if (Wanting(world, crew.Select(c => c.State), t, firing) is null)
            return null;
        int locker = LockerCar(world.Train);
        foreach (var (id, s) in crew.OrderBy(c => c.Id))
            if (s.Alive && (isBot?.Invoke(id) ?? Free(s, world.Train, locker)) && !s.Has(PlayerFlags.Seated) && !s.Has(PlayerFlags.Held)
                && world.Bodies.CarriedBy(id) is null)
                return id;
        return null;
    }

    /// <summary>
    /// Free to go, as anyone can see it: out on the roofs or jumping between them (on a coupler plate, in a car or down by the line, it's about
    /// something else: warming up, a hot box, a fire), or already at the locker (in the guard van, or on its hatch ladder).
    /// </summary>
    static bool Free(in PlayerState s, Train.TrainOnLine train, int locker)
    {
        if (s.Surface is Surface.Roof or Surface.Air)
            return true; // (in the air: between two roofs, mid-jump)
        return s.Parent == locker && locker > 0 && (s.Surface == Surface.Ladder || PlayerMotor.Indoors(s, train));
    }

    /// <summary>The car with the powder locker in it (the guard van), the last in the engine's rake; -1 with none.</summary>
    public static int LockerCar(TrainOnLine train)
    {
        int car = -1;
        foreach (var v in train.Dynamics.Consist.Vehicles)
            if (v.Id < train.Frames.Count && Guns.Locker(train.Frames[v.Id].Shape) is not null)
                car = v.Id;
        return car;
    }

    /// <summary>A charge in the hands of anyone in <paramref name="crew"/> but <paramref name="me"/>.</summary>
    public static bool Brought(World world, IReadOnlyList<(int Id, PlayerState State)> crew, int me) =>
        crew.Any(c => c.Id != me && c.State.Alive && world.Bodies.CarriedBy(c.Id) is { Kind: Physics.BodyKind.Powder });
}

/// <summary>
/// When a bot last saw each gun fire, by its own tick (note 377: a gun in action wants its powder kept coming). A gun's
/// <c>LastShotTick</c> is the host's world tick, which a client's world doesn't keep; a change in it is a shot.
/// </summary>
public sealed class ShotWatch
{
    readonly Dictionary<int, (uint Shot, uint Seen)> _seen = [];

    /// <summary>Once a tick: any gun whose last shot changed fired just now.</summary>
    public void See(TrainOnLine train, uint tick)
    {
        foreach (var v in train.Dynamics.Consist.Vehicles)
        {
            if (!v.HasGun)
                continue;
            uint shot = v.Gun.LastShotTick;
            if (!_seen.TryGetValue(v.Id, out var was))
                _seen[v.Id] = (shot, shot > 0 ? tick : 0);
            else if (shot != was.Shot)
                _seen[v.Id] = (shot, tick);
        }
    }

    /// <summary>The gun on <paramref name="vehicle"/> fired within the last <paramref name="seconds"/> s.</summary>
    public bool Firing(int vehicle, uint tick, double seconds) =>
        _seen.TryGetValue(vehicle, out var s) && s.Seen > 0 && tick - s.Seen < seconds * SimConstants.TickRate;
}

/// <summary>
/// A powder run (note 374; note 377 for a walker's): up onto the roofs, along them to the guard van, down its hatch ladder,
/// along its room to the powder locker in the front corner, a charge into the hands, back up the ladder, along the roofs to the
/// gun, and Use held there until the rack's full. A gunner stands behind its gun, where its seat is; a walker beside it, so
/// the gunner can stay seated. On the roof of another car the legs take it along (null, with <see cref="Along"/>, the way to
/// go given to <c>head</c>). Worked out afresh each tick from where the bot is, so it never waits on a step it's left.
/// </summary>
public sealed class PowderRun(GunTuning guns)
{
    /// <summary>Where the run's got to, or null (for tests and the harness's trace).</summary>
    public string? Step { get; private set; }

    /// <summary>Along the roofs: the legs walk it, the way <c>head</c> was given.</summary>
    public bool Along { get; private set; }

    uint? _heldAt;

    /// <summary>m: a walker stands this far to the side of the gun's pivot to fill it (within the gun's reach, off the seat).</summary>
    const double Beside = 0.7;

    /// <summary>One tick of the run to <paramref name="gunCar"/>'s gun; null when the legs walk it, or there's no locker to go to.</summary>
    public PlayerIntent? Go(in PlayerState self, World world, int me, int gunCar, bool beside, uint tick, Action<int> head)
    {
        Step = null;
        Along = false;
        var train = world.Train;
        // A charge just taken is in hand, whatever the client's view says a snapshot or two before the host's word reaches it:
        // gone back for another, a press with it in hand would put it down.
        bool carrying = world.Bodies.CarriedBy(me) is { Kind: Physics.BodyKind.Powder };
        if (carrying)
            _heldAt = tick;
        else if (_heldAt is { } held && tick - held < KitRun.TapEvery * 2)
            carrying = Guns.AtLocker(self, train, guns) is null;
        int lockerCar = PowderCarry.LockerCar(train);
        if (lockerCar < 0)
            return null;
        var shape = train.Frames[lockerCar].Shape;
        var room = shape.Interior!.Value;
        // The hatch ladder: the one whose foot is on the room's floor.
        Ladder? hatch = null;
        foreach (var l in shape.Ladders)
            if (room.Contains(l.Foot + new Double3(0, 0.2, 0)))
                hatch = l;
        if (hatch is not { } ladder)
            return null;
        bool inside = self.Parent == lockerCar && PlayerMotor.Indoors(self, train);
        if (carrying)
        {
            if (Guns.Mount(train, gunCar) is not { } mount)
                return null;
            // Behind the gun (its seat) for its gunner; beside it, on the side away from the hatch, for a walker.
            double side = ladder.Foot.X > 0 && gunCar == lockerCar ? -1 : 1;
            var stand = beside
                ? mount.Position with { X = mount.Position.X + side * Beside, Y = self.Position.Y }
                : mount.Position with { Y = self.Position.Y, Z = mount.Position.Z - mount.Facing.Z * (guns.SeatBehind + 0.1) };
            double face = DMath.Atan2(-mount.Facing.X, -mount.Facing.Z) + Math.PI;
            if (self.Parent == gunCar && self.Surface == Surface.Roof && Guns.MannedGun(self, train, guns) == gunCar)
            {
                var (settle, still) = WarmUp.Steer(self, stand, face);
                if (beside && !still)
                {
                    Step = "to the gun";
                    return settle;
                }
                // Use held, standing still, while the rack wants it (World.Charge). Full, it waits there with the charge: a
                // press with something in hand would put it down.
                Step = "charge";
                return Guns.Ready(train.Vehicles[gunCar].Gun, guns) < guns.Rack ? new PlayerIntent { Buttons = PlayerButtons.Use } : default;
            }
            if (self.Surface == Surface.Ladder)
            {
                Step = "up";
                return new PlayerIntent { MoveZ = 1 };
            }
            if (inside)
            {
                Step = "to the ladder";
                var (step, there) = WarmUp.Steer(self, ladder.Foot - ladder.Inward * 0.3, DMath.Atan2(-ladder.Inward.X, -ladder.Inward.Z) + Math.PI);
                return there ? new PlayerIntent { Actions = PlayerActions.Ladder } : step;
            }
            if (self.Surface == Surface.Roof && self.Parent == gunCar)
            {
                Step = "to the gun";
                return WarmUp.Steer(self, stand, face).Step;
            }
            // The engine's gun (note 377): from the roof of the car behind it, a running jump onto its hood (note 338: level
            // with the cars' roofs, and the cab's). The legs' walk turns round short of the engine (it's the cab's).
            if (self.Surface == Surface.Roof && train.VehicleAhead(self.Parent) == gunCar && gunCar == 0)
            {
                Step = "onto the engine";
                return ReliefDriver.OntoTheEngine(self, train);
            }
            if (self.Surface == Surface.Roof)
                head(train.Dynamics.Consist.IndexOf(gunCar) < train.Dynamics.Consist.IndexOf(self.Parent) ? -1 : 1);
            Step = "along";
            Along = true;
            return null;
        }
        if (Guns.AtLocker(self, train, guns) is not null)
        {
            // A tap every KitRun.TapEvery ticks (the press is the edge the host counts): a charge on the tap, and the client
            // hears it's in hand a snapshot later; tapping again meanwhile would put it straight back.
            Step = "take";
            return tick % KitRun.TapEvery == 0 ? new PlayerIntent { Buttons = PlayerButtons.Use } : default;
        }
        if (inside)
        {
            Step = "to the locker";
            var at = Guns.Locker(shape)!.Value;
            return WarmUp.Steer(self, at + new Double3(0.6, 0, 0.4), Math.PI / 2).Step;
        }
        if (self.Surface == Surface.Ladder && self.Parent == lockerCar)
        {
            Step = "down";
            return new PlayerIntent { MoveZ = -1 };
        }
        if (self.Surface == Surface.Roof && self.Parent == lockerCar)
        {
            Step = "to the hatch";
            var top = ladder.Foot with { Y = self.Position.Y };
            var (step, there) = WarmUp.Steer(self, top, self.Yaw);
            return there ? new PlayerIntent { Actions = PlayerActions.Ladder } : step;
        }
        if (self.Surface == Surface.Roof && self.Parent == 0 && ReliefDriver.OffTheEngine(self, train) is { } off)
        {
            Step = "off the engine";
            return off;
        }
        if (self.Surface == Surface.Roof)
            head(train.Dynamics.Consist.IndexOf(lockerCar) < train.Dynamics.Consist.IndexOf(self.Parent) ? -1 : 1);
        Step = "along";
        Along = true;
        return null;
    }
}
