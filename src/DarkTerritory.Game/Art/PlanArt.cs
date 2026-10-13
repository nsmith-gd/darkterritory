using System.Numerics;
using System.Runtime.CompilerServices;
using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Stops;

namespace DarkTerritory.Game.Art;

/// <summary>
/// A generated line as the art pass draws it (linegen plan §12, §13, §17.2, §18): the land is the terrain field the sim
/// stands people on, so what you see is what you walk on; alternates and dead lines run through land of their own; and
/// the plan's boards, washouts, brass, rivers, halts and towns stand where the plan put them.
/// </summary>
public sealed partial class WorldArt
{
    /// <summary>What the art needs of a route's plan, built once per route: its line and the terrain field over it.</summary>
    sealed class PlanScene
    {
        public PlanScene(Route route)
        {
            Plan = route.Plan!;
            Line = route.Build();
            Terrain = ((PlanConditions)Line.Conditions!).Terrain;
            Main = Plan.Alignment.Select((a, i) => (a, i)).First(x => x.a.Role == EdgeRole.Main).i;
            Washouts = [.. Plan.Structures.Where(s => s.Type == StructureType.Washout && s.Edge == "main").Select(s => (s.S0, s.S1))];
            Villages = [.. route.Of(FeatureKind.Village).Where(f => f.Stop is not null).Select(f => (f.Start, f.End))];
            Clearings = [.. route.Features.Where(f => f.Stop is not null).Select(f => Clearing(f.Start, f.End, f.Stop!))];
        }

        /// <summary>A stop's ground, cleared of the woods: its zone, out on each side past the last thing it built there.</summary>
        static (double S0, double S1, double Left, double Right) Clearing(double s0, double s1, StopLayout stop)
        {
            double left = 12, right = 12;
            foreach (var b in stop.Buildings)
            {
                double half = Math.Max(b.Length, b.Width) / 2 + 10;
                right = Math.Max(right, b.D + half);
                left = Math.Max(left, -b.D + half);
            }
            foreach (var t in stop.Tracks)
                foreach (var q in t.Path)
                    (right, left) = (Math.Max(right, q.D + 8), Math.Max(left, -q.D + 8));
            return (s0, s1, left, right);
        }

        public LinePlan Plan { get; }
        public RailLine Line { get; }
        public TerrainField Terrain { get; }
        public (double S0, double S1)[] Washouts { get; }
        /// <summary>The main-line spans the stop generator laid villages over (PlanStops): their houses and halts are its.</summary>
        public (double S0, double S1)[] Villages { get; }

        public bool InVillage(string edge, double s0, double s1) => edge == "main" && Villages.Any(v => v.S0 < s1 && s0 < v.S1);

        /// <summary>The stops' ground (PlanStops), where the plan's woods don't grow.</summary>
        public (double S0, double S1, double Left, double Right)[] Clearings { get; }

        public bool InClearing(double s, double lateral) => Clearings.Any(c => s >= c.S0 && s < c.S1 && lateral > -c.Left && lateral < c.Right);
        /// <summary>The main line's index among the terrain field's edges.</summary>
        public int Main { get; }

        public RailLine EdgeLine(string edge) => edge == "main" ? Line : Line.Branches[Plan.Edge(edge).Branch].Local;

        /// <summary>The biome at a main-line distance (§13.1).</summary>
        public string BiomeAt(double s) => BiomeOf(Plan, s);

        public BiomeDef? Biome(double s) => Plan.Rules.Biomes.GetValueOrDefault(BiomeAt(s));

        /// <summary>
        /// Where a lake wants the land finer than the line's own columns (note 424): the stretch of the main line it lies
        /// beside and its laterals, its shore and its rim's bank with them. Out there the columns are 30-50 m apart, wider
        /// than many a lake's shore is long, so the land couldn't follow its shoreline and the water's edge stood over it.
        /// </summary>
        public IReadOnlyList<LakeBand> LakeBands => _lakeBands ??= [.. Plan.Lakes.Select(Band).OfType<LakeBand>()];
        LakeBand[]? _lakeBands;

        LakeBand? Band(PlanLake lake)
        {
            var rules = Plan.Rules.Terrain.Lakes;
            double reach = lake.RadiusM * lake.Stretch * (1 + lake.Wobble) + rules.RimCrestM + 10;
            double s0 = double.MaxValue, s1 = double.MinValue, l0 = double.MaxValue, l1 = double.MinValue;
            for (int i = 0; i < 36; i++)
            {
                double a = i * Math.Tau / 36, x = lake.X + Math.Cos(a) * reach, z = lake.Z + Math.Sin(a) * reach;
                var near = Terrain.Nearby(x, z, reach + 400).Where(q => q.Edge == Main).ToList();
                if (near.Count == 0)
                    continue;
                var q = near[0];
                (s0, s1, l0, l1) = (Math.Min(s0, q.S), Math.Max(s1, q.S), Math.Min(l0, q.Lateral), Math.Max(l1, q.Lateral));
            }
            return s0 > s1 ? null : new LakeBand(s0 - 10, s1 + 10, (float)l0, (float)l1);
        }
    }

    /// <summary>A stretch of the main line (s) and laterals across it where a lake wants the land finer (<see cref="PlanScene.LakeBands"/>).</summary>
    readonly record struct LakeBand(double S0, double S1, float Lat0, float Lat1);

    /// <summary>A plan's biome at a main-line distance (§13.1).</summary>
    static string BiomeOf(LinePlan plan, double s)
    {
        foreach (var b in plan.Biomes)
            if (b.Edge == "main" && s >= b.S0 && s < b.S1)
                return b.Biome;
        return plan.Biomes.Count > 0 ? plan.Biomes[^1].Biome : "farmland";
    }

    static readonly ConditionalWeakTable<Route, PlanScene> Scenes = new();

    static PlanScene? Scene(Route? route) => route?.Plan is null ? null : Scenes.GetValue(route, r => new PlanScene(r));

    /// <summary>A generated line's biome at a main-line distance (linegen plan §13.1), or null for a hand-laid one.</summary>
    public static string? BiomeAt(Route? route, double s) => Scene(route)?.BiomeAt(s);

    /// <summary>
    /// Whether <paramref name="eye"/> is within <paramref name="reach"/> metres of a brass field on a generated line (its
    /// crystals, <see cref="Brass"/>): where the air carries its dust (Effects.Corruption's <c>Air.Brass</c>).
    /// </summary>
    public static bool NearBrass(Route? route, Double3 eye, double reach = 35)
    {
        if (Scene(route) is not { } p)
            return false;
        foreach (var st in p.Plan.Structures)
        {
            if (st.Type != StructureType.BrassField)
                continue;
            var line = p.EdgeLine(st.Edge);
            for (double s = st.S0 - reach; s <= st.S1 + reach; s += 10)
                if ((line.Sample(Math.Clamp(s, 0, line.Length)).Position - eye).Length < reach)
                    return true;
        }
        return false;
    }

