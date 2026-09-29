using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;

namespace DarkTerritory.Game.Art;

/// <summary>
/// The line as the art pass draws it, around the eye each frame (GDD §30; pipeline "track kit"): the ballast bed and its
/// shoulders grading through a ditch into mud and dead grass, low hills beyond, a gorge under every bridge; sleepers and
/// rails; and the lineside: telegraph poles with their sagging wires, black-forest stands of pines, dead trees, tufts,
/// rocks, fences, and the odd dead signal. Near the track the ground stays at rail height, because that is where the sim
/// stands people (PlayerMotor.GroundAt); the hills only rise where nobody walks.
/// </summary>
public sealed class WorldArt(Look look)
{
    readonly Look _look = look;
    readonly Dictionary<string, MeshAsset> _pieces = new();

    MeshAsset Piece(string key, Func<MeshAsset> make)
    {
        if (!_pieces.TryGetValue(key, out var p))
            _pieces[key] = p = make();
        return p;
    }

    /// <summary>The terrain's cross-section: lateral offsets (m) out from the centre line, and heights at them.</summary>
    static readonly float[] Lateral = [0, 1.55f, 2.35f, 2.95f, 3.7f, 5.5f, 8, 12, 17, 24, 33, 45, 60, 78, 100];
    static readonly float[] Profile = [0.0f, 0.0f, -0.24f, -0.3f, -0.06f, -0.02f, 0, 0, 0, 0, 0, 0, 0, 0, 0];

    const double Wrap = 4096;
    static float W(double v) => (float)(v - Math.Floor(v / Wrap) * Wrap);

    static float Hash(float x) => Frac(MathF.Sin(x * 12.9898f) * 43758.5453f);
    static float Frac(float x) => x - MathF.Floor(x);

    /// <summary>Smooth value noise in 2D, for the hills and the patches of ground.</summary>
    static float Noise(float x, float y)
    {
        float ix = MathF.Floor(x), iy = MathF.Floor(y), fx = x - ix, fy = y - iy;
        fx = fx * fx * (3 - 2 * fx);
        fy = fy * fy * (3 - 2 * fy);
        float H(float a, float b) => Hash(a * 57.1f + b * 113.7f);
        return float.Lerp(float.Lerp(H(ix, iy), H(ix + 1, iy), fx), float.Lerp(H(ix, iy + 1), H(ix + 1, iy + 1), fx), fy);
    }

    /// <summary>How much of a gorge there is under <paramref name="s"/>: 1 across a bridge's span, easing out past its ends.</summary>
    public static float Gorge(Route? route, double s)
    {
        if (route is null)
            return 0;
        float best = 0;
        foreach (var f in route.Features)
        {
            if (f.Kind != FeatureKind.Bridge || s < f.Start - 12 || s > f.End + 12)
                continue;
            double into = Math.Min(s - f.Start, f.End - s);
            float g = into >= 6 ? 1 : (float)Math.Clamp((into + 12) / 18, 0, 1);
            best = MathF.Max(best, g * g * (3 - 2 * g));
        }
        return best;
    }

    /// <summary>Height of the visible ground above rail height at <paramref name="s"/>, <paramref name="lateral"/> m from the line.</summary>
    public static float Ground(Route? route, double s, float lateral, float valleyDepth, float gorge = -1)
    {
        float a = MathF.Abs(lateral);
        float h;
        int i = 1;
        while (i < Lateral.Length - 1 && Lateral[i] < a)
            i++;
        float t = Math.Clamp((a - Lateral[i - 1]) / (Lateral[i] - Lateral[i - 1]), 0, 1);
        h = float.Lerp(Profile[i - 1], Profile[i], t);
        // Hills past the verge: rising with distance, a long wavelength along the line, different each side.
        float hill = MathF.Max(0, (a - 16) / 84);
        float side = lateral < 0 ? 31.7f : 0;
        float n = Noise((float)(s * 0.012) + side, a * 0.025f) * 0.7f + Noise((float)(s * 0.04) + side, a * 0.07f) * 0.3f;
        h += hill * hill * (n * 16 - 3);
        float g = gorge >= 0 ? gorge : Gorge(route, s);
        if (g > 0)
        {
            // The gorge: the floor far below, its sides climbing back out beyond 40 m.
            float wall = Math.Clamp((a - 40) / 50, 0, 1);
            h -= valleyDepth * g * (1 - wall * 0.75f);
        }
        return h;
    }

