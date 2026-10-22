using System.Numerics;
using Ballast.Render;

namespace DarkTerritory.Game.Art;

/// <summary>
/// Painted masks (GDD §29; ARCHITECTURE §8 note 731; the director's note 58: "friendslop horror so we need to be masked but
/// still have some fun to us"): the coat of paint over a crewmate's gas mask, the stencil on it, and the crew's sigil, small on
/// the mask and big on the engine's side. Each stencil is a flat design in a square from −1 to 1 (+x right, +y up), made of
/// a few brushed shapes, laid onto a surface by a mapping: the mask's curve, or a flat plate. Pieces are built pale and
/// tinted their colour as they're drawn (look.json <c>maskPaints</c>), so one mesh per stencil serves every colour.
/// </summary>
public static class MaskKit
{
    /// <summary>The stencils the art can paint, by look.json maskStencils' keys; anything else paints nothing.</summary>
    public static readonly string[] Known = ["stripe", "cross", "teeth", "star", "number", "moustache", "heart", "crow", "bolt", "smile", "x-eyes", "anchor"];

    /// <summary>
    /// The gas hood (tools/blender/crew.py's gas_mask: a black rubber blob over the whole head, centred at MASK_C with radii
    /// 0.106 across, 0.138 forward, 0.158 up) in the head bone's frame (its root at the neck's top, 1.58 m up and 0.01 m
    /// forward in the figure; +Y up, the face towards −Z), a hair larger so paint lies on it. The eyepieces' brass and lenses,
    /// the chin's filter drum and the cheeks' stand a centimetre and more proud of it, so they come through the paint.
    /// </summary>
    public static readonly Vector3 Centre = new(0, 0.094f, -0.004f);
    public static readonly Vector3 Radii = new(0.108f, 0.16f, 0.14f);
    /// <summary>
    /// The face as paint sees it, between the flying cap's rim and the collar (radians on the hood): <see cref="Yaw"/> either
    /// side of straight ahead, <see cref="Pitch"/> up and down from <see cref="PitchMid"/>. A design's square fills it; its
    /// eyes, at (±0.4, 0.7), are the eyepieces'.
    /// </summary>
    public const float Yaw = 1.0f, Pitch = 0.7f, PitchMid = -0.33f;

    /// <summary>
    /// A point on the hood <paramref name="lift"/> metres proud of it, <paramref name="yaw"/> round from straight ahead towards
    /// the wearer's right (+X) and <paramref name="pitch"/> up (radians).
    /// </summary>
    public static (Vector3 At, Vector3 Normal) OnHood(float yaw, float pitch, float lift)
    {
        var d = new Vector3(MathF.Sin(yaw) * MathF.Cos(pitch), MathF.Sin(pitch), -MathF.Cos(yaw) * MathF.Cos(pitch));
        var n = Vector3.Normalize(d / Radii);
        return (Centre + d * Radii + n * lift, n);
    }

    /// <summary>A design's (u, v) on the face (<see cref="Yaw"/>, <see cref="Pitch"/>), read the right way round from in front.</summary>
    public static (Vector3 At, Vector3 Normal) OnFace(Vector2 uv, float lift) =>
        // (Seen from in front, the wearer's right, +X, is on your left: the design's +u, your right, goes to −X.)
        OnHood(-uv.X * Yaw, PitchMid + uv.Y * Pitch, lift);

    /// <summary>
    /// The coat of paint over the mask (its base colour): the hood's face, cap rim to collar and cheek to cheek, its edge
    /// rounded as a brush leaves it. Flat paint, lit a breath so it's found by lamplight (as the armband is).
    /// </summary>
    /// <param name="visor">The welder's visor is down over the face (crew.py's variants 1 and 3): the coat's on it instead,
    /// the dark glass of its slot left bare.</param>
    public static MeshAsset Coat(Look? look, bool visor = false)
    {
        var k = Paint(look, 3120);
        var f = new Flat();
        if (visor)
        {
            f.Rect(Vector2.Zero, Vector2.One);
            Lay(k, f, uv => OnVisor(uv, 0.002f), InSlot);
            return k.Build("mask-coat-visor");
        }
        var outline = new List<Vector2>();
        for (int i = 0; i < 24; i++)
        {
            float a = i * MathF.Tau / 24;
            float x = MathF.Cos(a), y = MathF.Sin(a);
            // A squircle: fuller than an ellipse, so it covers the cheeks and the chin.
            outline.Add(new Vector2(MathF.Sign(x) * MathF.Pow(MathF.Abs(x), 0.6f), MathF.Sign(y) * MathF.Pow(MathF.Abs(y), 0.6f)));
        }
        f.Poly([.. outline]);
        Lay(k, f, uv => OnFace(uv, 0.002f));
        return k.Build("mask-coat");
    }

