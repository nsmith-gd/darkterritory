using Ballast;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The dining car (docs/design/upgrades.md §4 silly, §8.5; note 959; the director, 13 Oct: "If we have a dining car, we should
/// be able to order drinks at the bar"): bought at the blacksmith's, a cargo car converted behind the crew cars, its table
/// laid and its bar; Use behind the counter pours a drink into empty hands, a night's worth and no more, drunk as a beer. Note
/// 1016 (the director's Look Review verdict, 23 Oct): the bar's a pub's, at the back, away from the lockers (eight of them)
/// and the doors, with stools to sit on and a way behind it to pour from.
/// </summary>
public class DiningCarTests
{
    static readonly CampaignTuning C = DataFile.Load<CampaignTuning>(Path.Combine(DataFile.FindContentRoot(), CampaignTuning.File));
    static readonly PlayerTuning P = Tuning.Player;
    static TrainTuning With(params string[] ids) => Campaign.Campaign.Apply(C, ids, new Loadout(Tuning.Train, Tuning.Boiler, Tuning.Combat, Tuning.Enemies)).Train;

    static World Night(TrainTuning t, int cars = 6)
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(20_000)]));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(t, cars, 1)), line, 1_000);
        var world = new World(train, Tuning.Combat);
        world.EnableBodies();
        world.Stock();
        return world;
    }

    static void Tick(World w, ref PlayerState s, PlayerIntent intent)
    {
        w.BeginTick();
        w.CrewAct(ref s, intent, 1);
        w.Step(new TrainControls { Reverser = 1, Brake = 1 });
        PlayerMotor.Step(ref s, intent, w.Train, P, w.Train.Dynamics.Tuning, SimConstants.TickSeconds, applyLook: false);
    }

    static readonly PlayerIntent Use = new() { Buttons = PlayerButtons.Use };

    [Fact]
    public void BoughtItsTheCarBehindTheCrewCarLaidAndBarredAndCarryingNothing()
    {
        var bare = Night(Tuning.Train);
        Assert.DoesNotContain(bare.Train.Vehicles, v => v.Kind == VehicleKind.Dining);
        var t = With("crewCar", "diningCar");
        Assert.True(t.Composition.DiningCar);
        var w = Night(t);
        var dining = Assert.Single(w.Train.Vehicles, v => v.Kind == VehicleKind.Dining);
        // Right behind the crew car, carrying nothing; the cargo cars still there behind it.
        Assert.Equal(VehicleKind.Utility, w.Train.Vehicles[dining.Id - 1].Kind);
        Assert.Equal(CargoKind.None, dining.Cargo);
        Assert.Contains(w.Train.Vehicles, v => v.Kind == VehicleKind.Cargo);
        var shape = w.Train.Frames[dining.Id].Shape;
        var layout = Assert.IsType<DiningLayout>(shape.Dining);
        Assert.Single(shape.Interactables, i => i.Kind == InteractableKind.Bar);
        Assert.DoesNotContain(shape.Solids, x => x.Part == PartKind.Cargo);
        // The table and the counter inside its walls, the aisle clear past both on the left, from end door to end door.
        var room = shape.Interior!.Value;
        Assert.True(layout.Table.Min.X > 0 && layout.Counter.Min.X > 0 && layout.Counter.Max.Z < room.Max.Z);
        Assert.All(shape.DoorList.Where(d => Math.Abs(d.Box.Centre.Z) > room.Max.Z - 0.5), d => Assert.True(d.Box.Max.X <= layout.Table.Min.X));
        // Its drinks for the night, kept where the BBQ deck's cooler keeps its beers.
        Assert.Equal(t.Composition.Dining.Drinks, dining.Beers);
        // Never with no cargo car to spare: a two-car train (one cargo car and the van) keeps its cargo car.
        Assert.DoesNotContain(Night(t, cars: 2).Train.Vehicles, v => v.Kind == VehicleKind.Dining);
    }

    [Fact]
    public void ThePubsBarIsAtTheBackAwayFromTheLockersAndTheDoorsWithItsStoolsAndAWayBehindIt()
    {
        // Bought without a crew car, it's the kit's car: the crew lockers along its left wall at the front (note 173).
        var w = Night(With("diningCar"));
        int car = w.Train.Vehicles.Single(v => v.Kind == VehicleKind.Dining).Id;
        var shape = w.Train.Frames[car].Shape;
        var d = shape.Dining!;
        var room = shape.Interior!.Value;
        // Eight lockers (the director, 23 Oct: "reduce the locker count to 8").
        Assert.Equal(8, shape.Lockers.Count);
        // The bar down the rear half, right of the aisle and the end doors, clear of the lockers' row by more than its stools.
        Assert.True(d.Counter.Min.Z > 0 && d.Counter.Max.Z < room.Max.Z - 1.0, $"{d.Counter}");
        Assert.True(d.Counter.Min.Z - shape.Lockers[^1].Box.Max.Z > 2.0, $"{d.Counter} after {shape.Lockers[^1].Box}");
        Assert.All(shape.DoorList.Where(x => Math.Abs(x.Box.Centre.Z) > room.Max.Z - 0.5), x => Assert.True(x.Box.Max.X <= d.Counter.Min.X, $"{x.Box} beside {d.Counter}"));
        // The barman's walkway: a crewmate's width between the counter and the back bar, open at the rear end, closed at the front.
        Assert.True(d.Behind.Max.X - d.Behind.Min.X >= 2 * P.Radius + 0.02, $"{d.Behind}");
        Assert.Equal(d.Counter.Max.X, d.Behind.Min.X, 6);
        Assert.Equal(d.BackBar.Min.X, d.Behind.Max.X, 6);
        Assert.True(d.Return.Max.X >= room.Max.X - 1e-6 && d.Return.Min.Z <= d.Counter.Min.Z);
        Assert.True(room.Max.Z - d.Counter.Max.Z >= 2 * P.Radius + 0.2);
        // The pumps behind the counter, in the walkway; the stools down its front, facing it.
        Assert.True(d.Behind.ContainsXZ(d.Bar), $"{d.Bar}");
        var stools = shape.Lounges.Where(l => l.Kind == LoungeKind.Stool).ToList();
        Assert.Equal(t0.Composition.Dining.Bar.Stools, stools.Count);
        Assert.All(stools, l => Assert.True(l.At.X < d.Counter.Min.X && l.At.Z > d.Counter.Min.Z && l.At.Z < d.Counter.Max.Z && l.Yaw == -Math.PI / 2));
    }

    static readonly TrainTuning t0 = Tuning.Train;

    [Fact]
    public void AStoolIsSatOnFromTheAisleAndGotUpFromIntoIt()
    {
        var w = Night(With("diningCar"));
        int car = w.Train.Vehicles.Single(v => v.Kind == VehicleKind.Dining).Id;
        var shape = w.Train.Frames[car].Shape;
        int k = shape.Lounges.ToList().FindIndex(l => l.Kind == LoungeKind.Stool);
        var stand = shape.Interactables.Single(i => i.Kind == InteractableKind.Bench && i.Index == k).Position;
        var s = new PlayerState { Parent = car, Position = stand, Surface = Surface.Deck, Health = P.Health, Yaw = -Math.PI / 2, Kit = P.StartingKit };
        var seat = new PlayerIntent { Actions = PlayerActions.Seat };
        Bbq.Sit(ref s, seat, w.Train, []);
        Assert.True(s.Has(PlayerFlags.Lounging));
        Assert.Equal(shape.Lounges[k].At, s.Position);
        Assert.Equal(-Math.PI / 2, s.Yaw);
        Bbq.Sit(ref s, seat, w.Train, []);
        Assert.False(s.Has(PlayerFlags.Lounging));
        Assert.Equal(stand, s.Position);
    }

    [Fact]
    public void FromInFrontOfTheCounterNothingsPouredOnlyFromBehindIt()
    {
        var w = Night(With("diningCar"));
        int car = w.Train.Vehicles.Single(v => v.Kind == VehicleKind.Dining).Id;
        var shape = w.Train.Frames[car].Shape;
        var d = shape.Dining!;
        // Stood at the stools, facing the pumps across the counter: nothing.
        var front = new PlayerState { Parent = car, Position = new Double3(d.Counter.Min.X - 0.35, d.Bar.Y, d.Bar.Z), Surface = Surface.Deck, Health = P.Health,
            Yaw = -Math.PI / 2, Kit = P.StartingKit };
        for (int i = 0; i < 3; i++)
        {
            Tick(w, ref front, Use);
            Tick(w, ref front, default);
        }
        Assert.Null(w.Bodies.CarriedBy(1));
        Assert.Equal(0, w.BarDrinks);
        Assert.NotEqual(InteractableKind.Bar, CrewActions.Nearest(front, w.Train));
    }

    [Fact]
    public void BehindTheBarADrinkIsPouredAndDrunkUntilTheBarsDry()
    {
        var t = With("diningCar");
        var w = Night(t);
        int car = w.Train.Vehicles.Single(v => v.Kind == VehicleKind.Dining).Id;
        var layout = w.Train.Frames[car].Shape.Dining!;
        // Behind the counter at its pumps (note 1016), facing across it to the stools (-X), empty-handed.
        var s = new PlayerState { Parent = car, Position = layout.Bar, Surface = Surface.Deck, Health = P.Health, Yaw = Math.PI / 2, Kit = P.StartingKit };
        int drinks = t.Composition.Dining.Drinks;
        for (int n = 1; n <= drinks; n++)
        {
            Tick(w, ref s, Use);
            Assert.Equal(BodyKind.Drink, w.Bodies.CarriedBy(1)?.Kind);
            Assert.Equal(drinks - n, w.Train.Vehicles[car].Beers);
            Assert.Equal(n, w.BarDrinks);
            // Let go, then held: drunk, as a beer's drunk, and gone.
            Tick(w, ref s, default);
            for (int i = 0; i < 2.5 * SimConstants.TickRate; i++)
                Tick(w, ref s, Use);
            Assert.Null(w.Bodies.CarriedBy(1));
            Assert.DoesNotContain(w.Bodies.All, b => b.Kind == BodyKind.Drink);
            Tick(w, ref s, default);
        }
        // Dry: nothing poured.
        Tick(w, ref s, Use);
        Assert.Null(w.Bodies.CarriedBy(1));
        Assert.Equal(drinks, w.BarDrinks);
    }
}
