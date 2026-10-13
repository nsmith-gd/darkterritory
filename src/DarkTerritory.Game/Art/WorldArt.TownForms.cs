using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Towns;

namespace DarkTerritory.Game.Art;

/// <summary>
/// The town types drawn (queue #424, ARCHITECTURE §8 note 692): a town's own ground where its form has shaped it (the
/// terraces and the quarry's benches with their retaining walls, the basins' beds, the plank decks over the water, the
/// rails along the deep water's edge, the ramps), its water's surface, its ways, and its landmarks, the kit's pieces the
/// game already has (the lighthouse, the chapel, a wall's tower, the pit's headframe, a fish shed, a glasshouse). The land's
/// own mesh is left out where the town draws its ground (<see cref="TownHole"/>), so the two never fight.
/// </summary>
public sealed partial class WorldArt
{
    /// <summary>The departure town (set with <see cref="TownBounds"/>), so the land's cells leave its shaped ground to it.</summary>
    public Town? Departure { get; set; }

    /// <summary>How far round its shaped ground a town draws its own (m), so the land's coarser mesh never meets a terrace's foot.</summary>
    const double FormApron = 6;

    /// <summary>
    /// Whether the land's own mesh leaves a point (world) to a town (<see cref="Departure"/> or <see cref="Arrival"/>): it's
    /// where the town draws its own ground.
    /// </summary>
    bool TownHole(double x, double z)
    {
        foreach (var town in new[] { Departure, Arrival })
        {
            if (town is null || town.Ground.Flat)
                continue;
            var o = town.Anchor();
            var along = new Double3(o.Tangent.X, 0, o.Tangent.Z).Normalized;
            var right = Double3.Cross(along, Double3.Up).Normalized;
            double dx = x - o.Position.X, dz = z - o.Position.Z;
            if (town.Ground.Drawn(dx * along.X + dz * along.Z, dx * right.X + dz * right.Z, FormApron))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Where a town type's shaped ground is, in the main line's frame (along it, across it; a terminus's town turned round,
    /// note 600): the land's mesh takes its finer columns there as it does by a lake's shore, so it's left out close round
    /// the town's own ground and never over a reservoir or a quarry's bench.
    /// </summary>
    IEnumerable<LakeBand> TownBands()
    {
        foreach (var town in new[] { Departure, Arrival })
        {
            if (town is null || town.Ground.Flat)
                continue;
            var g = town.Ground;
            double a = g.S0 - FormApron, b = g.S0 + g.Ns * Sim.Towns.TownGround.Cell + FormApron;
            float l0 = (float)(g.D0 - FormApron), l1 = (float)(g.D0 + g.Nd * Sim.Towns.TownGround.Cell + FormApron);
            double o = town.Origin ?? 0;
            yield return new LakeBand(o + a, o + b, l0, l1);
        }
    }

    /// <summary>
    /// A town type's ground, water, ways and landmarks (note 692), from <paramref name="from"/> to <paramref name="to"/>
    /// along its line. The ground is one piece a town (cooked once, drawn by one transform), the water a sheet a frame.
    /// </summary>
    public void Forms(MeshBuilder mesh, RailLine line, Double3 eye, Town town, double from, double to, double time)
    {
        var plan = town.Plan;
        if (plan.Form.Length == 0 || plan.Bounds is not { } b || b.Gate < from - 200 || b.Rear > to + 200)
            return;
        var o = town.Anchor();
        var frame = Basis(o.Tangent, o.Position, eye, 0);
        var ground = Piece($"town-form-{plan.Name}-{plan.Form}-{plan.Gate:0}-{plan.Houses.Count}", () => FormGround(town));
        mesh.Instances.Add(new MeshInstance(ground, frame));
        // The water: a sheet over the basins' rectangles, a little under the deck's planks, rippling as a lake's does.
        int layer = _look?.Layer("water_dark") ?? -1;
        if (layer >= 0)
        {
            var sheet = new WaterSheet(mesh, eye, layer) { Tint = plan.Form == "dykeland" ? new Vector3(0.9f, 0.95f, 0.85f) : new Vector3(0.85f, 1.0f, 1.1f), Open = 0.6f };
            var along = new Double3(o.Tangent.X, 0, o.Tangent.Z).Normalized;
            var right = Double3.Cross(along, Double3.Up).Normalized;
            Vector3 W3(double s, double d, double up) => (o.Position + along * s + right * d + Double3.Up * up).RelativeTo(eye);
            foreach (var (s0, s1, d0, d1, level) in town.Ground.WaterRects())
            {
                if ((W3((s0 + s1) / 2, (d0 + d1) / 2, level).Length() > 520))
                    continue;
                sheet.Quad(W3(s0, d0, level), W3(s1, d0, level), W3(s1, d1, level), W3(s0, d1, level), 0, 0, 0, 0);
            }
        }
        // The landmarks: the kit's pieces where the form put them.
        foreach (var f in plan.Fixtures)
        {
            if (f.House >= 0 || !Landmark(f.Kind) || plan.Works is { } wk && wk.Holds(f.S, f.D, 1))
                continue;
            var at = town.World(f.S, f.D);
            if ((at - eye).Length > 460)
                continue;
            var m = Place(line, eye, at, f.S, f.FaceS, f.FaceD);
            if (f.Kind is "lift" or "glasshouse" or "seedhouse" && WorksKit.Prop(f.Kind == "lift" ? "headframe" : f.Kind) is { } prop && _props.Get(prop.Name) is { } model)
            {
                var (middle, _) = prop.Centred ? WorksKit.Footprint(model) : (Vector3.Zero, Vector3.Zero);
                mesh.Instances.Add(new MeshInstance(model, Matrix4x4.CreateTranslation(-middle) * Matrix4x4.CreateScale(prop.Scale) * Matrix4x4.CreateTranslation(0, -0.3f, 0) * m));
                continue;
            }
            var (piece, scale) = f.Kind switch
            {
                "lighthouse" => (Piece("form-lighthouse", () => NovaKit.Lighthouse(_look, 0)), 1f),
                "beacon" => (Piece("form-lighthouse-1", () => NovaKit.Lighthouse(_look, 1)), 0.35f),
                "chapel" => (Piece("form-chapel", () => NovaKit.Church(_look)), 1f),
                "watchtower" => (Piece("tower-1", () => StructureKit.Tower(_look, 1)), 1.1f),
                "crane" => (Piece("form-crane", () => SettingKit.Headframe(_look)), 0.55f),
                "boatshed" => (Piece("form-boatshed", () => NovaKit.FishShed(_look, 1)), 1.6f),
                _ => (Piece($"works-{(f.Kind == "seedhouse" ? "glasshouse" : f.Kind)}", () => WorksKit.Piece(_look, f.Kind == "seedhouse" ? "glasshouse" : f.Kind)), 1f),
            };
            mesh.Instances.Add(new MeshInstance(piece, Matrix4x4.CreateScale(scale) * m));
            if (f.Kind is "lighthouse" or "beacon")
            {
                // The light: lit, every night, turning (the lighthouse's words), a beam over the town and the water.
                var lamp = Vector3.Transform(new Vector3(0, f.Kind == "beacon" ? 6.5f : 19.5f, 0), m);
                mesh.PointLights.Add(new PointLight(lamp, Palette.LampAmber * 2.2f, f.Kind == "beacon" ? 10 : 24));
                mesh.Billboard(lamp, f.Kind == "beacon" ? 1.2f : 3.2f, 0, new Vector4(Palette.LampAmber, 1), -1, FxBlend.Additive);
                if (f.Kind == "lighthouse")
                {
                    float a = (float)(time * 0.45);
                    Effects.Beam(mesh, lamp, Vector3.Normalize(new Vector3(MathF.Cos(a), -0.06f, MathF.Sin(a))), 3.5f, 180, Palette.LampAmber * 0.18f);
                }
            }
        }
    }

    /// <summary>The landmarks a town type stands (TownForms' kinds) that the square and the works don't draw.</summary>
    static bool Landmark(string kind) => kind is "lighthouse" or "beacon" or "chapel" or "watchtower" or "lift" or "crane" or "boatshed" or "seedhouse" or "glasshouse";

    /// <summary>
    /// A town type's ground as one piece, in its frame from the line's start (−Z along the line, +X across to its right):
    /// each of <see cref="TownGround.Rects"/> a slab on a body down to the lowest ground (the body's sides are the terraces'
    /// and benches' faces, stone or the quarry's rock), the decks' planks on their piles, the rails and the parapets along
    /// <see cref="TownGround.Edges"/>, and the ways that aren't the walled town's streets.
    /// </summary>
    MeshAsset FormGround(Town town)
    {
        var plan = town.Plan;
        var g = town.Ground;
        var k = new Kit(_look, 9100 + plan.Name.Length);
        var rects = g.Rects(FormApron);
        double floor = rects.Count > 0 ? rects.Min(r => r.Level) - 1.5 : -2;
        bool quarry = plan.Form == "quarry";
        string land = plan.Form switch { "quarry" => "granite_lichen", "tidewater" => "cobbles", "harbour" => "ground_heath", "dykeland" => "marsh", _ => "ground_mud" };
        string face = quarry ? "rock_cliff" : "stone_block";
        static Vector3 V(double s, double y, double d) => new((float)d, (float)y, (float)-s);
        foreach (var (s0, s1, d0, d1, level, kind) in rects)
        {
            var lo = V(s1, floor, d0);
            var hi = V(s0, level, d1);
            switch (kind)
            {
                case 3:
                    // A deck: grey planks on a beam, its piles down to the bed at its corners.
                    k.Use("wood_grey", Palette.DeepBrown, 0.8f, 0.05f, tile: 2);
                    k.Box(V(s1, level - 0.25, d0), V(s0, level, d1), true, true);
                    k.Use("wood_sleeper", Palette.DeepBrown, 0.9f, 0.05f, tile: 2);
                    foreach (var (ps, pd) in new[] { (s0 + 0.3, d0 + 0.3), (s1 - 0.3, d1 - 0.3) })
                        k.Box(V(ps + 0.15, g.Bed(ps, pd) - 0.5, pd - 0.15), V(ps - 0.15, level - 0.25, pd + 0.15), false, false);
                    break;
                case 2:
                    // A basin's bed, mud under the water.
                    k.Use("ground_mud", Palette.DeepBrown, 0.95f, 0.2f, tile: 3);
                    k.Box(lo, hi, Kit.Faces.PosY);
                    break;
                default:
                    // Land: its top, a hand thick, on a body whose sides are the face of the step down from it.
                    k.Use(level == 0 ? "ground_mud" : land, Palette.DeepBrown, 0.9f, 0.04f, tile: 3);
                    k.Box(V(s1, level - 0.3, d0), V(s0, level, d1), Kit.Faces.PosY | Kit.Faces.Sides);
                    if (level - 0.3 > floor)
                    {
                        k.Use(face, Palette.DeepBrown, 0.8f, 0.05f, tile: 3);
                        k.Box(lo, V(s0, level - 0.3, d1), Kit.Faces.Sides);
                    }
                    break;
            }
        }
        // The rails along the deep water and the parapets along the terraces' edges (the Sim's walls, TownGround.Edges).
        foreach (var (s0, d0, s1, d1, bottom, top) in g.Edges())
        {
            double upper = top - Sim.Towns.TownGround.Parapet;
            bool rail = g.Water((s0 + s1) / 2 + (s1 == s0 ? 0.5 : 0), (d0 + d1) / 2 + (d1 == d0 ? 0.5 : 0)) is not null
                || g.Water((s0 + s1) / 2 - (s1 == s0 ? 0.5 : 0), (d0 + d1) / 2 - (d1 == d0 ? 0.5 : 0)) is not null;
            double half = 0.12;
            var (a, z) = s0 == s1 ? (V(s0 + half, upper, d0), V(s0 - half, upper + (rail ? 1.0 : 0.9), d1)) : (V(s1, upper, d0 - half), V(s0, upper + (rail ? 1.0 : 0.9), d0 + half));
            if (rail)
            {
                // A timber rail on posts: the top rail, and a post every two metres.
                k.Use("wood_grey", Palette.DeepBrown, 0.85f, 0.05f, tile: 2);
                k.Box(new Vector3(Math.Min(a.X, z.X), z.Y - 0.12f, Math.Min(a.Z, z.Z)), new Vector3(Math.Max(a.X, z.X), z.Y, Math.Max(a.Z, z.Z)));
                double len = Math.Max(Math.Abs(s1 - s0), Math.Abs(d1 - d0));
                for (double x = 0; x <= len; x += 2)
                {
                    var (ps, pd) = s0 == s1 ? (s0, d0 + x) : (s0 + x, d0);
                    k.BoxAt(V(ps, upper + 0.5, pd), new Vector3(0.07f, 0.5f, 0.07f));
                }
            }
            else
            {
                k.Use(face, Palette.DeepBrown, 0.8f, 0.05f, tile: 3);
                k.Box(new Vector3(Math.Min(a.X, z.X) - 0.1f, a.Y, Math.Min(a.Z, z.Z) - 0.1f), new Vector3(Math.Max(a.X, z.X) + 0.1f, z.Y, Math.Max(a.Z, z.Z) + 0.1f));
            }
        }
        // The ways on the ground (the walled town's streets are Streets'): beaten strips over the ground, a ramp's tilted
        // with it; a deck's are its planks already.
        k.Use(plan.Form == "tidewater" || plan.Form == "harbour" ? "cobbles" : "ballast", Palette.DeepBrown, 0.9f, 0.04f, tile: 2.5f);
        foreach (var w in plan.Ways)
        {
            if (w.Deck)
                continue;
            for (int i = 1; i < w.Points.Count; i++)
            {
                var (a, z) = (w.Points[i - 1], w.Points[i]);
                double es = z.S - a.S, ed = z.D - a.D, len = Math.Sqrt(es * es + ed * ed);
                if (len < 0.1)
                    continue;
                double ns = -ed / len * w.Width / 2, nd = es / len * w.Width / 2;
                double la = (w.Levels?[i - 1] ?? g.Level(a.S, a.D)) + 0.05, lz = (w.Levels?[i] ?? g.Level(z.S, z.D)) + 0.05;
                k.Quad(V(a.S - ns, la, a.D - nd), V(z.S - ns, lz, z.D - nd), V(z.S + ns, lz, z.D + nd), V(a.S + ns, la, a.D + nd), twoSided: true);
            }
        }
        return k.Build($"town-form-ground-{plan.Name}");
    }
}
