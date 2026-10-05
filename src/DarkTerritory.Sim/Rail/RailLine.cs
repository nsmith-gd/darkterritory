using Ballast;

namespace DarkTerritory.Sim.Rail;

/// <summary>
/// One piece of track described the way railways are surveyed: a length, a curve and a grade.
/// This is also what the procedural line generator (GDD §22) emits.
/// </summary>
/// <param name="Length">Metres along the track.</param>
/// <param name="Radius">Curve radius in metres; 0 is straight, positive curves left, negative right.</param>
/// <param name="GradePercent">Rise per 100 m of run. Positive climbs in the direction of increasing distance.</param>
/// <remarks>
/// A piece can also change along its length (the line generator's transitions, linegen plan §8): curvature from
/// <see cref="Radius"/> to <see cref="EndRadius"/> linearly with distance is a clothoid (Euler spiral) easing into or out
/// of a curve, and grade from <see cref="GradePercent"/> to <see cref="EndGradePercent"/> is a parabolic vertical curve.
/// Unset, a piece is the same all along, as hand-laid lines have always been.
/// </remarks>
public sealed record TrackSegment(double Length, double Radius = 0, double GradePercent = 0)
{
    /// <summary>The radius at the far end (0 straight); unset, <see cref="Radius"/> all along.</summary>
    public double? EndRadius { get; init; }
    /// <summary>The grade at the far end; unset, <see cref="GradePercent"/> all along.</summary>
    public double? EndGradePercent { get; init; }

    public double Curvature => Radius == 0 ? 0 : 1 / Radius;
    public double EndCurvature => EndRadius is { } r ? r == 0 ? 0 : 1 / r : Curvature;
    public double EndGrade => EndGradePercent ?? GradePercent;

    /// <summary>Curvature <paramref name="along"/> metres into the piece.</summary>
    public double CurvatureAt(double along) => EndRadius is null ? Curvature : Curvature + (EndCurvature - Curvature) * Math.Clamp(along / Length, 0, 1);

    /// <summary>Grade (percent) <paramref name="along"/> metres into the piece.</summary>
    public double GradeAt(double along) => EndGradePercent is not { } end ? GradePercent : GradePercent + (end - GradePercent) * Math.Clamp(along / Length, 0, 1);
}

/// <summary>Authoring format for content/lines/*.json.</summary>
public sealed record LineDefinition(string Name, IReadOnlyList<TrackSegment> Segments)
{
    public double StartHeadingDegrees { get; init; }
}

public enum BranchKind : byte
{
    /// <summary>A disused line off into the Territory, ending at a buffer stop (what the Switchman sets you onto, App. A.7).</summary>
    DeadLine,
    /// <summary>A facility's siding (GDD §17): where the empties go to be loaded.</summary>
    Spur,
    /// <summary>
    /// An alternate route (linegen plan §6.2): leaves the main line at a facing switch and rejoins it further on through a
    /// trailing (spring) switch that needs no crew. It is the choice the route card offers: a high line, a low line.
    /// </summary>
    Alternate,
}

/// <summary>A branch off the main line at a hand-thrown switch (GDD §17), generated with the route.</summary>
/// <param name="Toe">Main-line distance of the switch points. A branch leaves facing a train running up the line.</param>
/// <param name="Side">−1 left, +1 right of the direction of travel.</param>
/// <param name="Segments">The branch from the points to its buffer stop, starting along the main line's heading there.</param>
public sealed record BranchDefinition(BranchKind Kind, double Toe, int Side, IReadOnlyList<TrackSegment> Segments)
{
    public double Length => Segments.Sum(s => s.Length);

    /// <summary>
    /// For a generated yard's track (level-design P16): the metres back from its buffer stop that cars stand on to be
    /// worked, past the tight S-curve out from the main line. Unset, the whole branch.
    /// </summary>
    public double? Standing { get; init; }
    /// <summary>An alternate's end: the main-line distance where it comes back in. Unset for a branch that ends at a buffer stop.</summary>
    public double? Rejoin { get; init; }
    /// <summary>The switch starts set for the branch (the main line past it is closed, linegen plan §6.3).</summary>
    public bool StartsDiverging { get; init; }
}

