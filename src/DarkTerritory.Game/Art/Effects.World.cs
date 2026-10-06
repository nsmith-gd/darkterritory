using System.Numerics;
using Ballast.Render;

namespace DarkTerritory.Game.Art;

public sealed partial class Effects
{
    /// <summary>
    /// Coal pouring from a coaling tower's chute (spec D.2, GDD §12): lumps out of the spout at <paramref name="top"/>
    /// falling and tumbling the <paramref name="drop"/> down to the tender, a curtain of black dust round them, and where
    /// it lands a grey cloud kicked up and drifting off. Camera-relative; <paramref name="right"/>, <paramref name="along"/>
    /// the line's.
    /// </summary>
    public void CoalPour(MeshBuilder mesh, Vector3 top, Vector3 right, Vector3 along, float drop, double t)
    {
        float fallTime = MathF.Sqrt(2 * drop / 9.8f);
        for (int i = 0; i < 90; i++)
        {
            float h = Hash(i * 1.73f + 0.6f), h2 = Hash(i * 3.91f + 2.2f);
            float age = (float)((t + h * 7) % fallTime);
            float fall = 0.6f * age + 4.9f * age * age;
            var p = top - Vector3.UnitY * fall + right * ((h - 0.5f) * (0.3f + 0.25f * age)) + along * ((h2 - 0.5f) * (0.4f + 0.25f * age));
            float size = 0.14f + 0.22f * h2;
            mesh.Billboard(p, size, age * (3 + 6 * h), new Vector4(0.03f, 0.028f, 0.026f, 1), _spark, FxBlend.Alpha, (i & 1) == 0 ? 2 : 0, 2);
        }
        for (int i = 0; i < 14; i++)
        {
            float h = Hash(i * 2.53f + 9.1f);
            float s = (float)((t * 0.6 + h * 5) % 1.0);
            var p = top - Vector3.UnitY * (drop * s) + right * ((h - 0.5f) * 0.6f) + along * (0.3f * (h - 0.5f));
            mesh.Billboard(p, 0.7f + 0.8f * s, h * 6.28f, new Vector4(0.16f, 0.15f, 0.14f, 0.28f * (1 - s * 0.5f)), _smoke, FxBlend.Alpha, (int)(s * 8), 4);
        }
        for (int i = 0; i < 12; i++)
        {
            float h = Hash(i * 4.77f + 1.3f), h2 = Hash(i * 1.19f + 6.6f);
            float period = 2.5f + 1.5f * h;
            float s = (float)((t + h * 9) % period) / period;
            var p = top - Vector3.UnitY * (drop - 0.2f - 1.4f * s) + right * ((h2 - 0.5f) * (0.8f + 2.5f * s)) + along * ((h - 0.5f) * (0.6f + 2f * s)) + new Vector3(0.6f, 0, 0.3f) * s;
            mesh.Billboard(p, 0.8f + 2.4f * s, h * 6.28f, new Vector4(0.2f, 0.19f, 0.18f, 0.45f * MathF.Sin(MathF.PI * s)), _smoke, FxBlend.Alpha, (int)(s * 15.99f), 4);
        }
    }

