using Ballast;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Player;

/// <summary>
/// Bringing them home (GDD §17, §23.5; ARCHITECTURE §8 note 723; the friendslop audit's pillar "nobody gets left", approved by
/// the director, 14 Oct 2026): the physical verbs a crew answers "the engine can leave without you" with. A crewmate at a
/// moving car's door hauls a friend running beside it aboard; a living friend is picked up and carried over the shoulder (put
/// down, wriggled out of, or, with the crew mischief switch on, thrown); a healing find put on someone else heals half again
/// as much. The shared rules, alike on the host and in the HUD; the host's side is <see cref="World"/>'s.
/// </summary>
public static class Rescue
{
    /// <summary>
    /// Whether <paramref name="s"/> can lend a hand: alive, steady on their feet, and not on a ladder, in a gun's seat, at a
    /// crane, held by something, carried, or carrying someone already.
    /// </summary>
    public static bool CanHelp(in PlayerState s) =>
        s.Alive && s.Stagger == 0 && s.Grounded && !s.Has(PlayerFlags.Held) && !s.Has(PlayerFlags.Seated) && !s.Has(PlayerFlags.Operating)
        && !s.Has(PlayerFlags.Carried) && !s.Has(PlayerFlags.Carrying) && !s.Has(PlayerFlags.Hoisted);

    /// <summary>
    /// Note 723: a carried crewmate's intent: they look, talk and wriggle (Jump), and nothing else, the cab's controls included
    /// (their feet aren't theirs). The dead's own keys stay, as a stagger's do. Note 726: and one riding a crane's hook, whose
    /// Jump lets go of it.
    /// </summary>
    public static PlayerIntent CarriedIntent(in PlayerState s, in PlayerIntent intent)
    {
        if (!s.Has(PlayerFlags.Carried) && !s.Has(PlayerFlags.Hoisted) || !s.Alive)
            return intent;
        return intent with
        {
            MoveX = 0,
            MoveZ = 0,
            Buttons = intent.Buttons & (PlayerButtons.Jump | PlayerButtons.Hand),
            ThrottleNotch = 0,
            Lamp = LampSwitch.None,
            Actions = intent.Actions & PlayerActions.Bookmark,
            Moves = PlayerMoves.None,
            Emote = Emote.None,
            Signal = LampSignal.None,
        };
    }

    /// <summary>The height of a crewmate's chest over their feet (m), as horseplay's (<see cref="Horseplay.Chest"/>).</summary>
    const double Chest = Horseplay.Chest;

    /// <summary>A body from shins to crown, over the feet (m): what a reaching hand can take hold of.</summary>
    const double Shins = 0.3, Crown = 1.7;

    /// <summary>The look direction (world) of <paramref name="s"/>, pitch and all.</summary>
    public static Double3 Look(in PlayerState s, TrainOnLine train)
    {
        double yaw = PlayerMotor.WorldYaw(s, train);
        return new Double3(-DMath.Sin(yaw) * DMath.Cos(s.Pitch), DMath.Sin(s.Pitch), -DMath.Cos(yaw) * DMath.Cos(s.Pitch));
    }

    /// <summary>Where the eye of <paramref name="s"/> is (world): a stood or crouched head.</summary>
    static Double3 Eye(in PlayerState s, TrainOnLine train) =>
        PlayerMotor.WorldPosition(s, train) + Double3.Up * (s.Has(PlayerFlags.Crouched) ? 1.0 : 1.5);

    /// <summary>How far a point is from the body of a crewmate whose feet are at <paramref name="feet"/> (shins to crown).</summary>
    static double FromBody(Double3 point, Double3 feet)
    {
        double y = Math.Clamp(point.Y, feet.Y + Shins, feet.Y + Crown);
        return (point - feet with { Y = y }).Length;
    }

