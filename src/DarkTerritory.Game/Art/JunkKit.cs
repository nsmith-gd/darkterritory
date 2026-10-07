using System.Numerics;
using Ballast.Render;

namespace DarkTerritory.Game.Art;

/// <summary>
/// What the railway left lying beside its own line (GDD App. F.3, the director, 7 Oct 2026: "the same three things over
/// and over again"; note 325): the gangs' stacks of ties and rail, their huts and their drums, a milepost, a wheelset
/// off a wreck, a trolley tipped off the rails, a spill of coal, a camp gone cold, a grave dug beside the ballast. Small
/// pieces, each its own kind, for <see cref="WorldArt"/>'s leavings to deal out so that no two along a stretch are the
/// same. Cooked in their own frame, foot at the origin, +Z toward the line.
/// </summary>
public static class JunkKit
{
    /// <summary>A stack of old ties: cross-laid in courses, the top ones slid and one fallen off the end.</summary>
    public static MeshAsset Ties(Look? look, int variant)
    {
        var k = new Kit(look, 5000 + variant);
        k.Use("wood_sleeper", Palette.DeepBrown, 0.95f, 0.02f, tile: 1.2f);
        int courses = 3 + variant % 3;
        for (int c = 0; c < courses; c++)
        {
            bool across = c % 2 == 0;
            int n = c == courses - 1 ? 2 + variant % 2 : 4;
            for (int i = 0; i < n; i++)
            {
                float slide = MathF.Sin(variant * 3.1f + c * 1.7f + i * 2.3f) * 0.12f;
                float at = -0.75f + i * 0.5f;
                var centre = across ? new Vector3(slide, 0.08f + c * 0.15f, at) : new Vector3(at, 0.08f + c * 0.15f, slide);
                var half = across ? new Vector3(1.3f, 0.075f, 0.12f) : new Vector3(0.12f, 0.075f, 1.3f);
                k.With(Matrix4x4.CreateRotationY(slide * 0.6f), () => k.BoxAt(centre, half, bottom: false));
            }
        }
        // One off the end, its end in the grass.
        k.With(Matrix4x4.CreateRotationZ(0.35f) * Kit.At(1.75f, 0.3f, 0.2f), () => k.BoxAt(Vector3.Zero, new Vector3(1.3f, 0.075f, 0.12f), bottom: false));
        return k.Build($"junk-ties-{variant}");
    }

    /// <summary>Lengths of rail racked on two ties, rusted through, one bent.</summary>
    public static MeshAsset Rails(Look? look, int variant)
    {
        var k = new Kit(look, 5100 + variant);
        k.Use("wood_sleeper", Palette.DeepBrown, 0.95f, 0.02f, tile: 1.2f);
        foreach (float z in new[] { -2.5f, 2.5f })
            k.BoxAt(new Vector3(0, 0.075f, z), new Vector3(1.0f, 0.075f, 0.12f), bottom: false);
        k.Use("rail_steel", Palette.IronGrey, 0.95f, 0.15f);
        int n = 3 + variant % 3;
        for (int i = 0; i < n; i++)
        {
            float x = -0.6f + i * 0.3f, y = 0.15f + (i % 2) * 0.07f;
            float bend = i == 1 ? 0.06f + variant * 0.03f : 0;
            // A rail: its head, web and foot as three boxes, 9 m.
            k.With(Matrix4x4.CreateRotationY(bend) * Kit.At(x, y, 0), () =>
            {
                k.BoxAt(new Vector3(0, 0.01f, 0), new Vector3(0.07f, 0.01f, 4.5f), bottom: false);
                k.BoxAt(new Vector3(0, 0.07f, 0), new Vector3(0.012f, 0.05f, 4.5f), top: false, bottom: false);
                k.BoxAt(new Vector3(0, 0.13f, 0), new Vector3(0.035f, 0.018f, 4.5f), bottom: false);
            });
        }
        return k.Build($"junk-rails-{variant}");
    }