    static float SmoothStep(float a, float b, float x)
    {
        float t = Math.Clamp((x - a) / (b - a), 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>
    /// The land at <paramref name="s"/>, <paramref name="lateral"/> out, above rail height: the terrain field (§12.2),
    /// with the kit's own ballast shoulders and ditch (<paramref name="near"/>) close in, where the formation is anyway.
    /// </summary>
    static float Relief(PlanScene p, Route route, double s, float lateral, float near)
    {
        var t = p.Line.Sample(Math.Clamp(s, 0, p.Line.Length));
        var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
        var at = t.Position + right * lateral;
        float land = (float)(p.Terrain.Height(at.X, at.Z) - t.Position.Y);
        float a = MathF.Abs(lateral);
        // Under a span, or over a bore, it's all the land's (the valley, the hill).
        if (route.InTunnel(s) || route.BridgeAt(s) is not null)
            return land;
        float h = float.Lerp(near, land, SmoothStep(3.7f, 9f, a));
        // A washout (§7.3): the bed's gone into the gully it cut.
        foreach (var (s0, s1) in p.Washouts)
            if (s > s0 - 6 && s < s1 + 6 && a < 9)
                h -= 2.2f * SmoothStep(-6, 2, (float)Math.Min(s - s0, s1 - s)) * (1 - SmoothStep(4, 9, a));
        return h;
    }

    /// <summary>
    /// A biome's ground as the texture library has it (biomes.json "ground"), and the bare rock it goes to where the land
    /// is steep: the pair a generated line's land blends between.
    /// </summary>
    (int Ground, int Rock) BiomeGround(PlanScene p, double s)
    {
        var def = p.Biome(s);
        int ground = _look.Layer(BiomeTexture(def?.Ground)), second = _look.Layer(BiomeTexture(def?.Materials.FirstOrDefault() ?? "rock"));
        return (ground >= 0 ? ground : _look.Layer("ground_grass"), second >= 0 ? second : _look.Layer("rock_cliff"));
    }

    /// <summary>
    /// A biome's texture by its biomes.json name (the Maritime ground, tools/art/texgen/mat_maritime.py); an older plan's
    /// names are the splat families they stood for.
    /// </summary>
    static string BiomeTexture(string? name) => name switch
    {
        null => "ground_grass",
        "deadGrass" => "ground_grass",
        "soil" => "ground_forest",
        "mud" => "ground_mud",
        "rock" => "rock_cliff",
        "cinder" => "slag",
        _ => name,
    };

    /// <summary>
    /// How far the land gives way to its biome's second ground at a world point: on the steep (the slope), and in
    /// patches (outcrops, bare clay) the size of a field, so it isn't one texture to the horizon. A 2008 terrain's macro
    /// variation: world-space and slow, so it never repeats with the tiles.
    /// </summary>
    static float Patches(Vector3 world)
    {
        float big = Noise(world.X * 0.011f + 17.3f, world.Z * 0.011f - 4.1f), small = Noise(world.X * 0.05f - 9.2f, world.Z * 0.05f + 2.7f);
        return SmoothStep(0.58f, 0.78f, big * 0.75f + small * 0.25f);
    }

    /// <summary>A level crossing's plank: one heavy timber across the track, between and beside the rails, flush with their tops.</summary>
    static MeshAsset CrossingPlank(Look? look)
    {
        var k = new Kit(look, 3700);
        k.Use("wood_sleeper", Palette.DeepBrown, 0.9f, 0.05f, tile: 1);
        k.Tint = new Vector3(0.75f);
        foreach (var (x0, x1) in new[] { (-2.2f, -0.82f), (-0.66f, 0.66f), (0.82f, 2.2f) })
            k.Box(new Vector3(x0, 0.02f, -0.2f), new Vector3(x1, 0.16f, 0.2f), Kit.Faces.All & ~Kit.Faces.NegY);
        return k.Build("crossing-plank");
    }

    /// <summary>
    /// Whether a point beside the line is in a stand of forest: a world-space field (so the stands don't follow the line
    /// round), true over about <paramref name="cover"/> of the land, with a hard edge (a cut, an old field's line, a
    /// bog's shore). 1 in a stand, 0 out.
    /// </summary>
    static float Stand(RailLine line, double along, double lateral, float cover)
    {
        var t = line.Sample(Math.Clamp(along, 0, line.Length));
        var r = Double3.Cross(t.Tangent, Double3.Up).Normalized;
        return Stand(t.Position + r * lateral, cover);
    }

    /// <summary>A biome's stand cover (its tree density as a share of the land under forest; the sim's, note 371).</summary>
    static float Cover(BiomeDef def) => (float)Sim.Run.LinesideProps.Cover(def);

    /// <summary><see cref="Stand(RailLine, double, double, float)"/> at a world point: the sim's stands (note 371), where its trees are.</summary>
    static float Stand(Double3 w, float cover) => (float)Sim.Run.LinesideProps.Stand(w, cover);

    /// <summary>The land's tint at a world point: broad light and dark swathes, a little warmer and cooler, over the tiles.</summary>
    static Vector3 Macro(Vector3 world)
    {
        float a = Noise(world.X * 0.004f + 3.1f, world.Z * 0.004f + 8.7f), b = Noise(world.X * 0.021f - 1.3f, world.Z * 0.021f + 5.5f);
        float v = 0.72f + 0.42f * (a * 0.6f + b * 0.4f);
        float warm = (Noise(world.X * 0.007f - 6.6f, world.Z * 0.007f - 2.2f) - 0.5f) * 0.18f;
        return new Vector3(v * (1 + warm), v, v * (1 - warm));
    }

    /// <summary>How steep the land is at one vertex of a row across the line: rise over run to its neighbours.</summary>
    static float SlopeAt(Vector3[] row, int i) => SlopeAt(row, Math.Max(0, i - 1), Math.Min(row.Length - 1, i + 1));

    /// <summary>The slope across a row between two of its columns.</summary>
    static float SlopeAt(Vector3[] row, int before, int after)
    {
        var a = row[before];
        var b = row[after];
        float run = MathF.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Z - a.Z) * (b.Z - a.Z));
        return run < 1e-3f ? 0 : MathF.Abs(b.Y - a.Y) / run;
    }

