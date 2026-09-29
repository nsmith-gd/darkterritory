using Ballast;
using Ballast.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Physics;

public enum BodyKind : byte { Crate = 1, Lamp = 2, Ragdoll = 3 }

/// <summary>
/// A loose physical thing: cargo, a tool, a crewmate's body. It lives in a car's frame while it touches that
/// car, and in the world frame in flight, exactly like a player (ARCHITECTURE §6.1): a crate on a roof at
/// 22 m/s is still, and one thrown off the side lands behind the train with the train's speed.
/// </summary>
public sealed class Body
{
    public Body(int id, BodyKind kind, int parent, PbdBody pbd)
    {
        Id = id;
        Kind = kind;
        Parent = parent;
        Pbd = pbd;
    }

    public int Id { get; }
    public BodyKind Kind { get; }
    /// <summary>Vehicle id, or <see cref="PlayerState.World"/>.</summary>
    public int Parent { get; set; }
    public PbdBody Pbd { get; }
    /// <summary>Player carrying it, or −1.</summary>
    public int Carrier { get; set; } = -1;
    /// <summary>For a ragdoll, whose body it is.</summary>
    public int Owner { get; set; } = -1;
    /// <summary>Facing, for drawing single-point bodies (crates, lamps); tumbles in flight.</summary>
    public double Yaw { get; set; }
    public double Spin { get; set; }
    internal int Airborne;
    internal double LineHint;
    public Double3 Centre => Pbd.Centre;
}

/// <summary>Carry, throw and body tuning (content/tuning/player.json "hands").</summary>
public sealed record HandsTuning(double Reach, double ThrowSpeed, double RagdollThrowSpeed, double CarryHeight, double CarryForward);

/// <summary>
/// Host-simulated loose bodies (GDD §33: thrown objects, cargo, ragdolls, bodies). Clients mirror them from
/// Body records. A dead player's body persists where they fell (spec C.1) and can be carried: the Vigil
/// needs it in the engine, and a body carried to the terminus is revived at the gate (spec C.2).
/// </summary>
public sealed class Bodies
{
    readonly List<Body> _bodies = new();
    readonly Dictionary<int, bool> _useWas = new(), _throwWas = new();
    int _nextId = 1;
    const double Dt = SimConstants.TickSeconds;
    const double NearbyCar = 40;

    public IReadOnlyList<Body> All => _bodies;
    public HandsTuning Hands { get; set; } = new(1.6, 9, 4, 1.15, 0.6);

    public Body SpawnCrate(TrainOnLine train, int car, Double3 local, BodyKind kind = BodyKind.Crate)
    {
        double radius = kind == BodyKind.Crate ? 0.35 : 0.15;
        var pbd = new PbdBody([new Particle(local + Double3.Up * radius, 1, radius)]) { Friction = 0.2, Bounce = 0.1 };
        var b = new Body(_nextId++, kind, car, pbd) { LineHint = train.Cars[Math.Max(0, car)].FrontDistance };
        _bodies.Add(b);
        return b;
    }

    /// <summary>Bone layout: head, chest, pelvis, elbows, hands, knees, feet (standing, in a player's frame).</summary>
    static readonly (Double3 At, double Radius)[] Skeleton =
    [
        (new(0, 1.62, 0), 0.13), (new(0, 1.35, 0), 0.16), (new(0, 0.95, 0), 0.15),
        (new(-0.28, 1.12, 0), 0.08), (new(-0.3, 0.85, 0), 0.07), (new(0.28, 1.12, 0), 0.08), (new(0.3, 0.85, 0), 0.07),
        (new(-0.12, 0.5, 0), 0.09), (new(-0.12, 0.08, 0), 0.08), (new(0.12, 0.5, 0), 0.09), (new(0.12, 0.08, 0), 0.08),
    ];

    static readonly (int A, int B, double Stiffness)[] Bones =
    [
        (0, 1, 1), (1, 2, 1), (1, 3, 1), (3, 4, 1), (1, 5, 1), (5, 6, 1), (2, 7, 1), (7, 8, 1), (2, 9, 1), (9, 10, 1),
        // Soft braces so it folds like a body rather than a chain: head over pelvis, the hips and shoulders apart.
        (0, 2, 0.3), (7, 9, 0.5), (3, 5, 0.5), (1, 7, 0.2), (1, 9, 0.2),
    ];

