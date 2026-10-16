using System.Numerics;
using Ballast.Render;

namespace DarkTerritory.Game.Art;

/// <summary>
/// What's left of a fortress town that was only ever begun (queue #551, ARCHITECTURE §8 note 824; the World Building board's
/// Cultures tab, the director: "Ruins of fortress towns that once were. They're always small beginnings of a fortress that
/// were destroyed by the Wakers"). Out beside the line between the stops (Sim.Run.LinesideProps' "fortRuin"): new wall in
/// the fortresses' own dark dressed stone (StructureKit.Wall's), never finished to its height, its courses stepping off
/// where the masons stopped; and then a Waker came down the line at dawn, and one foot came down on it. A run of the wall
/// is trodden into rubble, the footprint pressed into the ground across it the size of a house (the earth shoved up round
/// its heel and three toes), slabs thrown out from it. Cooked in its own frame, its foot at the origin, the wall running
/// along x, its front (toward the line) to −Z.
/// </summary>
public static class RuinKit
{
    /// <summary>How many ruins there are: a corner of wall with its tower, and a gatehouse begun.</summary>
    public const int Variants = 2;

    /// <summary>How deep the walls' footings go under the piece's foot (m), so a slope under the piece doesn't show daylight.</summary>
    const float Footing = 1.2f;

    /// <summary>The fortresses' wall's thickness (StructureKit.Wall's), and the length of a mason's stretch of it (m).</summary>
    const float Thick = 1.6f, Stretch = 1.4f;

    public static MeshAsset FortRuin(Look? look, int variant)
    {
        var k = new Kit(look, 5100 + variant);
        var rng = new Random(551 + variant * 7);
        if (variant % Variants == 0)
            Corner(k, rng);
        else
            Gatehouse(k, rng);
        return k.Build($"fortruin-{variant}");
    }

    /// <summary>
    /// A corner: the front wall along the line with a squat round tower at its far end, a side wall begun from the tower back
    /// from the line, its scaffolding half down. The front wall's middle trodden flat: the footprint across it.
    /// </summary>
    static void Corner(Kit k, Random rng)
    {
        Stone(k);
        // The front wall: built to between three and six metres, stepping where the masons stopped; broken from −7 to 0.
        Wall(k, rng, -17, -7, 0, 3.0f, 5.8f);
        Wall(k, rng, 0, 7, 0, 2.6f, 4.4f);
        // The tower at the corner, sheared off: higher on its back than its front, a chunk of its top lying beside it.
        k.Cylinder(new Vector3(10, -Footing, 0.4f), new Vector3(10, 5.2f, 0.4f), 3.4f, 10, caps: false, smooth: false);
        k.Shade(0.55f);
        k.Disc(new Vector3(10, 5.2f, 0.4f), Vector3.UnitY, 3.4f, 10);
        k.Shade(1 / 0.55f);
        k.Box(new Vector3(8.0f, 5.2f, 1.6f), new Vector3(12.6f, 7.4f, 3.6f), Kit.Faces.All & ~Kit.Faces.NegY);
        Block(k, new Vector3(14.6f, 0.6f, -2.4f), new Vector3(1.6f, 0.9f, 1.3f), 0.5f, 0.3f);
        // The side wall, only begun: knee to shoulder high, back from the line, ending in the stone waiting to be laid.
        Wall(k, rng, 0, 12, 10, 0.9f, 2.2f, alongZ: true);
        for (int i = 0; i < 6; i++)
            Block(k, new Vector3(9 + (i % 3) * 1.25f, 0.35f + i / 3 * 0.7f, 14.4f + (float)rng.NextDouble() * 0.2f), new Vector3(0.6f, 0.35f, 0.45f), 0, 0.05f);
        // Its scaffolding: poles and a plank stage along the side wall, the far end fallen in.
        Scaffold(k, rng, 10 - Thick / 2 - 0.5f, 2.4f, 11.5f, from: 0.5f);
        // The trodden run: rubble where the wall stood, slabs thrown out, the footprint pressed across it.
        Rubble(k, rng, -7, 0, 0, 22);
        Stone(k);
        Block(k, new Vector3(-4.5f, 0.5f, -5.5f), new Vector3(2.2f, 0.4f, 1.4f), 0.35f, 0.6f);
        Block(k, new Vector3(-9.5f, 0.6f, -4.0f), new Vector3(1.8f, 0.45f, 1.2f), -0.4f, -0.5f);
        Footprint(k, new Vector3(-3.5f, 0, -0.5f), 0.25f);
    }

