using System.Numerics;
using Ballast.Render;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Art;

/// <summary>
/// Corruption grown on the train (GDD Part Eleven q8, "growth in the couplings, in the tender, under the cars"; ARCHITECTURE
/// §8 note 1039): black, wet and veined, where the campaign says creatures got on, more each night. One piece per vehicle
/// in its own frame, over the body: its two coupling ends, under its floor and up its sides from the sill, and on the
/// engine the crust over the tender's coal. Steps of growth (look.json <c>trainGrowth.levels</c>) so a train shares a few
/// pieces; the car's seed chooses where on it, so the same car grows the same way night to night.
/// </summary>
public static class GrowthKit
{
    /// <param name="own">How grown the vehicle itself is (0 none, to <paramref name="levels"/>): under its floor, or in the tender.</param>
    /// <param name="front">Its front coupling's (the one ahead of it; 0 for the engine's front).</param>
    /// <param name="rear">Its rear coupling's (0 with nothing behind).</param>
    public static MeshAsset Grown(Look? look, CarShape shape, int own, int front, int rear, int levels, int seed)
    {
        var k = new Kit(look, 1039 + seed);
        var rng = new Random(1039 * 31 + seed);
        float l = (float)shape.HalfLength, w = (float)shape.HalfWidth;
        bool engine = shape.Cab is not null;
        float Share(int level) => (float)level / Math.Max(1, levels);
        if (front > 0)
            Coupling(k, rng, engine ? -l - 0.05f : -l, -1, Share(front), w);
        if (rear > 0)
            Coupling(k, rng, l, 1, Share(rear), w);
        if (own > 0)
        {
            if (engine && shape.Solids.FirstOrDefault(s => s.Part == PartKind.Tender) is { } tender)
                Tender(k, rng, tender.Box, Share(own));
            else if (!engine)
                Underneath(k, rng, l, w, Share(own));
        }
        k.Emissive = 0;
        return k.Build("train-growth");
    }

    // Under the floor, where the frame is (TrainKit: the floor 1.1 m up, its beams just under it).
    const float Under = 0.98f, Sill = 1.05f, CouplerY = 0.9f;

    /// <summary>The wet black crust: the tar's, near-black and glossy.</summary>
    static void Crust(Kit k)
    {
        k.Emissive = 0;
        k.Use("tar", Palette.SootBlack, 0.1f, 0.35f, tile: 0.6f);
        k.Shade(0.35f);
    }

    /// <summary>
    /// The veins through it: a bruise's red under the black, wet, and faintly lit from inside (as the Corruption's trees'
    /// pods are, note 696), so they read on a car's dark side at night where the black crust alone wouldn't.
    /// </summary>
    static void Veins(Kit k)
    {
        k.Use("flesh", new Vector3(0.42f, 0.06f, 0.1f), 0.2f, 0.75f);
        // (Over the flesh's texture too: Use leaves a texture untinted.)
        k.Tint = new Vector3(0.5f, 0.08f, 0.11f);
        k.Emissive = 0.32f;
    }

    /// <summary>A lump of the growth: a squashed, uneven blob of <paramref name="r"/>'s half-sizes round <paramref name="c"/>.</summary>
    static void Lump(Kit k, Random rng, Vector3 c, Vector3 r)
    {
        const int Sides = 6;
        var rings = new List<Vector3[]>();
        float[] heights = [-1f, -0.55f, 0.1f, 0.65f, 1f];
        foreach (float h in heights)
        {
            float across = MathF.Sqrt(MathF.Max(0.02f, 1 - h * h));
            var ring = new Vector3[Sides];
            for (int i = 0; i < Sides; i++)
            {
                float a = i * MathF.Tau / Sides, wobble = 0.75f + 0.5f * (float)rng.NextDouble();
                ring[i] = c + new Vector3(MathF.Cos(a) * r.X * across * wobble, h * r.Y, MathF.Sin(a) * r.Z * across * wobble);
            }
            rings.Add(ring);
        }
        k.Loft(rings, closed: true, facing: Vector3.Zero);
        k.Cap(rings[0], -Vector3.UnitY);
        k.Cap(rings[^1], Vector3.UnitY);
    }

    /// <summary>A strand hanging or reaching from <paramref name="a"/> to <paramref name="b"/>, thinning as it goes.</summary>
    static void Strand(Kit k, Vector3 a, Vector3 b, float r) => k.Cylinder(a, b, r, 5, caps: true, radiusB: r * 0.3f);

