using Ballast;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Player;

[Flags]
public enum PlayerButtons : byte
{
    None = 0,
    Run = 1,
    Jump = 2,
    /// <summary>Interact: grab or let go of a ladder, later levers, switches, cargo.</summary>
    Use = 4,
    /// <summary>Hold the train brake. Only honoured from the engine.</summary>
    Brake = 8,
    /// <summary>Flip the reverser. Only honoured from the engine, with the train stopped.</summary>
    Reverser = 16,
    /// <summary>Fire the mounted gun you're standing at.</summary>
    Fire = 32,
    /// <summary>Throw what you're carrying.</summary>
    Throw = 64,
    /// <summary>A reaching hand is reported in <see cref="PlayerIntent.HandX"/>..<see cref="PlayerIntent.HandZ"/> (T29, VR).</summary>
    Hand = 128,
}

/// <summary>
/// What a client sends each tick (GDD §33: intent only, never positions).
/// Look is a delta so it stays meaningful when the player changes reference frame.
/// </summary>
public struct PlayerIntent
{
    /// <summary>Strafe, −1..1, positive right.</summary>
    public float MoveX;
    /// <summary>Forward/back, −1..1, positive forward. On a ladder, positive climbs.</summary>
    public float MoveZ;
    public float LookYaw;
    public float LookPitch;
    public PlayerButtons Buttons;
    /// <summary>Throttle notches to move this tick (−4..4). Only honoured from the engine.</summary>
    public sbyte ThrottleNotch;
    /// <summary>
    /// With <see cref="PlayerButtons.Hand"/>: a VR player's reaching hand, in metres from their feet in the frame they face
    /// (x right, y up, z behind, so ahead is −Z as ever). Reach is tested from it instead of from the body (T29).
    /// </summary>
    public float HandX, HandY, HandZ;
    /// <summary>
    /// With <see cref="Other"/> (and a reaching hand): the VR player's other hand, in the same frame (T43). Nothing reaches
    /// from it; it's there for what takes both hands, like getting a grip on your end of a heavy crate.
    /// </summary>
    public float OtherX, OtherY, OtherZ;
    public bool Other;

    public readonly bool Has(PlayerButtons b) => (Buttons & b) != 0;

    /// <summary>
    /// Reports a hand, on the centimetre grid the wire carries (<see cref="Net.Messages"/>), so a predicting client
    /// uses the hand the host will. The other hand too, when it's tracked.
    /// </summary>
    public void Reach(Double3 hand, Double3? other = null)
    {
        HandX = Centimetres(hand.X);
        HandY = Centimetres(hand.Y);
        HandZ = Centimetres(hand.Z);
        Buttons |= PlayerButtons.Hand;
        Other = other is not null;
        if (other is { } o)
        {
            OtherX = Centimetres(o.X);
            OtherY = Centimetres(o.Y);
            OtherZ = Centimetres(o.Z);
        }
    }

    public static float Centimetres(double metres) => (float)(Math.Round(Math.Clamp(metres, -300, 300) * 100) / 100);
}

/// <summary>What the player is on. Roof is exposed (roof speeds, Draggers); Deck is footing on the train that isn't.</summary>
public enum Surface : byte { Air, Ground, Roof, Coupler, Ladder, Deck }

/// <summary><see cref="Taken"/>: by the Soot Children, answering a voice from outside (T40).</summary>
/// <summary><see cref="Dragged"/>: pulled off the train at speed by the Draggers (T46).</summary>
/// <summary><see cref="Crushed"/>: under a casting let go of by the crane (T48).</summary>
public enum DeathCause : byte { None, JumpedAtSpeed, Derailed, Mauled, Hollow, Choir, Cold, Taken, Dragged, Crushed }

/// <summary>Conditions a player carries.</summary>
[Flags]
public enum PlayerFlags : byte
{
    None = 0,
    /// <summary>Spec C.2 "the revived": back from a Vigil cold. Onset comes sooner, light things only, no guns until the next POI.</summary>
    Revived = 1,
    /// <summary>Carrying freight (spec B.2 "carrying heavy cargo: 2.8 m/s, no climbing").</summary>
    Heavy = 2,
    /// <summary>A hand has coal on the shovel from the tender, on its way to the firebox (T29).</summary>
    Shovelful = 4,
    /// <summary>At a crane's controls (T48): the stick and Jump drive the crane, not you.</summary>
    Operating = 8,
}

