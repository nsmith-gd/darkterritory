using Ballast;
using DarkTerritory.Game.Art;
using static DarkTerritory.Game.Art.NestBuild;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The Follower's nest built up over its 60 s (note 460; the art checklist's follower-nest): from the follower_nest model's
/// own pieces, the loot there from the start, the strands down first, the lobes up out of the heap in turn, the hollow last;
/// never shrinking as it builds, and the whole model at the end.
/// </summary>
public class NestBuildTests
{
    static readonly Look Look = Look.Load(DataFile.FindContentRoot());

    [Fact]
    public void TheNestIsBuiltUpPieceByPiece()
    {
        var model = PropArt.Of(Look).Get("follower_nest");
        Assert.NotNull(model);
        var build = new NestBuild();
        var pieces = build.Pieces(model!);
        int Count(NestPart part) => pieces.Count(p => p.Part == part);
        // The recipe's: two crates, a sack and its spill under it; ten strands; six lobes; the hollow.
        Assert.Equal(4, Count(NestPart.Loot));
        Assert.Equal(10, Count(NestPart.Strand));
        Assert.Equal(6, Count(NestPart.Lobe));
        Assert.Equal(1, Count(NestPart.Hollow));
        Assert.Equal(model!.Triangles, pieces.Sum(p => p.Triangles.Length));

        // At the start the loot alone; at the end the whole nest; in between it only grows.
        int loot = pieces.Where(p => p.Part == NestPart.Loot).Sum(p => p.Triangles.Length);
        Assert.Equal(loot, build.At(model, 0).Mesh.Triangles);
        Assert.Equal(model.Triangles, build.At(model, 1).Mesh.Triangles);
        int was = 0;
        float top = 0;
        for (int i = 0; i <= 20; i++)
        {
            var (mesh, high) = build.At(model, i / 20f);
            Assert.True(mesh.Triangles >= was, $"the nest lost pieces at {i / 20f}");
            Assert.True(high >= top - 1e-4f, $"the nest sank at {i / 20f}");
            (was, top) = (mesh.Triangles, high);
        }
        Assert.Equal(1, top, 3);

        // The strands are down before the lobes are all up, and the hollow comes last.
        float strandsDone = pieces.Where(p => p.Part == NestPart.Strand).Max(p => p.To);
        float lobesDone = pieces.Where(p => p.Part == NestPart.Lobe).Max(p => p.To);
        var hollow = pieces.Single(p => p.Part == NestPart.Hollow);
        Assert.True(strandsDone < lobesDone);
        Assert.True(hollow.From >= pieces.Where(p => p.Part == NestPart.Lobe).Max(p => p.From));
        Assert.Equal(1, hollow.To);
    }
}