    /// <summary>A body where a player died, moving as they were (spec C.1: it persists at the death location).</summary>
    public Body SpawnRagdoll(TrainOnLine train, int owner, in PlayerState dead)
    {
        double c = Math.Cos(dead.Yaw), s = Math.Sin(dead.Yaw);
        var velocity = dead.Velocity;
        var at = dead.Position;
        var particles = Skeleton.Select(j =>
        {
            var local = at + new Double3(j.At.X * c + j.At.Z * s, j.At.Y, -j.At.X * s + j.At.Z * c);
            var p = new Particle(local, 1, j.Radius);
            p.SetVelocity(velocity, Dt);
            return p;
        }).ToArray();
        // Nobody dies standing straight: the head and chest go over backwards, or it folds into a neat pile.
        var backwards = new Double3(Math.Sin(dead.Yaw), 0, Math.Cos(dead.Yaw));
        particles[0].SetVelocity(velocity + backwards * 1.6, Dt);
        particles[1].SetVelocity(velocity + backwards * 1.0, Dt);
        var bones = Bones.Select(b => new DistanceConstraint(b.A, b.B, (Skeleton[b.A].At - Skeleton[b.B].At).Length, b.Stiffness)).ToArray();
        var body = new Body(_nextId++, BodyKind.Ragdoll, dead.Parent, new PbdBody(particles, bones) { Friction = 0.25, Bounce = 0.05 })
        {
            Owner = owner,
            LineHint = dead.LineHint,
        };
        _bodies.Add(body);
        return body;
    }

    public bool HasRagdoll(int owner) => _bodies.Any(b => b.Kind == BodyKind.Ragdoll && b.Owner == owner);

    /// <summary>Host: a body for everyone who died this tick.</summary>
    public void OnDeaths(TrainOnLine train, IEnumerable<(int Id, PlayerState State)> crew)
    {
        foreach (var (id, s) in crew)
            if (!s.Alive && !HasRagdoll(id))
                SpawnRagdoll(train, id, s);
    }

    /// <summary>
    /// A player's hands, on the host, before crew actions: Use (pressed) picks up the nearest body in reach
    /// or puts down what you're carrying; Throw throws it where you're looking. Returns true if it took the
    /// Use press, so the press doesn't also work a lever.
    /// </summary>
    public bool Handle(in PlayerState s, in PlayerIntent intent, int playerId, TrainOnLine train)
    {
        bool use = intent.Has(PlayerButtons.Use), thrown = intent.Has(PlayerButtons.Throw);
        bool usePressed = use && !_useWas.GetValueOrDefault(playerId);
        bool throwPressed = thrown && !_throwWas.GetValueOrDefault(playerId);
        _useWas[playerId] = use;
        _throwWas[playerId] = thrown;
        var carried = _bodies.FirstOrDefault(b => b.Carrier == playerId);
        if (!s.Alive)
        {
            if (carried is not null)
                Release(carried, s, train, 0);
            return false;
        }
        if (carried is not null && (throwPressed || usePressed))
        {
            double speed = throwPressed ? carried.Kind == BodyKind.Ragdoll ? Hands.RagdollThrowSpeed : Hands.ThrowSpeed : 0;
            Release(carried, s, train, speed);
            return usePressed;
        }
        if (!usePressed || carried is not null || intent.MoveZ > 0.5 || CrewActions.NearestInteractable(s, train) is not null)
            return false;
        if (InReach(s, train) is not { } nearest)
            return false;
        nearest.Carrier = playerId;
        nearest.Pbd.Wake();
        return true;
    }

    /// <summary>The loose body a player's hands would take with Use right now, if any (also the HUD's prompt).</summary>
    public Body? InReach(in PlayerState s, TrainOnLine train)
    {
        var hands = HandsAt(s, train);
        // Spec C.2: the revived can carry light things only.
        bool lightOnly = s.Has(PlayerFlags.Revived);
        return _bodies.Where(b => b.Carrier < 0 && (!lightOnly || b.Kind == BodyKind.Lamp))
            .Select(b => (b, d: (WorldCentre(b, train) - hands).Length)).Where(x => x.d <= Hands.Reach).OrderBy(x => x.d).FirstOrDefault().b;
    }