/// <summary>
/// Authoritative player movement state. Position and velocity are in the parent frame:
/// world when <see cref="Parent"/> is <see cref="World"/>, otherwise that car's <see cref="CarFrame"/>.
/// </summary>
public struct PlayerState
{
    public const int World = -1;

    public int Parent;
    /// <summary>Feet position in the parent frame.</summary>
    public Double3 Position;
    /// <summary>Velocity relative to the parent frame.</summary>
    public Double3 Velocity;
    /// <summary>Radians, relative to the parent frame; 0 faces its −Z.</summary>
    public double Yaw;
    public double Pitch;
    public Surface Surface;
    public int Health;
    public DeathCause Death;
    /// <summary>Last known distance along the line, used to find the ground under a player off the train.</summary>
    public double LineHint;
    /// <summary>Seconds into a timed action (shovelling). Resets when the action stops.</summary>
    public double ActionProgress;
    /// <summary>Seconds of cold exposure (spec B.2): climbs outside, falls near heat, kills at the death mark.</summary>
    public double Cold;
    public PlayerFlags Flags;
    /// <summary>
    /// A VR player's reaching hand this tick, from the feet in the frame they face (<see cref="PlayerIntent.HandX"/>);
    /// zero for a player without one. Taken from each tick's intent by <see cref="PlayerMotor.TakeHand"/>, so it isn't
    /// replicated: the host and a predicting client both have it from the same intent.
    /// </summary>
    public Double3 Hand;
    /// <summary>The VR player's other hand, the same way (T43); zero when it isn't reported.</summary>
    public Double3 OtherHand;
    /// <summary>
    /// Counts the host's authoritative moves (respawns, revivals, a harness shift change). A client that sees it change
    /// adopts the new state as a placement, not as a misprediction to correct.
    /// </summary>
    public byte Placed;

    public readonly bool Alive => Death == DeathCause.None;
    public readonly bool Has(PlayerFlags flag) => (Flags & flag) != 0;
    public readonly bool Grounded => Surface is Surface.Ground or Surface.Roof or Surface.Coupler or Surface.Deck;
}

/// <summary>
/// Kinematic first-person movement on and around a moving train. Deterministic and allocation-free,
/// so the host and a predicting client produce identical results from identical intent.
/// Greybox collision only: car bodies, coupler plates, ladders and the ground. Interiors and props
/// move to Jolt when it lands (ARCHITECTURE §4).
/// </summary>
public static class PlayerMotor
{
    const double MaxPitch = 1.5;
    const double GroundSnap = 0.05;
    const double NearbyCar = 40;

    /// <summary>Stands a player on whatever is highest at (x, z) on a car: a roof, or the engine's boiler.</summary>
    public static PlayerState SpawnOnRoof(TrainOnLine train, int car, double localZ, PlayerTuning p, double localX = 0)
    {
        var top = train.Frames[car].Shape.TopAt(localX, localZ) ?? (train.Frames[car].Shape.RoofHeight, SurfaceKind.Roof);
        return new PlayerState
        {
            Parent = car,
            Position = new Double3(localX, top.Top, localZ),
            Surface = ToSurface(top.Kind),
            Health = p.Health,
            LineHint = train.Cars[car].FrontDistance,
        };
    }

    /// <summary>Stands a player on the cab floor, facing forward: where the conductor and fireman work.</summary>
    public static PlayerState SpawnInCab(TrainOnLine train, PlayerTuning p, double localX = 0)
    {
        var cab = train.Frames[0].Shape.Cab ?? throw new InvalidOperationException("engine has no cab");
        return new PlayerState
        {
            Parent = 0,
            Position = new Double3(localX, cab.Min.Y + 0.1, cab.Centre.Z),
            Surface = Surface.Deck,
            Health = p.Health,
            LineHint = train.Cars[0].FrontDistance,
        };
    }

    /// <summary>True when standing inside the engine's cab.</summary>
    public static bool InCab(in PlayerState s, TrainOnLine train) =>
        s.Parent == 0 && s.Surface == Surface.Deck && train.Frames[0].Shape.Cab is { } cab && cab.Contains(s.Position);

