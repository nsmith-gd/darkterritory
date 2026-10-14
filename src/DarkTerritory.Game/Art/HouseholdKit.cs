using System.Numerics;
using Ballast.Render;

namespace DarkTerritory.Game.Art;

/// <summary>
/// What the house creatures' houses have that the others don't (G1's notes 584–586; the director, 9 Oct 2026). The
/// Householder's (note 584): its chair, its table set for one (a plate, a knife and fork, a cup, a candle burning in its
/// stick), and an iron stove lit against a wall, all of it whole and kept in a village that's been gone through. The
/// Hanger's (note 586): the ceiling's joists at <see cref="Sim.Enemies.Hanger.Ceiling"/>, most of its boards gone, the
/// rafters over them up into the dark of the roof, where it lives. Each piece in its own frame: its middle on the floor, +Y
/// up, facing −Z; cooked once and drawn by transform.
/// </summary>
public sealed class HouseholdKit(Look? look)
{
    readonly Dictionary<string, MeshAsset> _pieces = [];

    MeshAsset Get(string key, Action<Kit> build)
    {
        if (_pieces.TryGetValue(key, out var piece))
            return piece;
        var k = new Kit(look, 5840 + key.Length);
        build(k);
        return _pieces[key] = k.Build("household." + key);
    }

    static readonly Vector3 Polish = new(0.36f, 0.22f, 0.13f), Linen = new(0.82f, 0.79f, 0.7f), Iron = new(0.09f, 0.085f, 0.08f);

    /// <summary>The table's top, how high it stands, and how big (half its width across the sitter, half its depth).</summary>
    public const float TableHeight = 0.74f, TableHalfWidth = 0.45f, TableHalfDepth = 0.34f;

    /// <summary>Where the candle's flame is on the set table (its frame).</summary>
    public static readonly Vector3 TableFlame = new(0.12f, TableHeight + 0.27f, -0.16f);

