using System.Numerics;
using Ballast.Render;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Art;

public static partial class TrainKit
{
    /// <summary>
    /// The BBQ deck (ARCHITECTURE §8 note 698; the director, 13 Oct 2026: "an extended front car cow catcher that has a deck, a
    /// couple muskoka chairs that can be sat in, a plastic cooler with beers, and a BBQ mounted on a stand on the deck ... its
    /// the BBQ Dads at the end of the world"), in the engine's frame, over <see cref="CarShape.WithBbqDeck"/>'s solids: a deck of
    /// weathered planks on two iron stringers out over the cowcatcher (EngineDress draws it pushed out under the deck's front),
    /// railed in pipe round its front and sides; two Muskoka chairs facing down the line, one red and one green, the cooler
    /// between them (blue, its white lid); and the barrel grill on its stand by the cab, its side shelf and its chimney. The
    /// one thing the war train has that's painted for fun. Its coals are <see cref="BbqCoals"/>, drawn while it's lit.
    /// </summary>
    public static MeshAsset BbqDeck(Look? look, CarShape shape)
    {
        var k = new Kit(look, 688);
        var deck = shape.Bbq!;
        var planks = shape.Solids.First(s => s.Part == PartKind.BbqDeck).Box;
        float l = (float)shape.HalfLength, front = (float)deck.Front, top = (float)planks.Max.Y, under = (float)planks.Min.Y;
        float hw = (float)planks.Max.X;
        // The stringers out from the buffer beam, and a cross beam under the front.
        Iron(k);
        foreach (float x in new[] { -1.1f, 1.1f })
            k.Box(new Vector3(x - 0.09f, under - 0.32f, front + 0.1f), new Vector3(x + 0.09f, under, -l + 0.05f));
        k.Box(new Vector3(-hw + 0.2f, under - 0.26f, front + 0.05f), new Vector3(hw - 0.2f, under, front + 0.25f));
        // The planks across it, a finger's gap between.
        k.Use("wood_grey", new Vector3(0.52f, 0.42f, 0.32f), 0.9f, 0, tile: 1);
        for (float z = front; z < -l - 0.01f; z += 0.16f)
            k.Box(new Vector3(-hw, under + 0.03f, z + 0.01f), new Vector3(hw, top, MathF.Min(z + 0.15f, -l)));
        // The rails: pipe posts and two pipe rails, round the front and down each side to the running board's way on.
        Iron(k, 0.8f).Shade(0.8f);
        foreach (var rail in shape.Solids.Where(s => s.Part == PartKind.Rail))
        {
            var b = rail.Box;
            bool across = b.Max.X - b.Min.X > b.Max.Z - b.Min.Z;
            float x0 = (float)b.Centre.X, z0 = (float)b.Centre.Z;
            var a = across ? new Vector3((float)b.Min.X, 0, z0) : new Vector3(x0, 0, (float)b.Min.Z);
            var c = across ? new Vector3((float)b.Max.X, 0, z0) : new Vector3(x0, 0, (float)b.Max.Z);
            float height = (float)(b.Max.Y - b.Min.Y);
            foreach (float y in new[] { top + height, top + height * 0.5f })
                k.Cylinder(a + Vector3.UnitY * y, c + Vector3.UnitY * y, 0.03f, 6);
            float run = Vector3.Distance(a, c);
            int posts = Math.Max(2, (int)MathF.Ceiling(run / 1.0f) + 1);
            for (int i = 0; i < posts; i++)
            {
                var p = Vector3.Lerp(a, c, i / (float)(posts - 1));
                k.Cylinder(p + Vector3.UnitY * top, p + Vector3.UnitY * (top + height + 0.02f), 0.03f, 6);
            }
        }
        // The chairs, facing down the line: a red one and a green one.
        var paints = new[] { new Vector3(0.85f, 0.16f, 0.1f), new Vector3(0.2f, 0.62f, 0.3f) };
        for (int i = 0; i < deck.Chairs.Count; i++)
            MuskokaChair(k, V(deck.Chairs[i]), paints[i % paints.Length]);
        Cooler(k, V(deck.Cooler));
        Grill(k, V(deck.Grill));
        return k.Build("bbq-deck");
    }

    /// <summary>The grill's coals, glowing through its open front and under its lid, while it's lit (note 698).</summary>
    public static MeshAsset BbqCoals(Look? look, CarShape shape)
    {
        var k = new Kit(look, 689);
        var g = V(shape.Bbq!.Grill);
        k.Use("ember_crack", Palette.FurnaceOrange, 0.5f, 0);
        k.Emissive = 1.4f;
        k.Box(g + new Vector3(-0.27f, 0.82f, -0.16f), g + new Vector3(0.27f, 0.9f, 0.16f));
        // The lid's seam lit along the front.
        k.Box(g + new Vector3(-0.3f, 0.96f, -0.255f), g + new Vector3(0.3f, 0.99f, -0.235f));
        k.Emissive = 0;
        return k.Build("bbq-coals");
    }

    /// <summary>Where the grill's smoke leaves its chimney, in the engine's frame (note 698).</summary>
    public static Vector3 GrillChimney(CarShape shape) => V(shape.Bbq!.Grill) + new Vector3(0.24f, 1.45f, 0.1f);

    static Vector3 V(Ballast.Double3 d) => new((float)d.X, (float)d.Y, (float)d.Z);

