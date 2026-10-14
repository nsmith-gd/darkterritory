using System.Numerics;
using Ballast.Render;

namespace DarkTerritory.Game.Art;

/// <summary>
/// The whimsies' pieces (ARCHITECTURE §8 note 715; docs/design/upgrades.md §8.1's first tell: a thing on the train you can
/// point at): Gerald, a garden gnome, the canary in its cage, the gramophone on its table, the calliope's pipes. Each from its
/// foot at (0, 0, 0), facing −Z.
/// </summary>
public static class WhimsyKit
{
    static readonly Vector3 Brass = new(0.82f, 0.63f, 0.28f), Straw = new(0.85f, 0.72f, 0.38f), Coat = new(0.16f, 0.2f, 0.28f),
        Burlap = new(0.62f, 0.5f, 0.33f), Black = new(0.05f, 0.05f, 0.06f), Wood = new(0.4f, 0.25f, 0.13f);

    /// <summary>A ball of <paramref name="r"/> about <paramref name="centre"/>, turned on a lathe.</summary>
    static void Ball(Kit k, Vector3 centre, float r, int sides = 10)
    {
        var profile = new List<Vector2>();
        for (int i = 0; i <= 8; i++)
        {
            float t = MathF.PI * i / 8;
            profile.Add(new Vector2(MathF.Max(1e-4f, r * MathF.Sin(t)), r - r * MathF.Cos(t)));
        }
        k.Lathe(centre - new Vector3(0, r, 0), profile, sides, capTop: false);
    }

    /// <summary>
    /// Gerald (note 715): a straw dummy in a crew coat on a post, arms out on a crossbar like a scarecrow's, a burlap sack for a
    /// head with two stitched crosses for eyes and a crew cap on it, straw at his cuffs and collar. 1.6 m tall.
    /// </summary>
    public static MeshAsset Gerald(Look? look)
    {
        var k = new Kit(look, 1730);
        k.Use("dress_wood", Wood, 0.7f, 0);
        k.Box(new Vector3(-0.25f, 0, -0.25f), new Vector3(0.25f, 0.05f, 0.25f));
        k.Cylinder(new Vector3(0, 0, 0), new Vector3(0, 1.2f, 0), 0.03f, 6);
        // The coat: a body and the arms out along the crossbar.
        k.Use("dress_cloth", Coat, 0.85f, 0);
        k.BevelBox(new Vector3(-0.2f, 0.65f, -0.12f), new Vector3(0.2f, 1.25f, 0.12f), 0.04f);
        k.Box(new Vector3(-0.6f, 1.1f, -0.08f), new Vector3(0.6f, 1.24f, 0.08f));
        k.Use("dress_paint", new Vector3(0.75f, 0.62f, 0.3f), 0.5f, 0.6f);
        foreach (float y in new[] { 0.8f, 0.95f, 1.1f })
            k.Box(new Vector3(-0.015f, y, -0.125f), new Vector3(0.015f, y + 0.03f, -0.121f));
        // Straw out of the cuffs, the hem and the collar.
        k.Use("dress_straw", Straw, 0.9f, 0);
        foreach (int side in new[] { -1, 1 })
            for (int i = 0; i < 4; i++)
                k.Rod(new Vector3(side * 0.6f, 1.17f, -0.03f + 0.02f * i), new Vector3(side * (0.74f + 0.02f * i), 1.1f + 0.03f * i, -0.05f + 0.035f * i), 0.008f);
        for (int i = 0; i < 6; i++)
            k.Rod(new Vector3(-0.15f + 0.06f * i, 0.66f, 0), new Vector3(-0.17f + 0.07f * i, 0.52f, 0.02f * (i % 3 - 1)), 0.008f);
        // The sack head, tied off at the neck.
        k.Use("dress_burlap", Burlap, 0.95f, 0);
        Ball(k, new Vector3(0, 1.4f, 0), 0.15f);
        k.Use("dress_string", new Vector3(0.5f, 0.42f, 0.3f), 0.9f, 0);
        k.Cylinder(new Vector3(0, 1.25f, 0), new Vector3(0, 1.29f, 0), 0.06f, 8);
        // The eyes: two stitched crosses, and a stitched grin.
        k.Use("dress_paint", Black, 0.6f, 0);
        foreach (float x in new[] { -0.055f, 0.055f })
        {
            k.Rod(new Vector3(x - 0.025f, 1.44f, -0.142f), new Vector3(x + 0.025f, 1.40f, -0.142f), 0.006f);
            k.Rod(new Vector3(x - 0.025f, 1.40f, -0.142f), new Vector3(x + 0.025f, 1.44f, -0.142f), 0.006f);
        }
        k.Rod(new Vector3(-0.05f, 1.35f, -0.14f), new Vector3(0.05f, 1.35f, -0.14f), 0.005f);
        // The crew cap.
        k.Use("dress_cloth", Coat * 0.8f, 0.8f, 0);
        k.Cylinder(new Vector3(0, 1.5f, 0.01f), new Vector3(0, 1.59f, 0.01f), 0.13f, 12);
        k.Box(new Vector3(-0.1f, 1.5f, -0.22f), new Vector3(0.1f, 1.52f, -0.1f));
        return k.Build("whimsy-gerald");
    }

