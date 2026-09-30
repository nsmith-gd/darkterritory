using System.Numerics;
using Ballast.Render;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Game.Art;

/// <summary>
/// The Holdouts (GDD App. D.4): a derelict penal transport dumped off its wheels, a wildlander's barricaded shelter, a caged
/// halt lockup; each with its lamp on a mast (the housing here, the glass lit by the scene while it's Occupied or
/// Breaching). In the Line Plan's footprint frame: origin at the ground centre, X across (the door on +X, facing the
/// track), −Z along its long side, Y up to its roof.
/// </summary>
public static class HoldoutKit
{
    /// <param name="size">Half-width, half-length and height (holdouts.json <c>placement.size</c>).</param>
    /// <param name="lampHeight">How high the lamp is over the ground centre (the plan's, as low as the board can see it).</param>
    public static MeshAsset Piece(Look? look, HoldoutType type, Vector3 size, float lampHeight)
    {
        var k = new Kit(look, 1200 + (int)type * 10 + (int)lampHeight);
        switch (type)
        {
            case HoldoutType.PrisonCar:
                PrisonCar(k, size);
                break;
            case HoldoutType.BarricadedShelter:
                Shelter(k, size);
                break;
            default:
                Lockup(k, size);
                break;
        }
        Mast(k, size.Z, lampHeight);
        return k.Build($"holdout-{type}-{lampHeight:0}");
    }

    /// <summary>The penal transport: an iron box car sat on sleepers, slit windows barred, a padlocked sliding door.</summary>
    static void PrisonCar(Kit k, Vector3 s)
    {
        float w = s.X, l = s.Y, h = s.Z, floor = 0.35f;
        k.Use("wood_sleeper", Palette.DeepBrown, 0.9f, 0, tile: 1.5f);
        foreach (float z in new[] { -l * 0.7f, 0, l * 0.7f })
            k.Box(new Vector3(-w - 0.3f, -0.2f, z - 0.15f), new Vector3(w + 0.3f, floor, z + 0.15f));
        k.Use("iron_plate", Palette.IronGrey, 0.95f, 0.25f, tile: 1.5f);
        k.Box(new Vector3(-w, floor, -l), new Vector3(w, h - 0.35f, l), Kit.Faces.All & ~Kit.Faces.PosY);
        // A shallow arched roof along its length.
        var roof = new List<Vector2>();
        for (int i = 0; i <= 6; i++)
        {
            float a = MathF.PI * i / 6;
            roof.Add(new Vector2(-MathF.Cos(a) * (w + 0.08f), h - 0.35f + MathF.Sin(a) * 0.35f));
        }
        roof.Add(new Vector2(w + 0.08f, h - 0.4f));
        roof.Add(new Vector2(-w - 0.08f, h - 0.4f));
        // Counter-clockwise seen from +Z, as the prism wants it.
        roof.Reverse();
        k.Use("rust_heavy", Palette.RustRed, 0.95f, 0.15f, tile: 2);
        k.Prism(roof, -l - 0.08f, l + 0.08f, caps: true, smooth: false, lengthwise: true);
        // Ribs down the sides, and the slit windows high up, barred.
        k.Use("rust_heavy", Palette.IronGrey, 0.9f, 0.2f);
        for (float z = -l + 0.6f; z < l; z += 1.2f)
            foreach (int side in new[] { -1, 1 })
                k.Box(new Vector3(side * w - 0.05f, floor, z - 0.05f), new Vector3(side * w + 0.05f, h - 0.35f, z + 0.05f));
        foreach (int side in new[] { -1, 1 })
            for (float z = -l + 1.2f; z < l - 0.6f; z += 2.4f)
            {
                if (side > 0 && Math.Abs(z) < 1.2f)
                    continue; // the door's there
                k.Use("paint_black", Palette.SootBlack, 0.9f, 0.1f);
                k.Box(new Vector3(side * (w + 0.01f) - 0.02f, h - 1.2f, z - 0.45f), new Vector3(side * (w + 0.01f) + 0.02f, h - 0.85f, z + 0.45f));
                k.Use("rust_heavy", Palette.IronGrey, 0.8f, 0.3f);
                for (float bz = z - 0.35f; bz <= z + 0.36f; bz += 0.175f)
                    k.Rod(new Vector3(side * (w + 0.05f), h - 1.22f, bz), new Vector3(side * (w + 0.05f), h - 0.83f, bz), 0.015f);
            }
        // The door, slid shut on its rail in its frame, oxide paint where the iron isn't, a hasp and padlock across.
        k.Use("rust_heavy", Palette.IronGrey, 0.9f, 0.25f);
        k.Box(new Vector3(w, floor, -1.02f), new Vector3(w + 0.12f, h - 0.45f, -0.9f));
        k.Box(new Vector3(w, floor, 0.9f), new Vector3(w + 0.12f, h - 0.45f, 1.02f));
        k.Use("paint_oxide", Palette.RustRed * 1.3f, 0.85f, 0.15f, tile: 1);
        k.Box(new Vector3(w, floor + 0.05f, -0.9f), new Vector3(w + 0.1f, h - 0.5f, 0.9f));
        k.Use("rust_heavy", Palette.IronGrey, 0.85f, 0.3f);
        foreach (float y in new[] { floor + 0.5f, 1.9f, h - 0.9f })
            k.Box(new Vector3(w + 0.1f, y - 0.04f, -0.88f), new Vector3(w + 0.13f, y + 0.04f, 0.88f));
        k.Use("rust_heavy", Palette.IronGrey, 0.8f, 0.3f);
        k.Box(new Vector3(w + 0.1f, h - 0.5f, -1.8f), new Vector3(w + 0.16f, h - 0.4f, 1.8f));
        Padlock(k, new Vector3(w + 0.14f, 1.4f, 0.8f));
    }