    /// <summary>
    /// The enclosed space a player is in: <see cref="Outside"/>, the engine cab (vehicle 0), or a car's
    /// interior with every door shut (that car's id). A car with a door open is part of the outside: sound,
    /// voice and the Choir come in through it (GDD §26: protected versus exposed).
    /// </summary>
    public static int Space(in PlayerState s, TrainOnLine train)
    {
        if (s.Parent == PlayerState.World || s.Parent >= train.Frames.Count)
            return Outside;
        if (InCab(s, train))
            return 0;
        var shape = train.Frames[s.Parent].Shape;
        if (shape.Interior is { } room && room.Contains(s.Position) && s.Surface == Surface.Deck && train.Vehicles[s.Parent].DoorsOpen == 0)
            return s.Parent;
        return Outside;
    }

    public const int Outside = -1;

    /// <summary>Inside a car's walls, doors open or not.</summary>
    public static bool Indoors(in PlayerState s, TrainOnLine train) =>
        InCab(s, train) || s.Parent != PlayerState.World && s.Parent < train.Frames.Count
            && train.Frames[s.Parent].Shape.Interior is { } room && room.Contains(s.Position) && s.Surface == Surface.Deck;

    static Surface ToSurface(SurfaceKind k) => k switch
    {
        SurfaceKind.Roof => Surface.Roof,
        SurfaceKind.Coupler => Surface.Coupler,
        _ => Surface.Deck,
    };

    public static PlayerState SpawnOnGround(Double3 world, RailLine line, double lineHint, PlayerTuning p)
    {
        var s = new PlayerState { Parent = PlayerState.World, Position = world, Surface = Surface.Ground, Health = p.Health, LineHint = lineHint };
        s.Position = world with { Y = GroundHeight(ref s, line) };
        return s;
    }

    /// <summary>Turns the view by this tick's look input. <see cref="World.CrewAct"/> does this first, so shots go where you look.</summary>
    public static void Look(ref PlayerState s, in PlayerIntent intent)
    {
        if (!s.Alive)
            return;
        s.Yaw += intent.LookYaw;
        s.Pitch = Math.Clamp(s.Pitch + intent.LookPitch, -MaxPitch, MaxPitch);
    }

    /// <summary>
    /// Takes this tick's reaching hand from the intent (after <see cref="Look"/>, so it's in the frame the player now
    /// faces), held within an arm's length across of the body and between the feet and overhead: an intent can say
    /// where a hand is, not put it through a wall at the far end of the car. It can be as low as the feet because the
    /// headset player crouches for real (the sim's body doesn't). Without hand tuning, or without a hand, there's none.
    /// </summary>
    public static void TakeHand(ref PlayerState s, in PlayerIntent intent, HandTuning? hand)
    {
        s.Hand = s.OtherHand = default;
        if (hand is null || !s.Alive || !intent.Has(PlayerButtons.Hand))
            return;
        s.Hand = Held(intent.HandX, intent.HandY, intent.HandZ, hand);
        if (s.Hand != default && intent.Other)
            s.OtherHand = Held(intent.OtherX, intent.OtherY, intent.OtherZ, hand);
    }

    static Double3 Held(float hx, float hy, float hz, HandTuning hand)
    {
        if (!float.IsFinite(hx) || !float.IsFinite(hy) || !float.IsFinite(hz))
            return default;
        double x = hx, z = hz, across = Math.Sqrt(x * x + z * z);
        if (across > hand.Arm)
            (x, z) = (x * hand.Arm / across, z * hand.Arm / across);
        static double Cm(double v) => Math.Round(v * 100) / 100;
        return new Double3(Cm(x), Cm(Math.Clamp(hy, 0.01, hand.Overhead)), Cm(z));
    }

    /// <summary>A player's reaching hand (or, with <paramref name="other"/>, their other hand) in their parent frame, or null.</summary>
    public static Double3? HandAt(in PlayerState s, bool other = false)
    {
        var hand = other ? s.OtherHand : s.Hand;
        if (hand == default)
            return null;
        double c = Math.Cos(s.Yaw), n = Math.Sin(s.Yaw);
        return s.Position + new Double3(hand.X * c + hand.Z * n, hand.Y, -hand.X * n + hand.Z * c);
    }

