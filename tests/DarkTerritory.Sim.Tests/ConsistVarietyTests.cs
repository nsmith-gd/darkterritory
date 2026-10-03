using Ballast;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The consist's variety (GDD §10 "engine, armour, cannons, utility cars and many freight cars", §26 car reads, spec F.3;
/// ARCHITECTURE §8 note 184): the crew car, the armoured conversion, the second guard car, and the consist's small upgrades.
/// </summary>
public class ConsistVarietyTests
{
    static readonly CampaignTuning C = DataFile.Load<CampaignTuning>(Path.Combine(DataFile.FindContentRoot(), CampaignTuning.File));
    static readonly TrainTuning T = Tuning.Train;
    static readonly PlayerTuning P = Tuning.Player;
    const double Dt = SimConstants.TickSeconds;

    static TrainTuning With(params string[] upgrades) =>
        Campaign.Campaign.Apply(C, upgrades, new Loadout(T, Tuning.Boiler, Tuning.Combat, Tuning.Enemies)).Train;

    static TrainOnLine Train(TrainTuning t, int cars = 6, bool boiler = false)
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(20_000)]));
        return new TrainOnLine(new TrainDynamics(Consist.Uniform(t, cars, 1)), line, 1_000, boiler ? Tuning.Boiler : null);
    }

    static VehicleKind[] Kinds(TrainTuning t, int cars) => [.. Consist.Uniform(t, cars, 1).Vehicles.Select(v => v.Kind)];

    [Fact]
    public void WithoutUpgradesTheConsistIsEngineCargoAndTheGuardVan()
    {
        Assert.Equal([VehicleKind.Engine, .. Enumerable.Repeat(VehicleKind.Cargo, 5), VehicleKind.Guard], Kinds(T, 6));
        Assert.DoesNotContain(Consist.Uniform(T, 6, 1).Vehicles, v => v.Armoured);
    }

    [Fact]
    public void TheCrewsPurchasesAreMadeOfItsCargoCars()
    {
        var t = With("crewCar", "secondGuardCar", "armouredCar");
        // The crew car behind the engine, the second guard car in the middle of the cargo left, the van armoured.
        Assert.Equal([VehicleKind.Engine, VehicleKind.Utility, VehicleKind.Cargo, VehicleKind.Cargo, VehicleKind.Guard, VehicleKind.Cargo, VehicleKind.Guard], Kinds(t, 6));
        var consist = Consist.Uniform(t, 6, 1);
        Assert.Equal([6], consist.Vehicles.Where(v => v.Armoured).Select(v => v.Id));
        // Converted, a car carries no freight (spec F.3: it "costs a cargo slot"); the night pays on the cargo cars alone.
        Assert.All(consist.Vehicles.Where(v => v.Kind is VehicleKind.Utility || v.Id == 4), v => Assert.Equal(CargoKind.None, v.Cargo));
        Assert.Equal(3, consist.Vehicles.Count(v => v.Kind == VehicleKind.Cargo));
        // Never fewer than minCargoCars: a short consist takes the gun before the stove, and stops there.
        Assert.Equal([VehicleKind.Engine, VehicleKind.Cargo, VehicleKind.Guard, VehicleKind.Guard], Kinds(t, 3));
        Assert.Equal([VehicleKind.Engine, VehicleKind.Utility, VehicleKind.Cargo, VehicleKind.Guard], Kinds(With("crewCar"), 3));
        // The same list makes the same train, wherever it's built (every machine builds it from the upgrades).
        Assert.Equal(Kinds(t, 12), Kinds(With("crewCar", "secondGuardCar", "armouredCar"), 12));
    }

    [Fact]
    public void TheSecondGuardCarIsAThirdGun()
    {
        var train = Train(With("secondGuardCar"));
        Assert.Equal(3, train.Vehicles.Count(v => v.HasGun));
        var middle = train.Vehicles.Single(v => v.Kind == VehicleKind.Guard && v.Id != train.Vehicles.Count - 1);
        Assert.Equal(3, middle.Id);
        // Its gun stands where a guard car's does, facing back down the line; no rear platform with cars behind it.
        Assert.Equal(1, middle.Gun.Facing);
        Assert.Null(train.Frames[middle.Id].Shape.Platform);
        Assert.NotNull(train.Frames[^1].Shape.Platform);
    }

    [Fact]
    public void AnArmouredCarIsHeavierAndItsPlateTakesHalfTheBlow()
    {
        var plain = Consist.Uniform(T, 6, 1);
        var armoured = Consist.Uniform(With("armouredCar"), 6, 1);
        var c = T.Composition;
        Assert.Equal(c.ArmourTonnes, armoured.Vehicles[^1].MassTonnes(T) - plain.Vehicles[^1].MassTonnes(T), 6);
        Assert.Equal(plain.MassTonnes + c.ArmourTonnes, armoured.MassTonnes, 6);
        // Heavier, slower: the same engine pulling more (spec B.5's forces are the loaded consist's).
        Assert.True(new TrainDynamics(armoured).MaxTractiveForce / armoured.MassTonnes < new TrainDynamics(plain).MaxTractiveForce / plain.MassTonnes);
        // A blow to the shell: half through the plate.
        Assert.Equal(0.1 * c.ArmourDamage, armoured.Vehicles[^1].Batter(0.1, T), 9);
        Assert.Equal(0.1, plain.Vehicles[^1].Batter(0.1, T), 9);
        Assert.Equal(1 - 0.1 * c.ArmourDamage, armoured.Vehicles[^1].Integrity, 9);
        // And never below nothing.
        Assert.Equal(1 - 0.1 * c.ArmourDamage, armoured.Vehicles[^1].Batter(5, T), 9);
        Assert.Equal(0, armoured.Vehicles[^1].Integrity);
    }

    [Fact]
    public void ACarHuggerEatsAnArmouredCarHalfAsFast()
    {
        double Eaten(TrainTuning t)
        {
            var train = Train(t);
            train.Dynamics.Velocity = 8;
            var world = new World(train, Tuning.Combat);
            world.EnableEnemies(Tuning.Enemies, null, 1, crew: 4, authority: true);
            var hugger = world.AddEnemy(id => CarHugger.Lurking(id, train.Dynamics.RearDistance + 1, 1, Tuning.Enemies.CarHugger));
            for (int i = 0; i < 10 * SimConstants.TickRate; i++)
            {
                train.Dynamics.Velocity = 8;
                world.BeginTick();
                world.Step(new TrainControls { Reverser = 1 });
            }
            Assert.True(hugger.Latched);
            return train.Vehicles[^1].Eaten;
        }
        double plain = Eaten(T), armoured = Eaten(With("armouredCar"));
        Assert.True(plain > 0);
        Assert.Equal(plain * T.Composition.ArmourDamage, armoured, 6);
    }

    /// <summary>Standing in the middle of a car's floor, inside its walls.</summary>
    static PlayerState Inside(TrainOnLine train, int car, double z = 0)
    {
        var room = train.Frames[car].Shape.Interior!.Value;
        return new PlayerState { Parent = car, Position = new Double3(-0.45, room.Min.Y + 0.1, z), Surface = Surface.Deck, Health = P.Health, LineHint = train.Cars[car].FrontDistance };
    }

    [Fact]
    public void TheCrewCarIsWarmWithTheSteamGone()
    {
        var train = Train(With("crewCar"), boiler: true);
        train.Boiler.Pressure = 0;
        train.Boiler.Firebox = 0;
        Assert.Equal(VehicleKind.Utility, train.Vehicles[1].Kind);
        // Its stove is its own heat (GDD §26 "cramped, lamp-lit"): shut in, the whole car's warm. A cargo car shut, with
        // the boiler cold, isn't.
        Assert.True(PlayerMotor.NearHeat(Inside(train, 1, z: -4), train));
        Assert.False(PlayerMotor.NearHeat(Inside(train, 2), train));
        // With a door open, only by the stove.
        train.Vehicles[1].ToggleDoor(0);
        var stove = train.Frames[1].Shape.Stove!.Value;
        Assert.True(PlayerMotor.NearHeat(Inside(train, 1, z: stove.Min.Z - 1), train));
        Assert.False(PlayerMotor.NearHeat(Inside(train, 1, z: stove.Min.Z - T.Composition.StoveReach - 1), train));
        // And it warms the frozen like the cab does (spec B.2's recovery).
        train.Vehicles[1].DoorsOpen = 0;
        var s = Inside(train, 1) with { Cold = P.Cold.OnsetSeconds };
        for (int i = 0; i < P.Cold.RecoverSecondsNearHeat * SimConstants.TickRate; i++)
            PlayerMotor.Step(ref s, default, train, P, train.Dynamics.Tuning, Dt);
        Assert.Equal(0, s.Cold);
    }

    [Fact]
    public void TheCrewCarIsTheKitsCarItsLockersItsStores()
    {
        var train = Train(With("crewCar"));
        var shape = train.Frames[1].Shape;
        Assert.Equal(1, train.KitCar);
        Assert.NotEmpty(shape.Lockers);
        Assert.NotNull(shape.Stove);
        // No freight in it, no side doors to load it by: the end doors, and the aisle between the berths and the lockers.
        Assert.DoesNotContain(shape.Solids, x => x.Part == PartKind.Cargo);
        Assert.Equal(2, shape.DoorList.Count);
        var bunk = shape.Solids.Single(x => x.Part == PartKind.Bunk).Box;
        var lockers = shape.Solids.Where(x => x.Part == PartKind.CrewLocker).Max(x => x.Box.Max.X);
        Assert.True(bunk.Min.X - lockers >= 1.2, "an aisle between them");
        // The stove stands clear of the rear doorway.
        var rear = shape.DoorList.Single(d => d.Box.Min.Z > 0).Box;
        Assert.True(shape.Stove!.Value.Max.X <= rear.Min.X + 1e-9);
    }

    [Fact]
    public void InsulationSlowsTheColdInAnUnheatedCar()
    {
        double After(TrainTuning t)
        {
            var train = Train(t, boiler: true);
            train.Boiler.Pressure = 0;
            train.Boiler.Firebox = 0;
            var s = Inside(train, 2);
            for (int i = 0; i < 20 * SimConstants.TickRate; i++)
                PlayerMotor.Step(ref s, default, train, P, train.Dynamics.Tuning, Dt);
            return s.Cold;
        }
        double plain = After(T), insulated = After(With("carInsulation"));
        Assert.Equal(20 * P.Cold.IndoorsRate, plain, 3);
        Assert.Equal(plain * 0.5, insulated, 3);
    }

    [Fact]
    public void HandrailsKeepADraggersHandFurtherOff()
    {
        bool Reaches(TrainTuning t)
        {
            var train = Train(t);
            var shape = train.Frames[3].Shape;
            var dragger = Dragger.Under(50, train, 3, 1, 0);
            // On the roof, 0.8 m in from the edge: inside the grab at a crawl (enemies.json draggers.grabRange 1.0).
            var s = new PlayerState { Parent = 3, Position = new Double3(shape.HalfWidth - 0.8, shape.RoofHeight, 0), Surface = Surface.Roof, Health = P.Health };
            return dragger.Reaches(s, train, Tuning.Enemies.Draggers);
        }
        Assert.True(Reaches(T));
        var rails = With("roofHandrails");
        Assert.True(rails.Composition.Handrails);
        Assert.False(Reaches(rails));
    }

    [Fact]
    public void TheConsistsUpgradesChangeTheNightsTuning()
    {
        var base_ = new Loadout(T, Tuning.Boiler, Tuning.Combat, Tuning.Enemies);
        // Coupling reinforcement: a coupling-up knock at the most that couples does no damage, and the Passenger takes
        // half again as long unhooking the caboose.
        var reinforced = Campaign.Campaign.Apply(C, ["couplingReinforcement"], base_);
        Assert.Equal(T.Couplings.SafeContactSpeed * 1.5, reinforced.Train.Couplings.SafeContactSpeed, 6);
        Assert.True(reinforced.Train.Couplings.SafeContactSpeed >= T.Couplings.CoupleMaxSpeed);
        Assert.Equal(Tuning.Enemies.Passenger.UncoupleSeconds * 1.5, reinforced.Enemies!.Passenger.UncoupleSeconds, 6);
        // Reinforced couplings (F.3 "uncouple under load"): cutting a working train takes no longer than a slack one.
        var couplings = Campaign.Campaign.Apply(C, ["reinforcedCouplings"], base_).Train.Couplings;
        Assert.Equal(T.Couplings.UncoupleSeconds, couplings.UncoupleUnderLoadSeconds, 6);
        // Car insulation halves the cold indoors; the rest fit the consist.
        Assert.Equal(0.5, With("carInsulation").Composition.Insulation, 6);
        Assert.Equal(1, With("crewCar").Composition.UtilityCars);
        Assert.Equal(2, With("secondGuardCar").Composition.GuardCars);
        Assert.Equal(1, With("armouredCar").Composition.ArmouredCars);
        // Every consist upgrade the spec lists now does something.
        foreach (var id in new[] { "couplingReinforcement", "roofHandrails", "carInsulation", "armouredCar", "secondGuardCar", "reinforcedCouplings", "crewCar" })
            Assert.NotEqual(base_, Campaign.Campaign.Apply(C, [id], base_));
    }

    [Fact]
    public void AReinforcedCouplingTakesAHarderKnock()
    {
        double Damage(TrainTuning t)
        {
            // The engine backing onto its cut cars at 1.4 m/s: it couples (under couplings.coupleMaxSpeed), with a knock.
            var train = Train(t, cars: 4);
            train.Uncouple(2);
            var rear = train.Rakes.Single(r => r != train.Dynamics);
            rear.Distance = train.Dynamics.RearDistance - 0.5 - t.Geometry.CouplingGap;
            rear.PreviousDistance = rear.Distance;
            train.Dynamics.Velocity = -1.4;
            for (int i = 0; i < SimConstants.TickRate && train.Rakes.Count > 1; i++)
                train.Step(Dt, new TrainControls { Reverser = -1 });
            Assert.Single(train.Rakes);
            return 2 - train.Vehicles[2].Integrity - train.Vehicles[3].Integrity;
        }
        Assert.True(Damage(T) > 0);
        Assert.Equal(0, Damage(With("couplingReinforcement")), 9);
    }
}
