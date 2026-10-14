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

    /// <summary>A deck's piles (note 692): its corners and along its edges every <see cref="PileEvery"/> m.</summary>
    static IEnumerable<(double S, double D)> Piles(double s0, double s1, double d0, double d1)
    {
        int ns = Math.Max(1, (int)Math.Round((s1 - s0) / PileEvery)), nd = Math.Max(1, (int)Math.Round((d1 - d0) / PileEvery));
        for (int i = 0; i <= ns; i++)
            foreach (double d in nd > 0 ? new[] { d0, d1 } : [d0])
                yield return (s0 + (s1 - s0) * i / ns, d);
        for (int j = 1; j < nd; j++)
            foreach (double s in new[] { s0, s1 })
                yield return (s, d0 + (d1 - d0) * j / nd);
    }

    const double PileEvery = 3.5;

    /// <summary>The grid a fen's reeds and dead trees are scattered on (m), each a hash of its cell (the same every frame).</summary>
    const double MarshCell = 5;

    /// <summary>A drop taller than this (m) is drawn as rock, not a retaining wall's masonry (note 692): a crag, not a tier.</summary>
    const double RockFace = 6;

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
    public void Forms(MeshBuilder mesh, RailLine line, Double3 eye, Town town, double from, double to, double time, Sim.Route.Route? route = null)
    {
        var plan = town.Plan;
        if (plan.Form.Length == 0 || plan.Bounds is not { } b || b.Gate < from - 200 || b.Rear > to + 200)
            return;
        var o = town.Anchor();
        var frame = Basis(o.Tangent, o.Position, eye, 0);
        var ground = Piece($"town-form-{plan.Name}-{plan.Form}-{plan.Gate:0}-{plan.Houses.Count}", () => FormGround(town));
        mesh.Instances.Add(new MeshInstance(ground, frame));
        // The land it stands in past its wall (note 692's landforms), in chunks drawn as they're near enough to see.
        if (town.Ground.Margin > 0 && Scene(route) is { } scene)
        {
            var g = town.Ground;
            var along0 = new Double3(o.Tangent.X, 0, o.Tangent.Z).Normalized;
            var right0 = Double3.Cross(along0, Double3.Up).Normalized;
            for (double cs = g.S0; cs < g.S0 + g.Ns; cs += LandChunk)
                for (double cd = g.D0; cd < g.D0 + g.Nd; cd += LandChunk)
                {
                    var mid = o.Position + along0 * (cs + LandChunk / 2) + right0 * (cd + LandChunk / 2);
                    if ((mid - eye).Length > 900)
                        continue;
                    double a0 = cs, a1 = Math.Min(cs + LandChunk, g.S0 + g.Ns), b0 = cd, b1 = Math.Min(cd + LandChunk, g.D0 + g.Nd);
                    // (Its far chunks cast no shadow: the moon's and the lamp's maps are for what's near.)
                    if (Piece($"town-land-{plan.Name}-{plan.Form}-{plan.Gate:0}-{cs:0}-{cd:0}", () => FormLand(town, scene.Terrain, a0, a1, b0, b1)) is { Vertices.Length: > 0 } land)
                        mesh.Instances.Add(new MeshInstance(land, frame, Shadowless: (mid - eye).Length > 160));
                }
        }
        // The yard's walls (note 692; Sim TownPlan.Wards): the wall's piece at 0.7, a bay at a time, merlons to the yard.
        if (plan.Wards.Count > 0)
        {
            var along = new Double3(o.Tangent.X, 0, o.Tangent.Z).Normalized;
            const double bay = Sim.Run.Fortresses.WallBay;
            float squat = (float)(Sim.Run.Fortresses.WardHeight / Sim.Run.Fortresses.WallHeight), thin = (float)(Sim.Run.Fortresses.WardHalf / Sim.Run.Fortresses.WallHalf);
            foreach (var (s0, d0, s1, _) in plan.Wards)
            {
                int outside = d0 < 0 ? 1 : -1;
                var wall = Piece($"wall-{outside}", () => StructureKit.Wall(_look, outside));
                for (double x = s0; x < s1 - 0.05; x += bay)
                {
                    double run = Math.Min(bay, s1 - x);
                    var at = town.World(x, d0);
                    if ((at - eye).Length > 360)
                        continue;
                    mesh.Instances.Add(new MeshInstance(wall, Matrix4x4.CreateScale(thin, squat, (float)(run / bay)) * Basis(o.Tangent, at, eye, 0)));
                }
            }
        }
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
        // A fen's reeds and drowned trees in its open water (note 692; the director, 14 Oct: "built on a swampy marsh"), a
        // drowned ward's few dead trees: wherever the water's open (no deck over it, none beside), near enough to see.
        if (plan.Form is "dykeland" or "tidewater")
        {
            var g = town.Ground;
            bool fen = plan.Form == "dykeland";
            bool Open(double s, double d) => g.Water(s, d) is { } w && g.Level(s, d) < w - 0.05;
            for (double s = Math.Floor(g.S0 / MarshCell) * MarshCell; s < g.S0 + g.Ns; s += MarshCell)
                for (double d = Math.Floor(g.D0 / MarshCell) * MarshCell; d < g.D0 + g.Nd; d += MarshCell)
                {
                    uint h = (uint)((int)Math.Round(s) * 73856093 ^ (int)Math.Round(d) * 19349663);
                    h = (h ^ (h >> 13)) * 0x5bd1e995u;
                    float roll = (h >> 8) / 16777216f;
                    bool tree = roll < (fen ? 0.012f : 0.004f), reed = fen && roll > 0.74f;
                    if (!tree && !reed)
                        continue;
                    // Jittered off the grid, clear of any deck by a few metres.
                    double js = s + ((h & 0xF) / 15.0 - 0.5) * MarshCell, jd = d + (((h >> 4) & 0xF) / 15.0 - 0.5) * MarshCell;
                    if (!Open(js, jd) || !Open(js + 3, jd) || !Open(js - 3, jd) || !Open(js, jd + 3) || !Open(js, jd - 3))
                        continue;
                    var at = town.World(js, jd);
                    if ((at - eye).Length > (tree ? 420 : 240))
                        continue;
                    int v = (int)(h >> 24) % 3;
                    var piece = tree ? Piece($"marsh-tree-{v}", () => WorldKit.DeadTree(_look, v, 7 + v * 2.5f)) : Piece($"marsh-reeds-{v}", () => SettingKit.Reeds(_look, v));
                    mesh.Instances.Add(new MeshInstance(piece, Basis(o.Tangent, at, eye, (h >> 16) % 628 / 100f), Shadowless: !tree));
                }
        }
        // The landmarks: the kit's pieces where the form put them.
        foreach (var f in plan.Fixtures)
        {
            if (f.House >= 0 || !Landmark(f.Kind) || plan.Works is { } wk && wk.Holds(f.S, f.D, 1))
                continue;
            var at = town.World(f.S, f.D);
            // A boat rides on the water, not its bed (note 692).
            if (f.Kind == "dory" && town.Ground.Water(f.S, f.D) is { } surface)
                at += Double3.Up * (surface - town.Ground.Level(f.S, f.D));
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
                "dory" when ((int)Math.Abs(f.S * 7 + f.D * 13) % 3) is var v => (Piece($"dory-afloat-{v}", () => MaritimeKit.Afloat(_look, v)), 1f),
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

    static double SegDistance(double s, double d, (double S, double D) a, (double S, double D) b)
    {
        double es = b.S - a.S, ed = b.D - a.D, len2 = es * es + ed * ed;
        double k = len2 < 1e-9 ? 0 : Math.Clamp(((s - a.S) * es + (d - a.D) * ed) / len2, 0, 1);
        double x = a.S + es * k - s, y = a.D + ed * k - d;
        return Math.Sqrt(x * x + y * y);
    }

    /// <summary>A drawn stair's rise a step (m).</summary>
    const double StepRise = 0.3;

    /// <summary>A chunk of a town's land past its wall (m), and the grid it's laid on (m).</summary>
    const double LandChunk = 96, LandGrid = 6;

    /// <summary>
    /// A chunk of the land a town type stands in, past its wall (note 692's landforms; the director, 13 Oct: "integrating the
    /// layout of the town with the layout of the landscape"): a grid over what the town shapes there (its headland, the sea's
    /// bed, a mesa's ditch, a valley's sides), easing into the line plan's land at the margin's edge, rock where it's steep,
    /// mud under water, heath up high, the land's grass elsewhere. In the town's frame from its anchor, as its ground is.
    /// </summary>
    MeshAsset FormLand(Town town, Sim.LineGen.TerrainField terrain, double s0, double s1, double d0, double d1)
    {
        var g = town.Ground;
        var o = town.Anchor();
        var along = new Double3(o.Tangent.X, 0, o.Tangent.Z).Normalized;
        var right = Double3.Cross(along, Double3.Up).Normalized;
        var k = new Kit(_look, 9300 + town.Plan.Name.Length) { Baked = 0 };
        double H(double s, double d)
        {
            double w = g.Blend(s, d), land = g.Inside(s, d) ? g.Level(s, d) : g.Bed(s, d);
            if (w >= 1)
                return land;
            var p = o.Position + along * s + right * d;
            return w * land + (1 - w) * (terrain.Height(p.X, p.Z) - o.Position.Y);
        }
        bool Out(double s, double d) => !g.Inside(s, d) && g.Drawn(s, d, FormApron);
        static Vector3 V(double s, double y, double d) => new((float)d, (float)y, (float)-s);
        int ns = (int)Math.Ceiling((s1 - s0) / LandGrid), nd = (int)Math.Ceiling((d1 - d0) / LandGrid);
        var h = new double[ns + 1, nd + 1];
        for (int i = 0; i <= ns; i++)
            for (int j = 0; j <= nd; j++)
                h[i, j] = H(Math.Min(s1, s0 + i * LandGrid), Math.Min(d1, d0 + j * LandGrid));
        for (int i = 0; i < ns; i++)
            for (int j = 0; j < nd; j++)
            {
                double sa = s0 + i * LandGrid, sb = Math.Min(s1, sa + LandGrid), da = d0 + j * LandGrid, db = Math.Min(d1, da + LandGrid);
                if (!Out(sa, da) && !Out(sb, da) && !Out(sa, db) && !Out(sb, db) && !Out((sa + sb) / 2, (da + db) / 2))
                    continue;
                double ha = h[i, j], hb = h[i + 1, j], hc = h[i + 1, j + 1], hd = h[i, j + 1];
                double steep = Math.Max(Math.Max(Math.Abs(ha - hb), Math.Abs(hc - hd)), Math.Max(Math.Abs(ha - hd), Math.Abs(hb - hc))) / LandGrid;
                double mean = (ha + hb + hc + hd) / 4;
                bool wet = g.Water((sa + sb) / 2, (da + db) / 2) is { } wl && mean < wl;
                if (steep > 0.7)
                    k.Use("rock_cliff", Palette.DeepBrown, 0.8f, 0.05f, tile: 6);
                else if (wet)
                    k.Use("ground_mud", Palette.DeepBrown, 0.95f, 0.2f, tile: 4);
                else if (steep > 0.35)
                    k.Use("granite_lichen", Palette.DeepBrown, 0.85f, 0.05f, tile: 5);
                else
                    k.Use(mean > 8 ? "ground_heath" : "ground_grass", Palette.DeepBrown, 0.9f, 0.04f, tile: 4);
                var (pa, pb, pc, pd) = (V(sa, ha, da), V(sb, hb, da), V(sb, hc, db), V(sa, hd, db));
                // Corners as Kit.Box's top (x across, z back down the line), texture in the frame's metres.
                k.Quad(pa, pb, pc, pd, new(pa.X, pa.Z), new(pb.X, pb.Z), new(pc.X, pc.Z), new(pd.X, pd.Z));
            }
        return k.Build($"town-land-{town.Plan.Name}-{s0:0}-{d0:0}");
    }

    /// <summary>The landmarks a town type stands (TownForms' kinds) that the square and the works don't draw.</summary>
    static bool Landmark(string kind) => kind is "lighthouse" or "beacon" or "chapel" or "watchtower" or "lift" or "crane" or "boatshed" or "seedhouse" or "glasshouse" or "dory";

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
        var rects = g.Rects(FormApron, insideOnly: g.Margin > 0);
        double floor = rects.Count > 0 ? rects.Min(r => r.Level) - 1.5 : -2;
        bool quarry = plan.Form == "quarry";
        string land = plan.Form switch { "quarry" => "granite_lichen", "tidewater" => "cobbles", "harbour" => "ground_heath", "dykeland" => "marsh", _ => "ground_mud" };
        string face = quarry ? "rock_cliff" : "stone_block";
        static Vector3 V(double s, double y, double d) => new((float)d, (float)y, (float)-s);
        // The sides of a rectangle with lower ground beside them (the piece's +X is +D, its +Z back down the line): only
        // those are seen, the rest stand against ground as high (half the town's ground, on a quarry's benches).
        Kit.Faces Exposed(double s0, double s1, double d0, double d1, double level)
        {
            bool Lower(double s, double d) => g.Bed(s, d) < level - 0.05;
            bool Along(double s, double da, double db)
            {
                for (double d = da + 0.5; d < db; d += 1)
                    if (Lower(s, d))
                        return true;
                return false;
            }
            bool Across(double d, double sa, double sb)
            {
                for (double s = sa + 0.5; s < sb; s += 1)
                    if (Lower(s, d))
                        return true;
                return false;
            }
            var faces = Kit.Faces.None;
            if (Along(s0 - 0.5, d0, d1))
                faces |= Kit.Faces.PosZ;
            if (Along(s1 + 0.5, d0, d1))
                faces |= Kit.Faces.NegZ;
            if (Across(d0 - 0.5, s0, s1))
                faces |= Kit.Faces.NegX;
            if (Across(d1 + 0.5, s0, s1))
                faces |= Kit.Faces.PosX;
            return faces;
        }
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
                    // Its piles round its edges, every few metres (a stage of houses stands on them, note 692; what's
                    // under its middle can't be seen).
                    foreach (var (ps, pd) in Piles(s0 + 0.3, s1 - 0.3, d0 + 0.3, d1 - 0.3))
                        k.Box(V(ps + 0.15, g.Bed(ps, pd) - 0.5, pd - 0.15), V(ps - 0.15, level - 0.25, pd + 0.15), false, false);
                    break;
                case 2:
                    // A basin's bed, mud under the water.
                    k.Use("ground_mud", Palette.DeepBrown, 0.95f, 0.2f, tile: 3);
                    k.Box(lo, hi, Kit.Faces.PosY);
                    break;
                default:
                    // Land: its top, a hand thick, on a body whose sides are the face of the step down from it.
                    var sides = Exposed(s0, s1, d0, d1, level);
                    k.Use(level == 0 ? "ground_mud" : land, Palette.DeepBrown, 0.9f, 0.04f, tile: 3);
                    k.Box(V(s1, level - 0.3, d0), V(s0, level, d1), Kit.Faces.PosY | sides);
                    if (level - 0.3 > floor && sides != Kit.Faces.None)
                    {
                        // A drop past a storey's height is the hill's own rock, cut back (the director's renders: towns on their
                        // crags); a tier's step is built, the masonry of a retaining wall.
                        double drop = level - new[] { g.Level(s0 - 1.5, (d0 + d1) / 2), g.Level(s1 + 1.5, (d0 + d1) / 2), g.Level((s0 + s1) / 2, d0 - 1.5), g.Level((s0 + s1) / 2, d1 + 1.5) }.Min();
                        k.Use(drop > RockFace ? "rock_cliff" : face, Palette.DeepBrown, 0.8f, 0.05f, tile: drop > RockFace ? 6 : 3);
                        k.Box(lo, V(s0, level - 0.3, d1), sides);
                    }
                    break;
            }
        }
        // The beds (a glasshouse town's plots): ridges of earth along each, a crop on them under the grow lamps' rota.
        foreach (var d in plan.Districts.Where(x => x.Kind == DistrictKind.Plots))
        {
            double ps0 = d.Outline.Min(p => p.S), ps1 = d.Outline.Max(p => p.S), pd0 = d.Outline.Min(p => p.D), pd1 = d.Outline.Max(p => p.D);
            for (double s = ps0 + 0.8; s < ps1 - 0.4; s += 1.3)
            {
                k.Use("ground_mud", Palette.DeepBrown, 0.95f, 0.05f, tile: 2);
                k.Box(V(s + 0.35, -0.1, pd0 + 0.6), V(s - 0.35, 0.22, pd1 - 0.6), Kit.Faces.PosY | Kit.Faces.Sides);
                k.Use("bog_sphagnum", new Vector3(0.25f, 0.4f, 0.15f), 0.6f, 0.05f, tile: 1.5f);
                k.Box(V(s + 0.2, 0.22, pd0 + 0.8), V(s - 0.2, 0.5, pd1 - 0.8), Kit.Faces.PosY | Kit.Faces.Sides);
            }
        }
        // A ramp's sides drawn along it (the Sim's walls step with its raster; the eye sees the ramp's own line): a wall each
        // side from the lower of it and the ground beside it to a parapet over the higher.
        var ramps = plan.Ways.Where(w => w.Levels is not null).ToList();
        bool ByARamp(double s, double d) => ramps.Any(w => Enumerable.Range(1, w.Points.Count - 1).Any(i =>
            SegDistance(s, d, w.Points[i - 1], w.Points[i]) < w.Width / 2 + 1.6));
        k.Use(face, Palette.DeepBrown, 0.8f, 0.05f, tile: 3);
        foreach (var w in ramps)
            for (int i = 1; i < w.Points.Count; i++)
            {
                var (a, z) = (w.Points[i - 1], w.Points[i]);
                double es = z.S - a.S, ed = z.D - a.D, len = Math.Sqrt(es * es + ed * ed);
                if (len < 0.1)
                    continue;
                foreach (int side in new[] { -1, 1 })
                {
                    double ns = -ed / len * side, nd = es / len * side, off = w.Width / 2 + 0.15;
                    double la = w.Levels![i - 1], lz = w.Levels[i];
                    double ba = g.Bed(a.S + ns * (off + 1), a.D + nd * (off + 1)), bz = g.Bed(z.S + ns * (off + 1), z.D + nd * (off + 1));
                    // (Where it's level with the ground beside it, it's open to it: the street to the houses either side.)
                    if (Math.Abs(la - ba) <= Sim.Towns.TownGround.Stride && Math.Abs(lz - bz) <= Sim.Towns.TownGround.Stride)
                        continue;
                    double lowA = Math.Min(la, ba) - 0.3, lowZ = Math.Min(lz, bz) - 0.3, highA = Math.Max(la, ba) + 0.9, highZ = Math.Max(lz, bz) + 0.9;
                    var (pa, pz) = ((a.S + ns * off, a.D + nd * off), (z.S + ns * off, z.D + nd * off));
                    k.Quad(V(pa.Item1, lowA, pa.Item2), V(pz.Item1, lowZ, pz.Item2), V(pz.Item1, highZ, pz.Item2), V(pa.Item1, highA, pa.Item2), twoSided: true);
                }
            }
        // The rails along the deep water and the parapets along the terraces' edges: along the districts' own outlines, a
        // few long runs where the Sim's walls (TownGround.Edges) step with its raster, each piece where the ground either side
        // of it calls for one (a stride's step, or walkable ground on deep water).
        void Run((double S, double D) a, (double S, double D) z, double y, double height, double half, bool rail)
        {
            double es = z.S - a.S, ed = z.D - a.D, len = Math.Sqrt(es * es + ed * ed);
            if (len < 0.2)
                return;
            float yaw = (float)Math.Atan2(es, ed);
            var at = Matrix4x4.CreateRotationY(yaw) * Matrix4x4.CreateTranslation(V((a.S + z.S) / 2, y, (a.D + z.D) / 2));
            k.With(at, () =>
            {
                if (rail)
                {
                    k.Box(new Vector3(-(float)len / 2, (float)height - 0.12f, -0.06f), new Vector3((float)len / 2, (float)height, 0.06f));
                    for (double x = -len / 2; x <= len / 2; x += 3)
                        k.Box(new Vector3((float)x - 0.07f, 0, -0.07f), new Vector3((float)x + 0.07f, (float)height, 0.07f), top: false, bottom: false);
                }
                else
                    k.Box(new Vector3(-(float)len / 2 - 0.1f, 0, -(float)half), new Vector3((float)len / 2 + 0.1f, (float)height, (float)half), true, false);
            });
        }
        bool Walkable(double s, double d) => g.Water(s, d) is not { } wl || wl - g.Bed(s, d) <= Sim.Towns.TownGround.Wade || g.Deck(s, d);
        foreach (var d in plan.Districts.Where(x => !x.Outside && x.Falloff <= 0))
        {
            var o = d.Outline;
            for (int i = 0; i < o.Count; i++)
            {
                var (pa, pz) = (o[i], o[(i + 1) % o.Count]);
                double es = pz.S - pa.S, ed = pz.D - pa.D, len = Math.Sqrt(es * es + ed * ed);
                if (len < 0.5)
                    continue;
                // Its normal, and the ground a step either side of each piece of it.
                double ns = -ed / len, nd = es / len;
                int pieces = Math.Max(1, (int)Math.Ceiling(len / 4));
                for (int p = 0; p < pieces; p++)
                {
                    double t0 = (double)p / pieces, t1 = (double)(p + 1) / pieces, tm = (t0 + t1) / 2;
                    double ms = pa.S + es * tm, md = pa.D + ed * tm;
                    if (!g.Inside(ms, md) || ByARamp(ms, md))
                        continue;
                    var (as_, ad) = (ms + ns * 1.2, md + nd * 1.2);
                    var (bs, bd) = (ms - ns * 1.2, md - nd * 1.2);
                    bool wa = Walkable(as_, ad), wb = Walkable(bs, bd);
                    var a0 = (pa.S + es * t0, pa.D + ed * t0);
                    var a1 = (pa.S + es * t1, pa.D + ed * t1);
                    if (wa != wb)
                    {
                        // Walkable ground on deep water: a timber rail on posts at the ground's (or the deck's) level.
                        double level = wa ? g.Level(as_, ad) : g.Level(bs, bd);
                        k.Use("wood_grey", Palette.DeepBrown, 0.85f, 0.05f, tile: 2);
                        Run(a0, a1, level, 1.0, 0.06, true);
                        continue;
                    }
                    if (!wa)
                        continue;
                    double la = g.Level(as_, ad), lb = g.Level(bs, bd);
                    if (Math.Abs(la - lb) <= Sim.Towns.TownGround.Stride)
                        continue;
                    // A deck's edge over the marsh (a fen's stages, note 692): its timber rail, not a terrace's stone.
                    if (g.Deck(as_, ad) || g.Deck(bs, bd))
                    {
                        k.Use("wood_grey", Palette.DeepBrown, 0.85f, 0.05f, tile: 2);
                        Run(a0, a1, Math.Max(la, lb), 1.0, 0.06, true);
                        continue;
                    }
                    // A step past a stride: the stone parapet on the higher side.
                    k.Use(face, Palette.DeepBrown, 0.8f, 0.05f, tile: 3);
                    Run(a0, a1, Math.Max(la, lb) - 0.3, 1.2, 0.22, false);
                }
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
                // A steep climb is a flight of steps (a tread and a riser each, a hand's rise apiece), the rest a slope.
                int steps = Math.Abs(lz - la) / len > 0.2 ? (int)Math.Ceiling(Math.Abs(lz - la) / StepRise) : 0;
                if (steps == 0)
                {
                    k.Quad(V(a.S - ns, la, a.D - nd), V(z.S - ns, lz, z.D - nd), V(z.S + ns, lz, z.D + nd), V(a.S + ns, la, a.D + nd), twoSided: true);
                    continue;
                }
                for (int st = 0; st < steps; st++)
                {
                    double t0 = (double)st / steps, t1 = (double)(st + 1) / steps;
                    // The tread at this step's level from its front to its back; the riser up to it at its front.
                    double hTread = la + (lz - la) * (lz > la ? t1 : t0), hPrev = la + (lz - la) * (lz > la ? t0 : t1);
                    var (fs, fd) = (a.S + es * t0, a.D + ed * t0);
                    var (bs, bd) = (a.S + es * t1, a.D + ed * t1);
                    k.Quad(V(fs - ns, hTread, fd - nd), V(bs - ns, hTread, bd - nd), V(bs + ns, hTread, bd + nd), V(fs + ns, hTread, fd + nd), twoSided: true);
                    var (rs, rd) = lz > la ? (fs, fd) : (bs, bd);
                    k.Quad(V(rs - ns, hTread, rd - nd), V(rs + ns, hTread, rd + nd), V(rs + ns, hPrev, rd + nd), V(rs - ns, hPrev, rd - nd), twoSided: true);
                }
            }
        }
        // The breakwaters (note 692; Sim TownPlan.Moles): a stone mole from the sea bed to its walk, cobbled, a parapet each
        // side, its round head at the end where its light stands.
        double moleFoot = (plan.Districts.Where(x => x.Kind == DistrictKind.Water).Select(x => x.Level - x.Depth).DefaultIfEmpty(-4).Min()) - 1;
        foreach (var mole in plan.Moles)
        {
            const float half = (float)Sim.Towns.TownForms.MoleHalf, top = (float)Sim.Towns.TownForms.MoleLevel;
            for (int i = 1; i < mole.Count; i++)
            {
                var (a, z) = (mole[i - 1], mole[i]);
                double es = z.S - a.S, ed = z.D - a.D, len = Math.Sqrt(es * es + ed * ed);
                if (len < 0.2)
                    continue;
                float yaw = (float)Math.Atan2(es, ed), l = (float)len / 2 + half * 0.5f;
                var at = Matrix4x4.CreateRotationY(yaw) * Matrix4x4.CreateTranslation(V((a.S + z.S) / 2, 0, (a.D + z.D) / 2));
                k.With(at, () =>
                {
                    k.Use("stone_block", Palette.DeepBrown, 0.85f, 0.05f, tile: 3);
                    k.Box(new Vector3(-l, (float)moleFoot, -half), new Vector3(l, top, half), Kit.Faces.Sides);
                    k.Use("cobbles", Palette.DeepBrown, 0.9f, 0.05f, tile: 2.5f);
                    k.Box(new Vector3(-l, top - 0.2f, -half), new Vector3(l, top, half), Kit.Faces.PosY);
                    k.Use("stone_block", Palette.DeepBrown, 0.85f, 0.05f, tile: 2);
                    foreach (float z0 in new[] { -half, half - 0.5f })
                        k.Box(new Vector3(-l, top, z0), new Vector3(l, top + 1.0f, z0 + 0.5f), Kit.Faces.Sides | Kit.Faces.PosY);
                });
            }
            // Its head: an eight-sided bastion round the light.
            var (hs, hd) = mole[^1];
            const float r = (float)Sim.Towns.TownForms.MoleHead;
            foreach (float turn in new[] { 0f, MathF.PI / 4 })
                k.With(Matrix4x4.CreateRotationY(turn) * Matrix4x4.CreateTranslation(V(hs, 0, hd)), () =>
                {
                    k.Use("stone_block", Palette.DeepBrown, 0.85f, 0.05f, tile: 3);
                    k.Box(new Vector3(-r * 0.92f, (float)moleFoot, -r * 0.92f), new Vector3(r * 0.92f, top + 1.2f, r * 0.92f), Kit.Faces.Sides | Kit.Faces.PosY);
                });
        }
        return k.Build($"town-form-ground-{plan.Name}");
    }
}
