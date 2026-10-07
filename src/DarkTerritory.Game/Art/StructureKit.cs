using System.Numerics;
using Ballast.Render;
using DarkTerritory.Sim.Route;

namespace DarkTerritory.Game.Art;

/// <summary>
/// The line's structures (GDD §30: "rail bridges ... fortified towns ... facilities oversized, half-abandoned and too big
/// for the train"; the art sheet's viaduct and fortified station): cooked pieces in a frame along the line (−Z forward,
/// +X right, origin at rail height on the centre line), each built to be repeated bay by bay.
/// </summary>
public static class StructureKit
{
    /// <summary>A viaduct bay's length along the line: pier centre to pier centre.</summary>
    public const float Bay = 25;
    /// <summary>A timber trestle's bent spacing.</summary>
    public const float Bent = 5;

    /// <summary>A quad mapped by its position on one plane (<paramref name="axis"/> 0 = a face across X, 2 = across Z), metres.</summary>
    static void Planar(Kit k, Vector3 a, Vector3 b, Vector3 c, Vector3 d, int axis)
    {
        Vector2 U(Vector3 p) => axis == 0 ? new Vector2(p.Z, -p.Y) : new Vector2(p.X, -p.Y);
        k.Quad(a, b, c, d, U(a), U(b), U(c), U(d));
    }

    /// <summary>
    /// One bay of a masonry viaduct over a gorge <paramref name="depth"/> deep (the sheet's arches): a pier at z = 0, a
    /// round arch springing from it to the next, the spandrel walls, a string course and a coped parapet either side, and
    /// the ballasted deck on top.
    /// </summary>
    public static MeshAsset ViaductBay(Look? look, float depth, bool lastPier)
    {
        var k = new Kit(look, 900);
        const float half = 2.6f, pier = 3.0f, deck = 1.8f;
        float span = Bay - pier, r = span / 2, crown = -deck, spring = crown - r;
        float z0 = -pier / 2, zc = z0 - r; // the arch's start and centre
        k.Use("stone_block", Palette.Charcoal, 0.7f, 0.1f, tile: 2.5f);
        // The pier: battered a little (wider at its foot), from well below the gorge floor to the springing.
        foreach (float pz in lastPier ? new[] { 0f, -Bay } : new[] { 0f })
        {
            float foot = -depth - 3;
            var p0 = new Vector3(-half - 0.6f, foot, pz + pier / 2 + 0.3f);
            var p1 = new Vector3(half + 0.6f, spring, pz - pier / 2 - 0.3f);
            k.Box(new Vector3(p0.X, p0.Y, p1.Z), new Vector3(p1.X, p1.Y, p0.Z), Kit.Faces.All & ~Kit.Faces.NegY);
            // A plinth course where the batter steps in.
            k.Box(new Vector3(-half - 0.35f, spring, pz - pier / 2 - 0.1f), new Vector3(half + 0.35f, spring + 0.5f, pz + pier / 2 + 0.1f), Kit.Faces.All & ~Kit.Faces.NegY);
        }
        // The spandrels: each side face between the deck and the arch, and the pier's face up to the deck.
        const int n = 12;
        foreach (int side in new[] { -1, 1 })
        {
            float x = side * half;
            for (int i = 0; i < n; i++)
            {
                float t0 = MathF.PI * i / n, t1 = MathF.PI * (i + 1) / n;
                var a0 = new Vector3(x, spring + r * MathF.Sin(t0), zc + r * MathF.Cos(t0));
                var a1 = new Vector3(x, spring + r * MathF.Sin(t1), zc + r * MathF.Cos(t1));
                var top0 = a0 with { Y = 0 };
                var top1 = a1 with { Y = 0 };
                // Clockwise from the top left as seen from outside (+X side looks at −X: its left is +Z).
                if (side > 0)
                    Planar(k, top0, top1, a1, a0, 0);
                else
                    Planar(k, top1, top0, a0, a1, 0);
            }
            // The pier's part of the face, from the springing to the deck (either side of z = 0).
            var pa = new Vector3(x, 0, pier / 2);
            var pb = new Vector3(x, 0, -pier / 2);
            if (side > 0)
                Planar(k, pa, pb, pb with { Y = spring }, pa with { Y = spring }, 0);
            else
                Planar(k, pb, pa, pa with { Y = spring }, pb with { Y = spring }, 0);
        }
        // The arch's underside (the intrados), in voussoir-sized stone.
        k.Use("stone_block", Palette.Charcoal, 0.8f, 0.1f, tile: 1.6f);
        k.Shade(0.8f);
        for (int i = 0; i < n; i++)
        {
            float t0 = MathF.PI * i / n, t1 = MathF.PI * (i + 1) / n;
            var a0 = new Vector3(0, spring + r * MathF.Sin(t0), zc + r * MathF.Cos(t0));
            var a1 = new Vector3(0, spring + r * MathF.Sin(t1), zc + r * MathF.Cos(t1));
            // Seen from below: facing down and in, toward the arch's centre.
            k.Quad(a0 with { X = -half }, a0 with { X = half }, a1 with { X = half }, a1 with { X = -half },
                new(-half, t0 * r), new(half, t0 * r), new(half, t1 * r), new(-half, t1 * r));
        }
        // String course, parapets and coping.
        k.Use("stone_block", Palette.Charcoal, 0.7f, 0.1f, tile: 2.5f);
        foreach (int side in new[] { -1, 1 })
        {
            float x0 = side < 0 ? -half - 0.15f : half - 0.3f, x1 = side < 0 ? -half + 0.3f : half + 0.15f;
            k.Box(new Vector3(x0, -0.45f, -Bay), new Vector3(x1, -0.25f, 0));
            float p0 = side < 0 ? -half : half - 0.3f, p1 = side < 0 ? -half + 0.3f : half;
            k.Box(new Vector3(p0, -0.25f, -Bay), new Vector3(p1, 0.95f, 0), Kit.Faces.All & ~Kit.Faces.NegY);
            k.Shade(1.15f);
            k.Box(new Vector3(p0 - 0.06f, 0.95f, -Bay), new Vector3(p1 + 0.06f, 1.08f, 0), Kit.Faces.All & ~Kit.Faces.NegY);
            k.Use("stone_block", Palette.Charcoal, 0.7f, 0.1f, tile: 2.5f);
        }
        // The deck: ballast between the parapets (the sleepers and rails are the track's).
        k.Use("ballast", Palette.Ballast, 0.6f, 0, tile: 1.5f);
        k.Box(new Vector3(-half + 0.3f, -0.25f, -Bay), new Vector3(half - 0.3f, 0.0f, 0), Kit.Faces.PosY);
        return k.Build($"viaduct-{depth:0}-{lastPier}");
    }

