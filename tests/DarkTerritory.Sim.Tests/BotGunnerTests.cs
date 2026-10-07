using Ballast;
using DarkTerritory.Sim.Net;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The bot gunner takes its gun (note 299). In an 8-bot night it never did: from the yard it went into car 1 to catch the
/// mail cranes' bags, there was always another ahead, and the guard gun stood empty all night (no rounds fired, a hound pack
/// aboard). The bags are the walkers'; the gunner goes for them only with no other hand aboard but the driver.
/// </summary>
public class BotGunnerTests
{
    static readonly string Content = DataFile.FindContentRoot();

    [Fact]
    public void FromTheYardTheGunnerWalksBackToItsGunPastTheMailCranes()
    {
        var route = LineGen.Routes.Generate(Content, "frontier:7", 10);
        double gate = route.GateOr(Tuning.Route.YardLength);
        var report = Harness.Run(route.Build(), Tuning.Train, Tuning.Player, new HarnessOptions
        {
            Bots = 8,
            Cars = 10,
            Seconds = 90,
            Seed = 1,
            Link = new Ballast.Net.LinkConditions(0, 0, 0),
            StartDistance = Tuning.Run.DepartFrom(gate, Train.Consist.Uniform(Tuning.Train, 10, 1).LengthMetres),
            WalkAboard = true,
            Combat = Tuning.Combat,
            Route = route,
            Run = Tuning.Run,
            Facilities = DataFile.Load<Run.FacilityTuning>(Path.Combine(Content, Run.FacilityTuning.File)),
            Sight = DataFile.Load<Route.SightTuning>(Path.Combine(Content, Route.SightTuning.File)),
            YardLength = gate,
            Holdouts = Tuning.Holdouts,
        }, Tuning.Boiler);
        var posts = report.Posts!;
        double gunner = posts.Single(p => p.Key.StartsWith("gunner#", StringComparison.Ordinal)).Value;
        Assert.True(gunner >= 0, "the gunner never reached its gun");
        // The length of the train over the roofs, and not much more.
        Assert.InRange(gunner, 1, 75);
    }
}
