using System.Numerics;
using Ballast;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Towns;
using Ballast.Render;

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
        foreach (var (hx, hz, back, bottom, top) in layers)
        {
            Assert.True(top - bottom < step, $"{kind}: a layer {top - bottom:F2} m deep is no step up");
            // Each corner of a layer is on the heap as it's drawn: the mound there stands at least as high as the layer's top
            // (a step under it at most), never in the air beside it.
            foreach (var (x, z) in new[] { (hx, back + hz), (-hx, back + hz), (hx, back - hz), (-hx, back - hz) })
            {
                double ground = Surface(mesh, (float)x, (float)z);
                Assert.True(ground >= top - step, $"{kind}: a layer's corner ({x:F1}, {z:F1}) {top:F2} m up stands over the heap drawn there ({ground:F2} m)");
            }
        }
        // And its walls reach up the mound: the top layer near its crest.
        Assert.True(layers.Max(l => l.Top) >= height * 0.75, $"{kind}: its walls stop at {layers.Max(l => l.Top):F2} m of {height:F2}");
    }

    // The drawn mound's height at (x, z) in its own frame: the highest of its faces straight under that point, or below the ground.
    static double Surface(MeshAsset mesh, float x, float z)
    {
        var v = mesh.Vertices;
        double best = -1;
        for (int i = 0; i + 2 < v.Length; i += 3)
        {
            var (a, b, c) = (v[i].Position, v[i + 1].Position, v[i + 2].Position);
            float d = (b.Z - c.Z) * (a.X - c.X) + (c.X - b.X) * (a.Z - c.Z);
            if (Math.Abs(d) < 1e-9f)
                continue;
            float u = ((b.Z - c.Z) * (x - c.X) + (c.X - b.X) * (z - c.Z)) / d, w = ((c.Z - a.Z) * (x - c.X) + (a.X - c.X) * (z - c.Z)) / d;
            if (u < 0 || w < 0 || u + w > 1)
                continue;
            best = Math.Max(best, u * a.Y + w * b.Y + (1 - u - w) * c.Y);
        }
        return best;
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
