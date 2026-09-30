using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Art;

/// <summary>
/// The art pass as the scene draws it: the kit's cooked pieces placed by transform each frame, with what moves (doors,
/// the gun's facing, the lamps' glow) placed from the sim's state. One per <see cref="Look"/>; pieces are cooked the
/// first time they're wanted and kept.
/// </summary>
public sealed partial class SceneArt(Look look)
{
    readonly Dictionary<string, MeshAsset> _pieces = new();

    public Look Look { get; } = look;

    /// <summary>The line and its lineside.</summary>
    public WorldArt World { get; } = new(look);

    CreatureArt? _creatures;
    readonly Dictionary<byte, (Double3 Feet, double Time, float Speed)> _crewMotion = new();

    /// <summary>The crew and the creatures, skinned (content/art/models, tools/blender); loaded on first use.</summary>
    public CreatureArt Creatures => _creatures ??= new CreatureArt(Look);

    /// <summary>
    /// A crewmate as the crew model, walking or running by how fast they've moved since last drawn (the snapshot
    /// doesn't say; this is presentation only, so a frame's lag in the gait doesn't matter). False without the model.
    /// </summary>
    public bool Crewmate(MeshBuilder mesh, Crewmate c, Double3 eye, double time)
    {
        float speed = 0;
        if (_crewMotion.TryGetValue(c.Id, out var last) && time > last.Time)
        {
            var d = c.Feet - last.Feet;
            float moved = (float)Math.Sqrt(d.X * d.X + d.Z * d.Z);
            float now = moved / (float)(time - last.Time);
            // Smoothed a little, so the gait doesn't flicker between clips on one jittery snapshot.
            speed = float.Lerp(last.Speed, now, 0.35f);
        }
        _crewMotion[c.Id] = (c.Feet, time, speed);
        var pose = speed < 0.4f ? CrewPose.Idle : speed < 2.6f ? CrewPose.Walk : CrewPose.Run;
        var right = new Vector3((float)Math.Cos(c.Yaw), 0, (float)-Math.Sin(c.Yaw));
        var back = new Vector3((float)Math.Sin(c.Yaw), 0, (float)Math.Cos(c.Yaw));
        var m = CreatureArt.Basis(c.Feet.RelativeTo(eye), right, Vector3.UnitY, back);
        // A headset player's hands where they are (T47), the same way GreyboxScene's figure has them; the other arm (and
        // everyone's, on a keyboard) stays with the clip's swing.
        Vector3? left = null, rightHand = null;
        if (c.Hand != default || c.Other != default)
        {
            var (l, r) = Arms.Hands(c.Hand, c.Other);
            if (l != Arms.Hanging(-1))
                left = ToF(l);
            if (r != Arms.Hanging(1))
                rightHand = ToF(r);
        }
        return Creatures.Crewmate(mesh, m, pose, time, c.Variant, left, rightHand, ToF(Arms.Pole(-1)), ToF(Arms.Pole(1)));
    }

    static Vector3 ToF(Double3 d) => new((float)d.X, (float)d.Y, (float)d.Z);

    /// <summary>Smoke, steam, sparks, the lamp's beam, drifting fog.</summary>
    public Effects Effects { get; } = new(look);

    /// <summary>A cooked piece by name, made by <paramref name="make"/> the first time.</summary>
    public MeshAsset Piece(string key, Func<MeshAsset> make)
    {
        if (!_pieces.TryGetValue(key, out var piece))
            _pieces[key] = piece = make();
        return piece;
    }

    /// <summary>Every piece cooked so far (for budgets: `dt art check`).</summary>
    public IReadOnlyDictionary<string, MeshAsset> Pieces => _pieces;

    /// <summary>Pivot to handle of the brake valve's handle and of the reverser (tools/models cab_levers): each swings
    /// about its pivot, below its handle, so the handle travels where the sim's does (CabLevers), near enough.</summary>
    const float BrakeLever = 0.25f, ReverserLever = 0.9f;

