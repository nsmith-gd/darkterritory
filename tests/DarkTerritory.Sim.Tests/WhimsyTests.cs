using Ballast;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The whimsies (ARCHITECTURE §8 note 715; docs/design/upgrades.md §8.2, the director's approvals of 13 Oct 2026): Gerald and
/// the garden gnomes, that the Gannet takes for walkers now and then; the canary, that goes quiet with something aboard and
/// dies in fumes; the calliope, a note a pull; the gramophone, wound with Use, that the Moose stops for and the Gaunt hears as
/// talk. Each on the train once bought, each with its tell, each read out by the clerk.
/// </summary>
public class WhimsyTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly CampaignTuning C = DataFile.Load<CampaignTuning>(Path.Combine(Content, CampaignTuning.File));
    static readonly Loadout Base = new(Tuning.Train, Tuning.Boiler, Tuning.Combat, Tuning.Enemies);
    static readonly BandTuning B = DataFile.Load<BandTuning>(Path.Combine(Content, BandTuning.File));
    static readonly EnemyTuning E = Tuning.Enemies;
    static readonly PlayerTuning P = Tuning.Player;

    static readonly string[] Whimsies = ["gerald", "gardenGnomes", "canary", "calliope", "gramophone"];

    static TrainTuning Dressed(params string[] ids) => Campaign.Campaign.Apply(C, ids, Base).Train;

    static World World(TrainTuning t, int cars = 5)
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(20_000)]));
        var world = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(t, cars, 1)), line, 2_000), Tuning.Combat);
        world.EnableBodies();
        world.EnableBand(B);
        world.Stock();
        return world;
    }

    static void Tick(World w, ref PlayerState s, PlayerIntent intent, int id = 1)
    {
        w.BeginTick();
        w.CrewAct(ref s, intent, id);
        w.Step(new TrainControls { Reverser = 1, Brake = 1 });
        PlayerMotor.Step(ref s, intent, w.Train, P, w.Train.Dynamics.Tuning, SimConstants.TickSeconds, applyLook: false);
    }

    static readonly PlayerIntent Use = new() { Buttons = PlayerButtons.Use };

    [Fact]
    public void EachIsOnTheTrainOnceBoughtWithItsLineAndTally()
    {
        foreach (var id in Whimsies)
        {
            var u = Assert.Single(C.Upgrades, u => u.Id == id);
            Assert.Equal(UpgradeKind.Whimsy, u.Kind);
            Assert.NotNull(u.Line);
            Assert.NotNull(u.Tally);
            Assert.Contains(id, Dressed(id).Composition.Dress);
        }
        // The calliope is the whistle, louder: not with any other whistle, from either side.
        var s = Campaign.Campaign.New(C, 1, "t", 1) with { Scrip = 1e9, Cars = 12 };
        s = Campaign.Campaign.BuyUpgrade(C, s, "calliope").State;
        foreach (var other in new[] { "theHonk", "goldWhistle", "cucarachaHorn" })
            Assert.NotNull(Campaign.Campaign.BuyUpgrade(C, s, other).Refused);
        Assert.Equal(Base.Combat.Choir.WhistleLoudness * 1.6, Campaign.Campaign.Apply(C, ["calliope"], Base).Combat.Choir.WhistleLoudness, 9);
    }

    [Fact]
    public void GeraldStandsOnTheGuardVanAndTheGnomesAlongTheRoofs()
    {
        var w = World(Dressed("gerald", "gardenGnomes"));
        var decoys = w.Decoys();
        var t = w.Train.Dynamics.Tuning.Composition.Whimsy;
        Assert.Equal(1 + t.Gnomes, decoys.Count);
        var gerald = decoys[0];
        Assert.True(gerald.Gerald);
        Assert.Equal(w.Train.Frames.Count - 1, gerald.Car);
        foreach (var d in decoys)
        {
            var shape = w.Train.Frames[d.Car].Shape;
            Assert.True(d.Car > 0, "on a car, not the engine");
            Assert.Equal(shape.RoofHeight, d.Local.Y, 6);
            Assert.True(Math.Abs(d.Local.Z) < shape.HalfLength && Math.Abs(d.Local.X) < shape.HalfWidth, $"{d.Index} on its roof");
        }
        // None without the upgrades.
        Assert.Empty(World(Tuning.Train).Decoys());
    }

    [Fact]
    public void TheGannetTakesGeraldForAWalkerCarriesHimOffAndTheClerkHasIt()
    {
        var enemies = E with { Gannet = E.Gannet with { DecoyPull = 1 } };
        var n = new Night(4, speed: 20, enemies: enemies, trainTuning: Dressed("gerald"));
        // Somebody inside, nobody on the roofs: without Gerald it would never dive.
        n.Crew[1] = PlayerMotor.SpawnInCab(n.Train, P);
        var g = n.World.AddEnemy(id => Gannet.Arriving(id, n.Train, enemies.Gannet, enemies.Gannet.SoarHeight[0]));
        bool hung = false, carried = false;
        for (double t = 0; t < 40 && !n.World.Whimsy.IsTaken(0); t += SimConstants.TickSeconds)
        {
            n.Run(SimConstants.TickSeconds);
            hung |= g.Mode == GannetMode.Hang && g.Decoy == 0;
        }
        Assert.True(hung, "it hung over him first: the tell");
        Assert.True(n.World.Whimsy.IsTaken(0), "taken");
        n.Run(0.2);
        carried = g.Mode == GannetMode.Climb && g.Decoy == 0;
        Assert.True(carried, $"climbing away with him ({g.Mode}, {g.Decoy})");
        Assert.Empty(n.World.Decoys());
        var line = Assert.Single(n.World.Attribution.Log, i => i.Kind == Sim.Run.IncidentKind.Whimsy);
        Assert.Contains("Gerald taken by the Gannet", line.What);
        Assert.Contains("Body not recovered. Fee 0.", line.Action);
        Assert.Equal(P.Health, n.Crew[1].Health);
        n.AssertFair();
    }

    [Fact]
    public void TheCanaryGoesQuietWithSomethingAboardAndSingsWithout()
    {
        var n = new Night(4, speed: 8, trainTuning: Dressed("canary"));
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, 1, 0, P);
        n.Crew[2] = PlayerMotor.SpawnInCab(n.Train, P);
        n.Run(0.5);
        Assert.Equal(CanaryMood.Singing, n.World.Whimsy.Canary);
        // Tippy Toesie comes for the one stood still on car 1.
        var tippy = n.World.AddEnemy(id => TippyToesie.Hiding(id, E.TippyToesie));
        bool quiet = false;
        for (double t = 0; t < E.TippyToesie.IdleSeconds + 4 && !quiet; t += SimConstants.TickSeconds)
        {
            n.Run(SimConstants.TickSeconds, id => id == 2 ? new PlayerIntent { MoveX = 0.01f } : default);
            quiet = n.World.Whimsy.Canary == CanaryMood.Silent;
        }
        Assert.True(quiet, $"the canary still singing with Tippy Toesie {tippy.Phase} on car {tippy.Attached}");
        Assert.Contains(tippy.Attached, new[] { 0, 1, 2 });
    }

    [Fact]
    public void TheCanaryDiesAtTheChemicalWorks()
    {
        // The first generated night with a chemical works on it.
        var (route, index) = Enumerable.Range(1, 40).SelectMany(seed => new[] { Route.RouteTier.Frontier, Route.RouteTier.DeadLines }
                .Select(tier => Route.RouteGenerator.Generate(Tuning.Route, tier, (ulong)seed)))
            .Select(r => (r, r.Of(Route.FeatureKind.Facility).ToList().FindIndex(f => f.Facility == Route.FacilityKind.ChemicalWorks)))
            .First(x => x.Item2 >= 0);
        var w = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Dressed("canary"), 4, 1)), route.Build(), 1_000), Tuning.Combat);
        w.EnableRun(Tuning.Run, route, 600, authority: true);
        // Stood at the works (as the host's run has it), the fumes take it after canaryFumesSeconds.
        w.Run!.Mirror(Sim.Run.RunPhase.AtFacility, Sim.Run.RunEnd.None, 100, index, false, [.. Enumerable.Repeat(0.0, w.Run.FacilityCount)]);
        double seconds = w.Train.Dynamics.Tuning.Composition.Whimsy.CanaryFumesSeconds;
        for (int i = 0; i < (seconds - 1) * SimConstants.TickRate; i++)
            w.Step(default);
        Assert.Equal(CanaryMood.Singing, w.Whimsy.Canary);
        for (int i = 0; i < 2 * SimConstants.TickRate; i++)
            w.Step(default);
        Assert.Equal(CanaryMood.Dead, w.Whimsy.Canary);
        Assert.Contains(w.Attribution.Log, i => i.Kind == Sim.Run.IncidentKind.Whimsy && i.What.Contains("canary"));
    }

    [Fact]
    public void ThePullsOfTheCordAreTheCalliopesNotesAndAHeldPullIsOne()
    {
        var w = World(Dressed("calliope"));
        Assert.Equal(0, w.Whimsy.CalliopeNote);
        for (int pull = 1; pull <= 3; pull++)
        {
            // Held a second, the whistle blowing on: one note.
            for (int i = 0; i < SimConstants.TickRate; i++)
            {
                w.Whistled(1.0, 1);
                w.Step(default);
            }
            Assert.Equal(pull, w.Whimsy.CalliopeNote);
            // Let go till it's quiet.
            for (int i = 0; i < 1.1 * SimConstants.TickRate; i++)
                w.Step(default);
        }
        // The Whistler's blowing isn't a note; without the calliope there are none.
        w.Whistled(1.0);
        Assert.Equal(3, w.Whimsy.CalliopeNote);
        var plain = World(Tuning.Train);
        plain.Whistled(1.0, 1);
        Assert.Equal(0, plain.Whimsy.CalliopeNote);
    }

    [Fact]
    public void TheGramophoneIsWoundWithUseLiftedOffAgainAndRunsOut()
    {
        var w = World(Dressed("gramophone"));
        var van = w.Train.Frames[^1].Shape;
        var spot = Assert.Single(van.Interactables, i => i.Kind == InteractableKind.Gramophone);
        var s = PlayerMotor.SpawnInCab(w.Train, P) with { Parent = w.Train.Frames.Count - 1, Position = spot.Position, Yaw = Math.PI / 2, HeldSlot = 0 };
        Tick(w, ref s, Use);
        Assert.True(w.Whimsy.Playing);
        Assert.True(w.GramophonePlaying);
        int record = w.Whimsy.Record;
        // Held, it's one press.
        Tick(w, ref s, Use);
        Assert.True(w.Whimsy.Playing);
        Tick(w, ref s, default);
        Tick(w, ref s, Use);
        Assert.False(w.Whimsy.Playing);
        // Wound again: the next record, played twice through, and it stops.
        Tick(w, ref s, default);
        Tick(w, ref s, Use);
        Assert.Equal((record + 1) % B.Scores.Count, w.Whimsy.Record);
        double side = B.Scores[w.Whimsy.Record].Seconds * 2;
        for (int i = 0; i < (side + 0.5) * SimConstants.TickRate; i++)
            w.Step(default);
        Assert.False(w.Whimsy.Playing);
        // There's none to wind on a train without it.
        Assert.DoesNotContain(World(Tuning.Train).Train.Frames[^1].Shape.Interactables, i => i.Kind == InteractableKind.Gramophone);
    }

    [Fact]
    public void TheMooseStopsForTheGramophoneAndTheGauntHearsItAsTalk()
    {
        var w = World(Dressed("gramophone"));
        w.Whimsy = w.Whimsy with { Playing = true, WoundAt = (uint)w.Tick };
        var (record, hears) = Assert.Single(w.Playing());
        var ctx = new EnemyContext { Tuning = E, World = w };
        // A Moose within its hearing, riled at a crewmate: it forgets them and listens.
        var at = record with { X = record.X + hears - 3, Y = 0 };
        var moose = w.AddEnemy(id => Moose.Grazing(id, at, w.Train.Dynamics.Distance, 0));
        ctx.Crew.Add((new PlayerSnapshot(1, new PlayerState { Parent = PlayerState.World, Position = at + new Double3(0, 0, 8), Surface = Surface.Ground, Health = P.Health }), new PlayerIntent()));
        for (int i = 0; i < 2 * SimConstants.TickRate; i++)
        {
            w.Tick++;
            moose.Step(ctx);
        }
        Assert.Null(moose.Target);
        Assert.Equal(SpinePhase.Alert, moose.Phase);
        // A Gaunt by the van, its waker silent: the record's talk, and it calms a step.
        var gauntAt = record with { X = record.X + 2, Y = 0 };
        ctx.Crew.Clear();
        ctx.Crew.Add((new PlayerSnapshot(1, new PlayerState { Parent = PlayerState.World, Position = gauntAt + new Double3(0, 0, -1.2), Surface = Surface.Ground, Health = P.Health }), new PlayerIntent()));
        var g = w.AddEnemy(id => Gaunt.Asleep(id, gauntAt, E.Gaunt));
        g.Restore(SpinePhase.Commit, 5, E.Gaunt.Health, Enemy.Loose, gauntAt, 0, 0, 0, extra: 1, extra2: E.Gaunt.AttackAt);
        for (int i = 0; i < (E.Gaunt.SilenceSeconds + 0.1) * SimConstants.TickRate && !g.Gone; i++)
        {
            w.Tick++;
            g.Step(ctx);
        }
        Assert.Equal(E.Gaunt.AttackAt - 1, g.Anger);
    }

    [Fact]
    public void TheWhimsiesAreEveryClients()
    {
        var w = World(Dressed("gerald", "gardenGnomes", "canary", "calliope", "gramophone"));
        w.Whimsy = new WhimsyState(0b101, CanaryMood.Dead, 7, true, 2, 4321);
        var controls = new TrainControls();
        var client = World(Dressed("gerald", "gardenGnomes", "canary", "calliope", "gramophone"));
        Net.WorldRecords.Apply(Net.WorldRecords.Capture(w, controls, []), client, ref controls, []);
        Assert.Equal(w.Whimsy, client.Whimsy);
        // And a new night has them all back.
        w.Stock();
        Assert.Equal(default, w.Whimsy);
    }
}