    /// <summary>A player's reaching hand (or their other hand) in the world, or null for a player without one.</summary>
    public static Double3? HandWorld(in PlayerState s, TrainOnLine train, bool other = false) => HandAt(s, other) is { } h ? ToWorld(s, train, h) : null;

    /// <summary>
    /// Whether a player's hands are on a grip (in the world): a reported hand within grab of it, or, for everyone else,
    /// whatever the thing's own reach rule says (<paramref name="body"/>).
    /// </summary>
    public static bool Grips(in PlayerState s, TrainOnLine train, HandTuning? hand, Double3 grip, bool body) =>
        hand is not null && HandWorld(s, train) is { } h ? (h - grip).Length <= hand.Grab : body;

    /// <summary>Advances one tick. Call after the train has stepped this tick.</summary>
    /// <param name="applyLook">False when look was already applied this tick (the world's crew step does it).</param>
    public static void Step(ref PlayerState s, in PlayerIntent intent, TrainOnLine train, PlayerTuning p, TrainTuning t, double dt, bool applyLook = true)
    {
        if (!s.Alive)
            return;

        if (applyLook)
            Look(ref s, intent);

        StepCold(ref s, train, p, dt);
        if (!s.Alive)
            return;

        if (s.Surface == Surface.Ladder)
        {
            StepLadder(ref s, intent, train, p, t, dt);
            return;
        }

        if (s.Grounded)
        {
            double speed = s.Surface == Surface.Roof
                ? (intent.Has(PlayerButtons.Run) ? p.RoofRun : p.RoofWalkSafe)
                : (intent.Has(PlayerButtons.Run) ? p.Run : p.Walk);
            if (Chilled(s, p))
                speed *= p.Cold.OnsetSpeedScale;
            if (s.Has(PlayerFlags.Heavy))
                speed = Math.Min(speed, p.CarryHeavy);
            if (s.Has(PlayerFlags.Operating))
                speed = 0;
            var wish = WishDirection(s.Yaw, intent) * speed;
            s.Velocity = new Double3(wish.X, 0, wish.Z);
            if (intent.Has(PlayerButtons.Jump) && !s.Has(PlayerFlags.Heavy) && !s.Has(PlayerFlags.Operating))
            {
                // Take off in the car's frame and integrate this tick there. The car has already moved
                // this tick; switching to world first would count its motion twice (0.73 m at 22 m/s).
                // Support resolution below moves us into the world frame once we're clear of the roof.
                s.Velocity = s.Velocity with { Y = p.JumpVelocity };
                s.Surface = Surface.Air;
            }
        }
        else if (s.Parent == PlayerState.World)
        {
            s.Velocity -= Double3.Up * (p.Gravity * dt);
        }

        // Integrate in the parent frame: a car parent carries the player with it for free.
        var prevWorld = ToWorld(s, train, s.Position);
        s.Position += s.Velocity * dt;
        var world = ToWorld(s, train, s.Position);

        world = Collide(world, train, p, out bool ceiling);
        if (ceiling && s.Velocity.Y > 0)
            s.Velocity = s.Velocity with { Y = 0 };
        UpdateSupport(ref s, world, prevWorld, train, p, t);

        // Use while pushing towards it grabs a ladder; Use standing still is for working things (CrewActions). A hand on
        // the ladder takes hold of it without pushing (T29).
        if (intent.Has(PlayerButtons.Use) && (intent.MoveZ > 0.5 || s.Hand != default) && s.Surface != Surface.Ladder && !s.Has(PlayerFlags.Heavy))
            TryGrabLadder(ref s, train, p, byHand: intent.MoveZ <= 0.5);
    }

