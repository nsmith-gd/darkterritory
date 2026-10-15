using System.Numerics;
using Ballast.Render;

namespace DarkTerritory.Game.Art;

/// <summary>
/// The kit pieces the sim deals beside a generated line (Sim.Run.LinesideProps, notes 371 and 389), by the name the sim knows
/// each by: how many variants the art has, how each is built, and what it stands on. The sim can't build a mesh, so what
/// each piece stands on is measured here off the mesh itself and written to content/linegen/footprints.json
/// (<c>dt art footprints --write</c>); LinesideFootprintTests holds the file to the meshes, so a piece that changes shape
/// can't leave its walls behind.
/// </summary>
public static class LinesideFootprints
{
    /// <summary>How a piece's footprint is read off its mesh.</summary>
    public enum Shape : byte
    {
        /// <summary>One box round the whole mesh.</summary>
        Bounds,
        /// <summary>Round: a tank on its legs, as two squares a quarter turn apart (an octagon round the drum).</summary>
        Round,
        /// <summary>Only what's below a crewmate's head: a pole's post, not the crossarm over it.</summary>
        Post,
        /// <summary>Each foot standing on the ground on its own: a headframe's four legs and its back stay, open between.</summary>
        Feet,
        /// <summary>The church: its nave, and the tower and steeple in front of it.</summary>
        NaveAndTower,
        /// <summary>Walked through: a burying ground's thin stones and half-down fence, a wharf's deck out over the water.</summary>
        Passable,
        /// <summary>
        /// A whole facility's kit (note 758): the ground under whatever it draws at a crewmate's height, cell by cell, merged
        /// into boxes: its sheds, silos, stacks and works each where they stand, the open ground between them open.
        /// </summary>
        Cells,
    }

    /// <summary>
    /// A kind of piece: the sim's name, the art's cache name for a variant, how many, how it's built, how it stands; and
    /// whether it's built from the content's sourced models (a crate, a sandbag wall), so measured with them.
    /// </summary>
    public sealed record Kind(string Name, Func<int, string> Cached, int Variants, Func<Look?, int, MeshAsset> Make, Shape Shape, bool Instanced, bool Modelled = false);