    /// <summary>
    /// A gatehouse begun: two tower stumps either side of where the arch was to go, a wall stub out from each, the scaffolding
    /// and the hoist still up on one. The left stub stamped flat, the footprint across the gateway; the gate's iron leaf down
    /// in the mud in front of it.
    /// </summary>
    static void Gatehouse(Kit k, Random rng)
    {
        Stone(k);
        k.Box(new Vector3(-7.5f, -Footing, -2.0f), new Vector3(-3.5f, 6.4f, 2.4f), Kit.Faces.All & ~Kit.Faces.NegY);
        k.Box(new Vector3(-7.2f, 6.4f, -1.7f), new Vector3(-5.6f, 7.1f, 0.2f), Kit.Faces.All & ~Kit.Faces.NegY);
        k.Box(new Vector3(3.5f, -Footing, -2.0f), new Vector3(7.5f, 3.6f, 2.4f), Kit.Faces.All & ~Kit.Faces.NegY);
        // The right stub, built to four metres and stopped.
        Wall(k, rng, 7.5f, 17, 0.2f, 2.2f, 4.2f);
        // The left stub trodden into the ground, what's left of it a course or two high.
        Wall(k, rng, -17, -12.5f, 0.2f, 0.6f, 1.6f);
        Rubble(k, rng, -12.5f, -7.5f, 0.2f, 18);
        // The gate's iron leaf, never hung, lying in the mud where the foot threw it.
        k.Use("iron_plate", Palette.IronGrey, 0.9f, 0.35f, tile: 1.5f);
        k.Tint = new Vector3(1.0f, 0.75f, 0.6f);
        k.Push(Matrix4x4.CreateRotationX(0.08f) * Matrix4x4.CreateRotationY(0.35f) * Kit.At(-1.0f, 0.15f, -7.0f));
        k.Box(new Vector3(-2.2f, 0, -2.6f), new Vector3(2.2f, 0.18f, 2.6f), Kit.Faces.All);
        k.Use("rust_heavy", Palette.RustRed, 0.9f, 0.2f, tile: 1);
        foreach (float x in new[] { -1.4f, 0f, 1.4f })
            k.Box(new Vector3(x - 0.08f, 0.18f, -2.6f), new Vector3(x + 0.08f, 0.3f, 2.6f), Kit.Faces.All & ~Kit.Faces.NegY);
        k.Pop();
        k.Tint = Vector3.One;
        // Scaffolding round the right stump, and the shear legs that lifted its stone, a block still in the sling.
        Scaffold(k, rng, -3.0f, 2.8f, 4.4f, alongX: true, from: 3.3f);
        k.Use("wood_grey", Palette.DeepBrown, 0.9f, 0.05f, tile: 1);
        var apex = new Vector3(5.5f, 7.6f, 4.8f);
        k.Rod(new Vector3(3.4f, 0, 7.2f), apex, 0.12f, 6);
        k.Rod(new Vector3(7.6f, 0, 7.2f), apex, 0.12f, 6);
        k.Rod(new Vector3(5.5f, 3.6f, 2.2f), apex, 0.1f, 6);
        k.Use("leather", Palette.DeepBrown, 0.9f, 0.05f, tile: 1);
        k.Rod(apex, apex - new Vector3(0, 4.2f, 0), 0.03f);
        Stone(k);
        Block(k, apex - new Vector3(0, 4.7f, 0), new Vector3(0.5f, 0.35f, 0.4f), 0.2f, 0);
        // The footprint across the gateway.
        Footprint(k, new Vector3(-6.5f, 0, -1.0f), -0.2f);
    }

    static void Stone(Kit k)
    {
        k.Use("stone_block", Palette.Charcoal, 0.8f, 0.1f, tile: 2.5f);
        k.Tint = Vector3.One;
    }

    /// <summary>
    /// A run of new wall from <paramref name="a"/> to <paramref name="b"/> (along x at z, or along z at x), a mason's stretch
    /// at a time, each built to its own height between <paramref name="low"/> and <paramref name="high"/>: unfinished,
    /// stepping where they stopped.
    /// </summary>
    static void Wall(Kit k, Random rng, float a, float b, float at, float low, float high, bool alongZ = false)
    {
        Stone(k);
        for (float u = a; u < b - 0.05f; u += Stretch)
        {
            float u1 = MathF.Min(b, u + Stretch), top = low + (float)rng.NextDouble() * (high - low);
            var lo = alongZ ? new Vector3(at - Thick / 2, -Footing, u) : new Vector3(u, -Footing, at - Thick / 2);
            var hi = alongZ ? new Vector3(at + Thick / 2, top, u1) : new Vector3(u1, top, at + Thick / 2);
            k.Box(lo, hi, Kit.Faces.All & ~Kit.Faces.NegY);
        }
    }

    /// <summary>A dressed block lying where it fell: centred at <paramref name="c"/>, its half sizes, tipped about z and x.</summary>
    static void Block(Kit k, Vector3 c, Vector3 half, float tipZ, float tipX)
    {
        k.Push(Matrix4x4.CreateRotationZ(tipZ) * Matrix4x4.CreateRotationX(tipX) * Kit.At(c));
        k.Box(-half, half, Kit.Faces.All);
        k.Pop();
    }

