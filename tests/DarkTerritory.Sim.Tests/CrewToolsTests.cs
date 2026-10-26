using Ballast;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The crew's tools (queue #753, ARCHITECTURE §8 note 1052; docs/design/upgrades.md §7.3, the director, 14 Oct 2026: "all of
/// §7.3's"): the communication cord, the end-of-train telltale and fusees. Each is sold, does what its card says (note 196: a
/// purchase that does nothing is a bug), takes a share of its trouble and not all of it, and the clerk reads it.
/// </summary>
public class CrewToolsTests
{
    static readonly PlayerTuning P = Tuning.Player;
    static readonly CampaignTuning C = DataFile.Load<CampaignTuning>(Path.Combine(DataFile.FindContentRoot(), CampaignTuning.File));
    static readonly Loadout Base = new(Tuning.Train, Tuning.Boiler, Tuning.Combat, Tuning.Enemies);
    static PlayerIntent Whistle => new() { Actions = PlayerActions.Whistle };
    static PlayerIntent Fire => new() { Buttons = PlayerButtons.Fire };

    static TrainTuning With(params string[] upgrades) => Campaign.Campaign.Apply(C, upgrades, Base).Train;

    /// <summary>Steps the night as the host does, its bodies too (the crew, the world, the bodies).</summary>
    static void Run(Night n, double seconds, Func<int, PlayerIntent>? intent = null)
    {
        for (int i = 0; i < seconds * SimConstants.TickRate; i++)
        {
            n.World.BeginTick();
            foreach (var id in n.Crew.Keys.ToList())
            {
                var s = n.Crew[id];
                n.World.CrewAct(ref s, intent?.Invoke(id) ?? default, id);
                n.Crew[id] = s;
            }
            n.World.Step(n.Controls);
            n.World.StepBodies([.. n.Crew.Select(c => (c.Key, c.Value))]);
        }
    }

    [Fact]
    public void TheCordAndTheTelltaleAreToolsOnTheShelfFromTheTierTheyAnswer()
    {
        var cord = Assert.Single(C.Upgrades, u => u.Id == "commCord");
        var telltale = Assert.Single(C.Upgrades, u => u.Id == "trainTelltale");
        Assert.All(new[] { cord, telltale }, u =>
        {
            Assert.Equal(UpgradeKind.Tool, u.Kind);
            Assert.Equal(UpgradeSize.Small, u.Size);
            // Spec F.3's band for a small tool (upgrades.md §1): 15-25% of the next car.
            Assert.InRange(u.CostShare, 0.15, 0.25);
            Assert.NotNull(u.Tally);
        });
        // The cord answers a long train's cab that can't hear its van (Frontier on); the telltale, the first night's Passenger.
        var rich = Campaign.Campaign.New(C, 1, "t", 1) with { Scrip = 1e9 };
        Assert.False(Campaign.Campaign.BuyUpgrade(C, rich, "commCord").Ok);
        Assert.True(Campaign.Campaign.BuyUpgrade(C, rich with { Cars = 6 }, "commCord").Ok);
        Assert.True(Campaign.Campaign.BuyUpgrade(C, rich, "trainTelltale").Ok);
        // Bought, each is fitted, and the art has it to draw; as built, neither is.
        Assert.False(Tuning.Train.Kit.Cord);
        Assert.False(Tuning.Train.Kit.Telltale);
        Assert.True(With("commCord").Kit.Cord);
        Assert.True(With("commCord").Composition.Dressed("commCord"));
        Assert.True(With("trainTelltale").Kit.Telltale);
        Assert.True(With("trainTelltale").Composition.Dressed("trainTelltale"));
    }