    /// <summary>
    /// Which pair of ground textures a band of the cross-section blends between (ballast into the ditch's mud; mud into
    /// dead grass; grass into forest floor), by the band's middle; <see cref="GroundBlend"/> says how far, per vertex, so
    /// neighbouring quads agree along their shared edge.
    /// </summary>
    (int A, int B, int Band) GroundLayers(float lateral)
    {
        float a = MathF.Abs(lateral);
        if (a < 2.95f)
            return (_look.Layer("ballast"), _look.Layer("ground_mud"), 0);
        if (a < 12f)
            return (_look.Layer("ground_mud"), _look.Layer("ground_grass"), 1);
        return (_look.Layer("ground_grass"), _look.Layer("ground_forest"), 2);
    }

    static float GroundBlend(int band, float lateral, double s)
    {
        float a = MathF.Abs(lateral);
        return band switch
        {
            0 => Math.Clamp((a - 1.9f) / 1.05f, 0, 1),
            1 => Math.Clamp((a - 3.2f) / 8.8f, 0, 1),
            _ => Math.Clamp((a - 12) / 30 * (0.4f + 1.4f * Noise((float)(s * 0.03), a * 0.05f)), 0, 1),
        };
    }

    /// <summary>The ground's tint at a point: patches, so it isn't one colour to the horizon.</summary>
    static float GroundShade(float lateral, double s) => 0.8f + 0.35f * Noise((float)(s * 0.05), lateral * 0.08f);

    /// <summary>The ballast bed, the ground either side, sleepers and rails, from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public void Track(MeshBuilder mesh, RailLine line, Route? route, Double3 eye, double from, double to, double centre, float valleyDepth)
    {
        var k = new Kit(_look, mesh) { SurfaceOrigin = new Vector3(W(eye.X), W(eye.Y), W(eye.Z)), Baked = 0 };
        var origin = k.SurfaceOrigin;
        const double step = 5;
        int columns = Lateral.Length;
        var left = new Vector3[columns * 2 - 1];
        var right = new Vector3[columns * 2 - 1];
        var gorgeLeft = 0f;
        // Across the line: from the far left to the far right, both sides of one profile.
        float LateralAt(int c) => c < columns - 1 ? -Lateral[columns - 1 - c] : Lateral[c - (columns - 1)];
        void Row(double s, Vector3[] into, out float gorge)
        {
            var sample = line.Sample(s);
            var r = Double3.Cross(sample.Tangent, Double3.Up).Normalized;
            gorge = Gorge(route, s);
            for (int c = 0; c < into.Length; c++)
            {
                float lat = LateralAt(c);
                // On a bridge the bed isn't there: the ground under the deck is the gorge's.
                float h = gorge > 0.5f && MathF.Abs(lat) < 3.7f ? Ground(route, s, 3.7f, valleyDepth, gorge) : Ground(route, s, lat, valleyDepth, gorge);
                into[c] = (sample.Position + r * lat + Double3.Up * h).RelativeTo(eye);
            }
        }
        Row(from, left, out gorgeLeft);
        for (double s = from; s < to; s += step)
        {
            double s1 = Math.Min(s + step, to);
            Row(s1, right, out float gorgeRight);
            for (int c = 0; c + 1 < left.Length; c++)
            {
                float l0 = LateralAt(c), l1 = LateralAt(c + 1), lat = (l0 + l1) / 2;
                var (a, b, band) = GroundLayers(lat);
                bool bridge = (gorgeLeft > 0.5f || gorgeRight > 0.5f) && MathF.Abs(lat) < 3.7f;
                // Per corner: blend and tint (untextured, the greybox's colours for bed and ground).
                var colour = a >= 0 ? Vector3.One : (MathF.Abs(lat) < 2.4f && !bridge ? Palette.Ballast : Palette.MuddyOlive);
                Corner Make(float l, double at) => bridge
                    ? new(colour * GroundShade(l, at), 0.5f)
                    : new(colour * GroundShade(l, at), GroundBlend(band, l, at));
                if (bridge)
                    (a, b) = (_look.Layer("rock_cliff"), _look.Layer("ground_mud"));
                Quad(mesh, left[c], left[c + 1], right[c + 1], right[c], Make(l0, s), Make(l1, s), Make(l1, s1), Make(l0, s1), origin, a, b,
                    a >= 0 && _look.Textures[a].TileMetres is { } tm ? tm : 2);
            }
            (left, right) = (right, left);
            gorgeLeft = gorgeRight;
        }

        // Sleepers, near the eye only (past ~150 m the fog has them anyway), each a little off true.
        const double pitch = 0.68;
        double sleeperFrom = Math.Max(from, centre - 150), sleeperTo = Math.Min(to, centre + 150);
        k.Use("wood_sleeper", Palette.DeepBrown, 0.8f, 0, tile: 1.3f);
        for (double s = Math.Ceiling(sleeperFrom / pitch) * pitch; s < sleeperTo; s += pitch)
        {
            if (route?.BridgeAt(s) is not null)
                continue;
            float j = Hash((float)(s * 1.37));
            k.Tint = Vector3.One * (0.75f + 0.35f * j);
            var t = line.Sample(s);
            var m = Basis(t.Tangent, t.Position, eye, (j - 0.5f) * 0.05f);
            k.With(m, () => k.Box(new Vector3(-1.3f, -0.05f, -0.12f), new Vector3(1.3f, 0.07f, 0.12f), Kit.Faces.All & ~Kit.Faces.NegY));
        }
        // Rails: head, web and foot, in 5 m lengths (a bridge carries them on its deck too).
        k.Use("rail_steel", Palette.IronGrey, 0.4f, 0.6f, tile: 1);
        k.Tint = Vector3.One;
        for (double s = from; s < to; s += step)
        {
            var a = line.Sample(s);
            var b = line.Sample(Math.Min(s + step, to));
            var mid = a.Position + (b.Position - a.Position) * 0.5;
            var dir = (b.Position - a.Position).Normalized;
            float half = (float)((b.Position - a.Position).Length / 2);
            var m = Basis(dir, mid, eye, 0);
            k.With(m, () =>
            {
                foreach (int side in new[] { -1, 1 })
                {
                    float x = side * TrainKit.HalfGauge;
                    k.Box(new Vector3(x - 0.035f, 0.14f, -half), new Vector3(x + 0.035f, 0.19f, half), Kit.Faces.All & ~(Kit.Faces.PosZ | Kit.Faces.NegZ));
                    k.Box(new Vector3(x - 0.01f, 0.08f, -half), new Vector3(x + 0.01f, 0.14f, half), Kit.Faces.PosX | Kit.Faces.NegX);
                    k.Box(new Vector3(x - 0.07f, 0.07f, -half), new Vector3(x + 0.07f, 0.085f, half), Kit.Faces.PosY | Kit.Faces.PosX | Kit.Faces.NegX);
                }
            });
        }
    }

