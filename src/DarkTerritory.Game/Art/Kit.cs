using System.Numerics;
using Ballast.Render;

namespace DarkTerritory.Game.Art;

/// <summary>
/// The modelling kit (pipeline plan, "Geometry conversion"; GDD §27): the handful of primitives every hard-surface piece
/// in the game is built from, the way a 2006 artist boxed things out. Boxes, extruded profiles, faceted cylinders and
/// lathed forms, each with texture coordinates in metres (so a material's tile says its texel density) and a material
/// from the look. Chunky on purpose: big bevels, obvious planes, visible simplification. The result is a
/// <see cref="MeshAsset"/>, cooked once and drawn by transform.
/// </summary>
/// <remarks>Parts go in the piece's own frame: +X right, +Y up, −Z forward, metres (the car frame's convention).</remarks>
public sealed class Kit(Look? look, float seed = 0)
{
    readonly List<Vertex> _v = new();
    MeshBuilder? _into;
    int _count;

    /// <summary>A kit that draws straight into this frame's mesh (track, ground, wires: what's built per frame).</summary>
    public Kit(Look? look, MeshBuilder into, float seed = 0) : this(look, seed) => _into = into;

    void Emit(in Vertex v)
    {
        _count++;
        if (_into is not null)
            _into.Add(v);
        else
            _v.Add(v);
    }

    /// <summary>Moves the grime pattern, for what's drawn next (so neighbouring pieces don't wear alike).</summary>
    public void Reseed(float seed) => _seedOffset = new Vector3(MathF.Sin(seed * 12.9898f), MathF.Sin(seed * 78.233f), MathF.Sin(seed * 37.719f)) * 97;
    Vector3 _seedOffset = new Vector3(MathF.Sin(seed * 12.9898f), MathF.Sin(seed * 78.233f), MathF.Sin(seed * 37.719f)) * 97;

    /// <summary>Where the texel lattice for the shader's grime starts: the world position of this kit's origin, wrapped,
    /// for a kit drawing camera-relative geometry (so the grime stays on the ground as the camera moves).</summary>
    public Vector3 SurfaceOrigin { get; set; }
    readonly Stack<Matrix4x4> _stack = new();
    Matrix4x4 _xf = Matrix4x4.Identity;
    SurfaceMaterial _m = new(0.5f, 0.1f);
    readonly float _texels = look?.Tuning.TexelsPerMetre ?? 128;

    public Look? Look { get; } = look;
    /// <summary>Multiplies the texture (or is the colour, untextured).</summary>
    public Vector3 Tint { get; set; } = Vector3.One;
    public float Emissive { get; set; }
    /// <summary>How much darker the bottoms of boxes are: the baked shadow and the grime that collects low.</summary>
    public float Baked { get; set; } = 0.3f;
    public int Triangles => _count / 3;

    /// <summary>Wears the named texture (content/art/textures) from now on; flat <paramref name="fallback"/> without it.</summary>
    public Kit Use(string texture, Vector3 fallback, float wear = 0.5f, float shine = 0.1f, float? tile = null)
    {
        var m = Look?.Surface(texture, wear, tile) ?? new SurfaceMaterial(wear, shine);
        if (m.Layer < 0)
        {
            // No such texture (a greybox build, or a texture not made yet): the flat colour, weathered by the shader.
            _m = new SurfaceMaterial(wear, shine);
            Tint = fallback;
        }
        else
        {
            _m = m with { Shine = shine };
            Tint = Vector3.One;
        }
        return this;
    }

    /// <summary>Tints what's drawn next (on top of <see cref="Use"/>'s).</summary>
    public Kit Shade(float by)
    {
        Tint *= by;
        return this;
    }

    public void Push(Matrix4x4 local)
    {
        _stack.Push(_xf);
        _xf = local * _xf;
    }

    public void Pop() => _xf = _stack.Pop();

    /// <summary>Places what's drawn inside <paramref name="draw"/> by <paramref name="local"/>.</summary>
    public void With(Matrix4x4 local, Action draw)
    {
        Push(local);
        draw();
        Pop();
    }