    /// <summary>A garden gnome (note 715): 0.45 m, its red hat, white beard, blue coat and brown boots; a little fishing rod.</summary>
    public static MeshAsset Gnome(Look? look, int variant)
    {
        var k = new Kit(look, 1731);
        Vector3[] coats = [new(0.2f, 0.32f, 0.62f), new(0.25f, 0.5f, 0.25f), new(0.55f, 0.35f, 0.15f)];
        k.Use("dress_paint", new Vector3(0.3f, 0.3f, 0.3f), 0.6f, 0.1f);
        k.Cylinder(new Vector3(0, 0, 0), new Vector3(0, 0.03f, 0), 0.11f, 10);
        k.Use("dress_paint", new Vector3(0.3f, 0.18f, 0.08f), 0.5f, 0.3f);
        k.Box(new Vector3(-0.07f, 0.03f, -0.07f), new Vector3(0.07f, 0.07f, 0.04f));
        k.Use("dress_paint", coats[variant % coats.Length], 0.4f, 0.4f);
        k.Cylinder(new Vector3(0, 0.07f, 0), new Vector3(0, 0.25f, 0), 0.085f, 10, radiusB: 0.07f);
        k.Use("dress_paint", new Vector3(0.95f, 0.72f, 0.6f), 0.4f, 0.3f);
        Ball(k, new Vector3(0, 0.29f, -0.01f), 0.055f);
        Ball(k, new Vector3(0, 0.285f, -0.065f), 0.017f, 6);
        k.Use("dress_paint", new Vector3(0.95f, 0.94f, 0.9f), 0.5f, 0.3f);
        k.Cylinder(new Vector3(0, 0.27f, -0.04f), new Vector3(0, 0.17f, -0.07f), 0.05f, 8, radiusB: 0.005f);
        k.Use("dress_paint", new Vector3(0.82f, 0.12f, 0.1f), 0.4f, 0.4f);
        k.Cylinder(new Vector3(0, 0.32f, 0), new Vector3(0, 0.46f, 0.03f), 0.06f, 10, radiusB: 0.004f);
        k.Use("dress_wood", Wood, 0.6f, 0);
        k.Rod(new Vector3(0.07f, 0.17f, -0.05f), new Vector3(0.18f, 0.42f, -0.25f), 0.004f);
        return k.Build($"whimsy-gnome-{variant % coats.Length}");
    }