    /// <summary>
    /// A stencil on the mask, over the coat: the design laid on the face a little inside the coat's edge and proud of it.
    /// <paramref name="number"/> is the wearer's, for the "number" stencil.
    /// </summary>
    public static MeshAsset OnTheMask(Look? look, string key, int number, bool visor = false)
    {
        var k = Paint(look, 3121);
        if (visor)
            Lay(k, Design(key, number), uv => OnVisor(uv * 0.92f, 0.004f), uv => InSlot(uv * 0.92f));
        else
            Lay(k, Design(key, number), uv => OnFace(uv * 0.92f, 0.005f));
        return k.Build($"mask-stencil-{key}-{number}{(visor ? "-visor" : "")}");
    }

    /// <summary>
    /// The welder's visor down over the face (tools/blender/crew.py welder_visor: a patch of an ellipsoid round the temple
    /// pivots PIVOT, radii 0.13 across, 0.158 forward, 0.16 up, ±0.93 rad round and from 1.635 to 1.8 m up), in the head
    /// bone's frame, for a design's (u, v): its whole front, read the right way round from in front.
    /// </summary>
    public static (Vector3 At, Vector3 Normal) OnVisor(Vector2 uv, float lift)
    {
        // (The figure in Blender, facing +Y with +Z up; into the head bone's frame: x, z − 1.58, −(y − 0.01).)
        float a = -uv.X * VisorAround, z = VisorMid + uv.Y * VisorHalfHeight;
        float dz = z - VisorPivot.Z, h = MathF.Sqrt(MathF.Max(0, 1 - dz * dz / (VisorRadii.Z * VisorRadii.Z)));
        var q = new Vector3(MathF.Sin(a) * VisorRadii.X * h, MathF.Cos(a) * VisorRadii.Y * h, dz);
        var nb = Vector3.Normalize(new Vector3(q.X / (VisorRadii.X * VisorRadii.X), q.Y / (VisorRadii.Y * VisorRadii.Y), q.Z / (VisorRadii.Z * VisorRadii.Z)));
        var at = new Vector3(q.X, VisorPivot.Z + q.Z - 1.58f, -(VisorPivot.Y + q.Y - 0.01f));
        var n = new Vector3(nb.X, nb.Z, -nb.Y);
        return (at + n * lift, n);
    }

    /// <summary>The visor's slot of dark glass (crew.py: ±0.38 rad round, 1.69 to 1.716 m up), in a design's square, a hair wide.</summary>
    static bool InSlot(Vector2 uv)
    {
        float a = uv.X * VisorAround, z = VisorMid + uv.Y * VisorHalfHeight;
        return MathF.Abs(a) < 0.4f && z > 1.687f && z < 1.719f;
    }

    static readonly Vector3 VisorPivot = new(0, 0.012f, 1.702f), VisorRadii = new(0.13f, 0.158f, 0.16f);
    const float VisorAround = 0.88f, VisorMid = 1.7175f, VisorHalfHeight = 0.08f;