    public static Matrix4x4 At(float x, float y, float z) => Matrix4x4.CreateTranslation(x, y, z);
    public static Matrix4x4 At(Vector3 p) => Matrix4x4.CreateTranslation(p);

    public MeshAsset Build(string name) => new(name, [.. _v]);

    /// <summary>Appends another kit's triangles (already in this piece's frame).</summary>
    public void Append(MeshAsset piece, Matrix4x4 local)
    {
        var xf = local * _xf;
        foreach (var src in piece.Vertices)
        {
            var v = src;
            v.Position = Vector3.Transform(src.Position, xf);
            v.Normal = Vector3.Normalize(Vector3.TransformNormal(src.Normal, xf));
            Emit(v);
        }
    }

    Vertex V(Vector3 p, Vector3 n, Vector2 uv, float shade = 1)
    {
        var wp = Vector3.Transform(p, _xf);
        var v = new Vertex(wp, Vector3.Normalize(Vector3.TransformNormal(n, _xf)), Tint * shade, Emissive)
        {
            Surface = (p + _seedOffset + SurfaceOrigin) * _texels,
            Wear = Emissive >= 1 ? 0 : _m.Wear,
            Shine = _m.Shine,
            Layer = _m.Layer,
            Uv = _m.Layer >= 0 ? uv / MathF.Max(_m.TileMetres, 1e-3f) : uv,
        };
        return v;
    }