    /// <summary>
    /// One bay of an iron girder viaduct over a gorge <paramref name="depth"/> deep (GDD §30's rail bridges, linegen
    /// plan §12.3 "girders"): a steel trestle tower at z = 0, four battered legs on stone footings, braced in Xs storey by
    /// storey; and on it the deck girders to the next tower, two deep riveted plate girders under the rails, stiffened
    /// every metre and a half, cross-framed between, carrying the open timber deck, an iron-railed walkway either side.
    /// Red oxide gone to rust: nothing like the masonry viaduct's stone, and lighter-looking than it is.
    /// </summary>
    public static MeshAsset GirderBay(Look? look, float depth, bool lastPier)
    {
        var k = new Kit(look, 920);
        const float girder = 1.0f, deep = 1.7f, flange = 0.28f;
        float under = -0.35f, bottom = under - deep;
        // The tower: legs battered out to its foot, braced across and along every storey.
        foreach (float tz in lastPier ? new[] { 0f, -Bay } : new[] { 0f })
        {
            float foot = -depth - 0.5f;
            k.Use("stone_block", Palette.Charcoal, 0.7f, 0.1f, tile: 2f);
            float spreadFoot = 1.6f + depth * 0.09f;
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                    k.Box(new Vector3(sx * spreadFoot - 0.7f, foot - 1.5f, tz + sz * 2.2f - 0.7f), new Vector3(sx * spreadFoot + 0.7f, foot + 0.4f, tz + sz * 2.2f + 0.7f),
                        Kit.Faces.All & ~Kit.Faces.NegY);
            k.Use("rust_heavy", Palette.RustRed, 0.75f, 0.35f, tile: 1.2f);
            Vector3 Leg(float sx, float sz, float y) => new(sx * (1.6f + (bottom - y) * 0.09f), y, tz + sz * 2.2f);
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                    k.Rod(Leg(sx, sz, foot + 0.4f), Leg(sx, sz, bottom), 0.17f);
            k.Use("rust_heavy", Palette.RustRed, 0.8f, 0.3f, tile: 1.5f);
            const float storey = 7f;
            for (float y = bottom; y > foot + 1; y -= storey)
            {
                float y1 = MathF.Max(y - storey, foot + 0.4f);
                // Struts round the storey's top, and an X in each face.
                foreach (float sz in new[] { -1f, 1f })
                {
                    k.Rod(Leg(-1, sz, y), Leg(1, sz, y), 0.09f);
                    k.Rod(Leg(-1, sz, y), Leg(1, sz, y1), 0.045f);
                    k.Rod(Leg(1, sz, y), Leg(-1, sz, y1), 0.045f);
                }
                foreach (float sx in new[] { -1f, 1f })
                {
                    k.Rod(Leg(sx, -1, y), Leg(sx, 1, y), 0.09f);
                    k.Rod(Leg(sx, -1, y), Leg(sx, 1, y1), 0.045f);
                    k.Rod(Leg(sx, 1, y), Leg(sx, -1, y1), 0.045f);
                }
            }
            // The cap the girders bear on.
            k.Box(new Vector3(-2.0f, bottom - 0.45f, tz - 2.5f), new Vector3(2.0f, bottom, tz + 2.5f));
        }
        // The girders: web, flanges top and bottom, and a stiffener every metre and a half.
        k.Use("paint_oxide", Palette.RustRed, 0.85f, 0.25f, tile: 1.4f);
        foreach (float sx in new[] { -girder, girder })
        {
            k.Box(new Vector3(sx - 0.02f, bottom, -Bay), new Vector3(sx + 0.02f, under, 0));
            foreach (float y in new[] { bottom, under - 0.05f })
                k.Box(new Vector3(sx - flange / 2, y, -Bay), new Vector3(sx + flange / 2, y + 0.05f, 0));
            for (float z = -0.75f; z > -Bay; z -= 1.5f)
                foreach (float face in new[] { -1f, 1f })
                    k.Box(new Vector3(sx + face * 0.02f - (face < 0 ? 0.08f : 0), bottom + 0.05f, z - 0.06f),
                        new Vector3(sx + face * 0.02f + (face > 0 ? 0.08f : 0), under - 0.05f, z + 0.06f), top: false, bottom: false);
        }
        // Cross-frames between them, every three metres.
        k.Use("rust_heavy", Palette.RustRed, 0.8f, 0.3f, tile: 1.5f);
        for (float z = -1.5f; z > -Bay; z -= 3f)
        {
            k.Rod(new Vector3(-girder, under - 0.1f, z), new Vector3(girder, bottom + 0.1f, z), 0.035f);
            k.Rod(new Vector3(girder, under - 0.1f, z), new Vector3(-girder, bottom + 0.1f, z), 0.035f);
        }
        // The open deck: walkway planks either side of the track's own ties, an iron handrail on posts.
        k.Use("wood_grey", Palette.DeepBrown, 0.9f, 0, tile: 1.2f);
        foreach (int side in new[] { -1, 1 })
        {
            float x0 = side < 0 ? -2.4f : 1.55f, x1 = side < 0 ? -1.55f : 2.4f;
            k.Box(new Vector3(x0, under - 0.08f, -Bay), new Vector3(x1, under + 0.02f, 0), Kit.Faces.All & ~Kit.Faces.NegY);
            // Its brackets out from the girder.
            for (float z = -1.5f; z > -Bay; z -= 3f)
                k.Rod(new Vector3(side * girder, bottom + 0.5f, z), new Vector3(side * 2.3f, under - 0.1f, z), 0.04f);
        }
        k.Use("rust_heavy", Palette.IronGrey, 0.8f, 0.3f, tile: 1.5f);
        foreach (int side in new[] { -1, 1 })
        {
            for (float z = 0; z > -Bay; z -= 2.5f)
                k.Rod(new Vector3(side * 2.35f, under, z), new Vector3(side * 2.35f, under + 1.05f, z), 0.025f);
            k.Rod(new Vector3(side * 2.35f, under + 1.05f, 0), new Vector3(side * 2.35f, under + 1.05f, -Bay), 0.03f);
            k.Rod(new Vector3(side * 2.35f, under + 0.55f, 0), new Vector3(side * 2.35f, under + 0.55f, -Bay), 0.02f);
        }
        return k.Build($"girder-{depth:0}-{lastPier}");
    }

    /// <summary>
    /// One span of an iron through truss (linegen plan §12.3 "truss": the long crossings), Bay long, on a stone pier at
    /// z = 0: a Pratt truss either side, its end posts raked down to the bearings, verticals at each panel and the
    /// diagonals slanting in toward the middle; the top chords braced across overhead in a lattice (the train runs
    /// inside it, the bracing going over the roofs a man's height up: mind your head); floor beams and stringers under
    /// the track. Each span reads on its own from up the line, a cage, unlike the open girders.
    /// </summary>
    public static MeshAsset TrussSpan(Look? look, float depth, bool lastPier)
    {
        var k = new Kit(look, 930);
        const float half = 2.7f, high = 7.0f, low = -0.55f, z0 = -0.6f, z1 = -Bay + 0.6f, panel = (z0 - z1) / 5;
        // The pier.
        k.Use("stone_block", Palette.Charcoal, 0.7f, 0.1f, tile: 2.5f);
        foreach (float pz in lastPier ? new[] { 0f, -Bay } : new[] { 0f })
        {
            k.Box(new Vector3(-half - 0.9f, -depth - 3, pz - 1.3f), new Vector3(half + 0.9f, low - 0.35f, pz + 1.3f), Kit.Faces.All & ~Kit.Faces.NegY);
            k.Shade(1.1f);
            k.Box(new Vector3(-half - 1.05f, low - 0.6f, pz - 1.45f), new Vector3(half + 1.05f, low - 0.35f, pz + 1.45f), Kit.Faces.All & ~Kit.Faces.NegY);
            k.Use("stone_block", Palette.Charcoal, 0.7f, 0.1f, tile: 2.5f);
        }
        k.Use("rust_heavy", Palette.RustRed, 0.75f, 0.35f, tile: 1.3f);
        foreach (int side in new[] { -1, 1 })
        {
            float x = side * half;
            // Chords: the bottom the whole span, the top between the end posts' heads.
            float top0 = z0 - panel, top1 = z1 + panel;
            k.Box(new Vector3(x - 0.18f, low - 0.2f, z1), new Vector3(x + 0.18f, low + 0.2f, z0));
            k.Box(new Vector3(x - 0.22f, high - 0.22f, top1), new Vector3(x + 0.22f, high + 0.22f, top0));
            // The raked end posts.
            k.Rod(new Vector3(x, low, z0), new Vector3(x, high, top0), 0.2f);
            k.Rod(new Vector3(x, low, z1), new Vector3(x, high, top1), 0.2f);
            // Verticals at the panel points, and the diagonals slanting down toward the middle (Pratt).
            for (int i = 1; i < 5; i++)
            {
                float z = z0 - i * panel;
                k.Rod(new Vector3(x, low, z), new Vector3(x, high, z), 0.11f);
            }
            // Panel 1 slants down toward the middle, panel 3 likewise from the far end; the middle panel's crossed.
            float p1 = z0 - panel, p2 = z0 - 2 * panel, p3 = z0 - 3 * panel, p4 = z0 - 4 * panel;
            k.Rod(new Vector3(x, high, p1), new Vector3(x, low, p2), 0.07f);
            k.Rod(new Vector3(x, high, p4), new Vector3(x, low, p3), 0.07f);
            k.Rod(new Vector3(x, high, p2), new Vector3(x, low, p3), 0.05f);
            k.Rod(new Vector3(x, high, p3), new Vector3(x, low, p2), 0.05f);
        }
        // Overhead: struts across at each top panel point, an X between each.
        for (int i = 1; i < 5; i++)
        {
            float z = z0 - i * panel;
            k.Rod(new Vector3(-half, high, z), new Vector3(half, high, z), 0.09f);
            // Knee braces down the verticals, the portal's look at the ends.
            k.Rod(new Vector3(-half, high - 1.1f, z), new Vector3(-half + 0.9f, high, z), 0.05f);
            k.Rod(new Vector3(half, high - 1.1f, z), new Vector3(half - 0.9f, high, z), 0.05f);
            if (i < 4)
            {
                k.Rod(new Vector3(-half, high, z), new Vector3(half, high, z - panel), 0.035f);
                k.Rod(new Vector3(half, high, z), new Vector3(-half, high, z - panel), 0.035f);
            }
        }
        // The portals at either end, a plate across the end posts' heads with the lattice under it.
        k.Use("paint_oxide", Palette.RustRed, 0.85f, 0.25f, tile: 1.4f);
        foreach (float z in new[] { z0 - panel * 0.55f, z1 + panel * 0.55f })
        {
            k.Box(new Vector3(-half, high - 1.4f, z - 0.06f), new Vector3(half, high - 0.6f, z + 0.06f));
        }
        // Floor beams at the panel points and the stringers under the rails; a walkway either side of the track.
        k.Use("rust_heavy", Palette.RustRed, 0.8f, 0.3f, tile: 1.5f);
        for (int i = 0; i <= 5; i++)
        {
            float z = z0 - i * (z0 - z1) / 5;
            k.Box(new Vector3(-half, low - 0.45f, z - 0.15f), new Vector3(half, low - 0.05f, z + 0.15f));
        }
        foreach (float sx in new[] { -0.75f, 0.75f })
            k.Box(new Vector3(sx - 0.12f, low - 0.05f, z1), new Vector3(sx + 0.12f, low + 0.3f, z0));
        k.Use("wood_grey", Palette.DeepBrown, 0.9f, 0, tile: 1.2f);
        foreach (int side in new[] { -1, 1 })
        {
            float x0 = side < 0 ? -half + 0.25f : 1.55f, x1 = side < 0 ? -1.55f : half - 0.25f;
            k.Box(new Vector3(x0, low + 0.22f, z1), new Vector3(x1, low + 0.3f, z0), Kit.Faces.All & ~Kit.Faces.NegY);
        }
        return k.Build($"truss-{depth:0}-{lastPier}");
    }

