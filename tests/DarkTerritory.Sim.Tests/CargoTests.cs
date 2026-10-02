using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Cargo types (T68, GDD §18-19, App. B.8): each facility loads its own cargo into a car, and what's aboard weighs the
/// director's draws, raises the Choir's floor, spreads the Drift, and with comet material aboard relaxes two gates.
/// </summary>
public class CargoTests
{
    static readonly FacilityTuning F = FacilityTests.F;
    static readonly LineDefinition Straight = new("t", [new TrackSegment(80_000)]);
    static readonly RailLine Line = new(Straight);

    [Fact]
    public void EachFacilityHasItsCargo()
    {
        Assert.Equal(CargoKind.Livestock, F.CargoOf(FacilityKind.Slaughterhouse));
        Assert.Equal(CargoKind.Food, F.CargoOf(FacilityKind.GrainElevator));
        Assert.Equal(CargoKind.Chemicals, F.CargoOf(FacilityKind.ChemicalWorks));
        Assert.Equal(CargoKind.Ammunition, F.CargoOf(FacilityKind.MilitaryDepot));
        Assert.Equal(CargoKind.Heavy, F.CargoOf(FacilityKind.Foundry));
        // The coaling tower loads no cargo; anything unlisted is goods.
        Assert.Equal(CargoKind.Goods, F.CargoOf(FacilityKind.CoalingTower));
        // A night leaves with the fortress's own freight: goods, in its loaded cars.
        var consist = Consist.Uniform(Tuning.Train, 5, 0.5);
        Assert.All(consist.Vehicles.Where(v => v.Kind == VehicleKind.Cargo), v => Assert.Equal(CargoKind.Goods, v.Cargo));
    }