    Double3 HandsAt(in PlayerState s, TrainOnLine train)
    {
        var forward = new Double3(-Math.Sin(s.Yaw), 0, -Math.Cos(s.Yaw));
        var local = s.Position + Double3.Up * Hands.CarryHeight + forward * Hands.CarryForward;
        return s.Parent == PlayerState.World ? local : train.Frames[s.Parent].ToWorld(local);
    }

    void Release(Body b, in PlayerState s, TrainOnLine train, double speed)
    {
        b.Carrier = -1;
        var p = b.Pbd.Particles;
        int grip = b.Kind == BodyKind.Ragdoll ? 1 : 0;
        p[grip].InverseMass = 1;
        // Thrown along the view, including pitch, on top of the thrower's own motion.
        var look = new Double3(-Math.Sin(s.Yaw) * Math.Cos(s.Pitch), Math.Sin(s.Pitch), -Math.Cos(s.Yaw) * Math.Cos(s.Pitch));
        var throwVelocity = s.Velocity + look * speed + Double3.Up * (speed > 0 ? 1.5 : 0);
        for (int i = 0; i < p.Length; i++)
            p[i].SetVelocity(throwVelocity, Dt);
        b.Spin = speed > 0 ? 6 : 0;
        b.Airborne = speed > 0 ? 3 : 0;
        b.Pbd.Wake();
    }

    /// <summary>Host: one physics step for everything loose, after the train and players have moved.</summary>
    public void Step(TrainOnLine train, TrainTuning t, Func<int, PlayerState?> player)
    {
        // A hard stop or a collision throws loose things about: wake anything aboard.
        if (Math.Abs(train.Dynamics.Acceleration) > 1.0)
            foreach (var b in _bodies)
                if (b.Parent != PlayerState.World)
                    b.Pbd.Wake();
        foreach (var b in _bodies)
        {
            if (b.Carrier >= 0 && player(b.Carrier) is { } carrier)
                Carry(b, carrier, train);
            if (b.Parent != PlayerState.World && b.Parent >= train.Frames.Count)
                ToWorld(b, train, b.Parent);
            var frame = b.Parent == PlayerState.World ? (CarFrame?)null : train.Frames[b.Parent];
            // In a car's frame, its braking or pulling is felt as a push the other way.
            double carAccel = frame is { } cf ? (train.Rakes.FirstOrDefault(r => r.Consist.Vehicles.Any(v => v.Id == cf.Index))?.Acceleration ?? 0) : 0;
            var gravity = frame is { } f ? f.DirToLocal(Double3.Up * -t.Gravity + f.Back * carAccel) : Double3.Up * -t.Gravity;
            // Asleep, it stays in the frame it fell asleep in: it isn't touching anything new.
            if (b.Pbd.Asleep && b.Carrier < 0)
                continue;
            int touchedCar = -1;
            b.Pbd.Step(Dt, gravity, (pos, r) => Contact(b, frame, pos, r, train, ref touchedCar));
            if (b.Carrier < 0 && !b.Pbd.Asleep)
            {
                b.Yaw += b.Spin * Dt;
                b.Spin *= b.Pbd.Particles.Any(p => p.Contact) ? 0.5 : 0.995;
            }
            Reparent(b, train, touchedCar);
        }
    }

    /// <summary>Held at the carrier's hands: a crate rides there; a body hangs from its chest and drags.</summary>
    void Carry(Body b, in PlayerState s, TrainOnLine train)
    {
        var hands = HandsAt(s, train);
        if (b.Parent != s.Parent)
        {
            if (b.Parent == PlayerState.World)
                ToCar(b, train, s.Parent);
            else if (s.Parent == PlayerState.World)
                ToWorld(b, train, b.Parent);
            else
            {
                ToWorld(b, train, b.Parent);
                ToCar(b, train, s.Parent);
            }
        }
        var local = s.Parent == PlayerState.World ? hands : train.Frames[s.Parent].ToLocal(hands);
        int grip = b.Kind == BodyKind.Ragdoll ? 1 : 0;
        ref var p = ref b.Pbd.Particles[grip];
        p.InverseMass = 0;
        p.Position = local;
        p.Previous = local;
        b.Yaw = s.Yaw;
        b.Pbd.Wake();
    }

