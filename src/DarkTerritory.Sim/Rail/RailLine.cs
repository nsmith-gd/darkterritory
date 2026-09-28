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

    public RailLine(LineDefinition def)
    {
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
        var pos = Double3.Zero;
        int seg = 0;
        double segStart = 0;
        for (int i = 0; i < n; i++)
        {
            double s = Math.Min(i * Step, Length);
            while (seg < def.Segments.Count - 1 && s > segStart + def.Segments[seg].Length)
                segStart += def.Segments[seg++].Length;
            var segment = def.Segments[seg];
            _points[i] = pos;
            _grade[i] = segment.GradePercent;
            _curvature[i] = segment.Curvature;

            if (i == n - 1)
                break;
            // Midpoint integration of heading keeps curves closing correctly.
            double ds = Math.Min(Step, Length - s);
            double midHeading = heading + segment.Curvature * ds / 2;
            double horizontal = ds / Math.Sqrt(1 + Math.Pow(segment.GradePercent / 100, 2));
            pos += new Double3(-Math.Sin(midHeading) * horizontal, horizontal * segment.GradePercent / 100, -Math.Cos(midHeading) * horizontal);
            heading += segment.Curvature * ds;
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
}