    /// <summary>A transform from a piece's frame (−Z along <paramref name="tangent"/>, +X to its right) to camera-relative space.</summary>
    public static Matrix4x4 Basis(Double3 tangent, Double3 at, Double3 eye, float yaw)
    {
        var fwd = new Vector3((float)tangent.X, (float)tangent.Y, (float)tangent.Z);
        var right = Vector3.Normalize(Vector3.Cross(fwd, Vector3.UnitY));
        var up = Vector3.Cross(right, fwd);
        var back = -fwd;
        var o = at.RelativeTo(eye);
        var m = new Matrix4x4(right.X, right.Y, right.Z, 0, up.X, up.Y, up.Z, 0, back.X, back.Y, back.Z, 0, o.X, o.Y, o.Z, 1);
        return yaw == 0 ? m : Matrix4x4.CreateRotationY(yaw) * m;
    }

    readonly record struct Corner(Vector3 Tint, float Blend);

    /// <summary>A ground quad with world-planar texture coordinates and a two-layer blend, tint and blend per corner.</summary>
    static void Quad(MeshBuilder mesh, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Corner ca, Corner cb, Corner cc, Corner cd, Vector3 origin, int layer, int layer2, float tile)
    {
        void Tri(Vector3 p0, Vector3 p1, Vector3 p2, Corner k0, Corner k1, Corner k2)
        {
            var n = Vector3.Cross(p1 - p0, p2 - p0);
            if (n.LengthSquared() < 1e-10f)
                return;
            n = Vector3.Normalize(n);
            if (n.Y < 0)
            {
                (p1, p2) = (p2, p1);
                (k1, k2) = (k2, k1);
                n = -n;
            }
            void V(Vector3 p, Corner k)
            {
                var w = p + origin;
                mesh.Add(new Vertex(p, n, k.Tint)
                {
                    Surface = w * 128,
                    Wear = 0.45f,
                    Uv = new Vector2(w.X, w.Z) / tile,
                    Layer = layer,
                    Layer2 = layer2,
                    Blend = k.Blend,
                });
            }
            V(p0, k0);
            V(p1, k1);
            V(p2, k2);
        }
        Tri(a, b, c, ca, cb, cc);
        Tri(a, c, d, ca, cc, cd);
    }