    /// <summary>
    /// The crew's sigil small on the mask (note 731: "each crewmate's mask carries a small copy"): on the flying cap's earflap
    /// on each side (crew.py: buckled down over the hood at x ±0.108, 1.655 m up), a badge the size of a palm, off the painted
    /// face, so it's the crew's mark whatever the wearer's painted, seen from beside them.
    /// </summary>
    public static MeshAsset SigilOnTheMask(Look? look, string key)
    {
        var k = Paint(look, 3122);
        var f = Design(key, 0);
        foreach (int side in new[] { -1, 1 })
        {
            // Seen from the wearer's right (+X), your right is towards their face (−Z); from their left, away from it.
            var n = new Vector3(side, 0, 0);
            var right = new Vector3(0, 0, -side);
            var centre = new Vector3(side * SigilOut, SigilUp, SigilBack);
            Lay(k, f, uv => (centre + (right * uv.X + Vector3.UnitY * uv.Y) * SigilHalf, n));
        }
        return k.Build($"mask-sigil-{key}");
    }

    /// <summary>The earflap's badge in the head bone's frame: out from the middle, up, back (m), and its half-size.</summary>
    const float SigilOut = 0.124f, SigilUp = 0.072f, SigilBack = 0.004f, SigilHalf = 0.034f;

