using System.Numerics;
using Ballast.Render;

namespace DarkTerritory.Game.Art;

/// <summary>
/// The Hollow House's (G1's note 585; the director, 9 Oct 2026: "the ground rumbles under it (the tell); then its doors slam
/// shut one after another ... and it sinks and comes down"): what it shakes loose. Worked out from the time, as the rest of
/// the effects are, in the house's own frame (<see cref="WorldArt.HouseFrame"/>: its x on −Z, its y on +X, the boards 0.17 up).
/// </summary>
public sealed partial class Effects
{
    /// <summary>
    /// Grit and plaster dust shaken down from under the roof (over the house's <paramref name="halfX"/> by
    /// <paramref name="halfZ"/>, the kit's), falling to the boards and hanging there in the lamplight, and dust kicked up off
    /// the floor along the walls: <paramref name="strength"/> 0..1 how hard it's going.
    /// </summary>
    public void HouseRumble(MeshBuilder mesh, in Matrix4x4 frame, float halfX, float halfZ, float strength, double time, int seed)
    {
        if (strength <= 0)
            return;
        int grit = (int)(28 * strength) + 4;
        for (int i = 0; i < grit; i++)
        {
            float h = Hash(seed * 0.37f + i * 1.31f), h2 = Hash(seed * 1.7f + i * 2.03f);
            // Each grain falls the 2.8 m from the ceiling in about 0.75 s, then another from somewhere else.
            float fall = 0.75f, t = (float)((time + h * 5) % fall) / fall;
            int round = (int)Math.Floor((time + h * 5) / fall);
            float x = (Hash(h * 91 + round * 0.71f) - 0.5f) * 2 * (halfX - 0.3f), z = (Hash(h2 * 53 + round * 1.13f) - 0.5f) * 2 * (halfZ - 0.3f);
            float y = 2.95f - 2.78f * t * t;
            var p = Vector3.Transform(new Vector3(x, y, z), frame);
            mesh.Billboard(p, 0.012f + 0.012f * h2, 0, new Vector4(0.12f, 0.1f, 0.08f, 0.9f), _smoke, FxBlend.Alpha, 15, 4, stretch: 4);
            // A puff of plaster dust off the ceiling where it came from, sinking and spreading.
            if (i % 3 == 0)
            {
                var puff = Vector3.Transform(new Vector3(x, 2.9f - 1.4f * t, z), frame);
                mesh.Billboard(puff, 0.35f + 0.9f * t, h * 6.28f, new Vector4(0.8f, 0.76f, 0.7f, 0.3f * strength * (1 - t)), _smoke, FxBlend.Alpha, (int)(t * 10), 4);
            }
        }
        // Dust shaken up off the boards along the walls' feet, low.
        for (int i = 0; i < 10; i++)
        {
            float h = Hash(seed * 3.1f + i * 0.77f);
            float t = (float)((time * 0.5 + h) % 1);
            bool alongX = i % 2 == 0;
            float a = (Hash(h * 17 + (int)(time * 0.5 + h)) - 0.5f) * 2;
            var at = alongX ? new Vector3(a * (halfX - 0.4f), 0.3f + 0.4f * t, (i % 4 < 2 ? -1 : 1) * (halfZ - 0.4f))
                : new Vector3((i % 4 < 2 ? -1 : 1) * (halfX - 0.4f), 0.3f + 0.4f * t, a * (halfZ - 0.4f));
            mesh.Billboard(Vector3.Transform(at, frame), 0.6f + 0.8f * t, h * 6.28f, new Vector4(0.75f, 0.7f, 0.62f, 0.28f * strength * (1 - t)),
                _smoke, FxBlend.Alpha, (int)(t * 10), 4);
        }
    }