    /// <summary>The trodden run's rubble: stone thrown down in a low bank along where the wall stood, and out from it.</summary>
    static void Rubble(Kit k, Random rng, float x0, float x1, float z, int count)
    {
        Stone(k);
        k.Shade(0.8f);
        for (int i = 0; i < count; i++)
        {
            float x = x0 + (float)rng.NextDouble() * (x1 - x0), dz = ((float)rng.NextDouble() - 0.5f) * 6;
            var half = new Vector3(0.35f + (float)rng.NextDouble() * 0.5f, 0.2f + (float)rng.NextDouble() * 0.3f, 0.3f + (float)rng.NextDouble() * 0.4f);
            Block(k, new Vector3(x, half.Y * 0.6f, z + dz), half, ((float)rng.NextDouble() - 0.5f) * 1.2f, ((float)rng.NextDouble() - 0.5f) * 1.2f);
        }
        k.Shade(1 / 0.8f);
    }

    /// <summary>
    /// A Waker's footprint pressed into the ground (wakers.md: a titan on two legs): the earth shoved up round it in a ridge, a
    /// heel and three toes, nine metres heel to toe, its long way across the wall it came down on, turned by <paramref name="yaw"/>.
    /// The ground inside it is the land's own, under the ridge's lip.
    /// </summary>
    static void Footprint(Kit k, Vector3 heel, float yaw)
    {
        k.Use("ground_mud", Palette.DeepBrown, 0.95f, 0.15f, tile: 3);
        k.Tint = new Vector3(0.75f, 0.7f, 0.65f);
        k.Push(Matrix4x4.CreateRotationY(yaw) * Kit.At(heel));
        // The ridge round the heel and up both sides of the sole, shoved up and out.
        for (int i = 0; i <= 10; i++)
        {
            float a = MathF.PI * (0.15f + 0.7f * i / 10f);
            var p = new Vector3(MathF.Cos(a) * 2.6f, 0, -MathF.Sin(a) * 2.2f);
            Mound(k, p, 1.0f, 0.75f);
        }
        for (int i = 0; i < 4; i++)
        {
            Mound(k, new Vector3(-2.9f, 0, 0.6f + i * 1.3f), 1.0f, 0.8f);
            Mound(k, new Vector3(2.9f, 0, 0.6f + i * 1.3f), 1.0f, 0.8f);
        }
        // The three toes, each its own mound pushed up ahead of it.
        foreach (var (x, z) in new[] { (-2.0f, 7.0f), (0.2f, 7.8f), (2.3f, 6.9f) })
            Mound(k, new Vector3(x, 0, z), 1.3f, 1.0f);
        k.Pop();
        k.Tint = Vector3.One;
    }

    static void Mound(Kit k, Vector3 at, float radius, float height) =>
        k.Lathe(at - new Vector3(0, 0.4f, 0), [new(radius * 1.25f, 0), new(radius, height * 0.6f), new(radius * 0.45f, height), new(0, height * 1.08f)], 7);

    /// <summary>
    /// Scaffolding: two rows of poles with ledgers and a plank stage at <paramref name="stage"/>, beside a wall begun, from
    /// along <paramref name="from"/> for <paramref name="length"/> (along z at x = <paramref name="at"/>, or along x); its far
    /// third fallen in, the poles leant over.
    /// </summary>
    static void Scaffold(Kit k, Random rng, float at, float stage, float length, bool alongX = false, float from = 0)
    {
        k.Use("wood_grey", Palette.DeepBrown, 0.9f, 0.05f, tile: 1);
        Vector3 P(float u, float y, float w) => alongX ? new Vector3(from + u, y, at + w) : new Vector3(at + w, y, from + u);
        int poles = (int)(length / 2.2f) + 1;
        for (int i = 0; i < poles; i++)
        {
            float u = i * length / (poles - 1);
            bool fallen = i >= poles * 2 / 3;
            float lean = fallen ? 0.9f + (float)rng.NextDouble() * 0.4f : 0;
            foreach (float w in new[] { -1.0f, 0.4f })
            {
                var foot = P(u, -0.3f, w);
                var top = P(u, stage + 1.4f, w) + (fallen ? new Vector3(alongX ? 0 : -1, 0, alongX ? -1 : 0) * lean * stage : Vector3.Zero);
                if (fallen)
                    top.Y = stage * 0.45f;
                k.Rod(foot, top, 0.07f, 5);
            }
        }
        // The ledger and the stage over the standing two thirds.
        float stood = length * 2 / 3 - 0.5f;
        k.Rod(P(0, stage, -1.0f), P(stood, stage, -1.0f), 0.05f, 4);
        k.Rod(P(0, stage, 0.4f), P(stood, stage, 0.4f), 0.05f, 4);
        var a = P(0, stage + 0.05f, -1.1f);
        var b = P(stood, stage + 0.12f, 0.5f);
        k.Box(Vector3.Min(a, b), Vector3.Max(a, b), Kit.Faces.All);
        // The fallen third's planks, down against the wall's foot.
        for (int i = 0; i < 3; i++)
        {
            var c = P(stood + 1.2f + i * 1.3f, 0.25f + i * 0.2f, -1.6f);
            k.Push(Matrix4x4.CreateRotationZ(0.3f - i * 0.25f) * Kit.At(c));
            k.Box(new Vector3(-1.2f, -0.05f, -0.15f), new Vector3(1.2f, 0.05f, 0.15f), Kit.Faces.All);
            k.Pop();
        }
    }
}
