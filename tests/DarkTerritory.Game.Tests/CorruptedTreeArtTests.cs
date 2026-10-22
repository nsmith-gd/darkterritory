using System.Numerics;
using Ballast;
using Ballast.Render;
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
            // Modelled with the Corruption all over them (the director's second pass: 2,000-4,100), held under 4,500 so the
            // woods' frame stays under what it was without them (CorruptedTreeArt.ModelledWithinM); the cards two crossed
            // quads, both faces.
            Assert.InRange(near.Triangles, 1500, 4500);
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
        var hung = new[] { Look.Layer("wrack_card"), Look.Layer("leaf_card"), Look.Layer("shroud_card") };
        Assert.All(hung, l => Assert.True(l >= 0, "the hangings' cards are in the library (tools/art/textures.py --only wrack_card,leaf_card,shroud_card)"));
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
    /// The tar's drips sag from where they hang and draw back up, and the lantern's pods breathe, in the shaders alone
    /// (GreyboxRenderer.Motion 3 and 4): a drip's v is 1 at its root and falls as it hangs, a pod's is any; the whole part of
    /// u says which drip or pod a vertex is of, so each moves as one, and each on its own (dozens of them a tree).
    /// </summary>
    [Fact]
    public void TheTarsDripsSagFromTheirRootsAndTheLanternsPodsBreatheEachOnItsOwn()
    {
        int drip = Look.Layer("tar_drip"), pod = Look.Layer("glow_pod");
        Assert.True(drip >= 0 && pod >= 0, "tools/art/textures.py --only tar_drip,glow_pod");
        Assert.Equal(3, GreyboxRenderer.Motion("tar_drip"));
        Assert.Equal(4, GreyboxRenderer.Motion("glow_pod"));
        for (int v = 0; v < CorruptedTreeArt.Variants; v++)
        {
            var drips = Props.Get(CorruptedTreeArt.Prop("tar", v, far: false))!.Vertices.Where(x => (int)x.Layer == drip)
                .GroupBy(x => (int)MathF.Floor(x.Uv.X)).ToArray();
            Assert.True(drips.Length >= 30, $"tar {v}: {drips.Length} drips");
            foreach (var d in drips)
            {
                var root = d.MaxBy(x => x.Position.Y);
                var tip = d.MinBy(x => x.Position.Y);
                // Its root the top of it at v ~1; its tip the lowest, a quarter of the metres down in v (scene.vert).
                Assert.InRange(root.Uv.Y, 0.99f, 1.0f);
                float hang = root.Position.Y - tip.Position.Y;
                Assert.InRange(root.Uv.Y - tip.Uv.Y, hang / 4 - 0.02f, hang / 4 + 0.02f);
                Assert.True(tip.Position.Y - hang * hang / 16 * 1.2f > -0.05f, $"tar {v}: drip {d.Key} sags into the ground");
                Assert.True(d.Max(x => MathF.Abs(x.Position.X - root.Position.X)) < 0.3f, $"tar {v}: drip {d.Key} is more than one drip");
            }
            var pods = Props.Get(CorruptedTreeArt.Prop("lantern", v, far: false))!.Vertices.Where(x => (int)x.Layer == pod)
                .GroupBy(x => (int)MathF.Floor(x.Uv.X)).ToArray();
            Assert.True(pods.Length >= 25, $"lantern {v}: {pods.Length} pods");
            // One pod, or one cluster of bonnets, each.
            Assert.All(pods, p => Assert.True(p.Max(x => x.Position.Y) - p.Min(x => x.Position.Y) < 0.7f, $"lantern {v}: pod {p.Key}"));
        }
    }

    /// <summary>
    /// Which kind is the biome's to weigh: a kind it gives no weight never grows there, the rest about as often as their
    /// weights; with no weights, all eight alike; and all three of a kind turn up. The same seed is the same tree every time.
    /// </summary>
    /// <summary>
    /// Their light (note 757; the director, 14 Oct, `docs/playtest/2026-10-14.md` line 5: "we're going to want a few more like
    /// bioluminescent corruptions because they really stand out in the night. Oh, the crystal tree ... We could maybe have
    /// that glow a little bit"). Besides the lantern tree's pods and the barnacle's mouth: the crystal tree's crystals, the
    /// thorn tree's cocoons and the webbed birch's veins glow, and faintly (GDD §28: "Less is more"), less than the pods.
    /// The iron and the tar trees don't.
    /// </summary>
    [Fact]
    public void TheCrystalsTheCocoonsAndTheVeinsGlowFaintlyAndTheLanternsPodsMost()
    {
        float Most(string kind) => Enumerable.Range(0, CorruptedTreeArt.Variants)
            .Max(v => Props.Get(CorruptedTreeArt.Prop(kind, v, far: false))!.Vertices.Max(x => x.Emissive));
        float pods = Most("lantern");
        foreach (var kind in new[] { "crystal", "thorn", "webbed" })
            Assert.InRange(Most(kind), 0.1f, MathF.Min(0.45f, pods));
        Assert.Equal(0, Most("iron"));
        Assert.Equal(0, Most("tar"));
    }

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