    /// <summary>Oil drums, two to four: stood, one on its side, the paint gone to rust; a dark stain under them.</summary>
    public static MeshAsset Drums(Look? look, int variant)
    {
        var k = new Kit(look, 5200 + variant);
        (string Layer, Vector3 Colour)[] paint = [("paint_oxide", Palette.RustRed), ("paint_olive", Palette.MuddyOlive), ("paint_black", Palette.SootBlack)];
        int n = 2 + variant % 3;
        for (int i = 0; i < n; i++)
        {
            var (layer, colour) = paint[(variant + i) % paint.Length];
            k.Use(i % 2 == 0 ? "rust_heavy" : layer, colour, 0.9f, 0.15f);
            float a = i * 2.2f + variant;
            var at = new Vector3(MathF.Cos(a) * 0.55f * (i > 0 ? 1 : 0), 0, MathF.Sin(a) * 0.55f * (i > 0 ? 1 : 0));
            if (i == n - 1 && n > 2)
            {
                // On its side, rolled off a little.
                var c = at + new Vector3(0.6f, 0.29f, 0.3f);
                k.Cylinder(c - new Vector3(0.0f, 0, 0.44f), c + new Vector3(0.0f, 0, 0.44f), 0.29f, 10);
                k.Rod(c + new Vector3(-0.3f, 0, -0.15f), c + new Vector3(-0.3f, 0, 0.15f), 0.012f);
            }
            else
            {
                k.Cylinder(at, at + new Vector3(0, 0.88f, 0), 0.29f, 10, capA: false);
                // The rolling hoops.
                foreach (float y in new[] { 0.3f, 0.58f })
                    k.Cylinder(at + new Vector3(0, y - 0.015f, 0), at + new Vector3(0, y + 0.015f, 0), 0.3f, 10, caps: false);
            }
        }
        k.Use("tar", Palette.SootBlack, 0.4f, 0.5f);
        k.Tint = new Vector3(0.35f);
        k.Disc(new Vector3(0.2f, 0.01f, 0.1f), Vector3.UnitY, 0.85f, 10);
        k.Tint = Vector3.One;
        return k.Build($"junk-drums-{variant}");
    }

    /// <summary>A telegraph gang's cable reel, wooden, on its edge, the last of the copper stripped off it.</summary>
    public static MeshAsset Reel(Look? look)
    {
        var k = new Kit(look, 5300);
        k.Use("wood_grey", Palette.DeepBrown, 0.9f, 0.02f, tile: 1);
        foreach (float x in new[] { -0.45f, 0.45f })
            k.Cylinder(new Vector3(x - 0.04f, 0.8f, 0), new Vector3(x + 0.04f, 0.8f, 0), 0.8f, 14, smooth: false);
        k.Cylinder(new Vector3(-0.41f, 0.8f, 0), new Vector3(0.41f, 0.8f, 0), 0.35f, 10, caps: false);
        k.Use("copper_pipe", Palette.TarnishedBrass, 0.8f, 0.3f);
        k.Cylinder(new Vector3(-0.41f, 0.8f, 0), new Vector3(-0.1f, 0.8f, 0), 0.4f, 10, caps: false);
        k.Use("rust_heavy", Palette.IronGrey, 0.9f, 0.2f);
        k.Cylinder(new Vector3(-0.55f, 0.8f, 0), new Vector3(0.55f, 0.8f, 0), 0.06f, 6);
        return k.Build("junk-reel");
    }

