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
    /// <summary>How hot the firebox is, 0..1: the glow in the cab is how the Boiler reads the fire.</summary>
    public float FireGlow { get; set; } = 0.7f;

    public void Build(MeshBuilder mesh, TrainOnLine train, Double3 eye) =>
        Build(mesh, train.Line, train.Frames, train.Dynamics.Distance, eye);

    /// <param name="frames">Car frames to draw, e.g. interpolated between ticks.</param>
    /// <param name="hint">Any distance along the line near the eye, to start the nearest-point search.</param>
    public void Build(MeshBuilder mesh, RailLine line, IReadOnlyList<CarFrame> frames, double hint, Double3 eye)
    {
        mesh.Clear();
        double centre = NearestDistance(line, eye, hint);
        double from = Math.Max(0, centre - DrawDistance), to = Math.Min(line.Length, centre + DrawDistance);

        Track(mesh, line, eye, from, to, centre);
        Lineside(mesh, line, eye, from, to);
        foreach (var frame in frames)
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

    void Car(MeshBuilder mesh, CarFrame frame, Double3 eye)
    {
        var right = ToF(frame.Right);
        var up = ToF(frame.Up);
        var back = ToF(frame.Back);
        var o = V(frame.Origin, eye);
        Vector3 L(double x, double y, double z) => o + right * (float)x + up * (float)y + back * (float)z;
        void Draw(Box box, Vector3 color) => mesh.Box(L(box.Centre.X, box.Centre.Y, box.Centre.Z), right, up, back, ToF(box.HalfSize), color);

        var shape = frame.Shape;
        bool engine = frame.Index == 0;
        // What you see is what you collide with: every solid is drawn, coloured by what it is.
        foreach (var solid in shape.Solids)
            Draw(solid.Box, PartColour(solid.Part, frame.Index));

        double half = shape.HalfLength;
        if (engine)
        {
            mesh.Emissive = 1;
            foreach (var i in shape.Interactables.Where(i => i.Kind == InteractableKind.Firebox))
                Draw(Box.FromCentre(i.Position + new Double3(0, 0.7, -0.17), new Double3(0.3, 0.2, 0.02)), Palette.FurnaceOrange * (0.15f + 0.85f * FireGlow));
            Draw(Box.FromCentre(new Double3(0, 2.8, -half - 0.05), new Double3(0.35, 0.35, 0.1)), Palette.LampAmber);
            mesh.Emissive = 0;
            foreach (var i in shape.Interactables.Where(i => i.Kind == InteractableKind.Vent))
                Draw(Box.FromCentre(i.Position + new Double3(0, 1.1, 0), new Double3(0.12, 0.12, 0.04)), Palette.TarnishedBrass);
        }
        else
        {
            // Roof walkway plank down the safe centreline.
            Draw(new Box(new Double3(-0.35, shape.RoofHeight, -half + 0.2), new Double3(0.35, shape.RoofHeight + 0.04, half - 0.2)), Palette.TarnishedBrass);
        }
        foreach (var ladder in shape.Ladders)
        {
            // Rails run up the face the ladder is fixed to: thin across it, a hand-width wide along it.
            bool side = Math.Abs(ladder.Inward.X) > 0;
            var h = new Double3(side ? 0.05 : 0.25, ladder.Top / 2, side ? 0.25 : 0.05);
            Draw(Box.FromCentre(ladder.Foot + new Double3(0, ladder.Top / 2, 0), h), Palette.IronGrey);
        }
        // Wheel sets under both ends.
        foreach (double z in new[] { -half * 0.6, half * 0.6 })
            Draw(Box.FromCentre(new Double3(0, 0.45, z), new Double3(shape.HalfWidth * 0.8, 0.4, 1.2)), Palette.SootBlack);
    }

    static Vector3 PartColour(PartKind part, int car) => part switch
    {
        PartKind.Body => car % 3 == 0 ? Palette.RustRed : Palette.DeepBrown,
        PartKind.Chassis => Palette.Charcoal,
        PartKind.Boiler or PartKind.Stack => Palette.SootBlack,
        PartKind.CabWall or PartKind.CabRoof or PartKind.Coupler => Palette.IronGrey,
        PartKind.Tender => Palette.Charcoal,
        _ => Palette.IronGrey,
    };

    static Vector3 ToF(Double3 d) => new((float)d.X, (float)d.Y, (float)d.Z);
}
