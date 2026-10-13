using System.Numerics;
using Ballast.Render;

namespace DarkTerritory.Game.Art;

/// <summary>What the train's own creatures do to the train you can see (notes 364, 367): brakes wound on, an axle seized.</summary>
public sealed partial class Effects
{
    /// <summary>
    /// A wheel with its brake shoes wound hard on it (the Brakeman's, note 364): the shoe's face glowing dull red where it bites
    /// the tread, and a thin spray of sparks off it, thrown back along the rail and falling, the more the faster the train's
    /// going. <paramref name="tread"/> the shoe on the tread, <paramref name="back"/> the car's back.
    /// </summary>
    public void BrakeShoe(MeshBuilder mesh, Vector3 tread, Vector3 up, Vector3 back, float speed, double time, int seed)
    {
        if (_spark < 0 || MathF.Abs(speed) < 1)
            return;
        float hard = Math.Clamp(MathF.Abs(speed) / 15f, 0.2f, 1f);
        mesh.Billboard(tread, 0.22f, 0, new Vector4(1.0f, 0.3f, 0.06f, 0.55f * hard), _spark, FxBlend.Additive, 0, 2);
        var right = Vector3.Normalize(Vector3.Cross(up, back));
        int n = 4 + (int)(8 * hard);
        for (int k = 0; k < n; k++)
        {
            float h = Hash(k * 2.71f + seed * 0.37f);
            float period = 0.22f + h * 0.2f;
            float age = (float)((time + h * 3) % period);
            float t = age / period;
            var fly = back * (1.5f + 2.5f * h) * hard + right * ((Hash(k + 0.3f + seed) - 0.5f) * 1.2f) + up * (0.4f + Hash(k + 1.7f) * 0.8f);
            var p = tread + fly * age - up * (4.9f * age * age);
            mesh.Billboard(p, 0.16f + 0.1f * h, 0.3f, new Vector4(1.6f, 0.85f, 0.3f, (1 - t * t) * 0.8f), _spark, FxBlend.Additive, k % 4, 2, stretch: 2.5f);
        }
    }

    /// <summary>
    /// A seized wheel dragged along the rail (Hotbox's, note 367): a fan of sparks off where it skids, low along the rail and
    /// back from it, and the rail under it lit; heavier than a brake's, and only while the train's moving.
    /// </summary>
    public void Skid(MeshBuilder mesh, Vector3 contact, Vector3 up, Vector3 back, float speed, double time, int seed)
    {
        if (_spark < 0 || MathF.Abs(speed) < 0.3f)
            return;
        float hard = Math.Clamp(MathF.Abs(speed) / 7f, 0.3f, 1f);
        mesh.Billboard(contact + up * 0.03f, 0.4f, 0, new Vector4(1.2f, 0.55f, 0.15f, 0.7f * hard), _spark, FxBlend.Additive, 0, 2);
        var right = Vector3.Normalize(Vector3.Cross(up, back));
        for (int k = 0; k < 22; k++)
        {
            float h = Hash(k * 1.93f + seed * 0.71f);
            float period = 0.3f + h * 0.25f;
            float age = (float)((time + h * 2) % period);
            float t = age / period;
            var fly = back * (2.5f + 3.5f * h) * hard + right * ((Hash(k + 0.5f + seed) - 0.5f) * 2.2f) + up * (0.6f + Hash(k + 2.5f) * 1.4f);
            var p = contact + fly * age - up * (4.9f * age * age);
            mesh.Billboard(p, 0.3f + 0.25f * h, 0.3f, new Vector4(1.7f, 0.95f, 0.35f, (1 - t * t) * hard), _spark, FxBlend.Additive, k % 4, 2, stretch: 3f);
        }
    }
}
