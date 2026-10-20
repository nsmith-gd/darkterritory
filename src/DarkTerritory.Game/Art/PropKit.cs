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

    /// <summary>
    /// A village find (level-design P12): a small bundle tied up in sacking, the size of its body (loot.json radius),
    /// with a strap to carry it by; what's in it the HUD says.
    /// </summary>
    /// <summary>
    /// A beer from the BBQ deck's cooler (note 698): a can, red with a white band, its top bright. Larger than life, a little,
    /// to read in a hand at night.
    /// </summary>
    public static MeshAsset Beer(Look? look)
    {
        var k = new Kit(look, 1688);
        k.Use("bbq_plastic", new Vector3(0.75f, 0.1f, 0.08f), 0.2f, 0.5f);
        k.Cylinder(new Vector3(0, -0.08f, 0), new Vector3(0, 0.08f, 0), 0.042f, 10);
        k.Use("bbq_plastic", new Vector3(0.92f, 0.9f, 0.86f), 0.2f, 0.3f);
        k.Cylinder(new Vector3(0, -0.015f, 0), new Vector3(0, 0.02f, 0), 0.0425f, 10, caps: false);
        k.Use("rust_heavy", new Vector3(0.75f, 0.75f, 0.78f), 0.3f, 0.9f);
        k.Cylinder(new Vector3(0, 0.08f, 0), new Vector3(0, 0.09f, 0), 0.036f, 10);
        return k.Build("prop-beer");
    }

    /// <summary>
    /// The flare pistol (note 871), greybox: a stubby wide-bored signal pistol, its barrel along −Z, painted the stores' orange
    /// so it's told from anything else lying in the guard van. Its middle at (0, 0, 0).
    /// </summary>
    public static MeshAsset FlarePistol(Look? look)
    {
        var k = new Kit(look, 1871);
        k.Use("dress_paint", new Vector3(0.9f, 0.38f, 0.08f), 0.4f, 0.3f);
        k.Cylinder(new Vector3(0, 0.02f, 0.04f), new Vector3(0, 0.02f, -0.14f), 0.028f, 10);
        k.Use("rust_heavy", new Vector3(0.2f, 0.18f, 0.16f), 0.5f, 0.6f);
        k.BoxAt(new Vector3(0, -0.04f, 0.06f), new Vector3(0.016f, 0.05f, 0.022f));
        k.Cylinder(new Vector3(0, 0.02f, -0.14f), new Vector3(0, 0.02f, -0.15f), 0.031f, 10);
        return k.Build("prop-flare-pistol");
    }

    /// <summary>A flare fired from the pistol (note 871), greybox: a short red cartridge, its burning head at +Y. Its middle at (0, 0, 0).</summary>
    public static MeshAsset Flare(Look? look)
    {
        var k = new Kit(look, 1872);
        k.Use("dress_paint", new Vector3(0.75f, 0.08f, 0.06f), 0.4f, 0.2f);
        k.Cylinder(new Vector3(0, -0.06f, 0), new Vector3(0, 0.05f, 0), 0.022f, 8);
        k.Use("rust_heavy", new Vector3(0.25f, 0.22f, 0.2f), 0.5f, 0.6f);
        k.Cylinder(new Vector3(0, 0.05f, 0), new Vector3(0, 0.06f, 0), 0.02f, 8);
        return k.Build("prop-flare");
    }

    /// <summary>A sausage off the BBQ deck's grill (note 698), in its bun.</summary>
    public static MeshAsset Sausage(Look? look)
    {
        var k = new Kit(look, 1689);
        k.Use("sac", new Vector3(0.78f, 0.58f, 0.32f), 0.9f, 0, tile: 1);
        k.Box(new Vector3(-0.09f, -0.03f, -0.035f), new Vector3(0.09f, 0.015f, -0.005f));
        k.Box(new Vector3(-0.09f, -0.03f, 0.005f), new Vector3(0.09f, 0.015f, 0.035f));
        k.Use("leather", new Vector3(0.45f, 0.18f, 0.1f), 0.6f, 0, tile: 1);
        k.Cylinder(new Vector3(-0.11f, 0.0f, 0), new Vector3(0.11f, 0.0f, 0), 0.022f, 8);
        return k.Build("prop-sausage");
    }

    /// <summary>
    /// The band's instruments (note 700), by band.json id, lying along X about the body's middle: the fiddle with its bow, the
    /// tin whistle, the button accordion, the tenor banjo, the guitar, the bodhrán with its tipper, the spoons, and the ugly
    /// stick (a mop handle, a rubber boot, a tin can and bottle caps). Flat colours (no texture by those names), so the
    /// varnish and the lacquer read at night.
    /// </summary>
    public static MeshAsset Instrument(Look? look, string id)
    {
        var k = new Kit(look, 1700);
        var varnish = new Vector3(0.55f, 0.25f, 0.08f);
        var ebony = new Vector3(0.06f, 0.05f, 0.05f);
        var skin = new Vector3(0.88f, 0.8f, 0.64f);
        var nickel = new Vector3(0.78f, 0.78f, 0.8f);
        // A waisted body (two bouts and the waist between), lying flat, its front up.
        void Body(float lower, float upper, float at, float depth, Vector3 colour)
        {
            k.Use("band_varnish", colour, 0.3f, 0.4f);
            k.Cylinder(new Vector3(at + lower * 0.85f, -depth, 0), new Vector3(at + lower * 0.85f, depth, 0), lower, 14);
            k.Cylinder(new Vector3(at - upper * 0.95f, -depth, 0), new Vector3(at - upper * 0.95f, depth, 0), upper, 14);
            k.Box(new Vector3(at - upper * 0.95f, -depth, -upper * 0.7f), new Vector3(at + lower * 0.85f, depth, upper * 0.7f));
        }
        switch (id)
        {
            case "fiddle":
                Body(0.1f, 0.085f, 0.05f, 0.03f, varnish);
                k.Use("band_ebony", ebony, 0.4f, 0.3f);
                k.Box(new Vector3(-0.32f, 0.03f, -0.02f), new Vector3(0.02f, 0.045f, 0.02f));
                k.Box(new Vector3(-0.36f, -0.01f, -0.015f), new Vector3(-0.2f, 0.03f, 0.015f));
                k.Use("band_varnish", varnish * 0.8f, 0.3f, 0.4f);
                k.Cylinder(new Vector3(-0.38f, -0.015f, 0), new Vector3(-0.38f, 0.035f, 0), 0.022f, 8);
                k.Use("band_bridge", skin, 0.6f, 0);
                k.Box(new Vector3(0.09f, 0.03f, -0.03f), new Vector3(0.1f, 0.06f, 0.03f));
                // The bow, alongside: its stick and its hair.
                k.Use("band_ebony", ebony * 2, 0.4f, 0.3f);
                k.Rod(new Vector3(-0.3f, 0.02f, 0.13f), new Vector3(0.4f, 0.02f, 0.13f), 0.005f);
                k.Use("band_hair", new Vector3(0.9f, 0.88f, 0.82f), 0.8f, 0);
                k.Rod(new Vector3(-0.29f, 0.0f, 0.13f), new Vector3(0.37f, 0.0f, 0.13f), 0.003f);
                break;
            case "whistle":
                k.Use("band_tin", nickel, 0.4f, 0.8f);
                k.Cylinder(new Vector3(-0.12f, 0, 0), new Vector3(0.15f, 0, 0), 0.009f, 8, radiusB: 0.007f);
                k.Use("band_mouthpiece", new Vector3(0.1f, 0.25f, 0.12f), 0.4f, 0.2f);
                k.Cylinder(new Vector3(-0.2f, 0, 0), new Vector3(-0.12f, 0, 0), 0.011f, 8);
                k.Use("band_ebony", ebony, 0.4f, 0.3f);
                for (int i = 0; i < 6; i++)
                    k.Disc(new Vector3(-0.06f + 0.03f * i, 0.009f, 0), Vector3.UnitY, 0.003f, 6);
                break;
            case "accordion":
                k.Use("band_lacquer", new Vector3(0.62f, 0.06f, 0.06f), 0.25f, 0.5f);
                k.Box(new Vector3(-0.15f, -0.11f, -0.09f), new Vector3(-0.09f, 0.11f, 0.09f));
                k.Box(new Vector3(0.09f, -0.11f, -0.09f), new Vector3(0.15f, 0.11f, 0.09f));
                // The bellows' pleats, dark and light.
                for (int i = 0; i < 9; i++)
                {
                    k.Use("band_bellows", i % 2 == 0 ? ebony * 2 : new Vector3(0.85f, 0.82f, 0.75f), 0.7f, 0);
                    float x = -0.09f + 0.02f * i;
                    k.Box(new Vector3(x, -0.1f, -0.085f), new Vector3(x + 0.02f, 0.1f, 0.085f));
                }
                k.Use("band_buttons", new Vector3(0.92f, 0.9f, 0.84f), 0.3f, 0.3f);
                for (int r = 0; r < 2; r++)
                    for (int i = 0; i < 10; i++)
                        k.Cylinder(new Vector3(0.15f, -0.09f + 0.019f * i, -0.02f + 0.03f * r), new Vector3(0.16f, -0.09f + 0.019f * i, -0.02f + 0.03f * r), 0.006f, 6);
                k.Use("band_tin", nickel, 0.4f, 0.8f);
                k.Box(new Vector3(-0.155f, 0.11f, -0.03f), new Vector3(-0.085f, 0.115f, 0.03f));
                break;
            case "banjo":
                k.Use("band_tin", nickel, 0.3f, 0.9f);
                k.Cylinder(new Vector3(0.1f, -0.04f, 0), new Vector3(0.1f, 0.03f, 0), 0.14f, 18);
                k.Use("band_skin", skin, 0.6f, 0);
                k.Disc(new Vector3(0.1f, 0.031f, 0), Vector3.UnitY, 0.13f, 18);
                k.Use("band_varnish", varnish * 0.7f, 0.3f, 0.4f);
                k.Box(new Vector3(-0.45f, 0.0f, -0.02f), new Vector3(-0.03f, 0.03f, 0.02f));
                k.Box(new Vector3(-0.53f, -0.005f, -0.035f), new Vector3(-0.45f, 0.025f, 0.035f));
                k.Use("band_bridge", skin * 0.9f, 0.6f, 0);
                k.Box(new Vector3(0.14f, 0.031f, -0.03f), new Vector3(0.15f, 0.045f, 0.03f));
                break;
            case "guitar":
                Body(0.18f, 0.14f, 0.12f, 0.05f, new Vector3(0.78f, 0.52f, 0.26f));
                k.Use("band_ebony", ebony, 0.4f, 0.3f);
                k.Disc(new Vector3(-0.02f, 0.051f, 0), Vector3.UnitY, 0.045f, 14);
                k.Use("band_varnish", varnish * 0.6f, 0.3f, 0.4f);
                k.Box(new Vector3(-0.68f, 0.02f, -0.025f), new Vector3(-0.15f, 0.05f, 0.025f));
                k.Box(new Vector3(-0.8f, 0.015f, -0.04f), new Vector3(-0.68f, 0.045f, 0.04f));
                break;
            case "bodhran":
                k.Use("band_varnish", varnish, 0.4f, 0.3f);
                k.Cylinder(new Vector3(0, -0.04f, 0), new Vector3(0, 0.04f, 0), 0.22f, 22, caps: false);
                k.Use("band_skin", skin, 0.6f, 0);
                k.Disc(new Vector3(0, 0.04f, 0), Vector3.UnitY, 0.22f, 22);
                k.Disc(new Vector3(0, 0.04f, 0), -Vector3.UnitY, 0.22f, 22);
                // The cross-bar the hand goes behind, and the tipper.
                k.Use("band_varnish", varnish * 0.8f, 0.4f, 0.3f);
                k.Rod(new Vector3(-0.21f, -0.02f, 0), new Vector3(0.21f, -0.02f, 0), 0.008f);
                k.Use("band_ebony", ebony * 3, 0.4f, 0.3f);
                k.Rod(new Vector3(0.05f, 0.07f, -0.1f), new Vector3(0.05f, 0.07f, 0.12f), 0.007f, 6);
                break;
            case "spoons":
                k.Use("band_tin", nickel, 0.3f, 0.9f);
                foreach (float y in new[] { -0.012f, 0.012f })
                {
                    k.Cylinder(new Vector3(0.07f, y - 0.005f, 0), new Vector3(0.07f, y + 0.005f, 0), 0.03f, 10);
                    k.Rod(new Vector3(-0.12f, y, 0), new Vector3(0.04f, y, 0), 0.006f);
                }
                break;
            case "uglyStick":
                // Upright: the boot on the floor, the stick up through the can and the caps, a face on the can.
                k.Use("band_mop", new Vector3(0.72f, 0.6f, 0.42f), 0.6f, 0);
                k.Cylinder(new Vector3(0, -0.45f, 0), new Vector3(0, 0.55f, 0), 0.016f, 8);
                k.Use("band_rubber", new Vector3(0.12f, 0.2f, 0.1f), 0.8f, 0);
                k.Box(new Vector3(-0.05f, -0.6f, -0.05f), new Vector3(0.05f, -0.42f, 0.05f));
                k.Box(new Vector3(-0.05f, -0.6f, -0.05f), new Vector3(0.05f, -0.55f, 0.14f));
                k.Use("band_tin", nickel, 0.3f, 0.9f);
                k.Cylinder(new Vector3(0, 0.25f, 0), new Vector3(0, 0.37f, 0), 0.05f, 10);
                k.Use("band_caps", new Vector3(0.8f, 0.68f, 0.2f), 0.3f, 0.8f);
                for (int i = 0; i < 8; i++)
                {
                    float a = i * MathF.PI / 4, y = -0.1f + 0.06f * (i % 3);
                    k.Disc(new Vector3(MathF.Cos(a) * 0.025f, y, MathF.Sin(a) * 0.025f), new Vector3(MathF.Cos(a), 0, MathF.Sin(a)), 0.014f, 8);
                }
                k.Use("band_ebony", ebony, 0.4f, 0.3f);
                k.Disc(new Vector3(-0.015f, 0.33f, 0.051f), Vector3.UnitZ, 0.006f, 6);
                k.Disc(new Vector3(0.015f, 0.33f, 0.051f), Vector3.UnitZ, 0.006f, 6);
                break;
        }
        return k.Build($"prop-band-{id}");
    }

    public static MeshAsset Loot(Look? look, float radius)
    {
        var k = new Kit(look, 1130);
        k.Use("leather", Palette.DeepBrown, 0.9f, 0, tile: 0.4f);
        k.Box(new Vector3(-radius * 1.2f, -radius, -radius * 0.9f), new Vector3(radius * 1.2f, radius * 0.7f, radius * 0.9f));
        k.Use("rust_heavy", Palette.TarnishedBrass, 0.6f, 0.5f);
        k.Box(new Vector3(-radius * 1.22f, radius * 0.1f, -radius * 0.92f), new Vector3(radius * 1.22f, radius * 0.25f, radius * 0.92f), Kit.Faces.Sides);
        k.Use("leather", Palette.SootBlack, 0.8f, 0, tile: 0.4f);
        k.Rod(new Vector3(-radius * 0.6f, radius * 0.7f, 0), new Vector3(0, radius * 1.3f, 0), 0.015f, 5);
        k.Rod(new Vector3(0, radius * 1.3f, 0), new Vector3(radius * 0.6f, radius * 0.7f, 0), 0.015f, 5);
        return k.Build($"loot-{radius:0.00}");
    }

    /// <summary>A hand lantern: a tin base, glass all round with the flame's glow in it, wire guards, a hoop to carry it by.</summary>
    public static MeshAsset Lantern(Look? look)
    {
        var k = new Kit(look, 1130);
        k.Use("rust_heavy", Palette.IronGrey, 0.7f, 0.4f);
        k.Lathe(new Vector3(0, -0.14f, 0), [new(0.09f, 0), new(0.1f, 0.03f), new(0.09f, 0.06f)], 8, smooth: false);
        k.Lathe(new Vector3(0, 0.08f, 0), [new(0.09f, 0), new(0.07f, 0.04f), new(0.03f, 0.08f), new(0.02f, 0.1f)], 8, smooth: false);
        k.Use("lantern_pane", Palette.LampAmber, 0, 0, tile: 0.25f);
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
        k.Panel(new Vector3(-0.04f, 0.1f, -0.0405f), -Vector3.UnitZ, Vector3.UnitY, 0.02f, 0.02f, Vector2.Zero, new Vector2(0.1f, 0.1f));
        k.Emissive = 0;
        return k.Build("radio");
    }
}
