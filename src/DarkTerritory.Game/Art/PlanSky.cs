using System.Numerics;
using Ballast.Render;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Route;

namespace DarkTerritory.Game.Art;

/// <summary>
/// A generated night's far horizon (GDD §28, §30; linegen plan §13): the sky's 360° backdrop band, made for the route
/// from its seed, its tier and its country, in the same depth bands as the art pass's own (<c>tools/art/texgen/mat_sky.py</c>):
/// alpha 1 on silhouettes, lit points cutting through. The sky shader reads a band's value only as how far it lifts from
/// the fog colour toward the horizon's haze (0 to 0.06); this band keeps every layer under the haze, so the highland
/// stands as a dark mass against the paler sky at night instead of dissolving into it.
/// <para>
/// The country is Nova Scotia gone bad: the Cape Breton highland plateau far off, flat-topped and falling to the sea
/// in scarps; drumlins nearer, with a white wooden church's steeple on one; a headland with its lighthouse over a gap
/// of open sea and a fishing village huddled below it; a colliery's headframe, slag heap and smoke in the coal
/// country; and along the horizon the black spruce, spiky and close, with dead snags standing out of it. The deeper
/// the tier, the fewer lights: the lighthouse is out past the Frontier, and in deep territory the steeple has fallen.
/// </para>
/// </summary>
public static class PlanSky
{
    const int K = 2, W = 2048 * K, H = 512 * K;
    const int Horizon = (int)(H * 0.85);

    /// <summary>The band for a route's night, or null for a hand-laid line (the art pass's own band stays).</summary>
    public static Image? For(Route? route) => route?.Plan is { } plan ? For(plan) : null;

