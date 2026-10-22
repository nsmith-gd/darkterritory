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

    /// <summary>
    /// The hammock (note 744): a canvas sling <paramref name="span"/> m across from hook to hook at shoulder height, sagging to
    /// <see cref="CrewActs.HammockPan"/> over the floor in its middle; across X, its foot (the floor under its middle) at (0, 0, 0).
    /// </summary>
    public static MeshAsset Hammock(Look? look, float span)
    {
        var k = new Kit(look, 1736);
        float half = span / 2, hook = 1.55f, sag = (float)CrewActs.HammockPan - 0.05f;
        k.Use("dress_canvas", new Vector3(0.78f, 0.7f, 0.52f), 0.9f, 0);
        const int n = 12;
        float Y(float x) => sag + (hook - 0.35f - sag) * (x * x) / ((half - 0.35f) * (half - 0.35f));
        for (int i = 0; i < n; i++)
        {
            float x0 = -half + 0.35f + (span - 0.7f) * i / n, x1 = -half + 0.35f + (span - 0.7f) * (i + 1) / n;
            float w0 = 0.38f - 0.25f * MathF.Abs(x0) / half, w1 = 0.38f - 0.25f * MathF.Abs(x1) / half;
            k.Quad(new Vector3(x0, Y(x0), -w0), new Vector3(x1, Y(x1), -w1), new Vector3(x1, Y(x1), w1), new Vector3(x0, Y(x0), w0), twoSided: true);
        }
        // The ropes from each end to its hook on the wall, and the spreaders.
        k.Use("dress_string", new Vector3(0.6f, 0.5f, 0.32f), 0.9f, 0);
        foreach (int side in new[] { -1, 1 })
        {
            var end = new Vector3(side * (half - 0.35f), Y(half - 0.35f), 0);
            var wall = new Vector3(side * half, hook, 0);
            foreach (float z in new[] { -0.12f, 0, 0.12f })
                k.Rod(end with { Z = z }, wall, 0.006f, 3);
            k.Use("dress_wood", Wood, 0.6f, 0);
            k.Rod(end with { Z = -0.14f }, end with { Z = 0.14f }, 0.015f);
            k.Use("dress_brass", Brass * 0.6f, 0.5f, 0.6f);
            k.Cylinder(wall, wall - new Vector3(side * 0.06f, 0, 0), 0.02f, 6);
            k.Use("dress_string", new Vector3(0.6f, 0.5f, 0.32f), 0.9f, 0);
        }
        return k.Build($"whimsy-hammock-{span:0.00}");
    }

    /// <summary>
    /// The hot tub (note 744): a cedar barrel tub <see cref="Sim.Train.CarShape.TubHalf"/> across, banded in iron, its two benches
    /// inside, the water near its brim, and its pipe down over the side to the boiler. Its foot (the roof under its middle) at (0, 0, 0).
    /// </summary>
    public static MeshAsset HotTub(Look? look)
    {
        var k = new Kit(look, 1737);
        float r = (float)Sim.Train.CarShape.TubHalf, h = 0.75f;
        k.Use("dress_wood", new Vector3(0.55f, 0.32f, 0.18f), 0.6f, 0.15f);
        // Staves: an outer and an inner wall, the rim between.
        k.Cylinder(new Vector3(0, 0, 0), new Vector3(0, h, 0), r, 20, caps: false, smooth: false);
        k.Use("dress_wood", new Vector3(0.42f, 0.24f, 0.13f), 0.6f, 0.1f);
        k.Lathe(new Vector3(0, 0.02f, 0), [new Vector2(r - 0.05f, h), new Vector2(r - 0.05f, 0)], 20, smooth: false, capTop: false);
        k.Disc(new Vector3(0, 0.05f, 0), Vector3.UnitY, r - 0.05f, 20);
        k.Use("dress_wood", new Vector3(0.6f, 0.37f, 0.2f), 0.5f, 0.15f);
        k.Lathe(new Vector3(0, h, 0), [new Vector2(r - 0.05f, 0), new Vector2(r + 0.005f, 0)], 20, smooth: false, capTop: false);
        k.Use("dress_paint", Black * 2, 0.5f, 0.6f);
        foreach (float y in new[] { 0.12f, h - 0.14f })
            k.Cylinder(new Vector3(0, y, 0), new Vector3(0, y + 0.05f, 0), r + 0.012f, 20, caps: false);
        // The benches, either side.
        k.Use("dress_wood", new Vector3(0.5f, 0.3f, 0.16f), 0.6f, 0.1f);
        foreach (int side in new[] { -1, 1 })
            k.Box(new Vector3(side < 0 ? -r + 0.05f : r - 0.35f, (float)CrewActs.TubPan - 0.04f, -0.35f), new Vector3(side < 0 ? -r + 0.35f : r - 0.05f, (float)CrewActs.TubPan, 0.35f));
        // The water.
        k.Use("dress_water", new Vector3(0.25f, 0.42f, 0.45f), 0.02f, 0.9f);
        k.Disc(new Vector3(0, h - 0.12f, 0), Vector3.UnitY, r - 0.05f, 20);
        // The pipe off the boiler, over the side.
        k.Use("dress_brass", Brass * 0.8f, 0.4f, 0.8f);
        k.Rod(new Vector3(0, h + 0.05f, r - 0.1f), new Vector3(0, h + 0.05f, r + 0.15f), 0.025f, 6);
        k.Rod(new Vector3(0, h + 0.05f, r + 0.15f), new Vector3(0, 0.02f, r + 0.15f), 0.025f, 6);
        return k.Build("whimsy-hot-tub");
    }

    /// <summary>The fishing rod (note 744): a long cane along −Z from its butt at (0, 0, 0), its reel, and the line off its tip.</summary>
    public static MeshAsset FishingRod(Look? look)
    {
        var k = new Kit(look, 1738);
        k.Use("dress_wood", new Vector3(0.62f, 0.48f, 0.26f), 0.4f, 0.3f);
        k.Cylinder(new Vector3(0, 0, 0), new Vector3(0, 0, -2.2f), 0.012f, 6, radiusB: 0.004f);
        k.Use("dress_cloth", Black * 3, 0.8f, 0);
        k.Cylinder(new Vector3(0, 0, 0), new Vector3(0, 0, -0.3f), 0.016f, 6);
        k.Use("dress_brass", Brass, 0.3f, 0.9f);
        k.Cylinder(new Vector3(-0.03f, -0.03f, -0.22f), new Vector3(0.03f, -0.03f, -0.22f), 0.035f, 10);
        k.Use("dress_string", new Vector3(0.85f, 0.85f, 0.8f), 0.4f, 0.2f);
        k.Rod(new Vector3(0, 0, -2.2f), new Vector3(0, -0.6f, -2.25f), 0.002f, 3);
        return k.Build("whimsy-fishing-rod");
    }

    /// <summary>A trout (note 744): 0.3 m nose (−Z) to tail, silver and speckled; <paramref name="grilled"/>, browned and barred off the grill.</summary>
    public static MeshAsset Fish(Look? look, bool grilled)
    {
        var k = new Kit(look, grilled ? 1740 : 1739);
        k.Use("dress_paint", grilled ? new Vector3(0.55f, 0.35f, 0.15f) : new Vector3(0.62f, 0.66f, 0.62f), 0.25f, grilled ? 0.2f : 0.8f);
        k.With(Matrix4x4.CreateScale(0.45f, 0.75f, 1.6f), () => Ball(k, Vector3.Zero, 0.07f, 10));
        k.Cylinder(new Vector3(0, 0, 0.1f), new Vector3(0, 0, 0.16f), 0.015f, 6, radiusB: 0.04f);
        k.Box(new Vector3(-0.004f, -0.045f, 0.155f), new Vector3(0.004f, 0.045f, 0.17f));
        k.Use("dress_paint", grilled ? Black * 3 : new Vector3(0.75f, 0.4f, 0.45f), 0.4f, 0.3f);
        if (grilled)
            foreach (float z in new[] { -0.05f, 0, 0.05f })
                k.Box(new Vector3(0.031f, -0.02f, z - 0.006f), new Vector3(0.033f, 0.04f, z + 0.006f));
        else
            k.Box(new Vector3(0.031f, -0.01f, -0.08f), new Vector3(0.033f, 0.01f, 0.08f));
        k.Use("dress_paint", Black, 0.3f, 0.5f);
        Ball(k, new Vector3(0.028f, 0.015f, -0.08f), 0.007f, 5);
        return k.Build(grilled ? "whimsy-grilled-fish" : "whimsy-fish");
    }

    /// <summary>
    /// The yard cat (note 745), a ginger tom, by its mood: sat up (idle), walking, curled asleep (on the backhead), or arched,
    /// its fur and tail up, hissing (warning of what's aboard). Its middle at (0, 0, 0) as its body has it, its feet at −0.15,
    /// facing −Z.
    /// </summary>
    public static MeshAsset Cat(Look? look, int mood)
    {
        var k = new Kit(look, 1741 + mood);
        var fur = new Vector3(0.78f, 0.45f, 0.18f);
        const float foot = -0.15f;
        k.Use("dress_fur", fur, 0.9f, 0);
        switch (mood)
        {
            case 2: // asleep: a loaf, the tail round it, the head down on the paws
                k.With(Matrix4x4.CreateScale(1.1f, 0.6f, 1.4f) * Kit.At(0, foot + 0.09f, 0), () => Ball(k, Vector3.Zero, 0.12f, 10));
                Ball(k, new Vector3(0, foot + 0.08f, -0.17f), 0.065f, 8);
                k.Cylinder(new Vector3(0.1f, foot + 0.03f, 0.12f), new Vector3(0.12f, foot + 0.03f, -0.12f), 0.025f, 6);
                break;
            case 3: // arched, hissing
                k.With(Matrix4x4.CreateScale(0.8f, 1.0f, 1.5f) * Kit.At(0, foot + 0.24f, 0), () => Ball(k, Vector3.Zero, 0.11f, 10));
                foreach (float x in new[] { -0.05f, 0.05f })
                    foreach (float z in new[] { -0.12f, 0.12f })
                        k.Cylinder(new Vector3(x, foot, z), new Vector3(x, foot + 0.2f, z * 0.6f), 0.018f, 5);
                Ball(k, new Vector3(0, foot + 0.2f, -0.2f), 0.06f, 8);
                k.Cylinder(new Vector3(0, foot + 0.26f, 0.14f), new Vector3(0, foot + 0.48f, 0.18f), 0.035f, 6, radiusB: 0.02f);
                k.Use("dress_paint", new Vector3(0.75f, 0.25f, 0.25f), 0.4f, 0.4f);
                k.Box(new Vector3(-0.02f, foot + 0.17f, -0.262f), new Vector3(0.02f, foot + 0.19f, -0.255f));
                k.Use("dress_fur", fur, 0.9f, 0);
                break;
            default: // sat up, or walking
                bool sat = mood == 0;
                k.With(Matrix4x4.CreateScale(0.75f, 0.85f, 1.35f) * Kit.At(0, foot + (sat ? 0.13f : 0.16f), sat ? 0.04f : 0), () => Ball(k, Vector3.Zero, 0.1f, 10));
                foreach (float x in new[] { -0.045f, 0.045f })
                {
                    k.Cylinder(new Vector3(x, foot, -0.08f), new Vector3(x, foot + 0.14f, -0.08f), 0.017f, 5);
                    if (!sat)
                        k.Cylinder(new Vector3(x, foot, 0.1f), new Vector3(x, foot + 0.14f, 0.1f), 0.017f, 5);
                }
                Ball(k, new Vector3(0, foot + (sat ? 0.27f : 0.24f), -0.12f), 0.06f, 8);
                k.Cylinder(new Vector3(0, foot + 0.12f, 0.14f), new Vector3(0, sat ? foot + 0.02f : foot + 0.3f, sat ? 0.22f : 0.26f), 0.02f, 6);
                break;
        }
        // The ears, and the eyes: green, catching the light.
        if (mood != 2)
        {
            float hy = mood == 3 ? foot + 0.2f : mood == 0 ? foot + 0.27f : foot + 0.24f, hz = mood == 3 ? -0.2f : -0.12f;
            k.Use("dress_fur", fur * 0.8f, 0.9f, 0);
            foreach (float x in new[] { -0.03f, 0.03f })
                k.Cylinder(new Vector3(x, hy + 0.04f, hz), new Vector3(x * 1.4f, hy + 0.1f, hz + 0.005f), 0.02f, 4, radiusB: 0.002f);
            k.Use("dress_glass", new Vector3(0.6f, 0.95f, 0.35f), 0.05f, 0.9f);
            k.Emissive = 0.6f;
            foreach (float x in new[] { -0.022f, 0.022f })
                Ball(k, new Vector3(x, hy + 0.01f, hz - 0.05f), 0.009f, 5);
            k.Emissive = 0;
        }
        return k.Build($"whimsy-cat-{mood}");
    }

    /// <summary>
    /// The dog (note 745), a black-and-white sheepdog, by its mood: sat, walking (or fetching), lying down, or braced and barking
    /// at what's aboard. Its middle at (0, 0, 0) as its body has it, its feet at −0.25, facing −Z.
    /// </summary>
    public static MeshAsset Dog(Look? look, int mood)
    {
        var k = new Kit(look, 1746 + mood);
        var black = new Vector3(0.08f, 0.08f, 0.09f);
        var white = new Vector3(0.85f, 0.84f, 0.8f);
        const float foot = -0.25f;
        bool lying = mood == 2, barking = mood == 3, sat = mood == 0;
        float body = lying ? foot + 0.14f : sat ? foot + 0.3f : foot + 0.38f;
        k.Use("dress_fur", black, 0.9f, 0);
        k.With(Matrix4x4.CreateScale(0.85f, 0.85f, 1.7f) * Kit.At(0, body, sat ? 0.08f : 0), () => Ball(k, Vector3.Zero, 0.14f, 10));
        if (!lying)
            foreach (float x in new[] { -0.07f, 0.07f })
            {
                k.Use("dress_fur", white, 0.9f, 0);
                k.Cylinder(new Vector3(x, foot, barking ? -0.2f : -0.16f), new Vector3(x, body, -0.14f), 0.03f, 6);
                if (!sat)
                    k.Cylinder(new Vector3(x, foot, 0.2f), new Vector3(x, body, 0.17f), 0.03f, 6);
            }
        // The head, white-blazed, the muzzle out; the ears; the tail.
        float hy = lying ? foot + 0.17f : barking ? body + 0.08f : body + 0.16f, hz = lying ? -0.3f : -0.28f;
        k.Use("dress_fur", black, 0.9f, 0);
        Ball(k, new Vector3(0, hy, hz), 0.085f, 10);
        k.Use("dress_fur", white, 0.9f, 0);
        k.Cylinder(new Vector3(0, hy - 0.02f, hz - 0.05f), new Vector3(0, hy - 0.03f, hz - 0.15f), 0.045f, 8, radiusB: 0.03f);
        k.Use("dress_paint", black * 0.5f, 0.3f, 0.6f);
        Ball(k, new Vector3(0, hy - 0.02f, hz - 0.16f), 0.015f, 5);
        if (barking)
        {
            k.Use("dress_paint", new Vector3(0.7f, 0.25f, 0.28f), 0.4f, 0.3f);
            k.Box(new Vector3(-0.025f, hy - 0.08f, hz - 0.15f), new Vector3(0.025f, hy - 0.06f, hz - 0.05f));
        }
        k.Use("dress_fur", black, 0.9f, 0);
        foreach (float x in new[] { -0.06f, 0.06f })
            k.Cylinder(new Vector3(x, hy + 0.05f, hz + 0.02f), new Vector3(x * 1.6f, hy + (barking ? 0.12f : 0.03f), hz + 0.05f), 0.025f, 4, radiusB: 0.008f);
        k.Cylinder(new Vector3(0, body + 0.04f, 0.22f), new Vector3(0, body + (lying ? 0 : 0.18f), 0.36f), 0.03f, 6, radiusB: 0.015f);
        return k.Build($"whimsy-dog-{mood}");
    }

    /// <summary>A tennis ball (note 745): yellow-green felt, 0.035 m round its middle at (0, 0, 0), its seam white.</summary>
    public static MeshAsset TennisBall(Look? look)
    {
        var k = new Kit(look, 1752);
        k.Use("dress_felt", new Vector3(0.8f, 0.92f, 0.2f), 0.9f, 0);
        Ball(k, Vector3.Zero, 0.035f, 10);
        k.Use("dress_paint", new Vector3(0.95f, 0.95f, 0.9f), 0.6f, 0.1f);
        for (int i = 0; i < 12; i++)
        {
            float a0 = MathF.Tau * i / 12, a1 = MathF.Tau * (i + 1) / 12;
            Vector3 P(float a) => new(0.0355f * MathF.Cos(a), 0.012f * MathF.Sin(2 * a), 0.0355f * MathF.Sin(a));
            k.Rod(P(a0), P(a1), 0.0025f, 3);
        }
        return k.Build("whimsy-tennis-ball");
    }

    /// <summary>
    /// The tennis-ball launcher (note 745): a spring-loaded tube on a post clamped to the rear platform's rail, pointed up and
    /// back over the line, a bucket of balls beside it. Its foot (the platform under it) at (0, 0, 0); back is +Z.
    /// </summary>
    public static MeshAsset Launcher(Look? look)
    {
        var k = new Kit(look, 1753);
        k.Use("dress_paint", new Vector3(0.15f, 0.3f, 0.55f), 0.4f, 0.4f);
        k.Cylinder(new Vector3(0, 0, 0.3f), new Vector3(0, 0.9f, 0.3f), 0.035f, 8);
        k.Cylinder(new Vector3(0, 0.85f, 0.05f), new Vector3(0, 1.15f, 0.62f), 0.06f, 12);
        k.Use("dress_paint", new Vector3(0.9f, 0.85f, 0.2f), 0.4f, 0.4f);
        k.Cylinder(new Vector3(0, 1.15f, 0.62f), new Vector3(0, 1.17f, 0.66f), 0.065f, 12);
        k.Use("dress_brass", Brass * 0.6f, 0.4f, 0.8f);
        k.Rod(new Vector3(0, 0.84f, 0.02f), new Vector3(0, 0.75f, -0.12f), 0.012f);
        k.Use("dress_paint", new Vector3(0.7f, 0.2f, 0.15f), 0.6f, 0.2f);
        k.Cylinder(new Vector3(0.4f, 0, 0.1f), new Vector3(0.4f, 0.26f, 0.1f), 0.13f, 12, radiusB: 0.15f, capB: false);
        k.Use("dress_felt", new Vector3(0.8f, 0.92f, 0.2f), 0.9f, 0);
        foreach (var b in new[] { new Vector3(0.36f, 0.26f, 0.06f), new Vector3(0.44f, 0.27f, 0.12f), new Vector3(0.4f, 0.27f, 0.17f) })
            Ball(k, b, 0.035f, 8);
        return k.Build("whimsy-launcher");
    }

    /// <summary>
    /// Where the rear searchlight stands on the guard van (note 899), in the car's frame: the roof's back right corner, off the
    /// gun's rail down the middle and out of its muzzle's way (the tail lamp has the left corner).
    /// </summary>
    public static Ballast.Double3 SearchlightMount(Sim.Train.CarShape shape) => new(shape.HalfWidth - 0.4, shape.RoofHeight + 0.04, shape.HalfLength - 0.45);

    /// <summary>The searchlight's lens centre, and where it points, in its own frame (+Z back down the line, dipped to the track).</summary>
    public static readonly Vector3 SearchlightLens = new(0, 0.62f, 0.3f), SearchlightAim = Vector3.Normalize(new Vector3(0, -0.11f, 1));

    /// <summary>
    /// The rear searchlight (note 899): a black drum on a yoke on a short pedestal, its lens facing back down the line and a
    /// little down, the kind a yard tower sweeps with. Its glass is <see cref="SearchlightGlass"/>, drawn lit.
    /// </summary>
    public static MeshAsset Searchlight(Look? look)
    {
        var k = new Kit(look, 1761);
        var aim = SearchlightAim;
        var lens = SearchlightLens;
        k.Use("dress_iron", new Vector3(0.09f, 0.09f, 0.1f), 0.55f, 0.6f);
        k.Cylinder(new Vector3(0, 0, 0), new Vector3(0, 0.06f, 0), 0.22f, 12);
        k.Cylinder(new Vector3(0, 0.06f, 0), new Vector3(0, 0.38f, 0), 0.05f, 8);
        // The yoke: a fork up either side of the drum, to its trunnions.
        k.Box(new Vector3(-0.3f, 0.36f, -0.05f), new Vector3(0.3f, 0.41f, 0.05f));
        foreach (float x in new[] { -0.29f, 0.29f })
            k.Box(new Vector3(x - 0.025f, 0.38f, -0.04f), new Vector3(x + 0.025f, 0.66f, 0.04f));
        // The drum, its back a cooling hood, its front a brass bezel round the glass.
        var centre = lens - aim * 0.3f;
        k.Cylinder(centre - aim * 0.28f, lens - aim * 0.02f, 0.24f, 16);
        k.Cylinder(centre - aim * 0.38f, centre - aim * 0.28f, 0.16f, 12);
        k.Rod(centre + new Vector3(0, 0.24f, 0) - aim * 0.12f, centre + new Vector3(0, 0.3f, 0) - aim * 0.12f, 0.03f);
        k.Use("dress_brass", Brass * 0.7f, 0.35f, 0.85f);
        k.Cylinder(lens - aim * 0.03f, lens + aim * 0.01f, 0.26f, 16);
        return k.Build("whimsy-searchlight");
    }

    /// <summary>The searchlight's glass, lit (drawn only while it's shining).</summary>
    public static MeshAsset SearchlightGlass(Look? look)
    {
        var k = new Kit(look, 1762) { Emissive = 1 };
        k.Use("dress_glass", new Vector3(1.0f, 0.95f, 0.82f), 0.1f, 0);
        k.Cylinder(SearchlightLens, SearchlightLens + SearchlightAim * 0.015f, 0.215f, 16);
        return k.Build("whimsy-searchlight-glass");
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