    /// <summary>
    /// The country beside a generated line, by its biome (linegen plan §13.2, biomes.json): forest as thick as the biome
    /// grows it and as dead as it is, boulders where the land is rough and steep, reeds and dead trees in the marsh,
    /// fences and farmhouses in the fields, chimneys, tanks and broken walls in the ruin belt, headframes over the
    /// mines. Everything whose start is in [<paramref name="from"/>, <paramref name="to"/>), as the lineside is.
    /// </summary>
    void PlanDressing(MeshBuilder mesh, RailLine line, Route route, PlanScene p, Double3 eye, double from, double to, int seed, Func<double, double, bool> onBranch)
    {
        bool Clear(double s) => !route.InTunnel(s) && !route.InTunnel(s + 30) && !route.InTunnel(s - 30) && route.BridgeAt(s) is null;
        (Matrix4x4 M, float Slope) Place(double s, double lateral, float yaw, float scale, float sink, Vector3? stretch = null)
        {
            var t = line.Sample(Math.Clamp(s, 0, line.Length));
            var r = Double3.Cross(t.Tangent, Double3.Up).Normalized;
            float h = Ground(route, s, (float)lateral, 0) - sink;
            float slope = MathF.Abs(Ground(route, s, (float)lateral + 2, 0) - Ground(route, s, (float)lateral - 2, 0)) / 4;
            var o = (t.Position + r * lateral + Double3.Up * h).RelativeTo(eye);
            var fwd = Vector3.Normalize(new Vector3((float)t.Tangent.X, 0, (float)t.Tangent.Z));
            var right = Vector3.Cross(fwd, Vector3.UnitY);
            var m = new Matrix4x4(right.X, 0, right.Z, 0, 0, 1, 0, 0, -fwd.X, 0, -fwd.Z, 0, o.X, o.Y, o.Z, 1);
            return (Matrix4x4.CreateScale(stretch ?? Vector3.One * scale) * Matrix4x4.CreateRotationY(yaw) * m, slope);
        }
        bool Free(double s, double lateral) => !onBranch(s, lateral) && !p.InClearing(s, lateral) && PlanClear(route, line, s, lateral);
        // Near the line (within the chase camera's and a roof's reach), the spruce is modelled like the pine; out in the fog,
        // the crossed cards.
        MeshAsset Tree(string kind, int v, bool near, NovaKit.TreeForm form) => kind switch
        {
            "fir" => Piece($"fir-{v}-{form}", () => NovaKit.Conifer(_look, v, 12, 0.46f, form)),
            "birch" => Piece($"birch-{v % 3}", () => NovaKit.Birch(_look, v % 3)),
            "pine" => Piece($"pine-{v}", () => WorldKit.Pine(_look, v, 12)),
            "tamarack" => Piece($"tamarack-{v}-{form}", () => NovaKit.Conifer(_look, v, 12, 0.26f, form)),
            _ when near => Piece($"spruce3d-{v}-{form}", () => WorldKit.Spruce(_look, v, 12, form)),
            _ => Piece($"spruce-{v}-{form}", () => NovaKit.Conifer(_look, v, 12, 0.3f, form)),
        };

        // The trees, boulders and the rest beside the line are the sim's (notes 371, 389): it deals them from the night's
        // seed, alike on every machine, and stands them as walls, so what's drawn here is what's walked into. What's only
        // seen of each (its tint, a ghost or a dead spruce) is drawn from its own seed.
        var lineside = Sim.Run.LinesideProps.Of(route, line);
        var forts = Forts(line);
        foreach (var prop in lineside?.Props(from, to) ?? [])
        {
            if (prop.Kind == Sim.Run.LinesideKind.Pole || Sim.Run.LinesideProps.InsideAFort(forts, prop.Along, prop.Lateral))
                continue;
            var rng = new Random(unchecked((int)prop.Seed));
            double along = prop.Along, offset = prop.Lateral;
            float yaw = (float)prop.Yaw;
            int variant = prop.Variant;
            if (prop.Kind == Sim.Run.LinesideKind.Piece)
            {
                // A homestead and its barn, woodpile and fence; a road's pole, a car; a fish shed, a wharf, a lighthouse; a
                // biome's buildings and stone walls (LinesideFootprints: the piece by the sim's name).
                if (!LinesideFootprints.ByName.TryGetValue(prop.Species, out var made))
                    continue;
                var built = Piece(made.Cached(variant), () => made.Make(_look, variant));
                var at = Place(along, offset, yaw, (float)prop.Size, (float)prop.Sink).M;
                if (made.Instanced)
                    mesh.Instances.Add(new MeshInstance(built, at));
                else
                    mesh.Append(built, at);
                continue;
            }
            if (prop.Kind == Sim.Run.LinesideKind.Rock)
            {
                // The woods' boulders; an erratic, paler than the ledge; an outcrop's slab; the Atlantic's granite ledges and
                // its weed-black rocks at the tide line; boulders along an edge, granite or Fundy's red sandstone.
                var rock = Piece($"rock-{variant}", () => WorldKit.Rock(_look, variant, 1));
                var stretch = Sim.Run.LinesideProps.RockStretch(prop.Species);
                float size = (float)prop.Size, sink = (float)prop.Sink;
                var laid = stretch.X == 1 && stretch.Z == 1 ? Place(along, offset, yaw, size, sink).M
                    : Place(along, offset, yaw, 1, sink, new Vector3((float)stretch.X, (float)stretch.Y, (float)stretch.Z) * size).M;
                var shade = prop.Species switch
                {
                    "erratic" => new Vector3(1.35f, 1.35f, 1.3f),
                    "outcrop" => new Vector3(1.2f, 1.2f, 1.15f),
                    "ledge" => new Vector3(1.3f, 1.28f, 1.22f),
                    "weed" => new Vector3(0.28f, 0.25f, 0.18f),
                    "redBoulder" => new Vector3(1.2f, 0.75f, 0.6f),
                    _ => Vector3.One,
                };
                mesh.Append(rock, laid, shade);
                continue;
            }
            float height = (float)prop.Height;
            if (prop.Species == "fieldSpruce")
            {
                // Pasture spruce (maritime-rules.md §5): an old field grown in solid, all of an age and a height.
                mesh.Append(Piece($"spruce-{variant}", () => NovaKit.Conifer(_look, variant, 12, 0.3f)), Place(along, offset, yaw, height / 12, (float)prop.Sink).M,
                    new Vector3(0.85f, 0.95f, 0.9f));
                continue;
            }
            if (prop.Species == "apple")
            {
                // An orchard gone to ruin: small, gnarled dead apple trees in their rows.
                mesh.Append(Piece($"dead-{variant}", () => WorldKit.DeadTree(_look, variant, 10)), Place(along, offset, yaw, height / 10, (float)prop.Sink).M,
                    new Vector3(0.9f, 0.85f, 0.8f));
                continue;
            }
            bool dead = prop.Dead, corrupted = prop.Corrupted;
            string kind = prop.Species;
            var (m, _) = Place(along, offset, yaw, height / 12, 0.15f);
            // Its form and lean (note 395), from a stream of its own off its art seed, so the draws below are as they were.
            var (form, lean) = Form(prop.Seed, p.Biome(along));
            MeshAsset piece = dead
                ? rng.Next(2) == 0 ? Piece($"ghost-{variant}", () => NovaKit.GhostSpruce(_look, variant)) : Piece($"dead-{variant % 2}", () => WorldKit.DeadTree(_look, variant % 2, 10))
                : Tree(kind, variant, Math.Abs(offset) < NearSpruce, form);
            if (dead)
                (m, _) = Place(along, offset, yaw, height / (piece.Name.StartsWith("ghost") ? 8 + variant * 2.5f : 10) * 0.9f, 0.15f);
            if (kind == "birch" && !dead)
                (m, _) = Place(along, offset, yaw, height / 11, 0.1f);
            // Leant, about its foot (where the Sim's wall stands): a few degrees, any way.
            if (lean.Angle > 0)
                m = Matrix4x4.CreateRotationZ(lean.Angle) * Matrix4x4.CreateRotationY(lean.Toward) * m;
            // Tamarack goes gold in the fall, before its needles drop (the bog's one colour).
            var tint = kind == "tamarack" && !dead ? new Vector3(1.55f, 1.2f, 0.55f) * (0.85f + 0.3f * (float)rng.NextDouble()) : new Vector3(0.8f + 0.3f * (float)rng.NextDouble());
            // The corruption's trees (GDD §30): charred black and sweating, the brass breaking out through the bark up
            // the trunk as well as heaped at the foot.
            if (corrupted)
                tint = new Vector3(0.32f, 0.24f, 0.2f);
            mesh.Append(piece, m, tint);
            if (corrupted)
            {
                // Brighter than a brass field's growths, a sick glow in them: what the eye should catch in the dead wood.
                var brass = Piece($"growth-{variant % 3}", () => BrassCluster(_look, variant % 3, glow: 0.4f));
                var glint = new Vector3(1.6f, 1.35f, 0.8f);
                mesh.Append(brass, Place(along + 0.6, offset, yaw, 2.6f, 0.05f).M, glint);
                var trunk = Place(along, offset, yaw, 1, 0.05f).M;
                for (int b = 0; b < 3; b++)
                {
                    float up = height * (0.2f + 0.22f * b), turn = yaw + b * 2.1f;
                    mesh.Append(brass, Matrix4x4.CreateScale(2.2f - 0.4f * b) * Matrix4x4.CreateRotationZ(1.2f) * Matrix4x4.CreateRotationY(turn)
                        * Matrix4x4.CreateTranslation(0, up, 0) * trunk, glint);
                }
            }
        }
        for (double s = Math.Ceiling(from / 12) * 12; s < to; s += 12)
        {
            if (!Clear(s) || p.Biome(s) is not { } def)
                continue;
            var rng = new Random(unchecked(seed * 73856093 ^ (int)(s / 12) * 19349663));
            float cover = Cover(def);
            // The stand's mass behind the single trees: walls of packed spires where a stand runs on out from the
            // line, one at its near edge's depth and one deep in it, so the forest has a body and a serrated top.
            // (On the barrens and the coast the stands are stunted: the wind-cut white spruce of the headlands.)
            bool stunted = def.Verge == "barrens";
            if (def.TreeDensity >= 0.3 && s % 24 < 12)
                foreach (int sideOf in new[] { -1, 1 })
                    foreach (double depth in new[] { 55 + rng.NextDouble() * 30, 125 + rng.NextDouble() * 40 })
                    {
                        double lateral = sideOf * depth, along = s + rng.NextDouble() * 4;
                        if (Stand(line, along, lateral, cover) < 0.5f || Stand(line, along, lateral + sideOf * 20, cover) < 0.5f || !Free(along, lateral))
                            continue;
                        var (m, slope) = Place(along, lateral, (float)(rng.NextDouble() - 0.5) * 0.25f, 1, 0.5f);
                        if (slope > 0.9f)
                            continue;
                        int tv = rng.Next(5);
                        float th = stunted ? 6 + (float)rng.NextDouble() * 4 : 13 + (float)rng.NextDouble() * 6;
                        mesh.Append(Piece($"treeline-{tv}-{th:0}", () => NovaKit.Treeline(_look, tv, 26, th)), m, new Vector3(0.8f + 0.25f * (float)rng.NextDouble()));
                    }
        }
        // The forest floor (note 395): deadfall, stumps and juniper under the stands; juniper and red blueberry on the
        // barrens and the burns. Low and passable, the art's alone, as the tufts and the alder are.
        for (double s = Math.Ceiling(from / 12) * 12; s < to; s += 12)
        {
            if (!Clear(s) || p.Biome(s) is not { } def)
                continue;
            bool barrens = def.Verge == "barrens", woods = def.TreeDensity >= 0.3;
            if (!barrens && !woods)
                continue;
            var rng = new Random(unchecked(seed * 48271 ^ (int)(s / 12) * 69621));
            int count = barrens ? 2 + rng.Next(2) : 1 + rng.Next(2);
            for (int i = 0; i < count; i++)
            {
                double offset = (rng.Next(2) == 0 ? -1 : 1) * (FloorNear + rng.NextDouble() * (barrens ? 40 : 30)), along = s + rng.NextDouble() * 12;
                double pick = rng.NextDouble();
                var kind = barrens
                    ? pick < 0.45 ? NovaKit.FloorKind.Juniper : pick < 0.9 ? NovaKit.FloorKind.Blueberry : NovaKit.FloorKind.Stump
                    : pick < 0.4 ? NovaKit.FloorKind.Deadfall : pick < 0.7 ? NovaKit.FloorKind.Stump : NovaKit.FloorKind.Juniper;
                int v = rng.Next(3);
                float yaw = (float)rng.NextDouble() * 6.28f, scale = 0.85f + 0.3f * (float)rng.NextDouble();
                if (!Free(along, offset))
                    continue;
                var (m, slope) = Place(along, offset, yaw, scale, kind == NovaKit.FloorKind.Deadfall ? 0.12f : 0.04f);
                if (slope < 0.7f)
                    mesh.Append(Piece($"floor-{kind}-{v}", () => NovaKit.Floor(_look, kind, v)), m, new Vector3(0.8f + 0.3f * (float)rng.NextDouble()));
            }
        }
        // Low growth along the verge: dead grass, the bog's reeds thick out to its water, the barrens' heath and lichen.
        for (double s = Math.Ceiling(from / 3) * 3; s < to; s += 3)
        {
            if (!Clear(s) || p.Biome(s) is not { } def)
                continue;
            bool reeds = def.Verge == "reeds", barrens = def.Verge == "barrens";
            var rng = new Random(unchecked(seed * 19349663 ^ (int)(s / 3) * 83492791));
            for (int k = 0; k < (reeds ? 4 : barrens ? 3 : 2); k++)
            {
                double offset = (rng.Next(2) == 0 ? -1 : 1) * (3.3 + rng.NextDouble() * (reeds ? 40 : barrens ? 30 : 14));
                double along = s + rng.NextDouble() * 3;
                if (!Free(along, offset))
                    continue;
                int v = rng.Next(3);
                bool weed = def.Trees.Contains("corrupted") && rng.NextDouble() < 0.15;
                var piece = reeds && !weed ? Piece($"reeds-{v}", () => SettingKit.Reeds(_look, v)) : Piece($"tuft-{v}-{weed}", () => WorldKit.Tuft(_look, v, weed));
                mesh.Append(piece, Place(along, offset, (float)rng.NextDouble() * 6.28f, (barrens ? 0.5f : 0.7f) + (float)rng.NextDouble() * 0.7f, 0.02f).M,
                    barrens ? new Vector3(0.75f, 0.55f, 0.45f) : Vector3.One);
            }
        }
        // Alder: the thicket that grows up along every right-of-way in the Maritimes (maritime-rules.md §5), a fringe
        // between the verge and the trees, thicker where the ground's wet, none on the open barrens or the dykeland.
        for (double s = Math.Ceiling(from / 6) * 6; s < to; s += 6)
        {
            if (!Clear(s) || p.Biome(s) is not { } def || def.Verge != "grass" || def.TreeDensity < 0.1)
                continue;
            var rng = new Random(unchecked(seed * 2654435 ^ (int)(s / 6) * 40503));
            double chance = Math.Min(0.8, 0.25 + def.TreeDensity * 0.35 + def.Water * 0.3);
            foreach (int side in new[] { -1, 1 })
            {
                if (rng.NextDouble() > chance)
                    continue;
                double offset = side * (5.5 + rng.NextDouble() * 6.5), along = s + rng.NextDouble() * 6;
                if (!Free(along, offset))
                    continue;
                int v = rng.Next(4);
                var (m, slope) = Place(along, offset, (float)rng.NextDouble() * 6.28f, 0.8f + (float)rng.NextDouble() * 0.6f, 0.1f);
                if (slope < 0.9f)
                    mesh.Append(Piece($"alder-{v}", () => NovaKit.Alder(_look, v)), m, new Vector3(0.75f + 0.35f * (float)rng.NextDouble()));
            }
        }
        // The country road (maritime-rules.md §2.2): gravel between grass verges, following the land, crossing the line
        // at grade over plank decks between crossbucks; homesteads strung along it on the far side, a woodpile and a
        // fence to each, a car left where it stopped now and then, the road's own poles. Nobody's home.
        var rr = p.Plan.Rules.Terrain.Roads;
        foreach (var road in p.Plan.Roads.Where(x => x.S1 > from && x.S0 < to))
        {
            double Lat(double at) => TerrainField.RoadLateral(road, p.Plan.Crossings, at, rr.RampM);
            var k = new Kit(_look, mesh) { SurfaceOrigin = new Vector3(W(eye.X), W(eye.Y), W(eye.Z)), Baked = 0 };
            // Gravel, paler than the land either side: the one light line through the country at night.
            k.Use(_look?.Layer("ballast") >= 0 ? "ballast" : "ground_mud", new Vector3(0.3f, 0.28f, 0.25f), 0.9f, 0.05f, tile: 2.5f);
            k.Tint = new Vector3(0.95f, 0.85f, 0.72f);
            double a0 = Math.Max(road.S0, from), a1 = Math.Min(road.S1, to);
            Vector3 Edge(double at, double across)
            {
                var t = line.Sample(Math.Clamp(at, 0, line.Length));
                var r = Double3.Cross(t.Tangent, Double3.Up).Normalized;
                double l = Lat(at) + across;
                return (t.Position + r * l + Double3.Up * (OnMesh(at, (float)l) + 0.12)).RelativeTo(eye);
            }
            // The land's mesh is sampled in columns across the line (PlanLateral), coarser than a road's bed: lay the road on
            // whichever is higher, its bed or the mesh's own surface there, so a bed cut into a slope isn't under the drawn land.
            float OnMesh(double at, float l)
            {
                float a = MathF.Abs(l), bed = Ground(route, at, l, 0);
                int i = 1;
                while (i < PlanLateral.Length - 1 && PlanLateral[i] < a)
                    i++;
                float l0 = PlanLateral[i - 1], l1 = PlanLateral[i], f = Math.Clamp((a - l0) / Math.Max(0.01f, l1 - l0), 0, 1);
                float mesh0 = Ground(route, at, MathF.Sign(l) * l0, 0), mesh1 = Ground(route, at, MathF.Sign(l) * l1, 0);
                return MathF.Max(bed, mesh0 + (mesh1 - mesh0) * f);
            }
            for (double at = a0; at < a1; at += 4)
            {
                double b = Math.Min(at + 4, a1);
                // Over the track the crossing's deck is the road.
                if (Math.Abs(Lat(at)) < 2.6 || Math.Abs(Lat(b)) < 2.6 || !Clear(at))
                    continue;
                double hw = rr.HalfWidthM - 0.3;
                var q0 = Edge(at, -hw);
                var q1 = Edge(at, hw);
                var q2 = Edge(b, hw);
                var q3 = Edge(b, -hw);
                k.Quad(q0, q3, q2, q1, new Vector2(q0.X, q0.Z), new Vector2(q3.X, q3.Z), new Vector2(q2.X, q2.Z), new Vector2(q1.X, q1.Z), twoSided: true);
            }
            // Crossings: a plank deck between and beside the rails, a crossbuck each side, by the road.
            foreach (var c in p.Plan.Crossings.Where(x => x.Road == road.Id && x.S >= from && x.S < to))
            {
                for (double at = c.S - 9; at <= c.S + 9; at += 0.45)
                    mesh.Append(Piece("crossing-plank", () => CrossingPlank(_look)), Place(at, 0, 0, 1, 0).M);
                foreach (int side in new[] { -1, 1 })
                {
                    double at = c.S - side * rr.RampM * 7 / Math.Max(1, road.OffsetM);
                    mesh.Instances.Add(new MeshInstance(Piece("crossbuck", () => NovaKit.Crossbuck(_look)),
                        Place(at, Lat(at) + side * (rr.HalfWidthM + 1.2), side > 0 ? MathF.PI / 2 : -MathF.PI / 2, 1, 0.2f).M));
                }
            }
            // Its homesteads, poles and cars are the sim's (note 389): drawn with the rest of the lineside, above.
        }
        // The shore's surf is the water's own lap now (scene.frag, note 424). Its sheds, wharves, lighthouses, ledges and
        // boulders are the sim's (note 389), drawn with the rest of the lineside.
        // What people left, and what the ice left (biomes.json props): the sim's too (note 389), drawn with the rest above.
    }

