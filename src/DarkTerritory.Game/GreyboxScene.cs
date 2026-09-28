using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game;

/// <summary>
/// Builds greybox geometry for the line and the train around a camera. Positions are emitted
/// relative to the camera (floating origin), so a train 40 km down the line renders as precisely
/// as one at the yard.
/// </summary>
public sealed class GreyboxScene
{
    public float DrawDistance { get; init; } = 400;
    public int Seed { get; init; } = 7;

    public void Build(MeshBuilder mesh, TrainOnLine train, Double3 eye)
    {
        mesh.Clear();
        var line = train.Line;
        double centre = NearestDistance(line, eye, train.Dynamics.Distance);
        double from = Math.Max(0, centre - DrawDistance), to = Math.Min(line.Length, centre + DrawDistance);

        Track(mesh, line, eye, from, to, centre);
        Lineside(mesh, line, eye, from, to);
        foreach (var frame in train.Frames)
            Car(mesh, frame, eye);
    }

    /// <summary>Finds the along-line distance nearest a point, starting from a guess.</summary>
    public static double NearestDistance(RailLine line, Double3 p, double guess)
    {
        double s = guess;
        for (int i = 0; i < 8; i++)
        {
            var sample = line.Sample(s);
            s = Math.Clamp(sample.Distance + Double3.Dot(p - sample.Position, sample.Tangent), 0, line.Length);
        }
        return s;
    }

    static Vector3 V(Double3 p, Double3 eye) => p.RelativeTo(eye);

    void Track(MeshBuilder mesh, RailLine line, Double3 eye, double from, double to, double centre)
    {
        const double step = 5, gauge = 0.72, sleeperPitch = 0.75;
        const double groundHalfWidth = 90, bedHalfWidth = 1.8;
        for (double s = from; s < to; s += step)
        {
            var a = line.Sample(s);
            var b = line.Sample(Math.Min(s + step, to));
            var ra = Double3.Cross(a.Tangent, Double3.Up).Normalized;
            var rb = Double3.Cross(b.Tangent, Double3.Up).Normalized;
            var down = new Double3(0, -0.02, 0);

            // Ground either side, then the raised ballast bed, then rails.
            mesh.Quad(V(a.Position - ra * groundHalfWidth + down * 10, eye), V(a.Position - ra * bedHalfWidth + down, eye),
                      V(b.Position - rb * bedHalfWidth + down, eye), V(b.Position - rb * groundHalfWidth + down * 10, eye), Palette.MuddyOlive);
            mesh.Quad(V(a.Position + ra * bedHalfWidth + down, eye), V(a.Position + ra * groundHalfWidth + down * 10, eye),
                      V(b.Position + rb * groundHalfWidth + down * 10, eye), V(b.Position + rb * bedHalfWidth + down, eye), Palette.MuddyOlive);
            mesh.Quad(V(a.Position - ra * bedHalfWidth, eye), V(a.Position + ra * bedHalfWidth, eye),
                      V(b.Position + rb * bedHalfWidth, eye), V(b.Position - rb * bedHalfWidth, eye), Palette.Ballast);
            foreach (var side in new[] { -1.0, 1.0 })
            {
                var p0 = V(a.Position + ra * (side * gauge), eye);
                var p1 = V(b.Position + rb * (side * gauge), eye);
                var mid = (p0 + p1) / 2;
                var fwd = Vector3.Normalize(p1 - p0);
                var right = Vector3.Normalize(Vector3.Cross(fwd, Vector3.UnitY));
                mesh.Box(mid + new Vector3(0, 0.12f, 0), right, Vector3.UnitY, -fwd, new Vector3(0.04f, 0.07f, (p1 - p0).Length() / 2), Palette.IronGrey);
            }
        }
        // Sleepers only near the eye: past ~150 m the fog has eaten them anyway.
        double sleeperFrom = Math.Max(from, centre - 150), sleeperTo = Math.Min(to, centre + 150);
        for (double s = Math.Ceiling(sleeperFrom / sleeperPitch) * sleeperPitch; s < sleeperTo; s += sleeperPitch)
        {
            var t = line.Sample(s);
            var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
            mesh.Box(V(t.Position, eye) + new Vector3(0, 0.03f, 0), ToF(right), Vector3.UnitY, ToF(t.Tangent * -1), new Vector3(1.25f, 0.05f, 0.12f), Palette.DeepBrown);
        }
    }