    /// <summary>Every piece, in the sim's names (biomes.json's props, and the road's and the shore's own).</summary>
    public static readonly Kind[] Kinds =
    [
        new("saltbox", v => $"saltbox-{v}", 8, NovaKit.Saltbox, Shape.Bounds, true),
        new("barn", v => $"barn-{v}", 2, NovaKit.Barn, Shape.Bounds, true),
        new("church", _ => "nova-church", 1, (l, _) => NovaKit.Church(l), Shape.NaveAndTower, true),
        new("buryingGround", v => $"burying-{v}", 3, NovaKit.BuryingGround, Shape.Passable, true),
        new("fishShed", v => $"fishshed-{v}", 3, NovaKit.FishShed, Shape.Bounds, true),
        new("ruin", v => $"ruin-{v}", 3, SettingKit.RuinWall, Shape.Bounds, true),
        new("chimney", v => $"chimney-{v}", 3, SettingKit.Chimney, Shape.Bounds, true),
        new("tank", v => $"tank-{v}", 3, SettingKit.Tank, Shape.Round, true),
        new("headframe", _ => "headframe", 1, (l, _) => SettingKit.Headframe(l), Shape.Feet, true),
        new("stoneWall", v => $"stonewall-{v}", 3, NovaKit.StoneWall, Shape.Bounds, true),
        new("woodpile", v => $"woodpile-{v}", 3, NovaKit.Woodpile, Shape.Bounds, false),
        new("fencePost", v => $"fencepost-{v}", 1, WorldKit.FencePost, Shape.Post, false),
        new("car", v => $"car-{v}", 4, NovaKit.Car, Shape.Bounds, true),
        new("pole", v => $"pole-{v}", 3, WorldKit.Pole, Shape.Post, true),
        new("wharf", v => $"wharf-{v}", 2, NovaKit.Wharf, Shape.Passable, true),
        new("lighthouse", v => $"lighthouse-{v}", 2, NovaKit.Lighthouse, Shape.Bounds, true),
        new("rock", v => $"rock-{v}", 3, (l, v) => WorldKit.Rock(l, v, 1), Shape.Bounds, false),
        // A facility's buildings beyond its machinery (StructureKit.Facility; note 758), its two variants its kit on the left of
        // its track and on the right; measured off the modelled pieces (Measure's models). The coaling tower's trestle is its
        // own (StopWalls.AddTowers).
        .. Enum.GetValues<Sim.Route.FacilityKind>().Where(k => k != Sim.Route.FacilityKind.CoalingTower).Select(k =>
            new Kind(FacilityName(k), v => $"facility-{k}-{(v == 0 ? -1 : 1)}", 2, (l, v) => StructureKit.Facility(l, k, v == 0 ? -1 : 1), Shape.Cells, true)),
        // The railway's leavings beside the line (note 792; E1's, note 325): Sim.Run.Leavings deals each as a cluster of these.
        new("junkTies", v => $"junk-ties-{v}", 3, JunkKit.Ties, Shape.Bounds, true),
        new("junkRails", v => $"junk-rails-{v}", 2, JunkKit.Rails, Shape.Bounds, true),
        new("junkDrums", v => $"junk-drums-{v}", 3, JunkKit.Drums, Shape.Bounds, true),
        new("junkReel", _ => "junk-reel", 1, (l, _) => JunkKit.Reel(l), Shape.Bounds, true),
        new("junkMilepost", v => $"junk-milepost-{v}", 2, JunkKit.Milepost, Shape.Bounds, true),
        new("junkHut", v => $"junk-hut-{v}", 2, JunkKit.Hut, Shape.Bounds, true),
        new("junkWheelset", _ => "junk-wheelset", 1, (l, _) => JunkKit.Wheelset(l), Shape.Bounds, true),
        new("junkCoal", v => $"junk-coal-{v}", 2, JunkKit.Coal, Shape.Bounds, true),
        new("junkCamp", v => $"junk-camp-{v}", 2, JunkKit.Camp, Shape.Bounds, true),
        new("junkTrolley", _ => "junk-trolley", 1, (l, _) => JunkKit.Trolley(l), Shape.Bounds, true),
        new("junkGrave", v => $"junk-grave-{v}", 2, JunkKit.Grave, Shape.Bounds, true),
        // A dead signal stands on its post: its arm is over a crewmate's head.
        new("deadSignal", _ => "signal-False", 1, (l, _) => WorldKit.Signal(l, lit: false), Shape.Post, true),
        .. new[] { ("heavyCrate", "heavy_crate"), ("storesCrate", "stores_crate"), ("freightTimber", "freight_timber"), ("freightSacks", "freight_sacks"),
            ("freightCarboys", "freight_carboys"), ("sandbags", "sandbags") }.Select(p =>
            new Kind(p.Item1, _ => $"prop-{p.Item2}", 1, (l, _) => Prop(l, p.Item2), Shape.Bounds, true, Modelled: true)),
    ];

    /// <summary>A sourced model the leavings use (content/art/models, PropArt).</summary>
    static MeshAsset Prop(Look? look, string name) =>
        (look is null ? null : PropArt.Of(look).Get(name)) ?? throw new InvalidOperationException($"the {name} model isn't in the content (tools/models)");

    /// <summary>The sim's name for a facility's kit (footprints.json's): its two variants are its left side and its right.</summary>
    public static string FacilityName(Sim.Route.FacilityKind kind) => Sim.Run.StopWalls.FacilityPiece(kind);

    public static readonly IReadOnlyDictionary<string, Kind> ByName = Kinds.ToDictionary(k => k.Name);

