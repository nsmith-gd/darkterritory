using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// Nothing lies across the track (ARCHITECTURE §8 note 778; the director, 14 Oct, playtest line 19: "Sometimes there's
/// trees lying across the tracks. It doesn't make any sense if they're hitting us or not ... it doesn't seem like there's
/// anything we can do to avoid them either"). The track debris that once lay there (the Sleepers) is retired; nothing the
/// art draws beside a line may reach over its rails, where the train would pass through it untouched.
/// </summary>
public class TrackClearTests
{
    static readonly string Content = DataFile.FindContentRoot();

    /// <summary>How far either side of the rail's centre the train's way is asked to be clear (m): inside the platforms and a bridge's sides.</summary>
    const double HalfWidth = 1.2;
    /// <summary>How high over the track's bed (m; its sleepers' tops at 0.07, the rail heads at 0.19): over the rails, up past a coupler and a car's side.</summary>
    static readonly double[] Heights = [0.25, 0.6, 1.2, 2.0];
    /// <summary>How far either side of the middle no edge may pass through (m): just outside the rails (their heads 0.72 out), inside a check rail's or a platform's reach.</summary>
    const double BoxHalfWidth = 1.0;
    /// <summary>A branch this close to the main line (m) is still on its bed: two sleepers' lengths and a little.</summary>
    const double SharedBedM = 4;
    /// <summary>A brass field's growth thickens over its approach (linegen plan §5's dressing ramp): this far either side of it (m).</summary>
    const double ApproachM = 30;
    /// <summary>A fortress gate's shut leaves (note 847) are asked about only this far either side of it (m): its passage, and the box the train fills on up to it.</summary>
    const double GateClearM = 4.5;