    /// <summary>
    /// The driver's controls, modelled (tools/models cab_levers), where the sim has them (T29): the regulator's handle
    /// slid back along its rack as it opens, the brake valve's handle and the reverser swung about their pivots, and
    /// the blow-off valve on the cab wall at the vent. False where the models aren't built (the greybox draws them).
    /// </summary>
    public bool CabControls(MeshBuilder mesh, in CarFrame frame, Double3 eye, TrainControls controls)
    {
        var props = PropArt.Of(Look);
        if (frame.Shape.Levers is not { } levers || props.Get("lever_regulator") is not { } regulator)
            return false;
        var m = FrameMatrix(frame, eye);
        mesh.Append(regulator, Matrix4x4.CreateTranslation(ToF(levers.RegulatorAt(controls.Throttle))) * m);
        void Swung(string lever, string stand, Double3 rest, Double3 now, float length)
        {
            var pivot = ToF(rest) - new Vector3(0, length, 0);
            float angle = MathF.Asin(Math.Clamp((float)(now.Z - rest.Z) / length, -1, 1));
            if (props.Get(stand) is { } s)
                mesh.Append(s, Matrix4x4.CreateTranslation(pivot) * m);
            if (props.Get(lever) is { } l)
                mesh.Append(l, Matrix4x4.CreateRotationX(angle) * Matrix4x4.CreateTranslation(pivot) * m);
        }
        Swung("lever_brake", "brake_stand", levers.Brake, levers.BrakeAt(controls.Brake), BrakeLever);
        Swung("lever_reverser", "reverser_quadrant", levers.Reverser, levers.ReverserAt(controls.Reverser), ReverserLever);
        if (props.Get("vent_valve") is { } vent)
            foreach (var i in frame.Shape.Interactables.Where(i => i.Kind == InteractableKind.Vent))
                mesh.Append(vent, Matrix4x4.CreateTranslation(ToF(i.Position) + new Vector3(0, 1.1f, 0)) * m);
        return true;
    }

    /// <summary>A car frame's transform to camera-relative space: its axes as rows, its origin relative to the eye.</summary>
    public static Matrix4x4 FrameMatrix(in CarFrame frame, Double3 eye)
    {
        var o = frame.Origin.RelativeTo(eye);
        return new Matrix4x4(
            (float)frame.Right.X, (float)frame.Right.Y, (float)frame.Right.Z, 0,
            (float)frame.Up.X, (float)frame.Up.Y, (float)frame.Up.Z, 0,
            (float)frame.Back.X, (float)frame.Back.Y, (float)frame.Back.Z, 0,
            o.X, o.Y, o.Z, 1);
    }

    static string ShapeKey(CarShape s) =>
        $"{s.HalfWidth:0.###}x{s.HalfLength:0.###}x{s.RoofHeight:0.###}:{s.Solids.Count}:{s.Solids.Any(x => x.Part == PartKind.Coupler)}";

    /// <summary>
    /// A loose body that isn't a ragdoll (crates, freight, a lamp, a radio) as its prop, turned by its yaw in its parent's
    /// frame. A lamp lights its surroundings and glows. Returns false for what the kit doesn't draw (the dead).
    /// </summary>
    /// <summary>Facility freight comes in a few kinds of case (machine parts, ammunition, sacks of grain, medical
    /// stores); a body keeps its kind by its id.</summary>
    static readonly string[] FreightKinds = ["freight_parts", "freight_ammo", "freight_sacks", "freight_medical"];

    static string Freight(int id) => FreightKinds[(int)((uint)id * 2654435761u % (uint)FreightKinds.Length)];

