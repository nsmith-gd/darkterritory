using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Run;

/// <summary>
/// `dt screenshot --walls [radius]` (note 869; the director's playtest of 19 Oct, line 2: "Collision boxes are off"): every
/// Sim wall within the radius of the eye (60 m unless said) drawn over the scene as glowing edges, so what's walked into can be
/// laid against what's drawn. The walls are a whole night's as the app stands them (<see cref="DarkTerritory.Game.PrototypeSession"/>:
/// the run, its facilities and lineside, both towns), not the screenshot's own stops-only set. By kind: a building's (its
/// owner) magenta, a fort's blue, a gate's leaf cyan, a door green, a coaling tower's trestle orange, anything else yellow.
/// Returns how many it drew.
/// </summary>
static class WallsOverlay
{
    public static int Draw(MeshBuilder mesh, StopWalls walls, DarkTerritory.Sim.Rail.RailLine line, Double3 eye, double radius)
    {
        int drawn = 0;
        double hint = 0;
        foreach (var w in walls.All)
        {
            if (((w.Centre - eye) with { Y = 0 }).Length > radius + w.HalfLength + w.HalfWidth)
                continue;
            if (w.Gate != 0 && walls.GateOpen(w.Gate))
                continue;
            var colour = w.Gate != 0 ? new Vector4(0.1f, 1, 1, 1) : w.Door != 0 ? new Vector4(0.2f, 1, 0.2f, 1)
                : w.Trestle ? new Vector4(1, 0.5f, 0.05f, 1) : w.Owner != 0 ? new Vector4(1, 0.15f, 1, 1)
                : w.Fort ? new Vector4(0.25f, 0.45f, 1, 1) : new Vector4(1, 1, 0.1f, 1);
            var along = (w.Axis with { Y = 0 }).Normalized;
            var across = new Double3(-along.Z, 0, along.X);
            // A wall reaches under the ground (so a slope can't leave a gap under it): its edges from the ground the crew walk on.
            double bottom = Math.Clamp(DarkTerritory.Sim.Player.PlayerMotor.GroundAt(w.Centre, line, ref hint), w.Bottom, w.Top);
            var corners = new Vector3[8];
            int i = 0;
            foreach (double y in new[] { bottom, w.Top })
                foreach (var (a, c) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
                    corners[i++] = ((w.Centre with { Y = y }) + along * (a * w.HalfLength) + across * (c * w.HalfWidth)).RelativeTo(eye);
            for (int k = 0; k < 4; k++)
            {
                Edge(mesh, corners[k], corners[(k + 1) % 4], colour);
                Edge(mesh, corners[4 + k], corners[4 + (k + 1) % 4], colour);
                Edge(mesh, corners[k], corners[4 + k], colour);
            }
            drawn++;
        }
        return drawn;
    }

    // An edge as a thin glowing strip turned to the eye.
    static void Edge(MeshBuilder mesh, Vector3 a, Vector3 b, Vector4 colour)
    {
        var mid = (a + b) / 2;
        var side = Vector3.Cross(b - a, mid);
        if (side.LengthSquared() < 1e-8f)
            return;
        side = Vector3.Normalize(side) * MathF.Max(0.025f, mid.Length() * 0.0016f);
        // (Layer -1 is the soft blob: its middle, solid.)
        FxVertex V(Vector3 p) => new(p, new Vector2(0.5f, 0.5f), colour, -1);
        mesh.FxTriangle(FxBlend.Additive, V(a - side), V(b - side), V(b + side));
        mesh.FxTriangle(FxBlend.Additive, V(a - side), V(b + side), V(a + side));
    }
}
