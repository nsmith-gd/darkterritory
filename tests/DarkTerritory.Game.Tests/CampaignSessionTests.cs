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
                    Takings = new RunTakings(310, [new Sim.Stops.LootFind(7, "medicine", 310)], 120, 900, 150, 48),
                    Aboard = [new ThingAboard(Sim.Physics.BodyKind.RepairKit, 1, new Double3(-0.4, 1.3, -2.5), 0.1, 0.2, 0.1, Locker: 8, Slot: 1),
                        new ThingAboard(Sim.Physics.BodyKind.Toy, 4, new Double3(0.6, 1.4, 3), 0.15, 0.2, 0.1, Noise: Sim.Physics.ToyNoise.MusicBox)],
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
            // What the night had taken and spent, and what was aboard (note 500).
            Assert.Equal(s.Checkpoint.Takings!.Scavenged, back.Checkpoint.Takings!.Scavenged);
            Assert.Equal(s.Checkpoint.Takings.Stowed, back.Checkpoint.Takings.Stowed);
            Assert.Equal((s.Checkpoint.Takings.Mail, s.Checkpoint.Takings.TenderAtDeparture, s.Checkpoint.Takings.CoalLoaded, s.Checkpoint.Takings.AmmoAtDeparture),
                (back.Checkpoint.Takings.Mail, back.Checkpoint.Takings.TenderAtDeparture, back.Checkpoint.Takings.CoalLoaded, back.Checkpoint.Takings.AmmoAtDeparture));
            Assert.Equal(s.Checkpoint.Aboard, back.Checkpoint.Aboard);
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

    [Fact]
    public void AResumedNightKeepsWhatItEarnedAndWhatsAboard()
    {
        // Queue #237, ARCHITECTURE §8 note 500 (spec E "rolls back to last POI autosave"). Before the save at the coaling tower:
        // a Gannet's head stowed aboard (paid), a mail bag caught, coal burned since the night left; a thing taken off a crew
        // locker's shelf and left on a car's floor, a rescued child aboard, a lamp lost, an extinguisher half spent.
        const string spec = "frontier:10";
        var route = Sim.LineGen.Routes.Generate(Content, spec, 4);
        var facility = route.Of(FeatureKind.Facility).First(f => f.Facility == FacilityKind.CoalingTower);
        var setup = new SessionSetup(Route: spec, Cars: 4, Enemies: false) { Start = facility.End - 20 };
        Sim.Campaign.RunCheckpoint saved;
        int lamps, shelved;
        bool kitStocked;
        Sim.Physics.BodyKind taken;
        Ballast.Double3 kitAt;
        using (var night = NetPlaySession.HostGame(Content, setup, port: 0))
        {
            var world = night.Host!.World;
            var train = world.Train;
            var run = world.Run!;
            for (int i = 0; i < 10; i++)
                night.Step(default);
            var room = train.Frames[2].Shape.Interior!.Value;
            var head = world.Bodies.SpawnCrate(train, 2, new Ballast.Double3(0.3, room.Min.Y, 1.5), Sim.Physics.BodyKind.Loot);
            head.Owner = Sim.Run.Run.TrophyOwner(Sim.Enemies.EnemyKind.Gannet);
            for (int i = 0; i < 2 * DarkTerritory.Sim.SimConstants.TickRate; i++)
                night.Step(default);
            Assert.True(run.Scavenged > 0, "the Gannet's head was never stowed");
            run.AddSalvage(120);
            train.Boiler.Tender -= 40;
            var kit = world.Bodies.All.First(b => b.Stowed);
            taken = kit.Kind;
            shelved = world.Bodies.All.Count(b => b.Stowed);
            kit.Locker = -1;
            kit.Parent = 2;
            kitAt = new Ballast.Double3(-0.4, room.Min.Y + kit.Pbd.Particles[0].Radius, -1.5);
            kit.Pbd.Particles[0].Position = kit.Pbd.Particles[0].Previous = kitAt;
            kit.Pbd.Wake();
            world.Bodies.SpawnCrate(train, 2, new Ballast.Double3(0, room.Min.Y, 0), Sim.Physics.BodyKind.Child);
            world.Bodies.Remove(world.Bodies.All.First(b => b.Kind == Sim.Physics.BodyKind.Lamp && b != kit && !b.Stowed));
            lamps = world.Bodies.All.Count(b => b.Kind == Sim.Physics.BodyKind.Lamp);
            world.Bodies.All.First(b => b.Kind == Sim.Physics.BodyKind.Extinguisher && b.Home == 1).Charge = 0.4;
            for (int i = 0; i < 10 * DarkTerritory.Sim.SimConstants.TickRate && night.Checkpoints == 0; i++)
            {
                train.Dynamics.Velocity = 3;
                night.Step(default);
            }
            Assert.Equal(1, night.Checkpoints);
            saved = night.Checkpoint!;
            Assert.Equal(run.Scavenged, saved.Takings!.Scavenged);
            Assert.Equal(120, saved.Takings.Mail, 6);
            Assert.True(saved.Takings.TenderAtDeparture > saved.Tender + 39, $"left with {saved.Takings.TenderAtDeparture}, saved at {saved.Tender}");
        }

        using (var resumed = NetPlaySession.HostGame(Content, new SessionSetup(Route: spec, Cars: 4, Enemies: false), port: 0, resume: saved))
        {
            var world = resumed.Host!.World;
            var run = world.Run!;
            // The night's takings and spending go on: the head still pays, the mail too, and the coal burned before the save is
            // still on the bill.
            Assert.Equal(saved.Takings!.Scavenged, run.Scavenged);
            Assert.Equal(saved.Takings.Stowed, run.Stowed);
            Assert.Equal(120, run.Mail, 6);
            Assert.Equal(saved.Takings.TenderAtDeparture, run.Takings.TenderAtDeparture);
            // What was aboard is where it was: the thing off the shelf on the car's floor, not back in its locker; the child
            // aboard; the lamp lost stays lost; the extinguisher as spent as it was; the lockers' shelves as they were.
            var kit = Assert.Single(world.Bodies.All, b => b.Kind == taken && b.Parent == 2 && !b.Stowed);
            var lay = Assert.Single(saved.Aboard!, t => t.Kind == taken && t.Car == 2 && t.Locker < 0);
            Assert.True((kit.Pbd.Particles[0].Position - lay.At).Length < 0.01, $"the {taken}'s at {kit.Pbd.Particles[0].Position}, saved at {lay.At}");
            Assert.True((lay.At - kitAt).Length < 1, $"it was put down at {kitAt}, and saved at {lay.At}");
            Assert.Equal(shelved - 1, world.Bodies.All.Count(b => b.Stowed));
            Assert.Single(world.Bodies.All, b => b.Kind == Sim.Physics.BodyKind.Child && b.Parent == 2);
            Assert.Equal(lamps, world.Bodies.All.Count(b => b.Kind == Sim.Physics.BodyKind.Lamp));
            Assert.Equal(0.4, world.Bodies.All.Single(b => b.Kind == Sim.Physics.BodyKind.Extinguisher && b.Home == 1).Charge, 2);
            static string Shelves(IEnumerable<(Sim.Physics.BodyKind Kind, int Car, int Locker, int Slot)> things) =>
                string.Join(";", things.Where(t => t.Locker >= 0).OrderBy(t => t.Car).ThenBy(t => t.Locker).ThenBy(t => t.Slot));
            Assert.Equal(Shelves(saved.Aboard!.Select(t => (t.Kind, t.Car, t.Locker, t.Slot))),
                Shelves(world.Bodies.All.Select(b => (b.Kind, b.Parent, b.Locker, b.Slot))));
            kitStocked = world.KitStocked;
        }

        // Saved just past a stop with a yard (the wreck yard down its spur, frontier:10's first): it's still in the stocking's
        // look-ahead, but it isn't stocked again. Its crates and finds were left there, and the ones stowed are paid for.
        var wreck = route.Of(FeatureKind.Facility).First(f => f.Facility == FacilityKind.WreckYard);
        using (var past = NetPlaySession.HostGame(Content, new SessionSetup(Route: spec, Cars: 4, Enemies: false), port: 0,
            resume: saved with { Facility = 0, Front = wreck.End + 30, Rakes = null }))
        {
            for (int i = 0; i < 2 * DarkTerritory.Sim.SimConstants.TickRate; i++)
                past.Step(default);
            Assert.DoesNotContain(past.Host!.World.Bodies.All, b => b.Parent == Sim.Player.PlayerState.World);
        }

        // An older save, with neither, counts from the save and stocks the train afresh: the shelves full again. The kit
        // counts as stocked (GDD §23.2) as it did on the resumed night.
        using var older = NetPlaySession.HostGame(Content, new SessionSetup(Route: spec, Cars: 4, Enemies: false), port: 0,
            resume: saved with { Takings = null, Aboard = null });
        Assert.Equal(0, older.Host!.World.Run!.Scavenged);
        Assert.Equal(shelved, older.Host.World.Bodies.All.Count(b => b.Stowed));
        Assert.Equal(older.Host.World.KitStocked, kitStocked);
        Assert.Equal(lamps + 1, older.Host.World.Bodies.All.Count(b => b.Kind == Sim.Physics.BodyKind.Lamp));
    }
}