    /// <summary>
    /// The canary's brass cage (note 715), hung from its hook at (0, 0, 0): a domed cage of bars 0.32 m tall, its perch, and
    /// the bird on the perch, or (<paramref name="dead"/>) on its back on the cage's floor.
    /// </summary>
    public static MeshAsset Cage(Look? look, bool dead)
    {
        var k = new Kit(look, 1732);
        k.Use("dress_brass", Brass, 0.25f, 0.95f);
        const float r = 0.13f, floor = -0.42f, top = -0.14f;
        k.Rod(new Vector3(0, 0, 0), new Vector3(0, -0.06f, 0), 0.004f);
        Ball(k, new Vector3(0, -0.07f, 0), 0.012f, 6);
        const int bars = 14;
        for (int i = 0; i < bars; i++)
        {
            float a = MathF.Tau * i / bars;
            var foot = new Vector3(r * MathF.Cos(a), floor, r * MathF.Sin(a));
            var shoulder = foot with { Y = top };
            k.Rod(foot, shoulder, 0.0025f, 3);
            k.Rod(shoulder, new Vector3(0, -0.075f, 0), 0.0025f, 3);
        }
        k.Cylinder(new Vector3(0, floor - 0.02f, 0), new Vector3(0, floor, 0), r + 0.01f, 16);
        k.Cylinder(new Vector3(0, top - 0.005f, 0), new Vector3(0, top + 0.005f, 0), r + 0.002f, 16, caps: false);
        k.Use("dress_wood", Wood, 0.6f, 0);
        k.Rod(new Vector3(-r, -0.31f, 0), new Vector3(r, -0.31f, 0), 0.005f, 4);
        // The canary: yellow, its beak orange, its eye black.
        var yellow = new Vector3(0.98f, 0.82f, 0.12f);
        k.Push(dead ? Matrix4x4.CreateRotationZ(MathF.PI * 0.95f) * Kit.At(0, floor + 0.035f, 0) : Kit.At(0, -0.305f, 0));
        k.Use("dress_paint", yellow, 0.3f, 0.2f);
        k.With(Matrix4x4.CreateScale(1, 1, 1.5f) * Kit.At(0, 0.03f, 0.005f), () => Ball(k, Vector3.Zero, 0.025f, 8));
        Ball(k, new Vector3(0, 0.065f, -0.03f), 0.017f, 8);
        k.Box(new Vector3(-0.008f, 0.02f, 0.03f), new Vector3(0.008f, 0.03f, 0.07f));
        k.Use("dress_paint", new Vector3(0.95f, 0.5f, 0.1f), 0.3f, 0.2f);
        k.Cylinder(new Vector3(0, 0.065f, -0.045f), new Vector3(0, 0.062f, -0.062f), 0.006f, 5, radiusB: 0.001f);
        k.Use("dress_paint", Black, 0.3f, 0.6f);
        foreach (float x in new[] { -0.014f, 0.014f })
            Ball(k, new Vector3(x, 0.07f, -0.038f), 0.003f, 4);
        k.Pop();
        return k.Build(dead ? "whimsy-cage-dead" : "whimsy-cage");
    }