/// <summary>
/// What the land and the rail are like along a line, where a generated line says (linegen plan §12, §14): the ground
/// people and bodies stand on, the rail's adhesion, and anything dragging at a train. A hand-laid line has none: flat
/// ground at rail height and dry rail.
/// </summary>
public interface ITrackConditions
{
    /// <summary>The height of the ground under a world point.</summary>
    double Ground(Double3 world);
    /// <summary>Adhesion (1 dry) at a distance along a path.</summary>
    double Adhesion(int path, double distance);
    /// <summary>A deceleration (m/s²) the track puts on a train at <paramref name="speed"/> there (brass across the rail).</summary>
    double Drag(int path, double distance, double speed);
    /// <summary>How deep the cold is there (GDD §22 "deep cold"; linegen plan §14's cold steps, 0 a normal night; note 183).</summary>
    int ColdStep(int path, double distance) => 0;
    /// <summary>How exposed to the wind it is there (GDD §22 "wind"; 0 sheltered, 1 a normal night's wind; note 183).</summary>
    double Wind(int path, double distance) => 0;
}

/// <summary>A built branch: its own line, laid from the main line's points onwards.</summary>
public sealed class Branch
{
    internal Branch(int index, BranchDefinition def, RailLine main)
    {
        Index = index;
        Definition = def;
        var at = main.Sample(def.Toe);
        double heading = DMath.Atan2(-at.Tangent.X, -at.Tangent.Z) * 180 / Math.PI;
        Local = new RailLine(new LineDefinition($"{main.Name}/{index}", def.Segments) { StartHeadingDegrees = heading }, at.Position);
    }

    public int Index { get; }
    public BranchDefinition Definition { get; }
    public BranchKind Kind => Definition.Kind;
    public double Toe => Definition.Toe;
    public int Side => Definition.Side;
    /// <summary>The branch alone, distance 0 at the points.</summary>
    public RailLine Local { get; }
    /// <summary>Path distance of the far end: a buffer stop, or where an alternate comes back onto the main line.</summary>
    public double End => Toe + Local.Length;
    /// <summary>An alternate, which comes back onto the main line rather than ending.</summary>
    public bool Rejoins => Definition.Rejoin is not null;
    /// <summary>Main-line distance where an alternate comes back in (its trailing switch); <see cref="End"/> otherwise.</summary>
    public double Rejoin => Definition.Rejoin ?? End;
    /// <summary>
    /// What to add to a distance along this alternate's path, past its end, to get the main-line distance there: the
    /// alternate is shorter (negative) or longer (positive) than the stretch of main line it bypasses.
    /// </summary>
    public double Offset => Rejoin - End;
}

/// <summary>A point on the line: where it is, which way it runs, and the track conditions there.</summary>
public readonly record struct TrackSample(double Distance, Double3 Position, Double3 Tangent, double GradePercent, double Curvature);

/// <summary>
/// A stretch of one path on one physical piece of track (the main line, or a branch's own track). A distance along the
/// path between <see cref="From"/> and <see cref="To"/> is <see cref="Shift"/> less than the piece's own distance there.
/// </summary>
/// <param name="Piece"><see cref="RailLine.MainPath"/> for the main line's track, else the branch's index.</param>
public readonly record struct PathSpan(int Piece, double From, double To, double Shift);

/// <summary>
/// A built rail line. Segments are integrated once into a fine polyline so lookups by
/// distance are cheap and identical on host and clients.
/// <para>
/// Paths: a rake runs on the main line (<see cref="MainPath"/>), or on a branch taken at its points (the branch's index:
/// the main line up to the points, the branch after). An alternate's path carries on along the main line past where it
/// rejoins. Once a rake's front is back on the main line it is addressed in main-line distance, on the path
/// <see cref="ViaPath"/> of the alternate, until its tail has come off the alternate too.
/// </para>
/// </summary>
public sealed class RailLine
{
    /// <summary>Sampling step when integrating segments. 1 m keeps curve chord error under 1 mm at 150 m radius.</summary>
    public const double Step = 1.0;