    /// <summary>Whether the track's bed and rails are there at <paramref name="s"/> on the main line (not across a washout).</summary>
    static bool Laid(Route? route, double s) => Scene(route) is not { } p || !p.Washouts.Any(w => s >= w.S0 && s <= w.S1);

    /// <summary>
    /// Clear of the plan's own track and water at a point beside the main line: somewhere a tree can stand. An alternate
    /// wanders off where the main line's branch check can't see it.
    /// </summary>
    static bool PlanClear(Route? route, RailLine line, double along, double offset)
    {
        if (Scene(route) is not { } p)
            return true;
        var t = line.Sample(Math.Clamp(along, 0, line.Length));
        var at = t.Position + Double3.Cross(t.Tangent, Double3.Up).Normalized * offset;
        foreach (var n in p.Terrain.Nearby(at.X, at.Z, 14))
            if (n.Edge != p.Main && Math.Abs(n.Lateral) < 9)
                return false;
        // Not on a road (its bed and shoulders).
        foreach (var road in p.Plan.Roads)
            if (along >= road.S0 && along <= road.S1
                && Math.Abs(offset - TerrainField.RoadLateral(road, p.Plan.Crossings, along, p.Plan.Rules.Terrain.Roads.RampM)) < p.Plan.Rules.Terrain.Roads.HalfWidthM + 2.5)
                return false;
        return p.Terrain.WaterAt(at.X, at.Z) is null;
    }

    readonly Dictionary<(int Branch, long Index), Cell> _branchCells = new();
    PlanScene? _branchScene;

    /// <summary>
    /// Everything of the plan's that isn't the main line's cells: alternates' and dead lines' land (cooked in cells like
    /// the main line's), the boards, washouts, brass, water, halts and towns, and the terminus's glow above the fog.
    /// </summary>
    public void Plan(MeshBuilder mesh, RailLine line, Route route, Double3 eye, double centre, float drawDistance, double time)
    {
        if (Scene(route) is not { } p)
            return;
        BranchLand(mesh, p, route, eye, drawDistance);
        Signs(mesh, p, eye, drawDistance);
        Hazards(mesh, p, eye, drawDistance, time);
        Banks(mesh, p, route, eye, drawDistance);
        Water(mesh, p, eye, drawDistance);
        FarLand(mesh, p, line, eye, centre, drawDistance);
        Places(mesh, p, eye, drawDistance);
        Glow(mesh, p, line, eye, centre);
    }

    /// <summary>How deep the land falls under a generated line's bridge (its valley's, §12.3), for its piers; null on a hand-laid one.</summary>
    /// <summary>What a generated line's plan built a bridge as (§12.3); null on a hand-laid one.</summary>
    public static StructureType? SpanType(Route? route, RouteFeature bridge) =>
        route?.Plan?.Structures.FirstOrDefault(s => s.Edge == "main" && Math.Abs(s.S0 - bridge.Start) < 0.5)?.Type;

    public static float? SpanDepth(Route? route, RouteFeature bridge) =>
        route?.Plan?.Structures.FirstOrDefault(s => s.Edge == "main" && Math.Abs(s.S0 - bridge.Start) < 0.5) is { HeightM: > 0 } st ? (float)Math.Round(st.HeightM) : null;

    // ------------------------------------------------------------------ alternates' and dead lines' land

    static readonly float[] BranchLateral = [3.7f, 5.5f, 8, 12, 17, 24, 33, 45, 60, 78];

    void BranchLand(MeshBuilder mesh, PlanScene p, Route route, Double3 eye, float drawDistance)
    {
        if (!ReferenceEquals(_branchScene, p))
        {
            _branchCells.Clear();
            _branchScene = p;
        }
        foreach (var a in p.Plan.Alignment.Where(a => a.Role is EdgeRole.Alternate or EdgeRole.DeadLine))
        {
            var local = p.Line.Branches[a.Branch].Local;
            long last = (long)Math.Floor(local.Length / CellLength);
            for (long i = 0; i <= last; i++)
            {
                double mid = Math.Min((i + 0.5) * CellLength, local.Length);
                if ((local.Sample(mid).Position - eye).Length > drawDistance + CellLength)
                {
                    _branchCells.Remove((a.Branch, i));
                    continue;
                }
                if (!_branchCells.TryGetValue((a.Branch, i), out var cell))
                    _branchCells[(a.Branch, i)] = cell = BranchCell(p, route, a.Branch, local, i);
                var at = Matrix4x4.CreateTranslation(cell.Origin.RelativeTo(eye));
                mesh.Instances.Add(new MeshInstance(cell.Soup, at));
                foreach (var (piece, m) in cell.Pieces)
                    mesh.Instances.Add(new MeshInstance(piece, m * at));
            }
        }
    }

