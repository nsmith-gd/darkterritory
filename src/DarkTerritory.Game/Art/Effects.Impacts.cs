using System.Numerics;
using Ballast.Render;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Enemies;

namespace DarkTerritory.Game.Art;

public sealed partial class Effects
{
    /// <summary>How long a ball's impact shows (s): its smoke hangs as long as the shot's, and the scorch till the record goes.</summary>
    public const double ImpactSeconds = 8;

    /// <summary>
    /// Where a cannonball came down (T121 playtest: "all cannonballs should have an impact explosion and VFX to show where
    /// impact was"), <paramref name="age"/> seconds after: for a fifth of a second the burst, a star of flash and a ball of
    /// flame with a light that reaches 40 m out, so it reads at night, through fog, from the gun; earth (or splinters, or
    /// porcelain) and sparks thrown up and falling back; and a dirty column of smoke rolling up and spreading for seconds.
    /// On the ground a ring of dust and a scorch with embers in it; into water no fire, a column of spray instead.
    /// </summary>
    /// <param name="at">The impact (camera-relative).</param>
    /// <param name="along">The way the ball was going (unit): the burst throws back off what it hit.</param>
    /// <param name="struck">What it struck, for a creature: the Track Doll goes up in white porcelain.</param>
    /// <param name="seed">The impact's id: each one's own scatter.</param>
    public void CannonImpact(MeshBuilder mesh, Vector3 at, Vector3 along, ImpactSurface surface, EnemyKind struck, double age, long seed)
    {
        if (age < 0 || age > ImpactSeconds)
            return;
        float a = (float)age, s0 = seed % 89 + 0.5f;
        bool water = surface == ImpactSurface.Water, ground = surface == ImpactSurface.Ground;
        var up = Vector3.UnitY;
        // What the burst throws off: straight up off the ground or water, back off a wall, a car or a creature.
        var off = ground || water ? up : Vector3.Normalize(-along with { Y = 0 } + up * 0.8f);
        var side = Vector3.Normalize(Vector3.Cross(MathF.Abs(off.Y) > 0.9f ? Vector3.UnitX : up, off));
        var side2 = Vector3.Cross(off, side);
        if (water)
        {
            Splash(mesh, at, a, s0);
            return;
        }

        // The burst: a white-hot core, a star of flash round it and a wide glare in the air (what carries through fog from
        // the gun), and flame balling out and going.
        if (a < 0.22f)
        {
            float fade = 1 - a / 0.22f;
            // The glare is what's seen of it far off through the fog; close to, it would only wash the burst out, so it grows
            // with distance (as the headlamp's halo does).
            float far = Math.Clamp((at.Length() - 30) / 90, 0, 1);
            mesh.Billboard(at + off * 0.8f, 30 * (0.7f + 0.3f * fade), 0, new Vector4(1.3f, 0.8f, 0.4f, (0.2f + 0.6f * far) * fade), -1, FxBlend.Additive);
            mesh.Billboard(at + off * 0.8f, 11 * (0.6f + 0.4f * fade), s0, new Vector4(1.3f, 1.0f, 0.7f, fade), _flash, FxBlend.Additive, (int)(seed % 4), 2);
            mesh.Billboard(at + off * 0.6f, 4.5f * (0.7f + 0.3f * fade), s0 * 1.7f, new Vector4(2.0f, 1.8f, 1.5f, fade), -1, FxBlend.Additive);
        }
        // The fireball: out fast, then rolling up, reddening and going over a second.
        if (a < 1.3f)
            for (int k = 0; k < 12; k++)
            {
                float h = Hash(s0 * 2.3f + k * 1.9f), h2 = Hash(s0 * 0.9f + k * 3.7f);
                float life = 0.5f + 0.8f * h2;
                if (a > life)
                    continue;
                float t = a / life;
                var dir = Vector3.Normalize(off * (0.6f + h) + side * (h2 - 0.5f) * 1.6f + side2 * (h - 0.5f) * 1.6f);
                var p = at + dir * (0.5f + 3.2f * (1 - MathF.Exp(-a * 7))) + up * (2.2f * t * t + 0.4f);
                var hot = Vector3.Lerp(new Vector3(1.2f, 0.75f, 0.32f), new Vector3(0.8f, 0.25f, 0.06f), t);
                mesh.Billboard(p, (1.6f + 2.2f * h) * (0.7f + 0.9f * MathF.Sqrt(t)), s0 + k + a * 0.6f, new Vector4(hot, (1 - t) * (1 - t)), _flame,
                    FxBlend.Additive, (int)(t * 15.99f), 4);
            }
        // Its light: hard and white-orange for the burst, then the fireball's glow going down over a second.
        if (a < 1.3f)
        {
            float glow = a < 0.1f ? 1 : a < 0.25f ? 1 - 0.6f * (a - 0.1f) / 0.15f : 0.4f * MathF.Pow(1 - (a - 0.25f) / 1.05f, 2);
            mesh.PointLights.Add(new PointLight(at + off * 1.5f, new Vector3(5.0f, 3.2f, 1.5f) * glow, 55));
        }

        // Sparks and burning powder flung out in arcs and falling, the far-seen part of it after the flash.
        for (int k = 0; k < 30; k++)
        {
            float h = Hash(s0 * 3.1f + k * 1.3f), h2 = Hash(s0 + k * 4.7f), h3 = Hash(s0 * 1.9f + k * 2.1f);
            float life = 0.6f + 1.2f * h;
            if (a > life)
                continue;
            var v = Vector3.Normalize(off * (0.6f + h3) + side * (h - 0.5f) * 2 + side2 * (h2 - 0.5f) * 2) * (8 + 12 * h2);
            var p = at + v * a - up * (4.9f * a * a);
            if (p.Y < at.Y)
                continue;
            mesh.Billboard(p, 0.22f + 0.16f * h, 0, new Vector4(1.0f, 0.55f + 0.3f * h3, 0.2f, 1 - a / life), _spark, FxBlend.Additive, 1, 2, stretch: 2.4f);
        }

        // Debris: clods of earth, splinters of a car or a wall, or the doll's porcelain, thrown up and coming down.
        bool porcelain = struck == EnemyKind.TrackDoll;
        var debris = porcelain ? new Vector3(0.85f, 0.82f, 0.78f)
            : surface is ImpactSurface.Train or ImpactSurface.Structure ? new Vector3(0.13f, 0.1f, 0.08f)
            : surface == ImpactSurface.Creature ? new Vector3(0.06f, 0.05f, 0.05f)
            : new Vector3(0.09f, 0.075f, 0.06f);
        int chunks = porcelain ? 34 : 26;
        for (int k = 0; k < chunks; k++)
        {
            float h = Hash(s0 * 5.3f + k * 2.7f), h2 = Hash(s0 * 1.1f + k * 6.1f), h3 = Hash(s0 * 2.9f + k * 3.3f);
            var v = Vector3.Normalize(off * (0.7f + 1.2f * h3) + side * (h - 0.5f) * 1.8f + side2 * (h2 - 0.5f) * 1.8f) * (5 + 9 * h);
            // Up and down again, coming to rest where it lands (the ground's about where the ball came down).
            float flight = MathF.Max(0.2f, 2 * v.Y / 9.8f);
            float t = MathF.Min(a, flight);
            var p = at + v * t - up * (4.9f * t * t) + up * 0.1f;
            float settled = a > flight ? MathF.Max(0, 1 - (a - flight) / 3) : 1;
            if (settled <= 0)
                continue;
            float size = (porcelain ? 0.12f : 0.16f) + 0.18f * h2;
            // Porcelain catches the light (the lamp, the burst) and keeps a little of it; earth is dark.
            var colour = new Vector4(debris * (0.8f + 0.4f * h3), settled);
            mesh.Billboard(p, size, a * (4 + 8 * h), colour, _spark, FxBlend.Alpha, (k & 1) == 0 ? 2 : 0, 2);
            if (porcelain && a < 0.9f)
                mesh.Billboard(p, size * 1.6f, 0, new Vector4(0.6f, 0.62f, 0.66f, 0.5f * (1 - a / 0.9f)), -1, FxBlend.Additive);
        }

        // Smoke: a dirty bank out of the burst, rolling up and spreading, thinning away over the seconds.
        for (int k = 0; k < 20; k++)
        {
            float h = Hash(s0 * 1.7f + k * 2.3f), h2 = Hash(s0 * 0.3f + k * 5.9f), h3 = Hash(s0 + k * 8.1f);
            float born = h * 0.25f, age2 = a - born;
            if (age2 < 0)
                continue;
            float s = age2 / ((float)ImpactSeconds - born);
            float rise = 1 - MathF.Exp(-age2 * 0.9f);
            var p = at + off * (0.6f + 2.5f * rise * (0.6f + 0.6f * h2)) + up * (1.5f * s + 3.5f * s * s)
                + side * (h3 - 0.5f) * (1.2f + 4f * MathF.Sqrt(s)) + side2 * (h - 0.5f) * (1.2f + 4f * MathF.Sqrt(s)) + new Vector3(0.5f, 0, 0.3f) * age2;
            float size = 1.6f + 7f * MathF.Sqrt(s) * (0.7f + 0.5f * h2);
            float alpha = 0.85f * MathF.Pow(1 - s, 1.5f) * MathF.Min(1, age2 * 10);
            // Dirty grey-brown, lit from under by the fire while it burns.
            var colour = new Vector3(0.22f, 0.2f, 0.18f) * (0.8f + 0.4f * h)
                + (age2 < 1.0f ? new Vector3(0.45f, 0.24f, 0.08f) * (1 - age2) : Vector3.Zero);
            mesh.Billboard(p, size, h * 6.28f + age2 * 0.2f * (h2 - 0.5f), new Vector4(colour, alpha), _smoke, FxBlend.Alpha, (int)(MathF.Min(s * 1.4f, 0.999f) * 16), 4);
        }

        if (!ground)
            return;
        // On the ground: a ring of dust thrown out low, a scorch where it burst, and embers in it a while.
        for (int k = 0; k < 14; k++)
        {
            float h = Hash(s0 * 4.1f + k * 1.7f);
            float ang = k * MathF.Tau / 14 + h;
            float spread = 1 - MathF.Exp(-a * 2.2f);
            var p = at + (side * MathF.Cos(ang) + side2 * MathF.Sin(ang)) * (1 + 5.5f * spread) + up * (0.4f + 0.8f * spread);
            float alpha = 0.55f * MathF.Min(1, a * 8) * MathF.Max(0, 1 - a / 5);
            mesh.Billboard(p, 1.5f + 3.5f * spread, h * 6.28f, new Vector4(0.36f, 0.33f, 0.28f, alpha), _smoke, FxBlend.Alpha, (int)(spread * 10), 4);
        }
        float scorch = MathF.Min(1, a * 6) * MathF.Min(1, ((float)ImpactSeconds - a) / 1.5f);
        Decal(mesh, at + up * 0.04f, 1.9f + 0.4f * Hash(s0), s0, new Vector4(0.015f, 0.013f, 0.012f, 0.9f * scorch));
        if (a < 4)
        {
            // The crater's glow, and embers in it going out.
            mesh.Billboard(at + up * 0.3f, 2.4f, 0, new Vector4(0.9f, 0.35f, 0.08f, 0.5f * (1 - a / 4) * (1 - a / 4)), -1, FxBlend.Additive);
            for (int k = 0; k < 10; k++)
            {
                float h = Hash(s0 * 7.3f + k * 2.9f), h2 = Hash(s0 * 2.2f + k * 1.1f);
                float flick = 0.6f + 0.4f * MathF.Sin(a * (9 + 7 * h) + k);
                var p = at + side * (h - 0.5f) * 2.6f + side2 * (h2 - 0.5f) * 2.6f + up * 0.08f;
                mesh.Billboard(p, 0.22f + 0.16f * h2, 0, new Vector4(1.0f, 0.42f, 0.1f, flick * (1 - a / 4)), -1, FxBlend.Additive);
            }
        }
    }

