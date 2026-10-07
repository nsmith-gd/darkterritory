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
    }

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

    /// <summary>A biome's stand cover (its tree density as a share of the land under forest).</summary>
    static float Cover(BiomeDef def) => def.TreeDensity <= 0 ? 0 : Math.Clamp((float)def.TreeDensity * 0.85f + 0.08f, 0.04f, 0.92f);

    /// <summary><see cref="Stand(RailLine, double, double, float)"/> at a world point.</summary>
    static float Stand(Double3 w, float cover)
    {
        float x = (float)(w.X % 8192), z = (float)(w.Z % 8192);
        float n = Noise(x * 0.0065f + 11.7f, z * 0.0065f - 3.3f) * 0.7f + Noise(x * 0.028f - 7.1f, z * 0.028f + 1.9f) * 0.3f;
        // The noise sits about its middle (two value noises), so the threshold is set there: about cover of the land.
        float edge = 0.5f + (0.5f - cover) * 0.45f;
        return SmoothStep(edge - 0.012f, edge + 0.012f, n);
    }

    /// <summary>The land's tint at a world point: broad light and dark swathes, a little warmer and cooler, over the tiles.</summary>
    static Vector3 Macro(Vector3 world)
    {
        float a = Noise(world.X * 0.004f + 3.1f, world.Z * 0.004f + 8.7f), b = Noise(world.X * 0.021f - 1.3f, world.Z * 0.021f + 5.5f);
        float v = 0.72f + 0.42f * (a * 0.6f + b * 0.4f);
        float warm = (Noise(world.X * 0.007f - 6.6f, world.Z * 0.007f - 2.2f) - 0.5f) * 0.18f;
        return new Vector3(v * (1 + warm), v, v * (1 - warm));
    }

    /// <summary>How steep the land is at one vertex of a row across the line: rise over run to its neighbours.</summary>
    static float SlopeAt(Vector3[] row, int i)
    {
        var a = row[Math.Max(0, i - 1)];
        var b = row[Math.Min(row.Length - 1, i + 1)];
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
        MeshAsset Tree(string kind, int v, bool near) => kind switch
        {
            "fir" => Piece($"fir-{v}", () => NovaKit.Conifer(_look, v, 12, 0.46f)),
            "birch" => Piece($"birch-{v % 3}", () => NovaKit.Birch(_look, v % 3)),
            "pine" => Piece($"pine-{v}", () => WorldKit.Pine(_look, v, 12)),
            "tamarack" => Piece($"tamarack-{v}", () => NovaKit.Conifer(_look, v, 12, 0.26f)),
            _ when near => Piece($"spruce3d-{v}", () => WorldKit.Spruce(_look, v, 12)),
            _ => Piece($"spruce-{v}", () => NovaKit.Conifer(_look, v, 12, 0.3f)),
        };

        for (double s = Math.Ceiling(from / 12) * 12; s < to; s += 12)
        {
            if (!Clear(s) || p.Biome(s) is not { } def)
                continue;
            var rng = new Random(unchecked(seed * 73856093 ^ (int)(s / 12) * 19349663));
            // The forest comes in stands (maritime-rules.md §5, "the spruce wall"): world-space patches with hard
            // edges, as much of the land as the biome's density says, planted solid; out of them only the odd tree.
            // The line runs through them with the alder between: that's the Maritime view, not trees dotted on heath.
            float cover = Cover(def);
            bool dense = def.TreeDensity >= 0.8;
            double near = def.TreeDensity >= 1.2 ? 6 : dense ? 7 : 9;
            int count = def.TreeDensity <= 0 ? 0 : 50;
            double floraTotal = def.Flora.Values.Sum();
            for (int k = 0; k < count; k++)
            {
                double side = rng.Next(2) == 0 ? -1 : 1;
                double offset = side * (near + Math.Pow(rng.NextDouble(), 0.8) * (90 - near));
                double along = s + rng.NextDouble() * 12;
                float standing = Stand(line, along, offset, cover);
                if (rng.NextDouble() > (standing > 0.5f ? 1 : 0.035))
                    continue;
                bool dead = rng.NextDouble() < def.DeadTrees;
                bool corrupted = dead && def.Trees.Contains("corrupted") && rng.NextDouble() < 0.35;
                int variant = rng.Next(4);
                float yaw = (float)rng.NextDouble() * MathF.Tau;
                // Which kind, by the biome's flora weights.
                string kind = "spruce";
                double pick = rng.NextDouble() * floraTotal;
                foreach (var (name, w) in def.Flora)
                    if ((pick -= w) < 0)
                    {
                        kind = name;
                        break;
                    }
                // Stunted on the barrens and the highland (krummholz), tall in the forest.
                float height = def.Verge == "barrens" ? 3 + (float)rng.NextDouble() * 5 : (dead ? 7 : 11) + (float)rng.NextDouble() * (dense ? 11 : 8);
                // White pine stands out over the spruce (maritime-rules.md §5: to 35 m over a 20-30 m canopy), even on the barrens.
                if (kind == "pine" && !dead)
                    height = (def.Verge == "barrens" ? 9 : 20) + (float)rng.NextDouble() * 9;
                if (!Free(along, offset))
                    continue;
                var (m, slope) = Place(along, offset, yaw, height / 12, 0.15f);
                if (slope > 1.1f)
                    continue; // nothing grows on the crag
                MeshAsset piece = dead
                    ? rng.Next(2) == 0 ? Piece($"ghost-{variant}", () => NovaKit.GhostSpruce(_look, variant)) : Piece($"dead-{variant % 2}", () => WorldKit.DeadTree(_look, variant % 2, 10))
                    : Tree(kind, variant, Math.Abs(offset) < NearSpruce);
                if (dead)
                    (m, _) = Place(along, offset, yaw, height / (piece.Name.StartsWith("ghost") ? 8 + variant * 2.5f : 10) * 0.9f, 0.15f);
                if (kind == "birch" && !dead)
                    (m, _) = Place(along, offset, yaw, height / 11, 0.1f);
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
            // The stand's mass behind the single trees: walls of packed spires where a stand runs on out from the
            // line, one at its near edge's depth and one deep in it, so the forest has a body and a serrated top.
            // (On the barrens and the coast the stands are stunted: the wind-cut white spruce of the headlands.)
            bool stunted = def.Verge == "barrens";
            if (def.TreeDensity >= 0.3 && s % 24 < 12)
                foreach (int side in new[] { -1, 1 })
                    foreach (double depth in new[] { 55 + rng.NextDouble() * 30, 125 + rng.NextDouble() * 40 })
                    {
                        double lateral = side * depth, along = s + rng.NextDouble() * 4;
                        if (Stand(line, along, lateral, cover) < 0.5f || Stand(line, along, lateral + side * 20, cover) < 0.5f || !Free(along, lateral))
                            continue;
                        var (m, slope) = Place(along, lateral, (float)(rng.NextDouble() - 0.5) * 0.25f, 1, 0.5f);
                        if (slope > 0.9f)
                            continue;
                        int tv = rng.Next(5);
                        float th = stunted ? 6 + (float)rng.NextDouble() * 4 : 13 + (float)rng.NextDouble() * 6;
                        mesh.Append(Piece($"treeline-{tv}-{th:0}", () => NovaKit.Treeline(_look, tv, 26, th)), m, new Vector3(0.8f + 0.25f * (float)rng.NextDouble()));
                    }
            // Boulders where the land is rough, bigger and more of them the rougher it is, sunk into the slope.
            for (int k = 0; k < (int)(def.Rocks + rng.NextDouble()); k++)
            {
                double offset = (rng.Next(2) == 0 ? -1 : 1) * (6 + rng.NextDouble() * 80);
                double along = s + rng.NextDouble() * 12;
                if (!Free(along, offset))
                    continue;
                int v = rng.Next(3);
                float size = 0.8f + (float)rng.NextDouble() * (float)(1.2 + def.NoiseScale * 1.6);
                var (_, slope) = Place(along, offset, 0, 1, 0);
                size *= slope > 0.6f ? 1.6f : 1;
                var (m, _) = Place(along, offset, (float)rng.NextDouble() * 6.28f, size, size * (0.3f + 1.1f * Math.Min(slope, 1.5f)));
                mesh.Append(Piece($"rock-{v}", () => WorldKit.Rock(_look, v, 1)), m);
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
            // Homesteads and poles, hashed on the road and the stretch so a cell's always the same.
            for (double at = Math.Ceiling(a0 / 45) * 45; at < a1; at += 45)
            {
                double l = Lat(at);
                if (Math.Abs(l) < 10 || !Clear(at))
                    continue;
                int away = Math.Sign(l);
                var rng = new Random(unchecked(road.Id.Aggregate(17, (h, ch) => h * 31 + ch) * 31 ^ (int)(at / 45) * 7919));
                // The road's poles, leaning, on its far side.
                if (Free(at, l + away * (rr.HalfWidthM + 2.5)))
                    mesh.Instances.Add(new MeshInstance(Piece($"pole-{(int)(at / 45) % 3}", () => WorldKit.Pole(_look, (int)(at / 45) % 3)),
                        Place(at, l + away * (rr.HalfWidthM + 2.5), (float)(rng.NextDouble() - 0.5) * 0.3f, 0.85f, 0.2f).M));
                if (rng.NextDouble() < 0.22)
                {
                    // A homestead: the house facing the road, a barn behind now and then, the woodpile, a fence along the front.
                    double back = l + away * (rr.HalfWidthM + 12 + rng.NextDouble() * 10);
                    float face = away > 0 ? -MathF.PI / 2 : MathF.PI / 2;
                    int v = rng.Next(8);
                    if (Free(at, back) && Free(at + 8, back) && Free(at - 8, back))
                    {
                        mesh.Instances.Add(new MeshInstance(Piece($"saltbox-{v}", () => NovaKit.Saltbox(_look, v)), Place(at, back, face + (float)(rng.NextDouble() - 0.5) * 0.2f, 1, 0.3f).M));
                        int wv = rng.Next(3);
                        if (Free(at + 11, back))
                            mesh.Append(Piece($"woodpile-{wv}", () => NovaKit.Woodpile(_look, wv)), Place(at + 11, back - away * 2, 0, 1, 0.05f).M);
                        if (rng.NextDouble() < 0.45 && Free(at - 4, back + away * 22))
                        {
                            int bv = rng.Next(2);
                            mesh.Instances.Add(new MeshInstance(Piece($"barn-{bv}", () => NovaKit.Barn(_look, bv)), Place(at - 4, back + away * 22, face, 1, 0.3f).M));
                        }
                        for (double f = at - 14; f < at + 14; f += 2.4)
                            if (rng.NextDouble() > 0.15 && Free(f, l + away * (rr.HalfWidthM + 4.5)))
                                mesh.Append(Piece("fencepost-0", () => WorldKit.FencePost(_look, 0)), Place(f, l + away * (rr.HalfWidthM + 4.5), (float)(rng.NextDouble() - 0.5) * 0.4f, 1, 0.1f).M);
                    }
                }
                else if (rng.NextDouble() < 0.05)
                {
                    int cv = rng.Next(4);
                    mesh.Instances.Add(new MeshInstance(Piece($"car-{cv}", () => NovaKit.Car(_look, cv)),
                        Place(at, l + away * (rr.HalfWidthM + 0.5), (float)(rng.NextDouble() - 0.5) * 0.6f, 1, 0.12f).M));
                }
            }
        }
        // The shore's own (maritime-rules.md §6): fish sheds on their stilts at the head of a cove, a crib wharf run out
        // from them, and a lighthouse out on a headland; all at the water's edge, wherever it wanders.
        foreach (var sh in p.Plan.Shores.Where(x => x.Kind != ShoreKind.Dyke && x.S1 > from && x.S0 < to))
        {
            for (double s = Math.Max(sh.S0 + 60, Math.Ceiling(from / 20) * 20); s < Math.Min(sh.S1 - 60, to); s += 20)
            {
                if (!Clear(s))
                    continue;
                double e0 = p.Terrain.ShoreEdge(sh, s - 40), e = p.Terrain.ShoreEdge(sh, s), e1 = p.Terrain.ShoreEdge(sh, s + 40);
                var rng = new Random(unchecked(seed * 7919 ^ (int)(s / 20) * 104729 ^ sh.Id.Length));
                float seaward = sh.Side > 0 ? MathF.PI / 2 : -MathF.PI / 2;
                if (e > e0 && e >= e1 && sh.Kind == ShoreKind.Sea && rng.NextDouble() < 0.7)
                {
                    // A cove's head: two or three sheds along the waterline, a wharf out from the first.
                    int sheds = 2 + rng.Next(2);
                    for (int i = 0; i < sheds; i++)
                    {
                        double along = s + (i - sheds / 2.0) * 9, lateral = sh.Side * (p.Terrain.ShoreEdge(sh, along) - 2);
                        if (Free(along, lateral * 0.85))
                            mesh.Instances.Add(new MeshInstance(Piece($"fishshed-{i % 3}", () => NovaKit.FishShed(_look, i % 3)),
                                Place(along, lateral, seaward + (float)(rng.NextDouble() - 0.5) * 0.4f, 1, 0.9f).M));
                    }
                    int wv = rng.Next(2);
                    mesh.Instances.Add(new MeshInstance(Piece($"wharf-{wv}", () => NovaKit.Wharf(_look, wv)),
                        Place(s + 4, sh.Side * (e + 1), seaward + MathF.PI, 1, 0).M));
                }
                else if (e < e0 && e <= e1 && sh.Kind == ShoreKind.Sea && rng.NextDouble() < 0.18)
                {
                    int lv = rng.Next(2);
                    double lateral = sh.Side * Math.Max(12, e - 10);
                    if (Free(s, lateral))
                        mesh.Instances.Add(new MeshInstance(Piece($"lighthouse-{lv}", () => NovaKit.Lighthouse(_look, lv)), Place(s, lateral, seaward, 1, 0.3f).M));
                }
                // The Atlantic's edge (maritime-rules.md §3): granite ledges, broad pale whalebacks the ice smoothed,
                // running down into the water; weed-black rocks at the tide line; and the surf, a broken pale line where
                // the swell breaks on them.
                if (sh.Kind == ShoreKind.Sea)
                {
                    for (int i = 0; i < 2; i++)
                    {
                        double along = s + rng.NextDouble() * 20, lateral = sh.Side * (p.Terrain.ShoreEdge(sh, along) - 3 + rng.NextDouble() * 7);
                        float size = 3 + (float)rng.NextDouble() * 5;
                        int v = rng.Next(3);
                        mesh.Append(Piece($"rock-{v}", () => WorldKit.Rock(_look, v, 1)),
                            Place(along, lateral, (float)rng.NextDouble() * 6.28f, 1, size * 0.2f, new Vector3(size * 1.6f, size * 0.35f, size * 1.1f)).M, new Vector3(1.3f, 1.28f, 1.22f));
                    }
                    for (int i = 0; i < 3; i++)
                    {
                        double along = s + rng.NextDouble() * 20, lateral = sh.Side * (p.Terrain.ShoreEdge(sh, along) + 1 + rng.NextDouble() * 6);
                        int v = rng.Next(3);
                        float size = 0.6f + (float)rng.NextDouble();
                        mesh.Append(Piece($"rock-{v}", () => WorldKit.Rock(_look, v, 1)), Place(along, lateral, (float)rng.NextDouble() * 6.28f, size, size * 0.5f).M, new Vector3(0.28f, 0.25f, 0.18f));
                    }
                    var foam = new Kit(_look, mesh) { SurfaceOrigin = new Vector3(W(eye.X), W(eye.Y), W(eye.Z)), Baked = 0 };
                    foam.Use("plaster_ruin", new Vector3(0.8f), 0.9f, 0.3f, tile: 2);
                    foam.Tint = new Vector3(1.5f, 1.55f, 1.6f);
                    for (double f = s; f < s + 20; f += 2.5)
                    {
                        if (Noise((float)(f * 0.11), sh.Side * 3.1f) < 0.45f)
                            continue;
                        Vector3 Surf(double at, double out_)
                        {
                            var t = line.Sample(Math.Clamp(at, 0, line.Length));
                            var r = Double3.Cross(t.Tangent, Double3.Up).Normalized * sh.Side;
                            return (new Double3(t.Position.X, sh.LevelM + 0.06, t.Position.Z) + r * (p.Terrain.ShoreEdge(sh, at) + out_)).RelativeTo(eye);
                        }
                        var q0 = Surf(f, 0.5);
                        var q1 = Surf(f, 2.2);
                        var q2 = Surf(f + 2.3, 2.4);
                        var q3 = Surf(f + 2.3, 0.4);
                        foam.Quad(q0, q1, q2, q3, new Vector2(q0.X, q0.Z), new Vector2(q1.X, q1.Z), new Vector2(q2.X, q2.Z), new Vector2(q3.X, q3.Z), twoSided: true);
                    }
                }
                // Boulders along the tide line, granite or sandstone; in a river, across its bed (the rapids).
                for (int i = 0; i < (sh.Kind == ShoreKind.River ? 4 : 2); i++)
                {
                    double along = s + rng.NextDouble() * 20;
                    double lateral = sh.Side * (p.Terrain.ShoreEdge(sh, along) + (sh.Kind == ShoreKind.River ? rng.NextDouble() * sh.FlatM : (rng.NextDouble() - 0.6) * 10));
                    int v = rng.Next(3);
                    float size = 0.7f + (float)rng.NextDouble() * 1.6f;
                    mesh.Append(Piece($"rock-{v}", () => WorldKit.Rock(_look, v, 1)), Place(along, lateral, (float)rng.NextDouble() * 6.28f, size, size * 0.4f).M,
                        sh.Kind == ShoreKind.Fundy ? new Vector3(1.2f, 0.75f, 0.6f) : Vector3.One);
                }
            }
        }
        // What people left, and what the ice left: by the block, each piece as the biome has it (biomes.json props).
        const double block = 150;
        for (double b = Math.Floor(from / block) * block; b < to; b += block)
        {
            if (b < from || !Clear(b) || p.Biome(b) is not { } def)
                continue;
            var rng = new Random(unchecked(seed * 486187739 ^ (int)(b / block) * 6700417));
            foreach (var (name, rule) in def.Props.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                for (int n = 0; n < rule.Count; n++)
                {
                    if (rng.NextDouble() > rule.Chance)
                        continue;
                    double lateral = (rng.Next(2) == 0 ? -1 : 1) * (rule.OutM[0] + rng.NextDouble() * (rule.OutM[1] - rule.OutM[0]));
                    double along = b + rng.NextDouble() * block;
                    int v = rng.Next(12);
                    Prop(name, along, lateral, v, rng);
                }
        }

        void Prop(string name, double along, double lateral, int v, Random rng)
        {
            float face = lateral > 0 ? MathF.PI / 2 : -MathF.PI / 2, jitter = (float)(rng.NextDouble() - 0.5);
            void Put(MeshAsset piece, float yaw, float sink = 0.3f, Vector3? stretch = null)
            {
                if (Free(along, lateral) && Free(along, lateral * 0.8) && Free(along + 8, lateral) && Free(along - 8, lateral))
                    mesh.Instances.Add(new MeshInstance(piece, Place(along, lateral, yaw, 1, sink, stretch).M));
            }
            switch (name)
            {
                case "saltbox": Put(Piece($"saltbox-{v % 8}", () => NovaKit.Saltbox(_look, v % 8)), face + jitter * 0.6f); break;
                case "barn": Put(Piece($"barn-{v % 2}", () => NovaKit.Barn(_look, v % 2)), face + jitter * 0.5f); break;
                case "church": Put(Piece("nova-church", () => NovaKit.Church(_look)), face + jitter * 0.2f); break;
                case "buryingGround": Put(Piece($"burying-{v % 3}", () => NovaKit.BuryingGround(_look, v % 3)), face + jitter * 0.3f, 0.1f); break;
                case "fishShed": Put(Piece($"fishshed-{v % 3}", () => NovaKit.FishShed(_look, v % 3)), face + jitter * 0.8f, 0.2f); break;
                case "ruin": Put(Piece($"ruin-{v % 3}", () => SettingKit.RuinWall(_look, v % 3)), face + jitter, 0.2f); break;
                case "chimney": Put(Piece($"chimney-{v % 3}", () => SettingKit.Chimney(_look, v % 3)), jitter); break;
                case "tank": Put(Piece($"tank-{v % 3}", () => SettingKit.Tank(_look, v % 3)), jitter * 6); break;
                case "headframe": Put(Piece("headframe", () => SettingKit.Headframe(_look)), face + jitter * 0.4f); break;
                case "erratic":
                    {
                        // A granite erratic: a house-sized boulder the ice left sitting in the open, paler than the ledge.
                        float size = 2.2f + (float)rng.NextDouble() * 3;
                        if (Free(along, lateral))
                            mesh.Append(Piece($"rock-{v % 3}", () => WorldKit.Rock(_look, v % 3, 1)), Place(along, lateral, jitter * 6, size, size * 0.25f).M, new Vector3(1.35f, 1.35f, 1.3f));
                        break;
                    }
                case "outcrop":
                    {
                        // Granite ledge breaking through the thin soil: a broad, low slab.
                        float size = 2 + (float)rng.NextDouble() * 4;
                        if (Free(along, lateral))
                            mesh.Append(Piece($"rock-{v % 3}", () => WorldKit.Rock(_look, v % 3, 1)),
                                Place(along, lateral, jitter * 6, 1, size * 0.18f, new Vector3(size * 1.8f, size * 0.45f, size * 1.3f)).M, new Vector3(1.2f, 1.2f, 1.15f));
                        break;
                    }
                case "stoneWall":
                    {
                        // A field's wall, run along the line for a stretch, gaps where it's fallen.
                        int lengths = 6 + rng.Next(12);
                        for (int i = 0; i < lengths; i++)
                        {
                            double s = along + i * 8;
                            if (s > line.Length || !Clear(s) || !Free(s, lateral) || rng.NextDouble() < 0.12)
                                continue;
                            mesh.Instances.Add(new MeshInstance(Piece($"stonewall-{i % 3}", () => NovaKit.StoneWall(_look, i % 3)), Place(s + 8, lateral, 0, 1, 0.1f).M));
                        }
                        break;
                    }
                case "oldField":
                    {
                        // Pasture spruce (maritime-rules.md §5): a farm field given up, grown in solid with white spruce all of
                        // an age and a height, a dark block with a hard edge where the field's edge was.
                        float h = 4 + (float)rng.NextDouble() * 4;
                        for (int i = 0; i < 30; i++)
                        {
                            double s = along + (rng.NextDouble() - 0.5) * 36, l = lateral + (rng.NextDouble() - 0.5) * 24;
                            if (!Free(s, l))
                                continue;
                            int tv = rng.Next(4);
                            mesh.Append(Piece($"spruce-{tv}", () => NovaKit.Conifer(_look, tv, 12, 0.3f)),
                                Place(s, l, (float)rng.NextDouble() * 6.28f, h * (0.85f + 0.3f * (float)rng.NextDouble()) / 12, 0.1f).M, new Vector3(0.85f, 0.95f, 0.9f));
                        }
                        break;
                    }
                case "orchard":
                    {
                        // An orchard gone to ruin: rows of small, gnarled dead apple trees.
                        for (int row = 0; row < 4; row++)
                            for (int i = 0; i < 6; i++)
                            {
                                double s = along + i * 6, l = lateral + Math.Sign(lateral) * row * 6;
                                if (!Free(s, l) || rng.NextDouble() < 0.15)
                                    continue;
                                mesh.Append(Piece($"dead-{(i + row) % 2}", () => WorldKit.DeadTree(_look, (i + row) % 2, 10)),
                                    Place(s, l, (float)rng.NextDouble() * 6.28f, 0.42f + 0.1f * (float)rng.NextDouble(), 0.1f).M, new Vector3(0.9f, 0.85f, 0.8f));
                            }
                        break;
                    }
            }
        }
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
        BranchLand(mesh, p, eye, drawDistance);
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

    void BranchLand(MeshBuilder mesh, PlanScene p, Double3 eye, float drawDistance)
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
                    _branchCells[(a.Branch, i)] = cell = BranchCell(p, a.Branch, local, i);
                var at = Matrix4x4.CreateTranslation(cell.Origin.RelativeTo(eye));
                mesh.Instances.Add(new MeshInstance(cell.Soup, at));
                foreach (var (piece, m) in cell.Pieces)
                    mesh.Instances.Add(new MeshInstance(piece, m * at));
            }
        }
    }

    /// <summary>
    /// One 100 m cell of a branch's land either side of its bed, out to 78 m, where the main line's own land doesn't
    /// already reach (it runs 300 m out); a stand of trees on it.
    /// </summary>
    Cell BranchCell(PlanScene p, int branch, RailLine local, long index)
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
        // A stand of pines out beyond the verge, fewer where the land is open.
        var rng = new Random(unchecked(branch * 7919 + (int)index * 104729));
        for (int k = 0; k < 10; k++)
        {
            double s = a + rng.NextDouble() * (b - a);
            double lateral = (rng.Next(2) == 0 ? -1 : 1) * (12 + rng.NextDouble() * 55);
            var t = local.Sample(s);
            var w = t.Position + Double3.Cross(t.Tangent, Double3.Up).Normalized * lateral;
            if (Covered(w) || p.Terrain.WaterAt(w.X, w.Z) is not null || p.Terrain.Nearby(w.X, w.Z, 12).Any(n => Math.Abs(n.Lateral) < 9))
                continue;
            w = w with { Y = p.Terrain.Height(w.X, w.Z) - 0.15 };
            int variant = rng.Next(4);
            float height = 7 + (float)rng.NextDouble() * 10;
            var piece = Piece($"pine-{variant}", () => WorldKit.Pine(_look, variant, 12));
            var m = Matrix4x4.CreateScale(height / 12) * Matrix4x4.CreateRotationY((float)rng.NextDouble() * MathF.Tau) * Matrix4x4.CreateTranslation(w.RelativeTo(origin));
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

    // ------------------------------------------------------------------ water

    /// <summary>§12.4's water: a river's flat surface where it runs under its bridge, a marsh's standing water off the bed.</summary>
    void Water(MeshBuilder mesh, PlanScene p, Double3 eye, float drawDistance)
    {
        var k = new Kit(_look, mesh) { SurfaceOrigin = new Vector3(W(eye.X), W(eye.Y), W(eye.Z)), Baked = 0 };
        k.Use(_look?.Layer("water_dark") >= 0 ? "water_dark" : "tar", new Vector3(0.05f, 0.06f, 0.07f), 0.05f, 0.9f, tile: 29);
        k.Tint = new Vector3(0.35f, 0.4f, 0.45f);
        // The water's colour (maritime-rules.md §2-5): the upland lakes and rivers tea-dark with tannin, the sea slate,
        // Fundy's tidal water red-brown with the mud it carries.
        var peat = new Vector3(1.2f, 0.95f, 0.7f);
        var slate = new Vector3(0.85f, 1.0f, 1.1f);
        var mud = new Vector3(2.2f, 1.3f, 0.9f);
        foreach (var w in p.Plan.Water)
        {
            var line = p.EdgeLine(w.Edge);
            var mid = line.Sample(Math.Clamp((w.S0 + w.S1) / 2, 0, line.Length));
            if ((mid.Position - eye).Length > drawDistance + 150)
                continue;
            // The tar ponds (biomes.json contaminatedMarsh, GDD §30): not water. Black pitch, glossy as a mirror,
            // an oil film's colours on it and its slow bubbles: a marsh you'd not wade.
            bool tar = w.Type == "contaminatedMarsh" || p.BiomeAt((w.S0 + w.S1) / 2) == "contaminatedMarsh";
            if (tar)
                k.Use(_look?.Layer("tar") >= 0 ? "tar" : "water_dark", new Vector3(0.02f, 0.02f, 0.02f), PitchWear, 0.55f, tile: 9).Shade(0.3f);
            else
                k.Tint = w.Type == "tidal" ? mud : w.Type == "river" ? peat : new Vector3(0.9f, 0.95f, 0.9f);
            bool river = w.Type is "river" or "tidal";
            // A river runs across under the span and on down its valley either way into the far land (note 138); a marsh
            // lies beside the bed.
            double reach = river ? 1200 : 90, inner = river ? -1200 : 5;
            for (double s = w.S0; s < w.S1; s += 10)
            {
                double s1 = Math.Min(s + 10, w.S1);
                var a = line.Sample(s);
                var b = line.Sample(s1);
                var ra = Double3.Cross(a.Tangent, Double3.Up).Normalized;
                var rb = Double3.Cross(b.Tangent, Double3.Up).Normalized;
                foreach (int side in river ? new[] { 1 } : new[] { -1, 1 })
                {
                    Vector3 P(TrackSample t, Double3 r, double l) => (new Double3(t.Position.X, w.LevelM, t.Position.Z) + r * (side * l)).RelativeTo(eye);
                    var q0 = P(a, ra, inner);
                    var q1 = P(a, ra, reach);
                    var q2 = P(b, rb, reach);
                    var q3 = P(b, rb, inner);
                    k.Quad(q0, q1, q2, q3, new Vector2(q0.X, q0.Z), new Vector2(q1.X, q1.Z), new Vector2(q2.X, q2.Z), new Vector2(q3.X, q3.Z), twoSided: true);
                    if (tar)
                        TarFilm(k, a, ra, side, w.LevelM, s, eye);
                }
            }
            if (tar)
                k.Use(_look?.Layer("water_dark") >= 0 ? "water_dark" : "tar", new Vector3(0.05f, 0.06f, 0.07f), 0.05f, 0.9f, tile: 29);
        }
        // Lakes: a disc a little past the shore at the water's level; the land's own shore hides what's outside it.
        k.Tint = peat;
        foreach (var lake in p.Plan.Lakes)
        {
            var c = new Double3(lake.X, lake.LevelM, lake.Z);
            double reach = lake.RadiusM * lake.Stretch * (1 + lake.Wobble);
            if ((c - eye).Length > drawDistance + reach)
                continue;
            const int n = 40;
            var centre = c.RelativeTo(eye);
            // A lake in the tar ponds' country is one of them: pitch, not peat water.
            var near = p.Terrain.Nearby(lake.X, lake.Z, 700).Where(q => q.Edge == 0).OrderBy(q => Math.Abs(q.Lateral)).FirstOrDefault();
            bool tar = p.BiomeAt(near.S) == "contaminatedMarsh";
            if (tar)
                k.Use(_look?.Layer("tar") >= 0 ? "tar" : "water_dark", new Vector3(0.02f, 0.02f, 0.02f), PitchWear, 0.55f, tile: 9).Shade(0.3f);
            Vector3 Rim(int i)
            {
                double a = i * Math.Tau / n, u = Math.Cos(a) * lake.RadiusM * lake.Stretch, v = Math.Sin(a) * lake.RadiusM;
                double grow = 1 + lake.Wobble * 1.1 + 0.08;
                return new Double3(lake.X + (u * lake.Cos - v * lake.Sin) * grow, lake.LevelM, lake.Z + (u * lake.Sin + v * lake.Cos) * grow).RelativeTo(eye);
            }
            for (int i = 0; i < n; i++)
            {
                var a = Rim(i);
                var b = Rim(i + 1);
                k.Quad(centre, a, b, centre, new Vector2(centre.X, centre.Z), new Vector2(a.X, a.Z), new Vector2(b.X, b.Z), new Vector2(centre.X, centre.Z), twoSided: true);
            }
            if (tar)
            {
                var sheen = k.Tint;
                var rng = new Random(lake.Id.GetHashCode(StringComparison.Ordinal) & 0xffff);
                for (int i = 0; i < 6; i++)
                {
                    double a = rng.NextDouble() * Math.Tau, d = Math.Sqrt(rng.NextDouble()) * lake.RadiusM * 0.8;
                    var at = new Double3(lake.X + Math.Cos(a) * d, lake.LevelM, lake.Z + Math.Sin(a) * d);
                    TarSpot(k, at, rng, eye);
                }
                k.Tint = sheen;
                k.Use(_look?.Layer("water_dark") >= 0 ? "water_dark" : "tar", new Vector3(0.05f, 0.06f, 0.07f), 0.05f, 0.9f, tile: 29);
                k.Tint = peat;
            }
        }
        // Shores: the sea from just inside the water's edge out into the fog, along the shore and past its tapered ends.
        foreach (var sh in p.Plan.Shores)
        {
            var line = p.EdgeLine(sh.Edge);
            double taper = p.Plan.Rules.Terrain.Shore.TaperM;
            var tint = sh.Kind == ShoreKind.Sea ? slate : sh.Kind == ShoreKind.River ? peat : mud;
            k.Tint = tint;
            // Where it runs through the tar ponds' country the shore's water is pitch out into the fog, filmed and blistered.
            bool pitch = false;
            void Pitch(double s)
            {
                bool tar = p.BiomeAt(Math.Clamp(s, 0, line.Length)) == "contaminatedMarsh";
                if (tar == pitch)
                    return;
                pitch = tar;
                if (tar)
                    k.Use(_look?.Layer("tar") >= 0 ? "tar" : "water_dark", new Vector3(0.02f, 0.02f, 0.02f), PitchWear, 0.55f, tile: 9).Shade(0.3f);
                else
                {
                    k.Use(_look?.Layer("water_dark") >= 0 ? "water_dark" : "tar", new Vector3(0.05f, 0.06f, 0.07f), 0.05f, 0.9f, tile: 29);
                    k.Tint = tint;
                }
            }
            if (sh.Kind == ShoreKind.River)
            {
                // A river: a ribbon between its banks, falling with the rail, along its meander.
                for (double s = sh.S0 - taper * 0.5; s < sh.S1 + taper * 0.5; s += 10)
                {
                    double s1 = Math.Min(s + 10, sh.S1 + taper * 0.5);
                    var a = line.Sample(Math.Clamp(s, 0, line.Length));
                    var b = line.Sample(Math.Clamp(s1, 0, line.Length));
                    if ((a.Position - eye).Length > drawDistance + 200)
                        continue;
                    var ra = Double3.Cross(a.Tangent, Double3.Up).Normalized * sh.Side;
                    var rb = Double3.Cross(b.Tangent, Double3.Up).Normalized * sh.Side;
                    double da = p.Terrain.ShoreEdge(sh, s), db = p.Terrain.ShoreEdge(sh, s1);
                    Vector3 R(TrackSample t, Double3 r, double l) => (new Double3(t.Position.X, t.Position.Y - sh.LevelM, t.Position.Z) + r * l).RelativeTo(eye);
                    var q0 = R(a, ra, da - 4);
                    var q1 = R(a, ra, da + sh.FlatM + 4);
                    var q2 = R(b, rb, db + sh.FlatM + 4);
                    var q3 = R(b, rb, db - 4);
                    Pitch(s);
                    k.Quad(q0, q1, q2, q3, new Vector2(q0.X, q0.Z), new Vector2(q1.X, q1.Z), new Vector2(q2.X, q2.Z), new Vector2(q3.X, q3.Z), twoSided: true);
                }
                continue;
            }
            for (double s = sh.S0 - taper; s < sh.S1 + taper; s += 20)
            {
                double s1 = Math.Min(s + 20, sh.S1 + taper);
                var a = line.Sample(Math.Clamp(s, 0, line.Length));
                var b = line.Sample(Math.Clamp(s1, 0, line.Length));
                if ((a.Position - eye).Length > drawDistance + 1500)
                    continue;
                var ra = Double3.Cross(a.Tangent, Double3.Up).Normalized * sh.Side;
                var rb = Double3.Cross(b.Tangent, Double3.Up).Normalized * sh.Side;
                double inner = Math.Max(sh.NearM * 0.5, 12);
                Vector3 P(TrackSample t, Double3 r, double l) => (new Double3(t.Position.X, sh.LevelM, t.Position.Z) + r * l).RelativeTo(eye);
                Pitch(s);
                if (pitch && (a.Position - eye).Length < drawDistance)
                    for (int i = 0; i < 2; i++)
                        TarFilm(k, a, ra, 1, sh.LevelM, s + i * 10, eye);
                foreach (var (l0, l1) in new[] { (inner, 300.0), (300.0, 1500.0) })
                {
                    var q0 = P(a, ra, l0);
                    var q1 = P(a, ra, l1);
                    var q2 = P(b, rb, l1);
                    var q3 = P(b, rb, l0);
                    k.Quad(q0, q1, q2, q3, new Vector2(q0.X, q0.Z), new Vector2(q1.X, q1.Z), new Vector2(q2.X, q2.Z), new Vector2(q3.X, q3.Z), twoSided: true);
                }
            }
            if (pitch)
                k.Use(_look?.Layer("water_dark") >= 0 ? "water_dark" : "tar", new Vector3(0.05f, 0.06f, 0.07f), 0.05f, 0.9f, tile: 29);
        }
    }

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
    // open sea, no far side), and its heights over the water.
    static readonly double[] FarLateral = [292, 340, 420, 560, 800, 1150, 1700];
    static readonly double[] FarRise = [-0.6, 3, 8, 16, 26, 38, 50];
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
        const double step = 40, reach = 900;
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