    public static Image For(LinePlan plan)
    {
        var rng = new Random(unchecked((int)Streams.Hash(plan.Seed + "/sky")));
        int tier = (int)plan.Route.Tier;
        bool coal = tier >= (int)RouteTier.DeadLines || plan.Biomes.Any(b => b.Biome is "slag" or "industrialRuin");
        var layers = new List<(float[] Top, Vector3 High, Vector3 Low)>();
        var lit = new List<(int X, int Y, int Size, float Strength)>();
        var smoke = new List<(float X, float Y, float Width, float Strength)>();

        // Where the open sea is: one gap in the land, a headland at its edge.
        float seaX = (float)(rng.NextDouble() * W), seaW = W * (0.07f + 0.05f * (float)rng.NextDouble());
        float Sea(int x) => SmoothStep(seaW * 1.1f, seaW * 0.55f, Dist(x, seaX));

        // 1. The highland plateau: flat-topped ranges with steep scarp edges, falling away at the sea.
        var env = Profile(rng, 7, 2);
        var rough = Profile(rng, 90, 4, ridged: true);
        var far = new float[W];
        for (int x = 0; x < W; x++)
        {
            float plateau = SmoothStep(0.42f, 0.5f, env[x]);
            // The plateau's top rolls a little and is serrated by the far forest; its edge falls in scarps.
            float h = H * (0.05f + (0.17f + 0.08f * env[(x + W / 3) % W]) * plateau + 0.025f * rough[x] * (0.4f + plateau));
            far[x] = Horizon - h * (1 - Sea(x));
        }
        layers.Add((far, Hex(0x020304), Hex(0x030405)));

        // 2. Drumlins: long, smooth, whaleback hills, the nearer range, with the spruce fuzz on their backs.
        var mid = new float[W];
        var fuzz = Profile(rng, 700, 2);
        Array.Fill(mid, (float)Horizon);
        var drumlins = new List<(float X, float Width, float Height)>();
        for (int i = 0; i < 26; i++)
        {
            float cx = (float)(rng.NextDouble() * W), w = W * (0.018f + 0.03f * (float)rng.NextDouble()), h = H * (0.03f + 0.07f * (float)rng.NextDouble());
            if (Dist((int)cx, seaX) < seaW * 1.2f)
                continue;
            drumlins.Add((cx, w, h));
        }
        for (int x = 0; x < W; x++)
        {
            float h = H * 0.012f;
            foreach (var (cx, w, dh) in drumlins)
            {
                float d = Dist(x, cx) / w;
                h = MathF.Max(h, dh * MathF.Exp(-d * d * d * d * 0.5f));
            }
            h += H * 0.006f * fuzz[x];
            mid[x] = Horizon - h * (1 - Sea(x) * 0.9f);
        }
        layers.Add((mid, Hex(0x010102), Hex(0x020203)));

        // 3. What stands on the land: the landmarks, in their own near-mid band.
        var things = new float[W];
        Array.Fill(things, float.MaxValue);
        void Shape(float cx, float baseY, Func<float, float> heightAt, float half)
        {
            for (int x = (int)(cx - half) - 1; x <= (int)(cx + half) + 1; x++)
            {
                float h = heightAt(x - cx);
                if (h > 0)
                {
                    int wx = ((x % W) + W) % W;
                    things[wx] = MathF.Min(things[wx], baseY - h);
                }
            }
        }
        // The headland at the sea's edge, and its lighthouse: a tapering white tower, the lantern, a cap.
        float head = seaX + (rng.Next(2) == 0 ? -1 : 1) * seaW * 0.8f;
        float headTop = Horizon - H * 0.09f;
        Shape(head, Horizon, dx => H * 0.09f * Math.Clamp(1 - MathF.Pow(MathF.Abs(dx) / (seaW * 0.45f), 1.6f), 0, 1) + (MathF.Abs(dx) < seaW * 0.2f ? H * 0.004f : 0), seaW * 0.5f);
        float lx = head + (float)(rng.NextDouble() - 0.5) * seaW * 0.15f;
        float tower = 70 * K;
        Shape(lx, headTop + 4 * K, dx =>
        {
            float a = MathF.Abs(dx);
            if (a <= 2.5f * K)
                return tower + 12 * K; // the lantern and its cap
            if (a <= 4.5f * K)
                return tower + 1.5f * K; // the gallery
            if (a <= 6 * K)
                return tower * (6 * K - a) / (1.5f * K); // the tower's taper
            return 0;
        }, 7 * K);
        // The lamp: lit on the near tiers, a dead black lantern past them.
        if (tier <= (int)RouteTier.Frontier)
            lit.Add(((int)lx, (int)(headTop + 4 * K - tower - 8 * K), 3 * K, 1));
        // The fishing village huddled under the headland: saltboxes, one or two windows.
        float vx = head + (head < seaX ? -1 : 1) * seaW * 0.55f;
        for (int i = 0; i < 7; i++)
        {
            float hx = vx + (i - 3) * 16 * K + (float)(rng.NextDouble() - 0.5) * 6 * K, hw = (8 + 6 * (float)rng.NextDouble()) * K, hh = (8 + 7 * (float)rng.NextDouble()) * K;
            bool salt = rng.Next(2) == 0;
            Shape(hx, Horizon + 2 * K, dx =>
            {
                float a = dx + hw / 2;
                if (a < 0 || a > hw)
                    return 0;
                // A saltbox's long back roof: the ridge off centre.
                float ridge = salt ? hw * 0.35f : hw * 0.5f, rise = hh * 0.6f;
                return hh + (a < ridge ? rise * a / ridge : rise * (1 - (a - ridge) / (hw - ridge)));
            }, hw);
            if (rng.NextDouble() < (tier == 0 ? 0.35 : tier == 1 ? 0.2 : 0.06))
                lit.Add(((int)(hx + (float)(rng.NextDouble() - 0.5) * hw * 0.5f), (int)(Horizon + 2 * K - hh * 0.5f), 2 * K, 0.8f));
        }
        // The white church on a drumlin: nave, tower, and a tall needle steeple (broken off in deep territory).
        var hill = drumlins.OrderByDescending(d => d.Height).First();
        float chx = hill.X + (float)(rng.NextDouble() - 0.5) * hill.Width * 0.4f;
        float chBase = mid[(int)chx % W] + 3 * K;
        bool fallen = tier >= (int)RouteTier.DeepTerritory;
        Shape(chx, chBase, dx =>
        {
            if (dx > -22 * K && dx < 0)
                return 14 * K + 7 * K * (1 - MathF.Abs(dx + 11 * K) / (11 * K)); // the nave and its gable
            float a = MathF.Abs(dx - 5 * K);
            return a <= 5 * K ? 34 * K - (fallen ? 6 * K * a / (5 * K) : 0) : 0; // the tower, its top broken if it's fallen
        }, 30 * K);
        if (!fallen)
            Shape(chx + 5 * K, chBase - 34 * K, dx => MathF.Abs(dx) <= 4.5f * K ? 46 * K * (1 - MathF.Abs(dx) / (4.5f * K)) : 0, 6 * K);
        // The coal country: a colliery's headframe on its slag heap, and the smoke off the heap.
        if (coal)
        {
            float cx = (float)((seaX + W * (0.35 + rng.NextDouble() * 0.3)) % W);
            Shape(cx, Horizon + 2 * K, dx => H * 0.07f * MathF.Max(0, 1 - MathF.Abs(dx) / (70 * K)), 72 * K);
            float top = Horizon + 2 * K - H * 0.07f;
            Shape(cx - 20 * K, top + 4 * K, dx =>
            {
                float a = MathF.Abs(dx);
                // An A-frame: two legs and the sheave wheel over them, see-through between.
                float legs = 40 * K * (1 - a / (12 * K));
                bool leg = a <= 12 * K && (a > 12 * K - 3 * K - (40 * K - legs) * 0.02f || a < 2 * K || MathF.Abs(a - 6 * K) < 1.2f * K);
                return a <= 12 * K && leg ? legs : MathF.Abs(dx) < 7 * K ? 44 * K - MathF.Abs(dx) * 0.3f : 0;
            }, 14 * K);
            smoke.Add((cx + 30 * K, top + 10 * K, 10 * K, 0.7f));
            if (tier <= (int)RouteTier.DeadLines)
                lit.Add(((int)(cx + 6 * K), (int)(top - 6 * K), 2 * K, 0.7f));
        }
        for (int x = 0; x < W; x++)
            if (things[x] == float.MaxValue)
                things[x] = H + 10;
        layers.Add((things, Hex(0x000001), Hex(0x010101)));

        // 4. The black spruce along the horizon: narrow spires, close, with dead grey snags standing out of them.
        var near = new float[W];
        Array.Fill(near, H + 10f);
        var band = Profile(rng, 45, 3);
        float xs = 0;
        // Clear of the fishing village, so its roofs show over the shore.
        float Clearing(int x) => MathF.Max(Sea(x), SmoothStep(70 * K, 40 * K, Dist(x, vx)));
        while (xs < W)
        {
            float sea = Clearing((int)xs);
            float h = (8 + 24 * band[(int)xs % W] + (float)rng.NextDouble() * 14) * K * (1 - sea);
            float w = (4.5f + 3.5f * (float)rng.NextDouble()) * K;
            bool snag = rng.NextDouble() < 0.07 + tier * 0.03;
            float x0 = xs;
            float baseY = Horizon + 3 * K - H * 0.012f * band[(int)xs % W] * (1 - sea);
            for (int x = (int)(x0 - w) - 1; x <= (int)(x0 + w) + 1; x++)
            {
                float a = MathF.Abs(x - x0);
                float top;
                if (snag)
                    top = a < 0.8f * K ? h * 1.25f : a < 3 * K && (int)(x0 + h) % 3 == 0 ? h * 0.8f : 0; // a bare pole, a crooked arm
                else
                {
                    // A spire in ragged tiers, the way black spruce stands: a club at the top, notched boughs below.
                    float t = a / w;
                    float tiers = 0.82f + 0.18f * MathF.Abs(MathF.Sin(t * 9 + x0 * 0.37f));
                    top = t > 1 ? 0 : h * MathF.Pow(1 - t, 1.25f) * tiers + (a < 1.4f * K ? h * 0.1f : 0);
                }
                if (top > 0)
                {
                    int wx = ((x % W) + W) % W;
                    near[wx] = MathF.Min(near[wx], baseY - top);
                }
            }
            // The forest's floor line under the spires.
            xs += w * (snag ? 1.2f : 0.45f + 0.5f * (float)rng.NextDouble());
        }
        for (int x = 0; x < W; x++)
            near[x] = MathF.Min(near[x], Horizon + 2 * K - H * 0.014f * band[x] * (1 - Clearing(x)));
        layers.Add((near, Hex(0x000000), Hex(0x010101)));

        // Composite far to near, each band paler toward the horizon (fog pools low); a little grain inside them.
        var col = new Vector3[W * H];
        var alpha = new float[W * H];
        for (int y = 0; y < H; y++)
        {
            float haze = SmoothStep(Horizon - H * 0.35f, Horizon, y);
            for (int x = 0; x < W; x++)
            {
                int i = y * W + x;
                foreach (var (top, high, low) in layers)
                {
                    float m = Math.Clamp(y - top[x] + 0.5f, 0, 1);
                    if (m <= 0)
                        continue;
                    col[i] = Vector3.Lerp(col[i], Vector3.Lerp(high, low, haze), m);
                    alpha[i] += m * (1 - alpha[i]);
                }
                col[i] *= 0.7f + 0.6f * Grain(x / 4, y / 4);
            }
        }
        // Smoke: drifting off with the wind, widening, thinning; over the sky and, thinly, over the land behind.
        var smokeColour = Hex(0x30363E);
        foreach (var (ox, oy, width, strength) in smoke)
            for (int y = 0; y < (int)oy; y++)
            {
                float rise = oy - y;
                float cx = ox + rise * 0.55f + 12 * K * MathF.Sin(rise / (60 * K));
                float wd = width + rise * 0.22f;
                for (int x = (int)(cx - 3 * wd); x < (int)(cx + 3 * wd); x++)
                {
                    int wx = ((x % W) + W) % W, i = y * W + wx;
                    float d = (x - cx) / wd;
                    float a = MathF.Exp(-d * d) * MathF.Exp(-rise / (H * 0.6f)) * strength * (0.45f + 0.9f * Smooth(wx / 40f, y / 40f) * 0.7f + 0.3f * Smooth(wx / 12f, y / 12f));
                    a = Math.Clamp(a * 1.2f, 0, 0.85f);
                    col[i] = Vector3.Lerp(col[i], smokeColour * (0.8f + 0.4f * Smooth(wx / 20f, y / 20f)), alpha[i] > 0.5f ? a * 0.6f : Math.Clamp(a * 4, 0, 1));
                    alpha[i] += a * (1 - alpha[i]);
                }
            }
        // The lights that are left: tiny, amber, emissive.
        foreach (var (lx0, ly0, size, strength) in lit)
            for (int y = ly0; y < ly0 + size && y < H; y++)
                for (int x = lx0; x < lx0 + size; x++)
                {
                    int i = Math.Max(0, y) * W + ((x % W) + W) % W;
                    col[i] = Vector3.Lerp(col[i], Hex(0xE0A050), strength);
                    alpha[i] = 1;
                }
        // Below the horizon: dark ground haze, opaque; the sea a shade greyer and flatter where it's open.
        for (int y = Horizon + 2 * K; y < H; y++)
        {
            float gh = SmoothStep(Horizon, H, y);
            for (int x = 0; x < W; x++)
            {
                int i = y * W + x;
                var ground = Vector3.Lerp(Hex(0x161B21), Hex(0x07090B), gh) * (0.9f + 0.2f * Grain(x / 4, y / 4));
                var sea = Vector3.Lerp(Hex(0x1E242B), Hex(0x0A0D10), gh);
                col[i] = Vector3.Lerp(ground, sea, Sea(x));
                alpha[i] = 1;
            }
        }

        var px = new byte[W * H * 4];
        for (int i = 0; i < W * H; i++)
        {
            px[i * 4] = (byte)Math.Clamp(col[i].X * 255, 0, 255);
            px[i * 4 + 1] = (byte)Math.Clamp(col[i].Y * 255, 0, 255);
            px[i * 4 + 2] = (byte)Math.Clamp(col[i].Z * 255, 0, 255);
            px[i * 4 + 3] = (byte)Math.Clamp(alpha[i] * 255, 0, 255);
        }
        // Built at twice the size and box-filtered, so the tiny lights land as a pixel or two.
        return new Image(W, H, px).Resized(W / K, H / K);
    }

