using System.Numerics;
using Ballast;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Towns;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The night market's stalls drawn (queue #552, note 825; note 870, the director's playtest of 19 Oct, line 4: "Too many
/// cut and paste stalls in a row, no diversity to them"): each of the kit's forms inside its fixture's box (an awning
/// reaching out over the counter), cheap, its lights inside it and lit; and dealt so that no two stalls side by side are
/// the same form.
/// </summary>
public class TownMarketArtTests
{
    static readonly Look Look = Look.Load(DataFile.FindContentRoot());

    public static TheoryData<int> Variants => [.. Enumerable.Range(0, SquareKit.StallVariants)];

    [Theory]
    [MemberData(nameof(Variants))]
    public void AStallIsInsideItsFixturesBoxAndLitFromWithinIt(int variant)
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
        // An awning or a fire may reach half a metre out toward the square; nothing behind, where the stallholder stands.
        Assert.True(lo.Z >= -hd - 0.5 && hi.Z <= hd + 0.15, $"{lo.Z:0.00}..{hi.Z:0.00} across, its box ±{hd}");
        Assert.True(lo.Y >= -0.01, $"its foot at {lo.Y:0.00}");
        // Each its own height (a trestle's sheet is lower than a tent), none taller than the stall's own.
        Assert.InRange(hi.Y, 1.8, h + 0.2);
        // Its lights: over its counter or its ground, inside its reach, not underground.
        var lights = SquareKit.StallLights(variant);
        Assert.NotEmpty(lights);
        foreach (var (at, colour, range) in lights)
        {
            Assert.InRange(at.X, -hs - 0.3, hs + 0.3);
            Assert.InRange(at.Z, -hd - 0.5, hd);
            Assert.InRange(at.Y, 0.6, h);
            Assert.True(colour.Length() > 0.2f && range >= 3, $"a light of {colour} over {range} m");
        }
    }

    [Fact]
    public void TheMarketsStallsAreDealtSoNoNeighboursMatch()
    {
        // WorldArt deals each town's stalls seven on round the kit (from a place its name sets).
        for (int from = 0; from < SquareKit.StallVariants; from++)
            for (int i = 0; i < 8; i++)
            {
                int a = (from + 7 * i) % SquareKit.StallVariants, b = (from + 7 * (i + 1)) % SquareKit.StallVariants;
                Assert.NotEqual(SquareKit.StallForm(a), SquareKit.StallForm(b));
            }
        Assert.Equal(6, Enumerable.Range(0, SquareKit.StallVariants).Select(SquareKit.StallForm).Distinct().Count());
    }
}
