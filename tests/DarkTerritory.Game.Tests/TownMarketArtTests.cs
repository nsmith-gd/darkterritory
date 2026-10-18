using System.Numerics;
using Ballast;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Towns;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The night market's stall drawn (queue #552, note 825): its counter and posts inside its fixture's box (the awning
/// reaching out over the counter), cheap, and its lantern hung under the awning over the counter.
/// </summary>
public class TownMarketArtTests
{
    static readonly Look Look = Look.Load(DataFile.FindContentRoot());

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void AStallIsItsFixturesSizeAndItsLanternHangsUnderItsAwning(int variant)
    {
        var piece = SquareKit.Fixture(Look, "marketstall", variant);
        Assert.InRange(piece.Triangles, 100, 3000);
        var lo = new Vector3(float.MaxValue);
        var hi = new Vector3(float.MinValue);
        foreach (var v in piece.Vertices)
        {
            lo = Vector3.Min(lo, v.Position);
            hi = Vector3.Max(hi, v.Position);
        }
        var (hs, hd, h) = TownFixtures.Size("marketstall");
        Assert.True(lo.X >= -hs - 0.2 && hi.X <= hs + 0.2, $"{lo.X:0.00}..{hi.X:0.00} along, its box ±{hs}");
        // Its awning reaches half a metre out over the counter, toward the square.
        Assert.True(lo.Z >= -hd - 0.5 && hi.Z <= hd + 0.15, $"{lo.Z:0.00}..{hi.Z:0.00} across, its box ±{hd}");
        Assert.InRange(hi.Y, h - 0.2, h + 0.2);
        // The lantern: over the counter's front, under the awning's edge, above a crewmate's head.
        Assert.InRange(SquareKit.MarketLamp.Z, -hd - 0.5, -hd + 0.2);
        Assert.InRange(SquareKit.MarketLamp.Y, 1.8, h - 0.3);
    }
}
