using Ballast;
using DarkTerritory.Game;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>Spec E: saves, the host's upgrades on every machine, and the autosave per POI.</summary>
public class CampaignSessionTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly CampaignTuning C = DataFile.Load<CampaignTuning>(Path.Combine(Content, CampaignTuning.File));

    [Fact]
    public void ThreeSlotsOfText()
    {
        string dir = Path.Combine(Path.GetTempPath(), "dt-saves-" + Guid.NewGuid().ToString("N"));
        try
        {
            var saves = new SaveSlots(dir, C.SaveSlots);
            Assert.Equal(3, saves.Count);
            Assert.All(saves.List(), x => Assert.Null(x.State));
            var s = Campaign.New(C, 2, "Night Crew", 42) with { Scrip = 1234, Upgrades = ["tenderCapacity"] };
            s = Campaign.Settle(Campaign.Begin(s, new Contract(RouteTier.Local, 5, 450)), new RunReport(RunEnd.Delivered, 1500, 20, 2, 0, 2, 900, 30, 0, 20, 850, 4, 0));
            s = s with { Checkpoint = new RunCheckpoint("local:5", 0, 600, 9000, 300, [new CarState(1, 0.75, 1, 1, 200)], 1) };
            saves.Save(s);
            var back = saves.Load(2)!;
            Assert.Equal(s.Scrip, back.Scrip);
            Assert.Equal(s.Upgrades, back.Upgrades);
            Assert.Equal(s.History, back.History);
            Assert.Equal(s.Checkpoint.Cars, back.Checkpoint!.Cars);
            Assert.Contains("\"name\": \"Night Crew\"", File.ReadAllText(saves.PathOf(2)));
            Assert.Throws<ArgumentOutOfRangeException>(() => saves.PathOf(4));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TheHostsUpgradesAreEveryonesUpgrades()
    {
        var setup = new SessionSetup(Route: "frontier:7", Cars: 4, Enemies: false) { Upgrades = ["tenderCapacity", "improvedBrakes"] };
        using var host = NetPlaySession.HostGame(Content, setup, port: 0);
        var boiler = DataFile.Load<BoilerTuning>(Path.Combine(Content, BoilerTuning.File));
        Assert.Equal(boiler.TenderCapacity * 1.25, host.Host!.Train.BoilerTuning!.TenderCapacity, 0);
        using var joiner = NetPlaySession.Join(Content, new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, host.Port), () => host.Step(default));
        Assert.Equal(setup.Upgrades, joiner.Setup.Upgrades);
        Assert.Equal(host.Host.Train.BoilerTuning.TenderCapacity, joiner.Train.BoilerTuning!.TenderCapacity);
        Assert.Equal(host.Host.Train.Dynamics.Tuning.Performance[0].Brake, joiner.Train.Dynamics.Tuning.Performance[0].Brake);
    }

    [Fact]
    public void LeavingAFacilityAutosavesAndTheNightResumesFromThere()
    {
        // The coaling tower stands over the main line (the rest are down spurs): stopped under it near the far end of
        // its zone, then away past the end of it.
        var route = RouteGenerator.Generate(DataFile.Load<RouteTuning>(Path.Combine(Content, RouteTuning.File)), RouteTier.Frontier, 7);
        var facilities = route.Of(FeatureKind.Facility).ToList();
        var facility = facilities.First(f => f.Facility == FacilityKind.CoalingTower);
        int index = facilities.IndexOf(facility);
        var setup = new SessionSetup(Route: "frontier:7", Cars: 4, Enemies: false) { Start = facility.End - 20 };
        Sim.Campaign.RunCheckpoint saved;
        using (var night = NetPlaySession.HostGame(Content, setup, port: 0))
        {
            var train = night.Host!.Train;
            for (int i = 0; i < 10; i++)
                night.Step(default);
            Assert.Equal(RunPhase.AtFacility, night.Host.World.Run!.Phase);
            Assert.Equal(0, night.Checkpoints);
            train.Vehicles[1].Load = 0.9;
            train.Boiler.Tender = 123;
            // Moving off isn't leaving: shunting at a stop takes to and fro. Out past the end of the zone is.
            for (int i = 0; i < 10; i++)
            {
                train.Dynamics.Velocity = 3;
                night.Step(default);
            }
            Assert.Equal(0, night.Checkpoints);
            for (int i = 0; i < 10 * DarkTerritory.Sim.SimConstants.TickRate && night.Checkpoints == 0; i++)
            {
                train.Dynamics.Velocity = 3;
                night.Step(default);
            }
            Assert.Equal(1, night.Checkpoints);
            saved = night.Checkpoint!;
            Assert.Equal("frontier:7", saved.Route);
            Assert.Equal(index, saved.Facility);
            Assert.True(saved.Front > facility.End);
            Assert.Equal(0.9, saved.Cars[1].Load, 6);
        }

        // The session is lost; the night starts again from the save.
        using var resumed = NetPlaySession.HostGame(Content, new SessionSetup(Route: "frontier:7", Cars: 4, Enemies: false), port: 0, resume: saved);
        var run = resumed.Host!.World.Run!;
        // Past the gates with the clock running, and on from where it left.
        Assert.NotEqual(RunPhase.Yard, run.Phase);
        Assert.InRange(run.Seconds, saved.Seconds - 1, saved.Seconds + 1);
        Assert.InRange(resumed.Host.Train.Dynamics.Distance, saved.Front - 1, saved.Front + 1);
        Assert.Equal(0.9, resumed.Host.Train.Vehicles[1].Load, 6);
        Assert.InRange(resumed.Host.Train.Boiler.Tender, 120, 123);
        // That stop is already spent: no second helping of coal or crates.
        Assert.Equal(0, run.ChuteLeft(index));
        // And a joiner arriving now builds the train where it is, not in the yard.
        Assert.Equal(saved.Front, resumed.Setup.Start);
    }
}
