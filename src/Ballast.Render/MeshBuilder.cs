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

/// <summary>A vertex of the blended effects pass (VFX): position, texture coordinates, colour with alpha, texture layer.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct FxVertex(Vector3 position, Vector2 uv, Vector4 colour, float layer)
{
    public Vector3 Position = position;
    public Vector2 Uv = uv;
    public Vector4 Colour = colour;
    public float Layer = layer;
    public const int Stride = 40;
}

/// <summary>How an effect goes over the scene (pipeline "blend modes: additive for fire, sparks and muzzle flash; alpha for smoke and dust").</summary>
public enum FxBlend { Alpha, Additive }

/// <summary>
/// An enclosed space (a car's interior) in camera-relative space: inside it the night doesn't reach, no moon, no
/// headlamp, no rain, only the practical lights (GDD §28: "inside = warm, human, temporary safety").
/// </summary>
public readonly record struct Room(Vector3 Centre, Vector3 Right, Vector3 Up, Vector3 Back, Vector3 Half);

/// <summary>A cooked mesh placed in the scene: the renderer uploads <see cref="Asset"/> once and draws it by transform.</summary>
/// <param name="Model">Object to camera-relative space.</param>
/// <param name="Glow">Scales the asset's emissive surfaces (a lamp dimmed, a firebox dying down).</param>
/// <param name="Scar">
/// The damage mask (pipeline shader set: "damage-mask blend for persistent car scars"): x how much of it is scarred,
/// 0..1; y a seed choosing where. The mask lies in the asset's own texel space, so one seed scars the same places
/// every time: give each car its own, and a car keeps its scars.
/// </param>
/// <param name="Bones">A skinned asset's pose: where its bone palette starts in <see cref="MeshBuilder.Bones"/> (−1: not skinned).</param>
/// <param name="SurfaceOffset">Added to every vertex's <see cref="Vertex.Surface"/> (texels): moves the grime so two drawn
/// from one asset don't wear alike.</param>
public readonly record struct MeshInstance(MeshAsset Asset, Matrix4x4 Model, float Glow = 1, Vector3 Tint = default, Vector2 Scar = default,
    int Bones = -1, Vector3 SurfaceOffset = default);

/// <summary>A skinned vertex's bones (indices into its model's skeleton, as floats) and their weights.</summary>
public struct SkinWeights(Vector4 joints, Vector4 weights)
{
    public Vector4 Joints = joints;
    public Vector4 Weights = weights;

    public const int Stride = 32;
}

/// <summary>
/// Geometry built once (a car body, a tree, a creature's pose) and drawn many times by transform: the kit's pieces.
/// Immutable once made; the renderer keeps a GPU copy for as long as the asset is alive.
/// </summary>
/// <param name="skin">For a skinned model in its bind pose, each vertex's bones: drawn with a bone palette
/// (<see cref="MeshBuilder.Skinned"/>), the GPU poses it (skinned.glsl).</param>
public sealed class MeshAsset(string name, Vertex[] vertices, SkinWeights[]? skin = null)
{
    public string Name { get; } = name;
    public Vertex[] Vertices { get; } = vertices;
    public SkinWeights[]? Skin { get; } = skin is null || skin.Length == vertices.Length ? skin
        : throw new ArgumentException($"{name}: {skin.Length} skin weights for {vertices.Length} vertices");
    public int Triangles => Vertices.Length / 3;

    /// <summary>A sphere round every vertex, in the asset's own space (the renderer culls each pass by it). A skinned
    /// asset's is its bind pose's, which a pose can reach outside: those aren't culled.</summary>
    public (Vector3 Centre, float Radius) Bounds => _bounds ??= Sphere(Vertices);
    (Vector3, float)? _bounds;