    readonly Double3[] _points;
    readonly double[] _grade;
    readonly double[] _curvature;
    readonly Branch[] _branches;
    TrackIndex? _index;

    /// <summary>The path of a rake on the main line; a branch's index is the path onto it.</summary>
    public const int MainPath = -1;

    /// <summary>Main-line distance, for a rake whose tail is still on alternate <paramref name="alternate"/>.</summary>
    public static int ViaPath(int alternate) => -2 - alternate;
    /// <summary>The alternate a <see cref="ViaPath"/> came off, or −1 for any other path.</summary>
    public static int ViaOf(int path) => path <= -2 ? -2 - path : -1;

    public RailLine(LineDefinition def) : this(def, Double3.Zero)
    {
    }

    /// <summary>A main line with branches off it at switches (a route's, from <c>Route.Build</c>).</summary>
    public RailLine(LineDefinition def, IReadOnlyList<BranchDefinition> branches) : this(def, Double3.Zero)
    {
        _branches = new Branch[branches.Count];
        for (int i = 0; i < branches.Count; i++)
        {
            if (branches[i].Toe <= 0 || branches[i].Toe >= Length)
                throw new ArgumentException($"branch {i}'s points at {branches[i].Toe} m aren't on the line", nameof(branches));
            if (branches[i].Rejoin is { } j && (j <= branches[i].Toe || j >= Length))
                throw new ArgumentException($"alternate {i} rejoins at {j} m, not on the line past its points", nameof(branches));
            _branches[i] = new Branch(i, branches[i], this);
        }
    }

    internal RailLine(LineDefinition def, Double3 origin)
    {
        _branches = [];
        if (def.Segments.Count == 0)
            throw new ArgumentException("line has no segments", nameof(def));
        Name = def.Name;
        Segments = def.Segments;
        Length = def.Segments.Sum(s => s.Length);

        // A line a hair over a whole number of steps would end on a step too short to have a direction.
        int n = (int)Math.Ceiling(Length / Step - 1e-9) + 1;
        _points = new Double3[n];
        _grade = new double[n];
        _curvature = new double[n];
        Integrate(def, origin, Length, _points, _grade, _curvature);
    }

    /// <summary>
    /// Lays the segments out from <paramref name="origin"/>, a sample every <see cref="Step"/> metres. Heading 0 runs along
    /// −Z (engine forward); positive heading turns left (counter-clockwise from above). The line generator lays its lines
    /// with this too, so what it plans is exactly what gets built.
    /// </summary>
    public static void Integrate(LineDefinition def, Double3 origin, double length, Double3[] points, double[] grade, double[] curvature)
    {
        int n = points.Length;
        double heading = def.StartHeadingDegrees * Math.PI / 180;
        var pos = origin;
        int seg = 0, last = def.Segments.Count - 1;
        double segStart = 0;
        for (int i = 0; i < n; i++)
        {
            double s = Math.Min(i * Step, length);
            // The segment a sample is in; on a boundary, the one starting there.
            while (seg < last && s >= segStart + def.Segments[seg].Length)
                segStart += def.Segments[seg++].Length;
            var segment = def.Segments[seg];
            points[i] = pos;
            grade[i] = segment.GradeAt(s - segStart);
            curvature[i] = segment.CurvatureAt(s - segStart);

            if (i == n - 1)
                break;
            // On to the next sample through each segment in between, with its own curve and grade, so a boundary is
            // exact wherever it falls (a turnout's out-and-back curves have to come out parallel). Midpoint
            // integration of heading keeps curves closing correctly; a changing curvature or grade is taken at the
            // middle of each piece of the step (exact for the heading, since curvature changes linearly).
            double end = Math.Min(s + Step, length), at = s, pieceStart = segStart;
            for (int k = seg; at < end;)
            {
                var piece = def.Segments[k];
                double pieceEnd = k < last ? Math.Min(end, pieceStart + piece.Length) : end;
                double ds = pieceEnd - at;
                double u0 = at - pieceStart, u1 = pieceEnd - pieceStart;
                double k0 = piece.CurvatureAt(u0), k1 = piece.CurvatureAt(u1), km = piece.CurvatureAt((u0 + u1) / 2);
                double g = piece.GradeAt((u0 + u1) / 2) / 100;
                double midHeading = heading + (k0 + km) / 2 * ds / 2;
                double horizontal = ds / Math.Sqrt(1 + g * g);
                pos += new Double3(-DMath.Sin(midHeading) * horizontal, horizontal * g, -DMath.Cos(midHeading) * horizontal);
                heading += (k0 + k1) / 2 * ds;
                at = pieceEnd;
                if (k < last && at >= pieceStart + piece.Length)
                    pieceStart += def.Segments[k++].Length;
            }
        }
    }

