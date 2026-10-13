using DarkTerritory.Sim.LineGen;

namespace DarkTerritory.Game.Art;

/// <summary>
/// The Corruption's trees (the director, 13 Oct 2026, from eight renders; ARCHITECTURE §8 note 696;
/// tools/models/recipes/corrupted_trees.py): eight kinds, three of each, so no two you pass running are the same tree.
/// Which kind a corrupted tree is, is its biome's to weigh (biomes.json <c>corruptedKinds</c>: barnacled spruce on the
/// coast, iron-spiked trunks in the colliery towns, tar in the tar ponds), and which of the three, its own seed's; both
/// only seen, so they're drawn from the tree's art seed, never the Sim's stream. Each is a prop built 12 m tall (its
/// foot at its origin), drawn at the Sim's height, and a distance copy (<c>_far</c>) for the trees out past the
/// modelled ones' reach.
/// </summary>
public static class CorruptedTreeArt
{
    /// <summary>The kinds, as the recipe names them (ctree_&lt;kind&gt;_&lt;n&gt;).</summary>
    public static readonly string[] Kinds = ["barnacle", "lantern", "tar", "iron", "crystal", "webbed", "thorn", "lattice"];

    /// <summary>How many of each kind there are.</summary>
    public const int Variants = 3;

    /// <summary>How tall they're built (m): drawn at the Sim's height over this.</summary>
    public const float BuiltHeight = 12;

    /// <summary>
    /// How far out from the line they're modelled (m); past it, their cards (tools/models/impostor.py: 8 triangles, a
    /// picture of the tree from two sides). Nearer in than the spruce's 40 m: a corrupted tree is 2,000-4,000 triangles
    /// (the director's second pass made them so: more of the Corruption on them) where a modelled spruce is 1,300 and a dead
    /// one a card of 8. To 24 m, its cards cheaper than the spruce cards they stand for, the black forest's frame is 2-6%
    /// under what it was without them (dt perf --route frontier:7 --at 4700, note 696; to 28 m it was 2-4% over). The
    /// cards hold up from here out in the fog.
    /// </summary>
    public const double ModelledWithinM = 24;

    /// <summary>The prop a kind's variant is (its distance copy when <paramref name="far"/>).</summary>
    public static string Prop(string kind, int variant, bool far) => $"ctree_{kind}_{variant}" + (far ? "_far" : "");

    /// <summary>
    /// The kind and variant a corrupted tree is drawn as, from its art seed and its biome's weights (every kind alike where
    /// the biome gives none). The kinds are weighed in a fixed order, so it's the same tree on every machine and every run.
    /// </summary>
    public static (string Kind, int Variant) Pick(uint seed, BiomeDef? biome)
    {
        uint h = Mix(seed ^ 0x6F9E_2B41u);
        double u = (h >> 8) / (double)(1 << 24);
        int variant = (int)(Mix(h ^ 0x51ED_270Bu) % Variants);
        var weights = biome?.CorruptedKinds;
        double total = 0;
        foreach (var k in Kinds)
            total += Weight(weights, k);
        if (total <= 0)
            return (Kinds[(int)(h % (uint)Kinds.Length)], variant);
        double pick = u * total;
        foreach (var k in Kinds)
            if ((pick -= Weight(weights, k)) < 0)
                return (k, variant);
        return (Kinds[^1], variant);
    }

    /// <summary>
    /// Whether a tree that's only the art's (a stand's crossed cards out past the Sim's woods) is one of the Corruption's,
    /// at its biome's share (biomes.json corruptedTrees), and its art seed if it is: hashed from where it stands, so the
    /// trees round it draw as they did.
    /// </summary>
    public static uint? Blighted(double along, double lateral, double share)
    {
        if (share <= 0)
            return null;
        uint h = Mix(unchecked((uint)(long)Math.Round(along * 100) * 0x9E37_79B1u ^ (uint)(long)Math.Round(lateral * 100) * 0x85EB_CA77u));
        return (h >> 8) / (double)(1 << 24) < share ? Mix(h ^ 0xB1E5_7EEDu) : null;
    }

    static double Weight(IReadOnlyDictionary<string, double>? weights, string kind) =>
        weights is null ? 1 : weights.TryGetValue(kind, out var w) ? Math.Max(0, w) : 0;

    /// <summary>A 32-bit finaliser (murmur3's): a seed's bits spread over the whole word.</summary>
    static uint Mix(uint x)
    {
        x ^= x >> 16;
        x *= 0x85EB_CA6Bu;
        x ^= x >> 13;
        x *= 0xC2B2_AE35u;
        x ^= x >> 16;
        return x;
    }
}