    static (Vector3, float) Sphere(Vertex[] vertices)
    {
        if (vertices.Length == 0)
            return (Vector3.Zero, 0);
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var v in vertices)
        {
            min = Vector3.Min(min, v.Position);
            max = Vector3.Max(max, v.Position);
        }
        var centre = (min + max) / 2;
        float r2 = 0;
        foreach (var v in vertices)
            r2 = MathF.Max(r2, Vector3.DistanceSquared(v.Position, centre));
        return (centre, MathF.Sqrt(r2));
    }

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

    /// <summary>Enclosed spaces this frame (up to 16 are lit as such, the nearest). Cleared with the mesh.</summary>
    public List<Room> Rooms { get; } = new();

    /// <summary>Cooked meshes to draw this frame. Cleared with the mesh.</summary>
    public List<MeshInstance> Instances { get; } = new();

    /// <summary>The skinned instances' bone palettes this frame (each its model's skinning matrices, bone space to the
    /// model's object space), one after another. Cleared with the mesh.</summary>
    public List<Matrix4x4> Bones { get; } = new();

    /// <summary>
    /// Every triangle's corners as the frame draws them, camera-relative: the soup, then each instance placed by its model
    /// matrix, a skinned one posed by its palette first (skin.glsl's linear blend, on the CPU). For tests and tools, which
    /// ask where things are drawn without a GPU; nothing in the frame uses it.
    /// </summary>
    public Vertex[] Flattened()
    {
        var all = new List<Vertex>(_vertices);
        foreach (var instance in Instances)
        {
            var asset = instance.Asset;
            for (int i = 0; i < asset.Vertices.Length; i++)
            {
                var m = instance.Model;
                if (instance.Bones >= 0 && asset.Skin is { } skin)
                {
                    var w = skin[i];
                    int b = instance.Bones;
                    var blend = Bones[b + (int)w.Joints.X] * w.Weights.X + Bones[b + (int)w.Joints.Y] * w.Weights.Y
                        + Bones[b + (int)w.Joints.Z] * w.Weights.Z + Bones[b + (int)w.Joints.W] * w.Weights.W;
                    m = blend * m;
                }
                var v = asset.Vertices[i];
                var n = Vector3.TransformNormal(v.Normal, m);
                v.Position = Vector3.Transform(v.Position, m);
                v.Normal = n.LengthSquared() > 1e-24f ? Vector3.Normalize(n) : Vector3.UnitY;
                v.Surface += instance.SurfaceOffset;
                all.Add(v);
            }
        }
        return [.. all];
    }

    /// <summary>A skinned asset posed by <paramref name="skin"/> (a matrix per bone, bind space to object space), placed by
    /// <paramref name="model"/> (object to camera-relative).</summary>
    public void Skinned(MeshAsset asset, in Matrix4x4 model, ReadOnlySpan<Matrix4x4> skin, float glow = 1, Vector3 surfaceOffset = default)
    {
        if (asset.Skin is null)
            throw new ArgumentException($"{asset.Name} isn't skinned");
        Instances.Add(new MeshInstance(asset, model, glow, Bones: Bones.Count, SurfaceOffset: surfaceOffset));
        foreach (var m in skin)
            Bones.Add(m);
    }

    /// <summary>The effects this frame, in triangles: smoke and dust (alpha), fire, sparks and glows (additive).</summary>
    public List<FxVertex> AlphaFx { get; } = new();
    public List<FxVertex> AdditiveFx { get; } = new();

    /// <summary>
    /// A camera-facing sprite (pipeline "camera-facing billboards ... from flipbook atlases"). It faces the eye point (the
    /// origin), not the view plane, so both eyes of a headset see the same one. <paramref name="frame"/> picks a cell of a
    /// <paramref name="grid"/>×<paramref name="grid"/> flipbook.
    /// </summary>
    public void Billboard(Vector3 at, float size, float rotation, Vector4 colour, int layer, FxBlend blend, int frame = 0, int grid = 1, float stretch = 1)
    {
        float dist = at.Length();
        if (dist < 0.05f || colour.W <= 0.002f)
            return;
        var n = -at / dist;
        var up = MathF.Abs(n.Y) > 0.98f ? Vector3.UnitZ : Vector3.UnitY;
        var right = Vector3.Normalize(Vector3.Cross(up, n));
        up = Vector3.Cross(n, right);
        float c = MathF.Cos(rotation), s = MathF.Sin(rotation);
        var r = (right * c + up * s) * (size / 2);
        var u = (up * c - right * s) * (size / 2 * stretch);
        float cell = 1f / grid;
        var uv0 = new Vector2(frame % grid, frame / grid % grid) * cell;
        var list = blend == FxBlend.Additive ? AdditiveFx : AlphaFx;
        void V(Vector3 p, Vector2 t) => list.Add(new FxVertex(p, uv0 + t * cell, colour, layer));
        V(at - r + u, new(0, 0));
        V(at - r - u, new(0, 1));
        V(at + r - u, new(1, 1));
        V(at - r + u, new(0, 0));
        V(at + r - u, new(1, 1));
        V(at + r + u, new(1, 0));
    }

    /// <summary>A blended triangle of glow geometry (a lamp's beam), colour and alpha per corner.</summary>
    public void FxTriangle(FxBlend blend, in FxVertex a, in FxVertex b, in FxVertex c)
    {
        var list = blend == FxBlend.Additive ? AdditiveFx : AlphaFx;
        list.Add(a);
        list.Add(b);
        list.Add(c);
    }

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
        Bones.Clear();
        Rooms.Clear();
        AlphaFx.Clear();
        AdditiveFx.Clear();
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
        var from = piece.Vertices;
        int start = _vertices.Count;
        CollectionsMarshal.SetCount(_vertices, start + from.Length);
        var into = CollectionsMarshal.AsSpan(_vertices)[start..];
        // A uniform scale only lengthens normals, so they're renormalised by one factor, not per vertex.
        float scale = new Vector3(model.M11, model.M12, model.M13).Length();
        float inverse = scale > 1e-6f ? 1 / scale : 1;
        for (int i = 0; i < from.Length; i++)
        {
            ref var v = ref into[i];
            v = from[i];
            v.Position = Vector3.Transform(v.Position, model);
            v.Normal = Vector3.TransformNormal(v.Normal, model) * inverse;
            v.Color *= t;
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