    public string Name { get; }
    public double Length { get; }
    public IReadOnlyList<TrackSegment> Segments { get; }
    /// <summary>The land and rail conditions along it, for a generated line (null: flat ground at rail height, dry rail).</summary>
    public ITrackConditions? Conditions { get; set; }

    /// <summary>Samples the line at a distance, clamped to its ends.</summary>
    public TrackSample Sample(double distance)
    {
        double s = Math.Clamp(distance, 0, Length);
        double f = s / Step;
        int i = Math.Min((int)f, _points.Length - 2);
        // The final step is usually shorter than Step, so normalise by its real length.
        double t = (s - i * Step) / (Math.Min((i + 1) * Step, Length) - i * Step);
        var a = _points[i];
        var b = _points[i + 1];
        int nearest = t < 0.5 ? i : i + 1;
        var d = b - a;
        if (d.Length < 1e-9 && i > 0)
            d = a - _points[i - 1];
        return new TrackSample(s, Double3.Lerp(a, b, t), d.Normalized, _grade[nearest], _curvature[nearest]);
    }

    public static RailLine Load(string path) => new(DataFile.Load<LineDefinition>(path));

    /// <summary>The branches off this line, by index (a rake's path onto one is its index).</summary>
    public IReadOnlyList<Branch> Branches => _branches;

    /// <summary>
    /// Samples a path: the main line, or the main line up to a branch's points and the branch beyond them. The two
    /// agree up to the points, so a rake on the shared track is in the same place whichever path it's on.
    /// </summary>
    public TrackSample Sample(int path, double distance)
    {
        if (path == MainPath)
            return Sample(distance);
        if (path <= -2)
        {
            var alt = _branches[ViaOf(path)];
            return distance >= alt.Rejoin ? Sample(distance) : Sample(alt.Index, distance - alt.Offset) with { Distance = distance };
        }
        var b = _branches[path];
        if (distance <= b.Toe)
            return Sample(distance);
        if (b.Rejoins && distance > b.End)
            return Sample(distance + b.Offset) with { Distance = distance };
        var local = b.Local.Sample(distance - b.Toe);
        return local with { Distance = distance };
    }

    /// <summary>Where a path ends: the main line's length, a branch's buffer stop, or the main line's end past an alternate.</summary>
    public double PathLength(int path) => path switch
    {
        MainPath => Length,
        <= -2 => Length,
        _ => _branches[path].Rejoins ? Length - _branches[path].Offset : _branches[path].End,
    };

    /// <summary>True when <paramref name="distance"/> on <paramref name="path"/> is on the main line's own track.</summary>
    public bool OnMain(int path, double distance) => !double.IsNaN(MainDistance(path, distance));

    /// <summary>The main-line distance of a point on a path, or NaN where the path is off on a branch's own track.</summary>
    public double MainDistance(int path, double distance)
    {
        if (path == MainPath)
            return distance;
        if (path <= -2)
        {
            var alt = _branches[ViaOf(path)];
            return distance >= alt.Rejoin ? distance : MainDistance(alt.Index, distance - alt.Offset);
        }
        var b = _branches[path];
        if (distance <= b.Toe)
            return distance;
        return b.Rejoins && distance >= b.End ? distance + b.Offset : double.NaN;
    }

