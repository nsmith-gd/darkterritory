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
public sealed class SceneArt(Look look)
{
    readonly Dictionary<string, MeshAsset> _pieces = new();

    public Look Look { get; } = look;

    /// <summary>The line and its lineside.</summary>
    public WorldArt World { get; } = new(look);

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
    public bool Body(MeshBuilder mesh, IReadOnlyList<CarFrame> frames, Sim.Physics.Body b, Double3 eye, double heavyHalf, double time)
    {
        if (b.Kind == Sim.Physics.BodyKind.Ragdoll)
            return false;
        bool onCar = b.Parent != Sim.Player.PlayerState.World && b.Parent < frames.Count;
        if (!onCar && b.Parent != Sim.Player.PlayerState.World)
            return true;
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
        var piece = b.Kind switch
        {
            Sim.Physics.BodyKind.Cargo => Piece("prop-cargo", () => PropKit.Cargo(Look)),
            Sim.Physics.BodyKind.Heavy => Piece($"prop-heavy-{heavyHalf:0.00}", () => PropKit.Heavy(Look, (float)heavyHalf)),
            Sim.Physics.BodyKind.Crate => Piece("prop-crate", () => PropKit.Crate(Look)),
            Sim.Physics.BodyKind.Radio => Piece("prop-radio", () => PropKit.Radio(Look)),
            _ => Piece("prop-lantern", () => PropKit.Lantern(Look)),
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
    public bool Car(MeshBuilder mesh, in CarFrame frame, Double3 eye, Vehicle? vehicle, bool emergency)
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
        // In a Vigil the headlamp and tail lamp have no power (spec C.2).
        mesh.Instances.Add(new MeshInstance(body, m, emergency ? 0.06f : 1));

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
            mesh.Instances.Add(new MeshInstance(leaf, Matrix4x4.CreateTranslation((float)c.X, (float)c.Y, (float)c.Z) * m));
        }
        if (shape.Gun is { } gun)
        {
            float yaw = MathF.Atan2(-(float)gun.Facing.X, -(float)gun.Facing.Z);
            var at = gun.Position;
            mesh.Instances.Add(new MeshInstance(Piece("gun", () => TrainKit.Gun(Look)),
                Matrix4x4.CreateRotationY(yaw) * Matrix4x4.CreateTranslation((float)at.X, (float)at.Y, (float)at.Z) * m));
        }
        return true;
    }
}