    /// <summary>
    /// One 100 m cell of a branch's land either side of its bed, out to 78 m, where the main line's own land doesn't
    /// already reach (it runs 300 m out); a stand of pines on it where the sim stands them (LinesideProps.BranchTrees).
    /// </summary>
    Cell BranchCell(PlanScene p, Route route, int branch, RailLine local, long index)
    {
        double a = index * CellLength, b = Math.Min((index + 1) * CellLength, local.Length);
        var origin = local.Sample(a).Position;
        var built = new MeshBuilder();
        var surface = new Vector3(W(origin.X), W(origin.Y), W(origin.Z));
        int columns = BranchLateral.Length;
        bool Covered(Double3 w)
        {
            foreach (var n in p.Terrain.Nearby(w.X, w.Z, 300))
                if (n.Edge == p.Main)
                    return Math.Abs(n.Lateral) < 290;
            return false;
        }
        (Vector3 P, bool Covered)[] Row(double s, int side)
        {
            var t = local.Sample(s);
            var r = Double3.Cross(t.Tangent, Double3.Up).Normalized;
            var row = new (Vector3, bool)[columns];
            for (int c = 0; c < columns; c++)
            {
                var w = t.Position + r * (side * BranchLateral[c]);
                double h = c == 0 ? t.Position.Y - 0.35 : p.Terrain.Height(w.X, w.Z);
                row[c] = ((w with { Y = h }).RelativeTo(origin), Covered(w));
            }
            return row;
        }
        foreach (int side in new[] { -1, 1 })
        {
            var prev = Row(a, side);
            for (double s = a + 5; s <= b + 1e-6; s += 5)
            {
                var next = Row(Math.Min(s, b), side);
                for (int c = 0; c + 1 < columns; c++)
                {
                    if (prev[c].Covered && prev[c + 1].Covered && next[c].Covered && next[c + 1].Covered)
                        continue;
                    float lat = (BranchLateral[c] + BranchLateral[c + 1]) / 2;
                    var (la, lb, band) = GroundLayers(lat);
                    var colour = la >= 0 ? Vector3.One : Palette.MuddyOlive;
                    Corner Make(float l, double at) => new(colour * GroundShade(l, at), GroundBlend(band, l, at));
                    Quad(built, prev[c].P, prev[c + 1].P, next[c + 1].P, next[c].P, Make(BranchLateral[c], s - 5), Make(BranchLateral[c + 1], s - 5),
                        Make(BranchLateral[c + 1], s), Make(BranchLateral[c], s), surface, la, lb, la >= 0 && _look.Textures[la].TileMetres is { } tm ? tm : 2);
                }
                prev = next;
            }
        }
        // A stand of pines out beyond the verge: the sim's (note 432), so what's seen is what's walked into.
        if (Sim.Run.LinesideProps.Of(route, p.Line) is { } lineside)
            foreach (var tree in lineside.BranchTrees(branch, a, b))
            {
                var t = local.Sample(tree.Along);
                var w = (t.Position + Double3.Cross(t.Tangent, Double3.Up).Normalized * tree.Lateral) with { Y = tree.Ground };
                var piece = Piece($"pine-{tree.Variant}", () => WorldKit.Pine(_look, tree.Variant, 12));
                var m = Matrix4x4.CreateScale((float)(tree.Height / 12)) * Matrix4x4.CreateRotationY((float)tree.Yaw) * Matrix4x4.CreateTranslation(w.RelativeTo(origin));
                built.Instances.Add(new MeshInstance(piece, m));
            }
        return new Cell(MeshAsset.From($"branch-{branch}-{index}", built), [.. built.Instances.Select(x => (x.Asset, x.Model))], [.. built.PointLights], origin);
    }

    // ------------------------------------------------------------------ boards

    void Signs(MeshBuilder mesh, PlanScene p, Double3 eye, float drawDistance)
    {
        var rules = p.Plan.Rules;
        foreach (var sign in p.Plan.Signage)
        {
            if (sign.State == SignState.Missing)
                continue;
            var line = p.EdgeLine(sign.Edge);
            var t = line.Sample(Math.Clamp(sign.S, 0, line.Length));
            if ((t.Position - eye).Length > Math.Min(drawDistance, 300))
                continue;
            var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
            if (sign.Type == "deadSignal")
            {
                var foot = t.Position + right * (sign.Side * (rules.SignOffsetM + 0.6)) - Double3.Up * 0.25;
                mesh.Instances.Add(new MeshInstance(Piece("signal-False", () => WorldKit.Signal(_look, false)), Basis(t.Tangent, foot, eye, MathF.PI)));
                continue;
            }
            var board = rules.Boards.GetValueOrDefault(sign.Type);
            float width = (float)(board?.WidthM ?? 0.8), height = (float)(board?.HeightM ?? 2.2);
            var piece = Piece($"sign-{sign.Type}-{sign.Text}", () => SignKit.Board(_look, sign.Type, sign.Text, width, height));
            bool plate = sign.Type == "tunnelPlate";
            var at = plate ? t.Position : t.Position + right * (sign.Side * rules.SignOffsetM) - Double3.Up * 0.25;
            // Turned a little toward the track, so the lamp catches it square; a tunnel's plate square on its portal's face, the
            // far portal's turned round as its portal is (WorldFeatures.Tunnel).
            var m = Basis(t.Tangent, at, eye, plate ? sign.Side > 0 ? 0 : MathF.PI : -sign.Side * 0.18f);
            if (sign.State == SignState.Fallen)
                m = Matrix4x4.CreateRotationZ(sign.Side * 1.35f) * Matrix4x4.CreateTranslation(0, 0.15f, 0) * m;
            mesh.Instances.Add(new MeshInstance(piece, m));
        }
    }

    // ------------------------------------------------------------------ washouts and brass

    void Hazards(MeshBuilder mesh, PlanScene p, Double3 eye, float drawDistance, double time)
    {
        foreach (var st in p.Plan.Structures)
        {
            if (st.Type is not (StructureType.Washout or StructureType.BrassField))
                continue;
            var line = p.EdgeLine(st.Edge);
            var mid = line.Sample(Math.Clamp((st.S0 + st.S1) / 2, 0, line.Length));
            if ((mid.Position - eye).Length > drawDistance)
                continue;
            if (st.Type == StructureType.Washout)
                Washout(mesh, line, st, eye);
            else
                Brass(mesh, line, st, eye, time);
        }
    }

    // ------------------------------------------------------------------ causeways and retaining walls

    /// <summary>
    /// What holds the line up where the land won't (§12.3): a causeway across a marsh, the bed raised on a bank of stone
    /// pitched down either side into the standing water, a row of old timber piles along its toe where the bank was
    /// first held, rubble slumped off it here and there; and a retaining wall on a ledge, a battered masonry face holding
    /// the up side's cut back off the track, buttressed every ten metres, coped, its weep holes stained where the water
    /// comes through. Laid along the line itself, so they follow its curves.
    /// </summary>
    void Banks(MeshBuilder mesh, PlanScene p, Route route, Double3 eye, float drawDistance)
    {
        Kit? k = null;
        foreach (var st in p.Plan.Structures)
        {
            if (st.Type is not (StructureType.Causeway or StructureType.RetainingWall))
                continue;
            var line = p.EdgeLine(st.Edge);
            double from = Math.Max(st.S0, 0), to = Math.Min(st.S1, line.Length);
            double hint = (from + to) / 2;
            double near = Math.Clamp(line.Nearest(eye, ref hint).Distance, from, to);
            if ((line.Sample(near).Position - eye).Length > drawDistance)
                continue;
            k ??= new Kit(_look, mesh) { SurfaceOrigin = new Vector3(W(eye.X), W(eye.Y), W(eye.Z)), Baked = 0 };
            double a = Math.Max(from, near - drawDistance), b = Math.Min(to, near + drawDistance);
            if (st.Type == StructureType.Causeway)
            {
                // The marsh's standing water over the same stretch (§12.4); the bank's height under the bed without.
                double water = p.Plan.Water.FirstOrDefault(w => w.Edge == st.Edge && w.S0 <= st.S1 && w.S1 >= st.S0 && w.Type.EndsWith("arsh", StringComparison.OrdinalIgnoreCase))?.LevelM
                    ?? line.Sample(near).Position.Y - Math.Max(1, st.HeightM);
                Causeway(k, line, route, st.Edge == "main", a, b, water, eye);
            }
            else
                RetainingWall(k, line, st, a, b, eye);
        }
    }

    static Vector3 F(Double3 d) => new((float)d.X, (float)d.Y, (float)d.Z);

    // How far off the line the spruce is modelled (WorldKit.Spruce), not crossed cards: as WorldArt's NearTrees for its pines.
    const double NearSpruce = 40;

    /// <summary>How far out from the line the forest floor's pieces start (m): past the walk beside the train.</summary>
    const double FloorNear = 7;

    /// <summary>
    /// A tree's form and lean (note 395), from a stream of its own off its art seed (LinesideProp.Seed): mostly plain; on the
    /// barrens and the coast often flagged by the wind; now and then broken-topped or forked; one in six leant 4–10°.
    /// </summary>
    static (NovaKit.TreeForm Form, (float Angle, float Toward) Lean) Form(uint seed, Sim.LineGen.BiomeDef? biome)
    {
        var rng = new Random(unchecked((int)(seed * 2654435761u) ^ 0x5f3759df));
        bool windy = biome is { Verge: "barrens" } || biome?.Shore > 0.2;
        double pick = rng.NextDouble();
        var form = pick < (windy ? 0.4 : 0.07) ? NovaKit.TreeForm.Flagged
            : pick < (windy ? 0.52 : 0.2) ? NovaKit.TreeForm.Broken
            : pick < (windy ? 0.58 : 0.28) ? NovaKit.TreeForm.Forked
            : NovaKit.TreeForm.Plain;
        float lean = rng.NextDouble() < 1 / 6.0 ? (float)(4 + 6 * rng.NextDouble()) * MathF.PI / 180 : 0;
        return (form, (lean, (float)(rng.NextDouble() * MathF.Tau)));
    }
    // The causeway's bank, along: a stretch at a time.
    const double BankStep = 5;
    // (WorldArt's ground laterals from the bed's shoulder out: the pitching lies on the same facets the land's mesh has.)
    static readonly double[] BankStations = [2.35, 2.95, 3.7, 5.5, 8, 12, 17, 24, 33];