    /// <summary>
    /// A timber trestle bent (a weak bridge, GDD §17: "a bridge that takes four cars"): four raked posts from the gorge
    /// floor, sway bracing, a cap, and the stringers and deck timbers to the next bent. Handrails of rough timber.
    /// </summary>
    public static MeshAsset TrestleBent(Look? look, float depth)
    {
        var k = new Kit(look, 910);
        float foot = -depth - 1;
        k.Use("wood_grey", Palette.DeepBrown, 0.9f, 0, tile: 1.5f);
        foreach (float x in new[] { -1.9f, -0.7f, 0.7f, 1.9f })
        {
            float rake = MathF.Sign(x) * (MathF.Abs(x) > 1 ? 0.14f : 0.03f);
            k.Rod(new Vector3(x + rake * depth, foot, 0), new Vector3(x, -0.6f, 0), 0.17f);
        }
        k.Use("wood_sleeper", Palette.DeepBrown, 0.9f, 0, tile: 1.5f);
        for (float y = -3.5f; y > foot + 2; y -= 4.5f)
        {
            float w = 2.2f + (-y) * 0.14f;
            k.Rod(new Vector3(-w, y, 0.2f), new Vector3(w, y - 4.0f, 0.2f), 0.08f);
            k.Rod(new Vector3(w, y, -0.2f), new Vector3(-w, y - 4.0f, -0.2f), 0.08f);
            k.Box(new Vector3(-w, y - 0.12f, -0.12f), new Vector3(w, y + 0.12f, 0.12f));
        }
        k.Box(new Vector3(-2.3f, -0.75f, -0.25f), new Vector3(2.3f, -0.45f, 0.25f));
        // Stringers to the next bent, and the ties' deck.
        foreach (float x in new[] { -0.75f, 0.75f })
            k.Box(new Vector3(x - 0.18f, -0.45f, -Bent), new Vector3(x + 0.18f, -0.08f, 0));
        k.Use("wood_grey", Palette.DeepBrown, 0.9f, 0, tile: 1.2f);
        for (float z = -0.2f; z > -Bent; z -= 0.9f)
            k.Box(new Vector3(-2.2f, -0.08f, z - 0.3f), new Vector3(2.2f, 0.0f, z), Kit.Faces.All & ~Kit.Faces.NegY);
        foreach (int side in new[] { -1, 1 })
        {
            k.Rod(new Vector3(side * 2.1f, 0, -0.1f), new Vector3(side * 2.1f, 1.0f, -0.1f), 0.06f);
            k.Rod(new Vector3(side * 2.1f, 1.0f, 0), new Vector3(side * 2.1f, 1.0f, -Bent), 0.05f);
        }
        return k.Build($"trestle-{depth:0}");
    }

    /// <summary>A tunnel's bore profile: sides up to <see cref="TunnelSpring"/>, a round crown, counter-clockwise seen from +Z (inside out).</summary>
    public const float TunnelHalf = 3.1f, TunnelSpring = 4.4f;

    static Vector2[] Bore(float grow)
    {
        var p = new List<Vector2> { new(TunnelHalf + grow, -0.3f) };
        const int n = 10;
        for (int i = 0; i <= n; i++)
        {
            float a = MathF.PI * i / n;
            p.Add(new Vector2(MathF.Cos(a) * (TunnelHalf + grow), TunnelSpring + MathF.Sin(a) * (TunnelHalf + grow)));
        }
        p.Add(new Vector2(-TunnelHalf - grow, -0.3f));
        return [.. p];
    }

    /// <summary>A length of tunnel lining: sooted brick, seen from inside, and the ballast floor.</summary>
    public static MeshAsset TunnelLining(Look? look, float length)
    {
        var k = new Kit(look, 920);
        k.Use("brick_soot", Palette.Charcoal, 0.9f, 0.1f, tile: 1.2f);
        // The prism faces outward for a counter-clockwise profile; reversed, it faces in.
        var bore = Bore(0);
        Array.Reverse(bore);
        k.Prism(bore, -length, 0, caps: false, smooth: true, lengthwise: true);
        k.Use("ballast", Palette.Ballast, 0.6f, 0, tile: 1.5f);
        k.Box(new Vector3(-TunnelHalf, -0.35f, -length), new Vector3(TunnelHalf, -0.02f, 0), Kit.Faces.PosY);
        // Refuges (the manholes railwaymen stepped into) either side, dark.
        k.Shade(0.25f);
        foreach (int side in new[] { -1, 1 })
            k.Box(new Vector3(side * TunnelHalf - 0.02f, 0, -length / 2 - 0.5f), new Vector3(side * TunnelHalf + 0.02f, 1.9f, -length / 2 + 0.5f), side > 0 ? Kit.Faces.NegX : Kit.Faces.PosX);
        return k.Build($"tunnel-{length:0}");
    }