    /// <summary>
    /// The crew's sigil on the engine (note 731: "painted on the engine's tender/cab side"): on each side of the big cab
    /// (note 716), on the plain plate of its waist under the gallery windows (the hood behind it is all braces), a third of the
    /// way back, a metre or so across inside a ring, the shops' way of making a mark read on dark iron. In
    /// <paramref name="ink"/> (look.json sigilInk).
    /// </summary>
    public static MeshAsset EngineSigil(Look? look, Sim.Train.CarShape shape, string key, Vector3 ink)
    {
        var k = Paint(look, 3123);
        k.Tint *= ink;
        foreach (int side in new[] { -1, 1 })
        {
            if (SigilSpot(shape, side) is not var (centre, size))
                continue;
            var n = new Vector3(side, 0, 0);
            var right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, n));
            (Vector3, Vector3) On(Vector2 uv) => (centre + (right * uv.X + Vector3.UnitY * uv.Y) * (size / 2), n);
            var ring = new Flat();
            ring.Arc(Vector2.Zero, 1.0f, 0, MathF.Tau, 0.09f, 40);
            Lay(k, ring, On);
            Lay(k, Design(key, 0), uv => On(uv * 0.72f));
        }
        return k.Build($"engine-sigil-{key}");
    }

    /// <summary>
    /// Where the engine's sigil is painted on its <paramref name="side"/> (−1 left, +1 right), in its frame, and how big: on the
    /// cab's waist (the longest of its walls out at the body's sides, CarFrame.Engine's, under the gallery windows), five
    /// eighths of the way back towards the doorway, clear of the lamp box half way along; null on a car with no cab.
    /// </summary>
    public static (Vector3 Centre, float Size)? SigilSpot(Sim.Train.CarShape shape, int side)
    {
        var walls = shape.Solids.Where(s => s.Part == Sim.Train.PartKind.CabWall && (side < 0 ? s.Box.Min.X < -0.5 : s.Box.Max.X > 0.5)).ToList();
        if (shape.Cab is null || walls.Count == 0)
            return null;
        var b = walls.MaxBy(s => s.Box.Max.Z - s.Box.Min.Z).Box;
        if (b.Max.Z - b.Min.Z < 2)
            return null;
        float height = (float)(b.Max.Y - b.Min.Y);
        float x = (float)(side < 0 ? b.Min.X : b.Max.X) + side * SigilProud;
        return (new Vector3(x, (float)(b.Min.Y + height * 0.5), (float)(b.Min.Z + (b.Max.Z - b.Min.Z) * 0.62)), MathF.Min(SigilAcross, height * 0.86f));
    }

    /// <summary>The engine's sigil across (m), and how far proud of the cab wall's solid it's painted (out on its armour skin).</summary>
    const float SigilAcross = 1.3f, SigilProud = 0.07f;

    /// <summary>The paint: a pale chipped coat over whatever's under it, matte, lit a breath (the armband's 0.12).</summary>
    static Kit Paint(Look? look, float seed)
    {
        var k = new Kit(look, seed);
        k.Use("mask_paint", new Vector3(0.6f), 0.35f, 0.05f);
        k.Emissive = 0.14f;
        return k;
    }

    /// <summary>A design's triangles laid onto a surface, split finely enough to follow its curve.</summary>
    /// <param name="skip">Where in the design's square nothing's painted (the visor's glass): small pieces there are left out.</param>
    static void Lay(Kit k, Flat f, Func<Vector2, (Vector3 At, Vector3 Normal)> onto, Func<Vector2, bool>? skip = null)
    {
        foreach (var (a, b, c) in f.Triangles)
            Split(k, a, b, c, onto, 0, skip);
    }

    static void Split(Kit k, Vector2 a, Vector2 b, Vector2 c, Func<Vector2, (Vector3 At, Vector3 Normal)> onto, int depth, Func<Vector2, bool>? skip)
    {
        float longest = MathF.Max((b - a).LengthSquared(), MathF.Max((c - b).LengthSquared(), (a - c).LengthSquared()));
        // Finer where something's left out, so the edge of what's skipped is a clean one.
        if (longest > (skip is null ? 0.09f : 0.0025f) && depth < (skip is null ? 5 : 8))
        {
            Vector2 ab = (a + b) / 2, bc = (b + c) / 2, ca = (c + a) / 2;
            Split(k, a, ab, ca, onto, depth + 1, skip);
            Split(k, ab, b, bc, onto, depth + 1, skip);
            Split(k, ca, bc, c, onto, depth + 1, skip);
            Split(k, ab, bc, ca, onto, depth + 1, skip);
            return;
        }
        if (skip is not null && skip((a + b + c) / 3))
            return;
        var (pa, na) = onto(a);
        var (pb, nb) = onto(b);
        var (pc, nc) = onto(c);
        k.Tri(pa, pb, pc, na, nb, nc, (a + Vector2.One) * 0.1f, (b + Vector2.One) * 0.1f, (c + Vector2.One) * 0.1f);
    }

    /// <summary>
    /// A stencil's design (note 731's dozen: a stripe, a cross, a skull's teeth, a star, a number, a moustache, a heart, a crow,
    /// a bolt, a smile, X eyes and, for the Maritime's fortress towns, an anchor). Bold shapes with no fine lines: each is read
    /// at 5 m by a hand lamp, where a mask is a hand's breadth.
    /// </summary>
    public static Flat Design(string key, int number)
    {
        var f = new Flat();
        switch (key)
        {
            case "stripe":
                // A broad band down the middle, the war paint's.
                f.Poly(new(-0.22f, 1), new(-0.26f, -1), new(0.26f, -1), new(0.22f, 1));
                break;
            case "cross":
                f.Rect(new(0, 0), new(0.2f, 0.95f));
                f.Rect(new(0, 0.18f), new(0.75f, 0.2f));
                break;
            case "teeth":
                // A skull's grin: two rows of square teeth across the mouth, the gaps between them the leather's.
                for (int i = 0; i < 5; i++)
                {
                    float x = -0.72f + i * 0.36f;
                    f.Rect(new(x, 0.14f), new(0.14f, 0.2f));
                    f.Rect(new(x, -0.34f), new(0.14f, 0.2f));
                }
                f.Rect(new(0, -0.1f), new(0.88f, 0.035f));
                break;
            case "star":
                {
                    var pts = new Vector2[10];
                    for (int i = 0; i < 10; i++)
                    {
                        float a = MathF.PI / 2 + i * MathF.PI / 5, r = i % 2 == 0 ? 0.95f : 0.4f;
                        pts[i] = new Vector2(MathF.Cos(a), MathF.Sin(a)) * r;
                    }
                    f.Poly(pts);
                    break;
                }
            case "number":
                f.Digit(Math.Clamp(number, 0, 9), new(0, 0), 0.9f);
                break;
            case "moustache":
                // A handlebar under the nose: each side a stroke thick at the middle and thinning out and up to a curl.
                foreach (int s in new[] { -1, 1 })
                    f.Path([new(0.04f * s, 0.12f), new(0.3f * s, 0.06f), new(0.56f * s, -0.02f), new(0.78f * s, 0.12f), new(0.86f * s, 0.32f)], 0.3f, 0.07f);
                break;
            case "heart":
                f.Disc(new(-0.33f, 0.28f), 0.4f);
                f.Disc(new(0.33f, 0.28f), 0.4f);
                f.Poly(new(-0.71f, 0.16f), new(0, -0.9f), new(0.71f, 0.16f), new(0, 0.3f));
                break;
            case "crow":
                // A crow flying at you: its body and head, the wings out and swept up, the fan of the tail.
                f.Ellipse(new(0, -0.05f), 0.16f, 0.36f);
                f.Disc(new(0, 0.33f), 0.15f);
                f.Poly(new(-0.05f, 0.42f), new(0, 0.62f), new(0.05f, 0.42f));
                foreach (int s in new[] { -1, 1 })
                    f.Path([new(0.08f * s, 0.05f), new(0.45f * s, 0.18f), new(0.75f * s, 0.42f), new(0.97f * s, 0.5f)], 0.3f, 0.04f);
                f.Poly(new(-0.08f, -0.3f), new(-0.26f, -0.82f), new(0.26f, -0.82f), new(0.08f, -0.3f));
                break;
            case "bolt":
                // The lightning bolt's zigzag, as two slanted blades.
                f.Poly(new(0.22f, 0.98f), new(-0.42f, 0.0f), new(0.02f, 0.02f), new(0.48f, 0.98f));
                f.Poly(new(-0.02f, 0.18f), new(-0.46f, -0.98f), new(0.42f, -0.02f), new(0.06f, -0.02f));
                break;
            case "smile":
                // Round the eyepieces (their lenses come through it), and a grin under the drum.
                f.Disc(new(-0.43f, 0.68f), 0.24f);
                f.Disc(new(0.43f, 0.68f), 0.24f);
                f.Arc(new(0, 0.1f), 0.72f, MathF.PI * 1.15f, MathF.PI * 1.85f, 0.2f);
                break;
            case "x-eyes":
                // Dead men's eyes: an X over each eyepiece (at (±0.43, 0.68) in the design: MaskKit.Yaw), and a flat mouth under.
                foreach (int s in new[] { -1, 1 })
                {
                    var c = new Vector2(0.43f * s, 0.68f);
                    f.Stroke(c + new Vector2(-0.26f, 0.26f), c + new Vector2(0.26f, -0.26f), 0.15f);
                    f.Stroke(c + new Vector2(-0.26f, -0.26f), c + new Vector2(0.26f, 0.26f), 0.15f);
                }
                f.Stroke(new(-0.5f, -0.6f), new(0.5f, -0.6f), 0.14f);
                break;
            case "anchor":
                f.Ring(new(0, 0.74f), 0.17f, 0.07f);
                f.Stroke(new(0, 0.56f), new(0, -0.72f), 0.15f);
                f.Stroke(new(-0.42f, 0.36f), new(0.42f, 0.36f), 0.13f);
                f.Arc(new(0, -0.18f), 0.62f, MathF.PI * 1.08f, MathF.PI * 1.92f, 0.14f);
                f.Poly(new(-0.7f, -0.05f), new(-0.78f, -0.38f), new(-0.5f, -0.3f));
                f.Poly(new(0.7f, -0.05f), new(0.5f, -0.3f), new(0.78f, -0.38f));
                break;
        }
        return f;
    }

    /// <summary>A design as flat triangles in the square from −1 to 1, each counter-clockwise from its front.</summary>
    public sealed class Flat
    {
        public readonly List<(Vector2 A, Vector2 B, Vector2 C)> Triangles = [];

        public void Tri(Vector2 a, Vector2 b, Vector2 c)
        {
            float cross = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
            if (MathF.Abs(cross) < 1e-7f)
                return;
            Triangles.Add(cross > 0 ? (a, b, c) : (a, c, b));
        }

        /// <summary>A polygon fanned from its middle: for shapes every edge of which can be seen from it (a star, a blade).</summary>
        public void Poly(params Vector2[] pts)
        {
            var mid = Vector2.Zero;
            foreach (var p in pts)
                mid += p;
            mid /= pts.Length;
            for (int i = 0; i < pts.Length; i++)
                Tri(mid, pts[i], pts[(i + 1) % pts.Length]);
        }

        public void Rect(Vector2 centre, Vector2 half) =>
            Poly(centre + new Vector2(-half.X, -half.Y), centre + new Vector2(half.X, -half.Y), centre + half, centre + new Vector2(-half.X, half.Y));

        public void Ellipse(Vector2 centre, float rx, float ry, int sides = 16)
        {
            var pts = new Vector2[sides];
            for (int i = 0; i < sides; i++)
                pts[i] = centre + new Vector2(MathF.Cos(i * MathF.Tau / sides) * rx, MathF.Sin(i * MathF.Tau / sides) * ry);
            Poly(pts);
        }

        public void Disc(Vector2 centre, float r, int sides = 16) => Ellipse(centre, r, r, sides);

        /// <summary>A straight brush stroke <paramref name="width"/> wide.</summary>
        public void Stroke(Vector2 a, Vector2 b, float width) => Path([a, b], width, width);

        /// <summary>A stroke along <paramref name="pts"/>, its width going from <paramref name="w0"/> to <paramref name="w1"/>.</summary>
        public void Path(Vector2[] pts, float w0, float w1)
        {
            for (int i = 0; i + 1 < pts.Length; i++)
            {
                float t0 = i / (float)(pts.Length - 1), t1 = (i + 1) / (float)(pts.Length - 1);
                float h0 = (w0 + (w1 - w0) * t0) / 2, h1 = (w0 + (w1 - w0) * t1) / 2;
                var d = pts[i + 1] - pts[i];
                if (d.LengthSquared() < 1e-8f)
                    continue;
                var side = Vector2.Normalize(new Vector2(-d.Y, d.X));
                Tri(pts[i] - side * h0, pts[i + 1] - side * h1, pts[i + 1] + side * h1);
                Tri(pts[i] - side * h0, pts[i + 1] + side * h1, pts[i] + side * h0);
                // A round joint, so a bent stroke has no notch at its elbow.
                if (i > 0)
                    Disc(pts[i], h0, 8);
            }
        }

        /// <summary>An arc of a circle from <paramref name="a0"/> to <paramref name="a1"/> (radians), <paramref name="width"/> wide.</summary>
        public void Arc(Vector2 centre, float r, float a0, float a1, float width, int steps = 12)
        {
            var pts = new Vector2[steps + 1];
            for (int i = 0; i <= steps; i++)
            {
                float a = a0 + (a1 - a0) * i / steps;
                pts[i] = centre + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r;
            }
            Path(pts, width, width);
        }

        public void Ring(Vector2 centre, float r, float width) => Arc(centre, r, 0, MathF.Tau, width, 16);

        /// <summary>A stencil numeral (the shops' seven strokes), <paramref name="height"/> tall.</summary>
        public void Digit(int d, Vector2 centre, float height)
        {
            // Segments a-g: top, top right, bottom right, bottom, bottom left, top left, middle.
            int[] lit = [0b0111111, 0b0000110, 0b1011011, 0b1001111, 0b1100110, 0b1101101, 0b1111101, 0b0000111, 0b1111111, 0b1101111];
            float h = height, w = height * 0.55f, t = height * 0.2f;
            Vector2 tl = centre + new Vector2(-w / 2, h / 2), tr = centre + new Vector2(w / 2, h / 2);
            Vector2 ml = centre + new Vector2(-w / 2, 0), mr = centre + new Vector2(w / 2, 0);
            Vector2 bl = centre + new Vector2(-w / 2, -h / 2), br = centre + new Vector2(w / 2, -h / 2);
            (Vector2, Vector2)[] seg = [(tl, tr), (tr, mr), (mr, br), (bl, br), (ml, bl), (tl, ml), (ml, mr)];
            for (int s = 0; s < 7; s++)
                if ((lit[d] >> s & 1) != 0)
                {
                    // Each stroke run out past its ends by half its width, so the corners meet square, as a stencil's do.
                    var (a, b) = seg[s];
                    var along = Vector2.Normalize(b - a) * (t / 2);
                    Stroke(a - along, b + along, t);
                }
        }
    }
}