    /// <summary>A milepost: a squared post, whitewash flaking, an iron plate with the miles on it (the numbers gone).</summary>
    public static MeshAsset Milepost(Look? look, int variant)
    {
        var k = new Kit(look, 5400 + variant);
        float lean = MathF.Sin(variant * 1.9f) * 0.07f;
        k.With(Matrix4x4.CreateRotationX(lean), () =>
        {
            k.Use("plaster_ruin", new Vector3(0.62f, 0.6f, 0.55f), 0.9f, 0.05f, tile: 1);
            k.Box(new Vector3(-0.09f, -0.2f, -0.09f), new Vector3(0.09f, 1.15f, 0.09f));
            k.Use("enamel_plate", Palette.SootBlack, 0.8f, 0.3f);
            k.BoxAt(new Vector3(0, 0.95f, 0.1f), new Vector3(0.16f, 0.12f, 0.012f));
            k.Use("stencil_numerals", new Vector3(0.75f, 0.72f, 0.66f), 0.85f, 0.1f, tile: 1);
            k.Panel(new Vector3(0, 0.95f, 0.113f), Vector3.UnitZ, Vector3.UnitY, 0.24f, 0.16f);
        });
        return k.Build($"junk-milepost-{variant}");
    }

    /// <summary>A platelayers' hut: brick, one room, its tin roof half off, the door gone, a stovepipe.</summary>
    public static MeshAsset Hut(Look? look, int variant)
    {
        var k = new Kit(look, 5500 + variant);
        float w = 1.4f, d = 1.1f, h = 2.1f;
        k.Use(variant % 2 == 0 ? "brick_soot" : "wood_siding", variant % 2 == 0 ? Palette.Charcoal : Palette.DeepBrown, 0.85f, 0.05f, tile: 1.5f);
        // Walls (the doorway in the face toward the line).
        k.Box(new Vector3(-w, 0, -d), new Vector3(w, h, -d + 0.2f), bottom: false);
        k.Box(new Vector3(-w, 0, -d), new Vector3(-w + 0.2f, h, d), bottom: false);
        k.Box(new Vector3(w - 0.2f, 0, -d), new Vector3(w, h, d), bottom: false);
        k.Box(new Vector3(-w, 0, d - 0.2f), new Vector3(-0.4f, h, d), bottom: false);
        k.Box(new Vector3(0.4f, 0, d - 0.2f), new Vector3(w, h, d), bottom: false);
        k.Box(new Vector3(-0.4f, 1.85f, d - 0.2f), new Vector3(0.4f, h, d), bottom: false);
        k.Use("wood_floor", Palette.SootBlack, 0.95f, 0, tile: 1);
        k.Quad(new Vector3(-w, 0.02f, -d), new Vector3(w, 0.02f, -d), new Vector3(w, 0.02f, d), new Vector3(-w, 0.02f, d));
        // The roof: corrugated sheets on a lean-to slope, the near one gone, one hanging off.
        k.Use("corrugated_iron", Palette.RustRed, 0.95f, 0.2f, tile: 1);
        // Lean-to, falling toward the line; the near half's sheets are gone, the far half's stay.
        k.With(Matrix4x4.CreateRotationX(0.22f) * Kit.At(0, h - 0.1f, 0), () =>
        {
            k.Box(new Vector3(-w - 0.15f, 0, -d - 0.25f), new Vector3(-0.1f, 0.03f, d + 0.3f));
            k.Box(new Vector3(-0.1f, 0, -d - 0.25f), new Vector3(w + 0.15f, 0.03f, -0.2f));
        });
        k.With(Matrix4x4.CreateRotationZ(0.9f) * Kit.At(w + 0.1f, h - 0.4f, 0.2f), () =>
            k.Panel(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, 2 * d, 1.2f, twoSided: true));
        k.Use("rust_heavy", Palette.IronGrey, 0.9f, 0.2f);
        k.Cylinder(new Vector3(-w + 0.4f, h - 0.3f, -d + 0.3f), new Vector3(-w + 0.4f, h + 0.9f, -d + 0.3f), 0.07f, 6);
        return k.Build($"junk-hut-{variant}");
    }