    /// <summary>The band a body fills, for <see cref="Shape.Cells"/> (m over the kit's ground): above a step, below the head.</summary>
    const float CellLow = 0.3f, CellHigh = 1.8f;
    /// <summary>The grid <see cref="Shape.Cells"/> lays the drawn pieces on (m): a body's width.</summary>
    const float Cell = 0.5f;
    /// <summary>The most cells a side of one of <see cref="Shape.Cells"/>'s boxes: 4 m.</summary>
    const int CellsMost = 8;
    /// <summary>A post's height: a crewmate's head, about (what's over it, a crossarm, is out of reach).</summary>
    const float PostHigh = 2;
    /// <summary>A foot is what of a frame stands below this, and it stands this tall.</summary>
    const float FootHigh = 2.5f;

    /// <summary>
    /// Every piece's footprint, each variant's boxes as [centre x, centre z, half x, half z, turn, top] in the piece's own
    /// frame (its mesh's: x across, z along, turned about y as the art turns it; the top over its origin).
    /// </summary>
    /// <param name="models">What a facility's kit is measured with: the content's modelled pieces, as the game draws it (the
    /// lineside's own pieces are code, measured with <paramref name="look"/>).</param>
    public static SortedDictionary<string, double[][][]> Measure(Look? look, Look? models = null)
    {
        var all = new SortedDictionary<string, double[][][]>(StringComparer.Ordinal);
        foreach (var kind in Kinds)
            all[kind.Name] = [.. Enumerable.Range(0, kind.Variants).Select(v => Boxes(kind.Make(kind.Shape == Shape.Cells || kind.Modelled ? models ?? look : look, v), kind.Shape))];
        return all;
    }

    static double[][] Boxes(MeshAsset mesh, Shape shape)
    {
        var points = mesh.Vertices.Select(v => v.Position).ToArray();
        float top = points.Max(p => p.Y);
        return shape switch
        {
            Shape.Passable => [],
            Shape.Bounds => [Box(points, top)],
            Shape.Post => [Box([.. points.Where(p => p.Y < PostHigh)], top)],
            Shape.Round => Round(points, top),
            Shape.Feet => [.. Feet(points)],
            Shape.NaveAndTower => NaveAndTower(points, top),
            Shape.Cells => Cells(mesh),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };
    }

    /// <summary>
    /// The tower and steeple stand in front of the nave's gable (NovaKit.Church: the nave from z −6, its roof's eaves out to
    /// −6.3, to its ridge at 11.55): the nave to its ridge, and the tower run back to meet it, so there's no slot between.
    /// </summary>
    static double[][] NaveAndTower(Vector3[] points, float top)
    {
        var nave = Box([.. points.Where(p => p.Z >= -6.35f)], 11.6f);
        var tower = Box([.. points.Where(p => p.Z < -6.35f)], top);
        double front = tower[1] - tower[3], back = nave[1] - nave[3];
        return [nave, [tower[0], Math.Round((front + back) / 2, 2), tower[2], Math.Round((back - front) / 2, 2), 0, tower[5]]];
    }

    static double[] Box(Vector3[] points, float top)
    {
        float x0 = points.Min(p => p.X), x1 = points.Max(p => p.X), z0 = points.Min(p => p.Z), z1 = points.Max(p => p.Z);
        return [R((x0 + x1) / 2), R((z0 + z1) / 2), R((x1 - x0) / 2), R((z1 - z0) / 2), 0, R(top)];
    }

    /// <summary>A drum (SettingKit.Tank: its radius the z extent; its ladder runs up the +x side): two squares round it, a quarter turn apart.</summary>
    static double[][] Round(Vector3[] points, float top)
    {
        float r = MathF.Max(-points.Min(p => p.Z), points.Max(p => p.Z));
        return [[0, 0, R(r), R(r), 0, R(top)], [0, 0, R(r), R(r), R(MathF.PI / 4), R(top)]];
    }

