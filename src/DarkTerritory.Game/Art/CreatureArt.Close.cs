using System.Numerics;
using Ballast.Render;
using DarkTerritory.Sim.Enemies;

namespace DarkTerritory.Game.Art;

/// <summary>
/// The Close (GDD §21; the director's playtest of 23 Oct 2026, line 36; ARCHITECTURE §8 note 1013): procedural, a soft-body
/// sack with no limbs and so no rig, its clips the sim's modes (replicated in its Height) drawn as shapes that breathe by the
/// clock: <c>bow</c> (a long grey sack slumped along the roof, its edges down in the gaps, the roof's boards bowed in under it
/// over the car's room, easing and pressing every 3 s), <c>pour</c> (grey skin bulging down through the hatch like dough, a
/// slow pulse in it), <c>pin</c> (swollen to fill the car from the ceiling down, pressing everyone to the walls), <c>burst</c>
/// (sagged flat, sliding off the car's side). Its origin is the roof's top over the hatch, facing −Z along the car.
/// </summary>
public sealed partial class CreatureArt
{
    CloseMode? _closeMode;
    float _closeHalf = 6;

    /// <summary>The Close's mode as its phase has it, for a draw without the sim's (a test's, the greybox's).</summary>
    public static CloseMode CloseModeOf(SpinePhase phase) => phase switch
    {
        SpinePhase.Commit => CloseMode.Pour,
        SpinePhase.Grab or SpinePhase.Punish => CloseMode.Pin,
        SpinePhase.BreakOff or SpinePhase.Gone => CloseMode.Burst,
        _ => CloseMode.Bow,
    };

    // Its colours: a soft wet grey skin, its pulse a shade paler; the bowed boards the car's own weathered planking.
    static readonly Vector3 CloseSkin = new(0.40f, 0.38f, 0.37f), ClosePale = new(0.62f, 0.58f, 0.56f), CloseBoards = new(0.46f, 0.33f, 0.21f);

    /// <summary>The breath the bow and the pulse go by: in and out every 3 s (Sloe's design §4), 0 to 1.</summary>
    public static float CloseBreath(double since) => 0.5f - 0.5f * MathF.Cos((float)(since * 2 * Math.PI / 3));