    /// <summary>What a particle hits: car solids and shut doors of any car nearby, and the ground.</summary>
    Contact? Contact(Body b, CarFrame? frame, Double3 pos, double r, TrainOnLine train, ref int touchedCar)
    {
        var world = frame is { } f ? f.ToWorld(pos) : pos;
        Contact? best = null;
        double bestDepth = 0;
        foreach (var g in train.Frames)
        {
            if ((g.Origin - world).Length > NearbyCar)
                continue;
            var local = g.ToLocal(world);
            var vehicle = train.Vehicles[g.Index];
            foreach (var solid in g.Shape.Solids)
                Consider(Collide.SphereBox(local, r, solid.Box.Min, solid.Box.Max), g, local, ref best, ref bestDepth, ref touchedCar);
            foreach (var door in g.Shape.DoorList)
                if (!vehicle.DoorOpen(door.Index))
                    Consider(Collide.SphereBox(local, r, door.Box.Min, door.Box.Max), g, local, ref best, ref bestDepth, ref touchedCar);
        }
        double hint = b.LineHint;
        double ground = PlayerMotor.GroundAt(world, train.Line, ref hint) + r;
        b.LineHint = hint;
        if (world.Y < ground && ground - world.Y > bestDepth)
        {
            best = new Contact(world with { Y = ground }, Double3.Up);
            touchedCar = -2; // the ground
        }
        if (best is not { } hit)
            return null;
        return frame is { } fr ? new Contact(fr.ToLocal(hit.Position), fr.DirToLocal(hit.Normal)) : hit;
    }

    static void Consider(Contact? hit, CarFrame g, Double3 local, ref Contact? best, ref double bestDepth, ref int touchedCar)
    {
        if (hit is not { } h)
            return;
        double depth = (h.Position - local).Length;
        if (best is not null && depth <= bestDepth)
            return;
        bestDepth = depth;
        best = new Contact(g.ToWorld(h.Position), g.DirToWorld(h.Normal));
        touchedCar = g.Index;
    }

    /// <summary>Touching a car: live in its frame. Off every car for a few steps: the world's.</summary>
    static void Reparent(Body b, TrainOnLine train, int touchedCar)
    {
        if (b.Carrier >= 0)
            return;
        if (touchedCar >= 0)
        {
            b.Airborne = 0;
            if (touchedCar != b.Parent)
            {
                if (b.Parent != PlayerState.World)
                    ToWorld(b, train, b.Parent);
                ToCar(b, train, touchedCar);
            }
            return;
        }
        if (b.Parent != PlayerState.World && ++b.Airborne > 3)
            ToWorld(b, train, b.Parent);
    }

    static void ToWorld(Body b, TrainOnLine train, int car)
    {
        if (car == PlayerState.World)
            return;
        var f = train.Frames[Math.Min(car, train.Frames.Count - 1)];
        b.Pbd.Transform(f.ToWorld, f.VelocityToWorld, Dt);
        b.Parent = PlayerState.World;
    }

    static void ToCar(Body b, TrainOnLine train, int car)
    {
        var f = train.Frames[car];
        b.Pbd.Transform(f.ToLocal, f.VelocityToLocal, Dt);
        b.Parent = car;
    }

    public static Double3 WorldCentre(Body b, TrainOnLine train) =>
        b.Parent == PlayerState.World || b.Parent >= train.Frames.Count ? b.Centre : train.Frames[b.Parent].ToWorld(b.Centre);

    /// <summary>Client: replaces the mirrored bodies with the host's.</summary>
    public void Mirror(IEnumerable<Body> bodies)
    {
        _bodies.Clear();
        _bodies.AddRange(bodies);
    }

    /// <summary>Removes a body entirely (tests, a revived crewmate).</summary>
    public void Remove(Body b) => _bodies.Remove(b);
}