    /// <summary>
    /// A tunnel portal facing +Z (the way into it): a dressed stone face with the bore's opening, a ring of voussoirs
    /// and a keystone, a coping along the top, wing walls raked back into the hill.
    /// </summary>
    public static MeshAsset Portal(Look? look)
    {
        var k = new Kit(look, 930);
        const float w = 10, top = 11.5f, t = 1.6f;
        var bore = Bore(0.02f);
        k.Use("stone_block", Palette.Charcoal, 0.7f, 0.1f, tile: 2.5f);
        // The face at z = 0: from each point of the opening's outline out to the rectangle's edge.
        Vector3 Out(Vector2 p)
        {
            // Project from the bore's centre out to the rectangle's edge (sides, top).
            var c = new Vector2(0, TunnelSpring * 0.6f);
            var d = p - c;
            float sx = d.X != 0 ? w / MathF.Abs(d.X) : float.MaxValue, sy = d.Y > 0 ? (top - c.Y) / d.Y : float.MaxValue;
            float s = MathF.Min(sx, sy);
            var e = c + d * s;
            if (p.Y < 0)
                e = new Vector2(MathF.Sign(p.X) * w, p.Y);
            return new Vector3(e, 0);
        }
        for (int i = 0; i + 1 < bore.Length; i++)
        {
            var a = new Vector3(bore[i], 0);
            var b = new Vector3(bore[i + 1], 0);
            var oa = Out(bore[i]);
            var ob = Out(bore[i + 1]);
            // Each band of the face, from the opening's edge out to the rectangle's.
            k.Quad(ob, oa, a, b, new(-ob.X, -ob.Y), new(-oa.X, -oa.Y), new(-a.X, -a.Y), new(-b.X, -b.Y));
            // The corners the projection misses (the top corners of the face).
            if (MathF.Abs(oa.X) < w - 0.01f && MathF.Abs(ob.X) >= w - 0.01f || MathF.Abs(ob.X) < w - 0.01f && MathF.Abs(oa.X) >= w - 0.01f)
            {
                var corner = new Vector3(MathF.Sign(oa.X + ob.X) * w, top, 0);
                // Whichever way round it falls, one of the two windings faces out of the portal.
                var facing = Vector3.Cross(oa - corner, ob - corner);
                if (facing.Z < 0)
                    k.Tri(corner, oa, ob, new(-corner.X, -corner.Y), new(-oa.X, -oa.Y), new(-ob.X, -ob.Y));
                else
                    k.Tri(corner, ob, oa, new(-corner.X, -corner.Y), new(-ob.X, -ob.Y), new(-oa.X, -oa.Y));
            }
        }
        // Thickness: the top and ends of the face, and its back is the hill's.
        k.Box(new Vector3(-w, top, -t), new Vector3(w, top + 0.3f, 0.2f), Kit.Faces.All & ~Kit.Faces.NegY);
        k.Box(new Vector3(-w - 0.2f, -0.5f, -t), new Vector3(-w, top, 0), Kit.Faces.NegX | Kit.Faces.PosZ);
        k.Box(new Vector3(w, -0.5f, -t), new Vector3(w + 0.2f, top, 0), Kit.Faces.PosX | Kit.Faces.PosZ);
        // The reveal: the opening's depth through the face, lined like the bore.
        k.Use("brick_soot", Palette.Charcoal, 0.9f, 0.1f, tile: 1.2f);
        var inner = Bore(0.02f);
        Array.Reverse(inner);
        k.Prism(inner, -t, 0.01f, caps: false, smooth: true);
        // Voussoirs: a ring of lighter stone proud of the face, and the keystone.
        k.Use("stone_block", Palette.Charcoal, 0.6f, 0.1f, tile: 1.2f);
        k.Shade(1.25f);
        const int n = 11;
        for (int i = 0; i < n; i++)
        {
            float a0 = MathF.PI * i / n + 0.01f, a1 = MathF.PI * (i + 1) / n - 0.01f;
            float r0 = TunnelHalf + 0.05f, r1 = TunnelHalf + 0.75f;
            var p00 = new Vector3(MathF.Cos(a0) * r0, TunnelSpring + MathF.Sin(a0) * r0, 0.15f);
            var p01 = new Vector3(MathF.Cos(a1) * r0, TunnelSpring + MathF.Sin(a1) * r0, 0.15f);
            var p10 = new Vector3(MathF.Cos(a0) * r1, TunnelSpring + MathF.Sin(a0) * r1, 0.15f);
            var p11 = new Vector3(MathF.Cos(a1) * r1, TunnelSpring + MathF.Sin(a1) * r1, 0.15f);
            k.Quad(p10, p11, p01, p00);
            k.Quad(p11 with { Z = 0 }, p10 with { Z = 0 }, p10, p11);
        }
        k.BoxAt(new Vector3(0, TunnelSpring + TunnelHalf + 0.5f, 0.1f), new Vector3(0.4f, 0.65f, 0.25f));
        // Wing walls, raked back.
        k.Use("stone_block", Palette.Charcoal, 0.7f, 0.1f, tile: 2.5f);
        foreach (int side in new[] { -1, 1 })
            k.With(Matrix4x4.CreateRotationY(side * -0.5f) * Kit.At(side * w, 0, 0), () =>
                k.Box(new Vector3(side < 0 ? -9 : 0, -0.5f, -0.8f), new Vector3(side < 0 ? 0 : 9, 5.5f, 0), Kit.Faces.All & ~Kit.Faces.NegY));
        return k.Build("portal");
    }

    /// <summary>
    /// Ten metres of fortress wall (GDD §9: "lights, then walls, then gun towers"): dressed stone, battered, with a
    /// wall-walk and crenellations, facing the line from <paramref name="side"/>.
    /// </summary>
    public static MeshAsset Wall(Look? look, int side)
    {
        var k = new Kit(look, 940 + side);
        const float h = 8, t = 1.6f, len = 10;
        k.Use("stone_block", Palette.Charcoal, 0.8f, 0.1f, tile: 2.5f);
        k.Box(new Vector3(-t / 2 - 0.3f, -0.5f, -len), new Vector3(t / 2 + 0.3f, 1.2f, 0), Kit.Faces.All & ~Kit.Faces.NegY);
        k.Box(new Vector3(-t / 2, 1.2f, -len), new Vector3(t / 2, h, 0), Kit.Faces.All & ~Kit.Faces.NegY);
        // The merlons on the outer half (away from the line: +x on the right-hand wall), the walk behind them open, so the
        // watch walking it shows over the parapet from inside (B2's towns, note 335).
        float out0 = side > 0 ? 0 : -t / 2, out1 = side > 0 ? t / 2 : 0;
        for (float z = -0.3f; z > -len; z -= 1.25f)
            k.Box(new Vector3(out0, h, z - 0.7f), new Vector3(out1, h + 0.9f, z), Kit.Faces.All & ~Kit.Faces.NegY);
        // Soot and seep stains run down it from the walk: the texture's, darkened low on the side facing the line.
        k.Use("iron_plate", Palette.IronGrey, 0.9f, 0.3f);
        for (float z = -2.5f; z > -len; z -= 5f)
            k.Box(new Vector3(-side * (t / 2) - 0.04f * side - 0.04f, 3.5f, z - 0.35f), new Vector3(-side * (t / 2) - 0.04f * side + 0.04f, 4.2f, z + 0.35f));
        return k.Build($"wall-{side}");
    }

    /// <summary>A gun tower on the wall: square, taller than the wall, crenellated, a lit loophole and a lamp towards the line.</summary>
    public static MeshAsset Tower(Look? look, int side)
    {
        var k = new Kit(look, 950 + side);
        const float half = 2.4f, h = 13;
        k.Use("stone_block", Palette.Charcoal, 0.8f, 0.1f, tile: 2.5f);
        k.Box(new Vector3(-half - 0.3f, -0.5f, -half - 0.3f), new Vector3(half + 0.3f, 1.5f, half + 0.3f), Kit.Faces.All & ~Kit.Faces.NegY);
        k.Box(new Vector3(-half, 1.5f, -half), new Vector3(half, h, half), Kit.Faces.All & ~Kit.Faces.NegY);
        k.Box(new Vector3(-half - 0.35f, h, -half - 0.35f), new Vector3(half + 0.35f, h + 0.4f, half + 0.35f));
        for (int i = 0; i < 4; i++)
            foreach (float a in new[] { -1.6f, 0f, 1.6f })
            {
                var c = i switch { 0 => new Vector3(a, 0, -half - 0.1f), 1 => new Vector3(a, 0, half + 0.1f), 2 => new Vector3(-half - 0.1f, 0, a), _ => new Vector3(half + 0.1f, 0, a) };
                var e = i < 2 ? new Vector3(0.45f, 0.5f, 0.25f) : new Vector3(0.25f, 0.5f, 0.45f);
                k.BoxAt(c + new Vector3(0, h + 0.9f, 0), e);
            }
        // A lit loophole towards the line, and the searchlight's housing over the parapet.
        k.Use("window_lit", Palette.LampAmber, 0.1f, 0.3f, tile: 1);
        k.Panel(new Vector3(-side * (half + 0.01f), h - 3, 0), new Vector3(-side, 0, 0), Vector3.UnitY, 0.35f, 0.9f, Vector2.Zero, Vector2.One);
        k.Use("paint_black", Palette.SootBlack, 0.8f, 0.3f);
        k.BoxAt(new Vector3(-side * (half - 0.4f), h + 1.1f, 0), new Vector3(0.3f, 0.3f, 0.3f));
        return k.Build($"tower-{side}");
    }