    public bool Body(MeshBuilder mesh, IReadOnlyList<CarFrame> frames, Sim.Physics.Body b, Double3 eye, double heavyHalf, double time)
    {
        bool onCar = b.Parent != Sim.Player.PlayerState.World && b.Parent < frames.Count;
        if (!onCar && b.Parent != Sim.Player.PlayerState.World)
            return true;
        if (b.Kind == Sim.Physics.BodyKind.Ragdoll)
            return Corpse(mesh, frames, b, eye, onCar);
        var local = b.Pbd.Particles[0].Position;
        var at = onCar ? frames[b.Parent].ToWorld(local) : local;
        if ((at - eye).Length > 250)
            return true;
        var up = onCar ? frames[b.Parent].Up : Double3.Up;
        double yaw = b.Yaw + (onCar ? frames[b.Parent].Heading : 0);
        var u = new Vector3((float)up.X, (float)up.Y, (float)up.Z);
        var right = Vector3.Normalize(Vector3.Cross(new Vector3((float)Math.Sin(yaw), 0, (float)Math.Cos(yaw)), u));
        var back = Vector3.Cross(right, u);
        var o = at.RelativeTo(eye);
        var m = new Matrix4x4(right.X, right.Y, right.Z, 0, u.X, u.Y, u.Z, 0, back.X, back.Y, back.Z, 0, o.X, o.Y, o.Z, 1);
        // What the crew carry: the modelled props (tools/models make: stores_crate, freight_*, heavy_crate,
        // field_radio) where they're built, centred on the body like the kit's; the kit's pieces where not.
        var props = PropArt.Of(Look);
        var piece = b.Kind switch
        {
            Sim.Physics.BodyKind.Cargo => props.Get(Freight(b.Id)) ?? Piece("prop-cargo", () => PropKit.Cargo(Look)),
            Sim.Physics.BodyKind.Heavy => props.Get("heavy_crate") ?? Piece($"prop-heavy-{heavyHalf:0.00}", () => PropKit.Heavy(Look, (float)heavyHalf)),
            Sim.Physics.BodyKind.Crate => props.Get("stores_crate") ?? Piece("prop-crate", () => PropKit.Crate(Look)),
            Sim.Physics.BodyKind.Radio => props.Get("field_radio") ?? Piece("prop-radio", () => PropKit.Radio(Look)),
            // The hand lamp: the sourced lantern (tools/models hand_lantern) where it's built.
            _ => PropArt.Of(Look).Get("hand_lantern") ?? Piece("prop-lantern", () => PropKit.Lantern(Look)),
        };
        mesh.Append(piece, m);
        if (b.Kind == Sim.Physics.BodyKind.Lamp)
        {
            // A hand lamp's glow round it, flickering a little (pipeline: "dynamic point lights with flicker curves").
            float flicker = Flicker(time, b.Id);
            mesh.Billboard(o, 0.7f * flicker, 0, new Vector4(Palette.LampAmber * 0.55f * flicker, 1), -1, FxBlend.Additive);
        }
        return true;
    }

    readonly Vector3[] _joints = new Vector3[CreatureArt.RagdollJoints];

    /// <summary>A ragdoll as the crew model lying as its joints lie; false (the greybox's bones) if the model isn't there.</summary>
    bool Corpse(MeshBuilder mesh, IReadOnlyList<CarFrame> frames, Sim.Physics.Body b, Double3 eye, bool onCar)
    {
        var ps = b.Pbd.Particles;
        if (ps.Length < _joints.Length)
            return false;
        var near = onCar ? frames[b.Parent].ToWorld(ps[2].Position) : ps[2].Position;
        if ((near - eye).Length > 250)
            return true;
        for (int i = 0; i < _joints.Length; i++)
            _joints[i] = (onCar ? frames[b.Parent].ToWorld(ps[i].Position) : ps[i].Position).RelativeTo(eye);
        return Creatures.Corpse(mesh, _joints, b.Owner);
    }

    /// <summary>
    /// The backhead's dials as the engine stands (pipeline "diegetic readouts: needle and lever meshes"): each needle
    /// turned through its dial's 270° sweep by its fraction (pressure, heat, water, speed; 0..1).
    /// </summary>
    public void Gauges(MeshBuilder mesh, in CarFrame engine, Double3 eye, ReadOnlySpan<float> fractions)
    {
        if (engine.Shape.Cab is null || (engine.Origin - eye).Length > 30)
            return;
        var m = FrameMatrix(engine, eye);
        var needle = Piece("needle", () => TrainKit.Needle(Look));
        for (int i = 0; i < 4 && i < fractions.Length; i++)
        {
            // From 7:30 round to 4:30, clockwise as you face it: the dial faces +Z (back into the cab).
            float angle = (0.75f - 1.5f * Math.Clamp(fractions[i], 0, 1)) * MathF.PI;
            var c = TrainKit.GaugeCentre(engine.Shape, i);
            mesh.Append(needle, Matrix4x4.CreateRotationZ(angle) * Matrix4x4.CreateTranslation(c) * m);
        }
    }