    /// <summary>A vein crawling from <paramref name="from"/> along <paramref name="dir"/> on a face, kinking as it goes.</summary>
    static void Vein(Kit k, Random rng, Vector3 from, Vector3 dir, Vector3 kink, float length, float r)
    {
        var at = from;
        int steps = Math.Max(2, (int)(length / 0.18f));
        for (int i = 0; i < steps; i++)
        {
            var next = at + dir * (length / steps) + kink * (float)(rng.NextDouble() - 0.5) * 0.4f;
            k.Rod(at, next, r * (1 - 0.6f * i / steps), 4);
            at = next;
        }
    }

    /// <summary>
    /// A coupling grown over (the end beam at <paramref name="z"/>, the coupler reaching <paramref name="dir"/>): a crust round
    /// the draft gear and the knuckle, the hose coated, strands from the beam to where the two knuckles meet. Each car draws
    /// its own half, so the gap reads as one growth across it.
    /// </summary>
    static void Coupling(Kit k, Random rng, float z, float dir, float g, float w)
    {
        Crust(k);
        float reach = 0.55f;
        // The crust round the draft gear and the knuckle, fatter as it grows.
        int lumps = 2 + (int)(g * 6);
        for (int i = 0; i < lumps; i++)
        {
            float t = (float)rng.NextDouble();
            var c = new Vector3((float)(rng.NextDouble() - 0.5) * 0.4f, CouplerY + (float)(rng.NextDouble() - 0.4) * 0.3f, z + dir * t * reach);
            float s = 0.07f + 0.13f * g * (0.6f + 0.4f * (float)rng.NextDouble());
            Lump(k, rng, c, new Vector3(s * 1.3f, s, s * 1.2f));
        }
        // Over the end beam's foot, spreading out along it.
        int beam = 1 + (int)(g * 4);
        for (int i = 0; i < beam; i++)
        {
            float x = ((float)rng.NextDouble() * 2 - 1) * w * (0.3f + 0.6f * g);
            Lump(k, rng, new Vector3(x, Under + 0.02f, z + dir * 0.06f), new Vector3(0.12f + 0.18f * g, 0.06f + 0.06f * g, 0.08f));
        }
        // Strands across the gap: from the beam to the knuckles' meeting, sagging; and down off the coupler.
        int strands = 1 + (int)(g * 5);
        for (int i = 0; i < strands; i++)
        {
            float x = ((float)rng.NextDouble() * 2 - 1) * 0.5f * w;
            var a = new Vector3(x, Under - 0.02f, z);
            var mid = new Vector3(x * 0.5f, CouplerY - 0.15f - 0.2f * (float)rng.NextDouble(), z + dir * reach * 0.55f);
            var b = new Vector3(x * 0.15f, CouplerY + 0.02f, z + dir * reach);
            float r = 0.012f + 0.02f * g;
            Strand(k, a, mid, r);
            Strand(k, mid, b, r * 0.8f);
        }
        int drips = (int)(g * 4);
        for (int i = 0; i < drips; i++)
        {
            var a = new Vector3((float)(rng.NextDouble() - 0.5) * 0.3f, CouplerY - 0.1f, z + dir * reach * (float)rng.NextDouble());
            Strand(k, a, a - new Vector3(0, 0.15f + 0.3f * g * (float)rng.NextDouble(), 0), 0.018f + 0.012f * g);
        }
        // The hose (TrainKit.Coupler's, hanging off the right of the knuckle) coated down its length.
        Strand(k, new Vector3(0.35f, CouplerY - 0.05f, z + dir * 0.05f), new Vector3(0.38f, CouplerY - 0.45f, z + dir * 0.25f), 0.035f + 0.02f * g);
        // Veins off it, up the car's end and along its sills.
        Veins(k);
        int veins = 1 + (int)(g * 4);
        for (int i = 0; i < veins; i++)
        {
            float x = ((float)rng.NextDouble() * 2 - 1) * w * 0.8f;
            Vein(k, rng, new Vector3(x, Under, z + dir * 0.03f), Vector3.UnitY, Vector3.UnitX, 0.3f + 0.9f * g, 0.016f);
        }
    }