    /// <summary>
    /// A derailment (GDD §14, the night lost), <paramref name="since"/> seconds after it: a shower of sparks off every
    /// truck as the wheels leave the rail and plough the ballast; a cloud of ballast dust rolling out round the length of
    /// the train and settling; and the engine's boiler, its pipes torn, bursting steam out of both sides, the burst going
    /// over to a long hiss and a thin column that's still there when you've climbed out.
    /// </summary>
    public void Derailment(MeshBuilder mesh, IReadOnlyList<Sim.Train.CarFrame> frames, Ballast.Double3 eye, double since)
    {
        if (frames.Count == 0 || since < 0)
            return;
        float a = (float)since;
        for (int c = 0; c < frames.Count; c++)
        {
            var f = frames[c];
            if ((f.Origin - eye).Length > 250)
                continue;
            var right = F(f.Right);
            var back = F(f.Back);
            float l = (float)f.Shape.HalfLength;
            // Sparks: the first second and a half, off each truck, flung out sideways and up, falling.
            if (a < 1.6f)
                foreach (float z in new[] { -l * 0.65f, l * 0.65f })
                    for (int k = 0; k < 14; k++)
                    {
                        float h = Hash(c * 13.1f + z + k * 2.3f), h2 = Hash(c * 7.7f + k * 5.9f + z * 0.3f);
                        float born = h * 1.1f, age = a - born;
                        if (age < 0 || age > 0.6f)
                            continue;
                        float side = h2 < 0.5f ? -1 : 1;
                        var at = f.ToWorld(new Ballast.Double3(side * (TrainKit.HalfGauge + 0.1), 0.25, z)).RelativeTo(eye);
                        var p = at + right * side * (age * (4 + 6 * h2)) + Vector3.UnitY * (age * (3 + 3 * h) - 4.9f * age * age) + back * ((h - 0.5f) * age * 4);
                        mesh.Billboard(p, 0.22f, 0, new Vector4(1.0f, 0.6f, 0.2f, 1 - age / 0.6f), _spark, FxBlend.Additive, 1, 2, stretch: 2f);
                    }
            // Dust: out from under the car each side, low, spreading for a few seconds and hanging on.
            if (a < 14)
                for (int k = 0; k < 10; k++)
                {
                    float h = Hash(c * 3.7f + k * 1.9f), h2 = Hash(c * 9.3f + k * 4.1f);
                    // Thrown out fast as the cars plough in, then slowing (most of its spread in the first two seconds).
                    float s = 1 - MathF.Exp(-a / (1.2f + 1.5f * h));
                    float side = k % 2 == 0 ? -1 : 1;
                    var at = f.ToWorld(new Ballast.Double3(side * (f.Shape.HalfWidth + 0.5), 0.4, (h2 - 0.5) * 2 * l)).RelativeTo(eye);
                    var p = at + right * side * (0.5f + 3.5f * s) + Vector3.UnitY * (0.3f + 1.6f * s);
                    float alpha = 0.7f * MathF.Min(1, a * 5) * (1 - MathF.Max(0, (a - 6) / 8));
                    mesh.Billboard(p, 2f + 6f * s, h * 6.28f + a * 0.05f, new Vector4(0.22f, 0.2f, 0.17f, alpha), _smoke, FxBlend.Alpha, (int)(s * 12), 4);
                }
        }
        // The boiler bursting: out of both sides of the engine, white and hard, then hissing on.
        var e = frames[0];
        if ((e.Origin - eye).Length < 250)
        {
            float burst = a < 3 ? 1 - a / 3 : 0;
            float hiss = 0.35f + 0.65f * burst;
            for (int k = 0; k < 30; k++)
            {
                float h = Hash(k * 2.9f + 0.7f), h2 = Hash(k * 6.3f + 1.4f);
                float period = 0.8f + 0.6f * h;
                float age = (float)((since + h * 5) % period);
                float t = age / period;
                float side = k % 2 == 0 ? -1 : 1;
                var at = e.ToWorld(new Ballast.Double3(side * 0.75, 2.4, -e.Shape.HalfLength * (0.2 + 0.5 * h2))).RelativeTo(eye);
                var p = at + F(e.Right) * side * (age * (3 + 9 * burst)) + Vector3.UnitY * (age * (1.2f + 2.5f * (1 - burst)));
                mesh.Billboard(p, 0.5f + t * (1.8f + 5f * burst), h * 6.28f, new Vector4(0.7f, 0.71f, 0.74f, 0.8f * hiss * (1 - t)), _steam, FxBlend.Alpha, (int)(t * 15.99f), 4);
            }
        }
    }

    /// <summary>
    /// The cold the Choir bring (GDD v1.2 §21, App. A.7; §26 "not neon"): about each bell, at <paramref name="bell"/>, a
    /// faint mist sinking off it the way cold air sinks, and frost glittering down out of it, a few motes at a time, so
    /// that before you hear them sing you see your lamp catch something falling. Swooping (<paramref name="swooping"/>)
    /// it leaves a trail of it behind.
    /// </summary>
    public void ChoirCold(MeshBuilder mesh, Vector3 bell, double t, bool swooping, float seed)
    {
        for (int k = 0; k < 7; k++)
        {
            float h = Hash(seed + k * 2.13f), h2 = Hash(seed * 0.7f + k * 5.31f);
            float period = 2.6f + 1.4f * h;
            float s = (float)((t * 0.8 + h * 9) % period) / period;
            var p = bell + new Vector3((h - 0.5f) * 0.6f, -0.2f - 1.1f * s, (h2 - 0.5f) * 0.6f) * (swooping ? 1.6f : 1);
            mesh.Billboard(p, 0.5f + 1.1f * s, h * 6.28f, new Vector4(0.62f, 0.68f, 0.74f, 0.3f * MathF.Sin(MathF.PI * s)), _steam, FxBlend.Alpha, (int)(s * 15.99f), 4);
        }
        for (int k = 0; k < 12; k++)
        {
            float h = Hash(seed * 1.9f + k * 3.7f), h2 = Hash(seed + k * 7.1f);
            float period = 1.6f + 1.8f * h;
            float s = (float)((t + h * 7) % period) / period;
            float twinkle = 0.5f + 0.5f * MathF.Sin((float)t * (9 + 5 * h2) + k);
            var p = bell + new Vector3((h - 0.5f) * 1.1f + MathF.Sin((float)t + k) * 0.08f, 0.2f - 1.6f * s, (h2 - 0.5f) * 1.1f);
            mesh.Billboard(p, 0.06f, 0, new Vector4(0.6f, 0.75f, 0.9f, 0.9f * (1 - s) * twinkle), _spark, FxBlend.Additive, 0, 2);
        }
    }