    /// <summary>A car's two lanterns, hanging on their chains from the carlines where its lights are.</summary>
    public void CarLamps(MeshBuilder mesh, in CarFrame frame, Double3 eye)
    {
        if (frame.Shape.Interior is not { } room || (frame.Origin - eye).Length > 80)
            return;
        var m = FrameMatrix(frame, eye);
        var lamps = Piece($"lamps:{room.Min.Y:0.00}:{room.Max.Y:0.00}:{room.HalfSize.Z:0.00}:{room.Centre.Z:0.00}", () =>
        {
            var k = new Kit(Look, 71);
            // The sourced hand lantern hung by its ring where it's built (tools/models), the kit's cage where it isn't.
            var props = PropArt.Of(Look);
            var sourced = props.Get("hand_lantern");
            var lantern = sourced ?? PropKit.Lantern(Look);
            var ring = sourced is not null ? props.Socket("hand_lantern", "hang") ?? new Vector3(0, 0.36f, 0) : new Vector3(0, 0.26f, 0);
            var flame = sourced is not null ? props.Socket("hand_lantern", "lamp") ?? new Vector3(0, 0.15f, 0) : Vector3.Zero;
            var chain = Chain(Look);
            foreach (var at in LampPositions(room))
            {
                // The flame where the light is (LampPositions); the ring under the chain.
                var foot = at - flame;
                float top = foot.Y + ring.Y;
                float drop = (float)room.Max.Y - top;
                k.Append(chain, Matrix4x4.CreateScale(0.5f, drop / 0.18f, 0.5f) * Matrix4x4.CreateTranslation(new Vector3(at.X, top + drop / 2, at.Z)));
                k.Append(lantern, Matrix4x4.CreateTranslation(foot));
            }
            return k.Build("car-lamps");
        });
        mesh.Instances.Add(new MeshInstance(lamps, m, 1, default));
        var glow = Palette.LampAmber * 0.35f;
        foreach (var at in LampPositions(room))
            mesh.Billboard(Vector3.Transform(at, m), 0.6f, 0, new Vector4(glow, 1), -1, FxBlend.Additive);
    }

    static IEnumerable<Vector3> LampPositions(Box room)
    {
        foreach (double z in new[] { -room.HalfSize.Z * 0.5, room.HalfSize.Z * 0.5 })
            yield return new Vector3(0, (float)room.Max.Y - 0.42f, (float)(room.Centre.Z + z));
    }

    static MeshAsset Chain(Look? look)
    {
        var k = new Kit(look, 70);
        k.Use("rust_heavy", Palette.SootBlack, 0.6f, 0.4f);
        k.Rod(new Vector3(0, -0.09f, 0), new Vector3(0, 0.09f, 0), 0.01f);
        return k.Build("chain");
    }

    /// <summary>A flame's flicker, 0.85..1.05, different for each light and steady enough not to strobe.</summary>
    public static float Flicker(double time, int id)
    {
        double t = time * 7 + id * 13.7;
        return (float)(0.95 + 0.05 * Math.Sin(t) + 0.03 * Math.Sin(t * 2.7 + 1.3) + 0.02 * Math.Sin(t * 5.3 + 0.4));
    }

