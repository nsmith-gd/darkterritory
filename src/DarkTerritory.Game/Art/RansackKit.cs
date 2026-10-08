using System.Numerics;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Game.Art;

/// <summary>
/// A ransacked house's clutter and the Gaunt's nest, drawn (the director, 8 Oct 2026: "procedurally generated furniture
/// scattered about, like the place has been ransacked many times before ... sometimes a monster should be nesting in there";
/// ARCHITECTURE §8 note 326). Where each piece is and whether it's knocked over is the sim's (<see cref="StopWalls.ClutterOf"/>,
/// <see cref="StopWalls.Nest"/>): the solid ones are what a crewmate walks into. Each piece in its own frame, its middle on the
/// floor, x along its length, z across it toward the room (+z its front), y up; worn as the house is, nothing new.
/// </summary>
public static class RansackKit
{
    static readonly Vector3 Wood = new(0.32f, 0.24f, 0.16f), Dark = new(0.16f, 0.12f, 0.09f);

    /// <summary>One piece of clutter, in its own frame.</summary>
    public static void Piece(Kit k, Clutter c)
    {
        float hl = (float)c.HalfX, hd = (float)c.HalfY;
        switch (c.Kind)
        {
            case ClutterKind.Table:
                k.Use("wood_grey", Wood, 0.9f, 0.05f, tile: 1);
                if (c.Tipped)
                {
                    // Over on its side against the wall: its top stood up behind, its legs out into the room.
                    k.Box(new Vector3(-hl, 0, -hd), new Vector3(hl, 2 * hd, -hd + 0.04f));
                    foreach (float x in new[] { -hl + 0.06f, hl - 0.06f })
                        foreach (float y in new[] { 0.06f, 2 * hd - 0.06f })
                            k.Box(new Vector3(x - 0.03f, y - 0.03f, -hd + 0.04f), new Vector3(x + 0.03f, y + 0.03f, hd));
                }
                else
                {
                    // Stood, its top scored, a leg kicked out from under one corner so it lists.
                    k.Box(new Vector3(-hl, 0.7f, -hd), new Vector3(hl, 0.74f, hd));
                    foreach (float x in new[] { -hl + 0.06f, hl - 0.06f })
                        foreach (float z in new[] { -hd + 0.06f, hd - 0.06f })
                            k.Box(new Vector3(x - 0.03f, 0, z - 0.03f), new Vector3(x + 0.03f, 0.7f, z + 0.03f));
                }
                break;
            case ClutterKind.Bed:
                // An iron bedstead, its mattress slashed open and the stuffing pulled out over the side.
                k.Use("rust_heavy", Palette.IronGrey, 0.8f, 0.3f);
                k.Box(new Vector3(-hl, 0.25f, -hd), new Vector3(hl, 0.3f, hd));
                foreach (float x in new[] { -hl, hl })
                    foreach (float z in new[] { -hd, hd })
                        k.Rod(new Vector3(x, 0, z), new Vector3(x, x < 0 ? 0.95f : 0.6f, z), 0.02f);
                k.Rod(new Vector3(-hl, 0.95f, -hd), new Vector3(-hl, 0.95f, hd), 0.02f);
                k.Rod(new Vector3(hl, 0.6f, -hd), new Vector3(hl, 0.6f, hd), 0.02f);
                k.Use("sac", new Vector3(0.45f, 0.4f, 0.3f), 0.9f, 0, tile: 0.8f);
                k.Box(new Vector3(-hl + 0.05f, 0.3f, -hd + 0.03f), new Vector3(hl - 0.05f, 0.42f, hd - 0.03f));
                k.Use("wool", new Vector3(0.5f, 0.46f, 0.36f), 1, 0, tile: 0.4f);
                k.Box(new Vector3(-0.3f, 0.36f, -0.12f), new Vector3(0.25f, 0.46f, 0.12f));
                k.Box(new Vector3(0.1f, 0.0f, hd - 0.05f), new Vector3(0.45f, 0.12f, hd + 0.2f));
                k.Use("paint_black", Dark, 1, 0);
                k.Box(new Vector3(-0.5f, 0.421f, -0.02f), new Vector3(0.6f, 0.425f, 0.02f), Kit.Faces.PosY);
                break;
            case ClutterKind.Dresser:
                // A dresser pulled over on its face: its back up, its drawers out under it.
                k.Use("wood_grey", Dark, 0.9f, 0.05f, tile: 1);
                k.Box(new Vector3(-hl, 0, -hd), new Vector3(hl, 2 * hd * 0.8f, hd));
                k.Use("wood_grey", Wood, 0.9f, 0.05f, tile: 1);
                for (int i = 0; i < 3; i++)
                {
                    float x = -hl + 0.15f + i * (2 * hl - 0.3f) / 2;
                    k.Box(new Vector3(x - 0.18f, 0, hd), new Vector3(x + 0.18f, 0.14f, hd + 0.05f));
                }
                break;
            case ClutterKind.Chair:
                k.Use("wood_grey", Wood, 0.9f, 0.05f, tile: 1);
                if (c.Tipped)
                    k.Push(Matrix4x4.CreateRotationX(MathF.PI / 2) * Matrix4x4.CreateTranslation(0, 0.2f, -0.2f));
                k.Box(new Vector3(-0.2f, 0.42f, -0.2f), new Vector3(0.2f, 0.46f, 0.2f));
                foreach (float x in new[] { -0.17f, 0.17f })
                    foreach (float z in new[] { -0.17f, 0.17f })
                        k.Box(new Vector3(x - 0.02f, 0, z - 0.02f), new Vector3(x + 0.02f, z < 0 ? 0.95f : 0.42f, z + 0.02f));
                k.Box(new Vector3(-0.19f, 0.7f, -0.2f), new Vector3(0.19f, 0.78f, -0.16f));
                if (c.Tipped)
                    k.Pop();
                break;
            case ClutterKind.Crate:
                k.Use("wood_crate", Wood, 0.8f, 0.05f, tile: 0.6f);
                if (c.Tipped)
                {
                    // Stove in and on its side, its lid off beside it.
                    k.Box(new Vector3(-hl, 0, -hd), new Vector3(hl, 0.3f, hd), top: false);
                    k.Box(new Vector3(-hl, 0, hd + 0.02f), new Vector3(hl, 0.025f, hd + 0.3f));
                }
                else
                    k.Box(new Vector3(-hl, 0, -hd), new Vector3(hl, 0.32f, hd));
                break;
            case ClutterKind.Drawer:
                // A drawer pulled out, turned out and dropped: its sides, its bottom, nothing in it.
                k.Use("wood_grey", Wood, 0.9f, 0.05f, tile: 1);
                k.Box(new Vector3(-hl, 0, -hd), new Vector3(hl, 0.015f, hd));
                k.Box(new Vector3(-hl, 0, -hd), new Vector3(hl, 0.12f, -hd + 0.015f));
                k.Box(new Vector3(-hl, 0, hd - 0.015f), new Vector3(hl, 0.12f, hd));
                k.Box(new Vector3(-hl, 0, -hd), new Vector3(-hl + 0.015f, 0.12f, hd));
                k.Use("wood_grey", Dark, 0.9f, 0.05f, tile: 1);
                k.Box(new Vector3(hl - 0.02f, 0, -hd - 0.02f), new Vector3(hl, 0.14f, hd + 0.02f));
                break;
            case ClutterKind.Planks:
                // A shelf's boards, torn down, lying across each other.
                k.Use("wood_grey", Wood, 0.9f, 0.05f, tile: 1);
                for (int i = 0; i < 3; i++)
                {
                    k.Push(Matrix4x4.CreateRotationY((i - 1) * 0.35f) * Matrix4x4.CreateTranslation(0, i * 0.022f, (i - 1) * 0.06f));
                    k.Box(new Vector3(-hl, 0, -0.07f), new Vector3(hl, 0.02f, 0.07f));
                    k.Pop();
                }
                break;
            case ClutterKind.Rags:
                // Bedding and clothes pulled out and dropped in a heap.
                k.Use("wool", new Vector3(0.4f, 0.36f, 0.3f), 1, 0, tile: 0.4f);
                k.Box(new Vector3(-hl, 0, -hd), new Vector3(hl * 0.6f, 0.05f, hd * 0.7f));
                k.Use("sac", new Vector3(0.38f, 0.3f, 0.24f), 1, 0, tile: 0.5f);
                k.Box(new Vector3(-hl * 0.4f, 0.03f, -hd * 0.3f), new Vector3(hl, 0.09f, hd));
                break;
            case ClutterKind.Papers:
                // Letters and forms out of a bureau, scattered.
                k.Use("paper_form", new Vector3(0.7f, 0.66f, 0.55f), 0.8f, 0, tile: 0.6f);
                for (int i = 0; i < 4; i++)
                {
                    float a = i * 1.3f, x = (i % 2 - 0.5f) * hl, z = (i / 2 - 0.5f) * hd;
                    var u = new Vector3(MathF.Cos(a), 0, MathF.Sin(a)) * 0.1f;
                    var v = new Vector3(-MathF.Sin(a), 0, MathF.Cos(a)) * 0.14f;
                    var p = new Vector3(x, 0.004f + i * 0.002f, z);
                    k.Quad(p - u - v, p + u - v, p + u + v, p - u + v, twoSided: true);
                }
                break;
            case ClutterKind.Crockery:
                // A dresser's plates, smashed.
                k.Use("plaster_ruin", new Vector3(0.75f, 0.72f, 0.64f), 0.4f, 0.4f, tile: 0.3f);
                for (int i = 0; i < 6; i++)
                {
                    float a = i * 2.4f, r = 0.04f + i * 0.02f;
                    var p = new Vector3(MathF.Cos(a) * r * 2, 0, MathF.Sin(a) * r);
                    k.Tri(p + new Vector3(0, 0.01f, 0), p + new Vector3(0.06f, 0.01f, 0.02f), p + new Vector3(0.02f, 0.01f, 0.07f));
                }
                k.Cylinder(new Vector3(0.08f, 0, -0.04f), new Vector3(0.08f, 0.012f, -0.04f), 0.09f, 9);
                break;
            case ClutterKind.Frame:
                // A picture off its nail, face down, its glass out.
                k.Use("brass", Palette.TarnishedBrass, 0.8f, 0.3f, tile: 0.3f);
                k.Box(new Vector3(-hl, 0, -hd), new Vector3(hl, 0.03f, -hd + 0.04f));
                k.Box(new Vector3(-hl, 0, hd - 0.04f), new Vector3(hl, 0.03f, hd));
                k.Box(new Vector3(-hl, 0, -hd), new Vector3(-hl + 0.04f, 0.03f, hd));
                k.Box(new Vector3(hl - 0.04f, 0, -hd), new Vector3(hl, 0.03f, hd));
                k.Use("paper_form", new Vector3(0.3f, 0.26f, 0.2f), 0.9f, 0, tile: 1);
                k.Box(new Vector3(-hl + 0.04f, 0, -hd + 0.04f), new Vector3(hl - 0.04f, 0.012f, hd - 0.04f), Kit.Faces.PosY);
                break;
            case ClutterKind.Bucket:
                // Kicked over.
                k.Use("rust_heavy", Palette.IronGrey, 0.8f, 0.3f);
                k.Cylinder(new Vector3(-0.13f, 0.12f, 0), new Vector3(0.13f, 0.12f, 0), 0.12f, 9, radiusB: 0.1f, capA: false);
                break;
        }
    }