    /// <summary>
    /// Note 723, the hand-up: the friend a hand-up from <paramref name="s"/> would haul aboard, or null. <paramref name="s"/>
    /// stands on a car (at its open door, on its end platform or its steps) and the friend is on foot off the train beside it:
    /// within rescue.handUp.reach of the helper's chest (to any of them, shins to crown), and (when <paramref name="aimed"/>)
    /// looked at within its coneDegrees, nothing of the car between them, the car going at least fromSpeed and the two moving
    /// within maxRelativeSpeed of each other along the ground (a runner's sprint, not a standing friend snatched off the
    /// ballast at 40 km/h). The nearest, by id on a tie.
    /// </summary>
    public static (int Id, PlayerState State)? HandUpTarget(in PlayerState s, int self, IEnumerable<(int Id, PlayerState State)> mates, TrainOnLine train,
        RescueTuning t, bool aimed = true)
    {
        var h = t.HandUp;
        if (!h.Enabled || !CanHelp(s) || s.Parent < 0 || s.Parent >= train.Frames.Count || Math.Abs(train.RakeOf(s.Parent).Speed) < h.FromSpeed)
            return null;
        var frame = train.Frames[s.Parent];
        var chest = PlayerMotor.WorldPosition(s, train) + Double3.Up * Chest;
        var eye = Eye(s, train);
        var look = Look(s, train);
        double cos = DMath.Cos(h.ConeDegrees * Math.PI / 180);
        (int, PlayerState)? best = null;
        double bestD = double.MaxValue;
        foreach (var (id, o) in mates)
        {
            if (id == self || !o.Alive || o.Parent != PlayerState.World || o.Has(PlayerFlags.Held) || o.Has(PlayerFlags.Carried) || o.Has(PlayerFlags.Hoisted) || o.Surface == Surface.Ladder)
                continue;
            var feet = o.Position;
            double d = FromBody(chest, feet);
            if (d > h.Reach)
                continue;
            var relative = (o.Velocity - frame.Velocity) with { Y = 0 };
            if (relative.Length > h.MaxRelativeSpeed)
                continue;
            var at = feet + Double3.Up * Chest;
            if (aimed)
            {
                var to = at - eye;
                if (to.Length > 0.05 && Double3.Dot(to.Normalized, look) < cos)
                    continue;
            }
            if (!Clear(train, s.Parent, chest, at))
                continue;
            if (d < bestD || d == bestD && id < best!.Value.Item1)
                (best, bestD) = ((id, o), d);
        }
        return best;
    }

    /// <summary>
    /// Whether nothing of car <paramref name="car"/> stands between two world points: a wall, a shut door, the body. The
    /// segment's tested at points along it in the car's frame (a hand reached through an open door, not a closed one).
    /// </summary>
    public static bool Clear(TrainOnLine train, int car, Double3 from, Double3 to)
    {
        var frame = train.Frames[car];
        var vehicle = train.Vehicles[car];
        var a = frame.ToLocal(from);
        var b = frame.ToLocal(to);
        const int Steps = 12;
        // The ends themselves are where the two are: only what's between them counts.
        for (int i = 1; i < Steps; i++)
        {
            var p = a + (b - a) * (i / (double)Steps);
            foreach (var solid in frame.Shape.Solids)
                if (solid.Part is not (PartKind.RunningBoard or PartKind.Steps) && solid.Present(vehicle) && solid.BoxIn(vehicle).Contains(p))
                    return false;
            foreach (var door in frame.Shape.DoorList)
                if (!vehicle.DoorOpen(door.Index) && door.Box.Contains(p))
                    return false;
        }
        return true;
    }