    /// <summary>A ball into water: a column of spray thrown up and falling back, a sheet out sideways, mist, and rings.</summary>
    void Splash(MeshBuilder mesh, Vector3 at, float a, float s0)
    {
        var up = Vector3.UnitY;
        // The plunge's flash of white, for the eye at night (the lamp and the moon in the spray), and a cold light off it.
        if (a < 0.25f)
        {
            float fade = 1 - a / 0.25f;
            mesh.Billboard(at + up * 0.8f, 3.5f, s0, new Vector4(0.8f, 0.85f, 0.9f, 0.7f * fade), -1, FxBlend.Additive);
            mesh.PointLights.Add(new PointLight(at + up * 1.5f, new Vector3(0.9f, 1.0f, 1.15f) * fade, 18));
        }
        for (int k = 0; k < 40; k++)
        {
            float h = Hash(s0 * 2.1f + k * 1.3f), h2 = Hash(s0 * 0.7f + k * 3.9f), h3 = Hash(s0 + k * 6.7f);
            float vy = 6 + 10 * h, vr = 0.6f + 2.4f * h2 * h2;
            float flight = 2 * vy / 9.8f;
            if (a > flight + 0.6f)
                continue;
            float t = MathF.Min(a, flight);
            float ang = h3 * MathF.Tau;
            var p = at + new Vector3(MathF.Cos(ang), 0, MathF.Sin(ang)) * (vr * t) + up * MathF.Max(0, vy * t - 4.9f * t * t);
            float alpha = 0.75f * (a > flight ? 1 - (a - flight) / 0.6f : 1);
            mesh.Billboard(p, 0.5f + 1.4f * (a / (flight + 0.6f)), h * 6.28f, new Vector4(0.72f, 0.76f, 0.8f, alpha), _steam, FxBlend.Alpha, (int)(h2 * 15.99f), 4);
        }
        for (int k = 0; k < 10; k++)
        {
            float h = Hash(s0 * 3.3f + k * 2.2f);
            float s = MathF.Min(1, a / 5);
            float ang = k * MathF.Tau / 10 + h;
            var p = at + new Vector3(MathF.Cos(ang), 0, MathF.Sin(ang)) * (1.5f + 4f * s) + up * (0.5f + 1.2f * s);
            mesh.Billboard(p, 2.5f + 4f * s, h * 6.28f, new Vector4(0.6f, 0.63f, 0.66f, 0.45f * (1 - s)), _steam, FxBlend.Alpha, (int)(s * 15.99f), 4);
        }
        // Rings spreading on the water.
        for (int r = 0; r < 3; r++)
        {
            float t = a - r * 0.35f;
            if (t < 0 || t > 3)
                continue;
            Ring(mesh, at + up * 0.03f, 0.8f + 2.6f * t, new Vector4(0.7f, 0.74f, 0.78f, 0.35f * (1 - t / 3)));
        }
    }