    [Theory]
    [InlineData("local:4")] // a dead line's land up over the main line (3.5 km); the main line's boughs over the dead line (its 262 m)
    [InlineData("deadLines:2")] // a boulder's edge over the rails (6.8 km); boughs (4 km)
    [InlineData("deepTerritory:3")] // the grease's sheen a 113 m chord (5.3 km); a dead line's far land over another (its 766 m); a brass field
    public void NothingLiesAcrossTheMainLineOrABranch(string spec)
    {
        // Every 2 m along the main line and each alternate and dead line, short sight lines across its rails at four
        // heights, and no edge through the box the train fills, in the scene the game builds round an eye on that track
        // (every 300 m, the 150 m either side of it). Each night failed on main's art before note 778, for what its line
        // says; the edges found the deadfall (frontier:7, frontier:3 and deadLines:2 at 1.5 km), which the sight lines didn't.
        var route = Routes.Generate(Content, spec, 6);
        var line = route.Build();
        var tuning = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
        // A Look of its own, let go after: a static one held its art's cells and pieces for the whole run, beside every
        // other class's, and the suite's host ran out of memory.
        var scene = new GreyboxScene { DrawDistance = 400, Look = Look.Load(Content), Route = route, Time = 0.37 };
        var problems = new List<string>();
        int asked = 0;
        var plan = route.Plan!;
        // The fortresses' gates (note 847), drawn shut across the main line until a train comes up to them (and open for it):
        // as the scene stands its forts, with no run (a 600 m yard, a 400 m terminus zone).
        var gates = Sim.Run.Fortresses.Of(route, line, 600, 400).Select(f => f.Gate).ToList();
        var tracks = new List<(string Name, RailLine Track, double Toe, string Edge)> { ("main", line, 0, "main") };
        foreach (var b in line.Branches.Where(b => b.Kind is BranchKind.Alternate or BranchKind.DeadLine))
            tracks.Add(($"b{b.Index}", b.Local, b.Toe, plan.Alignment.First(a => a.Branch == b.Index && a.Role is EdgeRole.Alternate or EdgeRole.DeadLine).Edge));
        // Every track's points (the main line, and its branches of every kind: a spur's and a siding's too), to know where
        // another shares a track's bed.
        var beds = new Dictionary<(int, int), List<(RailLine Track, Double3 At)>>();
        foreach (var t in new[] { line }.Concat(line.Branches.Select(b => b.Local)))
            for (double s = 0; s <= t.Length; s += 2)
            {
                var p = t.Sample(s).Position;
                var cell = ((int)Math.Floor(p.X / SharedBedM), (int)Math.Floor(p.Z / SharedBedM));
                if (!beds.TryGetValue(cell, out var list))
                    beds[cell] = list = [];
                list.Add((t, p));
            }
        bool SharesABed(RailLine self, Double3 p)
        {
            int cx = (int)Math.Floor(p.X / SharedBedM), cz = (int)Math.Floor(p.Z / SharedBedM);
            for (int x = cx - 1; x <= cx + 1; x++)
                for (int z = cz - 1; z <= cz + 1; z++)
                    if (beds.TryGetValue((x, z), out var list))
                        foreach (var (t, at) in list)
                            if (!ReferenceEquals(t, self) && double.Hypot(at.X - p.X, at.Z - p.Z) < SharedBedM)
                                return true;
            return false;
        }
        foreach (var (name, track, toe, edge) in tracks)
            for (double centre = 150; centre < track.Length; centre += 300)
            {
                var at = track.Sample(centre);
                var eye = at.Position + Double3.Up * 3;
                double hint = toe + centre;
                line.Nearest(at.Position, ref hint);
                // The train well away from what's asked, on the main line.
                double trainAt = hint > 900 ? hint - 700 : Math.Min(line.Length - 60, hint + 700);
                var mesh = new MeshBuilder();
                scene.Build(mesh, new TrainOnLine(new TrainDynamics(Consist.Uniform(tuning, 6, 1)), line, Math.Clamp(trainAt, 60, line.Length - 60)), eye);
                double first = Math.Max(4, centre - 150), last = Math.Min(track.Length - 4, centre + 150);
                var points = new List<Vector3>();
                for (double s = first; s <= last + 2; s += 2)
                    points.Add(track.Sample(Math.Min(s, track.Length)).Position.RelativeTo(eye));
                var grid = new TriangleGrid(mesh, points);
                for (double s = first; s < last; s += 2)
                {
                    var q = track.Sample(s);
                    // Not where another track shares this one's bed, at its points and on through a turnout's long split
                    // (frontier:7's dead line is within 3 m of the main line for its first 300 m): one's sleepers lie in the
                    // other's ballast there, as at any set of points. Nor on a brass field (linegen plan §5: "growth across
                    // the rail", cut through at the cutting speed) or a washout: those are meant to be in the way.
                    if (SharesABed(track, q.Position)
                        || plan.Structures.Any(st => st.Edge == edge && st.Type is StructureType.BrassField or StructureType.Washout && s > st.S0 - ApproachM && s < st.S1 + ApproachM)
                        || name == "main" && gates.Any(g => Math.Abs(s - g) < GateClearM))
                        continue;
                    var right = Double3.Cross(q.Tangent, Double3.Up).Normalized;
                    foreach (double up in Heights)
                    {
                        var a = (q.Position - right * HalfWidth + Double3.Up * up).RelativeTo(eye);
                        var b = (q.Position + right * HalfWidth + Double3.Up * up).RelativeTo(eye);
                        asked++;
                        if (grid.FirstHit(a, b) is { } hit)
                            problems.Add($"{name} at {s:0}, {up} m up: something across the rails (a triangle {hit.Size:0.0} m across, centred {hit.Off:0.0} m off the rail)");
                    }
                    // And anything thin lying in the train's way through the 2 m on (a trunk square across the track slips
                    // between the sight lines across, 2 m apart, and over or under their heights): no edge of any triangle
                    // passes through the box the train fills over the rails.
                    if (s + 2 < track.Length - 4 && !plan.Structures.Any(st => st.Edge == edge && st.Type is StructureType.BufferStop && s + 2 > st.S0 - 1)
                        && grid.Through(q.Position.RelativeTo(eye), right, q.Tangent, 2, BoxHalfWidth, Heights[0], Heights[^1]) is { } edgeIn)
                        problems.Add($"{name} at {s:0}: something lying in the train's way ({edgeIn.Length:0.0} m long, {edgeIn.Up:0.00} m up, {edgeIn.Across:0.00} m off the middle)");
                }
            }
        Assert.True(asked > 1000, $"{spec}: only {asked} sight lines");
        Assert.True(problems.Count == 0, $"{spec}, {problems.Count} of {asked} sight lines across the rails hit something:\n" + string.Join("\n", problems));
    }