    /// <summary>
    /// A car: its body from the kit, its doors where the vehicle has them (shut in the doorway, or slid aside), and its gun
    /// turned the way it faces. Returns false when the kit can't draw this car (so the greybox does).
    /// </summary>
    public bool Car(MeshBuilder mesh, in CarFrame frame, Double3 eye, Vehicle? vehicle, long tick = -1)
    {
        var shape = frame.Shape;
        var m = FrameMatrix(frame, eye);
        bool engine = shape.Cab is not null;
        if (!engine && shape.Interior is null)
            return false;
        var livery = TrainKit.LiveryOf(frame.Index);
        int variant = frame.Index % 2;
        string key = engine ? $"engine:{ShapeKey(shape)}" : $"car:{ShapeKey(shape)}:{livery}:{variant}:{shape.Gun is not null}";
        var body = Piece(key, () => engine ? TrainKit.Engine(Look, shape, 0) : TrainKit.Car(Look, shape, livery, variant));
        // Wear and tear off the car's integrity (look.json "damage"): the scar mask over the body and doors, seeded by
        // the car so its scars stay where they are, and past the first state the torn plate the mask can't draw.
        var damage = Look.Tuning.Damage;
        double integrity = vehicle?.Integrity ?? 1;
        int seed = vehicle?.Id ?? frame.Index;
        var scar = new Vector2(damage.ScarOf(integrity), seed * 0.618f % 1 * 97);
        mesh.Instances.Add(new MeshInstance(body, m, 1, Scar: scar));
        int state = damage.StateOf(integrity);
        if (state > 0 && !engine)
            mesh.Instances.Add(new MeshInstance(Piece($"damage:{ShapeKey(shape)}:{state}:{seed}", () => DamageKit.Car(Look, shape, state, seed)), m));

        foreach (var door in shape.DoorList)
        {
            bool open = vehicle?.DoorOpen(door.Index) ?? false;
            var box = door.Box;
            bool side = box.Max.Z - box.Min.Z > box.Max.X - box.Min.X;
            if (open)
            {
                var move = side
                    ? new Double3(box.Min.X < 0 ? -0.12 : 0.12, 0, box.Max.Z - box.Min.Z)
                    : new Double3(box.Max.X - box.Min.X, 0, box.Min.Z < 0 ? 0.12 : -0.12);
                box = new Box(box.Min + move, box.Max + move);
            }
            var size = new Vector3((float)(box.Max.X - box.Min.X), (float)(box.Max.Y - box.Min.Y), (float)(box.Max.Z - box.Min.Z));
            var leaf = Piece($"door:{side}:{size.X:0.##}x{size.Y:0.##}x{size.Z:0.##}", () => TrainKit.Door(Look, size, side));
            var c = box.Centre;
            mesh.Instances.Add(new MeshInstance(leaf, Matrix4x4.CreateTranslation((float)c.X, (float)c.Y, (float)c.Z) * m, Scar: scar));
        }
        if (shape.Gun is { } gun)
        {
            float yaw = MathF.Atan2(-(float)gun.Facing.X, -(float)gun.Facing.Z);
            var at = gun.Position;
            var gunM = Matrix4x4.CreateRotationY(yaw) * Matrix4x4.CreateTranslation((float)at.X, (float)at.Y, (float)at.Z) * m;
            mesh.Instances.Add(new MeshInstance(Piece("gun", () => TrainKit.Gun(Look)), gunM));
            // The muzzle flash, for the two ticks after a round (pipeline VFX: "muzzle flash", additive): a hot star
            // at the muzzle and a burst of light over the roof and whatever it's aimed at.
            if (vehicle is { Gun.LastShotTick: > 0 } v && tick >= v.Gun.LastShotTick && tick - v.Gun.LastShotTick <= 2)
            {
                var muzzle = Vector3.Transform(new Vector3(0, -0.02f, -1.5f), gunM);
                float fade = 1 - (tick - v.Gun.LastShotTick) / 3f;
                int shot = (int)(v.Gun.LastShotTick % 4);
                mesh.Billboard(muzzle, 0.9f * fade, shot * 0.8f, new Vector4(1.0f, 0.75f, 0.4f, fade), -1, FxBlend.Additive);
                mesh.Billboard(muzzle, 0.35f, shot * 1.3f, new Vector4(1.2f, 1.0f, 0.8f, fade), -1, FxBlend.Additive, stretch: 2.5f);
                mesh.PointLights.Add(new PointLight(muzzle, new Vector3(1.6f, 1.1f, 0.6f) * fade, 12));
            }
        }
        return true;
    }
}