    /// <summary>A triangle with flat normal, counter-clockwise from its front, texture coordinates in metres.</summary>
    public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector2 ua, Vector2 ub, Vector2 uc, float sa = 1, float sb = 1, float sc = 1)
    {
        var n = Vector3.Cross(b - a, c - a);
        if (n.LengthSquared() < 1e-12f)
            return;
        n = Vector3.Normalize(n);
        Emit(V(a, n, ua, sa));
        Emit(V(b, n, ub, sb));
        Emit(V(c, n, uc, sc));
    }

    /// <summary>
    /// A triangle with flat normal, its texture projected flat along the axis it most faces (a rock's facets: no face
    /// gets its texture smeared along it the way one shared projection smears the faces side-on to it).
    /// </summary>
    public void Tri(Vector3 a, Vector3 b, Vector3 c)
    {
        var n = Vector3.Abs(Vector3.Cross(b - a, c - a));
        Vector2 P(Vector3 p) => n.Y >= n.X && n.Y >= n.Z ? new(p.X, p.Z) : n.X >= n.Z ? new(p.Z, -p.Y) : new(p.X, -p.Y);
        Tri(a, b, c, P(a), P(b), P(c));
    }

    /// <summary>A triangle with its own normals (smooth shading across faceted cylinders).</summary>
    public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 na, Vector3 nb, Vector3 nc, Vector2 ua, Vector2 ub, Vector2 uc)
    {
        if (Vector3.Cross(b - a, c - a).LengthSquared() < 1e-12f)
            return;
        Emit(V(a, na, ua));
        Emit(V(b, nb, ub));
        Emit(V(c, nc, uc));
    }

    /// <summary>A quad by its corners clockwise from the top left, as seen from its front; texture across a→b and down b→c.</summary>
    public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, bool twoSided = false)
    {
        float w = (b - a).Length(), h = (c - b).Length();
        // a is the top left as you look at it; texture v runs down.
        Tri(a, d, c, new(0, 0), new(0, h), new(w, h));
        Tri(a, c, b, new(0, 0), new(w, h), new(w, 0));
        if (twoSided)
        {
            Tri(a, c, d, new(0, 0), new(w, h), new(0, h));
            Tri(a, b, c, new(0, 0), new(w, 0), new(w, h));
        }
    }

    /// <summary>
    /// A flat rectangle facing <paramref name="normal"/>, <paramref name="up"/> its up: lenses, windows, gauge faces, signs.
    /// Texture across (0..width) and down (0..height) in metres, or <paramref name="uv0"/>..<paramref name="uv1"/> when given.
    /// </summary>
    /// <summary>The standard doorway's height (<see cref="Look.Doorway"/>): a person's door, or a big door's (<paramref name="bay"/>).</summary>
    public float DoorHeight(bool bay = false)
    {
        var d = Look?.Doorway ?? new Sim.Train.DoorwayTuning();
        return (float)(bay ? d.BayHeight : d.Height);
    }

    /// <summary>
    /// A doorway as the art draws one on a wall: its opening, a panel <paramref name="width"/> wide and the standard
    /// height (<see cref="DoorHeight"/>) up from <paramref name="sill"/> (the middle of its threshold), facing out along
    /// <paramref name="normal"/>. Every building's doors go through here, so they all stand the one height (note 110).
    /// </summary>
    public void Doorway(Vector3 sill, Vector3 normal, float width, bool bay = false, bool twoSided = false)
    {
        float h = DoorHeight(bay);
        Panel(sill + Vector3.UnitY * (h / 2), normal, Vector3.UnitY, width, h, Vector2.Zero, Vector2.One, twoSided);
    }

    public void Panel(Vector3 centre, Vector3 normal, Vector3 up, float width, float height, Vector2? uv0 = null, Vector2? uv1 = null, bool twoSided = false)
    {
        var n = Vector3.Normalize(normal);
        var u = Vector3.Normalize(up - n * Vector3.Dot(up, n));
        var r = Vector3.Cross(u, n); // the viewer's right, looking at the face from in front
        var a = centre - r * (width / 2) + u * (height / 2);
        var b = centre + r * (width / 2) + u * (height / 2);
        var c = centre + r * (width / 2) - u * (height / 2);
        var d = centre - r * (width / 2) - u * (height / 2);
        var t0 = uv0 ?? Vector2.Zero;
        var t1 = uv1 ?? new Vector2(width, height);
        Quad(a, b, c, d, t0, new Vector2(t1.X, t0.Y), t1, new Vector2(t0.X, t1.Y), twoSided);
    }

    /// <summary>A quad (corners as <see cref="Quad(Vector3, Vector3, Vector3, Vector3, bool)"/>) with explicit texture coordinates, in metres.</summary>
    public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud, bool twoSided = false)
    {
        Tri(a, d, c, ua, ud, uc);
        Tri(a, c, b, ua, uc, ub);
        if (twoSided)
        {
            Tri(a, c, d, ua, uc, ud);
            Tri(a, b, c, ua, ub, uc);
        }
    }

    /// <summary>Which faces of a box to draw (the ones nobody can see are left out).</summary>
    [Flags]
    public enum Faces { None = 0, PosX = 1, NegX = 2, PosY = 4, NegY = 8, PosZ = 16, NegZ = 32, Sides = PosX | NegX | PosZ | NegZ, All = 63 }

    /// <summary>An axis-aligned box. Texture in metres on every face, v down the sides; bottom corners take the baked shadow.</summary>
    public void Box(Vector3 min, Vector3 max, bool top = true, bool bottom = true) =>
        Box(min, max, Faces.Sides | (top ? Faces.PosY : 0) | (bottom ? Faces.NegY : 0));

    public void Box(Vector3 min, Vector3 max, Faces faces)
    {
        var (x0, y0, z0, x1, y1, z1) = (min.X, min.Y, min.Z, max.X, max.Y, max.Z);
        float lo = 1 - Baked;
        // Sides: u across, v down from the top edge (so textures hang the right way up). a→b is the bottom edge, seen
        // from outside, left to right.
        void Side(Vector3 a, Vector3 b, float ua, float ub)
        {
            var at = a with { Y = y1 };
            var bt = b with { Y = y1 };
            Tri(at, a, b, new(ua, -y1), new(ua, -y0), new(ub, -y0), 1, lo, lo);
            Tri(at, b, bt, new(ua, -y1), new(ub, -y0), new(ub, -y1), 1, lo, 1);
        }
        if (faces.HasFlag(Faces.PosZ))
            Side(new(x0, y0, z1), new(x1, y0, z1), x0, x1);
        if (faces.HasFlag(Faces.NegZ))
            Side(new(x1, y0, z0), new(x0, y0, z0), -x1, -x0);
        if (faces.HasFlag(Faces.PosX))
            Side(new(x1, y0, z1), new(x1, y0, z0), -z1, -z0);
        if (faces.HasFlag(Faces.NegX))
            Side(new(x0, y0, z0), new(x0, y0, z1), z0, z1);
        if (faces.HasFlag(Faces.PosY))
        {
            Tri(new(x0, y1, z1), new(x1, y1, z1), new(x1, y1, z0), new(x0, z1), new(x1, z1), new(x1, z0));
            Tri(new(x0, y1, z1), new(x1, y1, z0), new(x0, y1, z0), new(x0, z1), new(x1, z0), new(x0, z0));
        }
        if (faces.HasFlag(Faces.NegY))
        {
            Tri(new(x0, y0, z0), new(x1, y0, z0), new(x1, y0, z1), new(x0, z0), new(x1, z0), new(x1, z1), lo, lo, lo);
            Tri(new(x0, y0, z0), new(x1, y0, z1), new(x0, y0, z1), new(x0, z0), new(x1, z1), new(x0, z1), lo, lo, lo);
        }
    }

    public void BoxAt(Vector3 centre, Vector3 half, bool top = true, bool bottom = true) => Box(centre - half, centre + half, top, bottom);

    /// <summary>
    /// A box with its long edges chamfered by <paramref name="bevel"/> (GDD §27 "big bevels"): the eight-sided section is
    /// in X–Y, run along Z. Ends capped flat.
    /// </summary>
    public void BevelBox(Vector3 min, Vector3 max, float bevel, bool caps = true)
    {
        float b = MathF.Min(bevel, MathF.Min(max.X - min.X, max.Y - min.Y) * 0.45f);
        Vector2[] p =
        [
            new(min.X + b, min.Y), new(max.X - b, min.Y), new(max.X, min.Y + b), new(max.X, max.Y - b),
            new(max.X - b, max.Y), new(min.X + b, max.Y), new(min.X, max.Y - b), new(min.X, min.Y + b),
        ];
        Prism(p, min.Z, max.Z, caps, smooth: false);
    }

    /// <summary>
    /// A profile in X–Y (counter-clockwise seen from +Z) extruded from <paramref name="z0"/> to <paramref name="z1"/>. Texture
    /// runs round the profile (u, metres) and along Z (v). Smooth shading averages normals round the section.
    /// </summary>
    /// <param name="lengthwise">Texture u along the length (z) and v round the profile, so courses of brick run along a
    /// tunnel's bore rather than up its walls.</param>
    public void Prism(IReadOnlyList<Vector2> profile, float z0, float z1, bool caps, bool smooth, bool lengthwise = false)
    {
        int n = profile.Count;
        float u = 0;
        for (int i = 0; i < n; i++)
        {
            var a = profile[i];
            var b = profile[(i + 1) % n];
            float len = (b - a).Length();
            var pa0 = new Vector3(a, z0);
            var pb0 = new Vector3(b, z0);
            var pa1 = new Vector3(a, z1);
            var pb1 = new Vector3(b, z1);
            // Outward normal of this edge (counter-clockwise profile).
            var face = Vector3.Normalize(new Vector3(b.Y - a.Y, a.X - b.X, 0));
            var na = face;
            var nb = face;
            if (smooth)
            {
                var prev = profile[(i - 1 + n) % n];
                var next = profile[(i + 2) % n];
                na = Vector3.Normalize(face + Vector3.Normalize(new Vector3(a.Y - prev.Y, prev.X - a.X, 0)));
                nb = Vector3.Normalize(face + Vector3.Normalize(new Vector3(next.Y - b.Y, b.X - next.X, 0)));
            }
            // Seen from outside: pa1 (back) … the quad pa0, pb0, pb1, pa1 winds counter-clockwise from outside.
            Vector2 T(float along, float z) => lengthwise ? new(z, along) : new(along, z);
            Tri(pa0, pb0, pb1, na, nb, nb, T(u, z0), T(u + len, z0), T(u + len, z1));
            Tri(pa0, pb1, pa1, na, nb, na, T(u, z0), T(u + len, z1), T(u, z1));
            u += len;
        }
        if (!caps)
            return;
        var centre = Vector2.Zero;
        foreach (var p in profile)
            centre += p;
        centre /= n;
        for (int i = 0; i < n; i++)
        {
            var a = profile[i];
            var b = profile[(i + 1) % n];
            // The back cap faces +Z, the front −Z.
            Tri(new Vector3(centre, z1), new Vector3(a, z1), new Vector3(b, z1), centre, a, b);
            Tri(new Vector3(centre, z0), new Vector3(b, z0), new Vector3(a, z0), centre * new Vector2(-1, 1), b * new Vector2(-1, 1), a * new Vector2(-1, 1));
        }
    }

    /// <summary>A regular polygon of <paramref name="sides"/> round the origin, counter-clockwise, first vertex on a flat's edge.</summary>
    public static Vector2[] Polygon(int sides, float radius, float rotate = 0.5f)
    {
        var p = new Vector2[sides];
        for (int i = 0; i < sides; i++)
        {
            float a = (i + rotate) * MathF.Tau / sides;
            p[i] = new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius;
        }
        return p;
    }

    /// <summary>
    /// A faceted cylinder (or a frustum, with <paramref name="radiusB"/>) from <paramref name="a"/> to <paramref name="b"/>.
    /// Texture wraps round it in metres. Caps flat.
    /// </summary>
    public void Cylinder(Vector3 a, Vector3 b, float radius, int sides, bool caps = true, bool smooth = true, float? radiusB = null, bool capA = true, bool capB = true)
    {
        var axis = b - a;
        float length = axis.Length();
        if (length < 1e-5f)
            return;
        var dir = axis / length;
        var side = Vector3.Normalize(Vector3.Cross(dir, MathF.Abs(dir.Y) > 0.9f ? Vector3.UnitX : Vector3.UnitY));
        var up = Vector3.Cross(side, dir);
        float rb = radiusB ?? radius;
        float slope = (radius - rb) / length;
        Vector3 Ring(int i, float r) => (side * MathF.Cos(i * MathF.Tau / sides) + up * MathF.Sin(i * MathF.Tau / sides)) * r;
        Vector3 Normal(int i) => Vector3.Normalize(side * MathF.Cos(i * MathF.Tau / sides) + up * MathF.Sin(i * MathF.Tau / sides) + dir * slope);
        float circumference = MathF.Tau * MathF.Max(radius, rb);
        for (int i = 0; i < sides; i++)
        {
            int j = i + 1;
            var a0 = a + Ring(i, radius);
            var a1 = a + Ring(j, radius);
            var b0 = b + Ring(i, rb);
            var b1 = b + Ring(j, rb);
            float u0 = circumference * i / sides, u1 = circumference * j / sides;
            var n0 = smooth ? Normal(i) : Vector3.Normalize(Normal(i) + Normal(j));
            var n1 = smooth ? Normal(j) : n0;
            Tri(a0, b0, b1, n0, n0, n1, new(u0, 0), new(u0, length), new(u1, length));
            Tri(a0, b1, a1, n0, n1, n1, new(u0, 0), new(u1, length), new(u1, 0));
        }
        if (!caps)
            return;
        for (int i = 0; i < sides; i++)
        {
            int j = i + 1;
            var ra = Ring(i, radius);
            var rj = Ring(j, radius);
            if (capA)
                Tri(a, a + ra, a + rj, new(0, 0), new(Vector3.Dot(ra, side), Vector3.Dot(ra, up)), new(Vector3.Dot(rj, side), Vector3.Dot(rj, up)));
            var rbi = Ring(i, rb);
            var rbj = Ring(j, rb);
            if (capB)
                Tri(b, b + rbj, b + rbi, new(0, 0), new(Vector3.Dot(rbj, side), Vector3.Dot(rbj, up)), new(Vector3.Dot(rbi, side), Vector3.Dot(rbi, up)));
        }
    }

    /// <summary>
    /// A lathed form round the vertical axis through <paramref name="baseCentre"/>: the profile is (radius, height) from the
    /// bottom up. Stacks, domes, lamps, bells. Texture: u round, v down the profile.
    /// </summary>
    public void Lathe(Vector3 baseCentre, IReadOnlyList<Vector2> profile, int sides, bool smooth = true, bool capTop = true)
    {
        float v = 0;
        for (int k = 0; k + 1 < profile.Count; k++)
        {
            var p0 = profile[k];
            var p1 = profile[k + 1];
            float seg = (p1 - p0).Length();
            var d = Vector2.Normalize(p1 - p0);
            var n2 = new Vector2(d.Y, -d.X); // outward in (r, h)
            for (int i = 0; i < sides; i++)
            {
                int j = i + 1;
                float ai = i * MathF.Tau / sides, aj = j * MathF.Tau / sides;
                Vector3 P(Vector2 p, float a) => baseCentre + new Vector3(MathF.Cos(a) * p.X, p.Y, -MathF.Sin(a) * p.X);
                Vector3 N(float a) => Vector3.Normalize(new Vector3(MathF.Cos(a) * n2.X, n2.Y, -MathF.Sin(a) * n2.X));
                var nm = smooth ? default : Vector3.Normalize(N(ai) + N(aj));
                float u0 = MathF.Max(p0.X, p1.X) * ai, u1 = MathF.Max(p0.X, p1.X) * aj;
                // Counter-clockwise from outside: going round by increasing angle is clockwise seen from above, so the
                // quad runs i (low) → j (low) → j (high).
                Tri(P(p0, ai), P(p1, aj), P(p1, ai), smooth ? N(ai) : nm, smooth ? N(aj) : nm, smooth ? N(ai) : nm,
                    new(u0, -v), new(u1, -v - seg), new(u0, -v - seg));
                Tri(P(p0, ai), P(p0, aj), P(p1, aj), smooth ? N(ai) : nm, smooth ? N(aj) : nm, smooth ? N(aj) : nm,
                    new(u0, -v), new(u1, -v), new(u1, -v - seg));
            }
            v += seg;
        }
        var top = profile[^1];
        if (capTop && top.X > 1e-4f)
            for (int i = 0; i < sides; i++)
            {
                float ai = i * MathF.Tau / sides, aj = (i + 1) * MathF.Tau / sides;
                var c = baseCentre + new Vector3(0, top.Y, 0);
                var pi = baseCentre + new Vector3(MathF.Cos(ai) * top.X, top.Y, -MathF.Sin(ai) * top.X);
                var pj = baseCentre + new Vector3(MathF.Cos(aj) * top.X, top.Y, -MathF.Sin(aj) * top.X);
                Tri(c, pi, pj, new(0, 0), new(pi.X - c.X, pi.Z - c.Z), new(pj.X - c.X, pj.Z - c.Z));
            }
    }

    /// <summary>A flat disc facing <paramref name="normal"/>: gauge faces, lamp lenses, smokebox doors.</summary>
    public void Disc(Vector3 centre, Vector3 normal, float radius, int sides, Vector2? uvCentre = null, float uvRadius = 0)
    {
        var n = Vector3.Normalize(normal);
        var side = Vector3.Normalize(Vector3.Cross(MathF.Abs(n.Y) > 0.9f ? Vector3.UnitX : Vector3.UnitY, n));
        var up = Vector3.Cross(n, side);
        var uc = uvCentre ?? Vector2.Zero;
        float ur = uvCentre is null ? radius : uvRadius;
        for (int i = 0; i < sides; i++)
        {
            float a0 = i * MathF.Tau / sides, a1 = (i + 1) * MathF.Tau / sides;
            var d0 = side * MathF.Cos(a0) + up * MathF.Sin(a0);
            var d1 = side * MathF.Cos(a1) + up * MathF.Sin(a1);
            Tri(centre, centre + d0 * radius, centre + d1 * radius, uc,
                uc + new Vector2(MathF.Cos(a0), -MathF.Sin(a0)) * ur, uc + new Vector2(MathF.Cos(a1), -MathF.Sin(a1)) * ur);
        }
    }

    /// <summary>A thin rod between two points (handrails, ladder rungs, rods): a square section, cheap and readable.</summary>
    public void Rod(Vector3 a, Vector3 b, float halfWidth, int sides = 4)
    {
        Cylinder(a, b, halfWidth, sides, caps: true, smooth: sides > 4);
    }
}