    void Causeway(Kit k, RailLine line, Route route, bool main, double from, double to, double water, Double3 eye)
    {
        for (double s = Math.Floor(from / BankStep) * BankStep; s < to; s += BankStep)
        {
            double s0 = Math.Max(s, from), s1 = Math.Min(s + BankStep, to);
            if (s1 - s0 < 0.05)
                continue;
            var t0 = line.Sample(s0);
            var t1 = line.Sample(s1);
            var r0 = Double3.Cross(t0.Tangent, Double3.Up).Normalized;
            var r1 = Double3.Cross(t1.Tangent, Double3.Up).Normalized;
            foreach (int side in new[] { -1, 1 })
            {
                Vector3 At(TrackSample t, Double3 r, double lateral, double y) => (t.Position with { Y = y } + r * (side * lateral)).RelativeTo(eye);
                // The bank as the land's mesh has it (WorldArt.Ground at its own laterals, straight between; a branch's
                // from the sim's ground): where it meets the water, interpolated between the stations either side of it.
                double Land(TrackSample t, double along, double lateral) => main
                    ? t.Position.Y + Ground(route, along, (float)(side * lateral), 0)
                    : line.Conditions is { } c ? c.Ground(t.Position + Double3.Cross(t.Tangent, Double3.Up).Normalized * (side * lateral)) : t.Position.Y;
                double Edge(TrackSample t, double along)
                {
                    double la = BankStations[0], ga = Land(t, along, la);
                    foreach (double lb in BankStations.AsSpan(1))
                    {
                        double gb = Land(t, along, lb);
                        if (gb <= water)
                            return ga <= water ? la : la + (lb - la) * (ga - water) / (ga - gb);
                        (la, ga) = (lb, gb);
                    }
                    return la;
                }
                // The pitching: big set stones down the slope, laid on it (the land's stations, so on it and not under
                // it), from the shoulder to the station past the waterline.
                // (Dark with the wet and the marsh's slime, more so toward the water.)
                k.Use("stone_block", Palette.Charcoal, 0.85f, 0.3f, tile: 0.9f);
                k.Tint = new Vector3(0.55f, 0.56f, 0.5f);
                double reach = Math.Max(Edge(t0, s0), Edge(t1, s1));
                for (int i = 0; i + 1 < BankStations.Length && BankStations[i] < reach; i++)
                {
                    double la = BankStations[i], lb = BankStations[i + 1];
                    Vector3 Laid(TrackSample t, Double3 r, double along, double l) => At(t, r, l, Math.Max(Land(t, along, l), water - 0.4) + 0.04);
                    var q0 = Laid(t0, r0, s0, la);
                    var q1 = Laid(t0, r0, s0, lb);
                    var q2 = Laid(t1, r1, s1, lb);
                    var q3 = Laid(t1, r1, s1, la);
                    float u0 = (float)s0, u1 = (float)s1;
                    k.Quad(q0, q1, q2, q3, new(u0, (float)la), new(u0, (float)lb), new(u1, (float)lb), new(u1, (float)la), twoSided: true);
                }
                k.Tint = Vector3.One;
                // The toe's timber piles, a row of old black stumps standing out of the water, leaning as they've gone.
                k.Use("wood_sleeper", Palette.DeepBrown, 0.95f, 0, tile: 1);
                for (double ps = s0 - s0 % 1.4 + 1.4; ps < s1; ps += 1.4)
                {
                    uint h = (uint)((int)(ps * 7.1) * 2654435761u ^ (uint)(side + 3) * 40503u);
                    if (h % 5 == 0)
                        continue;
                    var tp = line.Sample(ps);
                    var rp = Double3.Cross(tp.Tangent, Double3.Up).Normalized;
                    double lean = ((h >> 6) % 7 - 3) * 0.05, tall = 0.25 + (h >> 9) % 4 * 0.12;
                    double edge = Edge(tp, ps) + 0.5;
                    var low = At(tp, rp, edge, water - 0.8);
                    var high = At(tp, rp, edge + lean, water + tall);
                    k.Rod(low, high, 0.09f + (h >> 12) % 3 * 0.015f, 6);
                }
                // Rubble slumped off the bank into the water.
                k.Use("stone_block", Palette.Charcoal, 0.85f, 0.1f, tile: 0.8f);
                uint g = (uint)((int)s0 * 2246822519u ^ (uint)(side + 5) * 3266489917u);
                if (g % 3 == 0)
                {
                    var tp = line.Sample((s0 + s1) / 2);
                    var rp = Double3.Cross(tp.Tangent, Double3.Up).Normalized;
                    var c = At(tp, rp, Edge(tp, (s0 + s1) / 2) + 0.9 + (g >> 5) % 3 * 0.4, water - 0.15);
                    float rad = 0.3f + (g >> 8) % 4 * 0.12f;
                    k.Box(c - new Vector3(rad, rad * 0.7f, rad * 0.8f), c + new Vector3(rad, rad * 0.5f, rad * 0.8f));
                }
            }
        }
    }

    void RetainingWall(Kit k, RailLine line, PlanStructure st, double from, double to, Double3 eye)
    {
        int side = st.Side == 0 ? 1 : st.Side;
        double high = Math.Max(1.5, st.HeightM);
        const double face = 3.4, batter = 0.12, step = 5;
        for (double s = Math.Floor(from / step) * step; s < to; s += step)
        {
            double s0 = Math.Max(s, from), s1 = Math.Min(s + step, to);
            if (s1 - s0 < 0.05)
                continue;
            var t0 = line.Sample(s0);
            var t1 = line.Sample(s1);
            var r0 = Double3.Cross(t0.Tangent, Double3.Up).Normalized;
            var r1 = Double3.Cross(t1.Tangent, Double3.Up).Normalized;
            Vector3 At(TrackSample t, Double3 r, double lateral, double up) => (t.Position + r * (side * lateral) + Double3.Up * up).RelativeTo(eye);
            k.Use("stone_block", Palette.Charcoal, 0.75f, 0.1f, tile: 2.2f);
            // The face, leaning back into the hill as it rises; from a little below the formation up to its coping.
            var a0 = At(t0, r0, face, -0.6);
            var a1 = At(t1, r1, face, -0.6);
            var b0 = At(t0, r0, face + high * batter, high);
            var b1 = At(t1, r1, face + high * batter, high);
            float u0 = (float)s0, u1 = (float)s1, h = (float)(high + 0.6);
            if (side > 0)
                k.Quad(a1, b1, b0, a0, new(u1, h), new(u1, 0), new(u0, 0), new(u0, h));
            else
                k.Quad(a0, b0, b1, a1, new(u0, h), new(u0, 0), new(u1, 0), new(u1, h));
            // The coping: a course of dressed stone along its top, proud of the face.
            k.Shade(1.15f);
            var c0 = At(t0, r0, face + high * batter - 0.12, high);
            var c1 = At(t1, r1, face + high * batter - 0.12, high);
            var d0 = At(t0, r0, face + high * batter + 0.5, high + 0.3);
            var d1 = At(t1, r1, face + high * batter + 0.5, high + 0.3);
            var e0 = At(t0, r0, face + high * batter - 0.12, high + 0.3);
            var e1 = At(t1, r1, face + high * batter - 0.12, high + 0.3);
            if (side > 0)
            {
                k.Quad(c1, e1, e0, c0);
                k.Quad(e1, d1, d0, e0);
            }
            else
            {
                k.Quad(c0, e0, e1, c1);
                k.Quad(e0, d0, d1, e1);
            }
            k.Shade(1 / 1.15f);
            // A buttress every ten metres, stepped back up the face.
            if (Math.Abs(s0 % 10) < 0.01)
            {
                var foot = At(t0, r0, face - 0.55, -0.6);
                var top = At(t0, r0, face + high * batter * 0.6 - 0.1, high * 0.75);
                k.Rod(foot, top, 0.42f);
            }
            // Its weep holes, a row of dark slots low on the face, the stone stained down from each.
            k.Use("paint_black", Palette.SootBlack, 0.9f, 0, tile: 1);
            for (double ws = s0 - s0 % 2.5 + 1.25; ws < s1; ws += 2.5)
            {
                var tw = line.Sample(ws);
                var rw = Double3.Cross(tw.Tangent, Double3.Up).Normalized;
                var w = At(tw, rw, face + 0.6 * batter - 0.02, 0.6);
                var along = F(tw.Tangent) * 0.08f;
                var up = Vector3.UnitY * 0.06f;
                if (side > 0)
                    k.Quad(w - along + up, w + along + up, w + along - up, w - along - up);
                else
                    k.Quad(w + along + up, w - along + up, w - along - up, w + along - up);
            }
        }
    }

    /// <summary>A washout: the rails left hanging over the gully, bent down into it, and the bed's stones strewn below.</summary>
    void Washout(MeshBuilder mesh, RailLine line, PlanStructure st, Double3 eye)
    {
        var k = new Kit(_look, mesh) { SurfaceOrigin = new Vector3(W(eye.X), W(eye.Y), W(eye.Z)), Baked = 0 };
        k.Use("rail_steel", Palette.IronGrey, 0.6f, 0.5f, tile: 1);
        double span = st.S1 - st.S0;
        foreach (int side in new[] { -1, 1 })
        {
            // Each rail droops from where the bed gave out, sagging toward the middle of the gap.
            const int n = 8;
            for (int i = 0; i < n; i++)
            {
                double s0 = st.S0 + span * i / n, s1 = st.S0 + span * (i + 1) / n;
                float sag0 = 1.6f * MathF.Sin(MathF.PI * i / n), sag1 = 1.6f * MathF.Sin(MathF.PI * (i + 1) / n);
                var a = line.Sample(s0);
                var b = line.Sample(s1);
                var r = Double3.Cross(a.Tangent, Double3.Up).Normalized;
                var pa = a.Position + r * (side * TrainKit.HalfGauge) + Double3.Up * (0.16 - sag0);
                var pb = b.Position + r * (side * TrainKit.HalfGauge) + Double3.Up * (0.16 - sag1);
                k.Rod(pa.RelativeTo(eye), pb.RelativeTo(eye), 0.035f);
            }
        }
        // Ballast and broken sleepers in the gully.
        var rng = new Random(st.Id.Aggregate(17, (h, c) => h * 31 + c));
        for (int i = 0; i < 14; i++)
        {
            double s = st.S0 - 4 + rng.NextDouble() * (span + 8);
            var t = line.Sample(Math.Clamp(s, 0, line.Length));
            var r = Double3.Cross(t.Tangent, Double3.Up).Normalized;
            double lateral = (rng.NextDouble() - 0.5) * 7;
            var at = t.Position + r * lateral - Double3.Up * (s > st.S0 && s < st.S1 ? 2.1 : 0.4);
            int v = rng.Next(3);
            mesh.Instances.Add(new MeshInstance(Piece($"rock-{v}", () => WorldKit.Rock(_look, v, 1)), Basis(t.Tangent, at, eye, (float)rng.NextDouble() * 6.28f)));
        }
    }

