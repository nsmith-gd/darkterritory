using Ballast;
using Ballast.Render;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The rest of the lineside is solid (ARCHITECTURE §8 note 389): the sim stands each kit piece on its footprint, which is
/// measured off the art's own mesh (footprints.json, <c>dt art footprints --write</c>), and the art draws each piece where the
/// sim dealt it. So a piece that changes shape can't leave its walls behind, and what's seen is what's walked into.
/// </summary>
public class LinesideFootprintTests
{
    static readonly string Content = DataFile.FindContentRoot();

    [Fact]
    public void TheFootprintsAreTheMeshesOwn()
    {
        var file = File.ReadAllText(Path.Combine(Content, LineGenConfig.Directory, "footprints.json"));
        var measured = LinesideFootprints.Json(LinesideFootprints.Measure(null));
        Assert.True(file == measured, "content/linegen/footprints.json is behind the kit's meshes: run `dt art footprints --write`");
        // Every piece the sim deals is in it, and every variant the art has.
        var config = DataFile.Load<FootprintsFile>(Path.Combine(Content, LineGenConfig.Directory, "footprints.json"));
        foreach (var kind in LinesideFootprints.Kinds)
            Assert.Equal(kind.Variants, config.Pieces[kind.Name].Length);
        // Each box sits inside its mesh's bounds (a post's crossarm and a frame's open middle are left out, never added; a
        // drum's octagon stands a little proud of its faceted sides).
        foreach (var kind in LinesideFootprints.Kinds)
            for (int v = 0; v < kind.Variants; v++)
            {
                var (min, max) = ArtCatalog.Bounds(kind.Make(null, v));
                double give = kind.Shape == LinesideFootprints.Shape.Round ? 0.03 * (max.Z - min.Z) : 0.01;
                foreach (var b in config.Pieces[kind.Name][v].Where(b => b[4] == 0))
                {
                    Assert.True(b[0] - b[2] >= min.X - give && b[0] + b[2] <= max.X + give, $"{kind.Name}-{v} spills across its mesh");
                    Assert.True(b[1] - b[3] >= min.Z - give && b[1] + b[3] <= max.Z + give, $"{kind.Name}-{v} spills along its mesh");
                    Assert.True(b[5] <= max.Y + 0.01, $"{kind.Name}-{v} stands over its mesh");
                }
            }
    }

    [Fact]
    public void TheArtDrawsEachPieceWhereTheSimStandsIt()
    {
        // local:1 has the country road's homesteads, the coast's sheds and lighthouses, and the farmland's walls.
        var route = Routes.Generate(Content, "local:1", 6);
        var line = route.Build();
        var side = LinesideProps.Of(route, line)!;
        var forts = Fortresses.Of(route, line, 600, 400);
        var art = new WorldArt(Look.Load(Content));
        var drawn = 0;
        foreach (var name in new[] { "saltbox", "stoneWall", "lighthouse", "fishShed", "pole" })
        {
            var p = side.Props(1500, line.Length - 1500).First(q => q.Kind == LinesideKind.Piece && q.Species == name && !LinesideProps.InsideAFort(forts, q.Along, q.Lateral));
            // The art draws by where a slot, a road's or shore's cell or a block starts: a stretch back far enough to hold
            // a stone wall's block (its lengths run on up to 150 m past the block).
            double from = p.Along - 400;
            var t = line.Sample(p.Along);
            var foot = t.Position + Double3.Cross(t.Tangent, Double3.Up).Normalized * p.Lateral;
            var eye = t.Position;
            var mesh = new MeshBuilder();
            art.Lineside(mesh, line, route, eye, from, p.Along + 50, 7, 18);
            var kind = LinesideFootprints.ByName[name];
            var at = foot.RelativeTo(eye);
            Assert.True(mesh.Instances.Any(i => i.Asset.Name == kind.Cached(p.Variant) && double.Hypot(i.Model.M41 - at.X, i.Model.M43 - at.Z) < 0.5),
                $"no {kind.Cached(p.Variant)} drawn where the sim stands it, {p.Along:0} m, {p.Lateral:0.0} out");
            drawn++;
        }
        Assert.Equal(5, drawn);
    }

    [Fact]
    public void TheArtDrawsEachBranchsPineWhereTheSimStandsIt()
    {
        // Note 432: an alternate's or a dead line's pines are the sim's (LinesideProps.BranchTrees), so the art draws each
        // one, of its mesh and height, where the sim stands it as a wall, and no other.
        var route = Routes.Generate(Content, "frontier:7", 6);
        var line = route.Build();
        var side = LinesideProps.Of(route, line)!;
        var art = new WorldArt(Look.Load(Content));
        int checkedOn = 0;
        foreach (var a in route.Plan!.Alignment.Where(a => a.Role is EdgeRole.Alternate or EdgeRole.DeadLine))
        {
            var local = line.Branches[a.Branch].Local;
            var trees = side.BranchTrees(a.Branch, 0, local.Length).ToList();
            if (trees.Count == 0)
                continue;
            // From the middle of its pines, everything within 150 m of the eye.
            var mid = trees[trees.Count / 2];
            var eye = local.Sample(mid.Along).Position;
            var mesh = new MeshBuilder();
            art.Plan(mesh, line, route, eye, 0, 150, 0);
            var pines = mesh.Instances.Where(i => i.Asset.Name.StartsWith("pine-", StringComparison.Ordinal)).ToList();
            foreach (var tree in trees)
            {
                var t = local.Sample(tree.Along);
                var at = ((t.Position + Double3.Cross(t.Tangent, Double3.Up).Normalized * tree.Lateral) with { Y = tree.Ground }).RelativeTo(eye);
                if (double.Hypot(at.X, at.Z) > 100)
                    continue;
                Assert.Contains(pines, i => i.Asset.Name == $"pine-{tree.Variant}-12" && double.Hypot(i.Model.M41 - at.X, i.Model.M43 - at.Z) < 0.05
                    && Math.Abs(i.Model.M42 - at.Y) < 0.05 && Math.Abs(new System.Numerics.Vector3(i.Model.M21, i.Model.M22, i.Model.M23).Length() * 12 - tree.Height) < 0.01);
                checkedOn++;
            }
            // And no pine but the sim's: every one drawn stands on one of the branches' trees.
            var all = route.Plan.Alignment.Where(b => b.Role is EdgeRole.Alternate or EdgeRole.DeadLine)
                .SelectMany(b => side.BranchTrees(b.Branch, 0, line.Branches[b.Branch].Local.Length)
                    .Select(x => (line.Branches[b.Branch].Local.Sample(x.Along) is var q ? q.Position + Double3.Cross(q.Tangent, Double3.Up).Normalized * x.Lateral : default).RelativeTo(eye)))
                .ToList();
            Assert.All(pines, i => Assert.Contains(all, w => double.Hypot(i.Model.M41 - w.X, i.Model.M43 - w.Z) < 0.05));
        }
        Assert.True(checkedOn > 20, $"only {checkedOn} pines checked");
    }
}
