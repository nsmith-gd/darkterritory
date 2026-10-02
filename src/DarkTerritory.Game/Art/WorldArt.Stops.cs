using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Stops;

namespace DarkTerritory.Game.Art;

/// <summary>
/// The route's generated stops as the art pass draws them (level-design P1-P14): each yard's sheds and hero building
/// between its tracks, each village's houses, barns and well on its roads, a halt's platform. They come from the stop's
/// layout (<see cref="RouteFeature.Stop"/>), so every machine sees the same place; the tracks are the route's own
/// spurs, drawn with the rest of the branches. The ground under a stop is levelled (the sim's zone is level), and the
/// forest kept off its buildings, roads and tracks.
/// </summary>
public sealed partial class WorldArt
{
    /// <summary>How far past a stop's zone its levelled ground eases back into the hills.</summary>
    const double FlatEase = 60;
    /// <summary>The clearance mask's cell (m): trees stay out of any cell a building, road or track comes near.</summary>
    const double ClearCell = 6;

    /// <summary>How levelled the ground is at <paramref name="s"/>: 1 across a stop's zone, easing out past its ends.</summary>
    public static float Flat(Route? route, double s)
    {
        if (route is null)
            return 0;
        float best = 0;
        foreach (var f in route.Features)
        {
            if (f.Stop is null || s < f.Start - FlatEase || s > f.End + FlatEase)
                continue;
            double out_ = s < f.Start ? f.Start - s : s > f.End ? s - f.End : 0;
            float g = (float)Math.Clamp(1 - out_ / FlatEase, 0, 1);
            best = MathF.Max(best, g * g * (3 - 2 * g));
        }
        return best;
    }

    Route? _clearRoute;
    HashSet<(long, long)> _clear = [];

    /// <summary>A point (along the line, out from it) the lineside should leave alone: on a stop's ground.</summary>
    bool OnStop(Route? route, double along, double offset)
    {
        if (route is null)
            return false;
        if (!ReferenceEquals(route, _clearRoute))
            (_clear, _clearRoute) = (Clearance(route), route);
        return _clear.Contains(((long)Math.Floor(along / ClearCell), (long)Math.Floor(offset / ClearCell)));
    }

    /// <summary>
    /// The cells of every stop's ground: round each building, along each road and track, and the yard's whole throat
    /// and the facility's own ground beyond its outermost track (where its modules and works stand, Site).
    /// </summary>
    static HashSet<(long, long)> Clearance(Route route)
    {
        var cells = new HashSet<(long, long)>();
        void Disc(double s, double d, double r)
        {
            for (long i = (long)Math.Floor((s - r) / ClearCell); i <= (long)Math.Floor((s + r) / ClearCell); i++)
                for (long j = (long)Math.Floor((d - r) / ClearCell); j <= (long)Math.Floor((d + r) / ClearCell); j++)
                {
                    double cs = (i + 0.5) * ClearCell, cd = (j + 0.5) * ClearCell;
                    if (double.Hypot(cs - s, cd - d) <= r + ClearCell * 0.71)
                        cells.Add((i, j));
                }
        }
        void Line(double start, IReadOnlyList<Pt> points, double r)
        {
            for (int i = 0; i + 1 < points.Count; i++)
            {
                var a = points[i];
                var b = points[i + 1];
                int n = Math.Max(1, (int)Math.Ceiling(Pt.Distance(a, b) / 3));
                for (int k = 0; k <= n; k++)
                {
                    var p = a + (b - a) * ((double)k / n);
                    Disc(start + p.S, p.D, r);
                }
            }
        }
        foreach (var f in route.Features)
        {
            if (f.Stop is not { } stop)
                continue;
            foreach (var b in stop.Buildings)
                Disc(f.Start + b.S, b.D, double.Hypot(b.Length, b.Width) / 2 + 4);
            foreach (var road in stop.Roads)
                Line(f.Start, road.Points, 5);
            foreach (var track in stop.Tracks)
                Line(f.Start, track.Path, 8);
            if (stop.Tracks.Count > 0)
            {
                double s0 = stop.Tracks.Min(t => t.Toe) - 10, s1 = stop.Tracks.Max(t => t.FaceEnd.S) + 15;
                var yard = stop.Tracks.Where(t => !t.Across).ToList();
                double far = (yard.Count > 0 ? yard.Max(t => t.Offset) : 0) + 45;
                for (double s = s0; s <= s1; s += ClearCell)
                    for (double d = 0; d <= far; d += ClearCell)
                        Disc(f.Start + s, stop.YardSide * d, 0);
            }
            if (stop.Halt is { } h)
                Line(f.Start, [h - new Pt(stop.HaltLength / 2, 0), h + new Pt(stop.HaltLength / 2, 0)], 6);
        }
        return cells;
    }