    /// <summary>
    /// The table, set for one (its frame: the sitter's side at +Z, so it's turned as its chair is): a scrubbed deal table with
    /// a cloth over it, laid square and clean. A plate at the sitter's place with the knife to its right and the fork to its
    /// left, a cup and its saucer to the right, a candle in a brass stick across from the plate, lit, its wax run down.
    /// </summary>
    public MeshAsset SetTable => Get("table", k =>
    {
        const float H = TableHeight, W = TableHalfWidth, D = TableHalfDepth;
        // The legs and the top, polished dark with use.
        k.Use("wood_grey", Polish, 0.35f, 0.25f, tile: 1);
        foreach (float x in new[] { -W + 0.05f, W - 0.05f })
            foreach (float z in new[] { -D + 0.05f, D - 0.05f })
                k.Box(new Vector3(x - 0.03f, 0, z - 0.03f), new Vector3(x + 0.03f, H - 0.04f, z + 0.03f));
        k.Box(new Vector3(-W + 0.03f, H - 0.12f, -D + 0.03f), new Vector3(W - 0.03f, H - 0.04f, D - 0.03f), Kit.Faces.All & ~Kit.Faces.PosY);
        k.Box(new Vector3(-W, H - 0.04f, -D), new Vector3(W, H, D));
        // The cloth: laid over it, hanging a hand's breadth down every side, its hem straight; a deep red, kept.
        k.Use("cream", Linen, 0.25f, 0.05f, tile: 0.6f);
        k.Tint = new Vector3(0.62f, 0.2f, 0.17f);
        k.Box(new Vector3(-W - 0.012f, H, -D - 0.012f), new Vector3(W + 0.012f, H + 0.004f, D + 0.012f));
        k.Box(new Vector3(-W - 0.014f, H - 0.1f, -D - 0.014f), new Vector3(W + 0.014f, H + 0.002f, D + 0.014f), Kit.Faces.All & ~Kit.Faces.PosY & ~Kit.Faces.NegY);
        // The plate (white enamel, a dark blue rim), at the sitter's place.
        float top = H + 0.004f;
        var plate = new Vector3(0, top, D - 0.17f);
        k.Use("cream", new Vector3(0.88f, 0.87f, 0.84f), 0.15f, 0.5f, tile: 0.3f);
        k.Lathe(plate, [new(0.08f, 0), new(0.11f, 0.008f), new(0.125f, 0.02f)], 16, smooth: true, capTop: true);
        k.Use("paint_black", new Vector3(0.1f, 0.14f, 0.3f), 0.2f, 0.5f);
        k.Lathe(plate + new Vector3(0, 0.019f, 0), [new(0.125f, 0), new(0.13f, 0.004f)], 16, capTop: false);
        // The knife to its right and the fork to its left, laid straight (the sitter faces −Z: their right is −X).
        k.Use("rust_heavy", new Vector3(0.6f, 0.6f, 0.58f), 0.2f, 0.7f, tile: 0.2f);
        k.Box(new Vector3(-0.17f, top, plate.Z - 0.11f), new Vector3(-0.155f, top + 0.006f, plate.Z + 0.1f));
        k.Box(new Vector3(0.155f, top, plate.Z - 0.11f), new Vector3(0.17f, top + 0.006f, plate.Z + 0.06f));
        foreach (float x in new[] { 0.152f, 0.159f, 0.166f, 0.173f })
            k.Box(new Vector3(x - 0.002f, top, plate.Z + 0.06f), new Vector3(x + 0.002f, top + 0.005f, plate.Z + 0.1f));
        k.Use("wood_grey", Polish * 0.6f, 0.3f, 0.2f, tile: 1);
        k.Box(new Vector3(-0.172f, top, plate.Z + 0.1f), new Vector3(-0.153f, top + 0.012f, plate.Z + 0.19f));
        // The cup on its saucer, to the right; its handle out.
        var cup = new Vector3(-0.26f, top, plate.Z - 0.12f);
        k.Use("cream", new Vector3(0.88f, 0.87f, 0.84f), 0.15f, 0.5f, tile: 0.3f);
        k.Lathe(cup, [new(0.045f, 0), new(0.065f, 0.012f)], 12);
        k.Lathe(cup + new Vector3(0, 0.012f, 0), [new(0.028f, 0), new(0.04f, 0.03f), new(0.046f, 0.07f)], 12, capTop: false);
        k.Use("paint_black", new Vector3(0.12f, 0.07f, 0.04f), 0.3f, 0.6f);
        k.Disc(cup + new Vector3(0, 0.068f, 0), Vector3.UnitY, 0.043f, 12);
        k.Use("cream", new Vector3(0.88f, 0.87f, 0.84f), 0.15f, 0.5f, tile: 0.3f);
        k.Rod(cup + new Vector3(-0.045f, 0.06f, 0), cup + new Vector3(-0.068f, 0.045f, 0), 0.006f);
        k.Rod(cup + new Vector3(-0.068f, 0.045f, 0), cup + new Vector3(-0.045f, 0.025f, 0), 0.006f);
        // The candle across from the plate, in a brass stick: lit, its wax run down one side.
        var stick = new Vector3(TableFlame.X, top, TableFlame.Z);
        k.Use("brass", new Vector3(0.6f, 0.45f, 0.18f), 0.3f, 0.7f, tile: 0.3f);
        k.Lathe(stick, [new(0.06f, 0), new(0.055f, 0.012f), new(0.015f, 0.03f), new(0.012f, 0.09f), new(0.03f, 0.1f), new(0.022f, 0.11f)], 12);
        k.Use("plaster_ruin", new Vector3(0.86f, 0.82f, 0.7f), 0.2f, 0.2f, tile: 0.2f);
        k.Cylinder(stick + new Vector3(0, 0.11f, 0), stick + new Vector3(0, TableFlame.Y - top - 0.035f, 0), 0.014f, 8);
        k.Cylinder(stick + new Vector3(0.012f, 0.1f, 0.004f), stick + new Vector3(0.012f, 0.19f, 0.004f), 0.006f, 5);
        k.Use("lamp_lens", Palette.LampAmber, 0, 0, tile: 0.25f);
        k.Emissive = 1;
        k.Cylinder(TableFlame - new Vector3(0, 0.03f, 0), TableFlame + new Vector3(0, 0.02f, 0), 0.008f, 6, radiusB: 0.001f);
        k.Emissive = 0;
        // A heel of bread on a board beside the plate: the meal's been laid, and waited for.
        k.Use("wood_grey", Polish * 1.3f, 0.5f, 0.1f, tile: 1);
        k.Box(new Vector3(0.2f, top, -0.05f), new Vector3(0.36f, top + 0.015f, 0.06f));
        k.Use("sac", new Vector3(0.52f, 0.36f, 0.18f), 0.6f, 0.05f, tile: 0.3f);
        k.Lathe(new Vector3(0.28f, top + 0.015f, 0.005f), [new(0.05f, 0), new(0.05f, 0.03f), new(0.03f, 0.05f)], 8);
    });