    /// <summary>A wheelset off a wreck: the axle and its two wheels, flanges and all, sunk in the verge.</summary>
    public static MeshAsset Wheelset(Look? look)
    {
        var k = new Kit(look, 5600);
        k.Use("wheel_iron", Palette.IronGrey, 0.95f, 0.2f);
        foreach (float x in new[] { -0.72f, 0.72f })
        {
            float s = MathF.Sign(x);
            k.Cylinder(new Vector3(x - s * 0.04f, 0.42f, 0), new Vector3(x + s * 0.04f, 0.42f, 0), 0.46f, 16);
            k.Cylinder(new Vector3(x - s * 0.07f, 0.42f, 0), new Vector3(x - s * 0.04f, 0.42f, 0), 0.5f, 16, capB: false);
        }
        k.Use("rust_heavy", Palette.RustRed, 0.95f, 0.1f);
        k.Cylinder(new Vector3(-0.85f, 0.42f, 0), new Vector3(0.85f, 0.42f, 0), 0.08f, 8);
        return k.Build("junk-wheelset");
    }

    /// <summary>Coal spilled off a tender or a wreck: a low heap, black, a broken shovel's handle in it.</summary>
    public static MeshAsset Coal(Look? look, int variant)
    {
        var k = new Kit(look, 5700 + variant);
        k.Use("coal", Palette.SootBlack, 0.6f, 0.35f, tile: 1);
        float r = 1.2f + variant * 0.35f;
        k.Lathe(Vector3.Zero, [new(r, -0.05f), new(r * 0.85f, 0.18f), new(r * 0.5f, 0.42f + variant * 0.08f), new(r * 0.15f, 0.55f + variant * 0.1f)], 9, smooth: false);
        // Lumps off its skirt.
        for (int i = 0; i < 5; i++)
        {
            float a = i * 1.3f + variant;
            k.With(Matrix4x4.CreateRotationY(a) * Kit.At(MathF.Cos(a) * (r + 0.2f), 0.05f, MathF.Sin(a) * (r + 0.2f)), () =>
                k.BoxAt(Vector3.Zero, new Vector3(0.08f, 0.06f, 0.07f), bottom: false));
        }
        k.Use("wood_grey", Palette.DeepBrown, 0.9f, 0.02f, tile: 1);
        k.Rod(new Vector3(r * 0.3f, 0.3f, 0.1f), new Vector3(r * 0.3f + 0.5f, 1.0f, -0.2f), 0.02f);
        return k.Build($"junk-coal-{variant}");
    }

    /// <summary>A camp gone cold: a ring of stones round black ash, charred ends of ties, a pot on its side, a log to sit on.</summary>
    public static MeshAsset Camp(Look? look, int variant)
    {
        var k = new Kit(look, 5800 + variant);
        k.Use("rock_cliff", Palette.BlueGrey * 0.7f, 0.9f, 0.05f, tile: 1);
        for (int i = 0; i < 9; i++)
        {
            float a = i * MathF.Tau / 9 + variant;
            k.With(Matrix4x4.CreateRotationY(a * 1.7f) * Kit.At(MathF.Cos(a) * 0.6f, 0.06f, MathF.Sin(a) * 0.6f), () =>
                k.BoxAt(Vector3.Zero, new Vector3(0.12f, 0.09f, 0.1f), bottom: false));
        }
        k.Use("coal", Palette.Charcoal, 0.5f, 0.1f, tile: 1);
        k.Disc(new Vector3(0, 0.03f, 0), Vector3.UnitY, 0.5f, 8);
        k.Use("wood_sleeper", Palette.SootBlack, 0.95f, 0.02f, tile: 1);
        for (int i = 0; i < 3; i++)
        {
            float a = i * 2.1f + variant * 0.7f;
            k.Rod(new Vector3(MathF.Cos(a) * 0.1f, 0.05f, MathF.Sin(a) * 0.1f), new Vector3(MathF.Cos(a) * 0.7f, 0.12f, MathF.Sin(a) * 0.7f), 0.06f);
        }
        k.Use("pine_bark", Palette.DeepBrown, 0.9f, 0.02f, tile: 1);
        k.Cylinder(new Vector3(-0.9f, 0.18f, 1.1f), new Vector3(0.9f, 0.18f, 1.25f), 0.18f, 7);
        k.Use("rust_heavy", Palette.IronGrey, 0.9f, 0.2f);
        k.Cylinder(new Vector3(0.75f, 0.12f, -0.55f), new Vector3(0.95f, 0.12f, -0.75f), 0.13f, 8, capB: false);
        return k.Build($"junk-camp-{variant}");
    }

