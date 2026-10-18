using Ballast;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The whimsies' second round (ARCHITECTURE §8 note 744; docs/design/upgrades.md §7.4, §8.2): the hammock across the guard
/// van's back end (warm-ish), the hot tub on the engine's hood (warm while there's steam, and it eats steam), the fishing rod
/// (a line in the water, a fish; grilled on the BBQ deck, a better meal than a sausage).
/// </summary>
public class LoungeTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly CampaignTuning C = DataFile.Load<CampaignTuning>(Path.Combine(Content, CampaignTuning.File));
    static readonly Loadout Base = new(Tuning.Train, Tuning.Boiler, Tuning.Combat, Tuning.Enemies);
    static readonly PlayerTuning P = Tuning.Player;

    static TrainTuning Dressed(params string[] ids) => Campaign.Campaign.Apply(C, ids, Base).Train;

    static World World(TrainTuning t, RailLine? line = null, double at = 2_000)
    {
        line ??= new RailLine(new LineDefinition("t", [new TrackSegment(20_000)]));
        var world = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(t, 4, 1)), line, at, Tuning.Boiler), Tuning.Combat);
        world.EnableBodies();
        world.Stock();
        return world;
    }

    static void Tick(World w, ref PlayerState s, PlayerIntent intent, int id = 1)
    {
        w.BeginTick();
        w.CrewAct(ref s, intent, id);
        w.StepBodies([(id, s)]);
        w.Step(new TrainControls { Reverser = 1, Brake = 1 });
        PlayerMotor.Step(ref s, intent, w.Train, P, w.Train.Dynamics.Tuning, SimConstants.TickSeconds, applyLook: false);
    }

    static void Hold(World w, ref PlayerState s, PlayerIntent intent, double seconds)
    {
        for (int i = 0; i < seconds * SimConstants.TickRate; i++)
            Tick(w, ref s, intent);
    }

    static readonly PlayerIntent Use = new() { Buttons = PlayerButtons.Use };
    static readonly PlayerIntent Sit = new() { Buttons = PlayerButtons.Use, Actions = PlayerActions.Seat };

    static PlayerState At(World w, int car, Double3 local) =>
        PlayerMotor.SpawnInCab(w.Train, P) with { Parent = car, Position = local, Yaw = 0, HeldSlot = 0 };

    [Fact]
    public void EachIsOnTheTrainOnceBought()
    {
        foreach (var id in new[] { "hammock", "hotTub", "fishingRod" })
        {
            var u = Assert.Single(C.Upgrades, u => u.Id == id);
            Assert.Equal(UpgradeKind.Whimsy, u.Kind);
            Assert.NotNull(u.Line);
            Assert.NotNull(u.Tally);
            Assert.Contains(id, Dressed(id).Composition.Dress);
        }
        var plain = World(Tuning.Train);
        Assert.Empty(plain.Train.Frames[^1].Shape.Lounges);
        Assert.Empty(plain.Train.Frames[0].Shape.Lounges);
        Assert.DoesNotContain(plain.Bodies.All, b => b.Kind == BodyKind.FishingRod);
        var w = World(Dressed("hammock", "hotTub", "fishingRod"));
        Assert.Equal(LoungeKind.Hammock, Assert.Single(w.Train.Frames[^1].Shape.Lounges).Kind);
        Assert.Equal(2, w.Train.Frames[0].Shape.Lounges.Count(l => l.Kind == LoungeKind.HotTub));
        Assert.Single(w.Bodies.All, b => b.Kind == BodyKind.FishingRod);
    }

    [Fact]
    public void TheHotTubIsSatInAndEatsTheSteam()
    {
        var w = World(Dressed("hotTub"));
        var engine = w.Train.Frames[0].Shape;
        var spot = engine.Interactables.First(i => i.Kind == InteractableKind.HotTub);
        var s = At(w, 0, spot.Position) with { Surface = Surface.Roof };
        Tick(w, ref s, Sit);
        Assert.True(s.Has(PlayerFlags.Lounging));
        Assert.Equal(LoungeKind.HotTub, Bbq.LoungingIn(s, w.Train)?.Kind);
        // Out on the roof in the night (and warm, until note 852 cut the cold).
        w.Train.Boiler.Pressure = 80;
        double steam = w.Train.Boiler.Pressure;
        Hold(w, ref s, default, 5);
        // A second soaking draws it at the tub's rate (and the boiler's own losses beside it).
        var plain = World(Dressed("hotTub"));
        plain.Train.Boiler.Pressure = 80;
        var t = new PlayerState();
        for (int i = 0; i < 5 * SimConstants.TickRate; i++)
            Tick(plain, ref t, default, 2);
        double drawn = (80 - w.Train.Boiler.Pressure) - (80 - plain.Train.Boiler.Pressure);
        Assert.Equal(w.Train.Dynamics.Tuning.Composition.Whimsy.TubSteam * 5, drawn, 1);
        // Up and out with the seat press.
        Tick(w, ref s, default);
        Tick(w, ref s, Sit);
        Assert.False(s.Has(PlayerFlags.Lounging));
    }

    /// <summary>
    /// A generated night with water beside the line, and a dry spot at its edge facing it: where Jacob would fish (note 572),
    /// found the way he's placed.
    /// </summary>
    static (Route.Route Route, Double3 Spot, double Yaw, double Along) Shore()
    {
        var anywhere = Tuning.Enemies.Jacob with { Chance = 1 };
        for (int seed = 1; seed <= 24; seed++)
        {
            var route = LineGen.Routes.Generate(Content, $"frontier:{seed}", 4);
            var w = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 4, 1)), route.Build(), 400), Tuning.Combat);
            if (Enemies.Jacob.Site(w, route, anywhere) is { } site)
                return (route, site.At, site.Yaw, site.Along);
        }
        throw new InvalidOperationException("no shore on any generated night");
    }

    [Fact]
    public void ALineInTheWaterLandsAFishAndTheClerkHasIt()
    {
        var (route, spot, yaw, along) = Shore();
        var w = World(Dressed("fishingRod"), route.Build(), 400);
        var rod = Assert.Single(w.Bodies.All, b => b.Kind == BodyKind.FishingRod);
        rod.Carrier = 1;
        rod.Claimed = true;
        var s = PlayerMotor.SpawnOnGround(spot, w.Train.Line, along, P) with { Yaw = yaw, HeldSlot = 0 };
        Assert.True(w.Angling(s, 1), "the line's in the water");
        // Facing away from it, it isn't.
        Assert.False(w.Angling(s with { Yaw = yaw + Math.PI }, 1));
        var bite = w.Train.Dynamics.Tuning.Composition.Whimsy.Bite;
        Hold(w, ref s, Use, bite[^1] + 1);
        Assert.Contains(w.Bodies.All, b => b.Kind == BodyKind.Fish);
        Assert.Equal(BodyKind.FishingRod, w.Bodies.CarriedBy(1)?.Kind);
        Assert.Contains(w.Attribution.Log, i => i.Kind == Sim.Run.IncidentKind.Whimsy && i.What == "A fish caught" && i.Actor == 1);
    }

    [Fact]
    public void AFishGrilledOnTheDeckIsABetterMealThanASausage()
    {
        var w = World(Dressed("fishingRod", "bbqDeck"));
        var deck = w.Train.Frames[0].Shape;
        var grill = deck.Interactables.Single(i => i.Kind == InteractableKind.Grill);
        w.Train.Vehicles[0].Grill = 100;
        var fish = w.Bodies.SpawnCrate(w.Train, 0, grill.Position + new Double3(0, 1.0, 0), BodyKind.Fish);
        fish.Carrier = 1;
        fish.Claimed = true;
        var s = PlayerMotor.SpawnInCab(w.Train, P) with { Position = grill.Position, Yaw = 0, HeldSlot = 0, Health = 40 };
        Hold(w, ref s, Use, w.Train.Dynamics.Tuning.Composition.Bbq.GrillSeconds + 0.2);
        Assert.Equal(BodyKind.GrilledFish, w.Bodies.CarriedBy(1)?.Kind);
        Assert.DoesNotContain(w.Bodies.All, b => b.Kind == BodyKind.Fish || b.Kind == BodyKind.Sausage);
        // Let go, stepped back from the grill, then held: eaten, and it heals what a fish does, more than a sausage.
        Tick(w, ref s, default);
        s = s with { Position = s.Position + new Double3(0.6, 0, 0.8) };
        int before = s.Health;
        Hold(w, ref s, Use, 2.5);
        Assert.Null(w.Bodies.CarriedBy(1));
        var whimsy = w.Train.Dynamics.Tuning.Composition.Whimsy;
        Assert.Equal(before + whimsy.FishHeals, s.Health);
        Assert.True(whimsy.FishHeals > w.Train.Dynamics.Tuning.Composition.Bbq.SausageHeals);
    }
}