    /// <summary>
    /// Spec B.2 cold: exposure climbs outside and kills at the death mark; near heat it falls fast enough that even the
    /// nearly frozen are recovered within the reset time. Heat is the cab while the fire's lit, or a shut car while the
    /// boiler has steam to heat it (the Vigil's vent leaves the cars cold).
    /// </summary>
    static void StepCold(ref PlayerState s, TrainOnLine train, PlayerTuning p, double dt)
    {
        var c = p.Cold;
        if (NearHeat(s, train))
        {
            s.Cold = Math.Max(0, s.Cold - dt * c.DeathSeconds / c.RecoverSecondsNearHeat);
            return;
        }
        s.Cold += dt;
        if (s.Cold >= c.DeathSeconds)
        {
            s.Health = 0;
            s.Death = DeathCause.Cold;
        }
    }

    /// <summary>Warm enough to recover: see <see cref="StepCold"/>.</summary>
    public static bool NearHeat(in PlayerState s, TrainOnLine train)
    {
        int space = Space(s, train);
        if (space == Outside)
            return false;
        if (train.BoilerTuning is null)
            return true;
        return space == 0 ? train.Boiler.Firebox > 0 || train.Boiler.Pressure > 0 : train.Boiler.Pressure > 0;
    }

    /// <summary>Past the onset of cold: slower, and the HUD says so. The revived reach it sooner (spec C.2).</summary>
    public static bool Chilled(in PlayerState s, PlayerTuning p) =>
        s.Cold >= p.Cold.OnsetSeconds * (s.Has(PlayerFlags.Revived) ? p.Cold.RevivedOnsetScale : 1);

    static Double3 WishDirection(double yaw, in PlayerIntent intent)
    {
        double x = Math.Clamp(intent.MoveX, -1, 1), z = Math.Clamp(intent.MoveZ, -1, 1);
        double len = Math.Sqrt(x * x + z * z);
        if (len > 1) { x /= len; z /= len; }
        var forward = new Double3(-Math.Sin(yaw), 0, -Math.Cos(yaw));
        var right = new Double3(Math.Cos(yaw), 0, -Math.Sin(yaw));
        return right * x + forward * z;
    }

    /// <summary>
    /// Pushes the player's cylinder out of every car body, wall, shut door and coupler plate nearby. A head
    /// that rises into something overhead (a car's ceiling) stops there instead of being shoved sideways.
    /// </summary>
    static Double3 Collide(Double3 world, TrainOnLine train, PlayerTuning p, out bool ceiling)
    {
        ceiling = false;
        foreach (var frame in train.Frames)
        {
            if ((frame.Origin - world).Length > NearbyCar)
                continue;
            var local = frame.ToLocal(world);
            foreach (var solid in frame.Shape.Solids)
                local = Ceiling(local, solid.Box, p, ref ceiling);
            foreach (var solid in frame.Shape.Solids)
                local = PushOut(local, solid.Box, p);
            var vehicle = train.Vehicles[frame.Index];
            foreach (var door in frame.Shape.DoorList)
                if (!vehicle.DoorOpen(door.Index))
                    local = PushOut(local, door.Box, p);
            world = frame.ToWorld(local);
        }
        return world;
    }

    /// <summary>Head up into the underside of a box whose footprint we're under: stop at it.</summary>
    static Double3 Ceiling(Double3 feet, Box box, PlayerTuning p, ref bool hit)
    {
        double head = feet.Y + p.Height;
        if (head <= box.Min.Y || feet.Y >= box.Min.Y || head - box.Min.Y > 0.5 || !box.ContainsXZ(feet))
            return feet;
        hit = true;
        return feet with { Y = box.Min.Y - p.Height };
    }

    static Double3 PushOut(Double3 feet, Box box, PlayerTuning p)
    {
        if (feet.Y >= box.Max.Y || feet.Y + p.Height <= box.Min.Y)
            return feet;
        // Close enough to the top to step (or land) onto it: support resolution handles that.
        if (box.Max.Y - feet.Y <= p.StepUp)
            return feet;

        double cx = Math.Clamp(feet.X, box.Min.X, box.Max.X), cz = Math.Clamp(feet.Z, box.Min.Z, box.Max.Z);
        double dx = feet.X - cx, dz = feet.Z - cz;
        double d2 = dx * dx + dz * dz;
        double r = p.Radius;
        if (d2 >= r * r)
            return feet;
        if (d2 > 1e-12)
        {
            double d = Math.Sqrt(d2);
            return feet with { X = cx + dx / d * r, Z = cz + dz / d * r };
        }
        // Centre inside the footprint: leave by the nearest face.
        double left = feet.X - box.Min.X, right = box.Max.X - feet.X, front = feet.Z - box.Min.Z, back = box.Max.Z - feet.Z;
        double min = Math.Min(Math.Min(left, right), Math.Min(front, back));
        if (min == left) return feet with { X = box.Min.X - r };
        if (min == right) return feet with { X = box.Max.X + r };
        if (min == front) return feet with { Z = box.Min.Z - r };
        return feet with { Z = box.Max.Z + r };
    }