    /// <summary>What stands on the ground under the frame, each foot its own box: points within a metre of each other are one foot.</summary>
    static IEnumerable<double[]> Feet(Vector3[] points)
    {
        var low = points.Where(p => p.Y < FootHigh).ToList();
        var feet = new List<List<Vector3>>();
        foreach (var p in low)
        {
            var near = feet.Where(f => f.Any(q => MathF.Abs(q.X - p.X) < 1 && MathF.Abs(q.Z - p.Z) < 1)).ToList();
            if (near.Count == 0)
                feet.Add([p]);
            else
            {
                near[0].Add(p);
                foreach (var other in near.Skip(1))
                {
                    near[0].AddRange(other);
                    feet.Remove(other);
                }
            }
        }
        return feet.Select(f => Box([.. f], FootHigh)).OrderBy(b => b[0]).ThenBy(b => b[1]);
    }

    /// <summary>
    /// The ground under every triangle of <paramref name="asset"/> where it passes through the band (clipped to it, laid flat:
    /// an upright wall's face is the line of cells along its foot), merged into as few boxes as cover it, in the kit's frame
    /// (x across, z along), each as tall as what stands on it.
    /// </summary>
    static double[][] Cells(MeshAsset asset)
    {
        var cells = new Dictionary<(int X, int Z), float>();
        var v = asset.Vertices;
        Span<Vector3> poly = stackalloc Vector3[9], next = stackalloc Vector3[9];
        for (int t = 0; t + 2 < v.Length; t += 3)
        {
            Vector3 a = v[t].Position, b = v[t + 1].Position, c = v[t + 2].Position;
            float lo = MathF.Min(a.Y, MathF.Min(b.Y, c.Y)), hi = MathF.Max(a.Y, MathF.Max(b.Y, c.Y));
            if (hi < CellLow || lo > CellHigh)
                continue;
            poly[0] = a;
            poly[1] = b;
            poly[2] = c;
            int n = Clip(poly, 3, next, CellLow, above: true);
            n = Clip(next, n, poly, CellHigh, above: false);
            if (n < 2)
                continue;
            float top = hi;
            // Its edges, walked a quarter cell at a time (an upright face lies flat as a line), and the cells whose middles it covers.
            for (int i = 0; i < n; i++)
            {
                var p = poly[i];
                var q = poly[(i + 1) % n];
                int steps = Math.Max(1, (int)MathF.Ceiling(MathF.Sqrt((q.X - p.X) * (q.X - p.X) + (q.Z - p.Z) * (q.Z - p.Z)) / (Cell / 4)));
                for (int s = 0; s <= steps; s++)
                    Mark(cells, Vector3.Lerp(p, q, s / (float)steps), top);
            }
            float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
            for (int i = 0; i < n; i++)
                (x0, x1, z0, z1) = (MathF.Min(x0, poly[i].X), MathF.Max(x1, poly[i].X), MathF.Min(z0, poly[i].Z), MathF.Max(z1, poly[i].Z));
            for (int cx = Index(x0); cx <= Index(x1); cx++)
                for (int cz = Index(z0); cz <= Index(z1); cz++)
                    if (Inside(poly[..n], (cx + 0.5f) * Cell, (cz + 0.5f) * Cell))
                        Raise(cells, (cx, cz), top);
        }
        return Merge(cells);
    }

    static int Index(float m) => (int)MathF.Floor(m / Cell);

    static void Mark(Dictionary<(int, int), float> cells, Vector3 p, float top) => Raise(cells, (Index(p.X), Index(p.Z)), top);

    static void Raise(Dictionary<(int, int), float> cells, (int, int) key, float top) =>
        cells[key] = cells.TryGetValue(key, out var was) ? MathF.Max(was, top) : top;

    /// <summary>The part of a convex polygon above (or below) a height.</summary>
    static int Clip(ReadOnlySpan<Vector3> src, int n, Span<Vector3> dst, float y, bool above)
    {
        int m = 0;
        for (int i = 0; i < n; i++)
        {
            var p = src[i];
            var q = src[(i + 1) % n];
            bool pin = above ? p.Y >= y : p.Y <= y, qin = above ? q.Y >= y : q.Y <= y;
            if (pin)
                dst[m++] = p;
            if (pin != qin)
                dst[m++] = Vector3.Lerp(p, q, (y - p.Y) / (q.Y - p.Y));
        }
        return m;
    }