    /// <summary>
    /// Every stop building whose centre is in [<paramref name="from"/>, <paramref name="to"/>), each road stretch that
    /// starts there, and a halt's platform: so neighbouring cells (<see cref="Cells"/>) share nothing and miss nothing.
    /// </summary>
    void Stops(MeshBuilder mesh, RailLine line, Route? route, Double3 eye, double from, double to, float valleyDepth)
    {
        if (route is null)
            return;
        var k = new Kit(_look, mesh) { SurfaceOrigin = new Vector3(W(eye.X), W(eye.Y), W(eye.Z)) };
        foreach (var f in route.Features)
        {
            if (f.Stop is not { } stop || f.End + 250 < from || f.Start - 250 > to)
                continue;
            Roads(mesh, line, route, f, stop, eye, from, to, valleyDepth);
            for (int i = 0; i < stop.Buildings.Count; i++)
            {
                var b = stop.Buildings[i];
                double along = f.Start + b.S;
                if (along < from || along >= to)
                    continue;
                k.Reseed(b.Variant * 7.1f + (float)(b.S * 0.13));
                Building(k, line, route, f, stop, i, eye, valleyDepth);
            }
            if (stop.Halt is { } halt && f.Start + halt.S >= from && f.Start + halt.S < to)
                Halt(k, line, f, halt, stop.HaltLength, eye);
        }
    }