    /// <summary>
    /// The Choir coming (GDD v1.2 App. A.7, the checklist's "an arrival beat: the frost before they're seen"): as it
    /// gathers, the air round the train goes to frost before a ghost is in sight, a glitter of it falling slow through the
    /// lamp, more of it the nearer they are (<paramref name="amount"/> 0 to 1). Anchored to the world in cells, as the
    /// corruption's motes are, so the train runs through it; pale blue, each crystal twinkling as it turns.
    /// </summary>
    public void Frost(MeshBuilder mesh, Ballast.Double3 eye, double t, float amount)
    {
        if (amount <= 0)
            return;
        const float cell = 5;
        const int reach = 4;
        int per = (int)MathF.Ceiling(12 * amount);
        long cx0 = (long)Math.Floor(eye.X / cell), cz0 = (long)Math.Floor(eye.Z / cell);
        for (long i = cx0 - reach; i <= cx0 + reach; i++)
            for (long j = cz0 - reach; j <= cz0 + reach; j++)
                for (int m = 0; m < per; m++)
                {
                    float h = Hash(i * 17.31f + j * 61.7f + m * 2.9f), h2 = Hash(i * 29.1f + j * 13.7f + m * 8.3f), h3 = Hash(i * 5.7f + j * 3.9f + m * 1.9f);
                    // Down 0.3 m/s from 3 m over the eye, swaying, then round again.
                    float fall = (float)((t * (0.22 + 0.12 * h3) + h3 * 7) % 7);
                    double wx = (i + h) * cell + Math.Sin(t * 0.6 + h * 6.28) * 0.35, wz = (j + h2) * cell + Math.Cos(t * 0.5 + h2 * 6.28) * 0.35;
                    var p = new Vector3((float)(wx - eye.X), 3 - fall, (float)(wz - eye.Z));
                    float d = new Vector2(p.X, p.Z).Length();
                    float twinkle = MathF.Pow(0.5f + 0.5f * MathF.Sin((float)t * (5 + 4 * h2) + h * 30), 3);
                    float a = amount * MathF.Min(1, MathF.Min(fall, 7 - fall)) * MathF.Max(0, 1 - d / (cell * reach)) * (0.25f + 0.75f * twinkle);
                    // (The plain soft blob, filling its quad, as the corruption's motes: a flipbook's dot is a pixel at this size.)
                    mesh.Billboard(p, 0.04f + 0.04f * h3, 0, new Vector4(0.7f, 0.82f, 1.0f, MathF.Min(1, a * 1.3f)), -1, FxBlend.Additive);
                }
        // And the cold itself: a thin frost haze low on the ground and the roofs round the eye, drifting.
        for (int k = 0; k < 10; k++)
        {
            float h = Hash(k * 4.37f + 0.3f), h2 = Hash(k * 2.11f + 6.1f);
            float a0 = (float)(t * 0.05) + h * 6.28f;
            var p = new Vector3(MathF.Cos(a0) * (3 + 9 * h2), -1.4f + 0.5f * h, MathF.Sin(a0) * (3 + 9 * h2));
            mesh.Billboard(p, 2.2f + 2.5f * h2, h * 6.28f, new Vector4(0.62f, 0.72f, 0.82f, 0.18f * amount), _steam, FxBlend.Alpha, (int)(h * 15.99f), 4);
        }
    }

    /// <summary>What hangs in the air of a corrupted stretch (GDD §30, plan §13.2), by the biome it is.</summary>
    public enum Air { Clean, Ash, Spores, Brass }

