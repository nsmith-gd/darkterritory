using System.Numerics;
using System.Runtime.InteropServices;

namespace Ballast.Render;

[StructLayout(LayoutKind.Sequential)]
public struct Vertex(Vector3 position, Vector3 normal, Vector3 color, float emissive = 0)
{
    public Vector3 Position = position;
    public Vector3 Normal = normal;
    public Vector3 Color = color;
    /// <summary>0 = lit surface, 1 = light source drawn at full colour (lamps, fireboxes, windows).</summary>
    public float Emissive = emissive;

    public const int Stride = 40;
}

/// <summary>A practical light (a car's lamp, the firebox) baked into vertices as geometry is added.</summary>
/// <param name="Position">In the same (camera-relative) space as the geometry.</param>
public readonly record struct PointLight(Vector3 Position, Vector3 Colour, float Range);

/// <summary>
/// CPU-side triangle soup for greybox geometry. Flat-shaded on purpose: faceted, chunky forms
/// are the art direction (GDD §27), not a limitation. Practical lights are per-vertex, the way late
/// PS2 games lit interiors: set <see cref="PointLights"/> before adding what they should light.
/// </summary>
public sealed class MeshBuilder
{
    readonly List<Vertex> _vertices = new();

    /// <summary>Lights applied to everything added until cleared. Cleared with the mesh.</summary>
    public List<PointLight> PointLights { get; } = new();

    /// <summary>Emissive amount applied to everything added until changed.</summary>
    public float Emissive { get; set; }

    public int Count => _vertices.Count;
    public ReadOnlySpan<Vertex> Vertices => CollectionsMarshal.AsSpan(_vertices);
    public void Clear()
    {
        _vertices.Clear();
        PointLights.Clear();
    }

    public void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 color)
    {
        var n = Vector3.Normalize(Vector3.Cross(b - a, c - a));
        if (float.IsNaN(n.X))
            return;
        if (PointLights.Count == 0 || Emissive >= 1)
        {
            _vertices.Add(new Vertex(a, n, color, Emissive));
            _vertices.Add(new Vertex(b, n, color, Emissive));
            _vertices.Add(new Vertex(c, n, color, Emissive));
            return;
        }
        _vertices.Add(Lit(a, n, color));
        _vertices.Add(Lit(b, n, color));
        _vertices.Add(Lit(c, n, color));
    }

    /// <summary>
    /// Sums the practical lights at a vertex and folds them in through the emissive channel, tinted: the
    /// shader already draws emissive surfaces at albedo strength, which is what "lit by a lamp" looks like.
    /// </summary>
    Vertex Lit(Vector3 p, Vector3 n, Vector3 color)
    {
        var light = Vector3.Zero;
        foreach (var l in PointLights)
        {
            var d = l.Position - p;
            float dist = d.Length();
            if (dist >= l.Range || dist < 1e-4f)
                continue;
            float facing = Vector3.Dot(n, d / dist);
            if (facing <= 0)
                continue;
            float falloff = 1 - dist / l.Range;
            light += l.Colour * (falloff * falloff * facing);
        }
        float strength = (light.X * 0.3f + light.Y * 0.59f + light.Z * 0.11f);
        if (strength <= 1e-3f)
            return new Vertex(p, n, color, Emissive);
        var tint = light / strength;
        return new Vertex(p, n, color * Vector3.Lerp(Vector3.One, tint, 0.55f), Math.Max(Emissive, Math.Min(0.9f, strength * 1.3f)));
    }

    /// <summary>Counter-clockwise quad a→b→c→d seen from the front.</summary>
    public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 color)
    {
        Triangle(a, b, c, color);
        Triangle(a, c, d, color);
    }

    /// <summary>Oriented box from its centre, three unit axes and half-extents along them.</summary>
    public void Box(Vector3 centre, Vector3 right, Vector3 up, Vector3 back, Vector3 half, Vector3 color)
    {
        var x = right * half.X;
        var y = up * half.Y;
        var z = back * half.Z;
        Vector3 P(int sx, int sy, int sz) => centre + x * sx + y * sy + z * sz;
        Quad(P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), color);      // back (+Z)
        Quad(P(1, -1, -1), P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), color);  // front (−Z)
        Quad(P(1, -1, 1), P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), color);      // right
        Quad(P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1), color);  // left
        Quad(P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), P(-1, 1, -1), color);      // top
        Quad(P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1), color);  // bottom
    }

    public void AxisBox(Vector3 min, Vector3 max, Vector3 color) =>
        Box((min + max) / 2, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, (max - min) / 2, color);

    /// <summary>A square-based pyramid: the cheapest readable pine tree silhouette.</summary>
    public void Pyramid(Vector3 baseCentre, float halfWidth, float height, Vector3 color)
    {
        var apex = baseCentre + new Vector3(0, height, 0);
        Vector3 C(int sx, int sz) => baseCentre + new Vector3(sx * halfWidth, 0, sz * halfWidth);
        Triangle(C(-1, 1), C(1, 1), apex, color);
        Triangle(C(1, 1), C(1, -1), apex, color);
        Triangle(C(1, -1), C(-1, -1), apex, color);
        Triangle(C(-1, -1), C(-1, 1), apex, color);
    }
}