    /// <summary>The gatehouse over the line: two drum-less square towers and a deep arch between them, a lamp at the keystone.</summary>
    public static MeshAsset Gatehouse(Look? look)
    {
        var k = new Kit(look, 960);
        k.Use("stone_block", Palette.Charcoal, 0.8f, 0.1f, tile: 2.5f);
        foreach (int side in new[] { -1, 1 })
            k.Box(new Vector3(side < 0 ? -8.5f : 3.5f, -0.5f, -3.5f), new Vector3(side < 0 ? -3.5f : 8.5f, 14, 3.5f), Kit.Faces.All & ~Kit.Faces.NegY);
        k.Box(new Vector3(-3.5f, 8.2f, -3.5f), new Vector3(3.5f, 13, 3.5f));
        for (float x = -8; x < 8.6f; x += 1.3f)
            foreach (float z in new[] { -3.4f, 3.4f })
                k.BoxAt(new Vector3(x, 14.45f, z), new Vector3(0.4f, 0.45f, 0.18f));
        // The iron gates, drawn back against the passage walls.
        k.Use("iron_plate", Palette.IronGrey, 0.9f, 0.35f);
        foreach (int side in new[] { -1, 1 })
            k.Box(new Vector3(side * 3.5f - side * 0.25f - 0.06f, 0, -3.2f), new Vector3(side * 3.5f - side * 0.25f + 0.06f, 7.8f, 0.5f));
        k.Use("rust_heavy", Palette.RustRed, 0.9f, 0.2f);
        k.Box(new Vector3(-3.5f, 7.8f, 3.3f), new Vector3(3.5f, 8.2f, 3.6f));
        return k.Build("gatehouse");
    }

    /// <summary>
    /// A bay of the fortified station's platform (the art sheet's "fortified station"): cobbles at a low kerb, timber posts,
    /// a pitched canopy of slate on rafters, a lantern hanging under each bay. <see cref="Lantern"/> is where its light is.
    /// </summary>
    /// <param name="lantern">False when the scene hangs a sourced lantern on the bracket instead (tools/models).</param>
    public static MeshAsset PlatformBay(Look? look, int variant, bool lantern = true)
    {
        var k = new Kit(look, 970 + variant);
        const float x0 = 3.6f, x1 = 10, len = 8;
        k.Use("cobbles", Palette.Ballast, 0.7f, 0.05f, tile: 2);
        k.Box(new Vector3(x0, -0.3f, -len), new Vector3(x1, 0.25f, 0), Kit.Faces.PosY | Kit.Faces.NegX);
        k.Use("stone_block", Palette.Charcoal, 0.7f, 0.1f, tile: 1.2f);
        k.Box(new Vector3(x0, -0.3f, -len), new Vector3(x0 + 0.35f, 0.3f, 0), Kit.Faces.PosY | Kit.Faces.NegX);
        k.Use("wood_sleeper", Palette.DeepBrown, 0.9f, 0, tile: 1.5f);
        foreach (float x in new[] { 5.2f, 9.2f })
        {
            k.Box(new Vector3(x - 0.13f, 0.25f, -0.13f), new Vector3(x + 0.13f, 4.2f, 0.13f));
            // Knee braces up under the canopy.
            k.Rod(new Vector3(x, 3.4f, 0), new Vector3(x - 0.9f, 4.2f, 0), 0.06f);
            k.Rod(new Vector3(x, 3.4f, 0), new Vector3(x + 0.9f, 4.2f, 0), 0.06f);
        }
        k.Box(new Vector3(4.2f, 4.1f, -len), new Vector3(10.2f, 4.3f, 0));
        for (float z = -0.5f; z > -len; z -= 1)
            k.Rod(new Vector3(4.0f, 4.35f, z), new Vector3(10.4f, 5.3f, z), 0.05f);
        k.Use("roof_slate", Palette.Charcoal, 0.8f, 0.2f, tile: 1.5f);
        k.Quad(new Vector3(3.7f, 4.4f, 0), new Vector3(3.7f, 4.4f, -len), new Vector3(10.6f, 5.4f, -len), new Vector3(10.6f, 5.4f, 0), twoSided: true);
        // The lantern: an iron cage on a bracket, lit glass (or just the bracket, when a sourced lantern hangs there).
        k.Use("rust_heavy", Palette.IronGrey, 0.8f, 0.3f);
        var l = Lantern;
        k.Rod(l + new Vector3(0, 0.25f, 0), l + new Vector3(0, 0.75f, 0), 0.015f);
        if (lantern)
        {
            k.BoxAt(l + new Vector3(0, 0.22f, 0), new Vector3(0.14f, 0.03f, 0.14f));
            k.Use("lamp_lens", Palette.LampAmber, 0, 0, tile: 0.25f);
            k.Emissive = 1;
            k.BoxAt(l, new Vector3(0.1f, 0.18f, 0.1f));
            k.Emissive = 0;
        }
        // Something on the platform: a bench, or crates, or a sack barrow.
        if (variant % 3 == 0)
        {
            k.Use("wood_grey", Palette.DeepBrown, 0.9f, 0, tile: 1);
            k.Box(new Vector3(8.6f, 0.25f, -5.5f), new Vector3(9.1f, 0.7f, -3.5f));
            k.Box(new Vector3(9.0f, 0.7f, -5.5f), new Vector3(9.1f, 1.2f, -3.5f));
        }
        else if (variant % 3 == 1)
        {
            k.Use("wood_crate", Palette.TarnishedBrass * 0.8f, 0.8f, 0, tile: 0.6f);
            k.Box(new Vector3(7.5f, 0.25f, -3), new Vector3(8.1f, 0.85f, -2.4f));
            k.Box(new Vector3(7.6f, 0.85f, -2.95f), new Vector3(8.1f, 1.35f, -2.45f));
            k.Box(new Vector3(8.2f, 0.25f, -3.1f), new Vector3(8.8f, 0.85f, -2.5f));
        }
        return k.Build($"platform-{variant}");
    }

    /// <summary>Where a platform bay's lantern hangs, in the bay's frame.</summary>
    public static readonly Vector3 Lantern = new(6.6f, 3.3f, -4);

