using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Stops;

namespace DarkTerritory.Sim.Run;

// The rest of the lineside (note 389; note 371's not-yet): what the art alone still placed beside a generated line, each from
// its own stream off the night's seed, and stood on its kit piece's footprint (footprints.json, measured off the art's mesh).
// The country road's homesteads, poles and cars (maritime-rules.md §2.2); the shore's sheds, wharves, lighthouses, ledges and
// boulders (§6, §3); and the biomes' props by the block (biomes.json `props`). The rules are the art's as they were
// (PlanArt.PlanDressing); only where each stands moves here, so it's walked into where it's seen.
public sealed partial class LinesideProps
{
    /// <summary>The road's homesteads and poles are dealt in cells this long along it, a shore's sheds and rocks in these, the biomes' props in blocks.</summary>
    public const double RoadCellM = 45, ShoreCellM = 20, BlockM = 150;
    /// <summary>A shore's ends, where nothing of its own is built (PlanArt's).</summary>
    const double ShoreEndsM = 60;
    /// <summary>The most a block's prop draws for its parts: a stone wall's lengths, an old field's spruce, an orchard's rows.</summary>
    const int WallLengths = 18, FieldTrees = 30, OrchardRows = 4, OrchardRow = 6;

    readonly IReadOnlyDictionary<string, double[][][]> _footprints;

    /// <summary>How many variants the art has of a piece (footprints.json lists each).</summary>
    int Variants(string piece) => _footprints.TryGetValue(piece, out var v) && v.Length > 0 ? v.Length : 1;

    /// <summary>A variant from a draw in [0, 1).</summary>
    int Variant(string piece, double draw) => Math.Min(Variants(piece) - 1, (int)(draw * Variants(piece)));

    /// <summary>The boxes a piece's variant stands on, in its own frame (none for one walked through, or one the file doesn't know).</summary>
    double[][] Boxes(string piece, int variant) =>
        _footprints.TryGetValue(piece, out var v) && variant >= 0 && variant < v.Length ? v[variant] : [];

    /// <summary>
    /// How a rock kind is stretched off the unit lump (PlanArt's): the Atlantic's granite ledges broad and low, an outcrop's
    /// slab breaking through the thin soil broader and lower; the rest as they come.
    /// </summary>
    public static Double3 RockStretch(string species) => species switch
    {
        "ledge" => new Double3(1.6, 0.35, 1.1),
        "outcrop" => new Double3(1.8, 0.45, 1.3),
        _ => new Double3(1, 1, 1),
    };

    /// <summary>A rock's top over its foot before it's sunk, at its size and stretch (the lump's own: about 0.85 at size 1).</summary>
    double RockTop(int variant, double size, string species)
    {
        var boxes = Boxes("rock", variant);
        return (boxes.Length > 0 ? boxes.Max(b => b[5]) : 0.85) * size * RockStretch(species).Y;
    }

    /// <summary>How far a piece reaches out round its foot (m): what's dealt within a reach is all that reaches into it.</summary>
    public double Extent(LinesideProp p) => p.Kind switch
    {
        LinesideKind.Piece => Reach(Boxes(p.Species, p.Variant)) * p.Size,
        LinesideKind.Rock => Reach(Boxes("rock", p.Variant)) * p.Size * Math.Max(RockStretch(p.Species).X, RockStretch(p.Species).Z),
        _ => p.Radius,
    };

    /// <summary>The furthest any box reaches from the foot, however it's turned: its centre's distance and its half diagonal.</summary>
    static double Reach(double[][] boxes) => boxes.Length == 0 ? 1 : boxes.Max(b => double.Hypot(b[0], b[1]) + double.Hypot(b[2], b[3]));

    /// <summary>The land's height at a point beside the line.</summary>
    double GroundAt(double along, double offset)
    {
        var w = World(along, offset);
        return _terrain.Height(w.X, w.Z);
    }

    /// <summary>The art's test for a building's ground: its foot, nearer the line, and a block either way along.</summary>
    bool BuildingRoom(double along, double lateral) =>
        Free(along, lateral) && Free(along, lateral * 0.8) && Free(along + 8, lateral) && Free(along - 8, lateral);

    static LinesideProp Piece(string name, double along, double lateral, double yaw, double scale, double sink, int variant, double top, double ground, uint seed = 0) =>
        new(LinesideKind.Piece, along, lateral, yaw, top * scale, scale, sink, variant, name, false, false, seed, ground);

