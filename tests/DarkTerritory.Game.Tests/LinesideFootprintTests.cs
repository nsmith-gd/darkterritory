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
}
