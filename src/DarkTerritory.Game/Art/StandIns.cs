using System.Numerics;
using Ballast.Render;
using DarkTerritory.Sim.Enemies;

namespace DarkTerritory.Game.Art;

/// <summary>
/// Stand-ins for GDD v1.5's Shy Thing and Huddle from the kit's primitives: the bare greybox's (GreyboxScene), and the art
/// pass's where their models (tools/blender shy_thing, huddle) aren't built. Each reads by silhouette and by its telegraph
/// (App. A.1).
/// </summary>
public static class StandIns
{
    /// <summary>
    /// The Shy Thing (GDD v1.5 §21, a greybox stand-in): tall, thin and pale (§28's dead ivory), standing dead still in the
    /// dark with its arms hanging past its knees and no face but two dark pits. With someone under, it faces them; unhinging
    /// (the GRAB), its jaw comes down off its head like a snake's, the throat a black gape, its teeth round the rim.
    /// </summary>
    public static void ShyThing(MeshBuilder mesh, Vector3 o, Vector3 r, Vector3 u, Vector3 b, SpinePhase phase, double phaseSeconds, double grabWindow, Vector3? prey)
    {
        if (prey is { } feet)
        {
            var to = feet - o;
            to -= u * Vector3.Dot(to, u);
            if (to.LengthSquared() > 1e-4f)
            {
                b = -Vector3.Normalize(to);
                r = Vector3.Normalize(Vector3.Cross(u, b));
            }
        }
        void Draw(double x, double y, double z, double hx, double hy, double hz, Vector3 colour) =>
            mesh.Box(o + r * (float)x + u * (float)y + b * (float)z, r, u, b, new Vector3((float)hx, (float)hy, (float)hz), colour);
        // Pale: the flesh palette's hue (look.json "Corrupted", its texture), lightened to §28's dead ivory.
        var skin = Palette.Corrupted * 1.9f;
        double open = phase == SpinePhase.Grab ? Math.Clamp(phaseSeconds / Math.Max(1, grabWindow), 0, 1) : phase == SpinePhase.Punish ? 1 : 0;
        // Under its gaze, the slightest lean toward its victim; otherwise nothing moves at all (§31: still when watched).
        double lean = phase is SpinePhase.Telegraph or SpinePhase.Commit ? -0.06 - 0.02 * Math.Sin(phaseSeconds * 0.6) : 0;
        Draw(0.08, 0.62, 0, 0.045, 0.62, 0.045, skin);
        Draw(-0.08, 0.62, 0, 0.045, 0.62, 0.045, skin);
        Draw(0, 1.6, lean * 0.5, 0.15, 0.38, 0.08, skin);
        Draw(0.23, 1.15, lean * 0.3, 0.03, 0.62, 0.03, skin);
        Draw(-0.23, 1.15, lean * 0.3, 0.03, 0.62, 0.03, skin);
        Draw(0.23, 0.5, lean * 0.3 - 0.03, 0.04, 0.06, 0.05, skin * 0.9f);
        Draw(-0.23, 0.5, lean * 0.3 - 0.03, 0.04, 0.06, 0.05, skin * 0.9f);
        double head = 2.12 + 0.12 * open;
        Draw(0.03, head, lean - 0.02 * open, 0.095, 0.12, 0.1, skin);
        Draw(0.065, head + 0.02, lean - 0.11, 0.016, 0.012, 0.01, Palette.SootBlack);
        Draw(-0.005, head + 0.02, lean - 0.11, 0.016, 0.012, 0.01, Palette.SootBlack);
        // The jaw: shut under the head, or let down past the chest with the gape between.
        double drop = 0.04 + 0.9 * open;
        double front = lean - 0.06 - 0.12 * open;
        if (open > 0.02)
        {
            Draw(0.03, head - 0.12 - drop * 0.5, front, 0.065, drop * 0.5, 0.05 + 0.05 * open, Palette.SootBlack);
            // The skin of its cheeks stretched down either side of the gape.
            Draw(0.03 + 0.08, head - 0.12 - drop * 0.5, front + 0.02, 0.018, drop * 0.5, 0.05, skin * 0.85f);
            Draw(0.03 - 0.08, head - 0.12 - drop * 0.5, front + 0.02, 0.018, drop * 0.5, 0.05, skin * 0.85f);
        }
        Draw(0.03, head - 0.13 - drop, front, 0.1, 0.03, 0.07 + 0.05 * open, skin);
        if (open > 0.05)
            for (int i = -3; i <= 3; i++)
            {
                double x = 0.03 + i * (0.022 + 0.012 * open);
                Draw(x, head - 0.11, front - 0.06 - 0.06 * open, 0.008, 0.025, 0.006, Palette.BoardEnamel);
                Draw(x, head - 0.1 - drop, front - 0.06 - 0.06 * open, 0.008, 0.025, 0.006, Palette.BoardEnamel);
            }
    }