    LinesideProp PieceOf(string name, double along, double lateral, double yaw, double scale, double sink, int variant, double ground, uint seed = 0)
    {
        var boxes = Boxes(name, variant);
        return Piece(name, along, lateral, yaw, scale, sink, variant, boxes.Length > 0 ? boxes.Max(b => b[5]) : 1, ground, seed);
    }

    LinesideProp Rock(string species, double along, double lateral, double yaw, double size, double sink, int variant, double ground) =>
        new(LinesideKind.Rock, along, lateral, yaw, RockTop(variant, size, species), size, sink, variant, species, false, false, 0, ground);

    /// <summary>Whether any of it reaches within <paramref name="reach"/> of the line.</summary>
    public bool Within(LinesideProp p, double reach) => Math.Abs(p.Lateral) - Extent(p) <= reach;

    /// <summary>
    /// The country road's (maritime-rules.md §2.2): its poles, leaning, on its far side; now and then a homestead facing it
    /// (the house, its woodpile, a barn behind, a fence along the front); now and then a car left where it stopped. A cell
    /// of 45 m along the road, each on its own stream, every draw made whatever's kept.
    /// </summary>
    IEnumerable<LinesideProp> Roads(double from, double to, double reach)
    {
        var rr = _plan.Rules.Terrain.Roads;
        foreach (var road in _plan.Roads)
        {
            if (road.S1 <= from || road.S0 >= to)
                continue;
            double a0 = Math.Max(road.S0, from), a1 = Math.Min(road.S1, to);
            for (double at = Math.Ceiling(a0 / RoadCellM) * RoadCellM; at < a1; at += RoadCellM)
            {
                double l = TerrainField.RoadLateral(road, _plan.Crossings, at, rr.RampM);
                if (Math.Abs(l) < 10 || !Clear(at))
                    continue;
                int away = Math.Sign(l);
                var rng = Streams.Rng(_seed, "lineside-road", road.Id, (long)Math.Round(at / RoadCellM));
                double lean = rng.NextDouble(), homestead = rng.NextDouble(), set = rng.NextDouble(), turn = rng.NextDouble(), barnRoll = rng.NextDouble(),
                    carRoll = rng.NextDouble(), carTurn = rng.NextDouble();
                int house = Variant("saltbox", rng.NextDouble()), pile = Variant("woodpile", rng.NextDouble()), barn = Variant("barn", rng.NextDouble()),
                    car = Variant("car", rng.NextDouble());
                var posts = new double[24];
                for (int i = 0; i < posts.Length; i++)
                    posts[i] = rng.NextDouble();
                var found = new List<LinesideProp>();
                double poleAt = l + away * (rr.HalfWidthM + 2.5);
                if (Free(at, poleAt))
                    found.Add(PieceOf("pole", at, poleAt, (lean - 0.5) * 0.3, 0.85, 0.2, (int)(at / RoadCellM) % Variants("pole"), GroundAt(at, poleAt)));
                if (homestead < 0.22)
                {
                    double back = l + away * (rr.HalfWidthM + 12 + set * 10);
                    double face = away > 0 ? -Math.PI / 2 : Math.PI / 2;
                    if (Free(at, back) && Free(at + 8, back) && Free(at - 8, back))
                    {
                        found.Add(PieceOf("saltbox", at, back, face + (turn - 0.5) * 0.2, 1, 0.3, house, GroundAt(at, back)));
                        if (Free(at + 11, back))
                            found.Add(PieceOf("woodpile", at + 11, back - away * 2, 0, 1, 0.05, pile, GroundAt(at + 11, back - away * 2)));
                        double barnOut = back + away * 22;
                        if (barnRoll < 0.45 && Free(at - 4, barnOut))
                            found.Add(PieceOf("barn", at - 4, barnOut, face, 1, 0.3, barn, GroundAt(at - 4, barnOut)));
                        double fence = l + away * (rr.HalfWidthM + 4.5);
                        for (int i = 0; i < 12; i++)
                        {
                            double f = at - 14 + i * 2.4;
                            if (posts[2 * i] > 0.15 && Free(f, fence))
                                found.Add(PieceOf("fencePost", f, fence, (posts[2 * i + 1] - 0.5) * 0.4, 1, 0.1, 0, GroundAt(f, fence)));
                        }
                    }
                }
                else if (carRoll < 0.05)
                {
                    // On the road's edge, so off a stop's ground by its own test (the road's is the car's).
                    double by = l + away * (rr.HalfWidthM + 0.5);
                    if (!OnStop(at, by) && !InClearing(at, by))
                        found.Add(PieceOf("car", at, by, (carTurn - 0.5) * 0.6, 1, 0.12, car, GroundAt(at, by)));
                }
                foreach (var p in found)
                    if (Within(p, reach))
                        yield return p;
            }
        }
    }

