using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Game;

/// <summary>
/// The odd things in the scene (queue #464, ARCHITECTURE §8 note 730): a tin can lying in a village's road (where it was last
/// kicked to), a scarecrow at a farmstead (turned to the line once it's been found so), a red postbox, and the bell rope by a
/// walled town's council house door. A village's well is the stop's own art (WorldArt). First-pass art: kit pieces, read at
/// night by their silhouettes.
/// </summary>
public sealed partial class GreyboxScene
{
    /// <summary>The night's odd things at the stops (World.Oddities, note 730); null: none drawn.</summary>
    public Oddities? Oddities { get; set; }

    MeshAsset? _can, _scarecrow, _postbox, _bellRope;

    void OddThings(MeshBuilder mesh, RailLine line, Double3 eye)
    {
        if (Oddities is { } odd)
            foreach (var thing in odd.Things)
            {
                var at = odd.At(thing.Index);
                if ((at - eye).Length > DrawDistance || thing.Kind == OddThingKind.Well)
                    continue;
                var facing = odd.Facing(thing.Index, line);
                var asset = thing.Kind switch
                {
                    OddThingKind.Can => _can ??= Can(Look),
                    OddThingKind.Scarecrow => _scarecrow ??= Scarecrow(Look),
                    _ => _postbox ??= Postbox(Look),
                };
                // A can lies however its last kick left it: turned by where it came to rest.
                float yaw = thing.Kind == OddThingKind.Can ? (float)((at.X * 3.1 + at.Z * 1.7) % (2 * Math.PI)) : Yaw(facing);
                mesh.Append(asset, Matrix4x4.CreateRotationY(yaw) * Matrix4x4.CreateTranslation(V(at, eye)));
            }
        foreach (var town in new[] { Town, Arrival })
            if (town?.BellRope is { } rope && (rope - eye).Length < DrawDistance)
                mesh.Append(_bellRope ??= BellRope(Look), Matrix4x4.CreateTranslation(V(rope, eye)));
    }

    /// <summary>The yaw that turns a piece's front (−Z) to <paramref name="facing"/> (level).</summary>
    static float Yaw(Double3 facing) => (float)Math.Atan2(-facing.X, -facing.Z);

    /// <summary>A tin can on its side in the road, its label long gone to rust, its lid hanging open.</summary>
    static MeshAsset Can(Look? look)
    {
        var k = new Kit(look, 7301);
        k.Use("rust_heavy", new Vector3(0.55f, 0.42f, 0.32f), 0.6f, 0.5f, tile: 0.3f);
        k.Cylinder(new Vector3(-0.06f, 0.042f, 0), new Vector3(0.06f, 0.042f, 0), 0.042f, 10);
        // The lid, bent back on its last bit of rim.
        k.Use("rail_steel", new Vector3(0.62f, 0.6f, 0.58f), 0.35f, 0.8f, tile: 0.3f);
        k.Disc(new Vector3(0.075f, 0.06f, 0.025f), Vector3.Normalize(new Vector3(0.6f, 0.3f, 0.7f)), 0.04f, 10);
        return k.Build("odd-can");
    }

    /// <summary>
    /// A scarecrow on its post: a sacking head under a farmer's old hat, a coat on the crossbar with its sleeves hanging, straw
    /// at the cuffs and the collar; two stitched eyes and a mouth on the sacking's front (−Z), so which way it looks reads.
    /// </summary>
    static MeshAsset Scarecrow(Look? look)
    {
        var k = new Kit(look, 7302);
        k.Use("wood_grey", new Vector3(0.42f, 0.36f, 0.28f), 0.9f, 0, tile: 1);
        k.Rod(new Vector3(0, -0.3f, 0), new Vector3(0, 1.95f, 0), 0.05f);
        k.Rod(new Vector3(-0.75f, 1.48f, 0), new Vector3(0.75f, 1.48f, 0), 0.04f);
        // The coat: a dark wool body over the post, its sleeves down off the bar, ragged short.
        k.Use("cap", new Vector3(0.22f, 0.2f, 0.17f), 0.95f, 0, tile: 1);
        k.Box(new Vector3(-0.24f, 0.78f, -0.13f), new Vector3(0.24f, 1.55f, 0.13f));
        foreach (float x in new[] { -1f, 1f })
            k.Box(new Vector3(x * 0.24f, 1.18f, -0.09f) + new Vector3(x < 0 ? -0.46f : 0, 0, 0), new Vector3(x * 0.24f, 1.53f, 0.09f) + new Vector3(x > 0 ? 0.46f : 0, 0, 0));
        // Straw out of the cuffs, the hem and the collar.
        k.Use("wood_grey", new Vector3(0.74f, 0.62f, 0.34f), 0.9f, 0, tile: 0.5f);
        k.Tint = new Vector3(1.2f, 1.05f, 0.6f);
        foreach (float x in new[] { -0.74f, 0.74f })
            for (int i = 0; i < 4; i++)
                k.Rod(new Vector3(x, 1.3f, 0), new Vector3(x + (x < 0 ? -0.04f : 0.04f) * (i - 1), 1.12f - i * 0.02f, (i - 1.5f) * 0.03f), 0.008f);
        for (int i = 0; i < 6; i++)
            k.Rod(new Vector3(-0.2f + i * 0.08f, 0.8f, -0.1f), new Vector3(-0.22f + i * 0.09f, 0.66f, -0.13f), 0.008f);
        k.Tint = Vector3.One;
        // The head: a stuffed sack, tied off at the neck.
        k.Use("cap", new Vector3(0.62f, 0.54f, 0.4f), 0.95f, 0, tile: 1);
        k.Lathe(new Vector3(0, 1.55f, 0), [new(0.05f, 0), new(0.15f, 0.08f), new(0.17f, 0.2f), new(0.14f, 0.32f), new(0.04f, 0.38f)], 10, smooth: true);
        // Its stitched face, to the front.
        k.Use("paint_black", new Vector3(0.05f), 0.8f, 0);
        foreach (float x in new[] { -0.06f, 0.06f })
            k.Box(new Vector3(x - 0.025f, 1.79f, -0.172f), new Vector3(x + 0.025f, 1.82f, -0.16f));
        k.Box(new Vector3(-0.06f, 1.69f, -0.165f), new Vector3(0.06f, 1.705f, -0.15f));
        // The hat: a wide brim and a dented crown.
        k.Use("cap", new Vector3(0.16f, 0.14f, 0.12f), 0.9f, 0, tile: 1);
        k.Cylinder(new Vector3(0, 1.88f, 0), new Vector3(0, 1.9f, 0), 0.3f, 14);
        k.Cylinder(new Vector3(0, 1.9f, 0), new Vector3(0, 2.06f, 0), 0.16f, 12, radiusB: 0.14f);
        return k.Build("odd-scarecrow");
    }

