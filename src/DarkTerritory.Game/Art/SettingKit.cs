using System.Numerics;
using Ballast.Render;

namespace DarkTerritory.Game.Art;

/// <summary>
/// What stands out in the country the line runs through (GDD §30, "a rail corridor civilization"; linegen plan §13.2's
/// biome scatter): the ruin belt's chimneys, tanks and broken walls, the mining country's headframes, the marsh's reeds.
/// Cooked pieces in their own frame, foot at the origin.
/// </summary>
public static class SettingKit
{
    /// <summary>A mill chimney: a tapering sooted brick stack on a plinth, its top broken off at a slant.</summary>
    public static MeshAsset Chimney(Look? look, int variant)
    {
        var k = new Kit(look, 1600 + variant);
        float height = 18 + variant * 5;
        k.Use("brick_soot", Palette.Charcoal, 0.8f, 0.05f, tile: 2);
        k.Box(new Vector3(-2.2f, -0.5f, -2.2f), new Vector3(2.2f, 3.2f, 2.2f), Kit.Faces.All & ~Kit.Faces.NegY);
        k.Cylinder(new Vector3(0, 3.2f, 0), new Vector3(0, height, 0), 1.6f, 10, caps: false, radiusB: 1.0f);
        // The broken crown: a short ring leaning off true.
        k.Cylinder(new Vector3(0, height, 0), new Vector3(0.25f, height + 1.4f, 0.1f), 1.0f, 10, caps: false, radiusB: 0.9f);
        return k.Build($"chimney-{variant}");
    }

    /// <summary>A storage tank gone to rust: a squat cylinder on a ring of stumpy legs, a ladder up its side.</summary>
    public static MeshAsset Tank(Look? look, int variant)
    {
        var k = new Kit(look, 1700 + variant);
        float r = 4 + variant * 1.5f, h = 5 + variant * 2;
        k.Use("rust_heavy", Palette.RustRed, 0.9f, 0.2f, tile: 2);
        k.Cylinder(new Vector3(0, 1.2f, 0), new Vector3(0, 1.2f + h, 0), r, 14, caps: true);
        k.Use("iron_plate", Palette.IronGrey, 0.8f, 0.2f, tile: 1);
        for (int i = 0; i < 6; i++)
        {
            float a = i * MathF.Tau / 6;
            var foot = new Vector3(MathF.Sin(a) * r * 0.8f, 0, MathF.Cos(a) * r * 0.8f);
            k.Rod(foot, foot + new Vector3(0, 1.3f, 0), 0.18f);
        }
        k.Rod(new Vector3(r + 0.3f, 0, 0), new Vector3(r + 0.3f, 1.2f + h, 0), 0.05f);
        return k.Build($"tank-{variant}");
    }

    /// <summary>A length of a building's wall still standing, window holes and a ragged top.</summary>
    public static MeshAsset RuinWall(Look? look, int variant)
    {
        var k = new Kit(look, 1800 + variant);
        var rng = new Random(88 + variant);
        k.Use(variant % 2 == 0 ? "brick_soot" : "plaster_ruin", Palette.BlueGrey, 0.95f, 0.05f, tile: 2);
        float x = -6;
        while (x < 6)
        {
            float w = 1 + (float)rng.NextDouble() * 1.4f;
            float top = 2 + (float)rng.NextDouble() * (variant % 2 == 0 ? 7 : 4);
            bool window = rng.NextDouble() < 0.35 && top > 3.5f;
            if (window)
            {
                // A window's hole: the wall under and over it.
                k.Box(new Vector3(x, -0.3f, -0.25f), new Vector3(x + w, 1.1f, 0.25f), Kit.Faces.All & ~Kit.Faces.NegY);
                k.Box(new Vector3(x, 2.6f, -0.25f), new Vector3(x + w, top, 0.25f), Kit.Faces.All);
            }
            else
                k.Box(new Vector3(x, -0.3f, -0.25f), new Vector3(x + w, top, 0.25f), Kit.Faces.All & ~Kit.Faces.NegY);
            x += w;
        }
        return k.Build($"ruin-{variant}");
    }

    /// <summary>A pithead's headframe: a lattice A-frame of rusted steel with its winding wheel at the top.</summary>
    public static MeshAsset Headframe(Look? look)
    {
        var k = new Kit(look, 1900);
        k.Use("rust_heavy", Palette.RustRed, 0.9f, 0.2f, tile: 1.5f);
        const float h = 22, w = 5;
        foreach (float x in new[] { -w, w })
            foreach (float z in new[] { -2f, 2f })
                k.Rod(new Vector3(x, 0, z), new Vector3(x * 0.3f, h, z * 0.5f), 0.22f);
        for (float y = 3; y < h; y += 3.5f)
        {
            float s = 1 - y / h * 0.7f;
            k.Rod(new Vector3(-w * s, y, -2 * (1 - y / h * 0.5f)), new Vector3(w * s, y + 3.5f, -2 * (1 - y / h * 0.5f)), 0.08f);
            k.Rod(new Vector3(w * s, y, 2 * (1 - y / h * 0.5f)), new Vector3(-w * s, y + 3.5f, 2 * (1 - y / h * 0.5f)), 0.08f);
        }
        k.Use("iron_plate", Palette.IronGrey, 0.7f, 0.3f, tile: 1);
        k.Cylinder(new Vector3(0, h, -0.3f), new Vector3(0, h, 0.3f), 2.4f, 16, caps: true);
        // A back stay, raking away to the winding house.
        k.Rod(new Vector3(0, h * 0.8f, 0), new Vector3(0, 0, 14), 0.25f);
        return k.Build("headframe");
    }

    /// <summary>A clump of marsh reeds: tall crossed cards of dead grass, taller and paler than the verge's tufts.</summary>
    public static MeshAsset Reeds(Look? look, int variant)
    {
        var k = new Kit(look, 2000 + variant);
        k.Use("grass_card", Palette.MuddyOlive, 0.3f, 0, tile: 1);
        k.Baked = 0;
        k.Tint = new Vector3(1.15f, 1.1f, 0.9f);
        for (int i = 0; i < 3; i++)
        {
            float a = variant * 0.9f + i * MathF.PI / 3;
            k.Panel(new Vector3(0, 0.95f, 0), new Vector3(MathF.Sin(a), 0, MathF.Cos(a)), Vector3.UnitY, 1.4f, 1.9f,
                Vector2.Zero, new Vector2(1, 1), twoSided: true);
        }
        return k.Build($"reeds-{variant}");
    }
}