    /// <summary>
    /// The Huddle (GDD v1.5 §21, a greybox stand-in): a flock of small, soft, round things the colour of a fungus (§28's
    /// fungal beige), each with two black bead eyes, bobbing about the one spot; bristling (the telegraph) they puff up and
    /// their fur stands in spikes; on someone (the GRAB), they're piled up them, chest high.
    /// </summary>
    public static void Huddle(MeshBuilder mesh, Vector3 o, Vector3 r, Vector3 u, Vector3 b, SpinePhase phase, double phaseSeconds, double health, int id)
    {
        void Draw(double x, double y, double z, double hx, double hy, double hz, Vector3 colour) =>
            mesh.Box(o + r * (float)x + u * (float)y + b * (float)z, r, u, b, new Vector3((float)hx, (float)hy, (float)hz), colour);
        int count = Math.Max(1, (int)Math.Round(health));
        bool bristling = phase is SpinePhase.Telegraph or SpinePhase.Commit;
        bool onSomeone = phase is SpinePhase.Grab or SpinePhase.Punish;
        // Soft and pale (the flesh palette's hue, its texture: look.json "Corrupted"), the colour of something growing in the dark.
        var fur = Palette.Corrupted * 1.65f;
        for (int i = 0; i < count; i++)
        {
            double a = i * 2.39996 + id * 0.7;
            double rad = onSomeone ? 0.16 + 0.05 * (i % 2) : 0.2 + 0.13 * (i % 3);
            double x = Math.Cos(a) * rad, z = Math.Sin(a) * rad;
            double y = onSomeone ? 0.2 + i * 0.24 : 0.11 + 0.035 * Math.Abs(Math.Sin(phaseSeconds * 5.3 + i * 1.7));
            float s = (bristling ? 1.35f : 1) * (1.15f + 0.12f * (i % 3));
            Vector3 L(double lx, double ly, double lz) => o + r * (float)lx + u * (float)ly + b * (float)lz;
            // A soft round body, a paler belly, two little ears.
            Blob(mesh, L(x, y, z), r, u, b, new Vector3(0.13f, 0.11f, 0.13f) * s, fur);
            Blob(mesh, L(x, y - 0.025 * s, z - 0.06 * s), r, u, b, new Vector3(0.085f, 0.07f, 0.07f) * s, fur * 1.15f);
            Blob(mesh, L(x + 0.06 * s, y + 0.11 * s, z + 0.01), r, u, b, new Vector3(0.03f, 0.045f, 0.025f) * s, fur * 0.9f);
            Blob(mesh, L(x - 0.06 * s, y + 0.11 * s, z + 0.01), r, u, b, new Vector3(0.03f, 0.045f, 0.025f) * s, fur * 0.9f);
            // Big black bead eyes, looking toward the train and whoever's at it (the basis's forward), a glint in each.
            foreach (double side in new[] { -1.0, 1.0 })
            {
                Blob(mesh, L(x + side * 0.05 * s, y + 0.035 * s, z - 0.11 * s), r, u, b, new Vector3(0.032f, 0.036f, 0.022f) * s, Palette.SootBlack);
                mesh.Emissive = 0.6f;
                Draw(x + side * 0.05 * s + 0.01, y + 0.05 * s, z - 0.135 * s, 0.008, 0.008, 0.004, Palette.BoardEnamel);
                mesh.Emissive = 0;
            }
            if (bristling)
                for (int k = 0; k < 4; k++)
                    Draw(x + (k - 1.5) * 0.05 * s, y + 0.15 * s, z + (k % 2) * 0.04 * s, 0.012, 0.06 * s, 0.012, fur * 0.7f);
        }
    }

    /// <summary>A faceted ellipsoid about a basis (§27: chunky forms, visible simplification): soft things in a box world.</summary>
    public static void Blob(MeshBuilder mesh, Vector3 centre, Vector3 r, Vector3 u, Vector3 b, Vector3 radii, Vector3 colour)
    {
        const int Rings = 5, Segments = 8;
        Vector3 P(int ring, int seg)
        {
            double lat = -Math.PI / 2 + Math.PI * ring / Rings, lon = 2 * Math.PI * seg / Segments;
            return centre + r * (float)(Math.Cos(lat) * Math.Cos(lon) * radii.X) + u * (float)(Math.Sin(lat) * radii.Y)
                + b * (float)(Math.Cos(lat) * Math.Sin(lon) * radii.Z);
        }
        void Tri(Vector3 p, Vector3 q, Vector3 w)
        {
            // Outward, whatever order the rings give: a face's normal points away from the middle.
            if (Vector3.Dot(Vector3.Cross(q - p, w - p), (p + q + w) / 3 - centre) < 0)
                (q, w) = (w, q);
            mesh.Triangle(p, q, w, colour);
        }
        for (int i = 0; i < Rings; i++)
            for (int j = 0; j < Segments; j++)
            {
                var a = P(i, j);
                var c = P(i + 1, j + 1);
                if (i > 0)
                    Tri(a, P(i, j + 1), c);
                if (i < Rings - 1)
                    Tri(a, c, P(i + 1, j));
            }
    }
}
