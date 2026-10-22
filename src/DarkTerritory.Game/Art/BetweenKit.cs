using System.Numerics;
using Ballast.Render;
using DarkTerritory.Sim.Towns;

namespace DarkTerritory.Game.Art;

/// <summary>
/// What stands between a town's houses (GDD §3.1; ARCHITECTURE §8 note 989; the director's references of 7 Oct: Peggy's
/// Cove's houses on the granite above the fish sheds, Blue Rocks' shingled shacks), each built to its
/// <see cref="TownPiece"/>'s footprint: X along the line (its half-length <see cref="TownPiece.HalfS"/>), Z across it
/// (<see cref="TownPiece.HalfD"/>), its front to −Z (placed to face the way the piece does). The fish shed weathered
/// shingle or grey board, its door shut or ajar, floats and buoys hung on its side; the lobster traps stacked, a coil of
/// rope beside them; the granite a lichened boulder or a low ledge, sunk in the ground (<see cref="Granite"/>, scaled to
/// the piece); the fence picket or post-and-rail, run down a lot line along Z, gone over and a rail down where it's broken.
/// </summary>
public static class BetweenKit
{
    /// <summary>The name a piece's mesh is kept by: one a look (a rock is one of six shapes, scaled where it's drawn).</summary>
    public static string Key(TownPiece p) => p.Kind switch
    {
        BetweenKind.Granite => $"between-granite-{p.Variant % 6}",
        BetweenKind.Fence => $"between-fence-{p.Variant}-{p.HalfD * 2:0.0}",
        BetweenKind.FishShed => $"between-shed-{p.Variant}-{(p.Open ? 1 : 0)}{(p.Hung ? 1 : 0)}",
        _ => $"between-traps-{p.Variant}",
    };

    /// <summary>Where a rock's unit lump is drawn to its piece's size: its reach along X and Z, and its height, at size 1.</summary>
    const float RockX = 1.05f, RockZ = 0.9f, RockY = 0.85f;

    /// <summary>The scale that puts a granite piece's unit lump (<see cref="Granite"/>) on its footprint and height.</summary>
    public static Matrix4x4 Scale(TownPiece p) => p.Kind == BetweenKind.Granite
        ? Matrix4x4.CreateScale((float)p.HalfS / RockX, (float)p.Height / RockY, (float)p.HalfD / RockZ)
        : Matrix4x4.Identity;

    public static MeshAsset Piece(Look? look, TownPiece p) => p.Kind switch
    {
        BetweenKind.Granite => Granite(look, p.Variant % 6),
        BetweenKind.Fence => Fence(look, p.Variant, (float)p.HalfD * 2, (float)p.Height),
        BetweenKind.FishShed => Shed(look, (float)p.HalfS, (float)p.HalfD, (float)p.Height, p.Variant, p.Open, p.Hung),
        _ => Traps(look, p.Variant),
    };

    /// <summary>A lump of granite, lichened, sunk in the ground (WorldKit's boulder in the shore's granite).</summary>
    public static MeshAsset Granite(Look? look, int variant) => WorldKit.Rock(look, variant, 1, "granite_lichen");

