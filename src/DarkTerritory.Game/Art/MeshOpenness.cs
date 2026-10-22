using System.Numerics;
using Ballast.Render;

namespace DarkTerritory.Game.Art;

/// <summary>
/// How much of a piece's inside shows from outside it (note 869; the director's playtest of 19 Oct, line 7: "LD assets next
/// to tracks, houses and such aren't 3D they are missing sides of geo"). The renderer culls back faces, so a wall a kit
/// leaves out (a face "nobody sees") is a hole straight through the piece from that side. Rays come in at the piece from all
/// round, from the ground up to a steep look down (as a crew on foot, on a roof or on a crane see it), each aimed at a point
/// inside its bounds; one whose first hit is a face's back has looked into it. `dt art closed` lists the pieces by it.
/// </summary>
public static class MeshOpenness
{
    /// <summary>
    /// The share of <paramref name="rays"/> rays at <paramref name="mesh"/> that see into it (0 closed .. 1), and the
    /// directions (horizontal, outward from the piece) the worst of them came from. Deterministic: the rays are laid out
    /// evenly, not drawn at random.
    /// </summary>
    public static (double Inside, int Hits, Vector3 Worst) SeenInside(MeshAsset mesh, int rays = 384)
    {
        var v = mesh.Vertices;
        int tris = v.Length / 3;
        if (tris == 0)
            return (0, 0, default);
        var lo = new Vector3(float.MaxValue);
        var hi = new Vector3(float.MinValue);
        foreach (var x in v)
        {
            lo = Vector3.Min(lo, x.Position);
            hi = Vector3.Max(hi, x.Position);
        }
        var size = hi - lo;
        var centre = (lo + hi) / 2;
        float reach = size.Length() + 1;
        int inside = 0, hits = 0;
        var worst = new Dictionary<int, int>();
        // Around the piece in 32 headings, from level to 50° down, each at a few points spread over it.
        const int headings = 32;
        int per = Math.Max(1, rays / headings);
        for (int h = 0; h < headings; h++)
        {
            float yaw = MathF.Tau * h / headings;
            for (int i = 0; i < per; i++)
            {
                float pitch = 50f * MathF.PI / 180 * ((i * 0.618034f) % 1f);
                var dir = Vector3.Normalize(new Vector3(-MathF.Cos(yaw) * MathF.Cos(pitch), -MathF.Sin(pitch), -MathF.Sin(yaw) * MathF.Cos(pitch)));
                // A point inside the bounds, spread by a low-discrepancy pattern: the middle half of the piece each way.
                var aim = centre + size * 0.5f * new Vector3(((i * 0.7548777f + h * 0.13f) % 1f) - 0.5f, ((i * 0.5698403f + h * 0.29f) % 1f) * 0.9f - 0.45f,
                    ((i * 0.4142136f + h * 0.71f) % 1f) - 0.5f);
                var from = aim - dir * reach;
                float best = float.MaxValue;
                bool front = false, any = false;
                for (int t = 0; t < tris; t++)
                {
                    var a = v[3 * t].Position;
                    var b = v[3 * t + 1].Position;
                    var c = v[3 * t + 2].Position;
                    if (Hit(from, dir, a, b, c) is not { } d || d > best + 1e-3f)
                        continue;
                    // The winding the renderer draws (counter-clockwise seen from in front, Kit's): a face whose normal
                    // points along the ray is its back. Two faces at one distance (a two-sided quad's pair): either's front shows.
                    bool facing = Vector3.Dot(Vector3.Cross(b - a, c - a), dir) < 0;
                    if (d < best - 1e-3f)
                        (best, front) = (d, facing);
                    else
                        front |= facing;
                    any = true;
                }
                if (!any)
                    continue;
                hits++;
                if (!front)
                {
                    inside++;
                    worst[h] = worst.GetValueOrDefault(h) + 1;
                }
            }
        }
        var w = worst.Count == 0 ? default : worst.MaxBy(kv => kv.Value).Key is var k
            ? new Vector3(MathF.Cos(MathF.Tau * k / headings), 0, MathF.Sin(MathF.Tau * k / headings)) : default;
        return (hits == 0 ? 0 : (double)inside / hits, hits, w);
    }

    // Möller–Trumbore, both sides: the distance along the ray, or null.
    static float? Hit(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c)
    {
        var e1 = b - a;
        var e2 = c - a;
        var p = Vector3.Cross(d, e2);
        float det = Vector3.Dot(e1, p);
        if (MathF.Abs(det) < 1e-9f)
            return null;
        float inv = 1 / det;
        var s = o - a;
        float u = Vector3.Dot(s, p) * inv;
        if (u < 0 || u > 1)
            return null;
        var q = Vector3.Cross(s, e1);
        float w = Vector3.Dot(d, q) * inv;
        if (w < 0 || u + w > 1)
            return null;
        float t = Vector3.Dot(e2, q) * inv;
        return t > 1e-4f ? t : null;
    }
}
