using System.Numerics;
using Ballast.Render;

namespace DarkTerritory.Game.Art;

public sealed partial class Effects
{
    /// <summary>How long a shot's powder smoke hangs about (s): black powder's is thick and white and slow to go.</summary>
    public const double ShotSmokeSeconds = 6;

    /// <summary>
    /// A cannon's shot (GDD v1.2 §19 "crude cannons", App. C.3), <paramref name="age"/> seconds after it: for a moment the
    /// flash, a ragged star of flame at the muzzle with its jet thrown out along the barrel and the light of it over
    /// everything; burning wadding and sparks flung on ahead and falling; and the powder smoke, a thick grey-white bank
    /// out of the muzzle that slows, rolls, lifts and spreads, left behind by the moving train (the air's still; the car
    /// isn't), so the gun car runs out of its own cloud.
    /// </summary>
    /// <param name="muzzle">The muzzle (camera-relative), <paramref name="along"/> the way the barrel points.</param>
    /// <param name="back">The car's back (the way it leaves the smoke), <paramref name="up"/> its up.</param>
    /// <param name="speed">The car's speed (m/s).</param>
    /// <param name="shot">Which shot (its tick): picks the flash's star and the smoke's own shape.</param>
    public void CannonShot(MeshBuilder mesh, Vector3 muzzle, Vector3 along, Vector3 back, Vector3 up, float speed, double age, long shot)
    {
        if (age < 0 || age > ShotSmokeSeconds)
            return;
        float a = (float)age;
        float seed = shot % 97;
        var left = back * speed * a;
        // The flash: 0.12 s, the star and its jet, and a light a street wide.
        if (a < 0.12f)
        {
            float fade = 1 - a / 0.12f;
            int star = (int)(shot % 4);
            mesh.Billboard(muzzle, 1.7f * (0.7f + 0.3f * fade), seed, new Vector4(1.1f, 0.85f, 0.6f, fade), _flash, FxBlend.Additive, star, 2);
            for (int k = 1; k <= 4; k++)
                mesh.Billboard(muzzle + along * (0.35f * k), (1.3f - 0.22f * k) * fade, seed + k, new Vector4(1.0f, 0.62f, 0.3f, fade * (1 - k * 0.18f)), _flame,
                    FxBlend.Additive, (int)(seed + k * 3) % 16, 4);
            mesh.PointLights.Add(new PointLight(muzzle + along * 0.6f, new Vector3(2.4f, 1.6f, 0.8f) * fade, 22));
        }
        // Burning wad and sparks: out ahead along the shot, falling, going out.
        for (int k = 0; k < 10; k++)
        {
            float h = Hash(seed * 3.1f + k * 1.7f), h2 = Hash(seed + k * 4.3f);
            float life = 0.5f + 0.9f * h;
            if (a > life)
                continue;
            var spread = Vector3.Normalize(Vector3.Cross(along, up)) * (h2 - 0.5f) * 0.4f + up * (h - 0.3f) * 0.3f;
            var p = muzzle + (along + spread) * (14f * a * (1 - 0.35f * a / life)) - up * (4.9f * a * a) + left;
            mesh.Billboard(p, 0.07f + 0.05f * h, 0, new Vector4(1.0f, 0.55f + 0.3f * h2, 0.2f, 1 - a / life), _spark, FxBlend.Additive, 1, 2);
        }
        // The powder smoke: a bank of puffs born in the first fifth of a second, thrown out along the barrel and braked by
        // the air, then rolling, rising and spreading; thick at first, thinning to nothing at ShotSmokeSeconds.
        for (int k = 0; k < 22; k++)
        {
            float h = Hash(seed * 1.3f + k * 2.9f), h2 = Hash(seed * 0.7f + k * 5.1f), h3 = Hash(seed + k * 7.7f);
            float born = h * 0.2f;
            float age2 = a - born;
            if (age2 < 0)
                continue;
            float s = age2 / (float)(ShotSmokeSeconds - born);
            // Out along the barrel: fast, braked hard (about 9 m out by the time it's stopped).
            float throwOut = (2f + 7f * h2) * (1 - MathF.Exp(-age2 * 3.5f));
            var side = Vector3.Normalize(Vector3.Cross(along, up));
            var p = muzzle + along * throwOut + side * (h3 - 0.5f) * (0.4f + 2.5f * s) + up * (0.15f + 1.6f * s * s + 0.3f * h) + left;
            float size = 0.9f + 6f * MathF.Sqrt(s) * (0.7f + 0.5f * h3);
            float alpha = 0.8f * MathF.Pow(1 - s, 1.6f) * MathF.Min(1, age2 * 12);
            // White-grey, a little warm while the flash is in it.
            var colour = new Vector3(0.44f, 0.44f, 0.43f) * (0.85f + 0.3f * h) + (a < 0.15f ? new Vector3(0.4f, 0.25f, 0.1f) * (1 - a / 0.15f) : Vector3.Zero);
            mesh.Billboard(p, size, h * 6.28f + age2 * 0.25f * (h2 - 0.5f), new Vector4(colour, alpha), _steam, FxBlend.Alpha, (int)(MathF.Min(s * 1.6f, 0.999f) * 16), 4);
        }
    }
}