    /// <summary>
    /// One building, standing on the levelled ground at its footprint: sheds and the hero as works buildings with their
    /// doors to the track they serve (P5, P7); houses one per part of the footprint, each roofed along its longer side
    /// (P9); barns and outbuildings as small sheds; a well.
    /// </summary>
    void Building(Kit k, RailLine line, Route route, RouteFeature f, StopLayout stop, int index, Double3 eye, float valleyDepth)
    {
        var b = stop.Buildings[index];
        double along = f.Start + b.S;
        var t = line.Sample(Math.Clamp(along, 0, line.Length));
        var at = Sim.Run.Run.StopWorld(line, f, b.Centre, Ground(route, along, (float)b.D, valleyDepth) - 0.15);
        // The layout's yaw turns its axis from along the line towards +D; the kit's frame turns the other way about +Y.
        var frame = Basis(t.Tangent, at, eye, (float)-b.Yaw);
        float length = (float)b.Length, width = (float)b.Width;
        switch (b.Kind)
        {
            case BuildingKind.Shed or BuildingKind.Hero:
                {
                    // Its doors face the track it serves: the first of its tracks, or the main line.
                    double towards = b.Tracks.Count > 0 ? stop.Tracks[b.Tracks[0]].FaceStart.D : 0;
                    int door = towards >= b.D ? 1 : -1;
                    bool hero = b.Kind == BuildingKind.Hero;
                    string wall = hero ? "brick_soot" : (b.Variant % 3) switch { 1 => "rust_heavy", _ => "wood_grey" };
                    float height = hero ? 10 + b.Variant : 6.5f + b.Variant * 0.8f;
                    // Where a yard gantry works this shed's bays (P18), its runway is an open bay cut through it: the
                    // castings lie in the open under the hook, the gantry's legs at the ends, the shed either side.
                    var gap = CraneBay(stop, index);
                    k.With(frame, () =>
                    {
                        double a = -b.Length / 2;
                        foreach (var (g0, g1) in gap is { } g ? new[] { (a, g.From - b.S), (g.To - b.S, b.Length / 2) } : [(a, b.Length / 2)])
                        {
                            double lo = Math.Max(g0, a), hi = Math.Min(g1, b.Length / 2);
                            if (hi - lo < 3)
                                continue;
                            k.With(Kit.At(0, 0, (float)-(lo + hi) / 2), () => StructureKit.Shed(k, width, (float)(hi - lo), height, wall, door));
                        }
                        if (gap is { } open)
                        {
                            // The cut: its floor, and the shell of the walls, roofless and below the gantry's rails.
                            double lo = Math.Max(open.From - b.S, a), hi = Math.Min(open.To - b.S, b.Length / 2);
                            float z0 = (float)-hi, z1 = (float)-lo, back = -door * width / 2, shell = 4.5f;
                            k.Use("concrete_stain", Palette.BlueGrey, 0.9f, 0.1f, tile: 2.5f);
                            k.Box(new Vector3(-width / 2, -0.4f, z0), new Vector3(width / 2, 0.15f, z1), Kit.Faces.All & ~Kit.Faces.NegY);
                            k.Use(wall, wall == "brick_soot" ? Palette.RustRed : Palette.DeepBrown, 0.9f, 0.1f, tile: 1.5f);
                            k.Box(new Vector3(MathF.Min(back, back + door * 0.3f), 0.15f, z0), new Vector3(MathF.Max(back, back + door * 0.3f), shell, z1));
                            // An end wall where the cut reaches the shed's end (elsewhere the roofed part's gable closes it).
                            foreach (var (edge, reaches) in new[] { (z0, hi >= b.Length / 2 - 0.01), (z1, lo <= a + 0.01) })
                                if (reaches)
                                    k.Box(new Vector3(-width / 2, 0.15f, edge - 0.15f), new Vector3(width / 2, shell * 0.7f, edge + 0.15f));
                        }
                    });
                    break;
                }
            case BuildingKind.Barn:
                k.With(frame, () => StructureKit.Shed(k, width, length, 6.5f, "wood_grey", b.Variant % 2 == 0 ? 1 : -1));
                break;
            case BuildingKind.Outbuilding:
                // (Its walls stand the standard doorway and a header over its sill: 2.8 m put the door through the eaves.)
                k.With(frame, () => StructureKit.Shed(k, width, length, 0.8f + k.DoorHeight() + 0.4f, b.Variant == 1 ? "rust_heavy" : "wood_grey", b.Variant % 2 == 0 ? 1 : -1));
                break;
            case BuildingKind.Well:
                k.With(frame, () => Well(k));
                break;
            case BuildingKind.Powerhouse:
                // The yard's powerhouse (level-design D.2): a brick engine house, its door to the main line, a tall
                // stack at the back. Whether it's running, the scene says (its lamp).
                k.With(frame, () =>
                {
                    int door = b.D > 0 ? -1 : 1;
                    StructureKit.Shed(k, width, length, 5.5f, "brick_soot", door);
                    k.Use("brick_soot", Palette.RustRed, 0.9f, 0.1f, tile: 1.2f);
                    k.Cylinder(new Vector3(-door * (width / 2 - 1.2f), 4, length / 4), new Vector3(-door * (width / 2 - 1.2f), 14, length / 4), 0.7f, 8, radiusB: 0.5f);
                });
                break;
            case BuildingKind.PrisonCar or BuildingKind.SignalBox or BuildingKind.LampRoom or BuildingKind.WaterTower or BuildingKind.Lockup:
                if (stop.Holdouts.FirstOrDefault(h => h.Building == index) is { } holdout)
                {
                    // Which way its door faces in the kit's frame (the building's axis is −Z, across it +X).
                    var (dx, dy) = Local(b, holdout.Door);
                    var facing = Math.Abs(dx) / b.Length > Math.Abs(dy) / b.Width ? new Vector3(0, 0, (float)-Math.Sign(dx)) : new Vector3((float)Math.Sign(dy), 0, 0);
                    k.With(frame, () => Holdout(k, b, facing));
                    var lampAt = Sim.Run.Run.StopWorld(line, f, holdout.Lamp, Ground(route, f.Start + holdout.Lamp.S, (float)holdout.Lamp.D, valleyDepth) + LampHeight(b.Kind));
                    k.With(Basis(t.Tangent, lampAt, eye, 0), () => LampFixture(k, (float)LampHeight(b.Kind)));
                }
                break;
            default:
                {
                    // The layout's footprint parts are (x along its axis, y across); the kit's frame has its axis on −Z.
                    var parts = b.Parts.Count > 0 ? b.Parts : [new FootprintPart(0, 0, b.Length, b.Width)];
                    for (int i = 0; i < parts.Count; i++)
                    {
                        var p = parts[i];
                        var rng = new Random(unchecked(b.Variant * 7919 + i * 104729 + (int)(b.S * 31) + (int)(b.D * 17)));
                        bool alongAxis = p.Length >= p.Width;
                        var local = (alongAxis ? Matrix4x4.Identity : Matrix4x4.CreateRotationY(MathF.PI / 2))
                            * Matrix4x4.CreateTranslation((float)p.Y, 0, (float)-p.X);
                        float w = (float)(alongAxis ? p.Width : p.Length), d = (float)(alongAxis ? p.Length : p.Width);
                        k.With(local * frame, () =>
                        {
                            if (!TownKit.HouseProp(k, _look, w, d, b.Variant + i))
                                TownKit.House(k, rng, w, d, b.Variant + i);
                        });
                    }
                    break;
                }
        }
    }