    /// <summary>
    /// A fish shed: grey boards or cedar shingle gone silver, a gable roof along it, its door in the front, shut or ajar on
    /// the dark; in some, floats and buoys hung from pegs down its side, red, white and faded yellow, on short lines.
    /// </summary>
    public static MeshAsset Shed(Look? look, float hx, float hz, float ht, int variant, bool open, bool hung)
    {
        var k = new Kit(look, 9700 + variant);
        float wall = ht - 0.95f, eave = 0.25f;
        k.Use(variant == 1 ? "wood_grey" : "shingle_cedar", Palette.BlueGrey, 0.95f, 0.03f, tile: 1);
        // Weathered to silver: the cedar's red gone grey.
        if (variant != 1)
            k.Tint = new Vector3(0.62f, 0.6f, 0.58f);
        k.Box(new Vector3(-hx, 0, -hz), new Vector3(hx, wall, hz), Kit.Faces.Sides);
        // The gable ends, seen from either side (through the door left ajar, from inside).
        foreach (float x in new[] { -hx, hx })
        {
            k.Tri(new Vector3(x, wall, -hz), new Vector3(x, ht, 0), new Vector3(x, wall, hz), new(0, 0), new(0.5f, 1), new(1, 0));
            k.Tri(new Vector3(x, wall, hz), new Vector3(x, ht, 0), new Vector3(x, wall, -hz), new(1, 0), new(0.5f, 1), new(0, 0));
        }
        // The roof: cedar shingles gone near black, or (2) a sheet of rusted iron.
        k.Use(variant == 2 ? "corrugated_iron" : "shingle_cedar", Palette.SootBlack, 0.9f, 0.05f, tile: 1);
        k.Tint = variant == 2 ? new Vector3(0.55f, 0.42f, 0.36f) : new Vector3(0.3f, 0.29f, 0.28f);
        k.Quad(new Vector3(-hx - eave, ht, 0), new Vector3(hx + eave, ht, 0), new Vector3(hx + eave, wall - 0.15f, -hz - eave), new Vector3(-hx - eave, wall - 0.15f, -hz - eave), twoSided: true);
        k.Quad(new Vector3(-hx - eave, wall - 0.15f, hz + eave), new Vector3(hx + eave, wall - 0.15f, hz + eave), new Vector3(hx + eave, ht, 0), new Vector3(-hx - eave, ht, 0), twoSided: true);
        // The door, off to one side of the front: shut, or ajar on the dark inside.
        float dx = -hx * 0.35f, dw = 0.85f, dh = MathF.Min(1.9f, wall - 0.1f);
        k.Use("glass_dirty", Palette.SootBlack, 0.4f, 0.4f, tile: 1);
        k.Shade(0.15f);
        k.Panel(new Vector3(dx, dh / 2, -hz - 0.01f), -Vector3.UnitZ, Vector3.UnitY, dw, dh);
        k.Use("wood_grey", Palette.DeepBrown, 0.95f, 0.03f, tile: 1);
        k.Tint = new Vector3(0.75f, 0.7f, 0.62f);
        if (open)
        {
            // Hung on its left post, swung out about a third.
            var hinge = new Vector3(dx - dw / 2, 0, -hz - 0.02f);
            var leaf = new Vector3(MathF.Cos(-0.55f), 0, MathF.Sin(-0.55f)) * dw;
            k.Quad(hinge + new Vector3(0, dh, 0), hinge + leaf + new Vector3(0, dh, 0), hinge + leaf, hinge, twoSided: true);
        }
        else
            k.Box(new Vector3(dx - dw / 2, 0, -hz - 0.05f), new Vector3(dx + dw / 2, dh, -hz - 0.01f), Kit.Faces.NegZ | Kit.Faces.PosX | Kit.Faces.NegX | Kit.Faces.PosY);
        // A small window in the front's other half, salt-blind.
        k.Use("glass_dirty", Palette.SootBlack, 0.4f, 0.4f, tile: 1);
        k.Panel(new Vector3(hx * 0.45f, wall * 0.62f, -hz - 0.01f), -Vector3.UnitZ, Vector3.UnitY, 0.55f, 0.45f);
        if (!hung)
            return k.Build($"between-shed-{variant}");
        // Floats and buoys down its +X side on short lines from a batten: a line of them, spaced unevenly.
        k.Use("wood_grey", Palette.DeepBrown, 0.9f, 0.02f, tile: 1);
        float bx = hx + 0.06f, top = wall - 0.35f;
        k.Box(new Vector3(hx, top - 0.05f, -hz + 0.2f), new Vector3(bx, top + 0.05f, hz - 0.2f), Kit.Faces.PosX | Kit.Faces.PosY | Kit.Faces.NegY);
        Vector3[] paints = [new(0.62f, 0.12f, 0.08f), new(0.85f, 0.83f, 0.78f), new(0.72f, 0.58f, 0.2f), new(0.2f, 0.3f, 0.36f)];
        int i = 0;
        for (float z = -hz + 0.35f; z < hz - 0.25f; z += 0.42f + (i % 3) * 0.13f, i++)
        {
            float drop = 0.25f + (i * 37 % 5) * 0.08f, r = i % 2 == 0 ? 0.13f : 0.1f, len = i % 2 == 0 ? 0.32f : 0.42f;
            k.Use("wool", Palette.DeepBrown, 0.9f, 0, tile: 1);
            k.Rod(new Vector3(bx + 0.02f, top, z), new Vector3(bx + 0.02f + r, top - drop, z), 0.006f);
            k.Use("paint_black", Palette.RustRed, 0.85f, 0.25f, tile: 1);
            k.Tint = paints[(i + variant) % paints.Length] * 2.2f;
            var c = new Vector3(bx + 0.02f + r, top - drop, z);
            k.Cylinder(c, c - new Vector3(0, len, 0), r, 8, radiusB: r * 0.75f);
        }
        return k.Build($"between-shed-{variant}-hung");
    }