    /// <summary>
    /// Note 723: <paramref name="runner"/> hauled aboard by <paramref name="helper"/>, the host's authoritative move (a predicting
    /// client adopts it: <see cref="PlayerState.Placed"/>). They land in the helper's car's frame, stood a step inboard of the
    /// helper (away from the side they came up), on the footing there, or where the helper stands if there's none clear (the
    /// crowd parts them). Still in the car's frame: the car has already moved this tick, so nothing is converted through the
    /// world but where they're put.
    /// </summary>
    public static void HaulAboard(ref PlayerState runner, in PlayerState helper, TrainOnLine train, PlayerTuning p, RescueTuning t)
    {
        int car = helper.Parent;
        var frame = train.Frames[car];
        var vehicle = train.Vehicles[car];
        var from = frame.ToLocal(PlayerMotor.WorldPosition(runner, train));
        var away = (helper.Position - from) with { Y = 0 };
        var spot = helper.Position;
        if (away.Length > 0.05)
        {
            var inboard = helper.Position + away.Normalized * t.HandUp.LandInboard;
            if (Standing(frame, vehicle, inboard, helper.Position.Y, p) is { } top)
                spot = inboard with { Y = top.Top };
        }
        double yaw = PlayerMotor.WorldYaw(runner, train) - frame.Heading;
        var surface = frame.Shape.TopAt(spot.X, spot.Z, vehicle, spot.Y + p.StepUp) is { } under ? PlayerMotor.ToSurface(under.Kind) : helper.Surface;
        runner.Parent = car;
        runner.Position = spot;
        runner.Velocity = Double3.Zero;
        runner.Yaw = yaw;
        runner.Surface = surface;
        runner.LineHint = train.Cars[car].FrontDistance;
        runner.Flags &= ~(PlayerFlags.OffTheGround | PlayerFlags.Shoved | PlayerFlags.OffTheHook | PlayerFlags.Crouched);
        runner.Placed++;
    }

    /// <summary>
    /// Footing at <paramref name="local"/> in a car's frame within a step of <paramref name="floor"/>, with a body's room over it
    /// (no wall, shut door or fitting in the way); null if not.
    /// </summary>
    static (double Top, SurfaceKind Kind)? Standing(CarFrame frame, Vehicle vehicle, Double3 local, double floor, PlayerTuning p)
    {
        if (frame.Shape.TopAt(local.X, local.Z, vehicle, floor + p.StepUp) is not { } top || Math.Abs(top.Top - floor) > p.StepUp)
            return null;
        foreach (double up in new[] { 0.5, 1.0, 1.5 })
        {
            var at = local with { Y = top.Top + up };
            foreach (var solid in frame.Shape.Solids)
                if (solid.Present(vehicle) && solid.BoxIn(vehicle) is var box
                    && at.X > box.Min.X - p.Radius && at.X < box.Max.X + p.Radius && at.Z > box.Min.Z - p.Radius && at.Z < box.Max.Z + p.Radius
                    && at.Y > box.Min.Y && at.Y < box.Max.Y)
                    return null;
            foreach (var door in frame.Shape.DoorList)
                if (!vehicle.DoorOpen(door.Index) && door.Box.Contains(at))
                    return null;
        }
        return top;
    }

    /// <summary>
    /// Note 723: the friend <paramref name="s"/> would pick up (Use held, rescue.carry.liftSeconds), or null: a living crewmate
    /// on their feet in the same frame (the ground, or the same car), not held, carried or carrying, within carry.reach chest to
    /// chest and coneDegrees of where they face. The nearest, by id on a tie.
    /// </summary>
    public static (int Id, PlayerState State)? LiftTarget(in PlayerState s, int self, IEnumerable<(int Id, PlayerState State)> mates, TrainOnLine train,
        RescueTuning t, PlayerTuning p)
    {
        var c = t.Carry;
        if (!c.Enabled || !CanHelp(s) || s.Has(PlayerFlags.Heavy) || s.Has(PlayerFlags.Shovelful) || s.Surface == Surface.Ladder || s.Parent >= train.Frames.Count)
            return null;
        int parent = s.Parent;
        return Nearest(s, self, mates, train, c.Reach, c.ConeDegrees, p,
            o => o.Parent == parent && Horseplay.Solid(o) && !o.Has(PlayerFlags.Carried) && !o.Has(PlayerFlags.Carrying));
    }

