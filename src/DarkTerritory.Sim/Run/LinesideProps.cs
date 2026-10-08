using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Stops;

namespace DarkTerritory.Sim.Run;

/// <summary>What stands beside a generated line that a body walks into (note 371): a tree, a boulder, a telegraph pole.</summary>
public enum LinesideKind : byte { Tree, Rock, Pole }

/// <summary>
/// One thing standing beside a generated line (note 371), where it stands (<see cref="Along"/> the main line,
/// <see cref="Lateral"/> out from it, right positive) and what it is. A tree: its <see cref="Species"/> by the biome's flora,
/// <see cref="Height"/>, whether it's <see cref="Dead"/> or <see cref="Corrupted"/>, its mesh <see cref="Variant"/>. A rock:
/// its <see cref="Size"/> (the unit lump's scale) and how far it's <see cref="Sink"/>-ed into the slope. <see cref="Seed"/>
/// is the art's for what's only seen (a tint; a ghost or a dead spruce), so nothing seen-only draws on the solids' stream.
/// </summary>
/// <see cref="Ground"/> is the land's height at its foot.
public readonly record struct LinesideProp(LinesideKind Kind, double Along, double Lateral, double Yaw, double Height, double Size, double Sink,
    int Variant, string Species, bool Dead, bool Corrupted, uint Seed, double Ground)
{
    /// <summary>
    /// Its trunk's radius at the ground, or a rock's across, or a pole's: the art's pieces' own (WorldKit.Boughs 0.015 h for
    /// the pine, NovaKit.Conifer 0.018 h for the spruce, fir and tamarack, NovaKit.Birch 0.13 m at 11 m, NovaKit.GhostSpruce
    /// 0.16 m; WorldKit.Rock a lump about 1 across and 0.85 deep at size 1; WorldKit.Pole 0.14 m at its foot).
    /// </summary>
    public double Radius => Kind switch
    {
        LinesideKind.Pole => LinesideProps.PoleRadius,
        LinesideKind.Rock => Size * 0.9,
        _ when Dead => Math.Clamp(Height * 0.014, 0.1, 0.3),
        _ => Math.Clamp(Height * Species switch { "pine" => 0.015, "birch" => 0.012, _ => 0.018 }, 0.08, 0.45),
    };
}

/// <summary>
/// The world is solid out along the line too (note 371; note 279's "Not yet", GDD App. F.1). A generated line's trees,
/// boulders and telegraph poles were the art's alone (PlanArt.PlanDressing, seeded by a constant, on float noise), so the
/// crew, the creatures, the balls and the bodies went through them. They're placed here now, from the plan, the route's
/// stops, the terrain and the night's seed, alike on every machine (a stream per 12 m slot, the stands' noise on an
/// integer hash, no float trig, no real powers), and stood as walls (<see cref="Walls"/>); the art draws its trees,
/// boulders and poles from <see cref="Props"/>, so what's seen is what's hit. The rules are the art's as they were: forest
/// in world-space stands as dense as the biome grows it, the odd tree out of them, none on a crag, boulders where the land
/// is rough, a pole every 50 m on the right; none on a stop's ground, a branch's, a road's, in water or inside a fort.
/// </summary>
public sealed class LinesideProps
{
    /// <summary>The slots trees and boulders are dealt in along the line (m), and how far out the forest runs.</summary>
    public const double SlotM = 12, ForestOutM = 90;
    /// <summary>The telegraph poles: every 50 m, 4.5 m right of the line (WorldArt's), 0.13 m round and 7.5 m tall (WorldKit.Pole).</summary>
    public const double PoleEveryM = 50, PoleOut = 4.5, PoleRadius = 0.13, PoleHeight = 7.5;
    /// <summary>How many tries a slot makes at a tree, and the steepest land one stands on (rise over run).</summary>
    const int TreeTries = 50;
    const double Crag = 1.1;
    /// <summary>The stops' ground is cleared in cells this big (WorldArt.Stops' ClearCell).</summary>
    public const double ClearCell = 6;

    readonly Route.Route _route;
    readonly RailLine _line;
    readonly LinePlan _plan;
    readonly TerrainField _terrain;
    readonly ulong _seed;
    readonly HashSet<(long, long)> _stopGround;
    readonly (double S0, double S1, double Left, double Right)[] _clearings;
    readonly int _main;
    readonly (double S0, double S1)[] _wet;

