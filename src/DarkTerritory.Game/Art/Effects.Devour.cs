using System.Numerics;
using Ballast.Render;

namespace DarkTerritory.Game.Art;

// A Ribbit pack eating its catch, seen (note 558; the director, 9 Oct: "There was no blood or lay down of me when they
// started eating me").
public sealed partial class Effects
{
    /// <summary>
    /// Someone down on their back with a Ribbit pack on them (note 558): blood. A pool spreading out from under them over
    /// the first seconds, spots thrown round it, and the wet of it thrown up off them in spurts as the mouths work, each
    /// arcing out and falling back. <paramref name="feet"/> where their feet are and <paramref name="head"/> the way their
    /// head lies from them (camera-relative, flat); <paramref name="t"/> seconds since the pack got onto them.
    /// </summary>
    public void Devour(MeshBuilder mesh, Vector3 feet, Vector3 head, double t, int seed)
    {
        var up = Vector3.UnitY;
        // Darker than a wound's spray (Impacts): pooled and soaking in, near black under the night, red where it's caught.
        var blood = new Vector3(0.14f, 0.018f, 0.012f);
        var pooled = blood * 0.35f;
        var side = Vector3.Cross(up, head);
        float floor = feet.Y + 0.03f;
        float a = (float)t;
        // Where they're being eaten: their legs and middle, under the pack.
        var middle = feet + head * 0.7f;
        // The pool: soaking out from under them over the first 4 s, then a slow creep, darker where it's deepest.
        float spread = 1 - MathF.Exp(-a / 1.6f);
        Decal(mesh, middle with { Y = floor }, 0.25f + 0.75f * spread + 0.05f * a / (a + 8), seed * 0.7f, new Vector4(pooled * 0.8f, 0.9f * MathF.Min(1, a * 2)));
        Decal(mesh, (feet + head * 0.25f + side * 0.15f) with { Y = floor + 0.002f }, 0.15f + 0.5f * spread, seed * 1.3f, new Vector4(pooled * 0.6f, 0.85f * MathF.Min(1, a * 1.5f)));
        // Spots round it, each landing in its turn.
        for (int k = 0; k < 14; k++)
        {
            float h = Hash(seed * 4.3f + k * 1.3f), h2 = Hash(seed * 1.9f + k * 3.7f), h3 = Hash(seed * 2.7f + k * 5.1f);
            if (a < h * 3)
                continue;
            float ang = h2 * MathF.Tau, r = 0.5f + 1.0f * h3;
            var at = middle + (head * MathF.Cos(ang) + side * MathF.Sin(ang)) * r;
            Decal(mesh, at with { Y = floor + 0.001f * (k + 2) }, 0.05f + 0.1f * h3, h * 6.28f, new Vector4(pooled * (0.6f + 0.4f * h), 0.85f));
        }
        // The spurts: every 0.6-1 s off one of the mouths, a burst of drops up and out, falling back onto them and the
        // ground; brightest in the first seconds.
        float fade = 0.55f + 0.45f * MathF.Exp(-a / 6);
        for (int burst = 0; burst < 3; burst++)
        {
            float hb = Hash(seed * 3.3f + burst * 7.7f);
            float period = 0.6f + 0.4f * hb;
            float age = ((a + hb * period) % period) / period * 0.9f;
            int n = (int)((a + hb * period) / period);
            float hn = Hash(seed * 0.37f + n * 2.1f + burst * 9.1f);
            var from = middle + head * (hn - 0.5f) * 0.8f + side * (Hash(hn * 13) - 0.5f) * 0.5f + up * 0.25f;
            for (int k = 0; k < 7; k++)
            {
                float h = Hash(hn * 5.9f + k * 1.7f), h2 = Hash(hn * 3.1f + k * 4.3f);
                float ang = h * MathF.Tau;
                var v = (head * MathF.Cos(ang) + side * MathF.Sin(ang)) * (0.6f + 1.2f * h2) + up * (1.6f + 1.4f * h);
                var p = from + v * age - up * (4.9f * age * age);
                if (p.Y < floor)
                    continue;
                mesh.Billboard(p, 0.035f + 0.04f * h2, 0, new Vector4(blood * (1.4f + 0.6f * h), fade), _spark, FxBlend.Alpha, 1, 2, stretch: 1.8f);
            }
        }
    }
}
