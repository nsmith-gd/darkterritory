using System.Numerics;
using Ballast.Render;
using DarkTerritory.Sim.Enemies;

namespace DarkTerritory.Game.Art;

/// <summary>
/// The in-car incidents as they're seen, for the art pass and the greybox alike: a fire's smoke curling up off the load
/// and then its flames (and their light on the walls), and Gnawers boiling out of the crates. A loose load is only heard
/// (its straps groaning): drawn, as nothing. The origin is the cargo stack's face on the car's floor; +X is into the load.
/// </summary>
public static class IncidentArt
{
    /// <summary>Draws it; false for a kind that isn't an incident.</summary>
    public static bool Draw(MeshBuilder mesh, Vector3 o, Vector3 r, Vector3 u, Vector3 b, EnemyKind kind, SpinePhase phase, double t, double extra, double health)
    {
        Vector3 L(double x, double y, double z) => o + r * (float)x + u * (float)y + b * (float)z;
        void Box(double x, double y, double z, double hx, double hy, double hz, Vector3 colour) =>
            mesh.Box(L(x, y, z), r, u, b, new Vector3((float)hx, (float)hy, (float)hz), colour);
        switch (kind)
        {
            case EnemyKind.CarFire:
                {
                    double burn = Math.Clamp(extra, 0.05, 1);
                    // Smoke first: grey curls rising off the stack, always there while it's going.
                    for (int i = 0; i < 6; i++)
                    {
                        double rise = (t * 0.6 + i * 0.37) % 1.6;
                        Box(-0.25 + 0.1 * Math.Sin(t + i), 0.9 + rise, (i - 2.5) * 0.3, 0.2 + 0.12 * rise, 0.14, 0.2, Palette.IronGrey * (float)(1.1 - 0.5 * rise));
                    }
                    if (phase is not (SpinePhase.Commit or SpinePhase.Punish))
                        return true;
                    // Alight: flames licking up the face of the load, taller the further it's gone, and their light.
                    mesh.Emissive = 1;
                    int tongues = 5 + (int)(6 * burn);
                    for (int i = 0; i < tongues; i++)
                    {
                        float flicker = (float)(0.55 + 0.45 * Math.Sin(t * 13 + i * 2.1));
                        double h = (0.35 + 1.1 * burn) * flicker, along = (i - (tongues - 1) / 2.0) * 0.22;
                        // Hot at the root, going to amber at the tips: two boxes a tongue, the tip narrower.
                        Box(-0.08, h * 0.3, along, 0.09, h * 0.3, 0.07, Palette.FurnaceOrange * (0.9f + 0.5f * flicker));
                        Box(-0.1, h * 0.75, along + 0.03, 0.05, h * 0.2, 0.04, Palette.LampAmber * (0.6f + 0.6f * flicker));
                    }
                    mesh.Emissive = 0;
                    mesh.PointLights.Add(new PointLight(L(-0.5, 0.9, 0), Palette.FurnaceOrange * (float)(1.4 + 2.0 * burn), (float)(4 + 5 * burn)));
                    return true;
                }
            case EnemyKind.Gnawers:
                {
                    // In the load, they're only heard; out of the crates, a boil of small dark backs over the stack and floor.
                    if (phase != SpinePhase.Punish)
                        return true;
                    int count = 4 + (int)(10 * Math.Clamp(health, 0, 1));
                    for (int i = 0; i < count; i++)
                    {
                        double a = t * (1.5 + i % 3) + i * 1.7;
                        Box(-0.2 + 0.35 * Math.Sin(a), 0.06 + 0.4 * (i % 4) * 0.25, 0.9 * Math.Cos(a * 0.7 + i), 0.08, 0.05, 0.13, Palette.SootBlack);
                    }
                    mesh.Emissive = 1;
                    for (int i = 0; i < count; i += 3)
                    {
                        double a = t * (1.5 + i % 3) + i * 1.7;
                        Box(-0.2 + 0.35 * Math.Sin(a), 0.1 + 0.4 * (i % 4) * 0.25, 0.9 * Math.Cos(a * 0.7 + i) - 0.12, 0.02, 0.02, 0.01, Palette.SignalRed);
                    }
                    mesh.Emissive = 0;
                    return true;
                }
            case EnemyKind.LooseLoad:
                return true;
        }
        return false;
    }
}
