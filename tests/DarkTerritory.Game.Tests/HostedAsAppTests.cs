using Ballast;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The app's bots are the harness's (queue #348, ARCHITECTURE §8 note 616; D1: "the director plays with the app's bots, and
/// every bot sweep we run is the harness's"): a campaign night hosted as the app hosts it (<see cref="NetPlaySession.HostGame"/>)
/// and the same night in the harness as the app hosts it (<see cref="HostedAsApp.For"/>) have the same crew of bots in the
/// same parts, the same director, the same line and train, the train standing in the same place.
/// </summary>
public class HostedAsAppTests
{
    static readonly string Content = DataFile.FindContentRoot();

    static string Part(IBot bot) => $"{bot.GetType().Name}:{((bot as RoofWalkerBot)?.Job ?? (bot as GunnerBot)?.Job)?.Job}";

    [Fact]
    public void ACampaignNightIsTheSameNightThroughTheAppAndTheHarness()
    {
        var t = DataFile.Load<CampaignTuning>(Path.Combine(Content, CampaignTuning.File));
        var s = Campaign.New(t, 1, "Crew 1", 7);
        var contract = Campaign.Offers(t, DataFile.Load<RunTuning>(Path.Combine(Content, RunTuning.File)), s)[0];
        var setup = HostedAsApp.Campaign(Campaign.Begin(s, contract), contract, enemies: true);
        const int bots = 3;

        using var app = NetPlaySession.HostGame(Content, setup, port: null, bots: bots);
        var hosted = HostedAsApp.For(Content, setup, bots, seconds: 1);
        (double Grace, double Start, string[] Parts, int Cars)? harness = null;
        Harness.Run(hosted.Line, hosted.Train, hosted.Player, hosted.Options with
        {
            Observe = (_, crew, world) => harness ??= (world.Director!.Grace, world.Train.Dynamics.Distance, [.. crew.Select(c => Part(c.Bot))],
                world.Train.OwnVehicles),
        }, hosted.Boiler);

        Assert.NotNull(harness);
        var h = harness.Value;
        var host = app.Host!.World;
        // The same director: seeded by the route (its grace is drawn from its seed), not the harness's own seed.
        Assert.Equal(host.Director!.Grace, h.Grace);
        // The same train, standing at the gate as the app stands it.
        Assert.Equal(host.Train.OwnVehicles, h.Cars);
        Assert.Equal(hosted.Options.StartDistance, host.Train.Dynamics.Distance, 3);
        Assert.Equal(host.Train.Dynamics.Distance, h.Start, 1);
        // The same crew of bots in the same parts: the driver, the gunner, the hands at the stops.
        Assert.Equal([.. app.BotCrew!.Bots.Select(b => Part(b.Bot))], h.Parts);
        Assert.Equal(setup.Route, app.Setup.Route);
        Assert.Equal(app.Route!.Seed, hosted.Options.Route!.Seed);
    }
}
