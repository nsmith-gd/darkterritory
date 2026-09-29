using System.Numerics;
using System.Runtime.InteropServices;

namespace Ballast.Render;

[StructLayout(LayoutKind.Sequential)]
public struct Vertex(Vector3 position, Vector3 normal, Vector3 color, float emissive = 0)
{
    public Vector3 Position = position;
    public Vector3 Normal = normal;
    /// <summary>Albedo when untextured; a tint over the texture when <see cref="Layer"/> names one.</summary>
    public Vector3 Color = color;
    /// <summary>0 = lit surface, 1 = light source drawn at full colour (lamps, fireboxes, windows).</summary>
    public float Emissive = emissive;
    /// <summary>Where on its surface this is, in texels, in a space that stays put under the camera (<see cref="SurfaceStyle"/>).</summary>
    public Vector3 Surface;
    /// <summary>How much grain, grime and staining show (0: none, a clean light source).</summary>
    public float Wear;
    /// <summary>How hard a specular it throws back (metal), for untextured surfaces; a spec map does it for textured ones.</summary>
    public float Shine;
    /// <summary>Texture coordinates, in repeats of the layer's tile.</summary>
    public Vector2 Uv;
    /// <summary>The material texture's layer in the renderer's array, or −1 for none (flat colour, weathered in the shader).</summary>
    public float Layer = -1;
    /// <summary>A second layer blended over the first by <see cref="Blend"/> (the terrain: mud into grass into forest floor), or −1.</summary>
    public float Layer2 = -1;
    public float Blend;

    public const int Stride = 80;
}

/// <summary>
/// What a surface is made of, for the look (GDD §27's material families): how worn it shows, how hard its specular is,
/// and which texture layer it wears (−1 for none) at how many metres to a repeat. <paramref name="Base"/> is the colour
/// the texture was painted as: a surface asked for in another shade of it wears the texture tinted by the difference.
/// </summary>
public readonly record struct SurfaceMaterial(float Wear, float Shine, int Layer = -1, float TileMetres = 1, Vector3 Base = default);

/// <summary>
/// The art pass's surface treatment (T39, GDD §27). Every surface gets texel coordinates at
/// <paramref name="TexelsPerMetre"/> in its own box's frame (so the grime rides with a moving car and doesn't swim as the
/// camera moves), a material by its colour, and the bottoms of things darkened by <paramref name="Baked"/> (the baked
/// shadow). Textured materials also get planar texture coordinates in the same frame.
/// </summary>
public sealed record SurfaceStyle(float TexelsPerMetre, float Baked, Func<Vector3, SurfaceMaterial> Material);

/// <summary>A practical light (a car's lamp, the firebox, a hand lantern). Lit per pixel, unshadowed.</summary>
/// <param name="Position">In the same (camera-relative) space as the geometry.</param>
public readonly record struct PointLight(Vector3 Position, Vector3 Colour, float Range);

/// <summary>A cooked mesh placed in the scene: the renderer uploads <see cref="Asset"/> once and draws it by transform.</summary>
/// <param name="Model">Object to camera-relative space.</param>
/// <param name="Glow">Scales the asset's emissive surfaces (a lamp dimmed in a Vigil, a firebox dying down).</param>
public readonly record struct MeshInstance(MeshAsset Asset, Matrix4x4 Model, float Glow = 1, Vector3 Tint = default);

/// <summary>
/// Geometry built once (a car body, a tree, a creature's pose) and drawn many times by transform: the kit's pieces.
/// Immutable once made; the renderer keeps a GPU copy for as long as the asset is alive.
/// </summary>
public sealed class MeshAsset(string name, Vertex[] vertices)
{
    public string Name { get; } = name;
    public Vertex[] Vertices { get; } = vertices;
    public int Triangles => Vertices.Length / 3;

    public static MeshAsset From(string name, MeshBuilder built) => new(name, built.Vertices.ToArray());
}

/// <summary>
/// CPU-side triangle soup for greybox geometry. Flat-shaded on purpose: faceted, chunky forms
/// are the art direction (GDD §27), not a limitation. Practical lights go in <see cref="PointLights"/>, and are lit per
/// pixel over everything; cooked kit pieces go in <see cref="Instances"/>.
/// </summary>
public sealed class MeshBuilder
{
    readonly List<Vertex> _vertices = new();