    /// <summary>
    /// A Close at <paramref name="model"/> (the roof's top over the hatch, facing −Z) in <paramref name="mode"/>,
    /// <paramref name="since"/> s into it, lying <paramref name="half"/> m either way along the car. Always drawn (true).
    /// </summary>
    public static bool CloseStandIn(MeshBuilder mesh, in Matrix4x4 model, CloseMode mode, double since, float half = 6)
    {
        var style = mesh.Style;
        mesh.Style = null;
        try
        {
            var o = model.Translation;
            var r = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitX, model));
            var u = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitY, model));
            var b = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, model));
            Vector3 At(float x, float y, float z) => o + r * x + u * y + b * z;
            float breath = CloseBreath(since);
            // A lumpy superellipsoid, squared off along its length (a full sack, not a spindle) and rounder across, its girth
            // swelling and easing along it.
            static float Pow(float v, float e) => MathF.Sign(v) * MathF.Pow(MathF.Abs(v), e);
            void Sack(float cx, float cy, float cz, float rx, float ry, float rz, Vector3 colour, float lumps = 0.12f, bool inside = false)
            {
                const int Rings = 14, Around = 18;
                Vector3 P(int i, int j)
                {
                    float th = MathF.PI * i / Rings, ph = 2 * MathF.PI * j / Around;
                    float along = Pow(MathF.Cos(th), 0.35f), round = Pow(MathF.Sin(th), 0.35f);
                    float girth = 1 + lumps * MathF.Sin(along * 5.3f + (float)since * 0.9f) * round;
                    return At(cx + rx * girth * round * Pow(MathF.Cos(ph), 0.8f), cy + ry * girth * round * Pow(MathF.Sin(ph), 0.8f), cz + rz * along);
                }
                for (int i = 0; i < Rings; i++)
                    for (int j = 0; j < Around; j++)
                    {
                        Vector3 a = P(i, j), c = P(i + 1, j), d = P(i + 1, j + 1), e = P(i, j + 1);
                        var shade = (i + j) % 3 == 0 ? colour * 0.92f : colour;
                        mesh.Triangle(a, c, d, shade);
                        mesh.Triangle(a, d, e, shade);
                        // Seen from within too (whoever it's swallowed: the skin all round them).
                        if (inside)
                        {
                            mesh.Triangle(a, d, c, shade * 0.8f);
                            mesh.Triangle(a, e, d, shade * 0.8f);
                        }
                    }
            }
            switch (mode)
            {
                case CloseMode.Bow:
                    {
                        // Slumped along the roof, breathing; down over the roof's edges either side.
                        Sack(0, 0.35f + 0.08f * breath, 0, 1.25f, 0.42f + 0.06f * breath, half * 0.95f, CloseSkin);
                        // The roof's boards bowed in over the room below (the tell from inside), lengthwise planks from the ceiling's
                        // line at the walls down to deepest in the middle, the grey skin showing between them where they've parted.
                        const int Strips = 10, Planks = 7;
                        float Sag(float z, float x) => -0.18f - (0.18f + 0.17f * breath) * (1 - z * z / (half * half)) * (1 - x * x / 1.5f);
                        for (int k = 0; k < Planks; k++)
                        {
                            float x0 = -1.15f + k * 2.3f / Planks + 0.03f, x1 = x0 + 2.3f / Planks - 0.06f;
                            var plank = k % 2 == 0 ? CloseBoards : CloseBoards * 0.86f;
                            for (int n = 0; n < Strips; n++)
                            {
                                float z0 = -half + 2 * half * n / Strips, z1 = -half + 2 * half * (n + 1) / Strips;
                                // Both faces: seen from under it in the room, and from the roof's own side.
                                mesh.Quad(At(x0, Sag(z0, x0), z0), At(x1, Sag(z0, x1), z0), At(x1, Sag(z1, x1), z1), At(x0, Sag(z1, x0), z1), plank);
                                mesh.Quad(At(x0, Sag(z0, x0), z0), At(x0, Sag(z1, x0), z1), At(x1, Sag(z1, x1), z1), At(x1, Sag(z0, x1), z0), plank);
                                // The skin pressing down through the seam beside it.
                                float sx0 = x1, sx1 = x1 + 0.06f;
                                if (k < Planks - 1)
                                {
                                    float d = 0.04f + 0.03f * breath;
                                    mesh.Quad(At(sx0, Sag(z0, sx0) - d, z0), At(sx1, Sag(z0, sx1) - d, z0), At(sx1, Sag(z1, sx1) - d, z1), At(sx0, Sag(z1, sx0) - d, z1), ClosePale);
                                    mesh.Quad(At(sx0, Sag(z0, sx0) - d, z0), At(sx0, Sag(z1, sx0) - d, z1), At(sx1, Sag(z1, sx1) - d, z1), At(sx1, Sag(z0, sx1) - d, z0), ClosePale);
                                }
                            }
                        }
                        break;
                    }
                case CloseMode.Pour:
                    {
                        // Still on the roof, but its middle bulging down through the hatch, longer and wider as it comes.
                        float p = Math.Clamp((float)since / 4, 0, 1);
                        Sack(0, 0.3f, 0, 1.2f, 0.35f, half * 0.9f * (1 - 0.4f * p), CloseSkin);
                        Sack(0, -0.4f - 0.9f * p, 0, 0.35f + 0.6f * p, 0.5f + 0.9f * p, 0.4f + 0.9f * p, ClosePale, 0.18f);
                        break;
                    }
                case CloseMode.Pin:
                    {
                        // Swollen to fill the room from the ceiling down, pressing against the walls; its pulse slow and heavy.
                        float pulse = 0.05f * breath;
                        Sack(0, 0.15f, 0, 1.1f, 0.25f, half * 0.6f, CloseSkin);
                        // Down the room, near enough wall to wall: whoever's in it is in it, the skin all round them.
                        Sack(0, -1.4f, 0, 1.15f + pulse, 1.05f + pulse, half * 0.8f, ClosePale, 0.16f, inside: true);
                        break;
                    }
                default:
                    // Burst: sagged flat and sliding off the car's side.
                    Sack(0.9f, 0.08f, 0, 1.4f, 0.08f, half * 0.85f, CloseSkin * 0.8f, 0.2f);
                    break;
            }
            return true;
        }
        finally
        {
            mesh.Style = style;
        }
    }
}