    static bool Inside(ReadOnlySpan<Vector3> poly, float x, float z)
    {
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            if ((poly[i].Z > z) != (poly[j].Z > z) && x < (poly[j].X - poly[i].X) * (z - poly[i].Z) / (poly[j].Z - poly[i].Z) + poly[i].X)
                inside = !inside;
        return inside;
    }

    /// <summary>
    /// The cells as boxes: each run along z widened across x while the next column's run matches it, none more than
    /// <see cref="CellsMost"/> cells a side, so where a site's module is worked inside a drawn building (StopWalls.Clear) only
    /// the ground round it gives way, not the whole shed.
    /// </summary>
    static double[][] Merge(Dictionary<(int X, int Z), float> cells)
    {
        var left = new HashSet<(int X, int Z)>(cells.Keys);
        var boxes = new List<double[]>();
        foreach (var start in cells.Keys.OrderBy(k => k.X).ThenBy(k => k.Z))
        {
            if (!left.Contains(start))
                continue;
            int z1 = start.Z;
            while (z1 - start.Z + 1 < CellsMost && left.Contains((start.X, z1 + 1)))
                z1++;
            int x1 = start.X;
            while (x1 - start.X + 1 < CellsMost && Enumerable.Range(start.Z, z1 - start.Z + 1).All(z => left.Contains((x1 + 1, z))))
                x1++;
            float top = 0;
            for (int x = start.X; x <= x1; x++)
                for (int z = start.Z; z <= z1; z++)
                {
                    left.Remove((x, z));
                    top = MathF.Max(top, cells[(x, z)]);
                }
            float bx0 = start.X * Cell, bx1 = (x1 + 1) * Cell, bz0 = start.Z * Cell, bz1 = (z1 + 1) * Cell;
            boxes.Add([R((bx0 + bx1) / 2), R((bz0 + bz1) / 2), R((bx1 - bx0) / 2), R((bz1 - bz0) / 2), 0, R(MathF.Ceiling(top * 2) / 2)]);
        }
        return [.. boxes];
    }

    /// <summary>To the centimetre: enough for a wall, and the same file on every machine that writes it.</summary>
    static double R(float v) => Math.Round(v, 2) is var r && r == 0 ? 0 : r;

    /// <summary>content/linegen/footprints.json as <c>dt art footprints --write</c> writes it: a variant to a line.</summary>
    public static string Json(SortedDictionary<string, double[][][]> all)
    {
        static string N(double v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        var sb = new System.Text.StringBuilder();
        sb.Append("// What each kit piece the lineside deals stands on (ARCHITECTURE §8 note 389): measured off the art's meshes by\n");
        sb.Append("// `dt art footprints --write` (src/DarkTerritory.Game/Art/LinesideFootprints.cs), and held to them by\n");
        sb.Append("// LinesideFootprintTests. Don't edit it by hand: change the piece, then write it again.\n");
        sb.Append("// Each variant's boxes, in the piece's own frame (its mesh's: x across, z along the line before it's turned):\n");
        sb.Append("// [centre x, centre z, half x, half z, turn about y (radians), top over its foot]. No boxes: walked through.\n");
        sb.Append("{\n  \"version\": \"1\",\n  \"pieces\": {\n");
        int i = 0;
        foreach (var (name, variants) in all)
        {
            sb.Append($"    \"{name}\": [\n");
            for (int v = 0; v < variants.Length; v++)
                sb.Append("      [").Append(string.Join(", ", variants[v].Select(b => "[" + string.Join(", ", b.Select(N)) + "]")))
                    .Append(v + 1 < variants.Length ? "],\n" : "]\n");
            sb.Append(++i < all.Count ? "    ],\n" : "    ]\n");
        }
        sb.Append("  }\n}\n");
        return sb.ToString();
    }
}