    /// <summary>
    /// The lineside: poles with their wires every 50 m, stands of pines and dead trees, tufts near the track, rocks,
    /// fences, and a dead signal now and then. Clear of tunnels, bridges and branches (their own furniture is theirs).
    /// </summary>
    public void Lineside(MeshBuilder mesh, RailLine line, Route? route, Double3 eye, double from, double to, double centre, int seed, float valleyDepth)
    {
        bool Clear(double s) => route is null || (!route.InTunnel(s) && route.BridgeAt(s) is null);
        bool OnBranch(double along, double offset) => line.Branches.Any(b => along > b.Toe - 20 && along < b.End + 20 && Math.Sign(offset) == b.Side
            && Math.Abs(offset) < (b.Kind == BranchKind.Spur ? 60 : 16));
        Matrix4x4 Place(double s, double lateral, float yaw, float scale, float sink = 0)
        {
            var t = line.Sample(s);
            var r = Double3.Cross(t.Tangent, Double3.Up).Normalized;
            float h = Ground(route, s, (float)lateral, valleyDepth) - sink;
            var p = t.Position + r * lateral + Double3.Up * h;
            var up = new Vector3(0, 1, 0);
            var o = p.RelativeTo(eye);
            var fwd = new Vector3((float)t.Tangent.X, 0, (float)t.Tangent.Z);
            fwd = Vector3.Normalize(fwd);
            var right = Vector3.Cross(fwd, up);
            var m = new Matrix4x4(right.X, 0, right.Z, 0, 0, 1, 0, 0, -fwd.X, 0, -fwd.Z, 0, o.X, o.Y, o.Z, 1);
            return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateRotationY(yaw) * m;
        }

        // Telegraph poles and their wires, sagging between them.
        var wire = new Kit(_look, mesh);
        wire.Use("rust_heavy", Palette.SootBlack, 0.2f, 0.2f, tile: 1);
        wire.Shade(0.35f);
        wire.Baked = 0;
        Vector3[]? lastTops = null;
        for (double s = Math.Ceiling(from / 50) * 50; s < to; s += 50)
        {
            if (!Clear(s))
            {
                lastTops = null;
                continue;
            }
            int index = (int)(s / 50);
            var m = Place(s, 4.5, 0, 1);
            mesh.Append(Piece($"pole-{index % 3}", () => WorldKit.Pole(_look, index % 3)), m);
            var tops = WorldKit.Insulators.Select(i => Vector3.Transform(i, m)).ToArray();
            if (lastTops is not null)
                for (int w = 0; w < tops.Length; w++)
                    Wire(wire, lastTops[w], tops[w], 0.45f);
            lastTops = tops;
        }

        // The forest: stands of pines, thinner near the line, thick further out, gaps where the ground is open.
        for (double s = Math.Floor(from / 12) * 12; s < to; s += 12)
        {
            if (!Clear(s))
                continue;
            // A fixed hash, not HashCode.Combine: that one's seeded afresh every process, and the pines would move.
            var rng = new Random(unchecked(seed * 73856093 ^ (int)(s / 12) * 19349663));
            float density = Noise((float)(s * 0.006), seed * 0.1f);
            int count = 2 + (int)(density * 6);
            for (int k = 0; k < count; k++)
            {
                double sideSign = rng.Next(2) == 0 ? -1 : 1;
                double offset = sideSign * (10 + Math.Pow(rng.NextDouble(), 0.7) * 80);
                double along = s + rng.NextDouble() * 12;
                float height = 7 + (float)rng.NextDouble() * 10;
                float yaw = (float)rng.NextDouble() * MathF.Tau;
                int variant = rng.Next(4);
                bool dead = Noise((float)(along * 0.01) + 7, (float)offset * 0.02f) > 0.72f;
                if (OnBranch(along, offset))
                    continue;
                float g = Gorge(route, along);
                if (g > 0.3f && Math.Abs(offset) < 45)
                    continue;
                var piece = dead ? Piece($"dead-{variant % 2}", () => WorldKit.DeadTree(_look, variant % 2, 10))
                    : Piece($"pine-{variant}", () => WorldKit.Pine(_look, variant, 12));
                mesh.Append(piece, Place(along, offset, yaw, (dead ? 0.8f : 1) * height / (dead ? 10 : 12), 0.15f), new Vector3(0.85f + 0.3f * (float)rng.NextDouble()));
            }
        }

        // Near the eye: tufts along the verge, rocks, and stretches of broken fence.
        double nearFrom = Math.Max(from, centre - 90), nearTo = Math.Min(to, centre + 90);
        for (double s = Math.Floor(nearFrom / 3) * 3; s < nearTo; s += 3)
        {
            if (!Clear(s))
                continue;
            var rng = new Random(unchecked(seed * 19349663 ^ (int)(s / 3) * 83492791));
            for (int k = 0; k < 2; k++)
            {
                double side = rng.Next(2) == 0 ? -1 : 1;
                double offset = side * (3.3 + rng.NextDouble() * 12);
                double along = s + rng.NextDouble() * 3;
                if (OnBranch(along, offset))
                    continue;
                bool weed = rng.NextDouble() < 0.06;
                int v = rng.Next(3);
                mesh.Append(Piece($"tuft-{v}-{weed}", () => WorldKit.Tuft(_look, v, weed)), Place(along, offset, (float)rng.NextDouble() * 6.28f, 0.7f + (float)rng.NextDouble() * 0.7f, 0.02f));
            }
            if (rng.NextDouble() < 0.1)
            {
                double side = rng.Next(2) == 0 ? -1 : 1;
                double offset = side * (5 + rng.NextDouble() * 25);
                int v = rng.Next(3);
                if (!OnBranch(s, offset))
                    mesh.Append(Piece($"rock-{v}", () => WorldKit.Rock(_look, v, 1)), Place(s, offset, (float)rng.NextDouble() * 6.28f, 0.4f + (float)rng.NextDouble() * 1.2f, 0.1f));
            }
        }
        for (double s = Math.Floor(from / 3) * 3; s < to; s += 3)
        {
            // A fence runs where the noise says, at 14 m out on the side away from the poles, posts every 3 m.
            if (!Clear(s) || Noise((float)(s * 0.004), 3.3f) < 0.6f || (s - centre) * (s - centre) > 200 * 200)
                continue;
            int index = (int)(s / 3);
            if (Hash(index * 0.37f) < 0.12f)
                continue; // a post gone
            if (OnBranch(s, -14))
                continue;
            mesh.Append(Piece($"fence-{index % 3}", () => WorldKit.FencePost(_look, index % 3)), Place(s, -14, 0, 1, 0.05f));
        }
        for (double s = Math.Ceiling(from / 700) * 700; s < to; s += 700)
            if (Clear(s) && !OnBranch(s, -3.8))
                mesh.Append(Piece($"signal-{(int)(s / 700) % 2 == 0}", () => WorldKit.Signal(_look, (int)(s / 700) % 2 == 0)), Place(s, -3.8, MathF.PI, 1));
    }

    /// <summary>A wire between two points, hanging in a catenary-ish curve: a thin cross of two ribbons, so it reads from any side.</summary>
    static void Wire(Kit k, Vector3 a, Vector3 b, float sag)
    {
        const int n = 6;
        const float w = 0.018f;
        var prev = a;
        var across = Vector3.Normalize(Vector3.Cross(b - a, Vector3.UnitY));
        for (int i = 1; i <= n; i++)
        {
            float t = (float)i / n;
            var p = Vector3.Lerp(a, b, t) - new Vector3(0, sag * 4 * t * (1 - t), 0);
            k.Quad(prev + Vector3.UnitY * w, p + Vector3.UnitY * w, p - Vector3.UnitY * w, prev - Vector3.UnitY * w, twoSided: true);
            k.Quad(prev + across * w, p + across * w, p - across * w, prev - across * w, twoSided: true);
            prev = p;
        }
    }
}