    /// <summary>
    /// A blow or a ball landing on a creature (T121 playtest: "all creatures need hit confirm feedback"), <paramref name="age"/>
    /// seconds after: a hard white-hot pop at the point of it, a flash over the thing itself, and chips thrown on the way the
    /// blow went, with a moment's light so it shows in the dark. A kill flashes harder and longer.
    /// </summary>
    /// <param name="at">The hit point, <paramref name="body"/> the creature's middle (camera-relative).</param>
    public void HitFlash(MeshBuilder mesh, Vector3 at, Vector3 body, Vector3 along, HitSource source, bool killed, double age, long seed)
    {
        float life = killed ? 0.3f : 0.2f;
        if (age < 0 || age > life + 0.4f)
            return;
        float a = (float)age, s0 = seed % 61 + 0.5f;
        float big = source == HitSource.Cannon ? 1.6f : 1;
        // Brought forward out of its body towards the eye (the origin), or the body would hide its own flash.
        if (at.LengthSquared() > 1)
            at -= Vector3.Normalize(at) * 0.45f;
        if (body.LengthSquared() > 1)
            body -= Vector3.Normalize(body) * 0.6f;
        if (a < life)
        {
            float fade = 1 - a / life;
            mesh.Billboard(at, 0.7f * big * (0.7f + 0.3f * fade), s0, new Vector4(1.4f, 1.3f, 1.1f, fade), _flash, FxBlend.Additive, (int)(seed % 4), 2);
            mesh.Billboard(at, 0.3f * big, 0, new Vector4(1.6f, 1.6f, 1.5f, fade), -1, FxBlend.Additive);
            // The flash over its body: it goes white for a blink.
            mesh.Billboard(body, 1.8f * big, 0, new Vector4(0.7f, 0.68f, 0.62f, 0.55f * fade * fade), -1, FxBlend.Additive);
            mesh.PointLights.Add(new PointLight(at - along * 0.3f, new Vector3(1.3f, 1.15f, 0.95f) * fade * big, 5 * big));
        }
        var side = Vector3.Normalize(Vector3.Cross(MathF.Abs(along.Y) > 0.9f ? Vector3.UnitX : Vector3.UnitY, along));
        for (int k = 0; k < 8; k++)
        {
            float h = Hash(s0 * 2.7f + k * 1.9f), h2 = Hash(s0 + k * 3.3f);
            float l = 0.25f + 0.35f * h;
            if (a > l)
                continue;
            var v = Vector3.Normalize(along + side * (h - 0.5f) * 1.4f + Vector3.UnitY * (0.4f + h2)) * (3 + 3 * h2);
            var p = at + v * a - Vector3.UnitY * (4.9f * a * a);
            mesh.Billboard(p, 0.06f + 0.04f * h, 0, new Vector4(1.0f, 0.85f, 0.6f, 1 - a / l), _spark, FxBlend.Additive, 1, 2, stretch: 1.8f);
        }
    }

