using System.Numerics;

namespace DarkTerritory.Game.Art;

/// <summary>
/// The Hollow House come down (G1's note 585; the director, 9 Oct 2026: "it sinks and comes down on whoever's still
/// inside"). The house itself is the creature: its own pieces, as <see cref="OpenHouse"/> stands them, brought down.
/// </summary>
public static partial class TownKit
{
    /// <summary>
    /// An open house collapsed (in its frame, as <see cref="OpenHouse"/>): its own walls (StopWalls.OpenWalls) broken into
    /// lengths, each standing to a ragged height and leaning in over the floor, some gone right over onto it; the roof's
    /// ridge come down into the middle, each slope folded in on the wall stubs in broken panels, its rafters snapped and
    /// sticking up out of it; the gables down in slabs; timbers across the heap and plaster and slate rubble round the
    /// walls' feet and over the boards. From the house alone (and <paramref name="seed"/>), so every machine has the same heap.
    /// </summary>
    public static void CollapsedHouse(Kit k, Sim.Stops.StopBuilding b, int seed)
    {
        static Vector3 K(double x, double y, float h) => new((float)y, h, (float)-x);
        const float Floor = 0.17f;
        uint h = (uint)(seed * 2654435761u ^ (uint)b.Variant * 40503u) | 1;
        float R()
        {
            h ^= h << 13;
            h ^= h >> 17;
            h ^= h << 5;
            return (h & 0xFFFFFF) / 16777216f;
        }
        double hx = b.Length / 2, hy = b.Width / 2, t = Sim.Run.StopWalls.WallThickness;
        var outline = Sim.Run.StopWalls.Composite(b) ? Sim.Run.StopWalls.Outline(b) : null;
        // The boards, as they were (it's what's under the heap).
        k.Use("wood_grey", Palette.DeepBrown * 0.8f, 0.95f, 0, tile: 1);
        if (outline is null)
            k.Box(Vector3.Min(K(-hx + t, -hy + t, -0.4f), K(hx - t, hy - t, Floor)), Vector3.Max(K(-hx + t, -hy + t, -0.4f), K(hx - t, hy - t, Floor)));
        else
            foreach (var (x0, y0, x1, y1) in outline.Cells)
                k.Box(Vector3.Min(K(x0, y0, -0.4f), K(x1, y1, Floor)), Vector3.Max(K(x0, y0, -0.4f), K(x1, y1, Floor)));

        // The walls: each run broken into lengths about a metre and a half long, each leaning in over the floor (about its
        // foot, along the wall) as far as it went, a ragged height of it left; one in four gone right over, flat on the heap.
        foreach (var (x, y, wx, wy) in Sim.Run.StopWalls.OpenWalls(b))
        {
            bool alongX = wx > wy;
            double len = 2 * (alongX ? wx : wy), thick = alongX ? wy : wx;
            int n = Math.Max(1, (int)Math.Round(len / 1.5));
            // Which way is in: toward the house's middle across the wall (an inner wall's either way).
            double across = alongX ? y : x;
            float inward = Math.Abs(across) < 0.3 ? (R() < 0.5f ? -1 : 1) : -MathF.Sign((float)across);
            for (int i = 0; i < n; i++)
            {
                double a0 = (alongX ? x : y) - len / 2 + len * i / n, a1 = a0 + len / n - 0.04 - 0.1 * R();
                if (R() < 0.12f)
                    continue;
                bool flat = R() < 0.25f;
                float stand = flat ? 1.2f + 1.2f * R() : 1.0f + 1.6f * R(), lean = flat ? 1.35f + 0.15f * R() : 0.12f + 0.7f * R() * R();
                // The kit's frame: the house's x is −Z, its y +X. A wall along x leans about the kit's Z (its top toward kit X),
                // one along y about the kit's X (its top toward kit −Z, the house's +x).
                Vector3 foot = alongX ? K((a0 + a1) / 2, across, Floor) : K(across, (a0 + a1) / 2, Floor);
                var turn = alongX
                    ? Matrix4x4.CreateRotationZ(-inward * lean)
                    : Matrix4x4.CreateRotationX(-inward * lean);
                k.Push(Matrix4x4.CreateTranslation(-foot) * turn * Matrix4x4.CreateTranslation(foot));
                k.Use("plaster_ruin", Palette.BlueGrey, 0.95f, 0.05f, tile: 2);
                Vector3 lo = alongX ? K(a0, across - thick, Floor - 0.2f) : K(across - thick, a0, Floor - 0.2f);
                Vector3 hi = alongX ? K(a1, across + thick, Floor + stand) : K(across + thick, a1, Floor + stand);
                k.Box(Vector3.Min(lo, hi), Vector3.Max(lo, hi));
                // A ragged top: a chunk of it standing higher at one end.
                double c0 = a0 + (a1 - a0) * (R() < 0.5f ? 0 : 0.55), c1 = c0 + (a1 - a0) * 0.45;
                float more = 0.2f + 0.5f * R();
                lo = alongX ? K(c0, across - thick, Floor + stand) : K(across - thick, c0, Floor + stand);
                hi = alongX ? K(c1, across + thick, Floor + stand + more) : K(across + thick, c1, Floor + stand + more);
                k.Box(Vector3.Min(lo, hi), Vector3.Max(lo, hi));
                k.Pop();
            }
        }

        // The roof over each part: its ridge come down into the middle and each slope folded in on the wall stubs, broken
        // into panels down its length, each a little askew; the rafters under them snapped, some ends standing up out of it.
        var parts = outline is null || b.Parts.Count == 0 ? [new Sim.Stops.FootprintPart(0, 0, b.Length, b.Width)] : b.Parts;
        foreach (var part in parts)
        {
            bool ridgeAlongX = part.Length >= part.Width;
            float span = (float)Math.Min(part.Length, part.Width), length = (float)Math.Max(part.Length, part.Width);
            // (Across the ridge u, along it v, in the house's plan; to the kit's frame.)
            Vector3 M(double u, double v, float up) => ridgeAlongX ? K(part.X + v, part.Y + u, up) : K(part.X + u, part.Y + v, up);
            int panels = Math.Max(2, (int)Math.Round(length / 2.4));
            float ridgeDown = 1.1f + 0.5f * R();
            foreach (int side in new[] { -1, 1 })
                for (int p = 0; p < panels; p++)
                {
                    double v0 = -length / 2 - 0.3 + (length + 0.6) * p / panels, v1 = v0 + (length + 0.6) / panels - 0.06 - 0.15 * R();
                    float eave = 1.7f + 0.8f * R(), mid = ridgeDown + 0.35f * R(), sag = 0.2f * R();
                    double out_ = side * (span / 2 + 0.3), inner = side * (0.05 + 0.4 * R());
                    var a = M(out_, v0, Floor + eave);
                    var bb = M(out_, v1, Floor + eave + 0.2f * (R() - 0.5f));
                    var c = M(inner, v1, Floor + mid + sag);
                    var d = M(inner, v0, Floor + mid);
                    k.Use("roof_slate", Palette.Charcoal, 0.95f, 0.1f, tile: 1.5f);
                    k.Quad(a, bb, c, d, twoSided: true);
                    // The rafters under it: two a panel, one snapped and stood up out of the heap.
                    k.Use("wood_grey", new Vector3(0.2f, 0.15f, 0.1f), 0.9f, 0.02f, tile: 1);
                    foreach (double f in new[] { 0.25, 0.75 })
                    {
                        double v = v0 + (v1 - v0) * f;
                        var lo = M(out_ - side * 0.1, v, Floor + eave - 0.08f);
                        var top = M(inner, v, Floor + mid - 0.05f);
                        k.Rod(lo, top, 0.045f);
                        if (R() < 0.35f)
                            k.Rod(top, top + new Vector3(0.6f * (R() - 0.5f), 1.2f + 1.4f * R(), 0.6f * (R() - 0.5f)), 0.05f);
                    }
                }
            // The gables, down: each end's triangle of plaster lying in over the heap at a slant.
            k.Use("plaster_ruin", Palette.BlueGrey * 0.9f, 0.95f, 0.05f, tile: 2);
            foreach (int end in new[] { -1, 1 })
            {
                float w = span / 2, rise = span * 0.42f * (0.5f + 0.4f * R());
                double v = end * (length / 2 - 0.1);
                float tilt = 0.9f + 0.5f * R();
                var foot = M(0, v, Floor + 1.8f);
                var l = M(-w * 0.8, v, Floor + 1.8f);
                var r = M(w * 0.7, v, Floor + 1.8f);
                var inward = M(0, v - end * 1, Floor + 1.8f) - foot;
                var top = foot + Vector3.Normalize(inward) * rise * MathF.Sin(tilt) + new Vector3(0, rise * MathF.Cos(tilt), 0);
                k.Tri(l, top, r);
                k.Tri(r, top, l);
            }
        }

        // Timbers across the heap: the wall plates and joists, lying at all angles, ends up.
        k.Use("wood_grey", new Vector3(0.22f, 0.17f, 0.11f), 0.9f, 0.02f, tile: 1);
        int timbers = 5 + (int)(b.Length * b.Width / 12);
        for (int i = 0; i < timbers; i++)
        {
            var a = K((R() - 0.5) * b.Length * 0.8, (R() - 0.5) * b.Width * 0.8, Floor + 1.0f + 1.0f * R());
            float yaw = R() * MathF.Tau, pitch = 0.1f + 0.7f * R(), len = 1.5f + 2.2f * R();
            var dir = new Vector3(MathF.Cos(yaw) * MathF.Cos(pitch), MathF.Sin(pitch), MathF.Sin(yaw) * MathF.Cos(pitch));
            k.Rod(a - dir * len * 0.4f, a + dir * len * 0.6f, 0.05f + 0.03f * R());
        }

        // Rubble: plaster lumps and broken slates heaped at the walls' feet and over the boards.
        int rubble = 18 + (int)(b.Length * b.Width / 3);
        for (int i = 0; i < rubble; i++)
        {
            bool edge = R() < 0.6f;
            double x = (R() - 0.5) * (b.Length - 0.6), y = (R() - 0.5) * (b.Width - 0.6);
            if (edge)
            {
                if (R() < 0.5f)
                    x = Math.Sign(x) * (hx - 0.4 - 0.5 * R());
                else
                    y = Math.Sign(y) * (hy - 0.4 - 0.5 * R());
            }
            float s = 0.08f + 0.22f * R(), up = 0.05f + 0.35f * R() * (edge ? 1.6f : 1);
            var at = K(x, y, Floor + up * 0.5f);
            k.Push(Matrix4x4.CreateRotationY(R() * MathF.Tau) * Matrix4x4.CreateRotationX(0.6f * (R() - 0.5f)) * Matrix4x4.CreateTranslation(at));
            if (R() < 0.6f)
                k.Use("plaster_ruin", new Vector3(0.5f, 0.48f, 0.44f), 0.95f, 0.02f, tile: 1);
            else
                k.Use("roof_slate", Palette.Charcoal, 0.9f, 0.1f, tile: 1);
            k.Box(new Vector3(-s, -up * 0.5f, -s * 0.7f), new Vector3(s, up * 0.5f, s * 0.7f));
            k.Pop();
        }
        // The earth under it, pushed up through the boards in the middle where it went down.
        k.Use("ground_mud", new Vector3(0.2f, 0.16f, 0.12f), 0.9f, 0, tile: 2);
        for (int i = 0; i < 6; i++)
        {
            var at = K((R() - 0.5) * b.Length * 0.5, (R() - 0.5) * b.Width * 0.5, Floor);
            float s = 0.4f + 0.6f * R();
            k.Push(Matrix4x4.CreateRotationY(R() * MathF.Tau) * Matrix4x4.CreateTranslation(at));
            k.Box(new Vector3(-s, -0.1f, -s * 0.7f), new Vector3(s, 0.12f + 0.2f * R(), s * 0.7f));
            k.Pop();
        }
    }
}