    LinesideProps(Route.Route route, RailLine line, TerrainField terrain)
    {
        _route = route;
        _line = line;
        _plan = route.Plan!;
        _terrain = terrain;
        _seed = ulong.TryParse(_plan.Seed.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? _plan.Seed[2..] : _plan.Seed,
            System.Globalization.NumberStyles.HexNumber, null, out var s) ? s : Streams.Mix(0, _plan.Seed);
        _stopGround = StopGround(route);
        _clearings = [.. route.Features.Where(f => f.Stop is not null).Select(f => Clearing(f.Start, f.End, f.Stop!))];
        _main = _plan.Alignment.Select((a, i) => (a.Role, i)).First(x => x.Role == EdgeRole.Main).i;
        // Where along the main line water could be within the forest's reach: a river, a tidal reach or a shore on the main
        // line, with room; anything on a branch, the branch's whole stretch. Lakes are asked by their own extent.
        (double, double) Span(string edge, double s0, double s1, double width)
        {
            const double margin = ForestOutM + 400;
            if (edge == "main")
                return (s0 - width - margin, s1 + width + margin);
            var b = _plan.Edge(edge);
            return b.Branch >= 0 && b.Branch < line.Branches.Count ? (line.Branches[b.Branch].Toe - margin, line.Branches[b.Branch].End + margin) : (double.MinValue, double.MaxValue);
        }
        _wet = [.. _plan.Water.Select(w => Span(w.Edge, w.S0, w.S1, w.WidthM)), .. _plan.Shores.Select(sh => Span(sh.Edge, sh.S0, sh.S1, 0))];
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Route.Route, LinesideProps> Built = new();

    /// <summary>
    /// A generated line's lineside, or null for a hand-laid one (whose lineside is the art's alone). One per route: it's a
    /// pure function of the route, so the art and the sim (and a test's many nights on one route) share what's been dealt.
    /// </summary>
    public static LinesideProps? Of(Route.Route route, RailLine line)
    {
        if (route.Plan is null || line.Conditions is not PlanConditions { Terrain: { } terrain })
            return null;
        lock (Built)
            return Built.GetValue(route, r => new LinesideProps(r, line, terrain));
    }

    /// <summary>The biome's stand cover: its tree density as a share of the land under forest.</summary>
    public static double Cover(BiomeDef def) => def.TreeDensity <= 0 ? 0 : Math.Clamp(def.TreeDensity * 0.85 + 0.08, 0.04, 0.92);

    /// <summary>
    /// Whether a world point is in a stand of forest (maritime-rules.md §5, "the spruce wall"): a world-space field, so the
    /// stands don't follow the line round, over about <paramref name="cover"/> of the land, with a hard edge (a cut, an old
    /// field's line, a bog's shore). 1 in a stand, 0 out.
    /// </summary>
    public static double Stand(Double3 w, double cover) => Stand(w.X, w.Z, cover);

    /// <summary><see cref="Stand(Double3, double)"/> at (<paramref name="x"/>, <paramref name="z"/>).</summary>
    public static double Stand(double x, double z, double cover)
    {
        double n = Noise(x * 0.0065 + 11.7, z * 0.0065 - 3.3) * 0.7 + Noise(x * 0.028 - 7.1, z * 0.028 + 1.9) * 0.3;
        // Two value noises sit about their middle, so the threshold is set there: about cover of the land.
        double edge = 0.5 + (0.5 - cover) * 0.45;
        double t = Math.Clamp((n - (edge - 0.012)) / 0.024, 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>Smooth value noise in 2D on an integer hash: the same on every machine.</summary>
    static double Noise(double x, double y)
    {
        double ix = Math.Floor(x), iy = Math.Floor(y), fx = x - ix, fy = y - iy;
        fx = fx * fx * (3 - 2 * fx);
        fy = fy * fy * (3 - 2 * fy);
        long a = (long)ix, b = (long)iy;
        double h00 = Hash(a, b), h10 = Hash(a + 1, b), h01 = Hash(a, b + 1), h11 = Hash(a + 1, b + 1);
        return (h00 + (h10 - h00) * fx) + ((h01 + (h11 - h01) * fx) - (h00 + (h10 - h00) * fx)) * fy;
    }

    static double Hash(long x, long y)
    {
        ulong h = unchecked((ulong)x * 0x9E3779B97F4A7C15UL ^ (ulong)y * 0xC2B2AE3D27D4EB4FUL);
        h ^= h >> 31;
        h = unchecked(h * 0xBF58476D1CE4E5B9UL);
        h ^= h >> 29;
        return (h >> 11) * (1.0 / (1UL << 53));
    }

    /// <summary>The biome at a main-line distance (linegen plan §13.1).</summary>
    public BiomeDef? Biome(double s)
    {
        string name = _plan.Biomes.Count > 0 ? _plan.Biomes[^1].Biome : "farmland";
        foreach (var b in _plan.Biomes)
            if (b.Edge == "main" && s >= b.S0 && s < b.S1)
            {
                name = b.Biome;
                break;
            }
        return _plan.Rules.Biomes.GetValueOrDefault(name);
    }

    /// <summary>Clear of bridges, and of tunnels and their cuttings (the hill's approaches).</summary>
    public bool Clear(double s) => !_route.InTunnel(s) && !_route.InTunnel(s + 30) && !_route.InTunnel(s - 30) && _route.BridgeAt(s) is null;

    /// <summary>
    /// Somewhere off everything that's the line's own (WorldArt's OnBranch, less the forts, which <see cref="InsideAFort"/>
    /// keeps out after): a branch's ground on its side, a stop's (its buildings, roads and tracks, its yard's throat), an
    /// alternate's, a road's, or water. The cheap tests first; the land's height only where it's still needed.
    /// </summary>
    public bool OffTheLine(double along, double offset) => OffTheLine(along, offset, out _);

    /// <param name="ground">The land's height there, once it's been asked; NaN if it wasn't.</param>
    bool OffTheLine(double along, double offset, out double ground)
    {
        ground = double.NaN;
        foreach (var b in _line.Branches)
            if (along > b.Toe - 20 && along < b.End + 20 && Math.Sign(offset) == b.Side && Math.Abs(offset) < (b.Kind == BranchKind.Spur ? 60 : 16))
                return false;
        if (OnStop(along, offset))
            return false;
        var roads = _plan.Rules.Terrain.Roads;
        foreach (var road in _plan.Roads)
            if (along >= road.S0 && along <= road.S1 && Math.Abs(offset - TerrainField.RoadLateral(road, _plan.Crossings, along, roads.RampM)) < roads.HalfWidthM + 2.5)
                return false;
        var at = World(along, offset);
        foreach (var n in _terrain.Nearby(at.X, at.Z, 14))
            if (n.Edge != _main && Math.Abs(n.Lateral) < 9)
                return false;
        ground = _terrain.Height(at.X, at.Z);
        return !MaybeWet(at, along) || _terrain.WaterAt(at.X, at.Z, ground) is null;
    }

    /// <summary>Whether water could be at a point at all (<see cref="TerrainField.WaterAt(double, double, double?)"/> is dear: it asks every river and shore).</summary>
    bool MaybeWet(Double3 at, double along)
    {
        foreach (var lake in _plan.Lakes)
        {
            double reach = lake.RadiusM * lake.Stretch * (1 + lake.Wobble) + 1;
            if (Math.Abs(at.X - lake.X) <= reach && Math.Abs(at.Z - lake.Z) <= reach)
                return true;
        }
        foreach (var (s0, s1) in _wet)
            if (along >= s0 && along <= s1)
                return true;
        return false;
    }

    /// <summary>Where the woods can grow: <see cref="OffTheLine"/> and off every stop's cleared ground.</summary>
    public bool Free(double along, double offset) => !InClearing(along, offset) && OffTheLine(along, offset);

    /// <summary>On a stop's ground (<see cref="StopGround"/>).</summary>
    public bool OnStop(double along, double offset) => _stopGround.Contains(((long)Math.Floor(along / ClearCell), (long)Math.Floor(offset / ClearCell)));

    /// <summary>A stop's whole zone out past the last thing it built, where the plan's woods don't grow.</summary>
    public bool InClearing(double s, double lateral)
    {
        foreach (var c in _clearings)
            if (s >= c.S0 && s < c.S1 && lateral > -c.Left && lateral < c.Right)
                return true;
        return false;
    }

    /// <summary>Inside a fortress's walls (<see cref="Fortresses.WallOut"/> out, with a little room), or its town's square: nothing wild grows there.</summary>
    public static bool InsideAFort(IReadOnlyList<Fort> forts, double along, double offset)
    {
        foreach (var f in forts)
        {
            if (Math.Abs(offset) < Fortresses.WallOut + 1.7 && along > f.Start - 2 && along < f.End + 2)
                return true;
            if (f.Square is { } sq && Math.Sign(offset) == sq.Side && Math.Abs(offset) < Math.Abs(sq.WallD) + 2 && along > sq.S0 - 2 && along < sq.S1 + 2)
                return true;
        }
        return false;
    }

    /// <summary>The world point <paramref name="offset"/> m right of the main line at <paramref name="along"/>, on the ground plane.</summary>
    public Double3 World(double along, double offset)
    {
        var t = _line.Sample(Math.Clamp(along, 0, _line.Length));
        return t.Position + Double3.Cross(t.Tangent, Double3.Up).Normalized * offset;
    }

    /// <summary>How steep the land is across the line at a point whose height is <paramref name="ground"/>: rise over the 2 m further out.</summary>
    double Slope(double along, double offset, double ground)
    {
        var b = World(along, offset + Math.Sign(offset) * 2);
        return Math.Abs(_terrain.Height(b.X, b.Z) - ground) / 2;
    }

    /// <summary>
    /// Every tree, boulder and pole whose slot starts in [<paramref name="from"/>, <paramref name="to"/>): so neighbouring
    /// stretches (the art's cells) share nothing and miss nothing. The same for the same night on every machine.
    /// </summary>
    /// <param name="reach">Only what stands within this of the line (m): the rest is dealt and passed over unchecked, so
    /// what's within it is the same either way.</param>
    public IEnumerable<LinesideProp> Props(double from, double to, double reach = double.MaxValue)
    {
        for (double s = Math.Ceiling(from / SlotM) * SlotM; s < to; s += SlotM)
            foreach (var p in Slot(s, reach))
                yield return p;
        for (double s = Math.Ceiling(from / PoleEveryM) * PoleEveryM; s < to; s += PoleEveryM)
            if (s >= 0 && s <= _line.Length && Clear(s) && OffTheLine(s, PoleOut, out double ground))
                yield return new LinesideProp(LinesideKind.Pole, s, PoleOut, 0, PoleHeight, 1, 0, (int)(s / PoleEveryM) % 3, "pole", false, false, 0, ground);
    }

    /// <summary>Whether a telegraph pole stands at <paramref name="s"/> (a multiple of <see cref="PoleEveryM"/>): not on a bridge, by a tunnel, on a siding or a branch, in water or inside a fort.</summary>
    public bool Pole(double s) => s >= 0 && s <= _line.Length && Clear(s) && OffTheLine(s, PoleOut);

    /// <summary>One slot's trees and boulders (PlanArt.PlanDressing's, on the slot's own stream).</summary>
    IEnumerable<LinesideProp> Slot(double s, double reach)
    {
        if (!Clear(s) || Biome(s) is not { } def)
            yield break;
        var rng = Streams.Rng(_seed, "lineside", "", (long)Math.Round(s / SlotM));
        // The forest comes in stands (maritime-rules.md §5, "the spruce wall"): as much of the land as the biome's density
        // says, planted solid; out of them only the odd tree. The line runs through them with the alder between.
        double cover = Cover(def);
        bool dense = def.TreeDensity >= 0.8;
        double near = def.TreeDensity >= 1.2 ? 6 : dense ? 7 : 9;
        int tries = def.TreeDensity <= 0 ? 0 : TreeTries;
        var flora = def.Flora.OrderBy(f => f.Key, StringComparer.Ordinal).ToArray();
        double floraTotal = flora.Sum(f => f.Value);
        bool corruptible = def.Trees.Contains("corrupted");
        for (int k = 0; k < tries; k++)
        {
            // Every try draws the same, kept or not, so what's within any reach is dealt alike however far it's asked.
            double side = rng.Chance(0.5) ? -1 : 1;
            double u = rng.NextDouble(), at = rng.NextDouble(), keep = rng.NextDouble(), deadRoll = rng.NextDouble(), corruptRoll = rng.NextDouble();
            int variant = (int)(rng.NextDouble() * 4);
            double yaw = rng.NextDouble() * 2 * Math.PI, pick = rng.NextDouble() * floraTotal, tall = rng.NextDouble(), pineTall = rng.NextDouble();
            uint look = rng.NextUInt();
            // u^0.75 (sqrt · sqrt sqrt, exact everywhere): more of them near than far, as the art's u^0.8 had it.
            double offset = side * (near + Math.Sqrt(u) * Math.Sqrt(Math.Sqrt(u)) * (ForestOutM - near));
            double along = s + at * SlotM;
            if (Math.Abs(offset) > reach || keep > (Stand(World(along, offset), cover) > 0.5 ? 1 : 0.035))
                continue;
            bool dead = deadRoll < def.DeadTrees;
            bool corrupted = dead && corruptible && corruptRoll < 0.35;
            string kind = "spruce";
            foreach (var (name, w) in flora)
                if ((pick -= w) < 0)
                {
                    kind = name;
                    break;
                }
            // Stunted on the barrens and the highland (krummholz), tall in the forest; white pine over the spruce
            // (maritime-rules.md §5: to 35 m over a 20-30 m canopy), even on the barrens.
            double height = def.Verge == "barrens" ? 3 + tall * 5 : (dead ? 7 : 11) + tall * (dense ? 11 : 8);
            if (kind == "pine" && !dead)
                height = (def.Verge == "barrens" ? 9 : 20) + pineTall * 9;
            if (InClearing(along, offset) || !OffTheLine(along, offset, out double ground) || Slope(along, offset, ground) > Crag)
                continue; // nothing grows on the line's ground, or on the crag
            yield return new LinesideProp(LinesideKind.Tree, along, offset, yaw, height, 1, 0.15, variant, kind, dead, corrupted, look, ground);
        }
        // Boulders where the land is rough, bigger and more of them the rougher it is, sunk into the slope.
        int rocks = (int)(def.Rocks + rng.NextDouble());
        for (int k = 0; k < rocks; k++)
        {
            double offset = (rng.Chance(0.5) ? -1 : 1) * (6 + rng.NextDouble() * 80);
            double along = s + rng.NextDouble() * SlotM;
            int variant = (int)(rng.NextDouble() * 3);
            double size = 0.8 + rng.NextDouble() * (1.2 + def.NoiseScale * 1.6);
            double yaw = rng.NextDouble() * 2 * Math.PI;
            if (Math.Abs(offset) > reach || InClearing(along, offset) || !OffTheLine(along, offset, out double ground))
                continue;
            double slope = Slope(along, offset, ground);
            size *= slope > 0.6 ? 1.6 : 1;
            yield return new LinesideProp(LinesideKind.Rock, along, offset, yaw, size * 1.1, size, size * (0.3 + 1.1 * Math.Min(slope, 1.5)), variant, "rock", false, false, 0, ground);
        }
    }

    /// <summary>
    /// Every tree, boulder and pole along the line, stood as walls (note 371): a trunk as the box round its radius, to its
    /// top; a boulder as its lump's box, turned as it lies, its top where its sinking leaves it (one sunk to under a step
    /// is stepped over, as PlayerMotor steps anything that low); a pole round its foot. Owner 0: a facility's module
    /// clears what stands on its work point (<see cref="StopWalls.Clear"/>).
    /// </summary>
    /// <param name="forts">The night's fortresses (<see cref="Fortresses.Of"/>), the departure one's town square on it once there's a town: nothing stands inside.</param>
    /// <param name="reach">How far out from the line they're solid (run.json <c>walls.linesideReachM</c>): where the crew and what hunts them go.</param>
    public IEnumerable<Wall> Walls(IReadOnlyList<Fort> forts, double reach)
    {
        List<(LinesideProp Prop, Wall Wall)> solids;
        lock (_solids)
            if (!_solids.TryGetValue(reach, out solids!))
                _solids[reach] = solids = [.. Props(0, _line.Length, reach).Select(p => (p, WallOf(p)))];
        // Nothing wild grows inside a fort; the poles run on through it.
        foreach (var (p, w) in solids)
            if (p.Kind == LinesideKind.Pole || !InsideAFort(forts, p.Along, p.Lateral))
                yield return w;
    }

    readonly Dictionary<double, List<(LinesideProp, Wall)>> _solids = [];

    Wall WallOf(LinesideProp p)
    {
        var t = _line.Sample(Math.Clamp(p.Along, 0, _line.Length));
        var along = new Double3(t.Tangent.X, 0, t.Tangent.Z).Normalized;
        var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
        var at = (t.Position + right * p.Lateral) with { Y = 0 };
        if (p.Kind == LinesideKind.Rock)
        {
            // The lump is about 1 across and 0.85 deep at size 1, its long side turned yaw from across the line.
            var axis = (right * DMath.Cos(p.Yaw) + along * DMath.Sin(p.Yaw)).Normalized;
            return new Wall(at, axis, p.Size * 0.9, p.Size * 0.75, p.Ground - 3, p.Ground + p.Height - p.Sink);
        }
        return new Wall(at, along, p.Radius, p.Radius, p.Ground - 3, p.Ground + p.Height);
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

    /// <summary>
    /// The cells (<see cref="ClearCell"/> along by across the main line) of every stop's ground: round each building, along
    /// each road and track, the yard's whole throat and the facility's own ground beyond its outermost track (where its
    /// modules and works stand, Site), and a halt's platform. What the lineside leaves alone.
    /// </summary>
    public static HashSet<(long, long)> StopGround(Route.Route route)
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
}
