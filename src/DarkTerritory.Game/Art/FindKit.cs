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
    /// The comet's light (GDD §19's "attracts everything; should look like it"), the green of a comet-loaded car's seams
    /// (<see cref="SceneArt"/>): a fragment's shards glow it, and its light is it.
    /// </summary>
    public static readonly Vector3 CometGlow = new(0.35f, 0.95f, 0.45f);

    /// <summary>Whether a find is one that glows (note 718: a comet fragment), for its light.</summary>
    public static bool Glows(string item) => item == "cometFragment";

    /// <summary>
    /// A lump of rock (note 718): an icosahedron's twelve corners pushed in and out by a repeatable hash, its underside
    /// flattened to sit, every facet flat. <paramref name="squash"/> its height to its width.
    /// </summary>
    static Vector3[] Lump(Kit k, Vector3 centre, float radius, float squash, int seed)
    {
        float g = (1 + MathF.Sqrt(5)) / 2;
        Vector3[] p =
        [
            new(-1, g, 0), new(1, g, 0), new(-1, -g, 0), new(1, -g, 0), new(0, -1, g), new(0, 1, g),
            new(0, -1, -g), new(0, 1, -g), new(g, 0, -1), new(g, 0, 1), new(-g, 0, -1), new(-g, 0, 1),
        ];
        for (int i = 0; i < p.Length; i++)
        {
            float h = MathF.Abs(MathF.Sin((i + 1) * 12.9898f + seed * 78.233f) * 43758.547f) % 1;
            var v = Vector3.Normalize(p[i]) * radius * (0.72f + 0.5f * h);
            p[i] = centre + new Vector3(v.X, MathF.Max(v.Y * squash, -radius * 0.35f), v.Z);
        }
        int[] f = [0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1];
        for (int i = 0; i < f.Length; i += 3)
            k.Tri(p[f[i]], p[f[i + 2]], p[f[i + 1]]);
        return p;
    }

    /// <summary>A shard (a thin four-sided spike) from <paramref name="a"/> out to <paramref name="b"/>.</summary>
    static void Shard(Kit k, Vector3 a, Vector3 b, float half)
    {
        var along = Vector3.Normalize(b - a);
        var side = Vector3.Normalize(Vector3.Cross(along, MathF.Abs(along.Y) > 0.9f ? Vector3.UnitX : Vector3.UnitY)) * half;
        var up = Vector3.Cross(side, along);
        Vector3[] ring = [a + side, a + up, a - side, a - up];
        for (int i = 0; i < 4; i++)
            k.Tri(ring[i], ring[(i + 1) % 4], b);
    }

    /// <summary>
    /// The model for a find of <paramref name="item"/> (loot.json's item key), centred on its body (a ball of
    /// <paramref name="radius"/>: the model sits from −radius up).
    /// </summary>
    public static MeshAsset Find(Look? look, string item, float radius) => Find(look, item, radius, 0);

    /// <summary>A crack across glass or a dent's shadow (note 719): a thin dark strip from <paramref name="a"/> to <paramref name="b"/>, standing just off the face.</summary>
    static void Crack(Kit k, Vector3 a, Vector3 b, Vector3 normal, float width = 0.004f)
    {
        var along = b - a;
        var side = Vector3.Normalize(Vector3.Cross(normal, along)) * width;
        var off = normal * 0.003f;
        k.Quad(a + off - side, b + off - side, b + off + side, a + off + side, twoSided: true);
    }

    /// <summary>A few glass shards lying about a broken thing (note 719), flat on the floor round <paramref name="at"/>.</summary>
    static void Shards(Kit k, Vector3 at, float spread, int seed)
    {
        for (int i = 0; i < 6; i++)
        {
            float a = i * 1.7f + seed, d = spread * (0.5f + 0.5f * MathF.Abs(MathF.Sin(i * 3.1f + seed)));
            var c = at + new Vector3(MathF.Cos(a) * d, 0.004f, MathF.Sin(a) * d);
            float s = 0.012f + 0.008f * (i % 3);
            k.Tri(c + new Vector3(-s, 0, 0), c + new Vector3(s * 0.4f, 0.002f, s), c + new Vector3(s, 0, -s * 0.5f));
        }
    }

    /// <summary>
    /// The model for a find of <paramref name="item"/>, knocked <paramref name="knocks"/> times (note 719): a fragile one shows
    /// it (a crack across the glass, a dented case, a broken neck and the shards by it). The clerk's the only other one who says.
    /// </summary>
    public static MeshAsset Find(Look? look, string item, float radius, int knocks)
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
            case "cometFragment":
                // A lump of the comet (note 718; GDD §19): black slag, glassy, split by green crystal that glows, shards of it
                // standing out of the cracks. Bigger than a find, and lit, so a crew sees it from the line.
                k.Use("slag", new Vector3(0.12f, 0.12f, 0.13f), 0.2f, 0.7f, tile: 0.4f);
                var rock = Lump(k, o + new Vector3(0, 0.1f, 0), 0.15f, 0.75f, 7);
                k.Use("quartz", CometGlow, 0, 0.9f, tile: 0.3f);
                k.Tint = CometGlow;
                k.Emissive = 1;
                Lump(k, o + new Vector3(0.03f, 0.12f, -0.02f), 0.1f, 0.8f, 3);
                foreach (int i in (int[])[1, 5, 8, 9, 11])
                {
                    var from = o + new Vector3(0, 0.1f, 0) + (rock[i] - o - new Vector3(0, 0.1f, 0)) * 0.6f;
                    Shard(k, from, from + Vector3.Normalize(rock[i] - o - new Vector3(0, 0.05f, 0)) * 0.11f, 0.018f);
                }
                k.Emissive = 0;
                break;
            case "grumblerJaw":
                // The Grumbler's jaw (note 718): a crate-gnawer's lower jaw, grey bone in a U, its teeth worn flat and splintered.
                k.Use("plaster_ruin", new Vector3(0.74f, 0.71f, 0.64f), 0.3f, 0.3f, tile: 0.5f);
                for (int side = -1; side <= 1; side += 2)
                    k.Cylinder(o + new Vector3(side * 0.07f, 0.03f, -0.1f), o + new Vector3(side * 0.04f, 0.035f, 0.09f), 0.028f, 6, radiusB: 0.022f);
                k.Cylinder(o + new Vector3(-0.045f, 0.035f, 0.09f), o + new Vector3(0.045f, 0.035f, 0.09f), 0.024f, 6);
                k.Use("rust_heavy", new Vector3(0.42f, 0.36f, 0.24f), 0.5f, 0.2f, tile: 0.5f);
                for (int i = 0; i < 6; i++)
                {
                    float z = -0.06f + i * 0.028f, x = 0.068f - i * 0.004f;
                    k.BoxAt(o + new Vector3(x, 0.065f, z), new Vector3(0.009f, 0.014f, 0.009f));
                    k.BoxAt(o + new Vector3(-x, 0.065f, z), new Vector3(0.009f, 0.014f, 0.009f));
                }
                break;
            case "gauntHand":
                // The Gaunt's hand (note 718): long, grey and spindly, the fingers half again a man's, curled where it fell.
                k.Use("skin", new Vector3(0.55f, 0.55f, 0.52f), 0.3f, 0.2f, tile: 0.4f);
                k.Cylinder(o + new Vector3(-0.2f, 0.03f, 0), o + new Vector3(-0.05f, 0.03f, 0), 0.025f, 6, radiusB: 0.035f);
                k.BoxAt(o + new Vector3(0, 0.03f, 0), new Vector3(0.05f, 0.016f, 0.045f));
                for (int i = 0; i < 4; i++)
                {
                    float z = -0.036f + i * 0.024f, curl = 0.02f + i * 0.006f;
                    var knuckle = o + new Vector3(0.05f, 0.03f, z);
                    var mid = knuckle + new Vector3(0.11f, 0.004f, z * 0.3f);
                    k.Cylinder(knuckle, mid, 0.009f, 5);
                    k.Cylinder(mid, mid + new Vector3(0.08f, curl, 0), 0.008f, 5, radiusB: 0.004f);
                }
                k.Cylinder(o + new Vector3(0.0f, 0.03f, 0.045f), o + new Vector3(0.08f, 0.03f, 0.11f), 0.009f, 5, radiusB: 0.005f);
                break;
            case "climberClaw":
                // A Climber's claw (note 718): a hooked black talon as long as a hand on its knuckle of grey hide.
                k.Use("skin", new Vector3(0.45f, 0.43f, 0.4f), 0.4f, 0.2f, tile: 0.4f);
                k.Cylinder(o + new Vector3(-0.08f, 0.03f, 0), o + new Vector3(0, 0.03f, 0), 0.03f, 6, radiusB: 0.026f);
                k.Use("paint_black", new Vector3(0.06f, 0.06f, 0.06f), 0.1f, 0.6f, tile: 0.5f);
                k.Cylinder(o + new Vector3(0, 0.03f, 0), o + new Vector3(0.09f, 0.05f, 0), 0.02f, 6, radiusB: 0.014f);
                k.Cylinder(o + new Vector3(0.09f, 0.05f, 0), o + new Vector3(0.15f, 0.03f, 0), 0.014f, 6, radiusB: 0.006f);
                k.Cylinder(o + new Vector3(0.15f, 0.03f, 0), o + new Vector3(0.17f, 0.0f, 0), 0.006f, 4, radiusB: 0.002f);
                break;
            case "passengerCap":
                // The Passenger's cap (note 718): a lost railway's crew cap, the cloth rotted black, its peak cracked, its
                // brass badge still bright.
                k.Use("coat_oilskin", new Vector3(0.1f, 0.1f, 0.12f), 0.6f, 0.2f, tile: 0.4f);
                k.Cylinder(o, o + new Vector3(0, 0.07f, 0), 0.085f, 12, radiusB: 0.095f);
                k.Use("paint_black", new Vector3(0.05f, 0.05f, 0.05f), 0.3f, 0.5f, tile: 0.4f);
                k.Quad(o + new Vector3(-0.08f, 0.012f, 0.06f), o + new Vector3(0.08f, 0.012f, 0.06f), o + new Vector3(0.06f, 0.004f, 0.15f), o + new Vector3(-0.06f, 0.004f, 0.15f), twoSided: true);
                Wear(k, new Vector3(0.85f, 0.8f, 0.68f), 0.35f);
                Lid(k, o + new Vector3(0, 0.045f, 0.087f), Vector3.UnitZ, 0.02f, "brass", 8);
                break;
            case "brakemanWheel":
                // The Brakeman's wheel (note 718): the rusted brake wheel his arm had grown into, a length of chain still on it.
                k.Use("wheel_iron", new Vector3(0.3f, 0.22f, 0.16f), 0.7f, 0.3f, tile: 0.4f);
                for (int i = 0; i < 10; i++)
                {
                    float a0 = i * MathF.Tau / 10, a1 = (i + 1) * MathF.Tau / 10;
                    k.Cylinder(o + new Vector3(MathF.Cos(a0) * 0.15f, 0.015f, MathF.Sin(a0) * 0.15f),
                        o + new Vector3(MathF.Cos(a1) * 0.15f, 0.015f, MathF.Sin(a1) * 0.15f), 0.014f, 5);
                }
                for (int i = 0; i < 4; i++)
                {
                    float a = i * MathF.Tau / 4 + 0.4f;
                    k.Cylinder(o + new Vector3(0, 0.015f, 0), o + new Vector3(MathF.Cos(a) * 0.15f, 0.015f, MathF.Sin(a) * 0.15f), 0.01f, 4);
                }
                k.Cylinder(o, o + new Vector3(0, 0.04f, 0), 0.03f, 8);
                k.Use("rust_heavy", new Vector3(0.36f, 0.24f, 0.14f), 0.7f, 0.3f, tile: 0.3f);
                for (int i = 0; i < 4; i++)
                    k.BoxAt(o + new Vector3(0.17f + i * 0.04f, 0.01f, 0.02f * (i % 2)), new Vector3(0.022f, 0.008f, 0.012f));
                break;
            case "knotterClaw":
                // A Knotter's claw (note 718): the hook off one end of its rope of pale flesh, a stub of that still on it.
                k.Use("flesh", new Vector3(0.78f, 0.66f, 0.6f), 0.3f, 0.4f, tile: 0.4f);
                k.Cylinder(o + new Vector3(-0.16f, 0.035f, 0), o + new Vector3(-0.02f, 0.035f, 0), 0.035f, 7, radiusB: 0.03f);
                k.Use("paint_black", new Vector3(0.08f, 0.07f, 0.07f), 0.2f, 0.5f, tile: 0.4f);
                for (int side = -1; side <= 1; side += 2)
                {
                    k.Cylinder(o + new Vector3(-0.02f, 0.035f, side * 0.015f), o + new Vector3(0.07f, 0.04f, side * 0.04f), 0.014f, 5, radiusB: 0.009f);
                    k.Cylinder(o + new Vector3(0.07f, 0.04f, side * 0.04f), o + new Vector3(0.1f, 0.02f, side * 0.01f), 0.009f, 5, radiusB: 0.003f);
                }
                break;
            case "hotboxPlate":
                // A Hotbox plate (note 718): one of the black plates off its back, curved, scorched blue at its edge.
                k.Use("iron_plate", new Vector3(0.1f, 0.1f, 0.11f), 0.4f, 0.6f, tile: 0.4f);
                for (int i = 0; i < 4; i++)
                {
                    float x0 = -0.12f + i * 0.06f, x1 = x0 + 0.06f;
                    float y0 = 0.05f - MathF.Pow(x0 / 0.12f, 2) * 0.04f, y1 = 0.05f - MathF.Pow(x1 / 0.12f, 2) * 0.04f;
                    k.Quad(o + new Vector3(x0, y0, -0.08f), o + new Vector3(x1, y1, -0.08f), o + new Vector3(x1, y1, 0.08f), o + new Vector3(x0, y0, 0.08f), twoSided: true);
                }
                k.Use("rust_heavy", new Vector3(0.3f, 0.32f, 0.42f), 0.5f, 0.5f, tile: 0.3f);
                k.BoxAt(o + new Vector3(0, 0.054f, 0.08f), new Vector3(0.12f, 0.006f, 0.006f));
                break;
            case "beetleShovel":
                // The Beetle's shovel (note 718): the Freight Beetle's head plate, broad and spade-edged, scored by crates.
                k.Use("iron_plate", new Vector3(0.22f, 0.18f, 0.12f), 0.6f, 0.4f, tile: 0.4f);
                k.Quad(o + new Vector3(-0.17f, 0.02f, 0.12f), o + new Vector3(0.17f, 0.02f, 0.12f), o + new Vector3(0.12f, 0.08f, -0.1f), o + new Vector3(-0.12f, 0.08f, -0.1f), twoSided: true);
                k.Quad(o + new Vector3(-0.12f, 0.08f, -0.1f), o + new Vector3(0.12f, 0.08f, -0.1f), o + new Vector3(0.1f, 0.0f, -0.12f), o + new Vector3(-0.1f, 0.0f, -0.12f), twoSided: true);
                k.Use("paint_black", new Vector3(0.07f, 0.06f, 0.05f), 0.4f, 0.3f, tile: 0.4f);
                for (int side = -1; side <= 1; side += 2)
                    k.Cylinder(o + new Vector3(side * 0.06f, 0.07f, -0.05f), o + new Vector3(side * 0.07f, 0.11f, 0.0f), 0.012f, 5, radiusB: 0.004f);
                break;
            case "towerJawTooth":
                // Tower Jaw's tooth (note 718): one of its iron-dark incisors, long and chisel-ended, the root still splintered.
                k.Use("rust_heavy", new Vector3(0.25f, 0.2f, 0.16f), 0.4f, 0.5f, tile: 0.4f);
                k.Cylinder(o + new Vector3(-0.14f, 0.035f, 0), o + new Vector3(0.12f, 0.03f, 0), 0.03f, 6, radiusB: 0.022f);
                k.Quad(o + new Vector3(0.12f, 0.055f, -0.02f), o + new Vector3(0.12f, 0.055f, 0.02f), o + new Vector3(0.17f, 0.012f, 0.02f), o + new Vector3(0.17f, 0.012f, -0.02f), twoSided: true);
                k.Use("wood_grey", new Vector3(0.5f, 0.46f, 0.4f), 0.6f, 0.1f, tile: 0.3f);
                for (int i = 0; i < 3; i++)
                    k.Cylinder(o + new Vector3(-0.14f, 0.035f, 0), o + new Vector3(-0.19f - i * 0.01f, 0.04f + i * 0.01f, -0.02f + i * 0.02f), 0.006f, 4);
                break;
            case "glassware":
                // A crate of glass (note 719): a shallow open crate in pale deal, straw in it, and six bottles and jars stood
                // in the straw, green and clear. Knocked, the bottles' glass cracks; smashed, half of them are stumps and the
                // shards are on the floor round it.
                k.Use("wood_crate", new Vector3(0.62f, 0.5f, 0.32f), 0.4f, 0.05f, tile: 0.25f);
                k.Box(o + new Vector3(-0.15f, 0, -0.1f), o + new Vector3(0.15f, 0.015f, 0.1f));
                foreach (float z in new[] { -0.1f, 0.1f })
                    k.Box(o + new Vector3(-0.15f, 0, z - 0.008f), o + new Vector3(0.15f, 0.08f, z + 0.008f));
                foreach (float x in new[] { -0.15f, 0.15f })
                    k.Box(o + new Vector3(x - 0.008f, 0, -0.1f), o + new Vector3(x + 0.008f, 0.08f, 0.1f));
                k.Use("hay", new Vector3(0.72f, 0.62f, 0.32f), 0.6f, 0, tile: 0.3f);
                k.Box(o + new Vector3(-0.14f, 0.015f, -0.09f), o + new Vector3(0.14f, 0.05f, 0.09f), top: true, bottom: false);
                k.Use("glass_dirty", new Vector3(0.45f, 0.62f, 0.5f), 0, 0.9f, tile: 0.25f);
                for (int i = 0; i < 6; i++)
                {
                    var at = o + new Vector3(-0.1f + i % 3 * 0.1f, 0.02f, i < 3 ? -0.045f : 0.045f);
                    bool stump = knocks >= 2 && i % 2 == 0;
                    float h = stump ? 0.05f : i % 2 == 0 ? 0.16f : 0.11f;
                    k.Lathe(at, i % 2 == 0 && !stump
                        ? [new(0.028f, 0), new(0.03f, 0.02f), new(0.03f, h * 0.6f), new(0.012f, h * 0.8f), new(0.011f, h)]
                        : [new(0.032f, 0), new(0.034f, 0.01f), new(0.034f, h)], 8, smooth: true, capTop: !stump);
                    if (knocks >= 1 && !stump)
                    {
                        k.Use("paint_black", new Vector3(0.05f, 0.05f, 0.05f), 0.2f, 0.3f, tile: 0.5f);
                        Crack(k, at + new Vector3(0.031f, h * 0.2f, 0), at + new Vector3(0.033f, h * 0.55f, 0.008f), Vector3.UnitX);
                        k.Use("glass_dirty", new Vector3(0.45f, 0.62f, 0.5f), 0, 0.9f, tile: 0.25f);
                    }
                }
                if (knocks >= 2)
                    Shards(k, o, 0.22f, 3);
                break;
            case "decanter":
                // A cut-glass decanter (note 719), squat, its stopper a glass ball, catching any lamp. Chipped, its stopper's
                // gone and a crack runs down it; broken, its neck's off and lying by it in shards.
                k.Use("glass_dirty", new Vector3(0.82f, 0.86f, 0.88f), 0, 1f, tile: 0.25f);
                float neck = knocks >= 2 ? 0.1f : 0.2f;
                k.Lathe(o, knocks >= 2
                    ? [new(0.05f, 0), new(0.07f, 0.03f), new(0.072f, 0.07f), new(0.05f, 0.1f)]
                    : [new(0.05f, 0), new(0.07f, 0.03f), new(0.072f, 0.07f), new(0.05f, 0.12f), new(0.02f, 0.16f), new(0.02f, neck)], 12, smooth: false, capTop: knocks < 2);
                k.Use("brass", new Vector3(0.75f, 0.6f, 0.3f), 0.3f, 0.8f, tile: 0.25f);
                Band(k, o, 0.073f, 0.065f, 0.075f, "brass", 12);
                if (knocks == 0)
                {
                    k.Use("glass_dirty", new Vector3(0.82f, 0.86f, 0.88f), 0, 1f, tile: 0.25f);
                    k.Lathe(o + new Vector3(0, neck, 0), [new(0.012f, 0), new(0.03f, 0.025f), new(0.012f, 0.05f), new(0.001f, 0.052f)], 8, smooth: true);
                }
                else
                {
                    k.Use("paint_black", new Vector3(0.05f, 0.05f, 0.05f), 0.2f, 0.3f, tile: 0.5f);
                    Crack(k, o + new Vector3(0.071f, 0.02f, 0), o + new Vector3(0.069f, 0.09f, 0.02f), Vector3.UnitX, 0.005f);
                    Crack(k, o + new Vector3(0, 0.03f, 0.071f), o + new Vector3(-0.02f, 0.08f, 0.068f), Vector3.UnitZ, 0.005f);
                }
                if (knocks >= 2)
                {
                    k.Use("glass_dirty", new Vector3(0.82f, 0.86f, 0.88f), 0, 1f, tile: 0.25f);
                    k.Cylinder(o + new Vector3(0.1f, 0.02f, 0.04f), o + new Vector3(0.19f, 0.02f, 0.07f), 0.02f, 8, radiusB: 0.015f);
                    Shards(k, o + new Vector3(0.12f, 0, 0), 0.08f, 5);
                }
                break;
            case "mantelClock":
                // A mantel clock (note 719): a walnut case with an arched top on a plinth, its white dial behind a brass bezel.
                // Dented, its case is knocked askew and the dial's glass crazed; smashed, the glass is out and the arch split off.
                float tilt = knocks >= 1 ? 0.12f : 0;
                k.Push(Matrix4x4.CreateRotationZ(tilt) * Matrix4x4.CreateTranslation(o));
                k.Use("wood_crate", new Vector3(0.32f, 0.17f, 0.08f), 0.2f, 0.4f, tile: 0.2f);
                k.Box(new Vector3(-0.13f, 0, -0.06f), new Vector3(0.13f, 0.03f, 0.06f));
                k.Box(new Vector3(-0.11f, 0.03f, -0.05f), new Vector3(0.11f, 0.17f, 0.05f));
                if (knocks < 2)
                    k.Cylinder(new Vector3(0, 0.17f, -0.05f), new Vector3(0, 0.17f, 0.05f), 0.11f, 14, radiusB: 0.11f);
                else
                    k.Cylinder(new Vector3(0.16f, 0.02f, 0.02f), new Vector3(0.2f, 0.12f, 0.03f), 0.04f, 8);
                k.Use("brass", new Vector3(0.78f, 0.62f, 0.3f), 0.3f, 0.8f, tile: 0.25f);
                k.Cylinder(new Vector3(0, 0.14f, 0.05f), new Vector3(0, 0.14f, 0.058f), 0.068f, 16);
                Wear(k, cream, 0.35f);
                Lid(k, new Vector3(0, 0.14f, 0.059f), Vector3.UnitZ, 0.058f, "pocketWatch", 16);
                if (knocks >= 1)
                {
                    k.Use("paint_black", new Vector3(0.04f, 0.04f, 0.04f), 0.2f, 0.3f, tile: 0.5f);
                    Crack(k, new Vector3(-0.05f, 0.11f, 0.061f), new Vector3(0.03f, 0.19f, 0.061f), Vector3.UnitZ, 0.003f);
                    Crack(k, new Vector3(0.0f, 0.15f, 0.061f), new Vector3(0.05f, 0.1f, 0.061f), Vector3.UnitZ, 0.003f);
                    // The dent in its side.
                    k.BoxAt(new Vector3(0.111f, 0.08f, 0.0f), new Vector3(0.003f, 0.03f, 0.02f));
                }
                k.Pop();
                if (knocks >= 2)
                    Shards(k, o, 0.16f, 7);
                break;
            default:
                // Anything else: a clean bundle in sacking, stencilled.
                LabelledBox(k, o + new Vector3(0, 0.07f, 0), new Vector3(0.13f, 0.07f, 0.09f), "supplies", "supplies");
                break;
        }
        k.Pop();
        return k.Build($"find-{item}-{radius:0.00}-{knocks}");
    }

    /// <summary>
    /// A two-person jackpot (note 719; the director, 14 Oct 2026: "Very big value things should take two to carry"), centred on
    /// its ball (<paramref name="radius"/>: it sits from −radius up), big enough that nobody takes it for a find one person
    /// carries: a safe in green-black iron with its brass dial and handle and a gilt maker's line; a church bell off its frame,
    /// bronze gone green at the lip, upright on its oak headstock; a ship's figurehead off a wreck, a carved woman in white and
    /// blue lying on her back, her arms crossed on her breast, the wood showing through.
    /// </summary>
    public static MeshAsset Jackpot(Look? look, string item, float radius)
    {
        var k = new Kit(look, 1132);
        var o = new Vector3(0, -radius, 0);
        switch (item)
        {
            case "safe":
                {
                    float w = radius * 0.85f, h = radius * 1.9f, d = radius * 0.8f;
                    k.Use("paint_black", new Vector3(0.06f, 0.11f, 0.08f), 0.5f, 0.5f, tile: 0.5f);
                    foreach (float x in new[] { -w * 0.8f, w * 0.8f })
                        foreach (float z in new[] { -d * 0.8f, d * 0.8f })
                            k.BoxAt(o + new Vector3(x, 0.03f, z), new Vector3(0.04f, 0.03f, 0.04f));
                    k.Box(o + new Vector3(-w, 0.06f, -d), o + new Vector3(w, h, d));
                    // Its door, a panel stood out of its front, and the hinges down one side.
                    k.Use("iron_plate", new Vector3(0.08f, 0.13f, 0.1f), 0.4f, 0.6f, tile: 0.4f);
                    k.Box(o + new Vector3(-w * 0.85f, 0.14f, d), o + new Vector3(w * 0.85f, h - 0.08f, d + 0.025f));
                    k.Use("brass", new Vector3(0.78f, 0.62f, 0.3f), 0.3f, 0.85f, tile: 0.25f);
                    k.Cylinder(o + new Vector3(0, h * 0.62f, d + 0.025f), o + new Vector3(0, h * 0.62f, d + 0.06f), 0.07f, 16);
                    k.Cylinder(o + new Vector3(0, h * 0.62f, d + 0.06f), o + new Vector3(0, h * 0.62f, d + 0.08f), 0.025f, 8);
                    k.Rod(o + new Vector3(w * 0.45f, h * 0.38f, d + 0.06f), o + new Vector3(w * 0.45f, h * 0.52f, d + 0.06f), 0.014f, 6);
                    k.Rod(o + new Vector3(w * 0.45f, h * 0.38f, d + 0.025f), o + new Vector3(w * 0.45f, h * 0.38f, d + 0.06f), 0.01f, 6);
                    k.Rod(o + new Vector3(w * 0.45f, h * 0.52f, d + 0.025f), o + new Vector3(w * 0.45f, h * 0.52f, d + 0.06f), 0.01f, 6);
                    foreach (float y in new[] { h * 0.28f, h * 0.78f })
                        k.Cylinder(o + new Vector3(-w * 0.86f, y - 0.05f, d + 0.02f), o + new Vector3(-w * 0.86f, y + 0.05f, d + 0.02f), 0.02f, 8);
                    // The gilt maker's line across the top of the door, and a gilt rule round its panel.
                    k.Box(o + new Vector3(-w * 0.6f, h - 0.2f, d + 0.026f), o + new Vector3(w * 0.6f, h - 0.17f, d + 0.03f));
                    k.Box(o + new Vector3(-w * 0.75f, 0.2f, d + 0.026f), o + new Vector3(w * 0.75f, 0.215f, d + 0.03f));
                    break;
                }
            case "churchBell":
                {
                    float lip = radius * 0.95f, h = radius * 1.75f;
                    k.Use("brass", new Vector3(0.55f, 0.38f, 0.16f), 0.5f, 0.7f, tile: 0.3f);
                    k.Lathe(o, [new(lip, 0), new(lip * 0.98f, 0.05f), new(lip * 0.82f, h * 0.18f), new(lip * 0.62f, h * 0.45f), new(lip * 0.56f, h * 0.75f),
                    new(lip * 0.5f, h * 0.88f), new(lip * 0.3f, h * 0.95f), new(0.001f, h * 0.96f)], 18, smooth: true);
                    // Verdigris round the lip, where the weather got in.
                    k.Use("paint_oxide", new Vector3(0.28f, 0.5f, 0.42f), 0.8f, 0.2f, tile: 0.3f);
                    k.Cylinder(o + new Vector3(0, 0.0f, 0), o + new Vector3(0, 0.06f, 0), lip * 1.01f, 18, caps: false, radiusB: lip * 0.97f);
                    k.Use("brass", new Vector3(0.6f, 0.43f, 0.18f), 0.4f, 0.8f, tile: 0.3f);
                    k.Cylinder(o + new Vector3(0, h * 0.3f, 0), o + new Vector3(0, h * 0.33f, 0), lip * 0.75f, 18, caps: false, radiusB: lip * 0.73f);
                    // Its oak headstock across the crown, with the iron straps that held it.
                    k.Use("wood_grey", new Vector3(0.36f, 0.27f, 0.18f), 0.7f, 0.1f, tile: 0.3f);
                    k.Box(o + new Vector3(-lip * 1.1f, h * 0.95f, -0.08f), o + new Vector3(lip * 1.1f, h * 0.95f + 0.14f, 0.08f));
                    k.Use("rust_heavy", new Vector3(0.22f, 0.18f, 0.16f), 0.8f, 0.3f);
                    foreach (float x in new[] { -0.12f, 0.12f })
                        k.Box(o + new Vector3(x - 0.02f, h * 0.8f, -0.085f), o + new Vector3(x + 0.02f, h * 0.95f + 0.15f, 0.085f));
                    break;
                }
            default:
                {
                    // The figurehead: carved standing (up +y, her front +z) and laid on her back, her face to the sky and her head
                    // along the ground, the scroll she stood out from at her feet: blue skirts, a white bodice, her arms crossed on
                    // it, her hair gilt.
                    k.Push(Matrix4x4.CreateRotationX(-MathF.PI / 2) * Matrix4x4.CreateTranslation(o + new Vector3(0, 0.25f, 0.66f)));
                    var blue = new Vector3(0.32f, 0.45f, 0.85f);
                    var white = new Vector3(0.95f, 0.93f, 0.88f);
                    k.Use("wood_grey", new Vector3(0.42f, 0.32f, 0.2f), 0.7f, 0.1f, tile: 0.3f);
                    k.Lathe(Vector3.Zero, [new(0.18f, 0), new(0.22f, 0.06f), new(0.2f, 0.12f)], 12, smooth: true);
                    k.Use("clapboard", blue * 0.5f, 0.5f, 0.25f, tile: 0.3f);
                    k.Tint = blue;
                    k.Lathe(new Vector3(0, 0.12f, 0), [new(0.21f, 0), new(0.24f, 0.08f), new(0.2f, 0.4f), new(0.15f, 0.68f)], 12, smooth: true);
                    k.Tint = white;
                    k.Lathe(new Vector3(0, 0.8f, 0), [new(0.15f, 0), new(0.165f, 0.12f), new(0.12f, 0.27f), new(0.05f, 0.3f)], 12, smooth: true);
                    k.Cylinder(new Vector3(-0.15f, 0.92f, 0.1f), new Vector3(0.1f, 0.98f, 0.15f), 0.04f, 6);
                    k.Cylinder(new Vector3(0.15f, 0.92f, 0.1f), new Vector3(-0.1f, 0.98f, 0.15f), 0.04f, 6);
                    k.Cylinder(new Vector3(0, 1.08f, 0), new Vector3(0, 1.14f, 0), 0.045f, 8);
                    k.Lathe(new Vector3(0, 1.12f, 0), [new(0.05f, 0), new(0.085f, 0.05f), new(0.09f, 0.12f), new(0.065f, 0.18f), new(0.001f, 0.21f)], 12, smooth: true);
                    k.Tint = Vector3.One;
                    k.Use("brass", new Vector3(0.75f, 0.58f, 0.25f), 0.5f, 0.6f, tile: 0.3f);
                    k.Cylinder(new Vector3(0, 1.23f, -0.01f), new Vector3(0, 1.3f, -0.02f), 0.093f, 12, radiusB: 0.07f);
                    k.Box(new Vector3(-0.08f, 0.98f, -0.1f), new Vector3(0.08f, 1.26f, -0.05f));
                    k.Pop();
                    break;
                }
        }
        return k.Build($"jackpot-{item}-{radius:0.00}");
    }
}