    /// <summary>A red pillar postbox on its plinth, a domed cap, its slot to the front (−Z) under a little hood.</summary>
    static MeshAsset Postbox(Look? look)
    {
        var k = new Kit(look, 7303);
        k.Use("stone_block", Palette.Charcoal, 0.8f, 0.1f, tile: 1);
        k.Box(new Vector3(-0.3f, -0.2f, -0.3f), new Vector3(0.3f, 0.1f, 0.3f));
        k.Use("paint_oxide", new Vector3(0.62f, 0.08f, 0.06f), 0.45f, 0.3f, tile: 0.5f);
        k.Cylinder(new Vector3(0, 0.1f, 0), new Vector3(0, 1.25f, 0), 0.23f, 14);
        k.Lathe(new Vector3(0, 1.25f, 0), [new(0.26f, 0), new(0.26f, 0.05f), new(0.2f, 0.14f), new(0.08f, 0.2f), new(0, 0.21f)], 14, smooth: true);
        // The slot's hood, and the slot under it, black.
        k.Box(new Vector3(-0.13f, 1.04f, -0.27f), new Vector3(0.13f, 1.07f, -0.2f));
        k.Use("paint_black", new Vector3(0.03f), 0.8f, 0);
        k.Box(new Vector3(-0.11f, 0.99f, -0.235f), new Vector3(0.11f, 1.02f, -0.22f));
        // The collection plate, cream enamel.
        k.Use("paper_form", new Vector3(0.85f, 0.8f, 0.66f), 0.4f, 0.2f, tile: 1);
        k.Box(new Vector3(-0.09f, 0.7f, -0.236f), new Vector3(0.09f, 0.82f, -0.226f));
        return k.Build("odd-postbox");
    }

    /// <summary>The bell's rope down the council house's front: hemp, from under its eaves (the cupola's over them) to a knotted tail at hand height.</summary>
    static MeshAsset BellRope(Look? look)
    {
        var k = new Kit(look, 7304);
        k.Use("wood_grey", new Vector3(0.66f, 0.56f, 0.38f), 0.95f, 0, tile: 0.4f);
        k.Tint = new Vector3(1.15f, 1.0f, 0.7f);
        k.Rod(new Vector3(0, 0.95f, 0), new Vector3(0, 5.7f, 0), 0.016f);
        // The sally: a fat woollen grip a little above the tail, and the tail knotted under it.
        k.Use("cap", new Vector3(0.55f, 0.12f, 0.1f), 0.95f, 0, tile: 1);
        k.Tint = Vector3.One;
        k.Cylinder(new Vector3(0, 1.35f, 0), new Vector3(0, 1.75f, 0), 0.04f, 8);
        k.Use("wood_grey", new Vector3(0.66f, 0.56f, 0.38f), 0.95f, 0, tile: 0.4f);
        k.Cylinder(new Vector3(0, 0.95f, 0), new Vector3(0, 1.02f, 0), 0.03f, 8);
        // Its iron guides on the wall, and up under the eaves where it goes in.
        k.Use("rust_heavy", new Vector3(0.2f, 0.18f, 0.16f), 0.6f, 0.4f, tile: 0.4f);
        foreach (float y in new[] { 3.2f, 5.6f })
            k.Box(new Vector3(-0.04f, y, -0.04f), new Vector3(0.04f, y + 0.06f, 0.12f));
        return k.Build("odd-bellrope");
    }
}
