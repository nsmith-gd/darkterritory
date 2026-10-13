using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Art;

// The cab's glass in the cold (note 485; GDD §22, §26 "frost on windows and metal, breath vapour").
public sealed partial class SceneArt
{
    /// <summary>Where a crewmate's mouth was last drawn (world), which way they faced, whether they breathe hard, and when.</summary>
    readonly record struct Mouth(Double3 At, Vector3 Facing, bool Hard, double Time);

    readonly Dictionary<byte, Mouth> _mouths = new();

    /// <summary>From how far off the glass's frost and fog are drawn (m): past it they're a pixel.</summary>
    const double GlassSeen = 60;

    /// <summary>The frost's texture across a pane (m a tile): the texture's own (index.json's tileMetres for fx_frost).</summary>
    const float FrostTile = 0.3f;

    /// <summary>The perimeter's step (m): the frost's ragged front is sampled this often along each edge.</summary>
    const float FrostStep = 0.04f;

    int? _frostLayer;

    /// <summary>
    /// The engine's glass in the cold (note 485): on each pane (TrainKit.CabPanes) a frost grown in from its frame, feathered
    /// fronds out of a rimed edge, thickest in the corners and reaching in <c>paneReach</c> at its full, never over the
    /// middle of the view ahead; and a fog breathed onto it by each crewmate whose face is near it (and by the eye, when
    /// <paramref name="eyeBreathes"/>: first person, the viewer's own), on the beat of their breath (Effects.Breath's), going
    /// as they breathe in. Drawn after the crew, so the mouths are this frame's.
    /// </summary>
    /// <param name="frost">How heavy the frost is (0..1): the night's cold's (ColdTuning.Frost), or the Choir's rime.</param>
    public void CabGlass(MeshBuilder mesh, in CarFrame frame, Double3 eye, float frost, double time, bool eyeBreathes = false, int eyeSeed = 0)
    {
        if ((frost <= 0 && Breath <= 0) || frame.Shape.Cab is null || (frame.Origin - eye).Length > GlassSeen)
            return;
        var cold = Look.Tuning.Atmosphere.Cold;
        _frostLayer ??= Look.Layer("fx_frost");
        var m = FrameMatrix(frame, eye);
        var panes = TrainKit.CabPanes(frame.Shape).ToList();
        if (frost > 0)
            for (int i = 0; i < panes.Count; i++)
                PaneFrost(mesh, panes[i], m, frost, cold, _frostLayer.Value, frame.Index * 31 + i);
        if (Breath <= 0)
            return;
        var faces = new List<(Double3 At, Vector3? Facing, bool Hard, int Seed)>();
        foreach (var (id, x) in _mouths)
            if (Math.Abs(x.Time - time) < 0.25)
                faces.Add((x.At, x.Facing, x.Hard, id));
        if (eyeBreathes)
            faces.Add((eye - frame.Up * 0.09, null, false, eyeSeed));
        foreach (var (at, facing, hard, seed) in faces)
        {
            var local = ToF(frame.ToLocal(at));
            var dir = facing is { } f ? ToF(frame.DirToLocal(new Double3(f.X, f.Y, f.Z))) : (Vector3?)null;
            foreach (var pane in panes)
                PaneFog(mesh, pane, m, local, dir, BreathOut(time, seed, hard), cold);
        }
    }

    /// <summary>
    /// How much of a breath is on the glass at <paramref name="time"/> (0..1): fogging up over the breath out
    /// (Effects.Breath's beat: 1.3 s out of every 3.4, or 1.6 hard), then going back as they breathe in, never quite clear
    /// while the face is still at the glass (<see cref="FogStays"/>).
    /// </summary>
    public static float BreathOut(double time, int seed, bool hard)
    {
        float period = hard ? 1.6f : 3.4f, phase = (float)((time + seed * 0.83) % period);
        const float Out = 1.3f;
        float puff = phase < Out ? MathF.Sqrt(phase / Out) : MathF.Exp(-(phase - Out) / 0.7f);
        return FogStays + (1 - FogStays) * puff;
    }

