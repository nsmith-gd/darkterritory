using System.Numerics;
using Ballast.Render;

namespace DarkTerritory.Game.Art;

/// <summary>
/// The village finds as things (GDD App. F.3, the director, 8 Oct 2026: "loot needs to be discoverable in there ... a bit of
/// a brighter more unique look to them so that they stand out from the background as interactable objects. Do this with good
/// texture work, not VFX"; ARCHITECTURE §8 note 326). Each kind of find its own small model, about the body's size, wearing
/// its cell of the finds atlas (tools/art/texgen/mat_finds.py): a tin of bully beef in its red label, a bandage packet with
/// its red cross, a jar under gingham, a paraffin tin in chrome yellow, a watch with a white face in brass. The atlas is the
/// cleanest, brightest stock in the game, its paper's sheen and its metal's glint in the spec, so a lamp finds them among the
/// worn plaster and grey boards by their surfaces alone.
/// </summary>
public static class FindKit
{
    /// <summary>The atlas's cells, in mat_finds.py's ITEMS order (the content contract: append, don't reorder).</summary>
    public static readonly string[] Cells = ["tinnedFood", "treats", "candles", "medicine", "bandages", "morphine", "preserves", "lampOil",
        "tools", "valuableTools", "pocketWatch", "lampParts", "rope", "supplies", "brass", "cream"];

    const string Atlas = "finds_atlas";

    /// <summary>How much bigger than life a find is drawn (its body's a 0.15 m ball either way).</summary>
    const float Scale = 1.35f;

    /// <summary>A cell's texture rectangle (u0, v0, u1, v1), a texel in from its edges so its neighbours don't bleed in.</summary>
    static Vector4 Cell(string name)
    {
        int i = Math.Max(0, Array.IndexOf(Cells, name));
        float c = 1f / 4, inset = 1.5f / 512;
        float u = i % 4 * c, v = i / 4 * c;
        return new Vector4(u + inset, v + inset, u + c - inset, v + c - inset);
    }

    /// <summary>The atlas, at one texture to a metre, so texture coordinates in metres are the atlas's own.</summary>
    static void Wear(Kit k, Vector3 fallback, float shine) => k.Use(Atlas, fallback, 0.15f, shine, tile: 1f);

    /// <summary>A label round an upright cylinder from y0 to y1, the cell wrapped once round it.</summary>
    static void Band(Kit k, Vector3 centre, float radius, float y0, float y1, string cell, int sides = 12, bool lying = false)
    {
        var r = Cell(cell);
        for (int i = 0; i < sides; i++)
        {
            float a0 = i * MathF.Tau / sides, a1 = (i + 1) * MathF.Tau / sides;
            Vector3 P(float a, float y) => lying
                ? centre + new Vector3(y, MathF.Sin(a) * radius, MathF.Cos(a) * radius)
                : centre + new Vector3(MathF.Cos(a) * radius, y, MathF.Sin(a) * radius);
            float u0 = r.X + (r.Z - r.X) * i / sides, u1 = r.X + (r.Z - r.X) * (i + 1) / sides;
            // Outward, seen from outside: top left, top right, bottom right, bottom left.
            k.Quad(P(a1, y1), P(a0, y1), P(a0, y0), P(a1, y0), new(u1, r.Y), new(u0, r.Y), new(u0, r.W), new(u1, r.W), twoSided: true);
        }
    }

    /// <summary>A face of a box with a cell on it (centre, outward normal, the way up across it, its size).</summary>
    static void Face(Kit k, Vector3 centre, Vector3 normal, Vector3 up, float w, float h, string cell)
    {
        var r = Cell(cell);
        k.Panel(centre, normal, up, w, h, new Vector2(r.X, r.Y), new Vector2(r.Z, r.W));
    }

    /// <summary>A box every face of which wears a cell (top and bottom one, the sides another).</summary>
    static void LabelledBox(Kit k, Vector3 centre, Vector3 half, string sides, string top)
    {
        Face(k, centre + new Vector3(0, 0, half.Z), Vector3.UnitZ, Vector3.UnitY, half.X * 2, half.Y * 2, sides);
        Face(k, centre - new Vector3(0, 0, half.Z), -Vector3.UnitZ, Vector3.UnitY, half.X * 2, half.Y * 2, sides);
        Face(k, centre + new Vector3(half.X, 0, 0), Vector3.UnitX, Vector3.UnitY, half.Z * 2, half.Y * 2, sides);
        Face(k, centre - new Vector3(half.X, 0, 0), -Vector3.UnitX, Vector3.UnitY, half.Z * 2, half.Y * 2, sides);
        Face(k, centre + new Vector3(0, half.Y, 0), Vector3.UnitY, -Vector3.UnitZ, half.X * 2, half.Z * 2, top);
        Face(k, centre - new Vector3(0, half.Y, 0), -Vector3.UnitY, Vector3.UnitZ, half.X * 2, half.Z * 2, top);
    }