    /// <summary>How long a killed creature takes to go (GreyboxScene.Deaths): over, still, then crumbled to nothing (s).</summary>
    public const double DeathSeconds = 2.0;

    /// <summary>
    /// A killed creature crumbling (GreyboxScene.Deaths), <paramref name="age"/> seconds after the blow, at
    /// <paramref name="at"/> (camera-relative): from halfway it falls in on itself in grey ash (light enough to show over dark
    /// ground; the effects pass is unlit), a few embers going out in it, the dust settling low over where it lay.
    /// </summary>
    public void Crumble(MeshBuilder mesh, Vector3 at, float age, int seed)
    {
        float start = (float)DeathSeconds * 0.5f;
        if (age < start || age > DeathSeconds + 1.2f)
            return;
        float a = age - start, s0 = seed % 53 + 0.5f;
        for (int k = 0; k < 14; k++)
        {
            float h = Hash(s0 * 1.7f + k * 2.3f), h2 = Hash(s0 + k * 4.1f);
            float t = a - 0.05f * k * h;
            if (t < 0)
                continue;
            float life = 1.1f + 0.8f * h2, s = Math.Clamp(t / life, 0, 1);
            if (s >= 1)
                continue;
            var p = at + new Vector3((h - 0.5f) * 0.9f, 0.15f + 0.5f * h2 + 0.35f * s, (h2 - 0.5f) * 0.9f);
            mesh.Billboard(p, 0.35f + 0.9f * s, h * 6.28f + s, new Vector4(new Vector3(0.34f, 0.32f, 0.3f) * (0.75f + 0.5f * h2), 0.8f * MathF.Sin(MathF.PI * s)), _smoke, FxBlend.Alpha,
                (int)(s * 15.99f), 4);
        }
        for (int k = 0; k < 8; k++)
        {
            float h = Hash(s0 * 3.1f + k * 1.9f), life = 0.6f + 0.6f * h;
            if (a > life)
                continue;
            var p = at + new Vector3((h - 0.5f) * 0.6f, 0.2f + 0.6f * (a / life) * h, (Hash(k + s0) - 0.5f) * 0.6f);
            mesh.Billboard(p, 0.04f, 0, new Vector4(1.0f, 0.45f, 0.15f, 1 - a / life), _spark, FxBlend.Additive, 0, 2);
        }
    }