    /// <summary>
    /// A scene's triangles near a stretch of track, binned by where they lie on the ground plan, so a short sight line asks
    /// only those near it. Only the bins along the track are kept: the whole scene's triangles, 400 m round, held at once
    /// beside the suite's other scenes, ran its host out of memory.
    /// </summary>
    sealed class TriangleGrid
    {
        const float Cell = 4;
        readonly List<(Vector3 A, Vector3 B, Vector3 C)> _tris = [];
        readonly Dictionary<(int, int), List<int>> _cells = new();
        readonly HashSet<(int, int)> _keep = [];

        /// <param name="along">The track's points (relative to the eye): a bin either side of each is kept.</param>
        public TriangleGrid(MeshBuilder mesh, IEnumerable<Vector3> along)
        {
            foreach (var p in along)
                for (int x = Bin(p.X) - 1; x <= Bin(p.X) + 1; x++)
                    for (int z = Bin(p.Z) - 1; z <= Bin(p.Z) + 1; z++)
                        _keep.Add((x, z));
            var v = mesh.Vertices;
            for (int t = 0; t + 2 < v.Length; t += 3)
                Add(v[t].Position, v[t + 1].Position, v[t + 2].Position);
            foreach (var i in mesh.Instances)
            {
                // A piece whose sphere is nowhere near the track is passed over whole.
                var (centre, radius) = i.Asset.Bounds;
                var c = Vector3.Transform(centre, i.Model);
                float scale = MathF.Max(new Vector3(i.Model.M11, i.Model.M12, i.Model.M13).Length(),
                    MathF.Max(new Vector3(i.Model.M21, i.Model.M22, i.Model.M23).Length(), new Vector3(i.Model.M31, i.Model.M32, i.Model.M33).Length()));
                if (!Touches(c.X - radius * scale, c.X + radius * scale, c.Z - radius * scale, c.Z + radius * scale))
                    continue;
                var w = i.Asset.Vertices;
                for (int t = 0; t + 2 < w.Length; t += 3)
                    Add(Vector3.Transform(w[t].Position, i.Model), Vector3.Transform(w[t + 1].Position, i.Model), Vector3.Transform(w[t + 2].Position, i.Model));
            }
        }

        bool Touches(float minX, float maxX, float minZ, float maxZ)
        {
            int x0 = Bin(minX), x1 = Bin(maxX), z0 = Bin(minZ), z1 = Bin(maxZ);
            if ((long)(x1 - x0 + 1) * (z1 - z0 + 1) > _keep.Count)
                return _keep.Any(k => k.Item1 >= x0 && k.Item1 <= x1 && k.Item2 >= z0 && k.Item2 <= z1);
            for (int x = x0; x <= x1; x++)
                for (int z = z0; z <= z1; z++)
                    if (_keep.Contains((x, z)))
                        return true;
            return false;
        }

        void Add(Vector3 a, Vector3 b, Vector3 c)
        {
            int x0 = Bin(MathF.Min(a.X, MathF.Min(b.X, c.X))), x1 = Bin(MathF.Max(a.X, MathF.Max(b.X, c.X)));
            int z0 = Bin(MathF.Min(a.Z, MathF.Min(b.Z, c.Z))), z1 = Bin(MathF.Max(a.Z, MathF.Max(b.Z, c.Z)));
            // A far-land quad spans hundreds of metres; it isn't what lies across rails, and binning it would fill the grid.
            if ((long)(x1 - x0 + 1) * (z1 - z0 + 1) > 400)
                return;
            int index = -1;
            for (int x = x0; x <= x1; x++)
                for (int z = z0; z <= z1; z++)
                {
                    if (!_keep.Contains((x, z)))
                        continue;
                    if (index < 0)
                    {
                        index = _tris.Count;
                        _tris.Add((a, b, c));
                    }
                    if (!_cells.TryGetValue((x, z), out var list))
                        _cells[(x, z)] = list = [];
                    list.Add(index);
                }
        }

        static int Bin(float v) => (int)MathF.Floor(v / Cell);