    /// <summary>
    /// A facility's buildings (GDD §30: "oversized, dangerous, partially abandoned, barely operable, dimly lit"), beside
    /// the line on <paramref name="side"/> (+1 right), centred along it. Each kind reads by shape: the coaling tower's
    /// bunker on stilts, the elevator's silos, the foundry's sawtooth sheds and stack, sheds and gantries for the rest.
    /// </summary>
    public static MeshAsset Facility(Look? look, FacilityKind? kind, int side)
    {
        var k = new Kit(look, 980 + (int)(kind ?? 0));
        float s = side == 0 ? 1 : side;
        switch (kind)
        {
            case FacilityKind.CoalingTower:
                {
                    // A concrete bunker up on timber stilts, its chute arm reaching over the track.
                    float x = s * 7;
                    k.Use("wood_grey", Palette.DeepBrown, 0.9f, 0, tile: 1.5f);
                    foreach (float dx in new[] { -3.5f, 3.5f })
                        foreach (float dz in new[] { -4.5f, 4.5f })
                            k.Rod(new Vector3(x + dx, -0.3f, dz), new Vector3(x + dx * 0.9f, 12, dz * 0.9f), 0.25f);
                    for (float y = 3; y < 12; y += 3.2f)
                        foreach (float dz in new[] { -4.5f, 4.5f })
                            k.Rod(new Vector3(x - 3.5f, y, dz), new Vector3(x + 3.5f, y + 3, dz), 0.1f);
                    k.Use("concrete_stain", Palette.BlueGrey, 0.9f, 0.1f, tile: 2.5f);
                    k.Box(new Vector3(x - 4, 12, -5.5f), new Vector3(x + 4, 22, 5.5f));
                    k.Use("corrugated_iron", Palette.IronGrey, 0.9f, 0.3f, tile: 1.5f);
                    k.Box(new Vector3(x - 4.3f, 22, -5.8f), new Vector3(x + 4.3f, 22.6f, 5.8f));
                    k.Use("rust_heavy", Palette.IronGrey, 0.9f, 0.3f);
                    // The hopper under the bunker, and the chute arm out over the rails.
                    k.Cylinder(new Vector3(x, 12, 0), new Vector3(x, 9.8f, 0), 2.2f, 8, radiusB: 0.6f);
                    k.Box(new Vector3(MathF.Min(x, s * 0.4f) - 0.4f, 9.2f, -0.45f), new Vector3(MathF.Max(x, s * 0.4f) + 0.4f, 9.7f, 0.45f));
                    k.Cylinder(new Vector3(s * 0.4f, 9.3f, 0), new Vector3(s * 0.4f, 8.8f, 0), 0.4f, 8);
                    break;
                }
            case FacilityKind.GrainElevator:
                {
                    // The modelled elevator where it's built (facility_pieces grain_elevator, note 381): four slip-formed
                    // silos, the bin-floor gallery and the leg house over them, the tallest thing for miles; its spout
                    // swung down to 2.5 m off the track. Without it, the kit's own: three silos and a headhouse.
                    if (k.Look is { } built && PropArt.Of(built).Get("grain_elevator") is not null)
                    {
                        Piece(k, "grain_elevator", s * 15, 0, Facing(s));
                        break;
                    }
                    float x = s * 15;
                    k.Use("concrete_stain", Palette.BlueGrey, 0.9f, 0.1f, tile: 3);
                    for (int i = 0; i < 3; i++)
                        k.Cylinder(new Vector3(x, -0.5f, -12 + i * 12), new Vector3(x, 26, -12 + i * 12), 5, 14);
                    k.Box(new Vector3(x - 4, 26, -18), new Vector3(x + 4, 32, 18));
                    k.Use("corrugated_iron", Palette.IronGrey, 0.9f, 0.3f, tile: 1.5f);
                    k.Box(new Vector3(x - 4.4f, 32, -18.4f), new Vector3(x + 4.4f, 32.5f, 18.4f));
                    k.Use("rust_heavy", Palette.IronGrey, 0.9f, 0.3f);
                    k.Rod(new Vector3(x - s * 4, 28, 0), new Vector3(s * 2.5f, 6, 0), 0.35f);
                    k.Use("window_lit", Palette.LampAmber, 0.1f, 0.3f, tile: 1);
                    k.Panel(new Vector3(x - s * 4.01f, 29, 6), new Vector3(-s, 0, 0), Vector3.UnitY, 0.8f, 1.1f, Vector2.Zero, Vector2.One);
                    break;
                }
            case FacilityKind.Foundry:
                {
                    // Long brick sheds with sawtooth roofs, a tall stack, and the dim glow of a furnace nobody tends. Where the
                    // greybox's block was, 14-30 m out: the yard between it and the spur is the gantry crane's (its far leg
                    // and the castings' stack stand at 7.5-9 m, facilities.json "crane").
                    float x0 = s * 22 - 8, x1 = s * 22 + 8;
                    var (a, b) = (MathF.Min(x0, x1), MathF.Max(x0, x1));
                    k.Use("brick_soot", Palette.RustRed, 0.9f, 0.1f, tile: 1.2f);
                    k.Box(new Vector3(a, -0.5f, -40), new Vector3(b, 12, 40), Kit.Faces.All & ~Kit.Faces.NegY);
                    k.Use("corrugated_iron", Palette.IronGrey, 0.9f, 0.3f, tile: 1.5f);
                    for (float z = -40; z < 40; z += 8)
                        k.Quad(new Vector3(a, 12, z + 8), new Vector3(b, 12, z + 8), new Vector3(b, 16, z), new Vector3(a, 16, z), twoSided: true);
                    k.Use("window_lit", Palette.FurnaceOrange, 0.1f, 0.3f, tile: 1);
                    k.Tint = new Vector3(1.0f, 0.55f, 0.3f);
                    for (float z = -36; z < 38; z += 6)
                        k.Panel(new Vector3(s > 0 ? a - 0.01f : b + 0.01f, 6, z), new Vector3(-s, 0, 0), Vector3.UnitY, 1.4f, 2.4f, Vector2.Zero, Vector2.One);
                    k.Use("brick_soot", Palette.RustRed, 0.9f, 0.1f, tile: 1.2f);
                    k.Cylinder(new Vector3(s * 26, -0.5f, 14), new Vector3(s * 26, 38, 14), 2.2f, 10, radiusB: 1.5f);
                    break;
                }
            case FacilityKind.MineHead:
                {
                    // The headframe over the shaft, its back-stays raking away from the line to the winding house; the
                    // house's chimney; the spoil heap behind (GDD §18: the mine head's winch hauls from it).
                    // Up by half again: the prop is modelled to a small colliery's frame, and next to the winding house it
                    // should be the tallest thing on the site.
                    Piece(k, "headframe", s * 12, 0, Facing(s), 1.5f);
                    WorksHouse(k, s * 25, 0, 10, 14, 8, "brick_soot");
                    k.Use("brick_soot", Palette.RustRed, 0.9f, 0.1f, tile: 1.2f);
                    k.Cylinder(new Vector3(s * 29, -0.5f, -5), new Vector3(s * 29, 26, -5), 1.4f, 10, radiusB: 1.0f);
                    k.Use("slag", Palette.Charcoal, 0.9f, 0, tile: 2);
                    k.Cylinder(new Vector3(s * 34, -1, 26), new Vector3(s * 34, 11, 26), 16, 12, radiusB: 1.5f);
                    break;
                }
            case FacilityKind.ChemicalWorks:
                {
                    // Storage tanks in a row, a pipe rack along the front on its trestles, the works behind with two
                    // tall thin stacks.
                    foreach (float z in new[] { -16f, 0, 16 })
                        Piece(k, "chem_tank", s * 13, z, Facing(s));
                    k.Use("rust_heavy", Palette.IronGrey, 0.8f, 0.4f);
                    for (float z = -24; z <= 24; z += 6)
                    {
                        k.Rod(new Vector3(s * 7, -0.3f, z), new Vector3(s * 7, 4.2f, z), 0.12f);
                        k.Rod(new Vector3(s * 7 - 0.6f, 4.1f, z), new Vector3(s * 7 + 0.6f, 4.1f, z), 0.08f);
                    }
                    foreach (float dx in new[] { -0.4f, 0, 0.4f })
                        k.Cylinder(new Vector3(s * 7 + dx, 4.35f, -25), new Vector3(s * 7 + dx, 4.35f, 25), 0.16f, 8);
                    WorksHouse(k, s * 26, 0, 12, 30, 10, "brick_soot");
                    k.Use("brick_soot", Palette.RustRed, 0.9f, 0.1f, tile: 1.2f);
                    foreach (float z in new[] { -8f, 8 })
                        k.Cylinder(new Vector3(s * 28, 9, z), new Vector3(s * 28, 34, z), 0.8f, 10, radiusB: 0.6f);
                    break;
                }
            case FacilityKind.MilitaryDepot:
                {
                    // A watchtower at the gate end, sandbag walls along the front, Nissen huts behind, a line of
                    // barbed-wire posts between the depot and the line.
                    Piece(k, "watchtower", s * 6, -20, Facing(s));
                    for (float z = -12; z <= 12; z += 3)
                        Piece(k, "sandbags", s * 5, z, MathF.PI / 2);
                    k.Use("corrugated_iron", Palette.IronGrey, 0.9f, 0.3f, tile: 1.5f);
                    var hut = new List<Vector2>();
                    for (int i = 0; i <= 10; i++)
                    {
                        float a = MathF.PI * i / 10;
                        hut.Add(new Vector2(MathF.Cos(a) * 4.5f, MathF.Sin(a) * 4.5f - 0.3f));
                    }
                    foreach (float z in new[] { -18f, 0, 18 })
                        k.With(Kit.At(s * 17, 0, z), () => k.Prism(hut, -7, 7, caps: true, smooth: true));
                    k.Use("wood_grey", Palette.DeepBrown, 0.9f, 0, tile: 1);
                    for (float z = -28; z <= 28; z += 3.5f)
                        k.Rod(new Vector3(s * 3.5f, -0.2f, z), new Vector3(s * 3.5f, 1.6f, z), 0.05f);
                    k.Use("rust_heavy", Palette.IronGrey, 0.8f, 0.4f);
                    foreach (float y in new[] { 0.6f, 1.1f, 1.5f })
                        k.Rod(new Vector3(s * 3.5f, y, -28), new Vector3(s * 3.5f, y, 28), 0.012f);
                    break;
                }
            case FacilityKind.Slaughterhouse:
                {
                    // The killing hall, and in front of it the pens and the ramp the cattle came up out of the cars by. The
                    // modelled hall where it's built (facility_pieces slaughterhouse, note 393): soot-black brick, its
                    // windows small and high, a clerestory, the dressing rail out over the yard, the boiler house and its
                    // chimney behind; without it, the kit's long windowless works box.
                    if (k.Look is { } built && PropArt.Of(built).Get("slaughterhouse") is not null)
                        Piece(k, "slaughterhouse", s * 22, 0, Facing(s));
                    else
                        WorksHouse(k, s * 22, 0, 14, 44, 9, "brick_soot");
                    for (float x = 8; x <= 14; x += 3)
                        for (float z = -18; z <= -3; z += 3)
                            Piece(k, "cattle_pen", s * x, z + 1.5f, MathF.PI / 2);
                    for (float z = -18; z <= -3; z += 3)
                        Piece(k, "cattle_pen", s * 15.5f, z + 1.5f, MathF.PI / 2);
                    foreach (float x in new[] { 9.5f, 12.5f })
                        Piece(k, "cattle_pen", s * x, -18, 0);
                    Piece(k, "cattle_ramp", s * 4.2f, -10, Facing(s));
                    break;
                }
            case FacilityKind.Switchyard:
                {
                    // The signal box that ran the yard, looking out over it, and a water tower with its spout swung
                    // out over the track; a goods shed behind.
                    Piece(k, "signal_box", s * 9, -6, Facing(s));
                    Piece(k, "water_tower", s * 6, 22, Facing(s));
                    WorksHouse(k, s * 24, -4, 10, 30, 7, "wood_grey");
                    break;
                }
            case FacilityKind.WreckYard:
                {
                    // Heaps of what's left of trains: carbodies on their sides and on each other, wheelsets, scrap; the
                    // sheds behind, rusted through.
                    for (int i = 0; i < 6; i++)
                    {
                        float x = s * (10 + (i % 3) * 5.5f), z = -22 + i * 8.5f;
                        float lean = (i % 2 == 0 ? 1 : -1) * (0.3f + 0.25f * (i % 3));
                        float y = i % 3 == 2 ? 2.6f : 0;
                        k.With(Matrix4x4.CreateRotationZ(lean) * Matrix4x4.CreateRotationY(0.25f * (i - 3)) * Kit.At(x, y, z), () => WreckedBody(k, i));
                    }
                    k.Use("wheel_iron", Palette.IronGrey, 0.8f, 0.4f, tile: 0.5f);
                    for (int i = 0; i < 5; i++)
                    {
                        float x = s * (7 + i * 1.2f), z = 18 + (i % 2) * 2;
                        k.Cylinder(new Vector3(x - 0.8f, 0.45f, z), new Vector3(x + 0.8f, 0.45f, z), 0.12f, 8);
                        foreach (float dx in new[] { -0.7f, 0.7f })
                            k.Cylinder(new Vector3(x + dx - 0.07f, 0.45f, z), new Vector3(x + dx + 0.07f, 0.45f, z), 0.45f, 12);
                    }
                    // The sheds set back behind the heaps (the default's would stand on them), rusted through.
                    WorksHouse(k, s * 34, 0, 14, 50, 9, "rust_heavy");
                    break;
                }
            default:
                {
                    // Sheds: long timber buildings on a stone sill, corrugated roofs, doors hanging open.
                    float x0 = s * 20 - 12, x1 = s * 20 + 12;
                    var (a, b) = (MathF.Min(x0, x1), MathF.Max(x0, x1));
                    k.Use("stone_block", Palette.Charcoal, 0.8f, 0.1f, tile: 2.5f);
                    k.Box(new Vector3(a, -0.5f, -30), new Vector3(b, 1, 30), Kit.Faces.All & ~Kit.Faces.NegY);
                    k.Use("wood_grey", Palette.DeepBrown, 0.9f, 0, tile: 1.5f);
                    k.Box(new Vector3(a, 1, -30), new Vector3(b, 9, 30), Kit.Faces.Sides);
                    k.Use("corrugated_iron", Palette.IronGrey, 0.9f, 0.3f, tile: 1.5f);
                    float mid = (a + b) / 2;
                    k.Quad(new Vector3(a - 0.5f, 8.8f, 30.5f), new Vector3(a - 0.5f, 8.8f, -30.5f), new Vector3(mid, 12.5f, -30.5f), new Vector3(mid, 12.5f, 30.5f), twoSided: true);
                    k.Quad(new Vector3(mid, 12.5f, 30.5f), new Vector3(mid, 12.5f, -30.5f), new Vector3(b + 0.5f, 8.8f, -30.5f), new Vector3(b + 0.5f, 8.8f, 30.5f), twoSided: true);
                    k.Use("wood_grey", Palette.DeepBrown, 0.9f, 0, tile: 1.5f);
                    foreach (float z in new[] { -30f, 30f })
                        k.Tri(new Vector3(a, 9, z), new Vector3(b, 9, z), new Vector3(mid, 12.4f, z), new(a, -9), new(b, -9), new(mid, -12.4f));
                    // A black doorway facing the line, and a door leaf hanging off it.
                    k.Shade(0.08f);
                    k.Doorway(new Vector3(s > 0 ? a - 0.01f : b + 0.01f, 1, -8), new Vector3(-s, 0, 0), 5, bay: true);
                    k.Use("wood_grey", Palette.DeepBrown, 0.9f, 0, tile: 1.5f);
                    float leaf = k.DoorHeight(bay: true);
                    k.With(Matrix4x4.CreateRotationY(0.9f * s) * Kit.At(s > 0 ? a : b, 1, -5.5f), () => k.Box(new Vector3(-0.08f, 0, -2.5f), new Vector3(0.08f, leaf, 0)));
                    break;
                }
        }
        return k.Build($"facility-{kind}-{side}");
    }

