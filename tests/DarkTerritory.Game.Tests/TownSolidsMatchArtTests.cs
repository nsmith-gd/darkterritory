using System.Numerics;
using Ballast;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Towns;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// What's walked into is what's drawn in the starting town (queue #596, ARCHITECTURE §8 note 869; the director's playtest of
/// 19 Oct, line 2: "Collision boxes are off"). The works' heaps stood a box from the ground over their whole footprint (a 4 m
/// wall round the slag heap's foot); the home platform's posts, bench and crates stood nothing at all. Now a heap is walls in
/// layers under its mound, and the platform's solids are where its bay draws them. (`dt screenshot --walls` draws the
/// walls over any view.)
/// </summary>
public class TownSolidsMatchArtTests
{
    [Theory]
    [InlineData("slag")]
    [InlineData("coal")]
    public void AHeapsWallsAreUnderItsMound(string kind)
    {
        var (hs, hd, height) = TownFixtures.Size(kind);
        var mesh = WorksKit.Piece(null, kind);
        double step = 0.35; // player.json stepUp
        var layers = TownFixtures.HeapLayers(hs, hd, height, 0.3).ToList();
        Assert.NotEmpty(layers);
        foreach (var (lhs, lhd, bottom, top) in layers)
        {
            Assert.True(top - bottom < step, $"{kind}: a layer {top - bottom:F2} m deep is no step up");
            // Each corner of a layer is on the heap: the mound there stands at least as high as the layer's foot.
            foreach (var (x, z) in new[] { (lhs, lhd), (-lhs, lhd), (lhs, -lhd), (-lhs, -lhd) })
            {
                double t = Math.Sqrt(x * x / (hs * hs) + z * z / (hd * hd));
                Assert.True(t <= 1 && TownFixtures.HeapHeight(height, t) >= bottom - 1e-6, $"{kind}: a layer's corner ({x:F1}, {z:F1}) stands off the heap");
            }
        }
        // And the drawn mound is that heap: as high as its walls' top, and its foot as wide as the ground layer.
        var (_, size) = WorksKit.Footprint(mesh);
        Assert.True(size.Y >= height * 0.9 && size.Y <= height * 1.15, $"{kind}: drawn {size.Y:F2} m high, its walls {height:F2}");
        Assert.True(size.X / 2 >= layers[0].HalfS && size.Z / 2 >= layers[1].HalfD, $"{kind}: its ground layer stands past its mound");
    }

    [Fact]
    public void ThePlatformsSolidsAreWhereItsBaysDrawThem()
    {
        for (int variant = 0; variant < 3; variant++)
        {
            var bay = StructureKit.PlatformBay(null, variant);
            // Something of the bay drawn at each of its solids' middles, up to its top: its posts, its bench or crates.
            var solids = Fortresses.PostOut.Select(x => (X: x, Z: 0.0, Top: Fortresses.PostTop))
                .Concat(Fortresses.OnThePlatformBay(variant).Select(o => (X: (o.X0 + o.X1) / 2, Z: (o.Z0 + o.Z1) / 2, o.Top)));
            foreach (var (x, z, top) in solids)
            {
                bool drawn = bay.Vertices.Any(v => Math.Abs(v.Position.X - x) < 0.6 && Math.Abs(v.Position.Z - z) < 1.1 && Math.Abs(v.Position.Y - top) < 0.15);
                Assert.True(drawn, $"platform bay {variant}: nothing drawn at its solid ({x:F1}, {z:F1}) up to {top:F2} m");
            }
            // Its deck: the cobbles' top where the Sim's deck is.
            Assert.Contains(bay.Vertices, v => Math.Abs(v.Position.Y - Fortresses.PlatformDeck) < 1e-3 && v.Position.X >= Fortresses.PlatformOut - 0.01);
        }
    }
}