    /// <summary>
    /// Its chair: a plain upright one, stood square at the table (its frame: the seat's middle over the origin, facing −Z, its
    /// back behind, at +Z). The seat <see cref="SeatHeight"/> up.
    /// </summary>
    public MeshAsset Chair => Get("chair", k =>
    {
        const float S = SeatHeight;
        k.Use("wood_grey", Polish, 0.35f, 0.25f, tile: 1);
        k.Box(new Vector3(-0.21f, S - 0.04f, -0.2f), new Vector3(0.21f, S, 0.2f));
        foreach (float x in new[] { -0.18f, 0.18f })
            foreach (float z in new[] { -0.17f, 0.17f })
                k.Box(new Vector3(x - 0.02f, 0, z - 0.02f), new Vector3(x + 0.02f, z > 0 ? 1.0f : S - 0.04f, z + 0.02f));
        // Its back: two rails and three spindles between the posts.
        foreach (float y in new[] { 0.72f, 0.94f })
            k.Box(new Vector3(-0.18f, y, 0.15f), new Vector3(0.18f, y + 0.05f, 0.19f));
        foreach (float x in new[] { -0.08f, 0, 0.08f })
            k.Box(new Vector3(x - 0.012f, S, 0.16f), new Vector3(x + 0.012f, 0.72f, 0.18f));
        foreach (float x in new[] { -0.18f, 0.18f })
            k.Box(new Vector3(x - 0.012f, 0.12f, -0.17f), new Vector3(x + 0.012f, 0.15f, 0.17f));
    });

    /// <summary>How high the chair's seat is (Householder: 0.45 m).</summary>
    public const float SeatHeight = 0.45f;

    /// <summary>The stove's half width and depth, and where its fire's mouth is (its frame).</summary>
    public const float StoveHalfWidth = 0.32f, StoveHalfDepth = 0.28f;
    public static readonly Vector3 StoveFire = new(0, 0.42f, -StoveHalfDepth - 0.02f);