    /// <summary>The practical lights this frame. Cleared with the mesh.</summary>
    public List<PointLight> PointLights { get; } = new();

    /// <summary>Cooked meshes to draw this frame. Cleared with the mesh.</summary>
    public List<MeshInstance> Instances { get; } = new();

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
        Instances.Clear();
    }

    /// <summary>Drops everything added after the first <paramref name="count"/> vertices (lights stay).</summary>
    public void Truncate(int count)
    {
        if (count < _vertices.Count)
            _vertices.RemoveRange(count, _vertices.Count - count);
    }

    /// <summary>Appends a vertex exactly as given (the kit builders make their own UVs and materials).</summary>
    public void Add(in Vertex v) => _vertices.Add(v);

    /// <summary>
    /// Bakes a small cooked piece into this frame's soup at <paramref name="model"/> (object to camera-relative): cheaper
    /// than a draw call each for hundreds of trees and grass tufts. Big pieces go in <see cref="Instances"/>.
    /// </summary>
    public void Append(MeshAsset piece, in Matrix4x4 model, Vector3? tint = null)
    {
        var t = tint ?? Vector3.One;
        foreach (var src in piece.Vertices)
        {
            var v = src;
            v.Position = Vector3.Transform(src.Position, model);
            v.Normal = Vector3.Normalize(Vector3.TransformNormal(src.Normal, model));
            v.Color *= t;
            _vertices.Add(v);
        }
    }

    public void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 color)
    {
        float t = Style?.TexelsPerMetre ?? 0;
        var o = SurfaceOrigin + SeedOffset(color, Vector3.Zero);
        // Bare triangles are the ground and the like: textured by world position, projected down the face's main axis.
        var n = Vector3.Cross(b - a, c - a);
        var w = SurfaceOrigin;
        Triangle(a, b, c, color, color, color, (a + o) * t, (b + o) * t, (c + o) * t, Planar(a + w, n), Planar(b + w, n), Planar(c + w, n));
    }

    /// <summary>A triangle with its own texture coordinates (in metres; the material's tile scales them).</summary>
    public void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 color, Vector2 ua, Vector2 ub, Vector2 uc)
    {
        float t = Style?.TexelsPerMetre ?? 0;
        var o = SurfaceOrigin + SeedOffset(color, Vector3.Zero);
        Triangle(a, b, c, color, color, color, (a + o) * t, (b + o) * t, (c + o) * t, ua, ub, uc);
    }

    /// <summary>Planar coordinates in metres: across and down the face, whichever way it mostly faces.</summary>
    static Vector2 Planar(Vector3 p, Vector3 n)
    {
        var an = Vector3.Abs(n);
        if (an.Y >= an.X && an.Y >= an.Z)
            return new Vector2(p.X, p.Z);
        return an.X >= an.Z ? new Vector2(p.Z, -p.Y) : new Vector2(p.X, -p.Y);
    }

    void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 ca, Vector3 cb, Vector3 cc, Vector3 sa, Vector3 sb, Vector3 sc,
        Vector2 ua, Vector2 ub, Vector2 uc)
    {
        var n = Vector3.Normalize(Vector3.Cross(b - a, c - a));
        if (float.IsNaN(n.X))
            return;
        var material = Emissive >= 1 || Style is null ? default(SurfaceMaterial) with { Layer = -1 } : Style.Material(ca);
        _vertices.Add(Surfaced(new Vertex(a, n, ca, Emissive), sa, ua, material));
        _vertices.Add(Surfaced(new Vertex(b, n, cb, Emissive), sb, ub, material));
        _vertices.Add(Surfaced(new Vertex(c, n, cc, Emissive), sc, uc, material));
    }

    static Vertex Surfaced(Vertex v, Vector3 surface, Vector2 uv, SurfaceMaterial m)
    {
        v.Surface = surface;
        v.Wear = m.Wear;
        v.Shine = m.Shine;
        v.Layer = m.Layer;
        if (m.Layer >= 0)
        {
            v.Uv = uv / MathF.Max(m.TileMetres, 1e-3f);
            // The texture carries the colour now; what's left of the vertex colour is how it differs from the
            // texture's own (a car painted a shade off, a lamp dimmed, the baked shadow low down).
            v.Color = Tint(v.Color, m.Base);
        }
        return v;
    }

    /// <summary>How <paramref name="colour"/> differs from <paramref name="basis"/>, as a multiplier (one where they match).</summary>
    public static Vector3 Tint(Vector3 colour, Vector3 basis)
    {
        if (basis == default)
            return Vector3.One;
        static float R(float c, float b) => b > 1e-4f ? Math.Clamp(c / b, 0, 4) : 1;
        return new Vector3(R(colour.X, basis.X), R(colour.Y, basis.Y), R(colour.Z, basis.Z));
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

    /// <summary>Counter-clockwise quad a→b→c→d seen from the front.</summary>
    public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 color)
    {
        Triangle(a, b, c, color);
        Triangle(a, c, d, color);
    }

    /// <summary>Oriented box from its centre, three unit axes and half-extents along them.</summary>
    /// <remarks>
    /// With a <see cref="Style"/>, its texels and texture coordinates are in the box's own frame (they move with it), and
    /// its bottom corners are darker by the style's baked shadow.
    /// </remarks>
    public void Box(Vector3 centre, Vector3 right, Vector3 up, Vector3 back, Vector3 half, Vector3 color)
    {
        var x = right * half.X;
        var y = up * half.Y;
        var z = back * half.Z;
        float t = Style?.TexelsPerMetre ?? 0;
        var seed = SeedOffset(color, half);
        var shadowed = color * (1 - (Style?.Baked ?? 0));
        // The texture's offset on this box: a little of the grain seed, so neighbouring boxes don't start their
        // planks on the same line.
        var uvSeed = new Vector2(seed.X, seed.Y) / 16;
        Vector3 P(int sx, int sy, int sz) => centre + x * sx + y * sy + z * sz;
        Vector3 S(int sx, int sy, int sz) => (new Vector3(half.X * sx, half.Y * sy, half.Z * sz) + seed) * t;
        Vector3 C(int sy) => sy < 0 ? shadowed : color;
        // Texture coordinates in metres across the face and down it (v grows downwards: a texture's top is up).
        Vector2 U(int axis, int sx, int sy, int sz)
        {
            var l = new Vector3(half.X * sx, half.Y * sy, half.Z * sz);
            return uvSeed + axis switch
            {
                0 => new Vector2(sx > 0 ? -l.Z : l.Z, -l.Y), // ±X faces: along the box's length
                2 => new Vector2(sz > 0 ? l.X : -l.X, -l.Y), // ±Z faces
                _ => new Vector2(l.X, l.Z),                  // top and bottom
            };
        }
        void Face(int axis, int ax, int ay, int az, int bx, int by, int bz, int cx, int cy, int cz, int dx, int dy, int dz)
        {
            // A face's own side is constant over its corners, so its corners' coordinates say which way it's read.
            Vector2 UF(int sx, int sy, int sz) => U(axis, sx, sy, sz);
            Triangle(P(ax, ay, az), P(bx, by, bz), P(cx, cy, cz), C(ay), C(by), C(cy), S(ax, ay, az), S(bx, by, bz), S(cx, cy, cz),
                UF(ax, ay, az), UF(bx, by, bz), UF(cx, cy, cz));
            Triangle(P(ax, ay, az), P(cx, cy, cz), P(dx, dy, dz), C(ay), C(cy), C(dy), S(ax, ay, az), S(cx, cy, cz), S(dx, dy, dz),
                UF(ax, ay, az), UF(cx, cy, cz), UF(dx, dy, dz));
        }
        Face(2, -1, -1, 1, 1, -1, 1, 1, 1, 1, -1, 1, 1);      // back (+Z)
        Face(2, 1, -1, -1, -1, -1, -1, -1, 1, -1, 1, 1, -1);  // front (−Z)
        Face(0, 1, -1, 1, 1, -1, -1, 1, 1, -1, 1, 1, 1);      // right
        Face(0, -1, -1, -1, -1, -1, 1, -1, 1, 1, -1, 1, -1);  // left
        Face(1, -1, 1, 1, 1, 1, 1, 1, 1, -1, -1, 1, -1);      // top
        Face(1, -1, -1, -1, 1, -1, -1, 1, -1, 1, -1, -1, 1);  // bottom
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