    /// <summary>The air a biome has (linegen biomes.json): ash over the colliery towns, sick spores over the tar ponds.</summary>
    public static Air AirOf(string? biome) => biome switch
    {
        "industrialRuin" => Air.Ash,
        "contaminatedMarsh" => Air.Spores,
        _ => Air.Clean,
    };

    /// <summary>
    /// Corruption particulate (GDD §30's corrupted country; the checklist's "corruption particulate"): motes in the air
    /// round the eye, anchored to the world in 6 m cells (so the train runs through them, they don't ride along), ash
    /// drifting down grey and slow, or spores rising, pale and sickly, catching the lamp; or, over a brass field (§30's
    /// contamination, the same brass as the crystals), a fine dust of it hanging, turning slowly, each grain glinting now and
    /// then as it catches the light. Faint: it's the air, not snow.
    /// </summary>
    public void Corruption(MeshBuilder mesh, Ballast.Double3 eye, double t, Air air)
    {
        if (air == Air.Clean)
            return;
        const float cell = 6;
        const int reach = 4;
        long cx0 = (long)Math.Floor(eye.X / cell), cz0 = (long)Math.Floor(eye.Z / cell);
        for (long i = cx0 - reach; i <= cx0 + reach; i++)
            for (long j = cz0 - reach; j <= cz0 + reach; j++)
                for (int m = 0; m < (air == Air.Brass ? 7 : 3); m++)
                {
                    float h = Hash(i * 12.9898f + j * 78.233f + m * 3.1f), h2 = Hash(i * 39.35f + j * 11.13f + m * 7.7f), h3 = Hash(i * 3.3f + j * 9.1f + m * 1.3f);
                    double wx = (i + h) * cell, wz = (j + h2) * cell;
                    float y, size;
                    Vector4 colour;
                    if (air == Air.Ash)
                    {
                        // Down 0.4 m/s over 6 m, swaying, then round again.
                        float fall = (float)((t * 0.4 + h3 * 6) % 6);
                        y = 3.5f - fall;
                        wx += Math.Sin(t * 0.7 + h * 6.28) * 0.4;
                        size = 0.07f + 0.06f * h3;
                        colour = new Vector4(0.42f, 0.41f, 0.39f, 0.75f * MathF.Min(1, MathF.Min(fall, 6 - fall)));
                    }
                    else if (air == Air.Brass)
                    {
                        // Hanging, not falling: a slow drift round its own spot, rising and settling, and a glint.
                        float a0 = (float)(t * (0.25 + 0.2 * h3)) + h * 6.28f;
                        wx += Math.Cos(a0) * 0.6;
                        wz += Math.Sin(a0 * 0.8f) * 0.6;
                        y = 0.2f + 2.6f * h3 * h3 + 0.3f * MathF.Sin(a0 * 0.6f);
                        size = 0.05f + 0.04f * h;
                        float glint = MathF.Pow(0.5f + 0.5f * MathF.Sin((float)t * (2.1f + 1.7f * h2) + h * 40), 6);
                        colour = new Vector4(0.95f, 0.72f, 0.36f, 0.35f + 0.9f * glint);
                    }
                    else
                    {
                        float rise = (float)((t * 0.22 + h3 * 5) % 5);
                        y = -1.5f + rise;
                        wz += Math.Sin(t * 0.5 + h2 * 6.28) * 0.3;
                        size = 0.06f + 0.04f * h3;
                        colour = new Vector4(0.7f, 0.76f, 0.42f, 0.9f * MathF.Min(1, MathF.Min(rise, 5 - rise)));
                    }
                    var p = new Vector3((float)(wx - eye.X), y, (float)(wz - eye.Z));
                    float d = new Vector2(p.X, p.Z).Length();
                    colour.W *= MathF.Max(0, 1 - d / (cell * reach));
                    // (The plain soft blob, filling its quad: a flipbook's dot would be a pixel at this size.)
                    if (air == Air.Ash)
                        mesh.Billboard(p, size, 0, colour, -1, FxBlend.Alpha);
                    else if (air == Air.Brass)
                        mesh.Billboard(p, size, 0, colour * new Vector4(0.8f, 0.8f, 0.8f, 1), -1, FxBlend.Additive);
                    else
                        mesh.Billboard(p, size, 0, colour * new Vector4(0.5f, 0.5f, 0.5f, 1), -1, FxBlend.Additive);
                }
    }
}