    /// <summary>
    /// Note 723: the hurt crewmate a healing find in <paramref name="s"/>'s hands would be put on (Use held, loot.json
    /// healing.useSeconds), or null: alive, short of <paramref name="full"/> health, not held, within rescue.bandage.reach chest to
    /// chest and its coneDegrees of where <paramref name="s"/> faces, in any frame.
    /// </summary>
    public static (int Id, PlayerState State)? Patient(in PlayerState s, int self, IEnumerable<(int Id, PlayerState State)> mates, TrainOnLine train,
        RescueTuning t, PlayerTuning p, int full)
    {
        var b = t.Bandage;
        if (!b.Enabled || !s.Alive || s.Stagger > 0 || s.Has(PlayerFlags.Held) || s.Has(PlayerFlags.Carried) || s.Parent >= train.Frames.Count)
            return null;
        return Nearest(s, self, mates, train, b.Reach, b.ConeDegrees, p, o => o.Health < full && !o.Has(PlayerFlags.Held));
    }

    /// <summary>The nearest crewmate (chest to chest, within reach and cone, at about their height) that <paramref name="ok"/> takes.</summary>
    static (int Id, PlayerState State)? Nearest(in PlayerState s, int self, IEnumerable<(int Id, PlayerState State)> mates, TrainOnLine train,
        double reach, double coneDegrees, PlayerTuning p, Func<PlayerState, bool> ok)
    {
        var chest = PlayerMotor.WorldPosition(s, train) + Double3.Up * Chest;
        double yaw = PlayerMotor.WorldYaw(s, train), cos = DMath.Cos(coneDegrees * Math.PI / 180);
        var facing = new Double3(-DMath.Sin(yaw), 0, -DMath.Cos(yaw));
        (int, PlayerState)? best = null;
        double bestD = double.MaxValue;
        foreach (var (id, o) in mates)
        {
            if (id == self || !o.Alive || o.Parent >= train.Frames.Count || !ok(o))
                continue;
            var to = PlayerMotor.WorldPosition(o, train) + Double3.Up * Chest - chest;
            double d = to.Length;
            if (d > reach || Math.Abs(to.Y) > p.Height * 0.6)
                continue;
            var flat = to with { Y = 0 };
            if (flat.Length > 0.05 && Double3.Dot(flat.Normalized, facing) < cos)
                continue;
            if (d < bestD || d == bestD && id < best!.Value.Item1)
                (best, bestD) = ((id, o), d);
        }
        return best;
    }

    /// <summary>
    /// Where a carried friend is, in their carrier's frame (note 723): over the shoulder, carry.shoulderUp over the carrier's
    /// feet and carry.shoulderBack behind them, a body across the back as the body-carry holds one (App. C.4).
    /// </summary>
    public static Double3 Shoulder(in PlayerState carrier, RescueTuning t)
    {
        var back = new Double3(DMath.Sin(carrier.Yaw), 0, DMath.Cos(carrier.Yaw));
        return carrier.Position + Double3.Up * t.Carry.ShoulderUp + back * t.Carry.ShoulderBack;
    }

    /// <summary>
    /// Note 723: <paramref name="carried"/> on <paramref name="carrier"/>'s shoulder this tick: in the carrier's frame (a frame
    /// changed is gone through the world, as a step from one car to the next is), moving as they move, on the footing they're
    /// on. The carried's own view (their yaw and pitch) is theirs.
    /// </summary>
    public static void Follow(ref PlayerState carried, in PlayerState carrier, TrainOnLine train, RescueTuning t)
    {
        if (carried.Parent != carrier.Parent)
            PlayerMotor.Reparent(ref carried, train, carrier.Parent);
        carried.Position = Shoulder(carrier, t);
        carried.Velocity = carrier.Velocity;
        carried.Surface = carrier.Surface;
        carried.LineHint = carrier.LineHint;
        carried.Flags = (carried.Flags | PlayerFlags.Carried) & ~(PlayerFlags.Crouched | PlayerFlags.OffTheGround | PlayerFlags.Shoved | PlayerFlags.OffTheHook);
    }