    /// <summary>The wildlander's shelter: a plank shack, a lean-to of corrugated iron, boards nailed across the door.</summary>
    static void Shelter(Kit k, Vector3 s)
    {
        float w = s.X, l = s.Y, h = s.Z;
        k.Use("wood_siding", Palette.DeepBrown, 0.95f, 0, tile: 1.2f);
        k.Box(new Vector3(-w, -0.1f, -l), new Vector3(w, h - 0.5f, l), Kit.Faces.All & ~Kit.Faces.PosY & ~Kit.Faces.NegY);
        // The lean-to roof, high over the door side, weighted with a stone or two.
        k.Use("corrugated_iron", Palette.IronGrey, 0.95f, 0.2f, tile: 1.3f);
        k.Quad(new Vector3(-w - 0.3f, h - 0.6f, l + 0.3f), new Vector3(-w - 0.3f, h - 0.6f, -l - 0.3f), new Vector3(w + 0.4f, h, -l - 0.3f), new Vector3(w + 0.4f, h, l + 0.3f), twoSided: true);
        // The gable triangles under it.
        k.Use("wood_siding", Palette.DeepBrown, 0.95f, 0, tile: 1.2f);
        foreach (float z in new[] { -l, l })
            k.Tri(new Vector3(-w, h - 0.5f, z), new Vector3(w, h - 0.5f, z), new Vector3(w, h - 0.05f, z), Vector2.Zero, new Vector2(1, 0), new Vector2(1, 0.3f));
        k.Use("stone_block", Palette.Charcoal, 0.8f, 0.1f);
        k.BoxAt(new Vector3(-0.3f, h - 0.2f, -0.8f), new Vector3(0.25f, 0.18f, 0.22f));
        k.BoxAt(new Vector3(0.6f, h - 0.05f, 1.1f), new Vector3(0.2f, 0.15f, 0.25f));
        // The door, and the barricade over it: planks nailed across at angles, a baulk wedged against it.
        k.Use("wood_grey", Palette.DeepBrown * 0.8f, 0.9f, 0, tile: 1);
        k.Box(new Vector3(w, 0, -0.55f), new Vector3(w + 0.06f, 2.0f, 0.55f));
        k.Use("wood_sleeper", Palette.DeepBrown, 0.95f, 0, tile: 1.5f);
        foreach (var (y0, y1) in new[] { (0.4f, 0.8f), (1.1f, 0.9f), (1.5f, 1.8f) })
            k.Rod(new Vector3(w + 0.12f, y0, -0.85f), new Vector3(w + 0.12f, y1, 0.85f), 0.07f);
        k.Rod(new Vector3(w + 0.9f, 0, 0.2f), new Vector3(w + 0.14f, 1.4f, 0.1f), 0.09f);
        // Furs hung to dry by the wall.
        k.Use("leather", Palette.MuddyOlive, 0.9f, 0.05f);
        k.Box(new Vector3(w + 0.02f, 1.2f, 1.1f), new Vector3(w + 0.06f, 1.9f, 1.7f));
    }