    /// <summary>
    /// Under a car's floor (the frame between and over the bogies): clumps on the beams, strands hanging toward the rails, and
    /// veins up its sides from the sill.
    /// </summary>
    static void Underneath(Kit k, Random rng, float l, float w, float g)
    {
        Crust(k);
        float span = l - 0.4f;
        int clumps = 3 + (int)(g * 14);
        for (int i = 0; i < clumps; i++)
        {
            // Where it's taken: from a few patches to the whole underside.
            float z = ((float)rng.NextDouble() * 2 - 1) * span * (0.35f + 0.65f * g);
            float x = ((float)rng.NextDouble() * 2 - 1) * (w - 0.3f);
            float s = 0.1f + 0.22f * g * (0.5f + 0.5f * (float)rng.NextDouble());
            Lump(k, rng, new Vector3(x, Under - s * 0.5f, z), new Vector3(s * 1.6f, s * 0.6f, s * 2f));
        }
        // Strands hanging under the floor, clear of the bogies' wheels (TrainKit: the bogies sit ~2 m in from each end).
        int hanging = 2 + (int)(g * 12);
        float clear = MathF.Max(0.5f, l - 3.2f);
        for (int i = 0; i < hanging; i++)
        {
            float z = ((float)rng.NextDouble() * 2 - 1) * clear;
            float x = ((float)rng.NextDouble() * 2 - 1) * (w - 0.35f);
            float drop = 0.12f + 0.35f * g * (float)rng.NextDouble();
            var a = new Vector3(x, Under - 0.04f, z);
            Strand(k, a, a + new Vector3(0.04f, -drop, 0.03f), 0.02f + 0.025f * g);
        }
        // Veins up the sides from the sill, the further the more it's grown.
        Veins(k);
        int veins = 2 + (int)(g * 10);
        for (int i = 0; i < veins; i++)
        {
            int side = rng.Next(2) * 2 - 1;
            float z = ((float)rng.NextDouble() * 2 - 1) * span * (0.35f + 0.65f * g);
            // (Proud of the boards' relief, which stands a little off the body's bounds.)
            var from = new Vector3(side * (w + 0.05f), Sill - 0.08f, z);
            Vein(k, rng, from, Vector3.UnitY, Vector3.UnitZ, 0.2f + 0.9f * g * (0.4f + 0.6f * (float)rng.NextDouble()), 0.04f);
        }
        // Along the sill itself, a black bead of it under the boards.
        Crust(k);
        foreach (int side in new[] { -1, 1 })
        {
            int beads = 1 + (int)(g * 5);
            for (int i = 0; i < beads; i++)
            {
                float z = ((float)rng.NextDouble() * 2 - 1) * span * (0.3f + 0.7f * g);
                Lump(k, rng, new Vector3(side * (w + 0.02f), Sill - 0.06f, z), new Vector3(0.07f + 0.06f * g, 0.07f + 0.05f * g, 0.3f + 0.6f * g));
            }
        }
    }

    /// <summary>In the tender (the engine's bunker, PartKind.Tender): a crust over the coal and down its walls, strands over its rim.</summary>
    static void Tender(Kit k, Random rng, Box box, float g)
    {
        var (min, max) = (new Vector3((float)box.Min.X, (float)box.Min.Y, (float)box.Min.Z), new Vector3((float)box.Max.X, (float)box.Max.Y, (float)box.Max.Z));
        Crust(k);
        int lumps = 2 + (int)(g * 10);
        for (int i = 0; i < lumps; i++)
        {
            // Over the rim and down the outsides of the bunker's walls, most along its back.
            var at = new Vector3(min.X + (max.X - min.X) * (float)rng.NextDouble(), max.Y - 0.05f - (max.Y - min.Y) * 0.6f * g * (float)rng.NextDouble(),
                rng.Next(3) == 0 ? min.Z : max.Z);
            float s = 0.06f + 0.12f * g;
            Lump(k, rng, at, new Vector3(s * 1.4f, s * 1.2f, s));
        }
        int strands = 1 + (int)(g * 6);
        for (int i = 0; i < strands; i++)
        {
            float x = min.X + (max.X - min.X) * (float)rng.NextDouble();
            var a = new Vector3(x, max.Y, max.Z);
            Strand(k, a, a + new Vector3(0.03f, -0.2f - 0.4f * g * (float)rng.NextDouble(), 0.04f), 0.015f + 0.02f * g);
        }
        Veins(k);
        int veins = 1 + (int)(g * 6);
        for (int i = 0; i < veins; i++)
        {
            float x = min.X + (max.X - min.X) * (float)rng.NextDouble();
            Vein(k, rng, new Vector3(x, max.Y - 0.02f, max.Z + 0.01f), -Vector3.UnitY, Vector3.UnitX, 0.2f + 0.6f * g, 0.014f);
        }
    }
}