    /// <summary>A flat soft disc on the ground (a scorch): two alpha triangles of the soft blob, turned by <paramref name="turn"/>.</summary>
    static void Decal(MeshBuilder mesh, Vector3 centre, float radius, float turn, Vector4 colour)
    {
        float c = MathF.Cos(turn) * radius, s = MathF.Sin(turn) * radius;
        var x = new Vector3(c, 0, s);
        var z = new Vector3(-s, 0, c);
        var a = new FxVertex(centre - x - z, new Vector2(0, 0), colour, -1);
        var b = new FxVertex(centre + x - z, new Vector2(1, 0), colour, -1);
        var d = new FxVertex(centre + x + z, new Vector2(1, 1), colour, -1);
        var e = new FxVertex(centre - x + z, new Vector2(0, 1), colour, -1);
        mesh.FxTriangle(FxBlend.Alpha, a, b, d);
        mesh.FxTriangle(FxBlend.Alpha, a, d, e);
    }

    /// <summary>A thin flat ring on the water, of short soft segments.</summary>
    static void Ring(MeshBuilder mesh, Vector3 centre, float radius, Vector4 colour)
    {
        const int segments = 24;
        float w = 0.12f + 0.04f * radius;
        for (int i = 0; i < segments; i++)
        {
            float a0 = i * MathF.Tau / segments, a1 = (i + 1) * MathF.Tau / segments;
            var d0 = new Vector3(MathF.Cos(a0), 0, MathF.Sin(a0));
            var d1 = new Vector3(MathF.Cos(a1), 0, MathF.Sin(a1));
            // The blob's middle row (v 0.5) is its densest across: inner edge at u 0.5 - , outer at 0.5 +.
            var p0 = new FxVertex(centre + d0 * (radius - w), new Vector2(0.5f, 0.0f), colour, -1);
            var p1 = new FxVertex(centre + d0 * (radius + w), new Vector2(0.5f, 1.0f), colour, -1);
            var p2 = new FxVertex(centre + d1 * (radius + w), new Vector2(0.5f, 1.0f), colour, -1);
            var p3 = new FxVertex(centre + d1 * (radius - w), new Vector2(0.5f, 0.0f), colour, -1);
            mesh.FxTriangle(FxBlend.Alpha, p0, p1, p2);
            mesh.FxTriangle(FxBlend.Alpha, p0, p2, p3);
        }
    }
}
