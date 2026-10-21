using System.Numerics;
using Ballast.Render;

namespace DarkTerritory.Game.Art;

public sealed partial class Effects
{
    /// <summary>How long a flare pistol's shot is seen at the muzzle (s): the flash, then its puff of smoke going.</summary>
    public const double FlareShotSeconds = 0.8;

    /// <summary>
    /// The flare pistol's shot (note 964; note 871's pistol), <paramref name="age"/> seconds after it, where it left the
    /// muzzle (<paramref name="at"/>, camera-relative), <paramref name="along"/> the way it went: a red-white flash for a
    /// moment and its light, sparks thrown on after it, and a small puff of grey smoke that drifts up and thins.
    /// </summary>
    public void FlareShot(MeshBuilder mesh, Vector3 at, Vector3 along, double age, int seed)
    {
        if (age < 0 || age >= FlareShotSeconds)
            return;
        float a = (float)age;
        if (a < 0.12f && _flash >= 0)
        {
            float fade = 1 - a / 0.12f;
            mesh.Billboard(at, 0.45f * (0.6f + 0.4f * fade), seed, new Vector4(1.2f, 0.75f, 0.6f, fade), _flash, FxBlend.Additive, seed & 3, 2);
            mesh.PointLights.Add(new PointLight(at, SceneArt.FlareGlow * 6f * fade, 6));
        }
        if (_spark >= 0 && a < 0.4f)
            for (int k = 0; k < 6; k++)
            {
                float h = Hash(k * 2.7f + seed * 1.3f), h2 = Hash(k * 5.1f + seed * 0.7f);
                var side = new Vector3(h - 0.5f, h2 - 0.3f, Hash(k * 3.3f + seed) - 0.5f) * 1.2f;
                var p = at + (along * (2.5f + 2f * h) + side) * a - new Vector3(0, 4.9f * a * a, 0);
                mesh.Billboard(p, 0.04f, 0, new Vector4(1f, 0.55f, 0.25f, 1 - a / 0.4f), _spark, FxBlend.Additive, 0, 2);
            }
        if (_smoke >= 0)
            for (int k = 0; k < 4; k++)
            {
                float h = Hash(k * 1.9f + seed * 2.1f), s = a / (float)FlareShotSeconds;
                var p = at + along * (0.1f + 0.25f * k) * (0.3f + s) + new Vector3(0, 0.35f * s + 0.05f * k, 0);
                mesh.Billboard(p, 0.12f + 0.35f * s, h * 6.28f, new Vector4(0.72f, 0.7f, 0.68f, 0.45f * (1 - s)), _smoke, FxBlend.Alpha, (int)(s * 15.99f), 4);
            }
    }

    /// <summary>
    /// A flare in flight (note 964), climbing and falling over where it was sent, <paramref name="velocity"/> its way
    /// (m/s): its hot white head and red glare, sputtering; behind it a streak of sparks along the way it came, dropping
    /// and dying; and a ribbon of smoke left hanging in the air behind it, so the crew see the arc it drew. Its light is
    /// the scene's (the burning flare's, GreyboxScene.FlareLight).
    /// </summary>
    public void FlareFlight(MeshBuilder mesh, Vector3 head, Vector3 velocity, double t, int seed)
    {
        float speed = velocity.Length();
        if (speed < 1 || _spark < 0)
            return;
        var back = -velocity / speed;
        float sputter = 0.8f + 0.2f * MathF.Sin((float)(t * 29) + seed);
        mesh.Billboard(head, 0.5f * sputter, 0, new Vector4(new Vector3(1f, 0.92f, 0.88f) * sputter, 1), -1, FxBlend.Additive);
        mesh.Billboard(head, 1.8f * sputter, 0, new Vector4(SceneArt.FlareGlow * 0.4f * sputter, 1), -1, FxBlend.Additive);
        // The streak: the glare drawn out along the last tenth of a second of its path, then sparks along the last few tenths
        // of it, falling a little as they go, so its arc reads from a stop's length off.
        for (int k = 1; k <= 5; k++)
        {
            float s = k / 5f;
            mesh.Billboard(head + back * (speed * 0.1f * s), 0.9f * (1 - 0.6f * s) * sputter, 0, new Vector4(SceneArt.FlareGlow * 0.55f * (1 - s), 1), -1, FxBlend.Additive);
        }
        for (int k = 0; k < 20; k++)
        {
            float h = Hash(k * 1.37f + seed * 3.1f + (float)Math.Floor(t * 20) * 0.11f), s = (k + h) / 20f;
            float lag = s * 0.5f;
            var p = head + back * (speed * lag) + new Vector3((h - 0.5f) * 0.5f, -2.5f * lag * lag, (Hash(k * 4.1f + seed) - 0.5f) * 0.5f);
            mesh.Billboard(p, 0.12f + 0.12f * (1 - s), 0, new Vector4(1f, 0.45f + 0.3f * (1 - s), 0.2f, 1 - s), _spark, FxBlend.Additive, 0, 2);
        }
        // The smoke it leaves: puffs along the last second and a half of its path, widening and thinning, lit by it.
        if (_smoke >= 0)
            for (int k = 0; k < 10; k++)
            {
                float s = (k + 0.5f) / 10f, lag = s * 1.5f;
                var p = head + back * (speed * lag) + new Vector3(0, 0.4f * s, 0);
                var lit = Vector3.Lerp(new Vector3(0.95f, 0.55f, 0.5f), new Vector3(0.7f, 0.66f, 0.64f), s);
                mesh.Billboard(p, 0.5f + 1.8f * s, Hash(k * 2.3f + seed) * 6.28f, new Vector4(lit, 0.55f * (1 - s)), _smoke, FxBlend.Alpha, (int)(s * 15.99f), 4);
            }
    }
}