    /// <summary>The yaw that turns a modelled piece (its front the model's +Z) to face the line from side <paramref name="s"/>.</summary>
    static float Facing(float s) => -s * MathF.PI / 2;

    /// <summary>A modelled piece (tools/models facility_pieces) set among a facility's buildings where it's built.</summary>
    static void Piece(Kit k, string name, float x, float z, float yaw, float scale = 1)
    {
        if (k.Look is { } look && PropArt.Of(look).Get(name) is { } piece)
            // Sunk a little, as the kit's buildings sit on sills down to -0.5: the ground falls away off the formation.
            k.Append(piece, Matrix4x4.CreateScale(scale) * Matrix4x4.CreateRotationY(yaw) * Kit.At(x, -0.3f, z));
    }

    /// <summary>
    /// A stop's shed (level-design P5, P7): a works building centred on the kit's origin, <paramref name="width"/> across
    /// (X), <paramref name="length"/> along (Z), its doorway on the +X side if <paramref name="door"/> is +1, else −X.
    /// </summary>
    public static void Shed(Kit k, float width, float length, float height, string wall, int door) =>
        WorksHouse(k, 0, 0, width, length, height, wall, door);

    /// <summary>A plain works building: walls of <paramref name="wall"/> on a stone sill, a corrugated pitched roof, a
    /// dark doorway facing the line (or the side <paramref name="door"/> says). Centred at (x, z), <paramref name="width"/>
    /// across, <paramref name="length"/> along.</summary>
    static void WorksHouse(Kit k, float x, float z, float width, float length, float height, string wall, int door = 0)
    {
        float a = x - width / 2, b = x + width / 2, z0 = z - length / 2, z1 = z + length / 2, mid = x;
        k.Use("stone_block", Palette.Charcoal, 0.8f, 0.1f, tile: 2.5f);
        k.Box(new Vector3(a, -0.5f, z0), new Vector3(b, 0.8f, z1), Kit.Faces.All & ~Kit.Faces.NegY);
        k.Use(wall, wall == "brick_soot" ? Palette.RustRed : Palette.DeepBrown, 0.9f, 0.1f, tile: wall == "brick_soot" ? 1.2f : 1.5f);
        k.Box(new Vector3(a, 0.8f, z0), new Vector3(b, height, z1), Kit.Faces.Sides);
        // Both gables facing out (the −Z one was wound inwards, so from outside it wasn't there: note 387).
        foreach (var (zz, l, r) in new[] { (z0, b, a), (z1, a, b) })
            k.Tri(new Vector3(l, height, zz), new Vector3(r, height, zz), new Vector3(mid, height + width * 0.3f, zz), new(l, -height), new(r, -height), new(mid, -height - width * 0.3f));
        k.Use("corrugated_iron", Palette.IronGrey, 0.9f, 0.3f, tile: 1.5f);
        k.Quad(new Vector3(a - 0.4f, height - 0.1f, z1 + 0.4f), new Vector3(a - 0.4f, height - 0.1f, z0 - 0.4f), new Vector3(mid, height + width * 0.3f, z0 - 0.4f), new Vector3(mid, height + width * 0.3f, z1 + 0.4f), twoSided: true);
        k.Quad(new Vector3(mid, height + width * 0.3f, z1 + 0.4f), new Vector3(mid, height + width * 0.3f, z0 - 0.4f), new Vector3(b + 0.4f, height - 0.1f, z0 - 0.4f), new Vector3(b + 0.4f, height - 0.1f, z1 + 0.4f), twoSided: true);
        k.Shade(0.08f);
        bool right = door == 0 ? x > 0 : door < 0;
        // The standard doorway (note 110): a big door where the walls stand tall enough over the sill for one and its
        // header, else a person's.
        bool bay = height - 0.8f >= k.DoorHeight(bay: true) + 0.6f;
        k.Doorway(new Vector3(right ? a - 0.01f : b + 0.01f, 0.8f, z), new Vector3(right ? -1 : 1, 0, 0), bay ? 4 : 1.1f, bay);
    }