    /// <summary>
    /// Lobster traps stacked (<see cref="TownPiece.Variant"/>: columns − 1 by six, layers − 1 by two, a coil of rope beside):
    /// wooden lath frames a metre long and a half high over dark netting; the coil at the +X end with a buoy on it.
    /// </summary>
    public static MeshAsset Traps(Look? look, int variant)
    {
        int columns = variant / 6 + 1, layers = variant % 6 / 2 + 1;
        bool coil = variant % 2 == 1;
        var k = new Kit(look, 9800 + variant);
        float hx = columns * 0.5f + (coil ? 0.35f : 0), x0 = -hx, hz = 0.33f;
        for (int c = 0; c < columns; c++)
            for (int l = 0; l < layers; l++)
            {
                // Each a little off the one under it, as they're thrown up: the netting inside dark, laths over it, grey.
                float jx = ((c * 7 + l * 3 + variant) % 5 - 2) * 0.025f, jz = ((c * 3 + l * 5) % 3 - 1) * 0.03f;
                float a = x0 + c * 1.0f + 0.03f + jx, b = a + 0.94f, y = l * 0.5f, z0 = -hz + 0.02f + jz, z1 = hz - 0.02f + jz;
                k.Use("glass_dirty", Palette.SootBlack, 0.6f, 0.1f, tile: 1);
                k.Shade(0.3f);
                k.Box(new Vector3(a + 0.02f, y, z0 + 0.02f), new Vector3(b - 0.02f, y + 0.45f, z1 - 0.02f), Kit.Faces.All & ~Kit.Faces.NegY);
                k.Use("wood_grey", Palette.BlueGrey, 0.95f, 0.03f, tile: 1);
                k.Tint = new Vector3(0.78f, 0.74f, 0.66f);
                // The frame's corner posts, and laths along its sides and over its top.
                foreach (float x in new[] { a, b - 0.04f })
                    foreach (float z in new[] { z0, z1 - 0.04f })
                        k.Box(new Vector3(x, y, z), new Vector3(x + 0.04f, y + 0.47f, z + 0.04f), Kit.Faces.Sides);
                foreach (float ly in new[] { 0.06f, 0.2f, 0.34f })
                    foreach (float z in new[] { z0, z1 - 0.02f })
                        k.Box(new Vector3(a, y + ly, z), new Vector3(b, y + ly + 0.045f, z + 0.02f), Kit.Faces.PosZ | Kit.Faces.NegZ | Kit.Faces.PosY);
                for (float z = z0 + 0.08f; z < z1 - 0.05f; z += 0.16f)
                    k.Box(new Vector3(a, y + 0.45f, z), new Vector3(b, y + 0.47f, z + 0.045f), Kit.Faces.PosY | Kit.Faces.PosZ | Kit.Faces.NegZ);
            }
        if (coil)
        {
            // A coil of manila on the ground at the +X end, and a buoy leant on the stack.
            float cx = hx - 0.3f;
            k.Use("wool", Palette.DeepBrown, 0.9f, 0, tile: 0.5f);
            k.Tint = new Vector3(0.62f, 0.5f, 0.32f);
            k.Cylinder(new Vector3(cx, 0, 0), new Vector3(cx, 0.14f, 0), 0.28f, 10);
            k.Use("paint_black", Palette.RustRed, 0.85f, 0.25f, tile: 1);
            k.Tint = new Vector3(0.62f, 0.12f, 0.08f) * 2.2f;
            k.Cylinder(new Vector3(cx - 0.05f, 0.14f, 0.1f), new Vector3(cx - 0.18f, 0.55f, 0.12f), 0.11f, 8, radiusB: 0.08f);
        }
        return k.Build($"between-traps-{variant}");
    }