    [Fact]
    public void PulledInACarTheCabsBellRingsWithThatCarsNumber()
    {
        var n = new Night(6, 0, trainTuning: With("commCord"));
        var bell = n.Train.Dynamics.Tuning.Kit.CordBell;
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, 4, 0, P);
        n.Crew[2] = PlayerMotor.SpawnInCab(n.Train, P);
        Assert.False(n.World.Cord.Any);
        // A press in car 4: the cab's bell, car 4, in crewmate 1's name. Held, it's the one pull.
        Run(n, 0.5, id => id == 1 ? Whistle : default);
        Assert.Equal(new CordPull(4, 1, n.World.Cord.Tick), n.World.Cord);
        Assert.Equal(1, n.World.CordPulls);
        // Let go and pulled again inside cordBell.every: nothing more.
        Run(n, 0.1);
        Run(n, 0.1, id => id == 1 ? Whistle : default);
        Assert.Equal(1, n.World.CordPulls);
        // After it, again.
        Run(n, bell.Every);
        Run(n, 0.1, id => id == 1 ? Whistle : default);
        Assert.Equal(2, n.World.CordPulls);
        // The driver's key in the cab is the whistle, never the cord.
        uint was = n.World.Cord.Tick;
        Run(n, bell.Every);
        Run(n, 0.2, id => id == 2 ? Whistle : default);
        Assert.Equal(was, n.World.Cord.Tick);
        Assert.Equal(1, n.World.Cord.By);
        Assert.True(n.World.WhistleSeconds > 0);
        // The clerk reads the pulls.
        Assert.Equal("Rope: 2.", Radio.Fill(Assert.Single(Campaign.Campaign.TallyLines(C, ["commCord"])), n.World.UpgradeCounts()));
    }

    [Fact]
    public void WithoutTheCordOrInACarCutOffTheTrainThereIsNoBell()
    {
        // Not fitted: the key in a car is nothing.
        var bare = new Night(6, 0);
        bare.Crew[1] = PlayerMotor.SpawnOnRoof(bare.Train, 4, 0, P);
        Run(bare, 0.5, _ => Whistle);
        Assert.False(bare.World.Cord.Any);
        Assert.Equal("Rope: dry.", Radio.Fill(Assert.Single(Campaign.Campaign.TallyLines(C, ["commCord"])), bare.World.UpgradeCounts()));
        // Fitted, but car 4's been cut off behind car 3: the cord parted with it.
        var n = new Night(6, 0, trainTuning: With("commCord"));
        Assert.True(n.Train.Uncouple(3, Parting.Pin));
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, 4, 0, P);
        Run(n, 0.5, _ => Whistle);
        Assert.False(n.World.Cord.Any);
        // Car 2's still on, and its cord still rings.
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, 2, 0, P);
        Run(n, 0.1);
        Run(n, 0.2, _ => Whistle);
        Assert.Equal(2, n.World.Cord.Car);
    }

    [Fact]
    public void TheTelltaleGoesOutWhenTheTrainPartsAndComesBackWhenItsWhole()
    {
        var bare = new Night(5, 0);
        Assert.Null(Telltale.Lit(bare.Train));
        var n = new Night(5, 0, trainTuning: With("trainTelltale"));
        Assert.Equal(5, Telltale.Tail(n.Train));
        Assert.True(Telltale.Lit(n.Train));
        Run(n, 0.5);
        Assert.Equal(0, n.World.TelltaleOuts);
        // A pin let go behind car 2: the cars behind it are gone from the engine's rake, and the lamp's out. It says nothing
        // of where: the engine's rake is all the cab knows.
        Assert.True(n.Train.Uncouple(2, Parting.Pin));
        Assert.False(Telltale.Lit(n.Train));
        Run(n, 0.5);
        Assert.Equal(1, n.World.TelltaleOuts);
        Assert.Equal("Lamp parts: 1.", Radio.Fill(Assert.Single(Campaign.Campaign.TallyLines(C, ["trainTelltale"])), n.World.UpgradeCounts()));
        // A parting further forward while it's out is the same lamp, out.
        Assert.True(n.Train.Uncouple(1, Parting.Pin));
        Run(n, 0.5);
        Assert.Equal(1, n.World.TelltaleOuts);
        // The clerk on a night nothing parted.
        var quiet = new Night(5, 0, trainTuning: With("trainTelltale"));
        Run(quiet, 0.5);
        Assert.Equal("Lamp parts: still up.", Radio.Fill(Assert.Single(Campaign.Campaign.TallyLines(C, ["trainTelltale"])), quiet.World.UpgradeCounts()));
    }

    [Fact]
    public void FuseesAreSoldForTheNightAndComeAboardInTheVan()
    {
        var s = new CampaignState { Scrip = 1000, Cars = 3 };
        var bought = Campaign.Campaign.BuyStores(C, s, StoreKind.Fusee);
        Assert.True(bought.Ok, bought.Refused);
        var box = C.Stores!.Fusees!;
        Assert.Equal(1000 - box.Cost, bought.State.Scrip);
        Assert.Equal(1, bought.State.Stores.Fusees);
        Assert.True(bought.State.Stores.Any);
        // Under a spare lamp: they burn out.
        Assert.True(box.Cost < C.Stores.Lamps.Cost);
        var twice = Campaign.Campaign.BuyStores(C, bought.State, StoreKind.Fusee);
        Assert.False(Campaign.Campaign.BuyStores(C, twice.State with { Scrip = 1000 }, StoreKind.Fusee).Ok);
        var kit = Campaign.Campaign.WithStores(C, Base, bought.State.Stores).Train;
        Assert.Equal(1, kit.Kit.Fusees);
        var n = new Night(5, 0, trainTuning: kit);
        n.World.Stock();
        var stocked = Assert.Single(n.World.Bodies.All, b => b.Kind == BodyKind.Fusees);
        Assert.Equal(box.Each, stocked.Charge);
        Assert.Equal(5, stocked.Parent);
    }

    /// <summary>Crewmate 1 on the ground beside car 2 of a standing train, a box of fusees in hand.</summary>
    static (Night N, Body Box) Holding()
    {
        var n = new Night(4, 0);
        var at = n.Train.Frames[2].ToWorld(new Double3(3, 0, 0));
        n.Crew[1] = PlayerMotor.SpawnOnGround(at, n.Train.Line, n.Train.Dynamics.Distance, P);
        var box = n.World.Bodies.SpawnItem(PlayerMotor.WorldPosition(n.Crew[1], n.Train) + Double3.Up * 1.1, n.Train.Dynamics.Distance, BodyKind.Fusees);
        box.Charge = 4;
        box.Carrier = 1;
        return (n, box);
    }

    [Fact]
    public void AFuseeStruckBurnsRedWhereItsDroppedForAFewMinutesAndIsGone()
    {
        var (n, box) = Holding();
        var t = n.Train.Dynamics.Tuning.Kit.Fusee;
        var from = PlayerMotor.WorldPosition(n.Crew[1], n.Train);
        // One press, held: one fusee.
        Run(n, 0.5, _ => Fire);
        var fusee = Assert.Single(n.World.Bodies.All, b => b.Kind == BodyKind.Fusee);
        Assert.Equal(3, box.Charge);
        Assert.Equal(1, n.World.FuseesStruck);
        // Pressed again inside kit.fusee.every: nothing.
        Run(n, 0.1);
        Run(n, 0.1, _ => Fire);
        Assert.Equal(3, box.Charge);
        // Nobody picks a burning fusee up.
        Assert.NotEqual(fusee, n.World.Bodies.InReach(n.Crew[1], n.Train));
        // It lies at the feet, on the ground, and it's a light: further than a hand lamp's, fainter, far under a flare's.
        Run(n, 2);
        var lies = Bodies.WorldCentre(fusee, n.Train);
        Assert.True(((lies - from) with { Y = 0 }).Length < 1.5, $"it lies {((lies - from) with { Y = 0 }).Length:0.00} m off");
        var light = Assert.Single(n.World.AllEmitters(), e => e.Kind == EmitterKind.Fusee && e.Sense == Sense.Light && e.Source == fusee.Id);
        var lamp = SenseTuning.Load().Of("handLamp").Single(g => g.Sense == Sense.Light);
        var flare = SenseTuning.Load().Of("flare").Single(g => g.Sense == Sense.Light);
        Assert.InRange(light.Radius, lamp.Radius, flare.Radius);
        Assert.True(light.Strength < lamp.Strength);
        // Burning a few minutes: still alight a minute in, its burn left in Charge for every machine...
        Run(n, 60);
        Assert.Contains(fusee, n.World.Bodies.All);
        Assert.InRange(fusee.Charge, 0.01, 0.99);
        // ...and gone at its end.
        Run(n, t.BurnSeconds - 60);
        Assert.DoesNotContain(n.World.Bodies.All, b => b.Kind == BodyKind.Fusee);
        Assert.DoesNotContain(n.World.AllEmitters(), e => e.Kind == EmitterKind.Fusee);
        Assert.InRange(t.BurnSeconds, 120, 300);
        // The clerk reads the night's fusees after the upgrades.
        var report = new RunReport(RunEnd.Delivered, 100, 10, 3, 0, 1, 900, 10, 0, 0, 890, 3, 0) { Counts = n.World.UpgradeCounts() };
        Assert.Contains("Flames: 1.", Radio.Tally(report, []));
    }

    [Fact]
    public void AnEmptyBoxStrikesNothing()
    {
        var (n, box) = Holding();
        box.Charge = 0;
        Run(n, 0.5, _ => Fire);
        Assert.DoesNotContain(n.World.Bodies.All, b => b.Kind == BodyKind.Fusee);
        Assert.DoesNotContain("Flames", string.Join("\n", Radio.Tally(new RunReport(RunEnd.Delivered, 100, 10, 3, 0, 1, 900, 10, 0, 0, 890, 3, 0) { Counts = n.World.UpgradeCounts() }, [])));
    }
}