    /// <summary>A platelayers' trolley tipped off the line on its side, a wheel in the air.</summary>
    public static MeshAsset Trolley(Look? look)
    {
        var k = new Kit(look, 5900);
        // On its side: the deck stands up, its wheels toward the line.
        k.With(Matrix4x4.CreateRotationZ(1.35f) * Kit.At(0.05f, 0.95f, 0), () =>
        {
            k.Use("wood_floor", Palette.DeepBrown, 0.95f, 0.02f, tile: 1);
            k.BoxAt(new Vector3(0, 0.45f, 0), new Vector3(0.85f, 0.04f, 1.1f));
            k.Use("rust_heavy", Palette.IronGrey, 0.95f, 0.2f);
            foreach (float x in new[] { -0.72f, 0.72f })
                k.BoxAt(new Vector3(x, 0.36f, 0), new Vector3(0.05f, 0.06f, 1.1f));
            k.Use("wheel_iron", Palette.IronGrey, 0.95f, 0.2f);
            foreach (float z in new[] { -0.7f, 0.7f })
            {
                k.Cylinder(new Vector3(-0.8f, 0.22f, z), new Vector3(0.8f, 0.22f, z), 0.035f, 6);
                foreach (float x in new[] { -0.72f, 0.72f })
                    k.Cylinder(new Vector3(x - 0.03f, 0.22f, z), new Vector3(x + 0.03f, 0.22f, z), 0.22f, 12);
            }
            // The pump handle, its walking beam bent.
            k.Rod(new Vector3(0, 0.5f, 0), new Vector3(0, 1.4f, 0.1f), 0.04f);
            k.Rod(new Vector3(0, 1.4f, -0.8f), new Vector3(0, 1.45f, 0.85f), 0.03f);
        });
        return k.Build("junk-trolley");
    }

    /// <summary>A grave beside the ballast: a mound, a cross of two ties' offcuts, a railwayman's cap on it.</summary>
    public static MeshAsset Grave(Look? look, int variant)
    {
        var k = new Kit(look, 6000 + variant);
        k.Use("ground_mud", Palette.DeepBrown * 0.8f, 0.95f, 0.02f, tile: 1);
        k.With(Matrix4x4.CreateScale(1, 1, 2.1f), () =>
            k.Lathe(Vector3.Zero, [new(0.5f, -0.05f), new(0.42f, 0.14f), new(0.2f, 0.22f), new(0, 0.24f)], 8));
        float lean = MathF.Sin(variant * 2.7f) * 0.12f;
        k.With(Matrix4x4.CreateRotationZ(lean) * Kit.At(0, 0, -1.15f), () =>
        {
            k.Use("wood_sleeper", Palette.DeepBrown, 0.95f, 0.02f, tile: 1);
            k.Box(new Vector3(-0.06f, -0.2f, -0.05f), new Vector3(0.06f, 1.0f, 0.05f));
            k.Box(new Vector3(-0.32f, 0.62f, -0.05f), new Vector3(0.32f, 0.74f, 0.05f));
            if (variant % 2 == 0)
            {
                k.Use("wool", Palette.SootBlack, 0.9f, 0.05f, tile: 1);
                k.Lathe(new Vector3(0, 1.0f, 0), [new(0.11f, 0), new(0.1f, 0.07f), new(0, 0.09f)], 8);
                k.Use("leather", Palette.SootBlack, 0.9f, 0.2f, tile: 1);
                k.BoxAt(new Vector3(0, 1.01f, 0.12f), new Vector3(0.09f, 0.008f, 0.05f));
            }
        });
        return k.Build($"junk-grave-{variant}");
    }
}
