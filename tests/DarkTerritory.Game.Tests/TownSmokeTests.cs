using Ballast;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// Note 688 (the director, 13 Oct evening: "When I'm moving, the smoke out of the chimney stacks is speeding up ... quite
/// dramatic when I start running"): a chimney's smoke was seeded by where it stood from the eye, so every step re-seeded
/// it and its plume raced. Each chimney's and stack's seed is its own, wherever you stand.
/// </summary>
public class TownSmokeTests
{
    static readonly string Content = DataFile.FindContentRoot();

    [Fact]
    public void AChimneysSmokeIsSeededTheSameWhereverYouStand()
    {
        var look = Look.Load(Content);
        var route = Sim.LineGen.Routes.Generate(Content, "frontier:7", 6);
        var line = route.Build();
        var consist = Consist.Uniform(DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File)), 6, 1);
        var town = Staging.DepartureTown(Content, route, line, consist.LengthMetres, out _);
        Assert.NotNull(town);
        var middle = town.World(town.Plan.Houses[0].S, town.Plan.Houses[0].D);

        // Each smoke by where it is in the world (the eye added back), from two places 23 m apart.
        Dictionary<(long, long), int> Seen(Double3 eye) => look.Art.World.Chimneys(line, eye, town, 400)
            .Concat(look.Art.World.Stacks(line, eye, town, 400))
            .ToDictionary(c => ((long)Math.Round((eye.X + c.Top.X) * 10), (long)Math.Round((eye.Z + c.Top.Z) * 10)), c => c.Seed);
        var here = Seen(middle + new Double3(0, 1.7, 0));
        var there = Seen(middle + new Double3(17, 1.7, 15.5));

        Assert.NotEmpty(here);
        Assert.All(here.Where(c => there.ContainsKey(c.Key)), c => Assert.Equal(c.Value, there[c.Key]));
        Assert.Equal(here.Count, here.Values.Distinct().Count()); // and no two chimneys share a plume
    }
}
