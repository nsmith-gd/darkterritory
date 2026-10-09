using System.Numerics;
using Ballast;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Towns;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// A walled town's rail yard drawn (queue #239, note 502): the goods shed, C1's model, stands on its fixture's footprint, so
/// what's solid is what's drawn; the kit's own pieces are built, cheap and their fixtures' size.
/// </summary>
public class TownYardArtTests
{
    static readonly Look Look = Look.Load(DataFile.FindContentRoot());

    [Fact]
    public void TheGoodsShedStandsOnItsFixturesFootprint()
    {
        var model = PropArt.Of(Look).Get(YardKit.Shed);
        Assert.NotNull(model);
        // Its walls, office and dock: turned a half to the siding, its length along the line.
        var (_, size) = YardKit.GroundFootprint(model);
        var (hs, hd, h) = TownFixtures.Size("goodsshed");
        Assert.InRange(size.X / 2, hs - 0.4, hs + 0.3);
        Assert.InRange(size.Z / 2, hd - 0.3, hd + 0.3);
        Assert.InRange(WorksKit.Footprint(model).Size.Y, h - 0.5, h + 0.5);
    }

    [Theory]
    [InlineData("goodsshed")]
    [InlineData("yardoffice")]
    [InlineData("weighbridge")]
    [InlineData("freight")]
    [InlineData("cart")]
    [InlineData("sleepers")]
    [InlineData("van")]
    [InlineData("flatdory")]
    [InlineData("flatrails")]
    [InlineData("buffers")]
    public void TheKitsOwnPiecesAreBuiltCheapAndTheirFixturesSize(string kind)
    {
        foreach (int side in new[] { -1, 1 })
        {
            var piece = YardKit.Piece(Look, kind, 1, side);
            Assert.InRange(piece.Triangles, 12, 8000);
            var lo = new Vector3(float.MaxValue);
            var hi = new Vector3(float.MinValue);
            foreach (var v in piece.Vertices)
            {
                lo = Vector3.Min(lo, v.Position);
                hi = Vector3.Max(hi, v.Position);
            }
            var (hs, hd, h) = TownFixtures.Size(kind);
            // Within its box (eaves, a door's rail, buffers a hand over), and up to its height; the weighbridge's plate lies
            // beside its house toward the yard's rear end, in the room its piece leaves (world/towns.json "after").
            double plate = kind == "weighbridge" ? 4.2 : 0;
            Assert.True(lo.X >= -hs - 0.45 - (side > 0 ? plate : 0) && hi.X <= hs + 0.45 + (side < 0 ? plate : 0), $"{kind} {lo.X:0.00}..{hi.X:0.00} across its face, its box ±{hs}");
            Assert.True(lo.Z >= -hd - 0.45 - (plate > 0 ? 2.5 : 0) && hi.Z <= hd + 0.45 + (plate > 0 ? 2.5 : 0), $"{kind} {lo.Z:0.00}..{hi.Z:0.00} deep, its box ±{hd}");
            Assert.InRange(hi.Y, h * 0.7, h + 0.3);
        }
    }

    [Fact]
    public void TheSidingIsItsLengthAndItsRailsAreOnItsGauge()
    {
        var siding = YardKit.Siding(Look, 8);
        var zs = siding.Vertices.Select(v => v.Position.Z).ToList();
        Assert.InRange(zs.Max() - zs.Min(), 7.95, 8.05);
        // Its rails are where a wheel would run: the train's gauge.
        Assert.Contains(siding.Vertices, v => Math.Abs(Math.Abs(v.Position.X) - (TrainKit.HalfGauge + 0.035f)) < 1e-3 && v.Position.Y > 0.2f);
    }
}
