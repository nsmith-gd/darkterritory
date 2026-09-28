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

/// <summary>
/// CPU-side triangle soup for greybox geometry. Flat-shaded on purpose: faceted, chunky forms
/// are the art direction (GDD §27), not a limitation.
/// </summary>
public sealed class MeshBuilder
{
    readonly List<Vertex> _vertices = new();

    /// <summary>Emissive amount applied to everything added until changed.</summary>
    public float Emissive { get; set; }

    public int Count => _vertices.Count;
    public ReadOnlySpan<Vertex> Vertices => CollectionsMarshal.AsSpan(_vertices);
    public void Clear() => _vertices.Clear();

    public void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 color)
    {
        var n = Vector3.Normalize(Vector3.Cross(b - a, c - a));
        if (float.IsNaN(n.X))
            return;
        _vertices.Add(new Vertex(a, n, color, Emissive));
        _vertices.Add(new Vertex(b, n, color, Emissive));
        _vertices.Add(new Vertex(c, n, color, Emissive));
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