    /// <summary>How far two paths run together from the start of the line.</summary>
    public double Shared(int a, int b)
    {
        static int Home(int p) => p <= -2 ? MainPath : p;
        a = Home(a);
        b = Home(b);
        return a == b ? PathLength(a) : a < 0 ? _branches[b].Toe : b < 0 ? _branches[a].Toe : Math.Min(_branches[a].Toe, _branches[b].Toe);
    }

    PathSpan[][]? _spans;

    /// <summary>The physical track a path runs over, in order along it (see <see cref="PathSpan"/>).</summary>
    public IReadOnlyList<PathSpan> Spans(int path)
    {
        // Cached by path: main, each branch, then each branch's via path.
        _spans ??= [.. Enumerable.Range(-1, 1 + 2 * _branches.Length).Select(i => BuildSpans(i < _branches.Length ? i : ViaPath(i - _branches.Length)))];
        return _spans[path == MainPath ? 0 : path >= 0 ? path + 1 : 1 + _branches.Length + ViaOf(path)];
    }

    PathSpan[] BuildSpans(int path)
    {
        if (path == MainPath)
            return [new PathSpan(MainPath, 0, Length, 0)];
        if (path <= -2)
        {
            var alt = _branches[ViaOf(path)];
            double o = alt.Offset;
            return
            [
                new PathSpan(MainPath, o, alt.Toe + o, -o),
                new PathSpan(alt.Index, alt.Toe + o, alt.Rejoin, -alt.Toe - o),
                new PathSpan(MainPath, alt.Rejoin, Length, 0),
            ];
        }
        var b = _branches[path];
        return b.Rejoins
            ? [new PathSpan(MainPath, 0, b.Toe, 0), new PathSpan(b.Index, b.Toe, b.End, -b.Toe), new PathSpan(MainPath, b.End, PathLength(path), b.Offset)]
            : [new PathSpan(MainPath, 0, b.Toe, 0), new PathSpan(b.Index, b.Toe, b.End, -b.Toe)];
    }

    /// <summary>
    /// The track nearest a world point, refining a hint along the main line: the main line, or a branch if one is
    /// nearer. Returns the path and the distance along it. The hint is left at the nearest point of the main line.
    /// </summary>
    public (int Path, double Distance) Nearest(Double3 world, ref double hint)
    {
        _index ??= new TrackIndex(this);
        // The main line from the hint, as a person walking along it would have it; the index finds it afresh if
        // the hint has been left far behind (a walk out along a branch).
        double main = Project(this, world, hint);
        if (_index.NearestOn(MainPath, world) is { } seed && Flat(Sample(seed).Position - world) + 1 < Flat(Sample(main).Position - world))
            main = Project(this, world, seed);
        hint = main;
        var best = (Path: MainPath, Distance: main);
        double bestD = Flat(Sample(main).Position - world);
        foreach (var b in _branches)
        {
            // A spur or a dead line is near only when its points are within its own length of here (as it always was);
            // an alternate can loop out of sight of the main line, so the index says whether it's near.
            if (!b.Rejoins && (main < b.Toe - 50 || main > b.End + 50) || b.Rejoins && !_index.Near(b.Index, world))
                continue;
            double guess = b.Rejoins ? _index.NearestOn(b.Index, world) ?? Math.Clamp(main - b.Toe, 0, b.Local.Length) : Math.Clamp(main - b.Toe, 0, b.Local.Length);
            double along = Project(b.Local, world, guess);
            double d = Flat(b.Local.Sample(along).Position - world);
            // Only its own track: at its points it is the main line, and an alternate is the main line again where it rejoins.
            if (along > 0 && (!b.Rejoins || along < b.Local.Length) && d < bestD)
            {
                bestD = d;
                best = (b.Index, b.Toe + along);
            }
        }
        return best;
    }