        /// <summary>The first triangle the segment passes through, if any: how big it is, and how far its centre is off the segment's middle across it.</summary>
        public (float Size, float Off)? FirstHit(Vector3 from, Vector3 to)
        {
            var seen = new HashSet<int>();
            int x0 = Bin(MathF.Min(from.X, to.X)), x1 = Bin(MathF.Max(from.X, to.X)), z0 = Bin(MathF.Min(from.Z, to.Z)), z1 = Bin(MathF.Max(from.Z, to.Z));
            for (int x = x0; x <= x1; x++)
                for (int z = z0; z <= z1; z++)
                    if (_cells.TryGetValue((x, z), out var list))
                        foreach (int i in list)
                            if (seen.Add(i) && TunnelArtTests.Hits([_tris[i]], from, to))
                            {
                                var (a, b, c) = _tris[i];
                                var mid = (from + to) / 2;
                                var centre = (a + b + c) / 3;
                                float size = MathF.Max((b - a).Length(), MathF.Max((c - b).Length(), (a - c).Length()));
                                return (size, new Vector2(centre.X - mid.X, centre.Z - mid.Z).Length());
                            }
            return null;
        }

        /// <summary>
        /// The first triangle edge that passes through the box from <paramref name="at"/> on along <paramref name="along"/>
        /// for <paramref name="length"/>, <paramref name="halfWidth"/> either side and from <paramref name="low"/> to
        /// <paramref name="high"/> up: how long the edge is, and where it's in the box (its height, how far off the middle).
        /// </summary>
        public (float Length, float Up, float Across)? Through(Vector3 at, Double3 right, Double3 along, double length, double halfWidth, double low, double high)
        {
            Vector3 r = new((float)right.X, (float)right.Y, (float)right.Z), f = new((float)along.X, (float)along.Y, (float)along.Z);
            float w = (float)halfWidth, l = (float)length;
            var corners = new[] { at - r * w, at + r * w, at - r * w + f * l, at + r * w + f * l };
            int x0 = Bin(corners.Min(c => c.X)), x1 = Bin(corners.Max(c => c.X)), z0 = Bin(corners.Min(c => c.Z)), z1 = Bin(corners.Max(c => c.Z));
            var seen = new HashSet<int>();
            // In the track's frame: across (right), up, along.
            Vector3 Local(Vector3 p) => new(Vector3.Dot(p - at, r), (p - at).Y, Vector3.Dot(p - at, f));
            var lo = new Vector3(-w, (float)low, 0);
            var hi = new Vector3(w, (float)high, l);
            for (int x = x0; x <= x1; x++)
                for (int z = z0; z <= z1; z++)
                    if (_cells.TryGetValue((x, z), out var list))
                        foreach (int i in list)
                        {
                            if (!seen.Add(i))
                                continue;
                            var (a, b, c) = _tris[i];
                            var (la, lb, lc) = (Local(a), Local(b), Local(c));
                            foreach (var (p, q) in new[] { (la, lb), (lb, lc), (lc, la) })
                                if (Enters(p, q, lo, hi) is { } t)
                                {
                                    var inside = Vector3.Lerp(p, q, t);
                                    return ((q - p).Length(), inside.Y, inside.X);
                                }
                        }
            return null;
        }

        /// <summary>Where along the segment from <paramref name="p"/> to <paramref name="q"/> (0-1) it's first inside the box, if it ever is (the slab test).</summary>
        static float? Enters(Vector3 p, Vector3 q, Vector3 lo, Vector3 hi)
        {
            float t0 = 0, t1 = 1;
            var d = q - p;
            for (int k = 0; k < 3; k++)
            {
                float o = k == 0 ? p.X : k == 1 ? p.Y : p.Z, v = k == 0 ? d.X : k == 1 ? d.Y : d.Z;
                float a = k == 0 ? lo.X : k == 1 ? lo.Y : lo.Z, b = k == 0 ? hi.X : k == 1 ? hi.Y : hi.Z;
                if (MathF.Abs(v) < 1e-9f)
                {
                    if (o < a || o > b)
                        return null;
                    continue;
                }
                float ta = (a - o) / v, tb = (b - o) / v;
                t0 = MathF.Max(t0, MathF.Min(ta, tb));
                t1 = MathF.Min(t1, MathF.Max(ta, tb));
                if (t0 > t1)
                    return null;
            }
            return t0;
        }
    }
}