    /// <summary>A stripped carbody for a wreck yard's heaps: its floor and sides, the roof half gone, rusted through.</summary>
    static void WreckedBody(Kit k, int seed)
    {
        k.Use(seed % 2 == 0 ? "rust_heavy" : "paint_oxide", Palette.RustRed, 0.9f, 0.3f, tile: 1.5f);
        const float w = 1.4f, h = 2.6f, l = 6.5f;
        k.Box(new Vector3(-w, 0, -l), new Vector3(w, 0.2f, l));
        k.Box(new Vector3(-w, 0.2f, -l), new Vector3(-w + 0.08f, h, l), Kit.Faces.All);
        k.Box(new Vector3(w - 0.08f, 0.2f, -l), new Vector3(w, h * (0.6f + 0.1f * (seed % 3)), l), Kit.Faces.All);
        k.Box(new Vector3(-w, 0.2f, l - 0.08f), new Vector3(w, h, l), Kit.Faces.All);
        k.Use("corrugated_iron", Palette.IronGrey, 0.9f, 0.3f, tile: 1.5f);
        k.Quad(new Vector3(-w, h, -l * 0.2f), new Vector3(-w, h, l), new Vector3(w * 0.3f, h + 0.3f, l), new Vector3(w * 0.3f, h + 0.3f, -l * 0.2f), twoSided: true);
    }

    /// <summary>A buffer stop: baulks of timber on an iron frame across the rails, a red lamp on top (lit separately).</summary>
    public static MeshAsset BufferStop(Look? look)
    {
        var k = new Kit(look, 990);
        k.Use("rust_heavy", Palette.RustRed, 0.9f, 0.2f);
        foreach (int side in new[] { -1, 1 })
            k.Rod(new Vector3(side * TrainKit.HalfGauge, 0.15f, 2.2f), new Vector3(side * TrainKit.HalfGauge, 1.0f, 0), 0.07f);
        k.Use("wood_sleeper", Palette.DeepBrown, 0.9f, 0, tile: 1.5f);
        k.Box(new Vector3(-1.4f, 0.7f, -0.3f), new Vector3(1.4f, 1.2f, 0.2f));
        k.Box(new Vector3(-1.3f, 0.15f, -0.25f), new Vector3(1.3f, 0.7f, 0.15f));
        k.Use("paint_black", Palette.SootBlack, 0.8f, 0.3f);
        k.Box(new Vector3(-0.12f, 1.2f, -0.12f), new Vector3(0.12f, 1.55f, 0.12f));
        return k.Build("buffer-stop");
    }

    /// <summary>
    /// A switch stand's throw lever, from its pivot (the origin) along +Z: a flat iron bar with a weighted handle at its end
    /// and the latch at the pivot; turned about X to throw it.
    /// </summary>
    public static MeshAsset SwitchLever(Look? look)
    {
        var k = new Kit(look, 996);
        k.Use("rust_heavy", Palette.IronGrey, 0.8f, 0.4f);
        k.Box(new Vector3(-0.025f, -0.02f, -0.06f), new Vector3(0.025f, 0.02f, 0.75f));
        k.Cylinder(new Vector3(-0.07f, 0, 0), new Vector3(0.07f, 0, 0), 0.05f, 8);
        // The handle's grip and its counterweight ball, painted (a lever you can find in the lamp's light).
        k.Use("paint_oxide", Palette.RustRed, 0.7f, 0.3f);
        k.Cylinder(new Vector3(-0.08f, 0, 0.7f), new Vector3(0.08f, 0, 0.7f), 0.03f, 6);
        k.Lathe(new Vector3(0, 0, 0.82f), [new(0, -0.08f), new(0.07f, -0.05f), new(0.08f, 0), new(0.07f, 0.05f), new(0, 0.08f)], 8);
        return k.Build("switch-lever");
    }

    /// <summary>A switch stand: an iron post, the throw lever's pivot, and the target lamp's housing on top (its glass lit per frame).</summary>
    public static MeshAsset SwitchStand(Look? look)
    {
        var k = new Kit(look, 995);
        k.Use("rust_heavy", Palette.IronGrey, 0.9f, 0.3f);
        k.Box(new Vector3(-0.25f, -0.1f, -0.25f), new Vector3(0.25f, 0.25f, 0.25f));
        k.Cylinder(new Vector3(0, 0.25f, 0), new Vector3(0, 1.6f, 0), 0.06f, 8);
        k.Use("paint_black", Palette.SootBlack, 0.8f, 0.3f);
        k.BoxAt(new Vector3(0, 1.75f, 0), new Vector3(0.19f, 0.19f, 0.19f));
        k.Lathe(new Vector3(0, 1.94f, 0), [new(0.12f, 0), new(0.04f, 0.12f), new(0.02f, 0.2f)], 6, smooth: false);
        return k.Build("switch-stand");
    }
}
