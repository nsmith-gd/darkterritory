using System.Numerics;
using Ballast;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Towns;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// A walled town's works drawn (queue #183, note 447): the facilities' modelled pieces stand on their fixtures' footprints,
/// so what's solid is what's drawn; the kit's own pieces are built, cheap and their fixtures' size.
/// </summary>
public class TownWorksArtTests
{
    static readonly Look Look = Look.Load(DataFile.FindContentRoot());

    [Theory]
    [InlineData("winding")]
    [InlineData("tip")]
    [InlineData("elevator")]
    [InlineData("casting")]
    [InlineData("watertower")]
    public void AModelledPieceStandsOnItsFixturesFootprint(string kind)
    {
        var prop = WorksKit.Prop(kind)!.Value;
        var model = PropArt.Of(Look).Get(prop.Name);
        Assert.NotNull(model);
        var (_, size) = WorksKit.Footprint(model);
        // Its front along the line, or to it: which of its sides runs along the line.
        float along = (prop.Along ? size.Z : size.X) * prop.Scale, across = (prop.Along ? size.X : size.Z) * prop.Scale;
        var (hs, hd, _) = TownFixtures.Size(kind);
        Assert.InRange(along / 2, hs * 0.85, hs + 0.5);
        Assert.InRange(across / 2, hd * 0.85, hd + 0.5);
    }

    [Theory]
    [InlineData("glasshouse")]
    [InlineData("cellar")]
    [InlineData("slag")]
    [InlineData("coal")]
    [InlineData("pigs")]
    [InlineData("warehouse")]
    public void TheKitsOwnPiecesAreBuiltCheapAndTheirFixturesSize(string kind)
    {
        var piece = WorksKit.Piece(Look, kind);
        Assert.InRange(piece.Triangles, 12, 6000);
        var lo = new Vector3(float.MaxValue);
        var hi = new Vector3(float.MinValue);
        foreach (var v in piece.Vertices)
        {
            lo = Vector3.Min(lo, v.Position);
            hi = Vector3.Max(hi, v.Position);
        }
        var (hs, hd, h) = TownFixtures.Size(kind);
        // Within its box (a roof's eaves, a door's rail a hand over), and up to its height.
        Assert.True(lo.X >= -hs - 0.45 && hi.X <= hs + 0.45, $"{kind} {lo.X:0.00}..{hi.X:0.00} along, its box ±{hs}");
        Assert.True(lo.Z >= -hd - 0.45 && hi.Z <= hd + 0.45, $"{kind} {lo.Z:0.00}..{hi.Z:0.00} across, its box ±{hd}");
        Assert.InRange(hi.Y, h * 0.7, h + 0.3);
    }
}