    /// <summary>
    /// The stretch along the line (zone S) of a yard gantry's runway, and a little either side for its legs, if one works
    /// bays in building <paramref name="index"/>; null if none does.
    /// </summary>
    static (double From, double To)? CraneBay(StopLayout stop, int index)
    {
        foreach (var c in stop.Containers)
            if (c.Kind == ContainerKind.CraneBay && c.Building == index && c.Track >= 0 && stop.Tracks[c.Track].Crane is { } rw)
                return (rw.From - 1.5, rw.To + 1.5);
        return null;
    }

    /// <summary>A village well: a round stone kerb, two posts and the winding bar across.</summary>
    static void Well(Kit k)
    {
        k.Use("stone_block", Palette.Charcoal, 0.8f, 0.1f, tile: 1.2f);
        k.Cylinder(new Vector3(0, -0.2f, 0), new Vector3(0, 0.8f, 0), 0.9f, 10);
        k.Shade(0.05f);
        k.Cylinder(new Vector3(0, 0.3f, 0), new Vector3(0, 0.81f, 0), 0.7f, 10);
        k.Use("wood_grey", Palette.DeepBrown, 0.9f, 0, tile: 1);
        foreach (float x in new[] { -0.85f, 0.85f })
            k.Rod(new Vector3(x, 0.6f, 0), new Vector3(x, 2.0f, 0), 0.07f);
        k.Rod(new Vector3(-0.95f, 1.8f, 0), new Vector3(0.95f, 1.8f, 0), 0.06f);
    }

    /// <summary>A halt's platform (P13): a low cobbled strip on its kerb beside the main line, a lamp post at one end.</summary>
    void Halt(Kit k, RailLine line, RouteFeature f, Pt halt, double length, Double3 eye)
    {
        var t = line.Sample(f.Start + halt.S);
        var frame = Basis(t.Tangent, t.Position, eye, 0);
        float side = halt.D < 0 ? -1 : 1, half = (float)length / 2, x0 = side * ((float)Math.Abs(halt.D) - 1.0f), x1 = side * ((float)Math.Abs(halt.D) + 2.2f);
        var (a, b) = (MathF.Min(x0, x1), MathF.Max(x0, x1));
        k.With(frame, () =>
        {
            k.Use("cobbles", Palette.Ballast, 0.7f, 0.05f, tile: 2);
            k.Box(new Vector3(a, -0.3f, -half), new Vector3(b, 0.25f, half), Kit.Faces.All & ~Kit.Faces.NegY);
            k.Use("stone_block", Palette.Charcoal, 0.7f, 0.1f, tile: 1.2f);
            float kerb = side > 0 ? a : b - 0.35f;
            k.Box(new Vector3(kerb, -0.3f, -half), new Vector3(kerb + 0.35f, 0.3f, half), Kit.Faces.All & ~Kit.Faces.NegY);
        });
        if (_props.Get("lamp_post") is { } post)
            k.Append(post, Basis(t.Tangent, t.Position + Double3.Cross(t.Tangent, Double3.Up).Normalized * (side * ((float)Math.Abs(halt.D) + 1.6)) - t.Tangent * (half - 2), eye, 0));
    }