    /// <summary>
    /// Note 723: <paramref name="carried"/> set down (put down, or wriggled free) at <paramref name="carrier"/>'s feet: in their
    /// frame, on their footing, at a stand in it (the crowd parts the two). A host move (<see cref="PlayerState.Placed"/>).
    /// </summary>
    public static void SetDown(ref PlayerState carried, in PlayerState carrier, TrainOnLine train)
    {
        if (carried.Parent != carrier.Parent)
            PlayerMotor.Reparent(ref carried, train, carrier.Parent);
        carried.Position = carrier.Position;
        carried.Velocity = carrier.Grounded ? Double3.Zero : carrier.Velocity;
        carried.Surface = carrier.Surface;
        carried.LineHint = carrier.LineHint;
        carried.Flags &= ~PlayerFlags.Carried;
        carried.Placed++;
    }

    /// <summary>
    /// Note 723 (the director's griefer joy; the crew mischief switch gates it): <paramref name="carried"/> thrown off the
    /// shoulder along <paramref name="thrower"/>'s view at carry.throwSpeed and carry.throwLift up, on the thrower's own motion,
    /// in their frame (the car has already moved this tick). Off their feet as a shove puts them (<see cref="PlayerFlags.Shoved"/>):
    /// the landing is the world's own rule (the train's speed, or the drop), the throw's own speed knocks nobody, and a landing
    /// that kills is the thrower's. A host move.
    /// </summary>
    public static void Throw(ref PlayerState carried, in PlayerState thrower, TrainOnLine train, RescueTuning t)
    {
        if (carried.Parent != thrower.Parent)
            PlayerMotor.Reparent(ref carried, train, thrower.Parent);
        var look = new Double3(-DMath.Sin(thrower.Yaw) * DMath.Cos(thrower.Pitch), DMath.Sin(thrower.Pitch), -DMath.Cos(thrower.Yaw) * DMath.Cos(thrower.Pitch));
        carried.Position = Shoulder(thrower, t);
        carried.Velocity = thrower.Velocity + look * t.Carry.ThrowSpeed + Double3.Up * t.Carry.ThrowLift;
        carried.Surface = Surface.Air;
        carried.Flags = (carried.Flags | PlayerFlags.Shoved) & ~PlayerFlags.Carried;
        carried.Placed++;
    }
}

/// <summary>
/// Bringing them home's numbers (note 723). Mirror of player.json <c>rescue</c>, where each field is documented. A bandage's
/// share on a friend is loot.json <c>healing.onAFriend</c>, with the rest of the healing finds' numbers.
/// </summary>
public sealed record RescueTuning
{
    public HandUpTuning HandUp { get; init; } = new();
    public CarryTuning Carry { get; init; } = new();
    public BandageTuning Bandage { get; init; } = new();
}

/// <summary>The hand-up onto a moving car (note 723). Docs in player.json <c>rescue.handUp</c>.</summary>
public sealed record HandUpTuning
{
    public bool Enabled { get; init; } = true;
    public double Reach { get; init; } = 1.6;
    public double ConeDegrees { get; init; } = 40;
    public double MaxRelativeSpeed { get; init; } = 7;
    public double FromSpeed { get; init; } = 1;
    public double Seconds { get; init; } = 0.6;
    public double LandInboard { get; init; } = 0.45;
    public double ReportFrom { get; init; } = 2;
}

/// <summary>Carrying a living friend (note 723). Docs in player.json <c>rescue.carry</c>.</summary>
public sealed record CarryTuning
{
    public bool Enabled { get; init; } = true;
    public double Reach { get; init; } = 1.2;
    public double ConeDegrees { get; init; } = 30;
    public double LiftSeconds { get; init; } = 1;
    public double ShoulderUp { get; init; } = 0.7;
    public double ShoulderBack { get; init; } = 0.15;
    public double ThrowSpeed { get; init; } = 4;
    public double ThrowLift { get; init; } = 1.5;
}

/// <summary>A healing find put on a friend (note 723). Docs in player.json <c>rescue.bandage</c>.</summary>
public sealed record BandageTuning
{
    public bool Enabled { get; init; } = true;
    public double Reach { get; init; } = 1.4;
    public double ConeDegrees { get; init; } = 40;
}