    /// <summary>The halt's lockup: a brick plinth, iron bars all round under a slate roof, a barred gate chained shut.</summary>
    static void Lockup(Kit k, Vector3 s)
    {
        float w = s.X, l = s.Y, h = s.Z;
        k.Use("brick_soot", Palette.RustRed * 0.7f, 0.9f, 0.05f, tile: 1.5f);
        k.Box(new Vector3(-w - 0.1f, -0.2f, -l - 0.1f), new Vector3(w + 0.1f, 0.45f, l + 0.1f));
        // The back wall's brick; the other three are bars.
        k.Box(new Vector3(-w, 0.45f, -l), new Vector3(-w + 0.25f, h - 0.3f, l));
        k.Use("rust_heavy", Palette.IronGrey, 0.85f, 0.35f);
        for (float z = -l + 0.15f; z < l; z += 0.22f)
        {
            if (Math.Abs(z) > 0.55f)
                k.Rod(new Vector3(w, 0.45f, z), new Vector3(w, h - 0.3f, z), 0.018f);
        }
        foreach (float zs in new[] { -l, l })
            for (float x = -w + 0.4f; x < w; x += 0.22f)
                k.Rod(new Vector3(x, 0.45f, zs), new Vector3(x, h - 0.3f, zs), 0.018f);
        foreach (float y in new[] { 0.5f, 1.4f, h - 0.35f })
        {
            k.Box(new Vector3(w - 0.03f, y - 0.03f, -l), new Vector3(w + 0.03f, y + 0.03f, l));
            foreach (float zs in new[] { -l, l })
                k.Box(new Vector3(-w, y - 0.03f, zs - 0.03f), new Vector3(w, y + 0.03f, zs + 0.03f));
        }
        // The gate: a barred frame, chained and padlocked.
        for (float z = -0.5f; z <= 0.51f; z += 0.2f)
            k.Rod(new Vector3(w + 0.05f, 0.5f, z), new Vector3(w + 0.05f, 2.1f, z), 0.02f);
        k.Box(new Vector3(w + 0.02f, 2.1f, -0.55f), new Vector3(w + 0.08f, 2.2f, 0.55f));
        k.Rod(new Vector3(w + 0.1f, 1.25f, 0.45f), new Vector3(w + 0.1f, 1.05f, 0.62f), 0.02f);
        Padlock(k, new Vector3(w + 0.1f, 1.0f, 0.55f));
        k.Use("roof_slate", Palette.Charcoal, 0.8f, 0.2f, tile: 1.2f);
        k.Box(new Vector3(-w - 0.25f, h - 0.3f, -l - 0.25f), new Vector3(w + 0.35f, h - 0.1f, l + 0.25f));
    }

    static void Padlock(Kit k, Vector3 at)
    {
        k.Use("brass", Palette.TarnishedBrass, 0.8f, 0.6f);
        k.BoxAt(at, new Vector3(0.04f, 0.07f, 0.06f));
        k.Rod(at + new Vector3(0, 0.07f, -0.035f), at + new Vector3(0, 0.12f, 0.035f), 0.012f);
    }

    /// <summary>The lamp's mast from the roof, and its unlit housing: an iron cage whose glass the scene lights (<see cref="LampGlass"/>).</summary>
    static void Mast(Kit k, float roof, float lamp)
    {
        k.Use("rust_heavy", Palette.IronGrey, 0.85f, 0.3f);
        if (lamp > roof + 0.4f)
            k.Cylinder(new Vector3(0, roof - 0.3f, 0), new Vector3(0, lamp + 0.35f, 0), 0.05f, 6);
        k.BoxAt(new Vector3(0, lamp + 0.22f, 0), new Vector3(0.16f, 0.03f, 0.16f));
        k.BoxAt(new Vector3(0, lamp - 0.2f, 0), new Vector3(0.14f, 0.025f, 0.14f));
        foreach (var (x, z) in new[] { (-1, -1), (-1, 1), (1, -1), (1, 1) })
            k.Rod(new Vector3(x * 0.12f, lamp - 0.2f, z * 0.12f), new Vector3(x * 0.12f, lamp + 0.2f, z * 0.12f), 0.012f);
    }

    /// <summary>The lamp's glass, lit (drawn over the housing by the scene while the Holdout is Occupied or Breaching).</summary>
    public static MeshAsset LampGlass(Look? look)
    {
        var k = new Kit(look, 1290);
        k.Use("lamp_lens", Palette.LampAmber, 0, 0, tile: 0.25f);
        k.Emissive = 1;
        k.BoxAt(Vector3.Zero, new Vector3(0.1f, 0.17f, 0.1f));
        return k.Build("holdout-lamp");
    }
}
