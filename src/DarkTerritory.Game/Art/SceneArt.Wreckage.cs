using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Art;

public sealed partial class SceneArt
{
    /// <summary>
    /// The wreck yard's heaps (GDD §18 "pull cargo off derailed trains. Unstable, unlit"; note 187) as the last train's
    /// cars (note 394): each the train's own car body, empty, on its side with its roof toward the line and its trucks
    /// in the air, wearing the derailment's wrecked damage (DamageKit) and its scars full, rusted and sooted. Placed as
    /// the sim's heap lies (GreyboxScene.Wreckage's frame: along the track's heading where it lies, turned by its own
    /// yaw, rolled a little further each time it's shifted, shuddering while it groans) and shedding dust then. False
    /// with no car in the train to draw it as (the greybox draws its boxes).
    /// </summary>
    public bool Wreckage(MeshBuilder mesh, Sim.Run.Site site, IReadOnlyList<CarFrame> frames, Double3 eye, double time, double reach)
    {
        var shape = frames.Select(f => f.Shape).FirstOrDefault(s => s.Interior is not null && s.Cab is null && s.Gun is null);
        if (shape is null)
            return false;
        var damage = Look.Tuning.Damage;
        float mid = (float)(shape.Bounds.Min.Y + shape.Bounds.Max.Y) / 2;
        foreach (var heap in site.Heaps)
        {
            if ((heap.Centre - eye).Length > reach)
                continue;
            double hint = site.Track.Length;
            var sample = site.Track.Sample(Math.Clamp(site.Track.Nearest(heap.Centre, ref hint).Distance, 0, site.Track.Length));
            double yaw = DMath.Atan2(-sample.Tangent.X, -sample.Tangent.Z) + heap.Yaw;
            var along = new Vector3(-MathF.Sin((float)yaw), 0, -MathF.Cos((float)yaw));
            var flat = Vector3.Cross(along, Vector3.UnitY);
            float roll = MathF.PI / 2 - 0.25f + 0.18f * heap.Shifts;
            float shake = heap.Groan > 0 ? 0.04f * MathF.Sin((float)time * 37) : 0;
            var up = Vector3.Normalize(Vector3.UnitY * MathF.Cos(roll + shake) + flat * MathF.Sin(roll + shake));
            var side = Vector3.Cross(up, along);
            // The body's middle where the greybox's box had its middle, so its side is on the ground as that one's was.
            var centre = (heap.Centre + Double3.Up * (1.45 - 0.1 * heap.Shifts)).RelativeTo(eye);
            var m = new Matrix4x4(side.X, side.Y, side.Z, 0, up.X, up.Y, up.Z, 0, along.X, along.Y, along.Z, 0,
                centre.X - up.X * mid, centre.Y - up.Y * mid, centre.Z - up.Z * mid, 1);
            // Two liveries among them, by the heap, so a yard of them isn't one car over and over; empty, their doors torn.
            var livery = heap.Index % 2 == 0 ? TrainKit.Livery.Planked : TrainKit.Livery.Steel;
            int variant = heap.Index % 2, seed = 700 + heap.Index;
            var body = Piece($"car:{ShapeKey(shape)}:{livery}:{variant}:False:empty", () => TrainKit.Car(Look, shape, livery, variant, load: false));
            var torn = Piece($"damage:{ShapeKey(shape)}:2:{seed}", () => DamageKit.Car(Look, shape, 2, seed));
            var scar = new Vector2(damage.ScarOf(0), Bite.ScarSeed(seed));
            mesh.Instances.Add(new MeshInstance(body, m, 0.06f, Tint: WreckTint, Scar: scar));
            mesh.Instances.Add(new MeshInstance(torn, m, 0.06f, Tint: WreckTint, Scar: scar));
            // Groaning: dust shaken off it.
            if (heap.Groan > 0)
                for (int i = 0; i < 5; i++)
                {
                    double t = (time * 0.7 + i / 5.0) % 1;
                    var p = heap.Centre + new Double3(Math.Sin(i * 2.1) * 3 * t, 0.3 + 2.2 * t, Math.Cos(i * 1.7) * 3 * t);
                    mesh.Billboard(p.RelativeTo(eye), 1.2f + 2.4f * (float)t, (float)(i + time), new Vector4(0.45f, 0.4f, 0.33f, 0.45f * (1 - (float)t)), -1, FxBlend.Alpha);
                }
        }
        return true;
    }

    /// <summary>A wreck's years out in the weather: its paint gone to rust and dirt (multiplies the body's own colour).</summary>
    static readonly Vector3 WreckTint = new(0.78f, 0.6f, 0.48f);
}