    /// <summary>
    /// The Gaunt's nest (its roost, level-design H.2): bedding and the house's stuffing dragged into a matted ring on the floor,
    /// trodden down in the middle, the bones of what it ate kicked to the edges. In its own frame, its middle on the floor.
    /// </summary>
    public static void Nest(Kit k, int seed)
    {
        // The hollow it lies in: trodden filth, dark and glossy.
        k.Use("tar", new Vector3(0.06f, 0.05f, 0.04f), 0.3f, 0.4f, tile: 0.5f);
        k.Cylinder(new Vector3(0, 0, 0), new Vector3(0, 0.02f, 0), 0.75f, 14);
        // The ring: bedding, stuffing and clothes dragged in and matted into heaps round it, each its own size and lie.
        k.Use("wool", new Vector3(0.22f, 0.2f, 0.17f), 1, 0, tile: 0.35f);
        for (int i = 0; i < 13; i++)
        {
            float a = i * MathF.Tau / 13 + seed * 0.37f, r = 0.8f + 0.15f * MathF.Sin(i * 2.7f + seed);
            var p = new Vector3(MathF.Cos(a) * r, 0, MathF.Sin(a) * r);
            float h = 0.1f + 0.1f * (0.5f + 0.5f * MathF.Sin(i * 1.9f + seed * 0.5f)), w = 0.16f + 0.08f * MathF.Sin(i * 3.1f);
            k.Push(Matrix4x4.CreateRotationZ(0.25f * MathF.Sin(i * 1.3f)) * Matrix4x4.CreateRotationY(-a + 0.3f * MathF.Sin(i)) * Matrix4x4.CreateTranslation(p));
            k.Box(new Vector3(-w, 0, -0.28f), new Vector3(w, h, 0.28f));
            k.Pop();
        }
        k.Use("sac", new Vector3(0.2f, 0.16f, 0.12f), 1, 0, tile: 0.4f);
        for (int i = 0; i < 5; i++)
        {
            float a = i * 1.37f + seed, r = 1.15f;
            k.Push(Matrix4x4.CreateRotationY(a) * Matrix4x4.CreateTranslation(MathF.Cos(a) * r, 0, MathF.Sin(a) * r));
            k.Box(new Vector3(-0.25f, 0, -0.1f), new Vector3(0.25f, 0.05f, 0.1f));
            k.Pop();
        }
        // Bones, gnawed and pale against the filth: long bones, ribs, a skull's dome, in the hollow and by the edge.
        k.Use("plaster_ruin", new Vector3(0.7f, 0.66f, 0.55f), 0.4f, 0.15f, tile: 0.3f);
        for (int i = 0; i < 9; i++)
        {
            float a = i * 0.9f + seed * 0.61f, r = 0.2f + (i % 3) * 0.3f, len = i % 2 == 0 ? 0.4f : 0.24f;
            var p = new Vector3(MathF.Cos(a) * r, 0.05f + (i % 2) * 0.03f, MathF.Sin(a) * r);
            var d = new Vector3(MathF.Cos(a * 2.3f), 0, MathF.Sin(a * 2.3f)) * len / 2;
            k.Cylinder(p - d, p + d, 0.026f, 6, radiusB: 0.02f);
            k.Cylinder(p + d, p + d * 1.18f, 0.04f, 6);
            k.Cylinder(p - d * 1.18f, p - d, 0.036f, 6);
        }
        k.Lathe(new Vector3(0.25f, 0.02f, -0.3f), [new(0.09f, 0), new(0.1f, 0.05f), new(0.07f, 0.1f), new(0.02f, 0.12f)], 8, smooth: true);
    }
}