    /// <summary>Finds what the player is standing on (if anything), re-parents, and applies landing rules.</summary>
    static void UpdateSupport(ref PlayerState s, Double3 world, Double3 prevWorld, TrainOnLine train, PlayerTuning p, TrainTuning t)
    {
        bool wasGrounded = s.Grounded;
        var worldVelocity = WorldVelocity(s, train);
        double fall = Math.Max(0, prevWorld.Y - world.Y);
        double below = Math.Max(p.StepUp, fall + 0.01);
        double snap = wasGrounded ? GroundSnap : 0;

        // Ground is always a candidate if we're at or below it: nobody falls through the earth.
        var probe = s with { Position = world };
        double groundY = GroundHeight(ref probe, train.Line);
        s.LineHint = probe.LineHint;
        double bestTop = world.Y <= groundY + (wasGrounded ? p.StepUp : 0) ? groundY : double.NegativeInfinity;
        int bestParent = PlayerState.World;
        var bestSurface = Surface.Ground;
        Double3 bestLocal = default;

        foreach (var frame in train.Frames)
        {
            if ((frame.Origin - world).Length > NearbyCar)
                continue;
            var local = frame.ToLocal(world);
            foreach (var solid in frame.Shape.Solids)
            {
                var box = solid.Box;
                double top = box.Max.Y;
                if (!box.ContainsXZ(local) || local.Y > top + snap || local.Y < top - below)
                    continue;
                double worldTop = frame.ToWorld(local with { Y = top }).Y;
                if (worldTop <= bestTop)
                    continue;
                bestTop = worldTop;
                bestParent = frame.Index;
                bestSurface = ToSurface(solid.Top);
                bestLocal = local with { Y = top };
            }
        }

        if (double.IsNegativeInfinity(bestTop))
        {
            // Nothing underfoot: fall, carrying whatever velocity the car gave us.
            s.Surface = Surface.Air;
            SetWorld(ref s, train, world, worldVelocity);
            return;
        }

        if (bestParent == PlayerState.World)
        {
            if (!wasGrounded)
                Land(ref s, worldVelocity, p, t);
            s.Parent = PlayerState.World;
            s.Surface = Surface.Ground;
            s.Position = world with { Y = groundY };
            s.Velocity = default;
            return;
        }

        var carFrame = train.Frames[bestParent];
        if (s.Parent != bestParent)
            s.Yaw += (s.Parent == PlayerState.World ? 0 : train.Frames[s.Parent].Heading) - carFrame.Heading;
        s.Parent = bestParent;
        s.Surface = bestSurface;
        s.Position = bestLocal;
        s.Velocity = default;
    }

    /// <summary>Spec B.3: one threshold for leaving the train. Faster than it kills; slower, you roll.</summary>
    static void Land(ref PlayerState s, Double3 worldVelocity, PlayerTuning p, TrainTuning t)
    {
        double horizontal = Math.Sqrt(worldVelocity.X * worldVelocity.X + worldVelocity.Z * worldVelocity.Z);
        if (SpeedBands.JumpOffIsLethal(t, horizontal))
        {
            s.Health = 0;
            s.Death = DeathCause.JumpedAtSpeed;
        }
        else if (horizontal > p.Landing.RollAbove)
        {
            s.Health = Math.Max(1, s.Health - p.Landing.RollDamage);
        }
    }

