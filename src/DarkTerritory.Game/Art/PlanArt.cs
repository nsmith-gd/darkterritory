using System.Numerics;
using System.Runtime.CompilerServices;
using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;

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
        }

        public LinePlan Plan { get; }
        public RailLine Line { get; }
        public TerrainField Terrain { get; }
        public (double S0, double S1)[] Washouts { get; }
        /// <summary>The main line's index among the terrain field's edges.</summary>
        public int Main { get; }

        public RailLine EdgeLine(string edge) => edge == "main" ? Line : Line.Branches[Plan.Edge(edge).Branch].Local;

        /// <summary>The biome at a main-line distance (§13.1).</summary>
        public string BiomeAt(double s)
        {
            foreach (var b in Plan.Biomes)
                if (b.Edge == "main" && s >= b.S0 && s < b.S1)
                    return b.Biome;
            return Plan.Biomes.Count > 0 ? Plan.Biomes[^1].Biome : "farmland";
        }

        public BiomeDef? Biome(double s) => Plan.Rules.Biomes.GetValueOrDefault(BiomeAt(s));
    }

    static readonly ConditionalWeakTable<Route, PlanScene> Scenes = new();

    static PlanScene? Scene(Route? route) => route?.Plan is null ? null : Scenes.GetValue(route, r => new PlanScene(r));

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
        // biomes.json names the textures (the Maritime ground, tools/art/texgen/mat_maritime.py); an older plan's names
        // are the splat families they stood for.
        string Texture(string? name) => name switch
        {
            null => "ground_grass",
            "deadGrass" => "ground_grass",
            "soil" => "ground_forest",
            "mud" => "ground_mud",
            "rock" => "rock_cliff",
            "cinder" => "slag",
            _ => name,
        };
        var def = p.Biome(s);
        int ground = _look.Layer(Texture(def?.Ground)), second = _look.Layer(Texture(def?.Materials.FirstOrDefault() ?? "rock"));
        return (ground >= 0 ? ground : _look.Layer("ground_grass"), second >= 0 ? second : _look.Layer("rock_cliff"));
    }

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
        bool Free(double s, double lateral) => !onBranch(s, lateral) && PlanClear(route, line, s, lateral);
        MeshAsset Tree(string kind, int v) => kind switch
        {
            "fir" => Piece($"fir-{v}", () => NovaKit.Conifer(_look, v, 12, 0.46f)),
            "birch" => Piece($"birch-{v % 3}", () => NovaKit.Birch(_look, v % 3)),
            "pine" => Piece($"pine-{v}", () => WorldKit.Pine(_look, v, 12)),
            _ => Piece($"spruce-{v}", () => NovaKit.Conifer(_look, v, 12, 0.3f)),
        };

        for (double s = Math.Ceiling(from / 12) * 12; s < to; s += 12)
        {
            if (!Clear(s) || p.Biome(s) is not { } def)
                continue;
            var rng = new Random(unchecked(seed * 73856093 ^ (int)(s / 12) * 19349663));
            bool dense = def.TreeDensity >= 0.8;
            // Trees: per 100 m² as the biome grows them, both sides out to 90 m; a dense forest crowds the line.
            double near = def.TreeDensity >= 1.2 ? 7 : dense ? 10 : 14;
            int count = Math.Min(20, (int)Math.Round(def.TreeDensity * 12 * 2 * (90 - near) / 100 * 0.45 + rng.NextDouble()));
            double floraTotal = def.Flora.Values.Sum();
            for (int k = 0; k < count; k++)
            {
                double side = rng.Next(2) == 0 ? -1 : 1;
                double offset = side * (near + Math.Pow(rng.NextDouble(), dense ? 0.9 : 0.6) * (90 - near));
                double along = s + rng.NextDouble() * 12;
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
                float height = def.Verge == "barrens" ? 3 + (float)rng.NextDouble() * 5 : (dead ? 7 : 9) + (float)rng.NextDouble() * (dense ? 13 : 9);
                if (!Free(along, offset))
                    continue;
                var (m, slope) = Place(along, offset, yaw, height / 12, 0.15f);
                if (slope > 1.1f)
                    continue; // nothing grows on the crag
                MeshAsset piece = dead
                    ? rng.Next(2) == 0 ? Piece($"ghost-{variant}", () => NovaKit.GhostSpruce(_look, variant)) : Piece($"dead-{variant % 2}", () => WorldKit.DeadTree(_look, variant % 2, 10))
                    : Tree(kind, variant);
                if (dead)
                    (m, _) = Place(along, offset, yaw, height / (piece.Name.StartsWith("ghost") ? 8 + variant * 2.5f : 10) * 0.9f, 0.15f);
                if (kind == "birch" && !dead)
                    (m, _) = Place(along, offset, yaw, height / 11, 0.1f);
                mesh.Append(piece, m, new Vector3(0.8f + 0.3f * (float)rng.NextDouble()));
                if (corrupted)
                    mesh.Append(Piece($"brass-{variant % 3}", () => BrassCluster(_look, variant % 3)), Place(along + 0.6, offset, yaw, 0.9f, 0.05f).M);
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
        Water(mesh, p, eye, drawDistance);
        Places(mesh, p, eye, drawDistance);
        Glow(mesh, p, line, eye, centre);
    }

    /// <summary>How deep the land falls under a generated line's bridge (its valley's, §12.3), for its piers; null on a hand-laid one.</summary>
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
            var at = sign.Type == "tunnelPlate" ? t.Position : t.Position + right * (sign.Side * rules.SignOffsetM) - Double3.Up * 0.25;
            // Turned a little toward the track, so the lamp catches it square.
            var m = Basis(t.Tangent, at, eye, -sign.Side * 0.18f);
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
    static MeshAsset BrassCluster(Look? look, int variant)
    {
        var k = new Kit(look, 1500 + variant);
        k.Use("mineral_growth", Palette.TarnishedBrass, 0.2f, 0.7f, tile: 0.8f);
        k.Emissive = 0.05f;
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
        k.Use("tar", new Vector3(0.05f, 0.06f, 0.07f), 0.05f, 0.9f, tile: 6);
        k.Tint = new Vector3(0.35f, 0.4f, 0.45f);
        foreach (var w in p.Plan.Water)
        {
            var line = p.EdgeLine(w.Edge);
            var mid = line.Sample(Math.Clamp((w.S0 + w.S1) / 2, 0, line.Length));
            if ((mid.Position - eye).Length > drawDistance + 150)
                continue;
            bool river = w.Type == "river";
            // A river runs across under the span, out to the corridor's edge either way; a marsh lies beside the bed.
            double reach = river ? 160 : 90, inner = river ? -160 : 5;
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
                }
            }
        }
    }

    // ------------------------------------------------------------------ halts and towns

    /// <summary>
    /// The plan's places (§11.3): a halt's platform, dark now, its name board the plan's; a dead town's houses round the
    /// line where it runs through, a church or a windmill among them. Nothing lit: nobody's there.
    /// </summary>
    void Places(MeshBuilder mesh, PlanScene p, Double3 eye, float drawDistance)
    {
        foreach (var st in p.Plan.Structures.Where(s => s.Type == StructureType.Platform))
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
        foreach (var town in p.Plan.Landmarks.Where(l => l.Type is "town" or "halt"))
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
        }
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
