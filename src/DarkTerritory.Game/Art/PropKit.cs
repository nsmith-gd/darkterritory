using System.Numerics;
using Ballast.Render;

namespace DarkTerritory.Game.Art;

/// <summary>
/// The things the crew handle (pipeline "small props": crate, lantern, toolbox): chunky, readable at a glance and in
/// a hand, each the size of its body in the sim (Sim.Physics) so what you pick up is what you see. Centred on the body.
/// </summary>
public static class PropKit
{
    /// <summary>The train's own stores: a nailed crate with corner battens, 0.68 m.</summary>
    public static MeshAsset Crate(Look? look)
    {
        var k = new Kit(look, 1100);
        const float h = 0.34f;
        k.Use("wood_crate", Palette.TarnishedBrass * 0.8f, 0.7f, 0, tile: 0.68f);
        k.Box(new Vector3(-h), new Vector3(h));
        k.Use("wood_sleeper", Palette.DeepBrown, 0.8f, 0, tile: 0.7f);
        foreach (float y in new[] { -h, h - 0.07f })
            k.Box(new Vector3(-h - 0.01f, y, -h - 0.01f), new Vector3(h + 0.01f, y + 0.07f, h + 0.01f), Kit.Faces.Sides);
        return k.Build("crate");
    }

    /// <summary>Freight from a facility: a bigger case, stencilled, strapped with iron both ways.</summary>
    public static MeshAsset Cargo(Look? look)
    {
        var k = new Kit(look, 1110);
        const float h = 0.44f;
        k.Use("wood_crate", Palette.BlueGrey, 0.8f, 0, tile: 0.88f);
        k.Box(new Vector3(-h), new Vector3(h));
        k.Use("rust_heavy", Palette.SootBlack, 0.8f, 0.3f);
        k.Box(new Vector3(-h - 0.01f, -h - 0.01f, -0.05f), new Vector3(h + 0.01f, h + 0.01f, 0.05f), Kit.Faces.All & ~(Kit.Faces.PosZ | Kit.Faces.NegZ));
        k.Box(new Vector3(-0.05f, -h - 0.01f, -h - 0.01f), new Vector3(0.05f, h + 0.01f, h + 0.01f), Kit.Faces.All & ~(Kit.Faces.PosX | Kit.Faces.NegX));
        return k.Build("cargo");
    }

    /// <summary>Two-man freight (T43): a long iron-banded case with a rope grip at each end.</summary>
    public static MeshAsset Heavy(Look? look, float half)
    {
        var k = new Kit(look, 1120);
        var e = new Vector3(half * 1.3f, half * 0.8f, half * 0.8f);
        k.Use("paint_oxide", Palette.RustRed, 0.9f, 0.1f, tile: 1);
        k.Box(-e, e);
        k.Use("rust_heavy", Palette.IronGrey, 0.9f, 0.3f);
        foreach (float x in new[] { -half * 1.1f, 0, half * 1.1f })
            k.Box(new Vector3(x - 0.05f, -e.Y - 0.01f, -e.Z - 0.01f), new Vector3(x + 0.05f, e.Y + 0.01f, e.Z + 0.01f), Kit.Faces.All & ~(Kit.Faces.PosX | Kit.Faces.NegX));
        k.Use("leather", Palette.DeepBrown, 0.8f, 0, tile: 0.5f);
        foreach (int end in new[] { -1, 1 })
            k.Rod(new Vector3(end * (e.X + 0.06f), half * 0.3f, -0.18f), new Vector3(end * (e.X + 0.06f), half * 0.3f, 0.18f), 0.03f, 6);
        return k.Build($"heavy-{half:0.00}");
    }

    /// <summary>A hand lantern: a tin base, glass all round with the flame's glow in it, wire guards, a hoop to carry it by.</summary>
    public static MeshAsset Lantern(Look? look)
    {
        var k = new Kit(look, 1130);
        k.Use("rust_heavy", Palette.IronGrey, 0.7f, 0.4f);
        k.Lathe(new Vector3(0, -0.14f, 0), [new(0.09f, 0), new(0.1f, 0.03f), new(0.09f, 0.06f)], 8, smooth: false);
        k.Lathe(new Vector3(0, 0.08f, 0), [new(0.09f, 0), new(0.07f, 0.04f), new(0.03f, 0.08f), new(0.02f, 0.1f)], 8, smooth: false);
        k.Use("lamp_lens", Palette.LampAmber, 0, 0, tile: 0.25f);
        k.Emissive = 1;
        k.Cylinder(new Vector3(0, -0.08f, 0), new Vector3(0, 0.08f, 0), 0.07f, 8, caps: false);
        k.Emissive = 0;
        k.Use("rust_heavy", Palette.SootBlack, 0.7f, 0.4f);
        for (int i = 0; i < 4; i++)
        {
            float a = i * MathF.PI / 2 + 0.4f;
            k.Rod(new Vector3(MathF.Cos(a) * 0.085f, -0.08f, MathF.Sin(a) * 0.085f), new Vector3(MathF.Cos(a) * 0.085f, 0.08f, MathF.Sin(a) * 0.085f), 0.008f);
        }
        k.Rod(new Vector3(-0.07f, 0.18f, 0), new Vector3(0, 0.26f, 0), 0.008f);
        k.Rod(new Vector3(0, 0.26f, 0), new Vector3(0.07f, 0.18f, 0), 0.008f);
        return k.Build("lantern");
    }

    /// <summary>A walkie-talkie (T41): an iron brick with its aerial up and a pinprick of a lamp, so a dropped one's found.</summary>
    public static MeshAsset Radio(Look? look)
    {
        var k = new Kit(look, 1140);
        k.Use("paint_olive", Palette.IronGrey, 0.8f, 0.3f, tile: 0.3f);
        k.Box(new Vector3(-0.07f, -0.12f, -0.04f), new Vector3(0.07f, 0.12f, 0.04f));
        k.Use("paint_black", Palette.SootBlack, 0.6f, 0.3f, tile: 0.3f);
        k.Box(new Vector3(-0.055f, -0.02f, -0.045f), new Vector3(0.055f, 0.09f, -0.04f), Kit.Faces.NegZ);
        k.Rod(new Vector3(0.04f, 0.12f, 0), new Vector3(0.04f, 0.32f, 0), 0.007f);
        k.Use("lamp_lens", Palette.SignalRed, 0, 0, tile: 0.1f);
        k.Emissive = 1;
        k.Tint = new Vector3(1, 0.2f, 0.1f);
        k.Panel(new Vector3(-0.04f, 0.1f, -0.0405f), -Vector3.UnitZ, Vector3.UnitY, 0.02f, 0.02f);
        k.Emissive = 0;
        return k.Build("radio");
    }
}