    /// <summary>
    /// A Muskoka chair at <paramref name="at"/> (its sitter's feet), facing −Z: a seat of slats sloping back, the fanned back
    /// raked well back, the broad flat arms a drink stands on, in its paint.
    /// </summary>
    static void MuskokaChair(Kit k, Vector3 at, Vector3 paint)
    {
        // (Flat paint, no texture under it: a painted chair, bright against the war train.)
        k.Use("bbq_paint", paint, 0.35f, 0.05f);
        // The legs: the front pair up to the arms, the back pair to the seat's back.
        foreach (int side in new[] { -1, 1 })
        {
            k.Box(at + new Vector3(side * 0.34f - 0.035f, 0, -0.34f), at + new Vector3(side * 0.34f + 0.035f, 0.6f, -0.27f));
            k.Box(at + new Vector3(side * 0.31f - 0.035f, 0, 0.18f), at + new Vector3(side * 0.31f + 0.035f, 0.34f, 0.25f));
            // The arm: a broad board, its front end rounded off square.
            k.Box(at + new Vector3(side * 0.42f - 0.08f, 0.6f, -0.4f), at + new Vector3(side * 0.42f + 0.08f, 0.63f, 0.3f));
            // The side rail under the seat, front to back.
            k.Box(at + new Vector3(side * 0.31f - 0.02f, 0.26f, -0.32f), at + new Vector3(side * 0.31f + 0.02f, 0.36f, 0.24f));
        }
        // The seat: slats across, sloping down to the back.
        for (int i = 0; i < 6; i++)
        {
            float z = -0.3f + i * 0.09f, y = 0.38f - i * 0.016f;
            k.Box(at + new Vector3(-0.3f, y - 0.025f, z), at + new Vector3(0.3f, y, z + 0.07f));
        }
        // The back: five slats fanned out and raked back, a rail across behind them.
        for (int i = -2; i <= 2; i++)
        {
            var foot = at + new Vector3(i * 0.11f, 0.3f, 0.24f);
            var head = at + new Vector3(i * 0.14f, 1.02f, 0.55f);
            k.Rod(foot, head, 0.045f);
        }
        k.Box(at + new Vector3(-0.32f, 0.72f, 0.42f), at + new Vector3(0.32f, 0.78f, 0.47f));
    }

    /// <summary>The cooler at <paramref name="at"/> (its middle, on the deck): a blue plastic box, its white lid, a handle each end.</summary>
    static void Cooler(Kit k, Vector3 at)
    {
        // (Flat plastic, no texture under it.)
        k.Use("bbq_plastic", new Vector3(0.1f, 0.34f, 0.78f), 0.2f, 0.25f);
        k.Box(at + new Vector3(-0.29f, 0, -0.19f), at + new Vector3(0.29f, 0.36f, 0.19f));
        k.Use("bbq_plastic", new Vector3(0.9f, 0.9f, 0.86f), 0.2f, 0.25f);
        k.Box(at + new Vector3(-0.31f, 0.36f, -0.21f), at + new Vector3(0.31f, 0.44f, 0.21f));
        foreach (int side in new[] { -1, 1 })
            k.Box(at + new Vector3(side * 0.29f + (side < 0 ? -0.04f : 0), 0.24f, -0.07f), at + new Vector3(side * 0.29f + (side > 0 ? 0.04f : 0), 0.3f, 0.07f));
    }

    /// <summary>
    /// The barrel grill at <paramref name="at"/> (its middle, on the deck): an oil drum cut lengthways on a welded stand, its
    /// lid hinged at the back, a chimney off one end, a side shelf, a shelf under it with a bag of charcoal.
    /// </summary>
    static void Grill(Kit k, Vector3 at)
    {
        Iron(k, 0.8f).Shade(0.6f);
        foreach (int sx in new[] { -1, 1 })
            foreach (int sz in new[] { -1, 1 })
                k.Box(at + new Vector3(sx * 0.3f - 0.025f, 0, sz * 0.2f - 0.025f), at + new Vector3(sx * 0.3f + 0.025f, 0.78f, sz * 0.2f + 0.025f));
        k.Box(at + new Vector3(-0.32f, 0.22f, -0.22f), at + new Vector3(0.32f, 0.25f, 0.22f));
        // The drum, black, on its side across the stand.
        k.Use("paint_black", new Vector3(0.09f, 0.09f, 0.1f), 0.6f, 0.5f);
        k.Cylinder(at + new Vector3(-0.32f, 0.92f, 0), at + new Vector3(0.32f, 0.92f, 0), 0.25f, 12);
        k.Cylinder(at + new Vector3(0.24f, 1.0f, 0.1f), at + new Vector3(0.24f, 1.45f, 0.1f), 0.05f, 8);
        // The lid's handle out front, and the side shelf.
        Iron(k, 0.8f);
        k.Rod(at + new Vector3(-0.15f, 1.12f, -0.29f), at + new Vector3(0.15f, 1.12f, -0.29f), 0.015f);
        k.Use("wood_grey", new Vector3(0.5f, 0.4f, 0.3f), 0.9f, 0, tile: 1);
        k.Box(at + new Vector3(0.32f, 0.86f, -0.18f), at + new Vector3(0.62f, 0.89f, 0.18f));
        // A bag of charcoal on the shelf under it.
        k.Use("sac", new Vector3(0.62f, 0.55f, 0.4f), 0.9f, 0, tile: 1);
        k.Box(at + new Vector3(-0.22f, 0.25f, -0.12f), at + new Vector3(0.08f, 0.52f, 0.12f));
    }
}