    /// <summary>
    /// A house going down into the ground (in its frame, unmoved): dust and dry earth breathing out of the gap opened round
    /// its walls' feet, low, drifting.
    /// </summary>
    public void HouseSinking(MeshBuilder mesh, in Matrix4x4 frame, float halfX, float halfZ, double time, int seed)
    {
        for (int i = 0; i < 18; i++)
        {
            float h = Hash(seed * 0.77f + i * 1.37f);
            float t = (float)((time * 0.35 + h) % 1);
            // Round its four sides, just out from the wall.
            float a = (Hash(h * 13 + (int)(time * 0.35 + h)) - 0.5f) * 2;
            var at = (i % 4) switch
            {
                0 => new Vector3(a * halfX, 0, halfZ + 0.3f),
                1 => new Vector3(a * halfX, 0, -halfZ - 0.3f),
                2 => new Vector3(halfX + 0.3f, 0, a * halfZ),
                _ => new Vector3(-halfX - 0.3f, 0, a * halfZ),
            };
            at.Y = 0.3f + 0.9f * t;
            mesh.Billboard(Vector3.Transform(at, frame), 0.8f + 1.4f * t, h * 6.28f, new Vector4(0.8f, 0.75f, 0.66f, 0.65f * MathF.Sin(t * MathF.PI)),
                _smoke, FxBlend.Alpha, (int)(t * 10), 4);
        }
    }

    /// <summary>
    /// A door slammed <paramref name="age"/> s ago: dust blown out round its frame and down off the lintel
    /// (<paramref name="top"/>, camera-relative; <paramref name="outward"/> the way out of the house).
    /// </summary>
    public void DoorSlam(MeshBuilder mesh, Vector3 top, Vector3 outward, Vector3 side, double age, int seed)
    {
        if (age < 0 || age > 1.6)
            return;
        float a = (float)age, spread = 1 - MathF.Exp(-a * 4);
        for (int i = 0; i < 9; i++)
        {
            float h = Hash(seed * 0.91f + i * 1.7f);
            var p = top + side * ((h - 0.5f) * 1.4f) + outward * (0.2f + 0.9f * spread * (0.5f + h)) - Vector3.UnitY * (0.2f + 1.6f * spread * h);
            mesh.Billboard(p, 0.5f + 1.2f * spread, h * 6.28f, new Vector4(0.9f, 0.85f, 0.76f, 0.9f * MathF.Max(0, 1 - a / 1.6f)), _smoke, FxBlend.Alpha,
                (int)(spread * 10), 4);
        }
        // Grit off the lintel.
        for (int i = 0; i < 8; i++)
        {
            float h = Hash(seed * 2.3f + i * 0.53f), t = MathF.Min(1, a / 0.6f);
            var p = top + side * ((h - 0.5f) * 1.1f) + outward * 0.05f - Vector3.UnitY * (2.1f * t * t);
            if (t < 1)
                mesh.Billboard(p, 0.015f, 0, new Vector4(0.12f, 0.1f, 0.08f, 0.9f), _smoke, FxBlend.Alpha, 15, 4, stretch: 3);
        }
    }

    /// <summary>
    /// A house come down <paramref name="age"/> s ago (in its frame): a cloud of plaster and earth rolled out over it and round
    /// it, rising and thinning over half a minute, and after that a low haze hanging over the heap.
    /// </summary>
    public void HouseDown(MeshBuilder mesh, in Matrix4x4 frame, float halfX, float halfZ, double age, int seed)
    {
        float a = (float)Math.Max(0, age);
        float spread = 1 - MathF.Exp(-a * 0.9f), fade = MathF.Max(0.12f, MathF.Exp(-a / 9));
        int puffs = 26;
        for (int i = 0; i < puffs; i++)
        {
            float h = Hash(seed * 0.53f + i * 1.9f), h2 = Hash(seed * 1.3f + i * 0.61f);
            float ang = i * MathF.Tau / puffs + h;
            float r = (0.3f + 0.7f * h2) * (1 + 1.6f * spread);
            var p = new Vector3(MathF.Cos(ang) * halfX * r, 0.6f + 2.2f * spread * h + 0.4f * MathF.Sin((float)age * 0.3f + i), MathF.Sin(ang) * halfZ * r);
            mesh.Billboard(Vector3.Transform(p, frame), 2.6f + 4.5f * spread * (0.6f + h), h * 6.28f + a * 0.05f,
                new Vector4(0.85f, 0.8f, 0.72f, 0.85f * fade * MathF.Min(1, a * 4 + 0.3f)), _smoke, FxBlend.Alpha, (int)(spread * 10) % 16, 4);
        }
    }
}
