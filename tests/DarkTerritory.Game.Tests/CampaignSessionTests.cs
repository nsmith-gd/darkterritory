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
            s = s with
            {
                Checkpoint = new RunCheckpoint("local:5", 0, 600, 9000, 300, [new CarState(1, 0.75, 1, 1, 200, Eaten: 0.25)], [1])
                {
                    Rakes = [new RakeSave([4, 0, 1, 2], -1, 9000, false, false), new RakeSave([3], -1, 8700, true, true)],
                },
            };
            saves.Save(s);
            var back = saves.Load(2)!;
            Assert.Equal(s.Scrip, back.Scrip);
            Assert.Equal(s.Upgrades, back.Upgrades);
            Assert.Equal(s.History, back.History);
            Assert.Equal(s.Checkpoint.Cars, back.Checkpoint!.Cars);
            // The rakes as they were (note 481), each front to back.
            Assert.Equal(s.Checkpoint.Rakes!.Select(r => (string.Join(",", r.Vehicles), r.Path, r.Distance, r.Handbrake, r.Locked)),
                back.Checkpoint.Rakes!.Select(r => (string.Join(",", r.Vehicles), r.Path, r.Distance, r.Handbrake, r.Locked)));
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
    public void TheSetupAJoinerIsSentFitsOnePacketWithRoomToGrow()
    {
        // The Welcome is one datagram (1200 bytes) and carries the setup: upgrades, mods, and a hash of every tuning file.
        // Indented, on Windows (two-byte line breaks), it went over at 19 tuning files.
        var setup = new SessionSetup(Route: "frontier:7", Cars: 8, Enemies: true)
        {
            Upgrades = ["tenderCapacity", "improvedBrakes", "roofGuns"],
            Content = SessionSetup.HashContent(Content),
            Mods = ["SomeoneElse-ALongishModName 1.2.3"],
        };
        string encoded = setup.Encode();
        Assert.DoesNotContain('\n', encoded);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(encoded) < 900, $"{encoded.Length} bytes");
        Assert.Equal(setup.Upgrades, SessionSetup.Decode(encoded).Upgrades);
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
        // frontier:10 at four cars is a night with one (the line generator draws it as any other kind, linegen plan §11.1).
        const string spec = "frontier:10";
        var route = Sim.LineGen.Routes.Generate(Content, spec, 4);
        var facilities = route.Of(FeatureKind.Facility).ToList();
        var facility = facilities.First(f => f.Facility == FacilityKind.CoalingTower);
        int index = facilities.IndexOf(facility);
        var setup = new SessionSetup(Route: spec, Cars: 4, Enemies: false) { Start = facility.End - 20 };
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
            Assert.Equal(spec, saved.Route);
            Assert.Equal(index, saved.Facility);
            Assert.True(saved.Front > facility.End);
            Assert.Equal(0.9, saved.Cars[1].Load, 6);
            // The save keeps the line itself (linegen plan §17.4), not just its spec.
            Assert.Equal(route.Plan!.Fingerprint(), Sim.LineGen.LinePlan.Decompress(saved.Plan!).Fingerprint());
        }

        // The session is lost; the night starts again from the save.
        using var resumed = NetPlaySession.HostGame(Content, new SessionSetup(Route: spec, Cars: 4, Enemies: false), port: 0, resume: saved);
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
        // It plays the saved line, and tells joiners which line that is.
        Assert.Equal(route.Plan!.Fingerprint(), resumed.Setup.PlanPrint);
    }

    [Fact]
    public void AResumedNightKeepsTheTrainAsItLeft()
    {
        // Queue #218, ARCHITECTURE §8 note 481 (spec E "rolls back to last POI autosave"). At the coaling tower, as the night
        // left it: a yard's standing cars picked up ahead of the engine (as a switchyard leaves them), the last car cut off
        // and left at the stop, and a bite out of the first car's shell. The save keeps all three.
        const string spec = "frontier:10";
        var route = Sim.LineGen.Routes.Generate(Content, spec, 4);
        var facility = route.Of(FeatureKind.Facility).First(f => f.Facility == FacilityKind.CoalingTower);
        var setup = new SessionSetup(Route: spec, Cars: 4, Enemies: false) { Start = facility.End - 20 };
        Sim.Campaign.RunCheckpoint saved;
        int[] picked, rake;
        int cutCar;
        using (var night = NetPlaySession.HostGame(Content, setup, port: 0))
        {
            var train = night.Host!.Train;
            for (int i = 0; i < 10; i++)
                night.Step(default);
            Assert.Equal(RunPhase.AtFacility, night.Host.World.Run!.Phase);
            // A night out here has cars standing in its yards (a switchyard's, a blocked siding's derelicts).
            var standing = train.Rakes.FirstOrDefault(r => train.Standing(r));
            Assert.NotNull(standing);
            picked = [.. standing.Consist.Vehicles.Select(v => v.Id)];
            var own = train.Dynamics.Consist.Vehicles.Select(v => v.Id).ToList();
            cutCar = own[^1];
            var g = train.Dynamics.Tuning.Geometry;
            double ahead = picked.Length * (g.CarLength + g.CouplingGap);
            Assert.True(train.Resume([new RakeState([.. picked, .. own], train.Dynamics.Distance + ahead, 0, 1, false, false)]));
            Assert.True(train.Uncouple(own[^2]));
            train.Vehicles[1].Eaten = 0.25;
            rake = [.. train.Dynamics.Consist.Vehicles.Select(v => v.Id)];
            for (int i = 0; i < 10 * DarkTerritory.Sim.SimConstants.TickRate && night.Checkpoints == 0; i++)
            {
                train.Dynamics.Velocity = 3;
                night.Step(default);
            }
            Assert.Equal(1, night.Checkpoints);
            saved = night.Checkpoint!;
            Assert.Equal(rake, saved.Rakes!.Single(r => r.Vehicles.Contains(0)).Vehicles);
            Assert.Equal([cutCar], saved.Rakes!.Single(r => r.Vehicles.Contains(cutCar)).Vehicles);
        }

        using (var resumed = NetPlaySession.HostGame(Content, new SessionSetup(Route: spec, Cars: 4, Enemies: false), port: 0, resume: saved))
        {
            var train = resumed.Host!.Train;
            // The engine's rake as it left, the picked-up cars still ahead of it and no longer standing in their yard.
            Assert.Equal(rake, train.Dynamics.Consist.Vehicles.Select(v => v.Id));
            Assert.InRange(train.Dynamics.Distance, saved.Front - 1, saved.Front + 1);
            Assert.All(picked, id => Assert.False(train.StandingCar(id)));
            // The cut car is where it was left, not back in the train.
            var cut = train.RakeOf(cutCar);
            Assert.NotSame(train.Dynamics, cut);
            Assert.Equal(saved.Rakes!.Single(r => r.Vehicles.Contains(cutCar)).Distance, cut.Distance, 6);
            // What the Car Hugger ate stays eaten.
            Assert.Equal(0.25, train.Vehicles[1].Eaten, 6);
            // A step on, a joiner's world agrees: the host's rakes go out to it like any.
            resumed.Step(default);
            Assert.Equal(rake, resumed.Train.Dynamics.Consist.Vehicles.Select(v => v.Id));
        }

        // An older save (no rakes) builds the train from its own cars, as before.
        using var older = NetPlaySession.HostGame(Content, new SessionSetup(Route: spec, Cars: 4, Enemies: false), port: 0, resume: saved with { Rakes = null });
        Assert.Equal(Enumerable.Range(0, 5), older.Host!.Train.Dynamics.Consist.Vehicles.Select(v => v.Id));
    }
}
