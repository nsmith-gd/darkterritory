using Ballast;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The BBQ deck (ARCHITECTURE §8 note 698; the director, 13 Oct 2026: "an extended front car cow catcher that has a deck, a
/// couple muskoka chairs that can be sat in, a plastic cooler with beers, and a BBQ mounted on a stand on the deck. Each
/// character needs to be able to interact with each object so they can have a tiny "party" at the front"): a deck on the
/// engine's front, walked to along the running boards; chairs to sit in, beers to take and drink, a grill to light and cook on.
/// </summary>
public class BbqDeckTests
{
    static readonly CampaignTuning C = DataFile.Load<CampaignTuning>(Path.Combine(DataFile.FindContentRoot(), CampaignTuning.File));
    static readonly PlayerTuning P = Tuning.Player;
    static readonly TrainTuning Fitted = Campaign.Campaign.Apply(C, ["bbqDeck"], new Loadout(Tuning.Train, Tuning.Boiler, Tuning.Combat, Tuning.Enemies)).Train;
    static BbqTuning Bbq => Fitted.Composition.Bbq;

    static World Night(TrainTuning t, double speed = 0)
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(20_000)]));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(t, 4, 1)), line, 1_000);
        if (speed > 0)
            train.Dynamics.Velocity = speed;
        var world = new World(train, Tuning.Combat);
        world.EnableBodies(); // the host's
        world.Stock();
        return world;
    }

    /// <summary>A crewmate on the engine, stood at <paramref name="at"/> (its frame), facing down the line.</summary>
    static PlayerState At(World w, Double3 at) => PlayerMotor.SpawnInCab(w.Train, P) with { Position = at, Yaw = 0, HeldSlot = 0 };

    static void Tick(World w, ref PlayerState s, PlayerIntent intent, int id = 1)
    {
        w.BeginTick();
        w.CrewAct(ref s, intent, id);
        w.Step(new TrainControls { Reverser = 1, Brake = 1 });
        PlayerMotor.Step(ref s, intent, w.Train, P, w.Train.Dynamics.Tuning, SimConstants.TickSeconds, applyLook: false);
    }

    static void Hold(World w, ref PlayerState s, PlayerIntent intent, double seconds)
    {
        for (int i = 0; i < seconds * SimConstants.TickRate; i++)
            Tick(w, ref s, intent);
    }

    static readonly PlayerIntent Use = new() { Buttons = PlayerButtons.Use };

    [Fact]
    public void TheDeckIsBuiltOnTheEnginesFrontOnlyWhenItsFitted()
    {
        Assert.True(Fitted.Composition.BbqDeck);
        var bare = Night(Tuning.Train).Train.Frames[0].Shape;
        Assert.Null(bare.Bbq);
        var engine = Night(Fitted).Train.Frames[0].Shape;
        var deck = Assert.IsType<BbqLayout>(engine.Bbq);
        // Ahead of the engine's front (its length is the train's), at the deck's height, as long as the tuning says.
        Assert.Equal(-engine.HalfLength - Bbq.DeckLength, deck.Front, 6);
        var planks = Assert.Single(engine.Solids, x => x.Part == PartKind.BbqDeck);
        Assert.Equal(Fitted.Geometry.Engine.DeckHeight, planks.Box.Max.Y, 6);
        Assert.True(planks.Box.Max.Z <= -engine.HalfLength + 1e-9);
        // Two chairs, the cooler and the grill, each worked from the deck; railed round.
        Assert.Equal(2, engine.Interactables.Count(i => i.Kind == InteractableKind.Chair));
        Assert.Single(engine.Interactables, i => i.Kind == InteractableKind.Cooler);
        Assert.Single(engine.Interactables, i => i.Kind == InteractableKind.Grill);
        Assert.Equal(3, engine.Solids.Count(x => x.Part == PartKind.Rail));
        // The running boards run on to it past the cab's front.
        Assert.Equal(2, engine.Solids.Count(x => x.Part == PartKind.RunningBoard && x.Box.Min.Z <= -engine.HalfLength + 1e-9));
        // Nothing else on the train changed: the cars are as they were.
        Assert.Equal(Night(Tuning.Train).Train.Frames[1].Shape.Solids, Night(Fitted).Train.Frames[1].Shape.Solids);
    }

    [Fact]
    public void YouWalkOutAlongTheRunningBoardOntoTheDeckAtSpeedAndTheRailHoldsYou()
    {
        var w = Night(Fitted, speed: 15);
        var engine = w.Train.Frames[0].Shape;
        double board = Fitted.Geometry.RoofWidth / 2 + Fitted.Geometry.Engine.RunningBoardWidth / 2;
        // On the right-hand running board, alongside the cab, walking forward.
        var s = At(w, new Double3(board, Fitted.Geometry.Engine.DeckHeight, -engine.HalfLength + 3));
        Hold(w, ref s, new PlayerIntent { MoveZ = 1 }, 6);
        Assert.Equal(0, s.Parent);
        Assert.Equal(Surface.Deck, s.Surface);
        // Out past the engine's front, onto the deck, and stopped by its front rail, not off the end.
        Assert.True(s.Position.Z < -engine.HalfLength - 1, $"at z {s.Position.Z:0.00}");
        Assert.True(s.Position.Z > engine.Bbq!.Front, $"through the rail to z {s.Position.Z:0.00}");
        Assert.True(Math.Abs(s.Position.Y - Fitted.Geometry.Engine.DeckHeight) < 0.05);
    }

    [Fact]
    public void AChairIsSatInAndGotUpFromAndHeldByWhoeversInIt()
    {
        var w = Night(Fitted, speed: 12);
        var deck = w.Train.Frames[0].Shape.Bbq!;
        var s = At(w, deck.Chairs[0] + new Double3(0, 0, 0.4));
        Tick(w, ref s, new PlayerIntent { Actions = PlayerActions.Seat });
        Assert.True(s.Has(PlayerFlags.Lounging));
        Assert.False(s.Has(PlayerFlags.Seated));
        Assert.Equal(deck.Chairs[0].X, s.Position.X, 3);
        Assert.Equal(deck.Chairs[0].Z, s.Position.Z, 3);
        // Sat, the stick moves nothing, and the train running carries you with it in the chair.
        Hold(w, ref s, new PlayerIntent { MoveZ = 1, MoveX = 1 }, 2);
        Assert.True(s.Has(PlayerFlags.Lounging));
        Assert.True((s.Position - deck.Chairs[0]).Length < 0.05);
        // Jump gets you up; so does the press again.
        Tick(w, ref s, new PlayerIntent { Buttons = PlayerButtons.Jump });
        Assert.False(s.Has(PlayerFlags.Lounging));
        s = At(w, deck.Chairs[1] + new Double3(0, 0, 0.4));
        Tick(w, ref s, new PlayerIntent { Actions = PlayerActions.Seat });
        Assert.True(s.Has(PlayerFlags.Lounging));
        Tick(w, ref s, new PlayerIntent { Actions = PlayerActions.Seat });
        Assert.False(s.Has(PlayerFlags.Lounging));
        // Somebody sat in it keeps it.
        var sitter = At(w, deck.Chairs[0] + new Double3(0, 0, 0.4));
        var other = At(w, deck.Chairs[0] + new Double3(0, 0, 0.4));
        w.BeginTick();
        w.CrewAct(ref sitter, new PlayerIntent { Actions = PlayerActions.Seat }, 1);
        w.StepBodies([(1, sitter), (2, other)]);
        w.CrewAct(ref other, new PlayerIntent { Actions = PlayerActions.Seat }, 2);
        Assert.True(sitter.Has(PlayerFlags.Lounging));
        Assert.False(other.Has(PlayerFlags.Lounging));
    }

    [Fact]
    public void TheCoolerHasItsBeersAndABeerTakesTheEdgeOffTheCold()
    {
        var w = Night(Fitted);
        var deck = w.Train.Frames[0].Shape.Bbq!;
        Assert.Equal(Bbq.Beers, w.Train.Vehicles[0].Beers);
        var cooler = w.Train.Frames[0].Shape.Interactables.Single(i => i.Kind == InteractableKind.Cooler);
        var s = At(w, cooler.Position) with { Cold = 100 };
        Tick(w, ref s, Use);
        Assert.Equal(BodyKind.Beer, w.Bodies.CarriedBy(1)?.Kind);
        Assert.Equal(Bbq.Beers - 1, w.Train.Vehicles[0].Beers);
        // Let go, then held: drunk, as a healing find's used.
        Tick(w, ref s, default);
        Assert.Equal(BodyKind.Beer, w.Bodies.CarriedBy(1)?.Kind);
        double cold = s.Cold;
        Hold(w, ref s, Use, 2.5);
        Assert.Null(w.Bodies.CarriedBy(1));
        Assert.True(s.Cold <= cold - Bbq.BeerWarmth + 3, $"cold {s.Cold:0.0} from {cold:0.0}");
        Assert.DoesNotContain(w.Bodies.All, b => b.Kind == BodyKind.Beer);
        // Every one taken, the cooler's empty.
        w.Train.Vehicles[0].Beers = 0;
        Tick(w, ref s, default);
        Tick(w, ref s, Use);
        Assert.Null(w.Bodies.CarriedBy(1));
        Assert.True(deck.Chairs.Count == 2);
    }

    [Fact]
    public void APartyAtTheFrontABeerTakenToAChairAndDrunkSatInIt()
    {
        // The director: "Each character needs to be able to interact with each object so they can have a tiny party at the
        // front." A beer from the cooler, into a chair with it (still in hand), drunk sat there, then up.
        var w = Night(Fitted, speed: 10);
        var engine = w.Train.Frames[0].Shape;
        var cooler = engine.Interactables.Single(i => i.Kind == InteractableKind.Cooler);
        var s = At(w, cooler.Position);
        Tick(w, ref s, Use);
        Tick(w, ref s, default);
        Assert.Equal(BodyKind.Beer, w.Bodies.CarriedBy(1)?.Kind);
        s = s with { Position = engine.Bbq!.Chairs[1] + new Double3(0, 0, 0.4) };
        Tick(w, ref s, new PlayerIntent { Buttons = PlayerButtons.Use, Actions = PlayerActions.Seat });
        Tick(w, ref s, default);
        Assert.True(s.Has(PlayerFlags.Lounging));
        Assert.Equal(BodyKind.Beer, w.Bodies.CarriedBy(1)?.Kind);
        Hold(w, ref s, Use, 2.5);
        Assert.Null(w.Bodies.CarriedBy(1));
        Assert.True(s.Has(PlayerFlags.Lounging));
        Tick(w, ref s, default);
        Tick(w, ref s, new PlayerIntent { Buttons = PlayerButtons.Use, Actions = PlayerActions.Seat });
        Assert.False(s.Has(PlayerFlags.Lounging));
    }

    [Fact]
    public void TheGrillIsLitAndCooksASausageThatHealsAndItBurnsOut()
    {
        var w = Night(Fitted);
        var grill = w.Train.Frames[0].Shape.Interactables.Single(i => i.Kind == InteractableKind.Grill);
        var s = At(w, grill.Position) with { Health = P.Health - 40 };
        Assert.False(Train.Bbq.Lit(w.Train.Vehicles[0]));
        Hold(w, ref s, Use, Bbq.LightSeconds + 0.1);
        Assert.True(Train.Bbq.Lit(w.Train.Vehicles[0]));
        // Lit, it's warm by it, and it brings the hounds as a hot boiler does.
        Assert.True(PlayerMotor.NearHeat(s, w.Train));
        Assert.True(Train.Bbq.AnyLit(w.Train));
        Hold(w, ref s, Use, Bbq.GrillSeconds);
        var sausage = w.Bodies.CarriedBy(1);
        Assert.Equal(BodyKind.Sausage, sausage?.Kind);
        // Eaten: its health back.
        Tick(w, ref s, default);
        s = s with { Position = s.Position + new Double3(0.6, 0, 0.8) };
        int before = s.Health;
        Hold(w, ref s, Use, 2.5);
        Assert.Null(w.Bodies.CarriedBy(1));
        Assert.Equal(before + Bbq.SausageHeals, s.Health);
        // The charcoal burns down and it's out.
        w.Train.Vehicles[0].Grill = 1;
        Hold(w, ref s, default, 1.2);
        Assert.False(Train.Bbq.Lit(w.Train.Vehicles[0]));
        Assert.False(Train.Bbq.ByTheGrill(s, w.Train));
    }

    [Fact]
    public void TheGrillAndTheCoolerAreEveryClientsToo()
    {
        // Protocol 59: the vehicle record carries the grill's charcoal and the cooler's beers.
        var w = Night(Fitted);
        w.Train.Vehicles[0].Grill = 123.5;
        w.Train.Vehicles[0].Beers = 17;
        var state = w.Train.Capture();
        var copy = Night(Fitted);
        copy.Train.Restore(state);
        Assert.Equal(123.5, copy.Train.Vehicles[0].Grill, 6);
        Assert.Equal(17, copy.Train.Vehicles[0].Beers);
        // And over the wire: the host's records applied to a client's world.
        var controls = new TrainControls();
        var records = Net.WorldRecords.Capture(w, controls, []);
        var client = Night(Fitted);
        Net.WorldRecords.Apply(records, client, ref controls, []);
        Assert.Equal(123.5, client.Train.Vehicles[0].Grill, 3);
        Assert.Equal(17, client.Train.Vehicles[0].Beers);
    }
}