    /// <param name="byHand">The reaching hand has to be on the ladder: from its foot to a grab iron over the top rung.</param>
    static void TryGrabLadder(ref PlayerState s, TrainOnLine train, PlayerTuning p, bool byHand)
    {
        var world = ToWorld(s, train, s.Position);
        var worldVelocity = WorldVelocity(s, train);
        var hand = byHand ? HandWorld(s, train) : null;
        if (byHand && hand is null)
            return;
        foreach (var frame in train.Frames)
        {
            var local = frame.ToLocal(world);
            foreach (var ladder in frame.Shape.Ladders)
            {
                double dx = local.X - ladder.Foot.X, dz = local.Z - ladder.Foot.Z;
                if (dx * dx + dz * dz > p.Ladder.GrabRange * p.Ladder.GrabRange)
                    continue;
                if (local.Y < ladder.Foot.Y - 0.5 || local.Y > ladder.Top + 0.1)
                    continue;
                if (hand is { } h && !OnLadder(frame.ToLocal(h), ladder, p.Hand.Grab))
                    continue;
                var relative = frame.VelocityToLocal(worldVelocity);
                if (Math.Sqrt(relative.X * relative.X + relative.Z * relative.Z) >= p.Ladder.GrabMaxRelativeSpeed)
                    continue;
                if (s.Parent != frame.Index)
                    s.Yaw += (s.Parent == PlayerState.World ? 0 : train.Frames[s.Parent].Heading) - frame.Heading;
                s.Parent = frame.Index;
                s.Surface = Surface.Ladder;
                s.Position = new Double3(ladder.Foot.X, Math.Clamp(local.Y, ladder.Foot.Y, ladder.Top - 0.05), ladder.Foot.Z);
                s.Velocity = default;
                return;
            }
        }
    }

    static void StepLadder(ref PlayerState s, in PlayerIntent intent, TrainOnLine train, PlayerTuning p, TrainTuning t, double dt)
    {
        var frame = train.Frames[s.Parent];
        var ladder = NearestLadder(frame.Shape, s.Position);
        var inward = ladder.Inward;
        if (intent.Has(PlayerButtons.Jump) || intent.Has(PlayerButtons.Use) && intent.MoveZ < -0.5)
        {
            // Let go: push off the car and fall.
            s.Velocity = inward * -1.5;
            s.Surface = Surface.Air;
            Reparent(ref s, train, PlayerState.World);
            return;
        }

        double top = ladder.Top;
        double y = s.Position.Y + Math.Clamp(intent.MoveZ, -1, 1) * p.LadderClimb * dt;
        if (y >= top)
        {
            // Over the top onto whatever the ladder serves, just inside the edge: the highest footing at the top rung, not
            // whatever's overhead (the cab steps come up under the cab roof).
            var onto = s.Position + inward * (p.Radius + 0.45);
            var surface = frame.Shape.TopAt(onto.X, onto.Z, top + 0.05);
            s.Position = onto with { Y = surface?.Top ?? top };
            s.Surface = ToSurface(surface?.Kind ?? SurfaceKind.Roof);
            s.Velocity = default;
            return;
        }
        s.Position = s.Position with { Y = Math.Max(ladder.Foot.Y, y) };
        s.Velocity = default;
        if (y <= ladder.Foot.Y && ladder.Foot.Y > 0)
        {
            // A hatch ladder: step off onto the floor it stands on.
            var floor = frame.Shape.TopAt(s.Position.X, s.Position.Z, ladder.Foot.Y);
            s.Position = s.Position with { Y = ladder.Foot.Y };
            s.Surface = ToSurface(floor?.Kind ?? SurfaceKind.Deck);
            return;
        }
        if (y <= 0)
        {
            // Stepping off the bottom rung onto the ballast at whatever speed the train is doing.
            s.Surface = Surface.Air;
            var world = frame.ToWorld(s.Position);
            SetWorld(ref s, train, world, frame.Velocity);
            UpdateSupport(ref s, world, world, train, p, t);
        }
    }

    static bool OnLadder(Double3 hand, Ladder ladder, double grab)
    {
        double dx = hand.X - ladder.Foot.X, dz = hand.Z - ladder.Foot.Z;
        return dx * dx + dz * dz <= grab * grab && hand.Y >= ladder.Foot.Y && hand.Y <= ladder.Top + 0.4;
    }

