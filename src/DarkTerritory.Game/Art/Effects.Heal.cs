using System.Numerics;
using Ballast.Render;

namespace DarkTerritory.Game.Art;

// The Grumbler's healing seen (note 487; GDD App. A.8 "it heals if only one player has hit it ... no one player can kill
// it"; the art checklist's grumbler-anim "a tell for its healing").
public sealed partial class Effects
{
    /// <summary>
    /// A creature mending (note 487): what a blow knocked out of it drawn back in. Its dark blood (Ichor's colour) rises off
    /// the ground round it in drops and runs back into the body; its skin glistens wet where it's knitting; and a faint
    /// vapour comes off it. <paramref name="body"/> the middle of it and <paramref name="ground"/> the ground under it
    /// (camera-relative), <paramref name="strength"/> 0..1, more the more hurt it is: so a lone crewmate's blow is seen
    /// closing again at once, and a gang's, which it doesn't heal, isn't.
    /// </summary>
    public void Knit(MeshBuilder mesh, Vector3 body, Vector3 ground, float strength, double t, int seed)
    {
        if (strength <= 0)
            return;
        var up = Vector3.UnitY;
        var blood = new Vector3(0.14f, 0.018f, 0.012f);
        float floor = ground.Y + 0.03f;
        // The drops: each off the ground up to 1.1 m out, along an arc back into the body over its own 0.5-0.8 s, round
        // again, so there's always some on their way in. Quicker as they near it, as if pulled.
        int drops = 6 + (int)(12 * strength);
        for (int k = 0; k < drops; k++)
        {
            float h = Hash(seed * 3.1f + k * 1.9f), h2 = Hash(seed * 1.7f + k * 5.3f), h3 = Hash(seed * 0.9f + k * 2.3f);
            float period = 0.5f + 0.3f * h3;
            float s = (float)((t / period + h) % 1.0);
            float a0 = h2 * MathF.Tau, r = 0.5f + 0.6f * h;
            var from = new Vector3(body.X + MathF.Cos(a0) * r, floor, body.Z + MathF.Sin(a0) * r);
            var to = body + new Vector3(MathF.Cos(a0) * 0.15f, (h3 - 0.5f) * 0.2f, MathF.Sin(a0) * 0.15f);
            float e = s * s;
            var p = Vector3.Lerp(from, to, e) + up * (MathF.Sin(e * MathF.PI) * 0.25f);
            float a = strength * MathF.Min(1, s * 6) * MathF.Min(1, (1 - s) * 8);
            // Wet: lit a little by whatever's about, so it shows against the ground; a short trail behind it, a thread.
            for (int j = 0; j < 3; j++)
            {
                float eb = MathF.Max(0, s - j * 0.06f);
                eb *= eb;
                var q = Vector3.Lerp(from, to, eb) + up * (MathF.Sin(eb * MathF.PI) * 0.25f);
                mesh.Billboard(j == 0 ? p : q, (0.06f + 0.05f * h) * (1 - 0.3f * j), 0, new Vector4(blood * (1.7f + 0.6f * h3), a * (1 - 0.3f * j)), _spark, FxBlend.Alpha, 1, 2,
                    stretch: 1.6f + 1.4f * e);
            }
            // Where it came from, the spot it leaves, going as it goes.
            Decal(mesh, from, 0.05f + 0.06f * h, h * 6.28f, new Vector4(blood * 0.8f, 0.7f * strength * (1 - s)));
        }
        // The skin knitting: wet glints on it, catching whatever light there is, each brief.
        for (int k = 0; k < 16; k++)
        {
            float h = Hash(seed * 7.7f + k * 3.3f), h2 = Hash(seed * 2.9f + k * 7.1f), h3 = Hash(seed * 4.1f + k * 1.1f);
            float glint = MathF.Pow(MathF.Max(0, MathF.Sin((float)t * (7 + 5 * h2) + h * 40)), 6);
            var p = body + new Vector3((h - 0.5f) * 0.7f, (h3 - 0.3f) * 0.35f, (h2 - 0.5f) * 0.7f);
            mesh.Billboard(p, 0.05f + 0.05f * h3, 0, new Vector4(0.75f, 0.62f, 0.5f, 0.55f * strength * glint), -1, FxBlend.Additive);
        }
        // A dull throb through it, the colour of what's going back in: brighter on each heave.
        float throb = 0.5f + 0.5f * MathF.Sin((float)t * 9 + seed);
        mesh.Billboard(body, 1.1f, 0, new Vector4(0.32f, 0.07f, 0.04f, (0.18f + 0.22f * throb) * strength), -1, FxBlend.Additive);
        // And the heat of it: a faint vapour off its back, rising.
        for (int k = 0; k < 3; k++)
        {
            float h = Hash(seed * 5.3f + k * 2.7f), age = (float)((t * 0.7 + h + k / 3.0) % 1.0);
            var p = body + up * (0.2f + 0.6f * age) + new Vector3((h - 0.5f) * 0.3f, 0, (Hash(h * 9) - 0.5f) * 0.3f);
            mesh.Billboard(p, 0.35f + 0.5f * age, h * 6.28f, new Vector4(0.55f, 0.52f, 0.48f, 0.4f * strength * (1 - age)), _steam, FxBlend.Alpha, (int)(age * 15.99f), 4);
        }
    }
}
