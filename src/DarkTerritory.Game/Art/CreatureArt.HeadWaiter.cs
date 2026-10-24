using System.Numerics;
using Ballast.Render;
using DarkTerritory.Sim.Enemies;

namespace DarkTerritory.Game.Art;

/// <summary>The Head Waiter (note 1008), drawn from boxes until it's modelled (docs/design/creatures/head-waiter.md §3).</summary>
public sealed partial class CreatureArt
{
    /// <summary>
    /// THE HEAD WAITER's stand-in: too tall, thin, in a black waistcoat and a long white apron, a white face with a painted
    /// moustache; a silver tray up on its fingertips with a covered plate, a glass in its other hand. Stood behind the seat it's
    /// come to wait on, facing the way the seat does (−Z of the basis): ringing, the glass up and glinting (<paramref name="pulse"/>);
    /// waiting, bent at the waist over them; clearing, both arms down to them. In its own colours, not the world's surfaces.
    /// </summary>
    public static void HeadWaiterStandIn(MeshBuilder mesh, Vector3 o, Vector3 r, Vector3 u, Vector3 b, HeadWaiterMode mode, float pulse)
    {
        var style = mesh.Style;
        mesh.Style = null;
        try
        {
            bool bowing = mode is HeadWaiterMode.Wait or HeadWaiterMode.Dismissed or HeadWaiterMode.Clear;
            float lean = mode == HeadWaiterMode.Clear ? 0.45f : bowing ? 0.22f : 0;
            Vector3 W(float x, float y, float z) => o + r * x + u * y + b * (z - lean * MathF.Max(0, y - 1.1f));
            void Part(float x, float y, float z, float hx, float hy, float hz, Vector3 colour) =>
                mesh.Box(W(x, y, z), r, u, b, new Vector3(hx, hy, hz), colour);
            var black = Palette.SootBlack * 0.8f;
            var white = new Vector3(0.86f, 0.84f, 0.78f);
            var silver = new Vector3(0.7f, 0.72f, 0.74f);
            foreach (float x in new[] { -0.09f, 0.09f })
                Part(x, 0.5f, 0, 0.055f, 0.5f, 0.06f, black);
            Part(0, 0.75f, -0.09f, 0.17f, 0.42f, 0.02f, white);
            Part(0, 1.4f, 0, 0.16f, 0.32f, 0.1f, black);
            Part(0, 1.48f, -0.1f, 0.05f, 0.16f, 0.005f, white);
            Part(0, 1.85f, -0.03f, 0.09f, 0.13f, 0.1f, white);
            Part(0, 1.8f, -0.135f, 0.05f, 0.006f, 0.006f, black);
            if (mode == HeadWaiterMode.Clear)
            {
                foreach (float x in new[] { -0.2f, 0.2f })
                    Part(x, 1.05f, -0.35f, 0.035f, 0.035f, 0.32f, black);
                return;
            }
            // The tray, up on its left fingertips (the arm bent up), its cover on it; the napkin over the right arm.
            Part(-0.24f, 1.45f, 0, 0.035f, 0.22f, 0.035f, black);
            Part(-0.3f, 1.72f, -0.08f, 0.2f, 0.01f, 0.2f, silver);
            Part(-0.3f, 1.79f, -0.08f, 0.11f, 0.06f, 0.11f, silver * 0.9f);
            Part(0.24f, 1.3f, -0.12f, 0.035f, 0.035f, 0.22f, black);
            Part(0.24f, 1.26f, -0.06f, 0.05f, 0.08f, 0.04f, white);
            // The glass in its right hand, the spoon to it; ringing, it's raised and catches the light.
            bool ringing = mode == HeadWaiterMode.Serve;
            float gy = ringing ? 1.62f : 1.3f;
            if (ringing)
                mesh.Emissive = 0.6f + 0.4f * pulse;
            Part(0.26f, gy, -0.34f, 0.025f, 0.06f, 0.025f, white * 1.1f);
            mesh.Emissive = 0;
            if (ringing)
                Part(0.2f, gy + 0.03f, -0.36f, 0.06f, 0.004f, 0.004f, silver);
        }
        finally
        {
            mesh.Style = style;
        }
    }
}