    void Lineside(MeshBuilder mesh, RailLine line, Double3 eye, double from, double to)
    {
        // Telegraph poles every 50 m and a scatter of pines: depth cues for fog and speed.
        for (double s = Math.Ceiling(from / 50) * 50; s < to; s += 50)
        {
            var t = line.Sample(s);
            var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
            var foot = V(t.Position + right * 4.5, eye);
            mesh.AxisBox(foot + new Vector3(-0.12f, 0, -0.12f), foot + new Vector3(0.12f, 7.5f, 0.12f), Palette.DeepBrown);
            mesh.AxisBox(foot + new Vector3(-0.9f, 6.8f, -0.06f), foot + new Vector3(0.9f, 6.95f, 0.06f), Palette.DeepBrown);
        }
        for (double s = Math.Floor(from / 12) * 12; s < to; s += 12)
        {
            var rng = new Random(HashCode.Combine(Seed, (int)(s / 12)));
            for (int k = 0; k < 3; k++)
            {
                double side = rng.Next(2) == 0 ? -1 : 1;
                double offset = side * (9 + rng.NextDouble() * 70);
                var t = line.Sample(s + rng.NextDouble() * 12);
                var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
                var foot = V(t.Position + right * offset + new Double3(0, -0.2, 0), eye);
                float h = 7 + (float)rng.NextDouble() * 9;
                mesh.AxisBox(foot + new Vector3(-0.2f, 0, -0.2f), foot + new Vector3(0.2f, h * 0.35f, 0.2f), Palette.DeepBrown);
                mesh.Pyramid(foot + new Vector3(0, h * 0.25f, 0), h * 0.22f, h * 0.8f, Palette.PineDark);
            }
        }
    }

    static void Car(MeshBuilder mesh, CarFrame frame, Double3 eye)
    {
        var right = ToF(frame.Right);
        var up = ToF(frame.Up);
        var back = ToF(frame.Back);
        var o = V(frame.Origin, eye);
        Vector3 L(double x, double y, double z) => o + right * (float)x + up * (float)y + back * (float)z;

        var body = frame.Shape.Body;
        var half = ToF((body.Max - body.Min) * 0.5);
        var centre = L(0, body.Max.Y / 2 + 0.25, 0);
        bool engine = frame.Index == 0;
        if (engine)
        {
            // Boiler forward, open cab at the back: the engine must read by silhouette alone (GDD §26).
            double l = half.Z;
            float w = half.X;
            mesh.Box(L(0, 1.0, 0), right, up, back, new Vector3(w, 0.4f, (float)l), Palette.Charcoal);
            mesh.Box(L(0, 2.5, -l * 0.3), right, up, back, new Vector3(0.95f, 1.1f, (float)l * 0.65f), Palette.SootBlack);
            mesh.Box(L(0, 4.1, -l * 0.8), right, up, back, new Vector3(0.35f, 0.6f, 0.35f), Palette.SootBlack);
            // Cab shell: roof, waist-high sides, back wall with a doorway, pillars at the corners.
            double cabFront = l * 0.35, cabBack = l;
            double cabMid = (cabFront + cabBack) / 2;
            float cabHalf = (float)(cabBack - cabFront) / 2;
            mesh.Box(L(0, 4.3, cabMid), right, up, back, new Vector3(w + 0.1f, 0.1f, cabHalf + 0.1f), Palette.IronGrey);
            foreach (int side in new[] { -1, 1 })
            {
                mesh.Box(L(side * (w - 0.05), 2.0, cabMid), right, up, back, new Vector3(0.05f, 0.6f, cabHalf), Palette.IronGrey);
                mesh.Box(L(side * (w - 0.05), 3.3, cabFront + 0.1), right, up, back, new Vector3(0.05f, 1.0f, 0.1f), Palette.IronGrey);
                mesh.Box(L(side * (w - 0.05), 3.3, cabBack - 0.1), right, up, back, new Vector3(0.05f, 1.0f, 0.1f), Palette.IronGrey);
                mesh.Box(L(side * (w * 0.62), 2.8, cabBack - 0.05), right, up, back, new Vector3(w * 0.38f, 1.4f, 0.05f), Palette.IronGrey);
            }
            // Firebox door glow on the boiler backhead.
            mesh.Emissive = 1;
            mesh.Box(L(0, 2.1, cabFront + 0.03), right, up, back, new Vector3(0.3f, 0.2f, 0.02f), Palette.FurnaceOrange);
            mesh.Box(L(0, 2.8, -l - 0.05), right, up, back, new Vector3(0.35f, 0.35f, 0.1f), Palette.LampAmber);
            mesh.Emissive = 0;
        }
        else
        {
            mesh.Box(centre, right, up, back, new Vector3(half.X, half.Y - 0.25f, half.Z), frame.Index % 3 == 0 ? Palette.RustRed : Palette.DeepBrown);
            // Roof walkway plank down the safe centreline.
            mesh.Box(L(0, body.Max.Y + 0.02, 0), right, up, back, new Vector3(0.35f, 0.03f, half.Z - 0.2f), Palette.TarnishedBrass);
            foreach (var ladder in frame.Shape.Ladders)
                mesh.Box(L(ladder.X, body.Max.Y / 2, ladder.Z), right, up, back, new Vector3(0.05f, (float)body.Max.Y / 2, 0.25f), Palette.IronGrey);
        }
        // Wheel sets under both ends.
        foreach (double z in new[] { -half.Z * 0.6, half.Z * 0.6 })
            mesh.Box(L(0, 0.45, z), right, up, back, new Vector3(half.X * 0.8f, 0.4f, 1.2f), Palette.SootBlack);
        if (frame.Shape.Coupler is { } c)
        {
            var mid = (c.Min + c.Max) * 0.5;
            var h = ToF((c.Max - c.Min) * 0.5);
            mesh.Box(L(mid.X, mid.Y, mid.Z), right, up, back, h, Palette.IronGrey);
        }
    }

    static Vector3 ToF(Double3 d) => new((float)d.X, (float)d.Y, (float)d.Z);
}