    /// <summary>
    /// A cast-iron stove stood against a wall (its frame: its front to −Z, the wall at +Z behind it): the firebox on four
    /// short legs, its door's grate glowing, a hob plate on top with a kettle on it, and the flue going up and back into the
    /// wall. Lit: the grate's slots and the gap round the door glow (emissive), the rest black iron.
    /// </summary>
    public MeshAsset Stove => Get("stove", k =>
    {
        const float W = StoveHalfWidth, D = StoveHalfDepth, Legs = 0.14f, Top = 0.78f;
        k.Use("paint_black", Iron, 0.4f, 0.45f, tile: 0.8f);
        foreach (float x in new[] { -W + 0.05f, W - 0.05f })
            foreach (float z in new[] { -D + 0.05f, D - 0.05f })
                k.Cylinder(new Vector3(x, 0, z), new Vector3(x, Legs, z), 0.025f, 6, radiusB: 0.035f);
        k.Box(new Vector3(-W, Legs, -D), new Vector3(W, Top - 0.03f, D));
        // A lip round its top and the hob plate, a ring for the kettle.
        k.Box(new Vector3(-W - 0.025f, Top - 0.03f, -D - 0.025f), new Vector3(W + 0.025f, Top, D + 0.025f));
        k.Lathe(new Vector3(-0.12f, Top, -0.05f), [new(0.1f, 0), new(0.105f, 0.008f)], 12);
        // The ash pan's drawer low in front, and the door over it with its hinges and its latch.
        k.Box(new Vector3(-W + 0.06f, Legs + 0.02f, -D - 0.02f), new Vector3(W - 0.06f, Legs + 0.1f, -D));
        k.Box(new Vector3(-0.2f, 0.27f, -D - 0.035f), new Vector3(0.2f, 0.58f, -D - 0.015f), Kit.Faces.All & ~Kit.Faces.NegZ);
        foreach (float y in new[] { 0.31f, 0.54f })
            k.Box(new Vector3(-0.23f, y - 0.02f, -D - 0.03f), new Vector3(-0.19f, y + 0.02f, -D - 0.01f));
        k.Box(new Vector3(0.17f, 0.41f, -D - 0.06f), new Vector3(0.24f, 0.44f, -D - 0.03f));
        // The door's face with its grate: iron bars over the fire behind, the slots lit.
        k.Box(new Vector3(-0.2f, 0.27f, -D - 0.036f), new Vector3(-0.15f, 0.58f, -D - 0.035f), Kit.Faces.NegZ);
        k.Box(new Vector3(0.15f, 0.27f, -D - 0.036f), new Vector3(0.2f, 0.58f, -D - 0.035f), Kit.Faces.NegZ);
        k.Box(new Vector3(-0.15f, 0.27f, -D - 0.036f), new Vector3(0.15f, 0.34f, -D - 0.035f), Kit.Faces.NegZ);
        k.Box(new Vector3(-0.15f, 0.5f, -D - 0.036f), new Vector3(0.15f, 0.58f, -D - 0.035f), Kit.Faces.NegZ);
        for (int i = 0; i < 5; i++)
        {
            float x = -0.12f + i * 0.06f;
            k.Box(new Vector3(x - 0.012f, 0.34f, -D - 0.045f), new Vector3(x + 0.012f, 0.5f, -D - 0.03f));
        }
        k.Use("firebox", new Vector3(1.0f, 0.45f, 0.12f), 0, 0, tile: 0.4f);
        k.Emissive = 1;
        k.Box(new Vector3(-0.15f, 0.34f, -D - 0.034f), new Vector3(0.15f, 0.5f, -D - 0.033f), Kit.Faces.NegZ);
        // The hob's ring and the draught holes down its sides, hot from the fire: it reads lit from any side.
        k.Lathe(new Vector3(-0.12f, Top + 0.001f, -0.05f), [new(0.085f, 0), new(0.1f, 0.002f)], 16, capTop: false);
        foreach (float x in new[] { -W - 0.001f, W + 0.001f })
            for (int i = 0; i < 4; i++)
            {
                float z = -D + 0.1f + i * 0.12f;
                k.Box(new Vector3(x - 0.001f, 0.28f, z - 0.03f), new Vector3(x + 0.001f, 0.34f, z + 0.03f));
            }
        // The fire's light leaking round the door's edge, and the ash pan's draught.
        k.Box(new Vector3(-0.2f, 0.265f, -D - 0.016f), new Vector3(0.2f, 0.272f, -D - 0.014f), Kit.Faces.NegZ);
        k.Box(new Vector3(-W + 0.07f, Legs + 0.1f, -D - 0.012f), new Vector3(W - 0.07f, Legs + 0.11f, -D - 0.011f), Kit.Faces.NegZ);
        k.Emissive = 0;
        // The flue: up off the back of the top, then an elbow back into the wall behind.
        k.Use("rust_heavy", Iron * 1.3f, 0.5f, 0.35f, tile: 0.8f);
        var flue = new Vector3(0.12f, Top, D - 0.12f);
        k.Cylinder(flue, flue + new Vector3(0, 1.45f, 0), 0.065f, 10);
        k.Cylinder(flue + new Vector3(0, 1.38f, -0.0f), flue + new Vector3(0, 1.45f, 0.32f), 0.065f, 10);
        k.Cylinder(flue + new Vector3(0, 1.45f, 0.18f), flue + new Vector3(0, 1.45f, 0.3f), 0.09f, 10);
        k.Cylinder(flue + new Vector3(0, 0.5f, 0), flue + new Vector3(0, 0.54f, 0), 0.075f, 10);
        // The kettle on the hob, black, its handle up.
        var kettle = new Vector3(-0.12f, Top + 0.008f, -0.05f);
        k.Use("rust_heavy", Iron * 1.6f, 0.4f, 0.5f, tile: 0.5f);
        k.Lathe(kettle, [new(0.09f, 0), new(0.11f, 0.06f), new(0.09f, 0.13f), new(0.03f, 0.16f)], 12);
        k.Rod(kettle + new Vector3(0.09f, 0.05f, 0), kettle + new Vector3(0.17f, 0.13f, 0), 0.012f);
        k.Rod(kettle + new Vector3(-0.07f, 0.15f, 0), kettle + new Vector3(0, 0.24f, 0), 0.006f);
        k.Rod(kettle + new Vector3(0, 0.24f, 0), kettle + new Vector3(0.07f, 0.15f, 0), 0.006f);
        // A rag rug in front of it, where the feet go.
        k.Use("wool", new Vector3(0.42f, 0.2f, 0.14f), 0.6f, 0.02f, tile: 0.5f);
        k.Box(new Vector3(-0.45f, 0, -D - 0.85f), new Vector3(0.45f, 0.012f, -D - 0.15f));
        k.Use("wool", new Vector3(0.24f, 0.26f, 0.3f), 0.6f, 0.02f, tile: 0.5f);
        k.Box(new Vector3(-0.35f, 0.012f, -D - 0.75f), new Vector3(0.35f, 0.014f, -D - 0.25f));
    });