    /// <summary>
    /// A brass field (GDD §22: "cut through slowly or ram it and pay"): the corruption's crystal grown up through the
    /// ballast and over the rails, thick in the middle of it, a faint glow in the lamp.
    /// </summary>
    void Brass(MeshBuilder mesh, RailLine line, PlanStructure st, Double3 eye, double time)
    {
        var rng = new Random(st.Id.Aggregate(29, (h, c) => h * 31 + c));
        double span = st.S1 - st.S0;
        int count = (int)(span * 3);
        for (int i = 0; i < count; i++)
        {
            double s = st.S0 - 10 + rng.NextDouble() * (span + 20);
            double into = Math.Min(s - st.S0, st.S1 - s);
            if (into < 0 && rng.NextDouble() > 0.3)
                continue;
            var t = line.Sample(Math.Clamp(s, 0, line.Length));
            var r = Double3.Cross(t.Tangent, Double3.Up).Normalized;
            double lateral = (rng.NextDouble() - 0.5) * (into > 0 ? 9 : 14);
            var at = t.Position + r * lateral;
            int v = rng.Next(3);
            float scale = 0.5f + (float)rng.NextDouble() * (into > 5 ? 1.4f : 0.6f);
            mesh.Instances.Add(new MeshInstance(Piece($"brass-{v}", () => BrassCluster(_look, v)),
                Matrix4x4.CreateScale(scale) * Basis(t.Tangent, at, eye, (float)rng.NextDouble() * 6.28f)));
        }
    }

    /// <summary>A cluster of brass crystal: faceted spikes leaning out of one root, and brass weeds round its foot.</summary>
    static MeshAsset BrassCluster(Look? look, int variant, float glow = 0.05f)
    {
        var k = new Kit(look, 1500 + variant);
        k.Use("mineral_growth", Palette.TarnishedBrass, 0.2f, 0.7f, tile: 0.8f);
        k.Emissive = glow;
        var rng = new Random(variant * 977 + 3);
        int spikes = 4 + variant * 2;
        for (int i = 0; i < spikes; i++)
        {
            float yaw = (float)(rng.NextDouble() * MathF.Tau), lean = 0.2f + (float)rng.NextDouble() * 0.6f;
            float length = 0.4f + (float)rng.NextDouble() * 0.9f;
            var dir = new Vector3(MathF.Sin(yaw) * lean, 1, MathF.Cos(yaw) * lean);
            dir = Vector3.Normalize(dir);
            k.Cylinder(new Vector3(0, -0.1f, 0), dir * length, 0.07f + (float)rng.NextDouble() * 0.05f, 5, caps: false, smooth: false, radiusB: 0.005f);
        }
        k.Emissive = 0;
        k.Append(WorldKit.Tuft(look, variant, true), Matrix4x4.Identity);
        return k.Build($"brass-{variant}");
    }

    // ------------------------------------------------------------------ water (WorldArt.Water.cs)

    /// <summary>Tar's wear: the band below 0.015 scene.frag reads as pitch, which no frost rimes.</summary>
    const float PitchWear = 0.01f;

    /// <summary>On a tar pond's 10 m of shore: its oil film and bubbles, seeded by where they lie so they stay put.</summary>
    static void TarFilm(Kit k, TrackSample a, Double3 right, int side, double level, double s, Double3 eye)
    {
        var rng = new Random((int)(s * 7.31) ^ (side * 7919));
        var tint = k.Tint;
        for (int i = 0; i < 2; i++)
        {
            double along = rng.NextDouble() * 10, lat = 6 + rng.NextDouble() * 60;
            TarSpot(k, new Double3(a.Position.X, level, a.Position.Z) + a.Tangent * along + right * (side * lat), rng, eye);
        }
        k.Tint = tint;
    }

    /// <summary>A spot on tar: a skin of oil film in bruise colours, violet into bottle green, and a few low blisters of
    /// pitch round it, black and wet.</summary>
    static void TarSpot(Kit k, Double3 c, Random rng, Double3 eye)
    {
        double rx = 3 + rng.NextDouble() * 6, rz = 1.5 + rng.NextDouble() * 4, turn = rng.NextDouble() * Math.Tau;
        var tint = k.Tint;
        // The film catches what light there is: a faint glint of its own so it reads on black pitch at night.
        k.Tint = Vector3.Lerp(new Vector3(0.5f, 0.25f, 0.7f), new Vector3(0.2f, 0.6f, 0.4f), (float)rng.NextDouble()) * 0.9f;
        k.Emissive = 0.05f;
        Vector3 E(double t) => new Double3(c.X + Math.Cos(t + turn) * rx * Math.Cos(turn) - Math.Sin(t + turn) * rz * Math.Sin(turn),
            c.Y + 0.02, c.Z + Math.Cos(t + turn) * rx * Math.Sin(turn) + Math.Sin(t + turn) * rz * Math.Cos(turn)).RelativeTo(eye);
        var o = (c + new Double3(0, 0.02, 0)).RelativeTo(eye);
        const int n = 8;
        for (int j = 0; j < n; j++)
        {
            var p0 = E(j * Math.Tau / n);
            var p1 = E((j + 1) * Math.Tau / n);
            k.Quad(o, p0, p1, o, new Vector2(o.X, o.Z), new Vector2(p0.X, p0.Z), new Vector2(p1.X, p1.Z), new Vector2(o.X, o.Z), twoSided: true);
        }
        k.Emissive = 0;
        k.Tint = tint * 0.6f;
        for (int i = 0; i < 5; i++)
        {
            double a = rng.NextDouble() * Math.Tau, d = rng.NextDouble() * (rx + 1);
            float r = 0.25f + (float)rng.NextDouble() * 0.5f;
            var b = (c + new Double3(Math.Cos(a) * d, 0, Math.Sin(a) * d)).RelativeTo(eye);
            k.Cylinder(b, b + new Vector3(0, r * 0.5f, 0), r, 7, caps: true, radiusB: r * 0.55f);
        }
        k.Tint = tint;
    }

    // Far land (note 138): past the corridor's ground (WorldArt.PlanLateral's 300 m), low hills on to the horizon, and
    // across a bay or a tidal river its far shore, so the water has a far side and the sky's band of distant highland
    // stands on land, not on haze. Laterals out from the line, and the hills' heights over the plan's own land there (m:
    // the first tucked under the corridor ground's edge); the far shore's distance for each kind of water (null: the
    // open sea, no far side), and its heights over the water. The first is well under the corridor's edge, 3 m: the far
    // land's rows are straight between FarStep's, and in a hollow along the line that chord rode over the corridor's own
    // ground at its edge, open under it to the sky (`dt holes`, note 433: frontier:3 at 12.2 km).
    static readonly double[] FarLateral = [292, 340, 420, 560, 800, 1150, 1700];
    static readonly double[] FarRise = [-3, 3, 8, 16, 26, 38, 50];
    /// <summary>The far land's rows along the line (m): 40 left a chord a few metres over a hollow.</summary>
    const double FarStep = 20;
    static double? FarShore(ShoreKind kind) => kind switch { ShoreKind.Fundy => 1100, ShoreKind.Dyke => 520, _ => null };
    static readonly double[] ShoreLateral = [-30, 0, 40, 140, 380, 900];
    static readonly double[] ShoreRise = [-2, 1.2, 4, 14, 32, 46];

    void FarLand(MeshBuilder mesh, PlanScene p, RailLine line, Double3 eye, double centre, float drawDistance)
    {
        var k = new Kit(_look, mesh) { SurfaceOrigin = new Vector3(W(eye.X), W(eye.Y), W(eye.Z)), Baked = 0 };
        k.Use(_look?.Layer("ground_forest") >= 0 ? "ground_forest" : "tar", Palette.MuddyOlive, 0.95f, 0.02f, tile: 40);
        k.Tint = new Vector3(0.55f, 0.55f, 0.5f);
        var terrain = p.Terrain;
        double corridor = p.Plan.Rules.Terrain.CorridorM, taper = p.Plan.Rules.Terrain.Shore.TaperM;
        const double step = FarStep, reach = 900;
        double from = Math.Max(0, centre - drawDistance - reach), to = Math.Min(line.Length, centre + drawDistance + reach);
        // The shore (not a river: that has its banks in the corridor) on a side at s, if any.
        PlanShore? ShoreAt(double s, int side) => p.Plan.Shores.FirstOrDefault(sh => sh.Kind != ShoreKind.River && sh.Side == side
            && p.EdgeLine(sh.Edge) == line && s >= sh.S0 - taper && s <= sh.S1 + taper);
        // A point of the far land: on the hills' profile, but never over another corridor's own ground (a branch out there
        // keeps its land, and its track on it), where it tucks under that land instead.
        Vector3 Point(TrackSample t, Double3 r, int side, double lateral, double? level, double rise)
        {
            var at = t.Position + r * (side * lateral);
            double h = (level ?? terrain.Height(at.X, at.Z)) + rise;
            var near = terrain.Nearby(at.X, at.Z, corridor + 40);
            if (near.Any(n => Math.Abs(n.Lateral) < corridor + 30))
                h = Math.Min(h, terrain.Height(at.X, at.Z) - 2);
            return new Double3(at.X, h, at.Z).RelativeTo(eye);
        }
        for (double s = from; s < to; s += step)
        {
            double s1 = Math.Min(s + step, to);
            var a = line.Sample(s);
            var b = line.Sample(s1);
            var ra = Double3.Cross(a.Tangent, Double3.Up).Normalized;
            var rb = Double3.Cross(b.Tangent, Double3.Up).Normalized;
            foreach (int side in new[] { -1, 1 })
            {
                // How high the hills run here: slowly up and down along the line, each side its own.
                float Hills(double at) => 0.55f + 0.9f * Noise((float)(at * 0.0011) + side * 31.7f, side * 5.3f);
                double[] lats = FarLateral, rises = FarRise;
                double? levelA = null, levelB = null;
                if (ShoreAt(s, side) is { } sh)
                {
                    if (FarShore(sh.Kind) is not { } far)
                        continue;
                    // Across the water: its far shore, its heights over the water's level, not the rail's.
                    lats = [.. ShoreLateral.Select(l => far + l)];
                    rises = ShoreRise;
                    levelA = levelB = sh.LevelM;
                }
                for (int c = 0; c + 1 < lats.Length; c++)
                {
                    // Over the land (or the water), the hills coming and going along the line; a far shore's beach level.
                    double RiseAt(int i, double at, double? level) => level is not null && i < 2 ? rises[i] : rises[i] * Hills(at);
                    var q0 = Point(a, ra, side, lats[c], levelA, RiseAt(c, s, levelA));
                    var q1 = Point(a, ra, side, lats[c + 1], levelA, RiseAt(c + 1, s, levelA));
                    var q2 = Point(b, rb, side, lats[c + 1], levelB, RiseAt(c + 1, s1, levelB));
                    var q3 = Point(b, rb, side, lats[c], levelB, RiseAt(c, s1, levelB));
                    k.Quad(q0, q1, q2, q3, new Vector2(q0.X, q0.Z), new Vector2(q1.X, q1.Z), new Vector2(q2.X, q2.Z), new Vector2(q3.X, q3.Z), twoSided: true);
                }
            }
        }
    }

