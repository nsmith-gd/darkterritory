using System.Numerics;
using Ballast.Render;

namespace DarkTerritory.Game.Art;

public sealed partial class Effects
{
    /// <summary>
    /// Where a ball from the train's own guns came down on the train (note 370; the checklist's cannon impacts: "a ball on
    /// the train's own body leaves no scorch (the car moves on)"): a burn on the car's plate that rides with it the rest of
    /// the night, its black heart in a burnt ring sooted out round it, the paint blistered off at its edge; for its first
    /// seconds the iron still glowing in it with the burst's embers going out, and a thread of smoke off it a while longer.
    /// </summary>
    /// <param name="at">The burn's centre on the car's face (camera-relative), just off it.</param>
    /// <param name="normal">Out of the face it's on: a side, an end, or up off a roof (unit, world).</param>
    /// <param name="age">Seconds since the ball landed.</param>
    /// <param name="room">How far the face runs on round it (m): the burn's no wider than the plate it's on.</param>
    public void TrainScorch(MeshBuilder mesh, Vector3 at, Vector3 normal, int seed, double age, float room = 1)
    {
        if (age < 0 || at.Length() > 220)
            return;
        float a = (float)age, s0 = seed % 97 + 0.5f;
        float come = MathF.Min(1, a * 8);
        if (come <= 0)
            return;
        float fit = Math.Clamp(room / 0.9f, 0.2f, 1);
        var u = Across(normal, 0) * fit;
        var v = Across(normal, MathF.PI / 2) * fit;
        // Lit, as the plate is (a fire's char is drawn the same way, Char): soot thrown out round it, the burn, and its
        // black heart, each a ragged blot a few millimetres off the last so none fights the plate or another.
        Blot(mesh, at, (0.78f + 0.15f * Hash(s0)) * (0.6f + 0.4f * come), u, v, new Vector3(0.03f, 0.026f, 0.022f), s0, 0.45f);
        Blot(mesh, at + normal * 0.004f, (0.48f + 0.1f * Hash(s0 + 1)) * come, u, v, new Vector3(0.016f, 0.013f, 0.011f), s0 + 3, 0.45f);
        Blot(mesh, at + normal * 0.008f, (0.24f + 0.06f * Hash(s0 + 2)) * come, u, v, new Vector3(0.008f, 0.007f, 0.006f), s0 + 7, 0.3f);
        // The paint blistered and lifted round the edge: a few flecks of rust-brown about the burn.
        for (int k = 0; k < 7; k++)
        {
            float h = Hash(s0 * 3.1f + k * 1.9f), ang = k * MathF.Tau / 7 + h;
            var off = (u * MathF.Cos(ang) + v * MathF.Sin(ang)) * (0.45f + 0.25f * h);
            Blot(mesh, at + normal * 0.006f + off, (0.05f + 0.05f * h) * come, u, v, new Vector3(0.1f, 0.05f, 0.025f), s0 + 11 + k, 0.5f);
        }
        // Still hot: the iron's glow in it and its last embers, going out over four seconds.
        if (a < 4)
        {
            float hot = (1 - a / 4) * (1 - a / 4);
            mesh.Billboard(at + normal * 0.12f, 0.8f, 0, new Vector4(1.0f, 0.38f, 0.08f, 0.55f * hot), -1, FxBlend.Additive);
            for (int k = 0; k < 8; k++)
            {
                float h = Hash(s0 * 5.3f + k * 2.7f), h2 = Hash(s0 * 1.3f + k * 4.1f);
                float flick = 0.55f + 0.45f * MathF.Sin(a * (11 + 8 * h) + k * 1.3f);
                var p = at + normal * 0.03f + Across(normal, h * MathF.Tau) * (0.1f + 0.35f * h2);
                mesh.Billboard(p, 0.06f + 0.05f * h2, 0, new Vector4(1.0f, 0.5f, 0.12f, flick * hot), -1, FxBlend.Additive);
            }
        }
        // A thread of smoke off it for a while, drifting up.
        if (a < 12)
            for (int k = 0; k < 5; k++)
            {
                float h = Hash(s0 * 2.9f + k * 3.3f);
                float t = (a * 0.5f + k * 0.2f + h * 0.2f) % 1;
                var p = at + normal * (0.15f + 0.3f * t) + Vector3.UnitY * (0.2f + 1.6f * t) + Across(normal, h * 6) * 0.15f * t;
                float alpha = 0.32f * MathF.Sin(t * MathF.PI) * (1 - a / 12);
                mesh.Billboard(p, 0.25f + 0.7f * t, h * 6.28f, new Vector4(0.18f, 0.17f, 0.16f, alpha), _smoke, FxBlend.Alpha, (int)(t * 15.99f), 4);
            }
    }

    /// <summary>
    /// A ragged lit blot of flat colour in the plane of <paramref name="u"/> and <paramref name="v"/>, wound to face the
    /// eye as every solid is (the scene is camera-relative), its rim <paramref name="ragged"/> uneven.
    /// </summary>
    static void Blot(MeshBuilder mesh, Vector3 centre, float radius, Vector3 u, Vector3 v, Vector3 colour, float seed, float ragged)
    {
        if (radius <= 0.005f)
            return;
        const int Sides = 12;
        Span<Vector3> rim = stackalloc Vector3[Sides];
        for (int k = 0; k < Sides; k++)
        {
            float ang = k * MathF.Tau / Sides;
            float r = radius * (1 - ragged * 0.5f + ragged * Hash(seed * 3.7f + k * 1.3f));
            rim[k] = centre + u * (MathF.Cos(ang) * r) + v * (MathF.Sin(ang) * r);
        }
        float emissive = mesh.Emissive;
        var style = mesh.Style;
        // Flat colour: the art style would pick a texture by the colour, and soot's no material it knows.
        mesh.Emissive = 0;
        mesh.Style = null;
        bool flip = Vector3.Dot(Vector3.Cross(rim[0] - centre, rim[1] - centre), centre) > 0;
        for (int k = 0; k < Sides; k++)
        {
            var (p, q) = (rim[k], rim[(k + 1) % Sides]);
            if (flip)
                mesh.Triangle(centre, q, p, colour);
            else
                mesh.Triangle(centre, p, q, colour);
        }
        mesh.Emissive = emissive;
        mesh.Style = style;
    }

    /// <summary>A unit direction in the plane square to <paramref name="normal"/>, at <paramref name="angle"/> round it.</summary>
    static Vector3 Across(Vector3 normal, float angle)
    {
        var u = Vector3.Normalize(Vector3.Cross(MathF.Abs(normal.Y) > 0.9f ? Vector3.UnitX : Vector3.UnitY, normal));
        var w = Vector3.Cross(normal, u);
        return u * MathF.Cos(angle) + w * MathF.Sin(angle);
    }
}
