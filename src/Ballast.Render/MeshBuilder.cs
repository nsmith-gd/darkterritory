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
    /// <summary>Where on its surface this is, in texels, in a space that stays put under the camera (<see cref="SurfaceStyle"/>).</summary>
    public Vector3 Surface;
    /// <summary>How much grain, grime and staining show (0: none, a clean light source).</summary>
    public float Wear;
    /// <summary>How hard a specular it throws back (metal).</summary>
    public float Shine;

    public const int Stride = 60;
}

/// <summary>What a surface is made of, for the look (GDD §27's material families).</summary>
public readonly record struct SurfaceMaterial(float Wear, float Shine);

/// <summary>
/// The art pass's surface treatment (T39, GDD §27): stands in for textures. Every surface gets texel coordinates at
/// <paramref name="TexelsPerMetre"/> in its own box's frame (so the grime rides with a moving car and doesn't swim as the
/// camera moves), a material by its colour, and the bottoms of things darkened by <paramref name="Baked"/> (the baked
/// shadow). The shader turns that into blocky grain, broad soot and rust fields, and oil streaks.
/// </summary>
public sealed record SurfaceStyle(float TexelsPerMetre, float Baked, Func<Vector3, SurfaceMaterial> Material);

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

    /// <summary>The surface treatment, or null for plain flat colour. Kept across <see cref="Clear"/>.</summary>
    public SurfaceStyle? Style { get; set; }

    /// <summary>
    /// Shifts the grain pattern for what's added next, so identical parts (every car's walls) don't wear identically.
    /// Something stable per thing: a vehicle's index, a prop's place along the line.
    /// </summary>
    public float Seed { get; set; }

    /// <summary>
    /// Where the camera-relative origin is in a stable space (the world, wrapped), for surfaces added as bare
    /// triangles: they get world texel coordinates. Boxes use their own frame instead.
    /// </summary>
    public Vector3 SurfaceOrigin { get; set; }

    public int Count => _vertices.Count;
    public ReadOnlySpan<Vertex> Vertices => CollectionsMarshal.AsSpan(_vertices);
    public void Clear()
    {
        _vertices.Clear();
        PointLights.Clear();
    }

    /// <summary>Drops everything added after the first <paramref name="count"/> vertices (lights stay).</summary>
    public void Truncate(int count)
    {
        if (count < _vertices.Count)
            _vertices.RemoveRange(count, _vertices.Count - count);
    }

    public void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 color)
    {
        float t = Style?.TexelsPerMetre ?? 0;
        var o = SurfaceOrigin + SeedOffset(color, Vector3.Zero);
        Triangle(a, b, c, color, color, color, (a + o) * t, (b + o) * t, (c + o) * t);
    }

    void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 ca, Vector3 cb, Vector3 cc, Vector3 sa, Vector3 sb, Vector3 sc)
    {
        var n = Vector3.Normalize(Vector3.Cross(b - a, c - a));
        if (float.IsNaN(n.X))
            return;
        var material = Emissive >= 1 || Style is null ? default : Style.Material(ca);
        _vertices.Add(Surfaced(Lit(a, n, ca), sa, material));
        _vertices.Add(Surfaced(Lit(b, n, cb), sb, material));
        _vertices.Add(Surfaced(Lit(c, n, cc), sc, material));
    }

    static Vertex Surfaced(Vertex v, Vector3 surface, SurfaceMaterial m)
    {
        v.Surface = surface;
        v.Wear = m.Wear;
        v.Shine = m.Shine;
        return v;
    }

    /// <summary>A texel offset from <see cref="Seed"/>, the colour and the size: stable for a thing, different between things.</summary>
    Vector3 SeedOffset(Vector3 color, Vector3 half)
    {
        if (Style is null)
            return Vector3.Zero;
        float h = Seed * 12.9898f + color.X * 78.233f + color.Y * 37.719f + color.Z * 11.13f + half.X * 3.7f + half.Y * 5.3f + half.Z * 7.1f;
        return new Vector3(Frac(MathF.Sin(h) * 43758.5f), Frac(MathF.Sin(h + 1.7f) * 43758.5f), Frac(MathF.Sin(h + 3.1f) * 43758.5f)) * 64;
    }

    static float Frac(float v) => v - MathF.Floor(v);

    /// <summary>
    /// Sums the practical lights at a vertex and folds them in through the emissive channel, tinted: the
    /// shader already draws emissive surfaces at albedo strength, which is what "lit by a lamp" looks like.
    /// </summary>
    Vertex Lit(Vector3 p, Vector3 n, Vector3 color)
    {
        if (PointLights.Count == 0 || Emissive >= 1)
            return new Vertex(p, n, color, Emissive);
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
    /// <remarks>
    /// With a <see cref="Style"/>, its texels are in the box's own frame (they move with it), and its bottom corners are
    /// darker by the style's baked shadow.
    /// </remarks>
    public void Box(Vector3 centre, Vector3 right, Vector3 up, Vector3 back, Vector3 half, Vector3 color)
    {
        var x = right * half.X;
        var y = up * half.Y;
        var z = back * half.Z;
        float t = Style?.TexelsPerMetre ?? 0;
        var seed = SeedOffset(color, half);
        var shadowed = color * (1 - (Style?.Baked ?? 0));
        Vector3 P(int sx, int sy, int sz) => centre + x * sx + y * sy + z * sz;
        Vector3 S(int sx, int sy, int sz) => (new Vector3(half.X * sx, half.Y * sy, half.Z * sz) + seed) * t;
        Vector3 C(int sy) => sy < 0 ? shadowed : color;
        void Face(int ax, int ay, int az, int bx, int by, int bz, int cx, int cy, int cz, int dx, int dy, int dz)
        {
            Triangle(P(ax, ay, az), P(bx, by, bz), P(cx, cy, cz), C(ay), C(by), C(cy), S(ax, ay, az), S(bx, by, bz), S(cx, cy, cz));
            Triangle(P(ax, ay, az), P(cx, cy, cz), P(dx, dy, dz), C(ay), C(cy), C(dy), S(ax, ay, az), S(cx, cy, cz), S(dx, dy, dz));
        }
        Face(-1, -1, 1, 1, -1, 1, 1, 1, 1, -1, 1, 1);      // back (+Z)
        Face(1, -1, -1, -1, -1, -1, -1, 1, -1, 1, 1, -1);  // front (−Z)
        Face(1, -1, 1, 1, -1, -1, 1, 1, -1, 1, 1, 1);      // right
        Face(-1, -1, -1, -1, -1, 1, -1, 1, 1, -1, 1, -1);  // left
        Face(-1, 1, 1, 1, 1, 1, 1, 1, -1, -1, 1, -1);      // top
        Face(-1, -1, -1, 1, -1, -1, 1, -1, 1, -1, -1, 1);  // bottom
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