    /// <summary>
    /// The shore's own (maritime-rules.md §6, §3): at a cove's head, two or three fish sheds on their stilts along the waterline
    /// and a crib wharf run out from them; a lighthouse out on a headland; the Atlantic's granite ledges and its weed-black rocks
    /// at the tide line; boulders along the edge, or across a river's bed. A cell of 20 m along the shore.
    /// </summary>
    IEnumerable<LinesideProp> Shores(double from, double to, double reach)
    {
        foreach (var sh in _plan.Shores)
        {
            if (sh.Kind == ShoreKind.Dyke || sh.S1 <= from || sh.S0 >= to)
                continue;
            for (double s = Math.Ceiling(Math.Max(from, sh.S0 + ShoreEndsM) / ShoreCellM) * ShoreCellM; s < Math.Min(sh.S1 - ShoreEndsM, to); s += ShoreCellM)
            {
                if (!Clear(s))
                    continue;
                var rng = Streams.Rng(_seed, "lineside-shore", sh.Id, (long)Math.Round(s / ShoreCellM));
                double cove = rng.NextDouble(), shedRoll = rng.NextDouble(), headland = rng.NextDouble();
                var turns = new double[3];
                for (int i = 0; i < turns.Length; i++)
                    turns[i] = rng.NextDouble();
                int wharf = Variant("wharf", rng.NextDouble()), light = Variant("lighthouse", rng.NextDouble());
                // Five draws a rock (along, out, size, variant, turn): two ledges, three weed rocks, four boulders.
                var rocks = new double[45];
                for (int i = 0; i < rocks.Length; i++)
                    rocks[i] = rng.NextDouble();
                double e0 = _terrain.ShoreEdge(sh, s - 40), e = _terrain.ShoreEdge(sh, s), e1 = _terrain.ShoreEdge(sh, s + 40);
                double seaward = sh.Side > 0 ? Math.PI / 2 : -Math.PI / 2;
                var found = new List<LinesideProp>();
                if (e > e0 && e >= e1 && sh.Kind == ShoreKind.Sea && cove < 0.7)
                {
                    int sheds = 2 + (int)(shedRoll * 2);
                    for (int i = 0; i < sheds; i++)
                    {
                        double along = s + (i - sheds / 2.0) * 9, lateral = sh.Side * (_terrain.ShoreEdge(sh, along) - 2);
                        if (Free(along, lateral * 0.85))
                            found.Add(PieceOf("fishShed", along, lateral, seaward + (turns[i] - 0.5) * 0.4, 1, 0.9, i % Variants("fishShed"), GroundAt(along, lateral)));
                    }
                    found.Add(PieceOf("wharf", s + 4, sh.Side * (e + 1), seaward + Math.PI, 1, 0, wharf, GroundAt(s + 4, sh.Side * (e + 1))));
                }
                else if (e < e0 && e <= e1 && sh.Kind == ShoreKind.Sea && headland < 0.18)
                {
                    double lateral = sh.Side * Math.Max(12, e - 10);
                    if (Free(s, lateral))
                        found.Add(PieceOf("lighthouse", s, lateral, seaward, 1, 0.3, light, GroundAt(s, lateral)));
                }
                // Rock i's draws: rocks[5i..5i+4].
                LinesideProp RockAt(int i, string species, double along, double lateral, double size, double sink) =>
                    Rock(species, along, lateral, rocks[5 * i + 4] * 2 * Math.PI, size, sink, Variant("rock", rocks[5 * i + 3]), GroundAt(along, lateral));
                if (sh.Kind == ShoreKind.Sea)
                {
                    for (int i = 0; i < 2; i++)
                    {
                        double along = s + rocks[5 * i] * ShoreCellM, size = 3 + rocks[5 * i + 2] * 5;
                        found.Add(RockAt(i, "ledge", along, sh.Side * (_terrain.ShoreEdge(sh, along) - 3 + rocks[5 * i + 1] * 7), size, size * 0.2));
                    }
                    for (int i = 2; i < 5; i++)
                    {
                        double along = s + rocks[5 * i] * ShoreCellM, size = 0.6 + rocks[5 * i + 2];
                        found.Add(RockAt(i, "weed", along, sh.Side * (_terrain.ShoreEdge(sh, along) + 1 + rocks[5 * i + 1] * 6), size, size * 0.5));
                    }
                }
                for (int i = 5; i < (sh.Kind == ShoreKind.River ? 9 : 7); i++)
                {
                    double along = s + rocks[5 * i] * ShoreCellM, size = 0.7 + rocks[5 * i + 2] * 1.6;
                    double out_ = sh.Kind == ShoreKind.River ? rocks[5 * i + 1] * sh.FlatM : (rocks[5 * i + 1] - 0.6) * 10;
                    found.Add(RockAt(i, sh.Kind == ShoreKind.Fundy ? "redBoulder" : "boulder", along, sh.Side * (_terrain.ShoreEdge(sh, along) + out_), size, size * 0.4));
                }
                foreach (var p in found)
                    if (Within(p, reach))
                        yield return p;
            }
        }
    }