    /// <summary>How much of a breath's fog stays on the glass between breaths, while the face is at it.</summary>
    public const float FogStays = 0.35f;

    /// <summary>
    /// How far in from a pane's frame its frost reaches (m) at <paramref name="frost"/>, at a point <paramref name="along"/>
    /// an edge of <paramref name="length"/> m with its own jag (0..1), from an edge weighted <paramref name="edge"/>
    /// (<see cref="EdgeWeight"/>): further in the corners, and never past
    /// <see cref="FrostMostOfPane"/> of the way to the pane's middle.
    /// </summary>
    public static float FrostReach(ColdTuning cold, float frost, float along, float length, float jag, float paneHalf, float edge = 1)
    {
        float corner = 1 + 0.9f * (1 - Math.Clamp(MathF.Min(along, length - along) / 0.2f, 0, 1));
        return MathF.Min(cold.PaneReach * frost * (0.55f + 0.9f * jag) * corner * edge, FrostMostOfPane * paneHalf);
    }

    /// <summary>
    /// How far the frost reaches in from each edge of a pane, its bottom, side, top, side (the order CabPanes' corner and edges
    /// go round in): furthest up from the sill, where the cold air sits and the frost always starts, least from the top.
    /// </summary>
    static readonly float[] EdgeWeight = [1.35f, 1, 0.75f, 1];

    /// <summary>How much of the way from a pane's frame to its middle the frost may reach, at its deepest.</summary>
    public const float FrostMostOfPane = 0.55f;

    void PaneFrost(MeshBuilder mesh, TrainKit.Pane pane, Matrix4x4 m, float frost, ColdTuning cold, int layer, int seed)
    {
        float a = pane.U.Length(), b = pane.V.Length(), half = MathF.Min(a, b) / 2;
        Vector3 u = pane.U / a, v = pane.V / b;
        // Each pane's frost from its own patch of the texture.
        var offset = new Vector2(Hash(seed, 1), Hash(seed, 2)) * 7;
        float body = cold.PaneDensity * Math.Clamp(frost / 0.15f, 0, 1);
        var outer = new Vector4(cold.PaneColour, body);
        var mid = new Vector4(cold.PaneColour, body * 0.8f);
        var inner = new Vector4(cold.PaneColour, 0);
        // Round the four edges, from each corner along its edge, the inward way.
        var edges = new (Vector3 From, Vector3 Along, float Length, Vector3 In)[]
        {
            (pane.Corner, u, a, v), (pane.Corner + pane.U, v, b, -u), (pane.Corner + pane.U + pane.V, -u, a, -v), (pane.Corner + pane.V, -v, b, u),
        };
        for (int e = 0; e < 4; e++)
        {
            var (from, along, length, inward) = edges[e];
            int steps = Math.Max(2, (int)MathF.Ceiling(length / FrostStep));
            (Vector3 O, Vector3 M, Vector3 I) At(int i)
            {
                float t = length * i / steps;
                // The front's jag: a hash every other step, eased between, so it's ragged and not a saw.
                float k = i / 2f, j0 = Hash(seed * 7 + e, (int)k), j1 = Hash(seed * 7 + e, (int)k + 1), s = k - MathF.Floor(k);
                float reach = FrostReach(cold, frost, t, length, float.Lerp(j0, j1, s * s * (3 - 2 * s)), half, EdgeWeight[e]);
                var o = from + along * t;
                return (o, o + inward * reach * 0.3f, o + inward * reach);
            }
            var prev = At(0);
            for (int i = 1; i <= steps; i++)
            {
                var next = At(i);
                // The rimed edge twice over, the second with the texture turned across the first, so its fronds cross
                // and the edge reads as a crust, not a row of tufts.
                Strip(prev.O, next.O, next.M, prev.M, outer, outer, mid, mid, false);
                Strip(prev.O, next.O, next.M, prev.M, outer, outer, inner, inner, true);
                Strip(prev.M, next.M, next.I, prev.I, mid, mid, inner, inner, false);
                prev = next;
            }
        }

        void Strip(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector4 c0, Vector4 c1, Vector4 c2, Vector4 c3, bool crossed)
        {
            FxVertex V(Vector3 p, Vector4 c)
            {
                var d = p - pane.Corner;
                var uv = crossed ? new Vector2(Vector3.Dot(d, v), -Vector3.Dot(d, u)) * 1.3f + new Vector2(offset.Y, offset.X) : new Vector2(Vector3.Dot(d, u), Vector3.Dot(d, v));
                return new FxVertex(Vector3.Transform(p, m), uv / FrostTile + offset, c, layer);
            }
            FxVertex q0 = V(p0, c0), q1 = V(p1, c1), q2 = V(p2, c2), q3 = V(p3, c3);
            mesh.FxTriangle(FxBlend.Alpha, q0, q1, q2);
            mesh.FxTriangle(FxBlend.Alpha, q0, q2, q3);
        }
    }