    static double Flat(Double3 v) => v.X * v.X + v.Z * v.Z;

    static double Project(RailLine line, Double3 world, double hint)
    {
        for (int i = 0; i < 3; i++)
        {
            var sample = line.Sample(hint);
            hint = Math.Clamp(sample.Distance + Double3.Dot(world - sample.Position, sample.Tangent), 0, line.Length);
        }
        return hint;
    }

    /// <summary>
    /// A coarse grid of points along every piece of track, for finding which track is near a point without walking the
    /// whole line (a branch kilometres long, an alternate looping out of sight of the main line). Built on first use.
    /// </summary>
    sealed class TrackIndex
    {
        const double Cell = 32, Pitch = 4;
        // Pieces searched this far out: past it, the nearest track is found from the hint as before.
        const int Rings = 10;
        readonly Dictionary<long, List<(int Piece, double S, double X, double Z)>> _cells = new();
        readonly (double MinX, double MinZ, double MaxX, double MaxZ)[] _bounds;

        public TrackIndex(RailLine line)
        {
            Add(MainPath, line);
            _bounds = new (double, double, double, double)[line._branches.Length];
            foreach (var b in line._branches)
            {
                Add(b.Index, b.Local);
                double minX = double.MaxValue, minZ = double.MaxValue, maxX = double.MinValue, maxZ = double.MinValue;
                for (double s = 0; s <= b.Local.Length; s += Pitch)
                {
                    var p = b.Local.Sample(s).Position;
                    (minX, minZ, maxX, maxZ) = (Math.Min(minX, p.X), Math.Min(minZ, p.Z), Math.Max(maxX, p.X), Math.Max(maxZ, p.Z));
                }
                _bounds[b.Index] = (minX, minZ, maxX, maxZ);
            }
        }

        static long Key(long x, long z) => (x << 32) ^ (z & 0xFFFFFFFF);

        void Add(int piece, RailLine line)
        {
            for (double s = 0; s <= line.Length + Pitch - 1e-9; s += Pitch)
            {
                double at = Math.Min(s, line.Length);
                var p = line.Sample(at).Position;
                long key = Key((long)Math.Floor(p.X / Cell), (long)Math.Floor(p.Z / Cell));
                if (!_cells.TryGetValue(key, out var list))
                    _cells[key] = list = new();
                list.Add((piece, at, p.X, p.Z));
            }
        }

        /// <summary>Within 60 m of a branch's extent: worth projecting onto.</summary>
        public bool Near(int branch, Double3 p)
        {
            var (minX, minZ, maxX, maxZ) = _bounds[branch];
            return p.X > minX - 60 && p.X < maxX + 60 && p.Z > minZ - 60 && p.Z < maxZ + 60;
        }

        /// <summary>The distance along a piece of its indexed point nearest <paramref name="p"/>, searching outward ring by ring.</summary>
        public double? NearestOn(int piece, Double3 p)
        {
            long cx = (long)Math.Floor(p.X / Cell), cz = (long)Math.Floor(p.Z / Cell);
            double best = double.MaxValue;
            double? found = null;
            for (int ring = 0; ring <= Rings; ring++)
            {
                for (long x = cx - ring; x <= cx + ring; x++)
                    for (long z = cz - ring; z <= cz + ring; z++)
                    {
                        if (Math.Max(Math.Abs(x - cx), Math.Abs(z - cz)) != ring || !_cells.TryGetValue(Key(x, z), out var list))
                            continue;
                        foreach (var (pc, s, px, pz) in list)
                        {
                            if (pc != piece)
                                continue;
                            double d = (px - p.X) * (px - p.X) + (pz - p.Z) * (pz - p.Z);
                            if (d < best || d == best && s < found)
                                (best, found) = (d, s);
                        }
                    }
                // Anything in a further ring is at least this far off.
                if (found is not null && Math.Sqrt(best) < ring * Cell)
                    break;
            }
            return found;
        }
    }
}