    /// <summary>
    /// A fence run of <paramref name="length"/> m along Z: picket (0, grey, gapped), post-and-rail (1), or post-and-rail gone
    /// over (2): leaning, its middle rail down at one end.
    /// </summary>
    public static MeshAsset Fence(Look? look, int variant, float length, float ht)
    {
        var k = new Kit(look, 9900 + variant);
        float z0 = -length / 2, z1 = length / 2;
        k.Use("wood_grey", Palette.BlueGrey, 0.95f, 0.03f, tile: 1);
        if (variant == 0)
        {
            k.Tint = new Vector3(0.72f, 0.72f, 0.68f);
            foreach (float y in new[] { 0.25f, ht - 0.3f })
                k.Box(new Vector3(0.015f, y, z0), new Vector3(0.045f, y + 0.07f, z1), Kit.Faces.PosX | Kit.Faces.NegX | Kit.Faces.PosY);
            for (float z = z0; z <= z1 + 0.01f; z += 2.4f)
                k.Box(new Vector3(-0.03f, 0, z - 0.05f), new Vector3(0.05f, ht + 0.1f, z + 0.05f), Kit.Faces.Sides | Kit.Faces.PosY);
            int n = 0;
            for (float z = z0 + 0.1f; z < z1 - 0.06f; z += 0.14f, n++)
            {
                // Some gone, some split short.
                if (n * 7 % 11 == 3)
                    continue;
                float top = n * 5 % 9 == 4 ? ht - 0.3f : ht;
                k.Box(new Vector3(-0.012f, 0, z - 0.035f), new Vector3(0.012f, top, z + 0.035f), Kit.Faces.PosX | Kit.Faces.NegX | Kit.Faces.PosY);
            }
            return k.Build($"between-fence-0-{length:0.0}");
        }
        // Post-and-rail: posts every 2.4 m and three rails; gone over, it leans out to +X, a rail fallen.
        bool broken = variant == 2;
        float lean = broken ? 0.18f : 0;
        Vector3 Up(float y, float z) => new(lean * y / ht, y, z);
        int posts = Math.Max(2, (int)MathF.Round(length / 2.4f) + 1);
        k.Tint = new Vector3(0.7f, 0.66f, 0.6f);
        for (int i = 0; i < posts; i++)
        {
            float z = z0 + (z1 - z0) * i / (posts - 1);
            k.Rod(Up(0, z), Up(ht + 0.08f, z), 0.06f);
        }
        float[] rails = [0.3f, ht * 0.62f, ht - 0.05f];
        for (int r = 0; r < rails.Length; r++)
        {
            if (broken && r == 1)
            {
                // Its middle rail come off the near post and down in the grass.
                k.Rod(Up(rails[r], z1 - 2.3f) + new Vector3(0.05f, 0, 0), new Vector3(0.12f, 0.05f, z1 - 0.2f), 0.035f);
                k.Rod(Up(rails[r], z0) + new Vector3(0.05f, 0, 0), Up(rails[r], z1 - 2.3f) + new Vector3(0.05f, 0, 0), 0.035f);
                continue;
            }
            k.Rod(Up(rails[r], z0) + new Vector3(0.05f, 0, 0), Up(rails[r], z1) + new Vector3(0.05f, 0, 0), 0.035f);
        }
        return k.Build($"between-fence-{variant}-{length:0.0}");
    }
}