    [Fact]
    public void ACrateStowedAtAFacilityIsThatFacilitysCargoAndAClientSeesIt()
    {
        var stop = new FacilityTests.Stop(ModuleKind.Crates);
        var kind = stop.World.Run!.FacilityFeature!.Facility!.Value;
        var car = stop.Train.Vehicles.First(v => v.Kind == VehicleKind.Cargo);
        var room = stop.Train.Frames[car.Id].Shape.Interior!.Value;
        var crate = stop.World.Bodies.All.First(b => b.Kind == BodyKind.Cargo);
        crate.Parent = car.Id;
        crate.Pbd.Particles[0].Position = new Double3(0, room.Min.Y + 0.5, 0);
        crate.Pbd.Particles[0].Previous = crate.Pbd.Particles[0].Position;
        stop.Step(F.Crates.SettleSeconds + 1, []);
        Assert.True(car.Load > 0);
        Assert.Equal(F.CargoOf(kind), car.Cargo);
        // The other cars, not loaded here, keep what they had.
        Assert.All(stop.Train.Vehicles.Where(v => v.Kind == VehicleKind.Cargo && v.Id != car.Id && v.Load == 0), v => Assert.Equal(CargoKind.None, v.Cargo));

        var client = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 4, 0)), stop.Train.Line, stop.Train.Dynamics.Distance);
        client.Restore(stop.Train.Capture());
        Assert.Equal(car.Cargo, client.Vehicles[car.Id].Cargo);
        var world = new World(client, Tuning.Combat);
        var controls = new TrainControls();
        WorldRecords.Apply(WorldRecords.Capture(stop.World, controls, []), world, ref controls, []);
        Assert.Equal(car.Cargo, world.Train.Vehicles[car.Id].Cargo);
    }

    static World Night(double speed, CargoKind cargo, Route.Route? route = null, int crew = 3, ulong seed = 1, EnemyTuning? enemies = null)
    {
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 5, 1)), Line, 40_000);
        foreach (var v in train.Vehicles.Where(v => v.Kind == VehicleKind.Cargo))
            v.Cargo = cargo;
        train.Dynamics.Velocity = speed;
        var world = new World(train, Tuning.Combat);
        world.EnableEnemies(enemies ?? Tuning.Enemies, route ?? Frontier(), seed, crew, authority: true);
        return world;
    }

    static Route.Route Frontier(RouteTier tier = RouteTier.Frontier) => new("t", tier, 1, Straight, [], new RouteWeather(0.01, false, 0, 0), 3600);

    static void Run(World world, double seconds, double speed)
    {
        for (int i = 0; i < seconds * SimConstants.TickRate; i++)
        {
            world.Train.Dynamics.Velocity = speed;
            world.BeginTick();
            world.Step(new TrainControls { Reverser = 1 });
        }
    }

    [Fact]
    public void WithLivestockAboardTheHoundsComeFarMoreOften()
    {
        // Hounds and the Track Doll priced alike and nothing else in reach: over a dozen nights, how often the first is the Hounds.
        var d = Tuning.Enemies.Director;
        var t = Tuning.Enemies with
        {
            Director = d with
            {
                GraceSeconds = 0,
                CooldownSeconds = [1, 1],
                SaveFor = [],
                Costs = d.Costs.ToDictionary(c => c.Key, c => c.Key is "cinderHounds" or "trackDoll" ? 1 : 1e9)
            },
        };
        int Hounds(CargoKind cargo)
        {
            int n = 0;
            for (ulong seed = 1; seed <= 16; seed++)
            {
                var world = Night(12, cargo, seed: seed, enemies: t);
                Run(world, 1.5, 12);
                n += world.Director!.Log[0].Kind == EnemyKind.CinderHound ? 1 : 0;
            }
            return n;
        }
        int plain = Hounds(CargoKind.Goods), livestock = Hounds(CargoKind.Livestock);
        // Even odds with goods; 2.5 to 1 with livestock (about 11 in 16).
        Assert.True(livestock >= 10 && livestock > plain, $"{livestock} of 16 with livestock, {plain} with goods");
    }

    [Fact]
    public void LivestockAboardTheChoirNeverQuietsBelowItsFloor()
    {
        var quiet = Night(10, CargoKind.Goods, enemies: Tuning.Enemies with { Director = Tuning.Enemies.Director with { GraceSeconds = 1e9 } });
        Run(quiet, 2, 10);
        Assert.Equal(0, quiet.Choir.Loudness, 6);
        var lowing = Night(10, CargoKind.Livestock, enemies: Tuning.Enemies with { Director = Tuning.Enemies.Director with { GraceSeconds = 1e9 } });
        // The meter's measured over a few seconds (App. C.7): it settles on the floor.
        Run(lowing, 8 * Tuning.Combat.Choir.WindowSeconds, 10);
        Assert.Equal(Tuning.Combat.Choir.LivestockFloor, lowing.Choir.Loudness, 2);
    }

    [Fact]
    public void ChemicalsAboardTheDriftSpreadsFaster()
    {
        static double Spread(CargoKind cargo)
        {
            var route = new Route.Route("t", RouteTier.Frontier, 1, Straight, [new RouteFeature(FeatureKind.Marsh, 0, 80_000)], new RouteWeather(0.01, false, 0, 0), 3600);
            var world = Night(8, cargo, route, enemies: Tuning.Enemies with { Director = Tuning.Enemies.Director with { GraceSeconds = 1e9 } });
            Run(world, 10, 8);
            return Assert.IsType<Drift>(Assert.Single(world.ActiveEnemies, e => e.Kind == EnemyKind.Drift)).Radius - Tuning.Enemies.Drift.StartRadius;
        }
        Assert.Equal(Tuning.Enemies.Drift.ChemicalSpread * Spread(CargoKind.Goods), Spread(CargoKind.Chemicals), 1);
    }

    [Fact]
    public void CometMaterialRelaxesTheTierGatesByOne()
    {
        // App. B.9: comet cargo brings the Gaunt to a Local line and the Passenger to the Frontier: their gates, a tier lower.
        var goods = Night(0, CargoKind.Goods, Frontier(RouteTier.Frontier));
        Assert.Equal(RouteTier.DeadLines, goods.Director!.Gate(goods, RouteTier.DeadLines));
        var comet = Night(0, CargoKind.Comet, Frontier(RouteTier.Frontier));
        Assert.Equal(RouteTier.Frontier, comet.Director!.Gate(comet, RouteTier.DeadLines));
        Assert.Equal(RouteTier.Local, comet.Director.Gate(comet, RouteTier.Frontier));
    }
    // ---- WP13 (GDD v1.4 §19, App. A.6, B.6, B.9; ARCHITECTURE §8 note 182) ----

    static readonly Route.Route Line7 = RouteGenerator.Generate(Tuning.Route, RouteTier.Frontier, 7);

    /// <summary>A night pulled up at the terminus with <paramref name="setUp"/> done to it first; its report.</summary>
    static RunReport Arrive(Action<World>? setUp = null)
    {
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), Line7.Build(), Line7.Length - 150, Tuning.Boiler);
        var world = new World(train, Tuning.Combat);
        world.EnableRun(Tuning.Run, Line7, 600, authority: true);
        var player = PlayerMotor.SpawnInCab(train, Tuning.Player);
        setUp?.Invoke(world);
        for (int i = 0; i < 3 * SimConstants.TickRate; i++)
        {
            train.Dynamics.Velocity = 0;
            world.BeginTick();
            world.CrewAct(ref player, default, 1);
            world.Step(new TrainControls { Reverser = 1, Brake = 1 });
            world.StepRun([player]);
        }
        return world.Run!.Report!;
    }

    static Body ChildIn(World world, int car)
    {
        var room = world.Train.Frames[car].Shape.Interior!.Value;
        return world.Bodies.SpawnCrate(world.Train, car, new Double3(room.Centre.X, room.Min.Y + 0.1, room.Centre.Z), BodyKind.Child);
    }

    [Fact]
    public void ARescuedChildBroughtHomePaysMoreThanAnyCarLoad()
    {
        var e = Tuning.Run.Economy;
        double perCar = e.PerCar["frontier"];
        var plain = Arrive();
        var withChild = Arrive(w => ChildIn(w, 2));
        Assert.Equal(RunEnd.Delivered, withChild.End);
        Assert.Equal(1, withChild.ChildrenHome);
        Assert.Equal(Math.Round(e.ChildPay * perCar), withChild.ChildPay);
        Assert.Equal(plain.Gross + withChild.ChildPay, withChild.Gross);
        // §19 "the most valuable cargo there is", B.9 "highest payout": more than a car-load of anything, comet included.
        Assert.All(Enum.GetValues<CargoKind>(), c => Assert.True(e.ChildPay > e.Rate(c), $"{c}"));
        // The clerk reads it out, flat.
        Assert.Contains(Radio.Tally(withChild), l => l.StartsWith("Child survivor: 1. Paid", StringComparison.Ordinal));
        // Left in a car cut off on the way, it isn't home.
        var lost = Arrive(w =>
        {
            ChildIn(w, 5);
            w.Train.Uncouple(4);
        });
        Assert.Equal(0, lost.ChildrenHome);
    }

    [Fact]
    public void EachCarPaysByWhatItCarries()
    {
        // Goods pay F.1's table; a car of comet material pays the comet's rate (run.json economy.cargoRates).
        var e = Tuning.Run.Economy;
        double perCar = e.PerCar["frontier"];
        var goods = Arrive();
        var comet = Arrive(w => w.Train.Vehicles[2].Cargo = CargoKind.Comet);
        Assert.Equal(goods.Gross + perCar * (e.Rate(CargoKind.Comet) - 1), comet.Gross, 6);
        Assert.Equal(1, e.Rate(CargoKind.Goods));
    }

    [Fact]
    public void TheGauntPassesOverTheChildForTheNextBestThing()
    {
        // A.6 REAL: "cannot be harmed". Led into a car with the child and a toy, the Gaunt leaves with the toy.
        var n = new Night(4, speed: 0);
        int car = 2;
        var room = n.Train.Frames[car].Shape.Interior!.Value;
        var child = ChildIn(n.World, car);
        var toy = n.World.Bodies.SpawnCrate(n.Train, car, new Double3(room.Centre.X, room.Min.Y + 0.1, room.Centre.Z + 2), BodyKind.Toy);
        n.Crew[1] = new PlayerState { Parent = car, Position = new Double3(room.Centre.X, room.Min.Y, room.Centre.Z - 1), Surface = Surface.Deck, Health = Tuning.Player.Health };
        var at = n.Train.Frames[car].ToWorld(n.Crew[1].Position);
        var gaunt = n.World.AddEnemy(id => Gaunt.Asleep(id, at, Tuning.Enemies.Gaunt));
        n.Run(3);
        Assert.True(gaunt.Gone, $"{gaunt.Phase}");
        Assert.DoesNotContain(toy, n.World.Bodies.All);
        Assert.Contains(child, n.World.Bodies.All);

        // With only the child there, it takes some of the car's freight, never the child.
        var m = new Night(4, speed: 0);
        var kid = ChildIn(m.World, car);
        m.Crew[1] = n.Crew[1] with { };
        var again = m.World.AddEnemy(id => Gaunt.Asleep(id, at, Tuning.Enemies.Gaunt));
        m.Run(3);
        Assert.True(again.Gone, $"{again.Phase}");
        Assert.Contains(kid, m.World.Bodies.All);
        Assert.True(m.Train.Vehicles[car].CargoIntegrity < 1);
        // What creatures rank loot by: the child is worth the most to the crew and nothing to them.
        Assert.Equal(0, Bodies.Prey(kid));
        Assert.True(Bodies.Value(kid) > Bodies.Value(BodyKind.Ragdoll));
    }

    [Fact]
    public void ACometContractPutsCometAboardAndWeighsEverythingUp()
    {
        // B.9: "all weights x1.4 · Passenger gate relaxed by one tier". The contract's freight is in every loaded car.
        var consist = Consist.Uniform(Tuning.Train, 5, Tuning.Run.DepartureLoad).Carrying(CargoKind.Comet);
        Assert.All(consist.Vehicles.Where(v => v.Kind == VehicleKind.Cargo), v => Assert.Equal(CargoKind.Comet, v.Cargo));
        Assert.DoesNotContain(consist.Vehicles, v => v.Kind != VehicleKind.Cargo && v.Cargo == CargoKind.Comet);
        Assert.Equal(1.4, Tuning.Enemies.Director.CargoWeights["comet"]["*"]);
        Assert.True(Tuning.Enemies.Director.CometRelaxesGates);
        var world = Night(0, CargoKind.Comet);
        Assert.Contains(CargoKind.Comet, Director.Aboard(world));
        Assert.Equal(RouteTier.Frontier, world.Director!.Gate(world, RouteTier.DeadLines));
        // Over a run of draws, the director weighs every option up by 1.4 alike: the share of each kind sent doesn't move,
        // only the total weight. Same seeds, comet or goods: the same first creature.
        var t = Tuning.Enemies with { Director = Tuning.Enemies.Director with { GraceSeconds = 0, CooldownSeconds = [1, 1], SaveFor = [] } };
        for (ulong seed = 1; seed <= 6; seed++)
        {
            var g = Night(12, CargoKind.Goods, seed: seed, enemies: t);
            var c = Night(12, CargoKind.Comet, seed: seed, enemies: t);
            Run(g, 1.5, 12);
            Run(c, 1.5, 12);
            Assert.Equal(g.Director!.Log[0].Kind, c.Director!.Log[0].Kind);
        }
    }

    static TrainOnLine Rakes(CargoKind second)
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(20_000)]));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 4, 1)), line, 5_000);
        train.Vehicles[2].Cargo = second;
        return train;
    }

    [Fact]
    public void MedicineIsSpoiledByAKnockTheRestShrugOff()
    {
        // §19 "fragile": backed onto at 1 m/s (the buckeyes couple; nothing else takes harm), the medicine does.
        var f = Tuning.Train.Fragile!;
        double Bump(CargoKind cargo)
        {
            var train = Rakes(cargo);
            train.Uncouple(0);
            for (int i = 0; i < 8 * SimConstants.TickRate; i++)
                train.Step(SimConstants.TickSeconds, new TrainControls { Throttle = 1, Reverser = 1 });
            for (int i = 0; i < 20 * SimConstants.TickRate; i++)
                train.Step(SimConstants.TickSeconds, new TrainControls { Brake = 1, Reverser = 1 });
            for (int i = 0; i < SimConstants.TickRate * 120 && train.Rakes.Count > 1; i++)
            {
                train.Dynamics.Velocity = -1.0;
                train.Step(SimConstants.TickSeconds, new TrainControls { Reverser = -1 });
            }
            Assert.Single(train.Rakes);
            Assert.Equal(1, train.Vehicles[1].CargoIntegrity);
            return train.Vehicles[2].CargoIntegrity;
        }
        Assert.Equal(1, Bump(CargoKind.Goods));
        var c = Tuning.Train.Couplings;
        double expected = (1.0 - f.SafeContactSpeed) * (1.0 - f.SafeContactSpeed) * c.DamagePerSpeedSquared * c.CargoDamageShare * f.ShockShare;
        Assert.Equal(1 - expected, Bump(CargoKind.Medicine), 2);
    }

    [Fact]
    public void HardBrakingSpoilsMedicineAndGentleBrakingDoesNot()
    {
        double Brake(CargoKind cargo, double brake)
        {
            var train = Rakes(cargo);
            train.Dynamics.Velocity = 15;
            for (int i = 0; i < 4 * SimConstants.TickRate; i++)
                train.Step(SimConstants.TickSeconds, new TrainControls { Brake = brake, Reverser = 1 });
            Assert.Equal(1, train.Vehicles[1].CargoIntegrity);
            return train.Vehicles[2].CargoIntegrity;
        }
        Assert.Equal(1, Brake(CargoKind.Goods, 1));
        Assert.True(Brake(CargoKind.Medicine, 1) < 0.99);
        Assert.Equal(1, Brake(CargoKind.Medicine, 0.3));
    }

    [Fact]
    public void CoalAndTimberFiresEscalateAndRunDownTheTrainFaster()
    {
        // B.9: "coal / timber: fire cascades escalate faster". Left alone, a fire in a car of coal or timber grows faster and
        // takes the next car sooner than one in a car of goods.
        (double Grown, double SpreadAt) Burn(CargoKind cargo)
        {
            var n = new Night(5, speed: 8);
            foreach (var v in n.Train.Vehicles.Where(v => v.Kind == VehicleKind.Cargo))
                v.Cargo = cargo;
            var fire = n.World.AddEnemy(id => CarFire.In(id, n.Train, 2, 2, Tuning.Enemies.CarFire));
            n.Run(20);
            double grown = fire.Extra;
            double t = 20;
            while (t < 400 && n.World.ActiveEnemies.Count(e => e is CarFire && !e.Gone) < 2)
            {
                n.Run(1);
                t++;
            }
            return (grown, t);
        }
        var goods = Burn(CargoKind.Goods);
        foreach (var fuel in new[] { CargoKind.Coal, CargoKind.Timber })
        {
            var b = Burn(fuel);
            Assert.True(b.Grown > goods.Grown, $"{fuel}: {b.Grown:0.000} vs {goods.Grown:0.000}");
            Assert.True(b.SpreadAt < goods.SpreadAt, $"{fuel}: spread at {b.SpreadAt} s vs {goods.SpreadAt} s");
        }
    }

    [Fact]
    public void ACannonFiredBesideAChemicalsCarGassesTheCrewNearIt()
    {
        // B.9: "firing a cannon near chemical cars is lethal to the crew". The guard van's gun, the car ahead of it chemicals:
        // the gunner and the crewmate on that car die of it; one up by the engine doesn't. A car of goods there, nobody does.
        Night Fire(CargoKind beside)
        {
            var n = new Night(6, speed: 10);
            int guard = n.Train.Dynamics.Consist.Vehicles[^1].Id;
            n.Train.Vehicles[guard - 1].Cargo = beside;
            var mount = n.Train.Frames[guard].Shape.Gun!.Value;
            var gunner = PlayerMotor.SpawnOnRoof(n.Train, guard, mount.Position.Z - 0.7, Tuning.Player);
            n.Crew[1] = gunner with { Yaw = Math.PI, Pitch = 0.3, Flags = gunner.Flags | PlayerFlags.Seated };
            n.Crew[2] = PlayerMotor.SpawnOnRoof(n.Train, guard - 1, 0, Tuning.Player);
            n.Crew[3] = PlayerMotor.SpawnOnRoof(n.Train, 1, 0, Tuning.Player);
            n.Run(0.2);
            n.Run(1, id => id == 1 ? new PlayerIntent { Buttons = PlayerButtons.Fire } : default);
            Assert.Single(n.Shots);
            return n;
        }
        var gassed = Fire(CargoKind.Chemicals);
        Assert.False(gassed.Crew[1].Alive);
        Assert.Equal(DeathCause.Poisoned, gassed.Crew[1].Death);
        Assert.False(gassed.Crew[2].Alive);
        Assert.True(gassed.Crew[3].Alive);
        Assert.Equal(1, gassed.World.Attribution.Gasser);
        var clear = Fire(CargoKind.Goods);
        Assert.All(clear.Crew.Values, c => Assert.True(c.Alive));
    }
    [Fact]
    public void AHostsFirstEverChildCallIsARealChildAndTheWorldSaysItCame()
    {
        // App. B.6 / A.6: "the first one a host player ever meets is always real". With the dice set to Soot Children every
        // time, the first call is still a child; the next is the dice's. The Game keeps ChildCalled in the host's profile.
        var t = Tuning.Enemies with { SootChildren = Tuning.Enemies.SootChildren with { RealChance = 0 } };
        var n = new Night(4, speed: 0);
        n.World.EnableEnemies(t, null, 1, crew: 4, authority: true);
        n.World.NextChildReal = true;
        var beside = n.Train.Frames[2].ToWorld(new Double3(4, 0, 0));
        n.Crew[1] = new PlayerState { Parent = PlayerState.World, Position = beside, Health = Tuning.Player.Health };
        n.Crew[2] = n.Crew[1] with { Position = beside + new Double3(0, 0, 2) };
        n.Run(SimConstants.TickSeconds);
        var rule = Spawns.For(EnemyKind.SootChildren)!;
        Assert.True(rule.Spawn(new SpawnContext(n.World, t, n.World.Director!)));
        var first = Assert.IsType<SootChildren>(n.World.ActiveEnemies.Last());
        Assert.False(first.Soot);
        Assert.True(n.World.ChildCalled);
        Assert.False(n.World.NextChildReal);
        Assert.True(rule.Spawn(new SpawnContext(n.World, t, n.World.Director!)));
        Assert.True(Assert.IsType<SootChildren>(n.World.ActiveEnemies.Last()).Soot);
    }
}
