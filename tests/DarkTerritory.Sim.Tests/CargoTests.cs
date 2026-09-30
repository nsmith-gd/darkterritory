using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Physics;
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
        // Hounds and Clingers priced alike and nothing else in reach: over a dozen nights, how often the first is the Hounds.
        var d = Tuning.Enemies.Director;
        var t = Tuning.Enemies with
        {
            Director = d with { GraceSeconds = 0, CooldownSeconds = [1, 1], SaveFor = [],
                Costs = d.Costs.ToDictionary(c => c.Key, c => c.Key is "cinderHounds" or "clingers" ? 1 : 1e9) },
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
        Assert.Equal(0, quiet.Choir.Aggro, 6);
        var lowing = Night(10, CargoKind.Livestock, enemies: Tuning.Enemies with { Director = Tuning.Enemies.Director with { GraceSeconds = 1e9 } });
        Run(lowing, 2, 10);
        Assert.Equal(Tuning.Combat.Choir.LivestockFloor, lowing.Choir.Aggro, 6);
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
    public void CometMaterialBringsTheGauntToALocalLine()
    {
        var d = Tuning.Enemies.Director;
        var only = Tuning.Enemies with
        {
            Director = d with { GraceSeconds = 0, CooldownSeconds = [1, 1], SaveFor = [], Costs = d.Costs.ToDictionary(c => c.Key, c => c.Key == "gaunt" ? 0.5 : 1e9) },
        };
        var goods = Night(0, CargoKind.Goods, Frontier(RouteTier.Local), enemies: only);
        Run(goods, 5, 0);
        Assert.DoesNotContain(goods.Director!.Log, l => l.Kind == EnemyKind.Gaunt);
        var comet = Night(0, CargoKind.Comet, Frontier(RouteTier.Local), enemies: only);
        Run(comet, 5, 0);
        Assert.Contains(comet.Director!.Log, l => l.Kind == EnemyKind.Gaunt);
    }
}