    /// <summary>
    /// A breath's fog on a pane (note 485): where the breath meets it (out along the way they face, or straight onto it for
    /// the eye), thicker the nearer the face, a soft patch with a fainter halo, held inside the pane's frame.
    /// </summary>
    void PaneFog(MeshBuilder mesh, TrainKit.Pane pane, Matrix4x4 m, Vector3 mouth, Vector3? facing, float breath, ColdTuning cold)
    {
        float d = Vector3.Dot(mouth - pane.Corner, pane.In);
        if (d < 0.02f || d > cold.FogReach || breath <= 0.01f)
            return;
        float toward = 1;
        var hit = mouth - pane.In * d;
        if (facing is { } f)
        {
            toward = Vector3.Dot(f, -pane.In);
            if (toward < 0.15f)
                return;
            hit = mouth + f * (d / toward);
        }
        float a = pane.U.Length(), b = pane.V.Length();
        Vector3 u = pane.U / a, v = pane.V / b;
        float near = 1 - d / cold.FogReach;
        float r = MathF.Min(0.06f + 0.07f * near, 0.45f * MathF.Min(a, b));
        float x = Vector3.Dot(hit - pane.Corner, u), y = Vector3.Dot(hit - pane.Corner, v) + 0.03f;
        if (x < -r || x > a + r || y < -r || y > b + r)
            return;
        x = Math.Clamp(x, r, a - r);
        y = Math.Clamp(y, r * 0.8f, b - r * 0.8f);
        float alpha = cold.FogDensity * Breath * breath * MathF.Pow(near, 0.5f) * MathF.Min(1, toward * 1.5f);
        var centre = pane.Corner + u * x + v * y + pane.In * 0.004f;
        Blob(centre, r, r * 0.8f, alpha);
        Blob(centre, MathF.Min(r * 1.7f, MathF.Min(x, a - x)), MathF.Min(r * 1.4f, MathF.Min(y, b - y)), alpha * 0.35f);

        void Blob(Vector3 c, float rx, float ry, float al)
        {
            var colour = new Vector4(cold.FogColour, al);
            FxVertex V(float s, float t) => new(Vector3.Transform(c + u * (rx * (2 * s - 1)) + v * (ry * (2 * t - 1)), m), new Vector2(s, t), colour, -1);
            mesh.FxTriangle(FxBlend.Alpha, V(0, 0), V(1, 0), V(1, 1));
            mesh.FxTriangle(FxBlend.Alpha, V(0, 0), V(1, 1), V(0, 1));
        }
    }

    static float Hash(int a, int b) => (float)((uint)(a * 73856093 ^ b * 19349663) % 10007) / 10007f;
}
