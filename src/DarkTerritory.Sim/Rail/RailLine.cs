using Ballast;

namespace DarkTerritory.Sim.Rail;

/// <summary>
/// One piece of track described the way railways are surveyed: a length, a curve and a grade.
/// This is also what the procedural line generator (GDD §22) emits.
/// </summary>
/// <param name="Length">Metres along the track.</param>
/// <param name="Radius">Curve radius in metres; 0 is straight, positive curves left, negative right.</param>
/// <param name="GradePercent">Rise per 100 m of run. Positive climbs in the direction of increasing distance.</param>
public sealed record TrackSegment(double Length, double Radius = 0, double GradePercent = 0)
{
    public double Curvature => Radius == 0 ? 0 : 1 / Radius;
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
}

/// <summary>A built branch: its own line, laid from the main line's points onwards.</summary>
public sealed class Branch
{
    internal Branch(int index, BranchDefinition def, RailLine main)
    {
        Index = index;
        Definition = def;
        var at = main.Sample(def.Toe);
        double heading = Math.Atan2(-at.Tangent.X, -at.Tangent.Z) * 180 / Math.PI;
        Local = new RailLine(new LineDefinition($"{main.Name}/{index}", def.Segments) { StartHeadingDegrees = heading }, at.Position);
    }

    public int Index { get; }
    public BranchDefinition Definition { get; }
    public BranchKind Kind => Definition.Kind;
    public double Toe => Definition.Toe;
    public int Side => Definition.Side;
    /// <summary>The branch alone, distance 0 at the points.</summary>
    public RailLine Local { get; }
    /// <summary>Path distance of the buffer stop at the end.</summary>
    public double End => Toe + Local.Length;
}

/// <summary>A point on the line: where it is, which way it runs, and the track conditions there.</summary>
public readonly record struct TrackSample(double Distance, Double3 Position, Double3 Tangent, double GradePercent, double Curvature);

/// <summary>
/// A built rail line. Segments are integrated once into a fine polyline so lookups by
/// distance are cheap and identical on host and clients.
/// </summary>
public sealed class RailLine
{
    /// <summary>Sampling step when integrating segments. 1 m keeps curve chord error under 1 mm at 150 m radius.</summary>
    public const double Step = 1.0;

    readonly Double3[] _points;
    readonly double[] _grade;
    readonly double[] _curvature;
    readonly Branch[] _branches;

    /// <summary>The path of a rake on the main line; a branch's index is the path onto it.</summary>
    public const int MainPath = -1;

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

        int n = (int)Math.Ceiling(Length / Step) + 1;
        _points = new Double3[n];
        _grade = new double[n];
        _curvature = new double[n];

        // Heading 0 runs along -Z (engine forward); positive heading turns left (counter-clockwise from above).
        double heading = def.StartHeadingDegrees * Math.PI / 180;
        var pos = origin;
        int seg = 0, last = def.Segments.Count - 1;
        double segStart = 0;
        for (int i = 0; i < n; i++)
        {
            double s = Math.Min(i * Step, Length);
            // The segment a sample is in; on a boundary, the one starting there.
            while (seg < last && s >= segStart + def.Segments[seg].Length)
                segStart += def.Segments[seg++].Length;
            var segment = def.Segments[seg];
            _points[i] = pos;
            _grade[i] = segment.GradePercent;
            _curvature[i] = segment.Curvature;

            if (i == n - 1)
                break;
            // On to the next sample through each segment in between, with its own curve and grade, so a boundary is
            // exact wherever it falls (a turnout's out-and-back curves have to come out parallel). Midpoint
            // integration of heading keeps curves closing correctly.
            double end = Math.Min(s + Step, Length), at = s, pieceStart = segStart;
            for (int k = seg; at < end;)
            {
                var piece = def.Segments[k];
                double pieceEnd = k < last ? Math.Min(end, pieceStart + piece.Length) : end;
                double ds = pieceEnd - at;
                double midHeading = heading + piece.Curvature * ds / 2;
                double horizontal = ds / Math.Sqrt(1 + Math.Pow(piece.GradePercent / 100, 2));
                pos += new Double3(-Math.Sin(midHeading) * horizontal, horizontal * piece.GradePercent / 100, -Math.Cos(midHeading) * horizontal);
                heading += piece.Curvature * ds;
                at = pieceEnd;
                if (k < last && at >= pieceStart + piece.Length)
                    pieceStart += def.Segments[k++].Length;
            }
        }
    }

    public string Name { get; }
    public double Length { get; }
    public IReadOnlyList<TrackSegment> Segments { get; }

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
        return new TrackSample(s, Double3.Lerp(a, b, t), (b - a).Normalized, _grade[nearest], _curvature[nearest]);
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
        if (path < 0 || distance <= _branches[path].Toe)
            return Sample(distance);
        var b = _branches[path];
        var local = b.Local.Sample(distance - b.Toe);
        return local with { Distance = distance };
    }

    /// <summary>Where a path ends: the main line's length, or a branch's buffer stop.</summary>
    public double PathLength(int path) => path < 0 ? Length : _branches[path].End;

    /// <summary>True when <paramref name="distance"/> on <paramref name="path"/> is on the main line (before any branch's points).</summary>
    public bool OnMain(int path, double distance) => path < 0 || distance <= _branches[path].Toe;

    /// <summary>How far two paths run together from the start of the line.</summary>
    public double Shared(int a, int b) =>
        a == b ? PathLength(a) : a < 0 ? _branches[b].Toe : b < 0 ? _branches[a].Toe : Math.Min(_branches[a].Toe, _branches[b].Toe);

    /// <summary>
    /// The track nearest a world point, refining a hint along the main line: the main line, or a branch if one is
    /// nearer. Returns the path and the distance along it.
    /// </summary>
    public (int Path, double Distance) Nearest(Double3 world, ref double hint)
    {
        hint = Project(this, world, hint);
        var best = (Path: MainPath, Distance: hint);
        double bestD = Flat(Sample(hint).Position - world);
        foreach (var b in _branches)
        {
            // Only branches whose points are within their own length of here can be near.
            if (hint < b.Toe - 50 || hint > b.End + 50)
                continue;
            double along = Project(b.Local, world, Math.Clamp(hint - b.Toe, 0, b.Local.Length));
            double d = Flat(b.Local.Sample(along).Position - world);
            if (along > 0 && d < bestD)
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
}
