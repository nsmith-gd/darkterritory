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
    public static int? Wanting(World world, IEnumerable<PlayerState> crew, GunTuning t)
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
            if (ready > (gun == LockerCar(train) ? t.FeedAt : t.FeedAtFar) || at < 0 || (ready, at).CompareTo((bestReady, bestAt)) >= 0)
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
    public static int? Carrier(World world, IReadOnlyList<(int Id, PlayerState State)> crew, GunTuning t, Func<int, bool>? isBot = null)
    {
        if (t.Rack <= 0 || t.FeedAt < 0)
            return null;
        foreach (var (id, s) in crew.OrderBy(c => c.Id))
            if (s.Alive && world.Bodies.CarriedBy(id) is { Kind: Physics.BodyKind.Powder })
                return id;
        if (Wanting(world, crew.Select(c => c.State), t) is null)
            return null;
        int locker = LockerCar(world.Train);
        foreach (var (id, s) in crew.OrderBy(c => c.Id))
            if (s.Alive && Free(s, world.Train, locker) && !s.Has(PlayerFlags.Seated) && !s.Has(PlayerFlags.Held) && (isBot?.Invoke(id) ?? true)
                && world.Bodies.CarriedBy(id) is null)
                return id;
        return null;
    }

    /// <summary>
    /// Free to go, as anyone can see it: out on the roofs (on a coupler plate, in a car or down by the line, it's about
    /// something else: warming up, a hot box, a fire), or already at the locker (in the guard van, or on its hatch ladder).
    /// </summary>
    static bool Free(in PlayerState s, Train.TrainOnLine train, int locker)
    {
        if (s.Surface == Surface.Roof)
            return true;
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

    /// <summary>m: a walker stands this far to the side of the gun's pivot to fill it (within the gun's reach, off the seat).</summary>
    const double Beside = 0.7;

    /// <summary>
    /// Along the car's roof to its front end on the centreline, facing forward, and a running jump onto the engine's hood when
    /// it's one to make (<see cref="WarmUp.CanJumpGap"/>); squared up at the end until it is.
    /// </summary>
    static PlayerIntent OntoTheEngine(in PlayerState self, TrainOnLine train)
    {
        double l = train.Frames[self.Parent].Shape.HalfLength;
        double turn = Math.IEEERemainder(0 - self.Yaw, 2 * Math.PI);
        bool aligned = Math.Abs(turn) < 0.1;
        var intent = new PlayerIntent
        {
            LookYaw = (float)Math.Clamp(turn * 0.3, -0.2, 0.2),
            MoveZ = aligned ? 1 : 0,
            MoveX = aligned ? (float)Math.Clamp(-self.Position.X * 0.8, -1, 1) : 0,
            Buttons = PlayerButtons.Run,
        };
        if (self.Position.Z < -l + 0.45 && aligned)
        {
            if (WarmUp.CanJumpGap(self, train, null, 0))
                intent.Buttons |= PlayerButtons.Jump;
            else
                intent.MoveZ = 0;
        }
        return intent;
    }

    /// <summary>One tick of the run to <paramref name="gunCar"/>'s gun; null when the legs walk it, or there's no locker to go to.</summary>
    public PlayerIntent? Go(in PlayerState self, World world, int me, int gunCar, bool beside, uint tick, Action<int> head)
    {
        Step = null;
        Along = false;
        var train = world.Train;
        bool carrying = world.Bodies.CarriedBy(me) is { Kind: Physics.BodyKind.Powder };
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
                return OntoTheEngine(self, train);
            }
            if (self.Surface == Surface.Roof)
                head(train.Dynamics.Consist.IndexOf(gunCar) < train.Dynamics.Consist.IndexOf(self.Parent) ? -1 : 1);
            Step = "along";
            Along = true;
            return null;
        }
        if (Guns.AtLocker(self, train, guns) is not null)
        {
            // A press, let go, and pressed again: the hands take a charge on the press.
            Step = "take";
            return tick % 2 == 0 ? new PlayerIntent { Buttons = PlayerButtons.Use } : default;
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
        if (self.Surface == Surface.Roof && self.Parent == 0 && OffTheEngine(self, train) is { } off)
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

    /// <summary>
    /// Off the engine's hood roof, back onto the train (note 377: a walker that brought the forward gun its powder): round the
    /// stack (it stands up through the hood on the centreline near its back), then back along the centreline and a running
    /// jump down onto car 1's roof. Null with no car behind the engine.
    /// </summary>
    public static PlayerIntent? OffTheEngine(in PlayerState self, TrainOnLine train)
    {
        int behind = train.VehicleBehind(0);
        if (behind <= 0 || self.Parent != 0 || self.Surface != Surface.Roof)
            return null;
        var plan = EnginePlan.Of(train.Dynamics.Tuning.Geometry);
        double edge = plan.Half - CarShape.BoilerToEnd;
        // Short of the stack, and on the centreline: out to its side first, then past it.
        if (self.Position.Z < plan.StackZ + 0.55)
            return WarmUp.Steer(self, new Double3(Beside, 0, plan.StackZ + 0.95), Math.PI).Step;
        double turn = Math.IEEERemainder(Math.PI - self.Yaw, 2 * Math.PI);
        bool aligned = Math.Abs(turn) < 0.1;
        var intent = new PlayerIntent
        {
            LookYaw = (float)Math.Clamp(turn * 0.3, -0.2, 0.2),
            MoveZ = aligned ? 1 : 0,
            MoveX = aligned ? (float)Math.Clamp(self.Position.X * 0.8, -1, 1) : 0,
            Buttons = PlayerButtons.Run,
        };
        if (self.Position.Z > edge - 0.45 && aligned)
        {
            if (WarmUp.CanJumpGap(self, train, null, behind))
                intent.Buttons |= PlayerButtons.Jump;
            else
                intent.MoveZ = 0;
        }
        return intent;
    }
}