    /// <summary>
    /// A stop's roads as strips of ground laid on the levelled earth (cobbles for a street, mud for the rest), the
    /// stretch of each that starts in this cell; where one crosses the main line, timber boards between the rails.
    /// </summary>
    void Roads(MeshBuilder mesh, RailLine line, Route route, RouteFeature f, StopLayout stop, Double3 eye, double from, double to, float valleyDepth)
    {
        var origin = new Vector3(W(eye.X), W(eye.Y), W(eye.Z));
        int mud = _look.Layer("ground_mud"), cobbles = _look.Layer("cobbles"), grass = _look.Layer("ground_grass");
        float tile = mud >= 0 && _look.Textures[mud].TileMetres is { } tm ? tm : 2;
        Vector3 At(Pt p, double lift) => Sim.Run.Run.StopWorld(line, f, p, Ground(route, f.Start + p.S, (float)p.D, valleyDepth) + lift).RelativeTo(eye);
        foreach (var road in stop.Roads)
        {
            float half = RoadHalfWidth(road.Kind);
            bool street = road.Kind == RoadKind.Street;
            int a = street && cobbles >= 0 ? cobbles : mud, b = street ? mud : grass;
            var pts = Dense(road.Points, 4);
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                var p = pts[i];
                var q = pts[i + 1];
                double along = f.Start + p.S;
                if (along < from || along >= to)
                    continue;
                // Leave the formation to the track (the crossing's boards cover it), and keep off the yard's rails.
                if (Math.Abs(p.D) < 3.2 && Math.Abs(q.D) < 3.2)
                    continue;
                var dir = (q - p).Unit;
                var n = dir.Normal * half;
                // Neighbouring pieces meet on their shared ends: each end's across is the average of the pieces' there.
                var n0 = i > 0 ? ((p - pts[i - 1]).Unit + dir).Unit.Normal * half : n;
                var n1 = i + 2 < pts.Count ? ((pts[i + 2] - q).Unit + dir).Unit.Normal * half : n;
                var tint = a >= 0 ? Vector3.One : Palette.MuddyOlive * 0.8f;
                // A shade lighter than the ground round it: worn, so the lamp picks the way out.
                var c0 = new Corner(tint * (street ? 1.25f : 1.1f), street ? 0.1f : 0.25f);
                var c1 = new Corner(tint * 0.8f, street ? 0.5f : 0.8f);
                Quad(mesh, At(p - n0, 0.04), At(p + n0, 0.04), At(q + n1, 0.04), At(q - n1, 0.04), c0, c0, c0, c0, origin, a, b, a == cobbles && cobbles >= 0 ? 2 : tile);
                // A soft verge each side, blending into the ground.
                foreach (int s in new[] { -1, 1 })
                {
                    var o0 = n0 * s;
                    var o1 = n1 * s;
                    var w0 = n0.Unit * (s * (half + 1.2));
                    var w1 = n1.Unit * (s * (half + 1.2));
                    Quad(mesh, At(p + o0, 0.04), At(p + w0, 0.02), At(q + w1, 0.02), At(q + o1, 0.04), c0, c1, c1, c0, origin, a, b, tile);
                }
            }
        }
        if (stop.Crossing is { } x && f.Start + x.S >= from && f.Start + x.S < to)
        {
            var t = line.Sample(f.Start + x.S);
            var k = new Kit(_look, mesh) { SurfaceOrigin = origin };
            k.Use("wood_sleeper", Palette.DeepBrown, 0.9f, 0, tile: 1.3f);
            k.With(Basis(t.Tangent, t.Position, eye, 0), () =>
            {
                for (float z = -CrossingHalf; z < CrossingHalf; z += 0.4f)
                    k.Box(new Vector3(-CrossingHalf, 0.02f, z), new Vector3(CrossingHalf, 0.13f, z + 0.36f), Kit.Faces.PosY | Kit.Faces.PosZ | Kit.Faces.NegZ);
            });
        }
    }

    /// <summary>Half a road's width by its kind, its verges aside (P10's through road down to a stub).</summary>
    static float RoadHalfWidth(RoadKind kind) => kind switch { RoadKind.Through => 3f, RoadKind.Street => 2.8f, RoadKind.Lane => 1.9f, _ => 1.6f };

    /// <summary>A stop's level crossing's boards: this far either way along the line and across it.</summary>
    const float CrossingHalf = 2.6f;

    /// <summary>A polyline resampled so no piece is longer than <paramref name="step"/> (the ground under it isn't flat).</summary>
    static List<Pt> Dense(IReadOnlyList<Pt> points, double step)
    {
        var dense = new List<Pt>();
        for (int i = 0; i + 1 < points.Count; i++)
        {
            int n = Math.Max(1, (int)Math.Ceiling(Pt.Distance(points[i], points[i + 1]) / step));
            for (int k = 0; k < n; k++)
                dense.Add(points[i] + (points[i + 1] - points[i]) * ((double)k / n));
        }
        if (points.Count > 0)
            dense.Add(points[^1]);
        return dense;
    }
}