    static Ladder NearestLadder(CarShape shape, Double3 p)
    {
        Ladder best = shape.Ladders[0];
        double bestD = double.MaxValue;
        foreach (var l in shape.Ladders)
        {
            double d = (l.Foot.X - p.X) * (l.Foot.X - p.X) + (l.Foot.Z - p.Z) * (l.Foot.Z - p.Z);
            if (d < bestD) { bestD = d; best = l; }
        }
        return best;
    }

    static Double3 ToWorld(in PlayerState s, TrainOnLine train, Double3 position) =>
        s.Parent == PlayerState.World ? position : train.Frames[s.Parent].ToWorld(position);

    public static Double3 WorldPosition(in PlayerState s, TrainOnLine train) => ToWorld(s, train, s.Position);

    public static Double3 WorldVelocity(in PlayerState s, TrainOnLine train) =>
        s.Parent == PlayerState.World ? s.Velocity : train.Frames[s.Parent].VelocityToWorld(s.Velocity);

    /// <summary>World-space yaw of the player's view, for cameras and audio.</summary>
    public static double WorldYaw(in PlayerState s, TrainOnLine train) =>
        s.Parent == PlayerState.World ? s.Yaw : s.Yaw + train.Frames[s.Parent].Heading;

    static void Reparent(ref PlayerState s, TrainOnLine train, int parent)
    {
        if (parent == s.Parent)
            return;
        var world = ToWorld(s, train, s.Position);
        var velocity = WorldVelocity(s, train);
        double yaw = WorldYaw(s, train);
        if (parent == PlayerState.World)
        {
            s.Parent = parent;
            s.Position = world;
            s.Velocity = velocity;
            s.Yaw = yaw;
            return;
        }
        var frame = train.Frames[parent];
        s.Parent = parent;
        s.Position = frame.ToLocal(world);
        s.Velocity = frame.VelocityToLocal(velocity);
        s.Yaw = yaw - frame.Heading;
    }

    /// <summary>
    /// Pulled off a car over its side (the Draggers, App. A.4): out past its edge and falling, with the train's speed and
    /// <paramref name="outward"/> on top. Spec B.3's one threshold decides it: faster than a survivable jump, that's death
    /// (the body goes over the side); slower, you land on the ballast and the train goes on without you. An authoritative
    /// move, so a predicting client adopts it (<see cref="PlayerState.Placed"/>).
    /// </summary>
    public static void PullOff(ref PlayerState s, TrainOnLine train, Double3 outward, TrainTuning t)
    {
        if (s.Parent == PlayerState.World || s.Parent >= train.Frames.Count)
            return;
        var frame = train.Frames[s.Parent];
        var across = frame.DirToLocal(outward);
        var local = s.Position with { X = Math.Sign(across.X == 0 ? 1 : across.X) * (frame.Shape.HalfWidth + 0.4), Y = s.Position.Y - 0.3 };
        var carVelocity = frame.VelocityToWorld(Double3.Zero);
        SetWorld(ref s, train, frame.ToWorld(local), carVelocity + outward);
        s.Surface = Surface.Air;
        s.Placed++;
        if (SpeedBands.JumpOffIsLethal(t, Math.Sqrt(carVelocity.X * carVelocity.X + carVelocity.Z * carVelocity.Z)))
        {
            s.Health = 0;
            s.Death = DeathCause.Dragged;
        }
    }

    static void SetWorld(ref PlayerState s, TrainOnLine train, Double3 world, Double3 worldVelocity)
    {
        s.Yaw = WorldYaw(s, train);
        s.Parent = PlayerState.World;
        s.Position = world;
        s.Velocity = worldVelocity;
    }

    /// <summary>Height of the ground near a world point, refining a hint along the line (bodies use this too).</summary>
    /// <remarks>
    /// Beside the track the ground is at the height of the nearest track: the main line, or a branch off it. On a
    /// generated line the land beyond rises and falls with its terrain (linegen plan §12), which keeps the formation at
    /// that same rail height.
    /// </remarks>
    public static double GroundAt(Double3 world, RailLine line, ref double hint)
    {
        var (path, along) = line.Nearest(world, ref hint);
        return line.Conditions is { } c ? c.Ground(world) : line.Sample(path, along).Position.Y;
    }

    static double GroundHeight(ref PlayerState s, RailLine line) => GroundAt(s.Position, line, ref s.LineHint);
}
