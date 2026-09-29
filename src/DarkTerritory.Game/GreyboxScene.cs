using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
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
    /// <summary>Tunnels, bridges, facilities and hazards to draw along the line, when it's a generated route.</summary>
    public Route? Route { get; set; }
    /// <summary>Live enemies to draw. When set, the route's Sleepers come from here rather than its features.</summary>
    public IReadOnlyList<Enemy>? Enemies { get; set; }
    /// <summary>Vehicle state for doors (open or shut). Without it every door is drawn shut.</summary>
    public IReadOnlyList<Vehicle>? Vehicles { get; set; }
    /// <summary>Other players, drawn as greybox figures.</summary>
    public IReadOnlyList<Crewmate>? Crew { get; set; }

    /// <summary>Depth of the valley under a bridge.</summary>
    const double ValleyDepth = 18;

    public void Build(MeshBuilder mesh, TrainOnLine train, Double3 eye)
    {
        Vehicles ??= train.Vehicles;
        Build(mesh, train.Line, train.Frames, train.Dynamics.Distance, eye);
    }

    /// <param name="frames">Car frames to draw, e.g. interpolated between ticks.</param>
    /// <param name="hint">Any distance along the line near the eye, to start the nearest-point search.</param>
    public void Build(MeshBuilder mesh, RailLine line, IReadOnlyList<CarFrame> frames, double hint, Double3 eye)
    {
        mesh.Clear();
        double centre = NearestDistance(line, eye, hint);
        double from = Math.Max(0, centre - DrawDistance), to = Math.Min(line.Length, centre + DrawDistance);

        Track(mesh, line, eye, from, to, centre);
        Lineside(mesh, line, eye, from, to);
        if (Route is not null)
            Features(mesh, line, eye, from, to);
        // Practical lights first, so everything built after is lit by them: each car's lamps, the firebox.
        foreach (var frame in frames)
        {
            if ((frame.Origin - eye).Length > 60)
                continue;
            if (frame.Shape.Interior is { } room)
                foreach (double z in new[] { -room.HalfSize.Z * 0.5, room.HalfSize.Z * 0.5 })
                    mesh.PointLights.Add(new PointLight(V(frame.ToWorld(new Double3(0, room.Max.Y - 0.2, room.Centre.Z + z)), eye), Palette.LampAmber * 1.6f, 7.5f));
            foreach (var i in frame.Shape.Interactables.Where(i => i.Kind == InteractableKind.Firebox))
                mesh.PointLights.Add(new PointLight(V(frame.ToWorld(i.Position + new Double3(0, 0.7, 0.3)), eye), Palette.FurnaceOrange * (0.6f + 1.6f * FireGlow), 5f));
        }
        foreach (var frame in frames)
            Car(mesh, frame, eye);
        if (Enemies is not null)
            foreach (var e in Enemies)
                if (!e.Gone)
                    DrawEnemy(mesh, line, frames, e, eye, from, to);
        if (Crew is not null)
            foreach (var c in Crew)
                DrawCrewmate(mesh, c, eye);
    }

    /// <summary>A crewmate: coat, head and a lamp at the chest so you can find each other in the dark. Dead ones lie down.</summary>
    static void DrawCrewmate(MeshBuilder mesh, Crewmate c, Double3 eye)
    {
        var right = new Vector3((float)Math.Cos(c.Yaw), 0, (float)-Math.Sin(c.Yaw));
        var back = new Vector3((float)Math.Sin(c.Yaw), 0, (float)Math.Cos(c.Yaw));
        var o = V(c.Feet, eye);
        if (!c.Alive)
        {
            mesh.Box(o + Vector3.UnitY * 0.15f, right, Vector3.UnitY, back, new Vector3(0.25f, 0.15f, 0.9f), Palette.DeepBrown);
            return;
        }
        mesh.Box(o + Vector3.UnitY * 0.45f, right, Vector3.UnitY, back, new Vector3(0.16f, 0.45f, 0.12f), Palette.Charcoal);
        mesh.Box(o + Vector3.UnitY * 1.2f, right, Vector3.UnitY, back, new Vector3(0.24f, 0.33f, 0.15f), Palette.DeepBrown);
        mesh.Box(o + Vector3.UnitY * 1.68f, right, Vector3.UnitY, back, new Vector3(0.12f, 0.13f, 0.12f), Palette.Corrupted);
        mesh.Emissive = 1;
        mesh.Box(o + Vector3.UnitY * 1.3f - back * 0.16f, right, Vector3.UnitY, back, new Vector3(0.05f, 0.05f, 0.02f), Palette.LampAmber);
        mesh.Emissive = 0;
    }

    /// <summary>
    /// Greybox stand-ins, each readable by silhouette and by its telegraph (App. A.1: the tell must be
    /// perceivable). The real creatures come with the art pass; these exist to make pacing watchable.
    /// </summary>
    static void DrawEnemy(MeshBuilder mesh, RailLine line, IReadOnlyList<CarFrame> frames, Enemy e, Double3 eye, double from, double to)
    {
        // A basis for the enemy: its car's, or the line's at its distance.
        Double3 origin, right, up = Double3.Up, back;
        if (e.Attached >= 0)
        {
            if (e.Attached >= frames.Count)
                return;
            var f = frames[e.Attached];
            origin = f.ToWorld(e.Local);
            (right, up, back) = (f.Right, f.Up, f.Back);
        }
        else
        {
            if (e.LineDistance < from || e.LineDistance > to)
                return;
            var t = line.Sample(e.LineDistance);
            right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
            back = t.Tangent * -1;
            origin = t.Position + right * e.Lateral + Double3.Up * e.Height;
        }
        var o = V(origin, eye);
        var (r, u, b) = (ToF(right), ToF(up), ToF(back));
        Vector3 L(double x, double y, double z) => o + r * (float)x + u * (float)y + b * (float)z;
        void Draw(double x, double y, double z, double hx, double hy, double hz, Vector3 colour) =>
            mesh.Box(L(x, y, z), r, u, b, new Vector3((float)hx, (float)hy, (float)hz), colour);
        float pulse = (float)(0.5 + 0.5 * Math.Sin(e.PhaseSeconds * 9));

        switch (e.Kind)
        {
            case EnemyKind.Sleepers:
                // Ties that aren't: across the rail, and braced (lifting, writhing) once they've telegraphed.
                bool braced = e.Phase >= SpinePhase.Telegraph;
                for (int i = 0; i < 6; i++)
                {
                    double lift = braced ? 0.12 + 0.08 * Math.Sin(e.PhaseSeconds * 6 + i) : 0;
                    Draw(0, lift, -i * 2.6, 1.5, 0.14, 0.45, Palette.Corrupted);
                }
                break;
            case EnemyKind.CinderHound:
                // Low, long, and lit from inside: the heat they hunt by is what you see of them at night.
                bool aboard = e.Phase == SpinePhase.Punish;
                double y = e.Attached >= 0 ? 0.35 : 0; // boarded: standing on the roof, not centred in it
                Draw(0, y, 0, 0.22, 0.28, 0.65, Palette.SootBlack);
                Draw(0, y + 0.2, -0.75, 0.16, 0.16, 0.25, Palette.SootBlack);
                mesh.Emissive = 1;
                // Cracks of ember along the flanks and back, proud of the body so they read from any side.
                Draw(0, y + 0.02, 0, 0.24, 0.1, 0.45, Palette.FurnaceOrange * (0.5f + 0.5f * (aboard ? pulse : 0.7f)));
                Draw(0, y + 0.29, 0.1, 0.08, 0.02, 0.4, Palette.FurnaceOrange * 0.8f);
                Draw(0.07, y + 0.26, -1.01, 0.03, 0.03, 0.02, Palette.LampAmber);
                Draw(-0.07, y + 0.26, -1.01, 0.03, 0.03, 0.02, Palette.LampAmber);
                mesh.Emissive = 0;
                break;
            case EnemyKind.Clinger:
                // A bulge on the hull; the drill point brightens as it works through (Extra = drill progress).
                double outward = Math.Sign(e.Local.X);
                Draw(outward * 0.2, 0, 0, 0.2, 0.55, 0.8, Palette.Corrupted);
                mesh.Emissive = 1;
                float drilled = (float)Math.Clamp(e.Extra, 0, 1);
                var bite = e.Phase == SpinePhase.Punish ? Palette.FurnaceOrange * (0.6f + 0.4f * pulse) : Palette.FurnaceOrange * (0.1f + 0.5f * drilled);
                // The seam where it's working through shows on its back, away from the hull.
                Draw(outward * 0.41, 0.1, 0, 0.02, 0.06, 0.5, bite);
                mesh.Emissive = 0;
                break;
            case EnemyKind.Hollow:
                if (e.Phase == SpinePhase.Telegraph)
                {
                    // Soot falling into the cab from the stack.
                    for (int i = 0; i < 4; i++)
                        Draw(0.3 * Math.Sin(i * 1.7), 1.0 - (e.PhaseSeconds * 2 + i * 0.4) % 1.6, 0.3 * Math.Cos(i * 2.3), 0.04, 0.2, 0.04, Palette.SootBlack);
                }
                else
                {
                    // Tall and thin, blacker than the cab.
                    Draw(0, -0.1, 0, 0.22, 0.95, 0.16, Palette.SootBlack);
                    mesh.Emissive = 1;
                    Draw(0.07, 0.7, -0.17, 0.025, 0.02, 0.01, Palette.Corrupted * 1.6f);
                    Draw(-0.07, 0.7, -0.17, 0.025, 0.02, 0.01, Palette.Corrupted * 1.6f);
                    mesh.Emissive = 0;
                }
                break;
        }
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
            bool bridge = Route?.BridgeAt(s + step / 2) is not null;

            // Ground either side, then the raised ballast bed, then rails. Under a bridge the ground
            // drops into a valley and there's no ballast: the deck is drawn with the features.
            var valley = bridge ? new Double3(0, -ValleyDepth, 0) : default;
            double inner = bridge ? 0 : bedHalfWidth;
            mesh.Quad(V(a.Position - ra * groundHalfWidth + down * 10 + valley, eye), V(a.Position - ra * inner + down + valley, eye),
                      V(b.Position - rb * inner + down + valley, eye), V(b.Position - rb * groundHalfWidth + down * 10 + valley, eye), Palette.MuddyOlive);
            mesh.Quad(V(a.Position + ra * inner + down + valley, eye), V(a.Position + ra * groundHalfWidth + down * 10 + valley, eye),
                      V(b.Position + rb * groundHalfWidth + down * 10 + valley, eye), V(b.Position + rb * inner + down + valley, eye), Palette.MuddyOlive);
            if (!bridge)
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
            if (Route is not null && (Route.InTunnel(s) || Route.BridgeAt(s) is not null))
                continue;
            var t = line.Sample(s);
            var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
            var foot = V(t.Position + right * 4.5, eye);
            mesh.AxisBox(foot + new Vector3(-0.12f, 0, -0.12f), foot + new Vector3(0.12f, 7.5f, 0.12f), Palette.DeepBrown);
            mesh.AxisBox(foot + new Vector3(-0.9f, 6.8f, -0.06f), foot + new Vector3(0.9f, 6.95f, 0.06f), Palette.DeepBrown);
        }
        for (double s = Math.Floor(from / 12) * 12; s < to; s += 12)
        {
            if (Route is not null && (Route.InTunnel(s) || Route.BridgeAt(s) is not null))
                continue;
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

    /// <summary>A box following the line: <paramref name="lateral"/> metres right of centre, base at <paramref name="y"/> above rail.</summary>
    static void Along(MeshBuilder mesh, RailLine line, Double3 eye, double s, double length, double lateral, double y, double halfWidth, double height, Vector3 color)
    {
        var t = line.Sample(s + length / 2);
        var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
        var centre = t.Position + right * lateral + Double3.Up * (y + height / 2);
        mesh.Box(V(centre, eye), ToF(right), Vector3.UnitY, ToF(t.Tangent * -1), new Vector3((float)halfWidth, (float)height / 2, (float)length / 2), color);
    }

    void Features(MeshBuilder mesh, RailLine line, Double3 eye, double from, double to)
    {
        foreach (var f in Route!.Features)
        {
            if (f.End < from || f.Start > to)
                continue;
            double a = Math.Max(f.Start, from), b = Math.Min(f.End, to);
            switch (f.Kind)
            {
                case FeatureKind.Tunnel:
                    // Walls and roof close round the track, a hill sits on top, and the portals are faced in stone.
                    for (double s = a; s < b; s += 10)
                    {
                        double len = Math.Min(10, b - s);
                        Along(mesh, line, eye, s, len, -3.2, -0.2, 0.6, 7.2, Palette.Charcoal);
                        Along(mesh, line, eye, s, len, 3.2, -0.2, 0.6, 7.2, Palette.Charcoal);
                        Along(mesh, line, eye, s, len, 0, 6.6, 3.8, 1.2, Palette.Charcoal);
                        Along(mesh, line, eye, s, len, 0, 7.8, 40, 14, Palette.MuddyOlive);
                    }
                    foreach (double portal in new[] { f.Start, f.End - 1.5 })
                        if (portal >= from && portal <= to)
                        {
                            Along(mesh, line, eye, portal, 1.5, -5.5, 0, 2.2, 8, Palette.IronGrey);
                            Along(mesh, line, eye, portal, 1.5, 5.5, 0, 2.2, 8, Palette.IronGrey);
                            Along(mesh, line, eye, portal, 1.5, 0, 7.4, 7.7, 1.6, Palette.IronGrey);
                        }
                    break;
                case FeatureKind.Bridge:
                    // Weak bridges are timber trestles; sound ones are iron girders on stone piers.
                    var deck = f.MaxCars > 0 ? Palette.DeepBrown : Palette.IronGrey;
                    for (double s = a; s < b; s += 10)
                    {
                        double len = Math.Min(10, b - s);
                        Along(mesh, line, eye, s, len, 0, -0.6, 2.2, 0.6, deck);
                        Along(mesh, line, eye, s, len, -2.3, -0.6, 0.12, 1.6, deck);
                        Along(mesh, line, eye, s, len, 2.3, -0.6, 0.12, 1.6, deck);
                    }
                    for (double s = Math.Ceiling(a / 25) * 25; s < b; s += 25)
                        Along(mesh, line, eye, s, 3, 0, -ValleyDepth, f.MaxCars > 0 ? 1.6 : 2.0, ValleyDepth - 0.6, f.MaxCars > 0 ? Palette.DeepBrown : Palette.Charcoal);
                    break;
                case FeatureKind.Sleepers when Enemies is null:
                    // Shaped like ties, lying across the rail: invisible until the lamp finds them (App. A.2).
                    for (double s = a; s < b; s += 2.6)
                        Along(mesh, line, eye, s, 0.9, 0, 0.12, 1.5, 0.28, Palette.Corrupted);
                    break;
                case FeatureKind.Grease:
                    mesh.Emissive = 0.15f;
                    foreach (double side in new[] { -0.72, 0.72 })
                        Along(mesh, line, eye, a, b - a, side, 0.19, 0.05, 0.02, Palette.GreaseSheen);
                    mesh.Emissive = 0;
                    break;
                case FeatureKind.Junction:
                    // Switch stand with a lamp, and a stub of diverging rail: what the Switchman throws.
                    double side2 = f.Side * 2.6;
                    Along(mesh, line, eye, f.Start, 0.3, side2, 0, 0.15, 1.4, Palette.IronGrey);
                    mesh.Emissive = 1;
                    Along(mesh, line, eye, f.Start, 0.25, side2, 1.4, 0.12, 0.25, Palette.LampAmber);
                    mesh.Emissive = 0;
                    for (int i = 0; i < 6; i++)
                        Along(mesh, line, eye, f.Start + i * 5, 5, f.Side * (0.9 + i * 0.35), 0.05, 0.05, 0.15, Palette.IronGrey);
                    break;
                case FeatureKind.Facility:
                    Facility(mesh, line, eye, f);
                    break;
            }
        }
    }

    /// <summary>Placeholder silhouettes until facility modules exist: oversized, dark, one working lamp (GDD §30).</summary>
    static void Facility(MeshBuilder mesh, RailLine line, Double3 eye, RouteFeature f)
    {
        double mid = (f.Start + f.End) / 2, side = f.Side;
        switch (f.Facility)
        {
            case FacilityKind.CoalingTower:
                Along(mesh, line, eye, mid - 6, 12, side * 7, 0, 5, 22, Palette.Charcoal);
                Along(mesh, line, eye, mid - 2, 4, side * 2.6, 9, 1.6, 1.2, Palette.IronGrey);
                break;
            case FacilityKind.GrainElevator:
                for (int i = 0; i < 3; i++)
                    Along(mesh, line, eye, mid - 20 + i * 12, 10, side * 12, 0, 5, 26, Palette.BlueGrey);
                break;
            case FacilityKind.Foundry:
                Along(mesh, line, eye, mid - 40, 80, side * 22, 0, 14, 16, Palette.RustRed);
                Along(mesh, line, eye, mid + 10, 6, side * 26, 16, 2, 18, Palette.SootBlack);
                break;
            default:
                Along(mesh, line, eye, mid - 30, 60, side * 20, 0, 12, 10, Palette.DeepBrown);
                break;
        }
        mesh.Emissive = 1;
        Along(mesh, line, eye, mid, 0.4, side * 4, 5, 0.2, 0.3, Palette.LampAmber);
        mesh.Emissive = 0;
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
        // What you see is what you collide with: every solid is drawn, coloured by what it is. Long interior
        // surfaces go down in 2 m slices so the per-vertex lamp light has vertices to land on.
        foreach (var solid in shape.Solids)
        {
            var colour = PartColour(solid.Part, frame.Index);
            var b = solid.Box;
            double length = b.Max.Z - b.Min.Z;
            if (shape.Interior is null || length <= 2.5 || solid.Part is not (PartKind.Wall or PartKind.Chassis or PartKind.Body or PartKind.Cargo))
            {
                Draw(b, colour);
                continue;
            }
            int slices = (int)Math.Ceiling(length / 2);
            for (int k = 0; k < slices; k++)
            {
                double z0 = b.Min.Z + length * k / slices, z1 = b.Min.Z + length * (k + 1) / slices;
                Draw(new Box(b.Min with { Z = z0 }, b.Max with { Z = z1 }), colour);
            }
        }

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
        // Doors: shut in the doorway, or slid aside along the end wall when open.
        var vehicle = Vehicles is { } vs && frame.Index < vs.Count ? vs[frame.Index] : null;
        foreach (var door in shape.DoorList)
        {
            bool open = vehicle?.DoorOpen(door.Index) ?? false;
            var box = door.Box;
            if (open)
            {
                double slide = box.Max.X - box.Min.X;
                double inward = box.Min.Z < 0 ? 0.12 : -0.12;
                box = new Box(box.Min + new Double3(slide, 0, inward), box.Max + new Double3(slide, 0, inward));
            }
            Draw(box, Palette.DeepBrown);
            Draw(Box.FromCentre(new Double3(open ? box.Min.X + 0.1 : box.Max.X - 0.12, box.Min.Y + 1.0, box.Centre.Z), new Double3(0.04, 0.04, 0.08)), Palette.TarnishedBrass);
        }
        if (shape.Interior is { } room)
        {
            // A lamp in every car: the warm interior against the hostile exterior (GDD §26).
            mesh.Emissive = 1;
            Draw(Box.FromCentre(new Double3(0, room.Max.Y - 0.08, 0), new Double3(0.12, 0.06, 0.12)), Palette.LampAmber);
            mesh.Emissive = 0;
        }
        if (shape.Gun is { } gun)
        {
            // Barrel along the gun's facing: its arc is readable from its silhouette (GDD §26).
            Draw(Box.FromCentre(gun.Position + gun.Facing * 0.9, new Double3(0.08, 0.08, 0.9)), Palette.SootBlack);
            Draw(Box.FromCentre(gun.Position, new Double3(0.3, 0.25, 0.35)), Palette.IronGrey);
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
        PartKind.Wall => car % 3 == 0 ? Palette.RustRed : Palette.DeepBrown,
        PartKind.Cargo => Palette.MuddyOlive,
        PartKind.Locker => Palette.IronGrey,
        _ => Palette.IronGrey,
    };

    static Vector3 ToF(Double3 d) => new((float)d.X, (float)d.Y, (float)d.Z);
}