    // ------------------------------------------------------------------ halts and towns

    /// <summary>
    /// The plan's places (§11.3): a halt's platform, dark now, its name board the plan's; a dead town's houses round the
    /// line where it runs through, a church or a windmill among them. Nothing lit: nobody's there. Where the stop generator
    /// laid a village over one (PlanStops), the village is drawn instead (WorldArt.Stops), its halt at the platform.
    /// </summary>
    void Places(MeshBuilder mesh, PlanScene p, Double3 eye, float drawDistance)
    {
        foreach (var st in p.Plan.Structures.Where(s => s.Type == StructureType.Platform && !p.InVillage(s.Edge, s.S0, s.S1)))
        {
            var line = p.EdgeLine(st.Edge);
            for (double s = Math.Floor(st.S0 / 8) * 8; s < st.S1; s += 8)
            {
                var t = line.Sample(Math.Clamp(s, 0, line.Length));
                if ((t.Position - eye).Length > Math.Min(drawDistance, 260))
                    continue;
                int v = (int)(s / 8) % 3;
                float yaw = st.Side < 0 ? MathF.PI : 0;
                mesh.Instances.Add(new MeshInstance(Piece($"platform-{v}", () => StructureKit.PlatformBay(_look, v)), Basis(t.Tangent, t.Position, eye, yaw)));
            }
        }
        foreach (var town in p.Plan.Landmarks.Where(l => l.Type is "town" or "halt" && !p.InVillage(l.Edge, l.S0, l.S1)))
        {
            var line = p.EdgeLine(town.Edge);
            double mid = (town.S0 + town.S1) / 2;
            if ((line.Sample(Math.Clamp(mid, 0, line.Length)).Position - eye).Length > drawDistance + 200)
                continue;
            var rng = new Random(town.Name.Aggregate(41, (h, c) => h * 31 + c));
            int houses = town.Type == "town" ? 10 + rng.Next(8) : 1 + rng.Next(3);
            for (int i = 0; i < houses; i++)
            {
                double s = town.S0 + rng.NextDouble() * (town.S1 - town.S0);
                int side = rng.Next(2) == 0 ? -1 : 1;
                double lateral = side * (16 + rng.NextDouble() * (town.Type == "town" ? 50 : 20));
                int v = rng.Next(9);
                Place(mesh, p, line, Piece($"house-{v}", () => TownKit.House(_look, v)), s, lateral, (float)(rng.NextDouble() - 0.5) * 0.8f, eye);
            }
            if (town.Type == "town")
                Place(mesh, p, line, rng.Next(2) == 0 ? Piece("church", () => TownKit.Church(_look)) : Piece("windmill", () => TownKit.Windmill(_look)),
                    mid, (rng.Next(2) == 0 ? -1 : 1) * 45, (float)rng.NextDouble(), eye);
            if (town.Type == "town")
                DeadTown(mesh, p, line, town.Name, mid, eye);
        }
    }

    /// <summary>
    /// A dead town's rooms open to the line (the checklist's dead towns: "readable from a moving train"), as a hand-laid
    /// route's villages have them (WorldArt.Settlements): a house nearest the line with its front wall gone and the child's
    /// room in it, the bedside lamp on; further along the parlour laid out for a wake; across the line the photographer's,
    /// the dead boy propped for his portrait; the churchyard's cadaver saint and tombs, a defaced statue in the square.
    /// Hashed on the town's name so every machine dresses it alike.
    /// </summary>
    void DeadTown(MeshBuilder mesh, PlanScene p, RailLine line, string name, double mid, Double3 eye)
    {
        float h = name.Aggregate(17, (a, c) => a * 37 + c) * 0.0001f;
        int side = Hash(h * 1.3f) < 0.5f ? -1 : 1;
        void Room(string prop, double along, double across, float light, float range, Vector3 colour)
        {
            if (_props.Get(prop) is not { } room)
                return;
            var t = line.Sample(Math.Clamp(along, 0, line.Length));
            var at = t.Position + Double3.Cross(t.Tangent, Double3.Up).Normalized * across;
            if (p.Terrain.WaterAt(at.X, at.Z) is not null)
                return;
            at = at with { Y = p.Terrain.Height(at.X, at.Z) - 0.1 };
            var m = Basis(t.Tangent, at, eye, across > 0 ? MathF.PI / 2 : -MathF.PI / 2);
            mesh.Instances.Add(new MeshInstance(room, m));
            if (_props.Socket(prop, "lamp") is { } lamp)
                mesh.PointLights.Add(new PointLight(Vector3.Transform(lamp, m), colour * light, range));
        }
        Room("boy_room", mid - 40, side * (15 + Hash(h * 2.1f) * 4), 1.6f, 6.5f, new Vector3(1.0f, 0.72f, 0.42f));
        if (Hash(h * 3.7f) < 0.7f)
            Room("wake_room", mid + 42, side * (16 + Hash(h * 4.3f) * 3), 0.9f, 4.5f, new Vector3(1.0f, 0.74f, 0.46f));
        if (Hash(h * 5.9f) < 0.6f)
            Room("portrait_room", mid - 4, -side * (17 + Hash(h * 6.1f) * 3), 0.9f, 4.0f, new Vector3(1.0f, 0.7f, 0.4f));
        // The churchyard by the line: the cadaver saint at its gate, the tombs between it and the rails.
        if (_props.Get("transi") is { } saint)
            Place(mesh, p, line, saint, mid + 20, side * 30, 0, eye);
        if (_props.Get("effigy") is { } tomb)
            for (int i = 0; i < 3; i++)
                Place(mesh, p, line, tomb, mid + 8 + i * 9, side * (19 + Hash(h * (7 + i)) * 5), MathF.PI / 2, eye);
        if (_props.Get("mercury_defaced") is { } square)
            Place(mesh, p, line, square, mid, -side * 26, Hash(h * 9.1f) - 0.5f, eye);
    }

    void Place(MeshBuilder mesh, PlanScene p, RailLine line, MeshAsset piece, double s, double lateral, float yaw, Double3 eye)
    {
        var t = line.Sample(Math.Clamp(s, 0, line.Length));
        var at = t.Position + Double3.Cross(t.Tangent, Double3.Up).Normalized * lateral;
        if (p.Terrain.WaterAt(at.X, at.Z) is not null)
            return;
        at = at with { Y = p.Terrain.Height(at.X, at.Z) - 0.2 };
        float face = lateral > 0 ? MathF.PI / 2 : -MathF.PI / 2;
        mesh.Instances.Add(new MeshInstance(piece, Basis(t.Tangent, at, eye, face + yaw)));
    }

    // ------------------------------------------------------------------ the terminus's glow

    /// <summary>
    /// §10.3: from a few km out, the terminus's lights glow on the fog's underside ahead (a silent town's don't), the
    /// first sign of the end of the night.
    /// </summary>
    void Glow(MeshBuilder mesh, PlanScene p, RailLine line, Double3 eye, double at)
    {
        var term = p.Plan.Terminus;
        if (term.Silent || at < term.SkyGlowFromM || at > term.GateM)
            return;
        var gate = line.Sample(Math.Min(term.GateM + 200, line.Length)).Position;
        var toward = gate - eye;
        double d = toward.Length;
        if (d < 1)
            return;
        // Drawn at a fixed distance in its direction, high above the horizon: the glow is on the cloud, not the town.
        var dir = toward * (1 / d);
        var o = (eye + dir * 900 + Double3.Up * 160).RelativeTo(eye);
        float strength = (float)Math.Clamp(1 - (d - 300) / (term.GateM - term.SkyGlowFromM + 300), 0.15, 1);
        mesh.Billboard(o, 700, 0, new Vector4(Palette.LampAmber * 0.10f * strength, 1), -1, FxBlend.Additive);
        mesh.Billboard(o - new Vector3(0, 120, 0), 420, 0, new Vector4(Palette.LampAmber * 0.14f * strength, 1), -1, FxBlend.Additive);
    }
}