    /// <summary>A periodic 1D fractal profile over the band's width (so the sky wraps), 0..1.</summary>
    static float[] Profile(Random rng, int cells, int octaves, bool ridged = false)
    {
        var total = new float[W];
        float amp = 1, sum = 0;
        for (int o = 0; o < octaves; o++)
        {
            int n = cells << o;
            var lattice = new float[n];
            for (int i = 0; i < n; i++)
                lattice[i] = (float)rng.NextDouble() * 2 - 1;
            for (int x = 0; x < W; x++)
            {
                float f = (float)x / W * n;
                int i0 = (int)f;
                float t = f - i0;
                t = t * t * (3 - 2 * t);
                float v = lattice[i0 % n] + (lattice[(i0 + 1) % n] - lattice[i0 % n]) * t;
                total[x] += amp * (ridged ? 1 - 2 * MathF.Abs(v) : v);
            }
            sum += amp;
            amp *= 0.5f;
        }
        float min = total.Min(), max = total.Max();
        for (int x = 0; x < W; x++)
            total[x] = (total[x] - min) / Math.Max(1e-6f, max - min);
        return total;
    }

    static float Dist(float x, float x0)
    {
        float d = MathF.Abs(x - x0) % W;
        return MathF.Min(d, W - d);
    }

    static float SmoothStep(float a, float b, float x)
    {
        float t = Math.Clamp((x - a) / (b - a), 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>Smooth value noise in 2D over the grain lattice, 0..1.</summary>
    static float Smooth(float x, float y)
    {
        int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y);
        float fx = x - ix, fy = y - iy;
        fx = fx * fx * (3 - 2 * fx);
        fy = fy * fy * (3 - 2 * fy);
        float a = Grain(ix, iy), b = Grain(ix + 1, iy), c = Grain(ix, iy + 1), d = Grain(ix + 1, iy + 1);
        return (a + (b - a) * fx) * (1 - fy) + (c + (d - c) * fx) * fy;
    }

    static float Grain(int x, int y)
    {
        uint h = (uint)(x * 374761393 + y * 668265263);
        h = (h ^ (h >> 13)) * 1274126177;
        return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
    }

    static Vector3 Hex(int rgb) => new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
}
