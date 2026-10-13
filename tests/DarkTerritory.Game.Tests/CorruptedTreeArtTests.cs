using System.Numerics;
using Ballast;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.LineGen;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The Corruption's trees (the director, 13 Oct 2026, from eight renders; ARCHITECTURE §8 note 696;
/// tools/models/recipes/corrupted_trees.py): eight kinds, three of each and a distance copy of each, all built, in the
/// library's layers, a tree's size with its foot at its origin; the hangings swing from where they hang; and which kind a
/// tree is, is its biome's to weigh.
/// </summary>
public class CorruptedTreeArtTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly Look Look = Look.Load(Content);
    static readonly PropArt Props = PropArt.Of(Look);

    public static TheoryData<string> Kinds => [.. CorruptedTreeArt.Kinds];

    /// <summary>
    /// Each of a kind's three is built, near and far: worn in layers (the library's, its cards the kind's own; no flat
    /// fallback), about the 12 m it's drawn over (a broken top lower), its foot at its origin, the near one in a modelled
    /// tree's budget and the far one its cards, and the three not the same tree.
    /// </summary>
    [Theory]
    [MemberData(nameof(Kinds))]
    public void EachKindHasThreeTreesAndTheirDistanceCopies(string kind)
    {
        var heights = new List<float>();
        for (int v = 0; v < CorruptedTreeArt.Variants; v++)
        {
            var near = Props.Get(CorruptedTreeArt.Prop(kind, v, far: false));
            var far = Props.Get(CorruptedTreeArt.Prop(kind, v, far: true));
            Assert.True(near is not null && far is not null, $"{kind} {v}: tools/models/build.sh corrupted_trees");
            Assert.All(near!.Vertices.Concat(far!.Vertices), x => Assert.True(x.Layer >= 0, $"{kind} {v}: a vertex with no layer"));
            // Modelled about as a spruce is (WorldKit.Spruce 850-1,850), the cards two crossed quads, both faces.
            Assert.InRange(near.Triangles, 1000, 2600);
            Assert.Equal(8, far.Triangles);
            float top = near.Vertices.Max(x => x.Position.Y), foot = near.Vertices.Min(x => x.Position.Y);
            Assert.InRange(top, 8f, CorruptedTreeArt.BuiltHeight + 1.5f);
            Assert.InRange(foot, -0.6f, 0.05f);
            Assert.InRange(far.Vertices.Max(x => x.Position.Y), top * 0.85f, top * 1.1f);
            heights.Add(near.Triangles + top);
        }
        Assert.Equal(heights.Count, heights.Distinct().Count());
    }

    /// <summary>
    /// A kind's cards are one layer (six columns: its three trees from two sides each), cut out round the trees, in the
    /// look, and saying where they came from.
    /// </summary>
    [Fact]
    public void EachKindsCardsAreOneLayerCutOutRoundItsTrees()
    {
        var index = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(Content, "art", "textures", "index.models.json"))).RootElement;
        foreach (var kind in CorruptedTreeArt.Kinds)
        {
            string layer = $"ctree_{kind}_far_0";
            Assert.True(Look.Layer(layer) >= 0, layer);
            var entry = index.EnumerateArray().Single(e => e.GetProperty("name").GetString() == layer);
            Assert.True(entry.GetProperty("alphaTest").GetBoolean(), $"{layer} isn't cut out");
            Assert.Contains("corrupted_trees.py", entry.GetProperty("sources")[0].GetProperty("generator").GetString());
            for (int v = 0; v < CorruptedTreeArt.Variants; v++)
                Assert.All(Props.Get(CorruptedTreeArt.Prop(kind, v, far: true))!.Vertices, x => Assert.Equal(Look.Layer(layer), (int)x.Layer));
        }
    }

    /// <summary>
    /// What hangs off them (wrack, lichen, dead leaves) bends in the wind from where it hangs, as a bough does from the
    /// trunk: scene.vert bends a card from v 1, so a hanging card's top is at v 1 and its foot at v 0 (mat_blight keeps the
    /// cards' roots at their image's foot for this).
    /// </summary>
    [Fact]
    public void WhatHangsOffThemSwingsFromWhereItHangs()
    {
        var hung = new[] { Look.Layer("wrack_card"), Look.Layer("leaf_card") };
        Assert.All(hung, l => Assert.True(l >= 0, "the hangings' cards are in the library (tools/art/textures.py --only wrack_card,leaf_card)"));
        int cards = 0;
        foreach (var kind in new[] { "barnacle", "webbed", "lattice" })
        {
            var mesh = Props.Get(CorruptedTreeArt.Prop(kind, 0, far: false))!;
            var card = mesh.Vertices.Where(x => hung.Contains((int)x.Layer)).ToArray();
            Assert.NotEmpty(card);
            // Each triangle of a card: its highest corner the nearest its root (v 1), its lowest the nearest its tip.
            for (int i = 0; i + 2 < card.Length; i += 3)
            {
                var tri = card.AsSpan(i, 3).ToArray();
                var high = tri.MaxBy(x => x.Position.Y);
                var low = tri.MinBy(x => x.Position.Y);
                if (high.Position.Y - low.Position.Y < 0.05f)
                    continue;
                Assert.True(high.Uv.Y > low.Uv.Y, $"{kind}: a card hanging the wrong way up (v {high.Uv.Y:0.00} over {low.Uv.Y:0.00})");
                cards++;
            }
        }
        Assert.True(cards > 50);
    }

    /// <summary>
    /// Which kind is the biome's to weigh: a kind it gives no weight never grows there, the rest about as often as their
    /// weights; with no weights, all eight alike; and all three of a kind turn up. The same seed is the same tree every time.
    /// </summary>
    [Fact]
    public void WhichKindIsTheBiomesToWeigh()
    {
        var biomes = Routes.Generate(Content, "frontier:7", 6).Plan!.Rules.Biomes;
        var coast = biomes["coast"];
        Assert.NotNull(coast.CorruptedKinds);
        var seen = new Dictionary<string, int>();
        var variants = new HashSet<int>();
        const int N = 20000;
        for (uint s = 0; s < N; s++)
        {
            var (kind, v) = CorruptedTreeArt.Pick(s * 2654435761u, coast);
            seen[kind] = seen.GetValueOrDefault(kind) + 1;
            variants.Add(v);
            Assert.Equal((kind, v), CorruptedTreeArt.Pick(s * 2654435761u, coast));
        }
        double total = coast.CorruptedKinds!.Values.Sum();
        foreach (var k in CorruptedTreeArt.Kinds)
        {
            double want = coast.CorruptedKinds.GetValueOrDefault(k) / total;
            Assert.InRange(seen.GetValueOrDefault(k) / (double)N, want - 0.02, want + 0.02);
        }
        Assert.Equal([0, 1, 2], variants.Order());
        // With no weights, every kind.
        var none = Enumerable.Range(0, 4000).Select(i => CorruptedTreeArt.Pick((uint)i * 40503u, null).Kind).ToHashSet();
        Assert.Equal(CorruptedTreeArt.Kinds.Order(), none.Order());
        // The stands' own trees out past the woods are as often corrupted, by where they stand.
        int blighted = 0;
        for (int i = 0; i < 20000; i++)
            if (CorruptedTreeArt.Blighted(1000 + i * 3.7, 60 + i % 97 * 1.3, coast.CorruptedTrees) is not null)
                blighted++;
        Assert.InRange(blighted / 20000.0, coast.CorruptedTrees - 0.02, coast.CorruptedTrees + 0.02);
        Assert.Null(CorruptedTreeArt.Blighted(1000, 60, 0));
        // Every biome names only kinds there are.
        Assert.All(biomes.Values, b => Assert.All((IEnumerable<string>?)b.CorruptedKinds?.Keys ?? [], k => Assert.Contains(k, CorruptedTreeArt.Kinds)));
    }
}