    /// <summary>
    /// The gramophone (note 715) on its little table: a walnut cabinet, the turntable and its black record, the tone arm, and the
    /// brass horn flaring up and out over the aisle (+X). The table's foot at (0, 0, 0), its top at <paramref name="table"/>.
    /// </summary>
    public static MeshAsset Gramophone(Look? look, float table)
    {
        var k = new Kit(look, 1733);
        k.Use("dress_wood", Wood, 0.6f, 0.2f);
        k.Box(new Vector3(-0.22f, table - 0.04f, -0.22f), new Vector3(0.22f, table, 0.22f));
        foreach (int x in new[] { -1, 1 })
            foreach (int z in new[] { -1, 1 })
                k.Box(new Vector3(x * 0.18f - 0.02f, 0, z * 0.18f - 0.02f), new Vector3(x * 0.18f + 0.02f, table - 0.04f, z * 0.18f + 0.02f));
        k.Use("dress_wood", new Vector3(0.32f, 0.17f, 0.08f), 0.4f, 0.4f);
        k.Box(new Vector3(-0.17f, table, -0.17f), new Vector3(0.17f, table + 0.14f, 0.17f));
        k.Use("dress_brass", Brass, 0.3f, 0.9f);
        k.Rod(new Vector3(0.17f, table + 0.07f, 0.05f), new Vector3(0.22f, table + 0.07f, 0.05f), 0.006f);
        k.Use("dress_paint", Black, 0.2f, 0.5f);
        k.Cylinder(new Vector3(0, table + 0.14f, 0), new Vector3(0, table + 0.15f, 0), 0.14f, 20);
        k.Use("dress_paint", new Vector3(0.7f, 0.15f, 0.12f), 0.4f, 0.2f);
        k.Cylinder(new Vector3(0, table + 0.15f, 0), new Vector3(0, table + 0.152f, 0), 0.04f, 12);
        // The tone arm and the horn: up from the cabinet's back corner, out over the aisle.
        k.Use("dress_brass", Brass, 0.25f, 0.95f);
        var socket = new Vector3(-0.12f, table + 0.14f, 0.12f);
        k.Cylinder(socket, socket + new Vector3(0, 0.08f, 0), 0.02f, 8);
        k.Rod(socket + new Vector3(0, 0.07f, 0), new Vector3(0.06f, table + 0.17f, -0.04f), 0.006f);
        var neck = socket + new Vector3(0, 0.08f, 0);
        var mouth = neck + new Vector3(0.28f, 0.32f, -0.06f);
        k.Cylinder(neck, Vector3.Lerp(neck, mouth, 0.5f), 0.02f, 10, radiusB: 0.06f, caps: false);
        k.Cylinder(Vector3.Lerp(neck, mouth, 0.5f), mouth, 0.06f, 14, radiusB: 0.2f, caps: false);
        k.Use("dress_brass", Brass * 0.4f, 0.4f, 0.6f);
        k.Cylinder(Vector3.Lerp(neck, mouth, 0.5f), mouth, 0.055f, 14, radiusB: 0.195f, caps: false, smooth: true);
        return k.Build($"whimsy-gramophone-{table:0.00}");
    }

    /// <summary>A white fleck on the record, put where it's got to as it turns (the record playing).</summary>
    public static MeshAsset Label(Look? look)
    {
        var k = new Kit(look, 1735);
        k.Use("dress_paint", new Vector3(0.95f, 0.92f, 0.8f), 0.3f, 0.3f);
        k.Box(new Vector3(-0.012f, 0, -0.012f), new Vector3(0.012f, 0.003f, 0.012f));
        return k.Build("whimsy-label");
    }

    /// <summary>Where the gramophone's horn opens (the sound comes from), in its own frame.</summary>
    public static Vector3 Horn(float table) => new(0.16f, table + 0.54f, 0.06f);

    /// <summary>
    /// The steam calliope (note 715): eight brass pipes stepped down in height on a manifold along the boiler's top, each with
    /// its mouth cut near its foot; the manifold from (0, 0, 0) back along +Z.
    /// </summary>
    public static MeshAsset Calliope(Look? look)
    {
        var k = new Kit(look, 1734);
        k.Use("dress_brass", Brass * 0.85f, 0.3f, 0.9f);
        k.Cylinder(new Vector3(0, 0.04f, -0.08f), new Vector3(0, 0.04f, 0.78f), 0.05f, 10);
        for (int i = 0; i < 8; i++)
        {
            float z = i * 0.1f, h = 0.75f - i * 0.06f, r = 0.032f - i * 0.0018f;
            k.Use("dress_brass", Brass, 0.25f, 0.95f);
            k.Cylinder(new Vector3(0, 0.08f, z), new Vector3(0, 0.08f + h, z), r, 10);
            k.Cylinder(new Vector3(0, 0.04f, z), new Vector3(0, 0.1f, z), r * 0.7f, 8);
            k.Use("dress_paint", Black, 0.4f, 0.3f);
            k.Box(new Vector3(-r * 0.6f, 0.14f, z - r - 0.002f), new Vector3(r * 0.6f, 0.17f, z - r + 0.004f));
        }
        return k.Build("whimsy-calliope");
    }

    /// <summary>The calliope's pipe for the <paramref name="note"/>th note's height (the tune's ups and downs): where its steam comes out.</summary>
    public static Vector3 Pipe(int pipe) => new(0, 0.08f + 0.75f - pipe * 0.06f, pipe * 0.1f);
}