    /// <summary>A cell on a disc (a lid, a dial).</summary>
    static void Lid(Kit k, Vector3 centre, Vector3 normal, float radius, string cell, int sides = 12)
    {
        var r = Cell(cell);
        k.Disc(centre, normal, radius, sides, new Vector2((r.X + r.Z) / 2, (r.Y + r.W) / 2), (r.Z - r.X) / 2);
    }

    /// <summary>A tin can: its label round it, its lid and base tinplate (the atlas's brass cell's bright metal).</summary>
    static void Tin(Kit k, Vector3 at, float radius, float height, string label)
    {
        Band(k, at, radius, 0.008f, height - 0.008f, label);
        Band(k, at, radius * 1.02f, 0, 0.008f, "brass");
        Band(k, at, radius * 1.02f, height - 0.008f, height, "brass");
        Lid(k, at + new Vector3(0, height, 0), Vector3.UnitY, radius * 1.02f, "brass");
        Lid(k, at, -Vector3.UnitY, radius * 1.02f, "brass");
    }

    /// <summary>
    /// The model for a find of <paramref name="item"/> (loot.json's item key), centred on its body (a ball of
    /// <paramref name="radius"/>: the model sits from −radius up).
    /// </summary>
    public static MeshAsset Find(Look? look, string item, float radius)
    {
        var k = new Kit(look, 1131);
        float floor = -radius;
        var o = new Vector3(0, floor, 0);
        var cream = new Vector3(0.85f, 0.8f, 0.68f);
        Wear(k, cream, 0.35f);
        // A third again the size of the thing itself, about where it sits: read across a dark room, not fussed over in a hand.
        k.Push(Matrix4x4.CreateTranslation(-o) * Matrix4x4.CreateScale(Scale) * Matrix4x4.CreateTranslation(o));
        switch (item)
        {
            case "tinnedFood":
                // Two tins stood, a third lying across them.
                Tin(k, o + new Vector3(-0.055f, 0, 0), 0.05f, 0.11f, "tinnedFood");
                Tin(k, o + new Vector3(0.055f, 0, 0.01f), 0.05f, 0.11f, "tinnedFood");
                Band(k, o + new Vector3(-0.055f, 0.16f, 0), 0.05f, 0, 0.11f, "tinnedFood", lying: true);
                break;
            case "treats":
                // A paper packet of toffees, a twist at each end.
                LabelledBox(k, o + new Vector3(0, 0.05f, 0), new Vector3(0.1f, 0.05f, 0.06f), "treats", "treats");
                Band(k, o + new Vector3(0.1f, 0.05f, 0), 0.025f, 0, 0.04f, "treats", 6, lying: true);
                Band(k, o + new Vector3(-0.14f, 0.05f, 0), 0.025f, 0, 0.04f, "treats", 6, lying: true);
                break;
            case "candles":
                // Six candles in a blue paper wrapping round their middles.
                for (int i = 0; i < 6; i++)
                {
                    var c = o + new Vector3((i % 3 - 1) * 0.034f, 0.02f + i / 3 * 0.034f, 0);
                    Band(k, c + new Vector3(-0.11f, 0, 0), 0.016f, 0, 0.22f, "cream", 6, lying: true);
                }
                Band(k, o + new Vector3(-0.05f, 0.037f, 0), 0.062f, 0, 0.1f, "candles", 10, lying: true);
                break;
            case "medicine":
                // A chemist's bottle: brown glass, its label, a cork.
                k.Use("paint_oxide", new Vector3(0.45f, 0.2f, 0.08f), 0.1f, 0.8f, tile: 0.25f);
                k.Lathe(o, [new(0.045f, 0), new(0.05f, 0.01f), new(0.05f, 0.13f), new(0.02f, 0.17f), new(0.016f, 0.2f)], 10, smooth: true);
                Wear(k, cream, 0.35f);
                Band(k, o, 0.052f, 0.03f, 0.115f, "medicine");
                k.Use("wood_crate", new Vector3(0.5f, 0.38f, 0.24f), 0.6f, 0, tile: 0.2f);
                k.Cylinder(o + new Vector3(0, 0.2f, 0), o + new Vector3(0, 0.23f, 0), 0.017f, 6);
                break;
            case "bandages":
                // A packet, its red cross up; a roll beside it, half unwound.
                LabelledBox(k, o + new Vector3(-0.03f, 0.03f, 0), new Vector3(0.09f, 0.03f, 0.07f), "cream", "bandages");
                Band(k, o + new Vector3(0.06f, 0.035f, 0.05f), 0.035f, 0, 0.07f, "cream", 8, lying: true);
                break;
            case "morphine":
                // A flat white enamel tin, the cross on its lid.
                Band(k, o, 0.08f, 0, 0.035f, "cream", 14);
                Lid(k, o + new Vector3(0, 0.035f, 0), Vector3.UnitY, 0.08f, "morphine", 14);
                Lid(k, o, -Vector3.UnitY, 0.08f, "cream", 14);
                break;
            case "preserves":
                // A jar of plum, gingham tied over its mouth.
                // The plum through the glass, dark and glossy.
                k.Use("paint_oxide", new Vector3(0.35f, 0.06f, 0.1f), 0.1f, 0.8f, tile: 0.25f);
                k.Cylinder(o, o + new Vector3(0, 0.12f, 0), 0.06f, 10);
                Wear(k, cream, 0.1f);
                Lid(k, o + new Vector3(0, 0.135f, 0), Vector3.UnitY, 0.085f, "preserves", 10);
                Band(k, o, 0.064f, 0.1f, 0.135f, "preserves", 10);
                break;
            case "lampOil":
                // A paraffin tin, its spout and handle.
                LabelledBox(k, o + new Vector3(0, 0.1f, 0), new Vector3(0.07f, 0.1f, 0.045f), "lampOil", "brass");
                Wear(k, cream, 0.6f);
                Band(k, o + new Vector3(0.035f, 0.2f, 0), 0.014f, 0, 0.03f, "brass", 6);
                k.Use("rust_heavy", new Vector3(0.2f, 0.2f, 0.2f), 0.6f, 0.3f);
                k.Rod(o + new Vector3(-0.05f, 0.2f, 0), o + new Vector3(-0.02f, 0.25f, 0), 0.006f);
                k.Rod(o + new Vector3(-0.02f, 0.25f, 0), o + new Vector3(0.01f, 0.2f, 0), 0.006f);
                break;
            case "tools":
                // A tool roll in red oilcloth, tied, a spanner's jaw out of one end.
                Band(k, o + new Vector3(-0.14f, 0.05f, 0), 0.05f, 0, 0.28f, "tools", 10, lying: true);
                Lid(k, o + new Vector3(0.14f, 0.05f, 0), Vector3.UnitX, 0.05f, "tools", 10);
                Lid(k, o + new Vector3(-0.14f, 0.05f, 0), -Vector3.UnitX, 0.05f, "tools", 10);
                k.Use("rail_steel", new Vector3(0.6f, 0.6f, 0.62f), 0.3f, 0.6f, tile: 0.3f);
                k.Box(o + new Vector3(0.14f, 0.035f, -0.015f), o + new Vector3(0.2f, 0.065f, 0.015f));
                break;
            case "valuableTools":
                // A fitted case in green baize, its brass plate on the lid, brass corners.
                LabelledBox(k, o + new Vector3(0, 0.045f, 0), new Vector3(0.14f, 0.045f, 0.09f), "valuableTools", "valuableTools");
                foreach (float x in new[] { -0.13f, 0.13f })
                    foreach (float z in new[] { -0.08f, 0.08f })
                        LabelledBox(k, o + new Vector3(x, 0.045f, z), new Vector3(0.015f, 0.047f, 0.015f), "brass", "brass");
                break;
            case "pocketWatch":
                // A gilt watch on its back, white face up, its chain coiled beside it.
                Band(k, o, 0.055f, 0, 0.022f, "brass", 14);
                Lid(k, o + new Vector3(0, 0.022f, 0), Vector3.UnitY, 0.055f, "pocketWatch", 14);
                Band(k, o + new Vector3(0, 0, -0.06f), 0.012f, 0, 0.01f, "brass", 6);
                for (int i = 0; i < 8; i++)
                {
                    float a = i * 0.7f;
                    Band(k, o + new Vector3(0.07f + MathF.Cos(a) * 0.03f, 0, -0.04f + MathF.Sin(a) * 0.03f), 0.006f, 0, 0.006f, "brass", 4);
                }
                break;
            case "lampParts":
                // A brass burner and its glass chimney, laid together.
                k.Lathe(o, [new(0.04f, 0), new(0.045f, 0.02f), new(0.03f, 0.05f), new(0.025f, 0.07f)], 10, smooth: false);
                Band(k, o, 0.046f, 0.005f, 0.045f, "lampParts", 10);
                k.Use("glass_dirty", new Vector3(0.7f, 0.75f, 0.72f), 0, 0.8f, tile: 0.25f);
                k.Cylinder(o + new Vector3(0.05f, 0.035f, 0), o + new Vector3(0.21f, 0.035f, 0), 0.033f, 10, radiusB: 0.026f);
                break;
            case "rope":
                // A coil of manila, three turns lying flat.
                for (int turn = 0; turn < 3; turn++)
                    for (int i = 0; i < 12; i++)
                    {
                        float a0 = i * MathF.Tau / 12, a1 = (i + 1) * MathF.Tau / 12, rr = 0.1f - turn * 0.012f;
                        var p0 = o + new Vector3(MathF.Cos(a0) * rr, 0.02f + turn * 0.03f, MathF.Sin(a0) * rr);
                        var p1 = o + new Vector3(MathF.Cos(a1) * rr, 0.02f + turn * 0.03f, MathF.Sin(a1) * rr);
                        var r = Cell("rope");
                        float u0 = r.X + (r.Z - r.X) * (i % 4) / 4, u1 = u0 + (r.Z - r.X) / 4;
                        var up = new Vector3(0, 0.02f, 0);
                        var outward0 = Vector3.Normalize(p0 - o with { Y = p0.Y }) * 0.02f;
                        var outward1 = Vector3.Normalize(p1 - o with { Y = p1.Y }) * 0.02f;
                        k.Quad(p0 + up, p1 + up, p1 + outward1, p0 + outward0, new(u0, r.Y), new(u1, r.Y), new(u1, r.W), new(u0, r.W), twoSided: true);
                        k.Quad(p0 + outward0, p1 + outward1, p1 - up, p0 - up, new(u0, r.Y), new(u1, r.Y), new(u1, r.W), new(u0, r.W), twoSided: true);
                        k.Quad(p0 - up, p1 - up, p1 - outward1, p0 - outward0, new(u0, r.Y), new(u1, r.Y), new(u1, r.W), new(u0, r.W), twoSided: true);
                        k.Quad(p0 - outward0, p1 - outward1, p1 + up, p0 + up, new(u0, r.Y), new(u1, r.Y), new(u1, r.W), new(u0, r.W), twoSided: true);
                    }
                break;
            case "gannetHead":
                // The Gannet's head (note 340, its trophy), lying on its side: the sooted white skull, egg-shaped and plated, the
                // pale bone spear of its beak out along the ground with its hooked tip, and the dark serrations down its edge.
                // Not a village find, so no cell of its own: it stands out by its size.
                k.Use("fleece", new Vector3(0.86f, 0.84f, 0.78f), 0.05f, 0.25f, tile: 0.5f);
                k.Cylinder(o + new Vector3(-0.24f, 0.11f, 0), o + new Vector3(-0.2f, 0.11f, 0), 0.06f, 12, radiusB: 0.1f, capB: false);
                k.Cylinder(o + new Vector3(-0.2f, 0.11f, 0), o + new Vector3(-0.06f, 0.1f, 0), 0.1f, 12, radiusB: 0.11f, caps: false);
                k.Cylinder(o + new Vector3(-0.06f, 0.1f, 0), o + new Vector3(0.04f, 0.08f, 0), 0.11f, 12, radiusB: 0.07f, capA: false);
                k.Use("plaster_ruin", new Vector3(0.78f, 0.76f, 0.7f), 0.1f, 0.4f, tile: 0.5f);
                k.Cylinder(o + new Vector3(0.02f, 0.09f, 0), o + new Vector3(0.62f, 0.05f, 0), 0.055f, 10, radiusB: 0.012f);
                k.Cylinder(o + new Vector3(0.02f, 0.06f, 0), o + new Vector3(0.55f, 0.025f, 0), 0.04f, 10, radiusB: 0.01f);
                k.Cylinder(o + new Vector3(0.6f, 0.055f, 0), o + new Vector3(0.67f, 0.0f, 0), 0.014f, 6, radiusB: 0.004f);
                k.Use("paint_black", new Vector3(0.08f, 0.08f, 0.08f), 0.2f, 0.3f, tile: 0.5f);
                for (int i = 0; i < 7; i++)
                    k.BoxAt(o + new Vector3(0.08f + i * 0.065f, 0.03f - i * 0.002f, 0.03f), new Vector3(0.01f, 0.014f, 0.006f));
                // The pale ringed eye in its black skin.
                k.Cylinder(o + new Vector3(-0.08f, 0.13f, 0.1f), o + new Vector3(-0.08f, 0.13f, 0.115f), 0.03f, 10);
                break;
            default:
                // Anything else: a clean bundle in sacking, stencilled.
                LabelledBox(k, o + new Vector3(0, 0.07f, 0), new Vector3(0.13f, 0.07f, 0.09f), "supplies", "supplies");
                break;
        }
        k.Pop();
        return k.Build($"find-{item}-{radius:0.00}");
    }
}