    /// <summary>
    /// The Hanger's ceiling (note 586) over a part of a house, in that part's frame (its middle on the floor's frame, its
    /// longer side along −Z, <paramref name="span"/> across it and <paramref name="length"/> along): joists across it every
    /// 0.6 m with their undersides at <paramref name="ceiling"/>, a few of the boards that were laid over them left in
    /// patches (the rest fallen, rotten through), and the rafters under the slates over them, up to the ridge
    /// (<paramref name="eaves"/> its walls' tops; TownKit.Gabled's pitch). Between the boards the roof space is black.
    /// </summary>
    public MeshAsset Ceiling(float span, float length, float ceiling, float eaves, int seed) =>
        Get($"ceiling.{span:0.00}.{length:0.00}.{ceiling:0.00}.{seed % 7}", k =>
        {
            float w = span / 2 - 0.2f, d = length / 2 - 0.2f, ridge = eaves + span * 0.42f;
            k.Use("wood_grey", new Vector3(0.24f, 0.18f, 0.12f), 0.9f, 0.02f, tile: 1);
            int joists = Math.Max(2, (int)(length / 0.6f));
            for (int i = 0; i <= joists; i++)
            {
                float z = -d + 2 * d * i / joists;
                k.Box(new Vector3(-w - 0.15f, ceiling, z - 0.04f), new Vector3(w + 0.15f, ceiling + 0.16f, z + 0.04f));
            }
            // What's left of the boards: short runs across a few of the joists, gapped and broken off.
            uint h = (uint)(seed * 2654435761u) | 1;
            float R()
            {
                h ^= h << 13;
                h ^= h >> 17;
                h ^= h << 5;
                return (h & 0xFFFFFF) / 16777216f;
            }
            k.Use("wood_grey", new Vector3(0.3f, 0.24f, 0.17f), 0.95f, 0.02f, tile: 1);
            for (int i = 0; i < joists; i++)
            {
                if (R() < 0.45f)
                    continue;
                float z0 = -d + 2 * d * i / joists, z1 = -d + 2 * d * (i + 1) / joists;
                int boards = 1 + (int)(R() * 4);
                float x = -w + R() * w;
                for (int b = 0; b < boards && x < w; b++)
                {
                    float bw = 0.12f + 0.05f * R();
                    float lo = R() < 0.3f ? z0 + 0.2f * R() : z0 - 0.04f, hi = R() < 0.3f ? z1 - 0.25f * R() : z1 + 0.04f;
                    k.Box(new Vector3(x, ceiling + 0.16f, lo), new Vector3(x + bw, ceiling + 0.18f, hi));
                    x += bw + (R() < 0.3f ? 0.15f + 0.4f * R() : 0.01f);
                }
            }
            // The rafters, each side up the slope from the wall plate to the ridge, a hand under the slates; a ridge beam.
            k.Use("wood_grey", new Vector3(0.2f, 0.15f, 0.1f), 0.9f, 0.02f, tile: 1);
            for (int i = 0; i <= joists; i++)
            {
                float z = -d + 2 * d * i / joists;
                foreach (float s in new[] { -1f, 1f })
                    k.Rod(new Vector3(s * (span / 2 - 0.1f), eaves - 0.08f, z), new Vector3(0, ridge - 0.12f, z), 0.04f);
            }
            k.Box(new Vector3(-0.05f, ridge - 0.22f, -d - 0.2f), new Vector3(0.05f, ridge - 0.08f, d + 0.2f));
            // A collar tie across every other pair, low in the roof space: what it hangs from when it's up there.
            for (int i = 0; i <= joists; i += 2)
            {
                const float Up = 0.45f;
                float z = -d + 2 * d * i / joists, y = eaves - 0.08f + Up * (ridge - eaves - 0.04f), x = (span / 2 - 0.1f) * (1 - Up);
                k.Rod(new Vector3(-x, y, z), new Vector3(x, y, z), 0.035f);
            }
        });
}
