using System.Numerics;

namespace Ballast;

/// <summary>
/// Double-precision position for world anchors. Routes run 18–40 km, past where float keeps
/// millimetre precision; render and physics convert to float relative to a floating origin.
/// </summary>
public readonly record struct Double3(double X, double Y, double Z)
{
    public static readonly Double3 Zero = new(0, 0, 0);
    public static readonly Double3 Up = new(0, 1, 0);

    public static Double3 operator +(Double3 a, Double3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Double3 operator -(Double3 a, Double3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Double3 operator *(Double3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    public static Double3 operator *(double s, Double3 a) => a * s;

    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
    public Double3 Normalized => this * (1 / Length);

    public static double Dot(Double3 a, Double3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    public static Double3 Cross(Double3 a, Double3 b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    public static Double3 Lerp(Double3 a, Double3 b, double t) => a + (b - a) * t;

    /// <summary>Offset from <paramref name="origin"/> as float, for render/physics near the floating origin.</summary>
    public Vector3 RelativeTo(Double3 origin) => new((float)(X - origin.X), (float)(Y - origin.Y), (float)(Z - origin.Z));

    public override string ToString() => $"({X:0.###}, {Y:0.###}, {Z:0.###})";
}