    /// <summary>
    /// What people left, and what the ice left (biomes.json `props`): by the 150 m block, each prop its own stream, as the
    /// biome has it: its chance, how far out, how many. A building where its ground is the woods' and a block either way
    /// is clear; a stone wall's lengths run along the line with gaps where it's fallen; an old field grown in with spruce;
    /// an orchard's rows of dead apple trees; an erratic, an outcrop.
    /// </summary>
    IEnumerable<LinesideProp> Blocks(double from, double to, double reach)
    {
        for (double b = Math.Floor(from / BlockM) * BlockM; b < to; b += BlockM)
        {
            if (b < from || !Clear(b) || Biome(b) is not { } def)
                continue;
            long block = (long)Math.Round(b / BlockM);
            foreach (var (name, rule) in def.Props.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                for (int n = 0; n < rule.Count; n++)
                {
                    var rng = Streams.Rng(_seed, "lineside-prop", name, block * 16 + n);
                    double chance = rng.NextDouble(), side = rng.NextDouble(), out_ = rng.NextDouble(), at = rng.NextDouble(), v = rng.NextDouble(),
                        jitter = rng.NextDouble() - 0.5;
                    if (chance > rule.Chance)
                        continue;
                    double lateral = (side < 0.5 ? -1 : 1) * (rule.OutM[0] + out_ * (rule.OutM[1] - rule.OutM[0]));
                    double along = b + at * BlockM;
                    foreach (var p in Prop(name, along, lateral, v, jitter, rng, reach))
                        yield return p;
                }
        }
    }

    IEnumerable<LinesideProp> Prop(string name, double along, double lateral, double v, double jitter, Pcg32 rng, double reach)
    {
        double face = lateral > 0 ? Math.PI / 2 : -Math.PI / 2;
        // The prop's own further draws, all made before anything's tested.
        double size = rng.NextDouble();
        switch (name)
        {
            case "erratic":
            case "outcrop":
                {
                    // A granite erratic: a house-sized boulder the ice left in the open. An outcrop: the ledge through thin soil, a broad, low slab.
                    bool erratic = name == "erratic";
                    size = erratic ? 2.2 + size * 3 : 2 + size * 4;
                    var rock = Rock(name, along, lateral, jitter * 6, size, size * (erratic ? 0.25 : 0.18), Variant("rock", v), 0);
                    if (Math.Abs(lateral) - Extent(rock) > reach || !OffTheLine(along, lateral, out double ground) || InClearing(along, lateral))
                        yield break;
                    yield return rock with { Ground = ground };
                    yield break;
                }
            case "stoneWall":
                {
                    // A field's wall, run along the line for a stretch, gaps where it's fallen. Each length runs 8 m on from
                    // where it stands (NovaKit.StoneWall, along its −z), where its ground was tested (the art stood it 8 m on).
                    int lengths = 6 + (int)(size * 12);
                    var gaps = new double[WallLengths];
                    for (int i = 0; i < gaps.Length; i++)
                        gaps[i] = rng.NextDouble();
                    if (Math.Abs(lateral) - 1 > reach)
                        yield break;
                    for (int i = 0; i < lengths; i++)
                    {
                        double s = along + i * 8;
                        if (s > _line.Length || !Clear(s) || !Free(s, lateral) || gaps[i] < 0.12)
                            continue;
                        yield return PieceOf("stoneWall", s, lateral, 0, 1, 0.1, i % Variants("stoneWall"), GroundAt(s, lateral));
                    }
                    yield break;
                }
            case "oldField":
                {
                    // Pasture spruce (maritime-rules.md §5): a field given up, grown in solid with white spruce all of an age and height.
                    double h = 4 + size * 4;
                    var trees = new (double S, double L, int V, double Yaw, double H)[FieldTrees];
                    for (int i = 0; i < trees.Length; i++)
                        trees[i] = (along + (rng.NextDouble() - 0.5) * 36, lateral + (rng.NextDouble() - 0.5) * 24, (int)(rng.NextDouble() * 4),
                            rng.NextDouble() * 2 * Math.PI, h * (0.85 + 0.3 * rng.NextDouble()));
                    if (Math.Abs(lateral) - 13 > reach)
                        yield break;
                    foreach (var (s, l, tv, yaw, height) in trees)
                        if (Math.Abs(l) <= reach + 1 && !InClearing(s, l) && OffTheLine(s, l, out double ground))
                            yield return new LinesideProp(LinesideKind.Tree, s, l, yaw, height, 1, 0.1, tv, "fieldSpruce", false, false, 0, ground);
                    yield break;
                }
            case "orchard":
                {
                    // An orchard gone to ruin: rows of small, gnarled dead apple trees, running out from the line.
                    var trees = new (double Gap, double Yaw, double Scale)[OrchardRows * OrchardRow];
                    for (int i = 0; i < trees.Length; i++)
                        trees[i] = (rng.NextDouble(), rng.NextDouble() * 2 * Math.PI, 0.42 + 0.1 * rng.NextDouble());
                    if (Math.Abs(lateral) - 1 > reach)
                        yield break;
                    for (int row = 0; row < OrchardRows; row++)
                        for (int i = 0; i < OrchardRow; i++)
                        {
                            var (gap, yaw, scale) = trees[row * OrchardRow + i];
                            double s = along + i * 6, l = lateral + Math.Sign(lateral) * row * 6;
                            if (Math.Abs(l) > reach + 1 || gap < 0.15 || InClearing(s, l) || !OffTheLine(s, l, out double ground))
                                continue;
                            yield return new LinesideProp(LinesideKind.Tree, s, l, yaw, scale * 10, 1, 0.1, (i + row) % 2, "apple", true, false, 0, ground);
                        }
                    yield break;
                }
        }
        // A building, or what's left of one (the art's yaws and sinks).
        (double Yaw, double Sink)? how = name switch
        {
            "saltbox" => (face + jitter * 0.6, 0.3),
            "barn" => (face + jitter * 0.5, 0.3),
            "church" => (face + jitter * 0.2, 0.3),
            "buryingGround" => (face + jitter * 0.3, 0.1),
            "fishShed" => (face + jitter * 0.8, 0.2),
            "ruin" => (face + jitter, 0.2),
            "chimney" => (jitter, 0.3),
            "tank" => (jitter * 6, 0.3),
            "headframe" => (face + jitter * 0.4, 0.3),
            _ => null,
        };
        if (how is not { } h2)
            yield break;
        var piece = PieceOf(name, along, lateral, h2.Yaw, 1, h2.Sink, Variant(name, v), 0);
        if (!Within(piece, reach) || !BuildingRoom(along, lateral))
            yield break;
        yield return piece with { Ground = GroundAt(along, lateral) };
    }

    /// <summary>
    /// A piece's walls: each box of its footprint, scaled and turned as the art turns it (the mesh's x across the line and z
    /// back along it, then the piece's yaw), from 3 m under its foot to its top over where it's sunk to.
    /// </summary>
    IEnumerable<Wall> FootprintWalls(LinesideProp p, double[][] boxes, Double3 scale)
    {
        var t = _line.Sample(Math.Clamp(p.Along, 0, _line.Length));
        var fwd = new Double3(t.Tangent.X, 0, t.Tangent.Z).Normalized;
        var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
        var at = (t.Position + right * p.Lateral) with { Y = 0 };
        double c = DMath.Cos(p.Yaw), s = DMath.Sin(p.Yaw);
        foreach (var b in boxes)
        {
            // A turned box keeps its shape only under an even scale across; the stretched ones (rocks) are square to their frame.
            double sx = b[4] == 0 ? scale.X : (scale.X + scale.Z) / 2, sz = b[4] == 0 ? scale.Z : (scale.X + scale.Z) / 2;
            double x = b[0] * sx, z = b[1] * sz;
            var centre = at + right * (x * c + z * s) + fwd * (x * s - z * c);
            double turn = p.Yaw + b[4];
            var axis = (right * DMath.Cos(turn) + fwd * DMath.Sin(turn)).Normalized;
            yield return new Wall(centre, axis, b[2] * sx, b[3] * sz, p.Ground - 3, p.Ground - p.Sink + b[5] * scale.Y);
        }
    }
}
