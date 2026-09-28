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

    public readonly bool Has(PlayerButtons b) => (Buttons & b) != 0;
}

public enum Surface : byte { Air, Ground, Roof, Coupler, Ladder }

public enum DeathCause : byte { None, JumpedAtSpeed }

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

    public readonly bool Alive => Death == DeathCause.None;
    public readonly bool Grounded => Surface is Surface.Ground or Surface.Roof or Surface.Coupler;
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

    public static PlayerState SpawnOnRoof(TrainOnLine train, int car, double localZ, PlayerTuning p) => new()
    {
        Parent = car,
        Position = new Double3(0, train.Frames[car].Shape.Body.Max.Y, localZ),
        Surface = Surface.Roof,
        Health = p.Health,
        LineHint = train.Cars[car].FrontDistance,
    };

    public static PlayerState SpawnOnGround(Double3 world, RailLine line, double lineHint, PlayerTuning p)
    {
        var s = new PlayerState { Parent = PlayerState.World, Position = world, Surface = Surface.Ground, Health = p.Health, LineHint = lineHint };
        s.Position = world with { Y = GroundHeight(ref s, line) };
        return s;
    }

    /// <summary>Advances one tick. Call after the train has stepped this tick.</summary>
    public static void Step(ref PlayerState s, in PlayerIntent intent, TrainOnLine train, PlayerTuning p, TrainTuning t, double dt)
    {
        if (!s.Alive)
            return;

        s.Yaw += intent.LookYaw;
        s.Pitch = Math.Clamp(s.Pitch + intent.LookPitch, -MaxPitch, MaxPitch);

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
            var wish = WishDirection(s.Yaw, intent) * speed;
            s.Velocity = new Double3(wish.X, 0, wish.Z);
            if (intent.Has(PlayerButtons.Jump))
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

        world = Collide(world, train, p);
        UpdateSupport(ref s, world, prevWorld, train, p, t);

        if (intent.Has(PlayerButtons.Use) && s.Surface != Surface.Ladder)
            TryGrabLadder(ref s, train, p);
    }

    static Double3 WishDirection(double yaw, in PlayerIntent intent)
    {
        double x = Math.Clamp(intent.MoveX, -1, 1), z = Math.Clamp(intent.MoveZ, -1, 1);
        double len = Math.Sqrt(x * x + z * z);
        if (len > 1) { x /= len; z /= len; }
        var forward = new Double3(-Math.Sin(yaw), 0, -Math.Cos(yaw));
        var right = new Double3(Math.Cos(yaw), 0, -Math.Sin(yaw));
        return right * x + forward * z;
    }

    /// <summary>Pushes the player's cylinder out of every car body and coupler plate nearby.</summary>
    static Double3 Collide(Double3 world, TrainOnLine train, PlayerTuning p)
    {
        foreach (var frame in train.Frames)
        {
            if ((frame.Origin - world).Length > NearbyCar)
                continue;
            var local = frame.ToLocal(world);
            local = PushOut(local, frame.Shape.Body, p);
            if (frame.Shape.Coupler is { } c)
                local = PushOut(local, c, p);
            world = frame.ToWorld(local);
        }
        return world;
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
            Consider(frame.Shape.Body, CarSurface.Roof);
            if (frame.Shape.Coupler is { } c)
                Consider(c, CarSurface.Coupler);

            void Consider(Box box, CarSurface kind)
            {
                double top = box.Max.Y;
                if (!box.ContainsXZ(local) || local.Y > top + snap || local.Y < top - below)
                    return;
                double worldTop = frame.ToWorld(local with { Y = top }).Y;
                if (worldTop <= bestTop)
                    return;
                bestTop = worldTop;
                bestParent = frame.Index;
                bestSurface = kind == CarSurface.Roof ? Surface.Roof : Surface.Coupler;
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

    static void TryGrabLadder(ref PlayerState s, TrainOnLine train, PlayerTuning p)
    {
        var world = ToWorld(s, train, s.Position);
        var worldVelocity = WorldVelocity(s, train);
        foreach (var frame in train.Frames)
        {
            var local = frame.ToLocal(world);
            foreach (var ladder in frame.Shape.Ladders)
            {
                double dx = local.X - ladder.X, dz = local.Z - ladder.Z;
                if (dx * dx + dz * dz > p.Ladder.GrabRange * p.Ladder.GrabRange)
                    continue;
                if (local.Y < -0.5 || local.Y > frame.Shape.Body.Max.Y + 0.1)
                    continue;
                var relative = frame.VelocityToLocal(worldVelocity);
                if (Math.Sqrt(relative.X * relative.X + relative.Z * relative.Z) >= p.Ladder.GrabMaxRelativeSpeed)
                    continue;
                if (s.Parent != frame.Index)
                    s.Yaw += (s.Parent == PlayerState.World ? 0 : train.Frames[s.Parent].Heading) - frame.Heading;
                s.Parent = frame.Index;
                s.Surface = Surface.Ladder;
                s.Position = new Double3(ladder.X, Math.Max(0, local.Y), ladder.Z);
                s.Velocity = default;
                return;
            }
        }
    }

    static void StepLadder(ref PlayerState s, in PlayerIntent intent, TrainOnLine train, PlayerTuning p, TrainTuning t, double dt)
    {
        var frame = train.Frames[s.Parent];
        var inward = frame.Shape.LadderInward(s.Position);
        if (intent.Has(PlayerButtons.Jump) || intent.Has(PlayerButtons.Use) && intent.MoveZ < -0.5)
        {
            // Let go: push off the car and fall.
            s.Velocity = inward * -1.5;
            s.Surface = Surface.Air;
            Reparent(ref s, train, PlayerState.World);
            return;
        }

        double top = frame.Shape.Body.Max.Y;
        double y = s.Position.Y + Math.Clamp(intent.MoveZ, -1, 1) * p.LadderClimb * dt;
        if (y >= top)
        {
            // Over the top onto the roof, just inside the edge.
            var onto = s.Position + inward * (p.Radius + 0.45);
            s.Position = onto with { Y = top };
            s.Surface = Surface.Roof;
            s.Velocity = default;
            return;
        }
        s.Position = s.Position with { Y = Math.Max(0, y) };
        s.Velocity = default;
        if (y <= 0)
        {
            // Stepping off the bottom rung onto the ballast at whatever speed the train is doing.
            s.Surface = Surface.Air;
            var world = frame.ToWorld(s.Position);
            SetWorld(ref s, train, world, frame.Velocity);
            UpdateSupport(ref s, world, world, train, p, t);
        }
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

    static void SetWorld(ref PlayerState s, TrainOnLine train, Double3 world, Double3 worldVelocity)
    {
        s.Yaw = WorldYaw(s, train);
        s.Parent = PlayerState.World;
        s.Position = world;
        s.Velocity = worldVelocity;
    }

    /// <summary>Ground under a player off the train: flat terrain at rail height for now.</summary>
    static double GroundHeight(ref PlayerState s, RailLine line)
    {
        double hint = s.LineHint;
        for (int i = 0; i < 3; i++)
        {
            var sample = line.Sample(hint);
            hint = sample.Distance + Double3.Dot(s.Position - sample.Position, sample.Tangent);
        }
        s.LineHint = Math.Clamp(hint, 0, line.Length);
        return line.Sample(s.LineHint).Position.Y;
    }
}
