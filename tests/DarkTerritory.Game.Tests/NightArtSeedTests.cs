using Ballast;
using DarkTerritory.Sim.LineGen;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// Each night its own lineside (ARCHITECTURE §8 note 780; GDD App. F.3, the director: "the same three things over and over
/// again"). What the art alone dresses the land with (the forest floor, the stands, the verge, the shrines) was dealt from one
/// seed, 7, on every night, so it fell at the same distances on every night (note 778's fallen trunk at 1.5 km on three); it's
/// dealt from the night's own seed now. (The railway's leavings are the sim's since note 792, from the night's seed:
/// LinesideLeavingsTests.)
/// </summary>
public class NightArtSeedTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly string[] Nights = ["frontier:7", "frontier:3", "local:3", "deadLines:3", "deepTerritory:3", "deadLines:2", "deepTerritory:2", "local:12"];

    [Fact]
    public void EachNightDressesItsLinesideFromItsOwnSeed()
    {
        var seeds = new Dictionary<string, int>();
        foreach (string spec in Nights)
        {
            var route = Routes.Generate(Content, spec, 6);
            var scene = new GreyboxScene { Route = route };
            Assert.Equal(GreyboxScene.ArtSeedOf(route.Plan!.Seed), scene.ArtSeed);
            // The same night built again is dressed the same (the host and every client draw it alike).
            Assert.Equal(scene.ArtSeed, new GreyboxScene { Route = Routes.Generate(Content, spec, 6) }.ArtSeed);
            seeds[spec] = scene.ArtSeed;
        }
        // Nights of one number in different tiers (deadLines:3 and deepTerritory:3) share a route seed, not a plan's.
        Assert.Equal(Nights.Length, seeds.Values.Distinct().Count());
        Assert.DoesNotContain(7, seeds.Values);
    }

    [Fact]
    public void ASeedSetIsTheSeedUsed()
    {
        var route = Routes.Generate(Content, "frontier:7", 6);
        Assert.Equal(7, new GreyboxScene { Route = route, Seed = 7 }.ArtSeed);
        Assert.Equal(7, new GreyboxScene().ArtSeed);
    }
}
