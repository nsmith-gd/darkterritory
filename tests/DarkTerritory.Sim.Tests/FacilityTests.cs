using Ballast;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>Spec D loading modules: manual crates and the capstan winch.</summary>
public class FacilityTests
{
    static readonly TrainTuning T = Tuning.Train;
    static readonly PlayerTuning P = Tuning.Player;
    internal static readonly FacilityTuning F = DataFile.Load<FacilityTuning>(Path.Combine(DataFile.FindContentRoot(), FacilityTuning.File));

    /// <summary>A route with a facility that has the module, and that facility.</summary>
    /// <param name="power">The yard's power (level-design D.2): live unless a test is about it.</param>
    internal static (Route.Route Route, RouteFeature Facility) With(ModuleKind module, Stops.PowerState power = Stops.PowerState.Live)
    {
        foreach (var tier in new[] { RouteTier.Frontier, RouteTier.DeadLines, RouteTier.DeepTerritory, RouteTier.Local })
            for (ulong seed = 1; seed < 200; seed++)
            {
                var route = RouteGenerator.Generate(Tuning.Route, tier, seed);
                var f = route.Of(FeatureKind.Facility).FirstOrDefault(f => f.Facility is { } k && F.ModulesOf(k).Contains(module)
                    && (f.Stop?.Power ?? Stops.PowerState.Live) == power);
                if (f is not null)
                    return (route, f);
            }
        throw new InvalidOperationException($"no route has a {module}");
    }

    /// <summary>A route with a facility of this kind down a spur, and that facility (GDD §18's set pieces, note 185).</summary>
    internal static (Route.Route Route, RouteFeature Facility) With(FacilityKind kind)
    {
        var (route, i) = Bots.FacilityWork.Find(Tuning.Route, kind) ?? throw new InvalidOperationException($"no route has a {kind}");
        return (route, route.Of(FeatureKind.Facility).ElementAt(i));
    }

    internal sealed class Stop
    {
        public readonly World World;
        public readonly List<PlayerState> Crew = [];
        public readonly Site Site;
        /// <summary>Every damage event the world made, tick by tick (the harness applies them; these tests read them).</summary>
        public readonly List<Enemies.DamageEvent> Hurt = [];

        /// <summary>Stopped down the facility's spur with the engine up at the buffer stop (GDD §17), empties behind it.</summary>
        public Stop(ModuleKind module, Stops.PowerState power = Stops.PowerState.Live) : this(With(module, power))
        {
        }

        /// <summary>Stopped down a facility of this kind's spur, as above.</summary>
        public Stop(FacilityKind kind) : this(With(kind))
        {
        }

        Stop((Route.Route Route, RouteFeature Facility) at)
        {
            var (route, f) = at;
            var line = route.Build();
            // The facility's own track: its yard's first (level-design P6), where its loading modules stand.
            var spur = line.Branches.First(b => b.Kind == BranchKind.Spur && f.Contains(b.Toe));
            var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 4, 0)), line, spur.End - 0.5, Tuning.Boiler);
            var state = train.Capture();
            train.Restore(state with { Rakes = [state.Rakes[0] with { Path = spur.Index }] });
            World = new World(train, Tuning.Combat);
            World.EnableBodies();
            World.EnableRun(Tuning.Run, route, 600, authority: true, F);
            Step(0.2, []);
            Assert.Equal(RunPhase.AtFacility, World.Run!.Phase);
            Site = World.Run.CurrentSite!;
        }

        public TrainOnLine Train => World.Train;

        /// <summary>Puts the engine's rake down the spur with its front at a path distance, standing.</summary>
        public void StandAt(double front)
        {
            var state = Train.Capture();
            Train.Restore(state with { Rakes = [state.Rakes[0] with { Distance = front, Velocity = 0 }] });
            Train.RefreshFrames();
        }

        /// <summary>Where the engine's front stands for a cargo car's middle to be at a distance along the facility's track.</summary>
        public double FrontFor(int car, double along)
        {
            var consist = Train.Dynamics.Consist;
            int i = consist.IndexOf(car);
            return Train.Line.Branches[Site.Spur].Toe + along + consist.OffsetOf(i) + consist.Vehicles[i].Length(T) / 2;
        }

        public void Step(double seconds, PlayerIntent[] intents)
        {
            for (int t = 0; t < seconds * SimConstants.TickRate; t++)
            {
                Train.Dynamics.Velocity = 0;
                World.BeginTick();
                for (int i = 0; i < Crew.Count; i++)
                {
                    var s = Crew[i];
                    World.CrewAct(ref s, intents[i], i + 1);
                    Crew[i] = s;
                }
                World.Step(new TrainControls { Reverser = 1, Brake = 1 });
                Hurt.AddRange(World.Damage);
                for (int i = 0; i < Crew.Count; i++)
                {
                    var s = Crew[i];
                    PlayerMotor.Step(ref s, intents[i], Train, P, T, SimConstants.TickSeconds, applyLook: false);
                    Crew[i] = s;
                }
                World.StepBodies([.. Crew.Select((c, i) => (i + 1, c))]);
                World.StepRun(Crew);
            }
        }

        /// <summary>Stands someone on the ground at a point, facing along the line.</summary>
        public PlayerState OnTheGround(Double3 at) => PlayerMotor.SpawnOnGround(at, Train.Line, Site.Feature.Start, P);
    }

    [Fact]
    public void EveryFacilityHasTheModulesItsKindOffers()
    {
        var route = RouteGenerator.Generate(Tuning.Route, RouteTier.Frontier, 7);
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 6, 1)), route.Build(), 900);
        var world = new World(train);
        world.EnableRun(Tuning.Run, route, 600, authority: true, F);
        var facilities = route.Of(FeatureKind.Facility).ToList();
        Assert.Equal(facilities.Count, world.Run!.Sites.Count);
        for (int i = 0; i < facilities.Count; i++)
        {
            var offered = F.ModulesOf(facilities[i].Facility!.Value);
            if (offered.Count == 0)
                Assert.Null(world.Run.Sites[i]);
            else
                Assert.Equal(offered, world.Run.Sites[i]!.Modules);
        }
        Assert.Empty(F.ModulesOf(FacilityKind.CoalingTower));
    }

    [Fact]
    public void CratesComeOutWhenTheTrainStopsAndACarTakesThemAsLoad()
    {
        var stop = new Stop(ModuleKind.Crates);
        var cargo = stop.World.Bodies.All.Where(b => b.Kind == BodyKind.Cargo).ToList();
        Assert.Equal(stop.Site.CrateCount, cargo.Count);
        Assert.InRange(cargo.Count, F.Crates.Count[0], F.Crates.Count[1]);
        Assert.True(stop.Site.Stocked);
        // Every crate stands on the ground by the track, not in the air.
        stop.Step(2, []);
        Assert.All(cargo, b => Assert.Equal(PlayerState.World, b.Parent));

        // Put one down inside the first cargo car: once it lies still there, it's loaded, and gone.
        var car = stop.Train.Vehicles.First(v => v.Kind == VehicleKind.Cargo);
        var room = stop.Train.Frames[car.Id].Shape.Interior!.Value;
        var crate = cargo[0];
        crate.Parent = car.Id;
        crate.Pbd.Particles[0].Position = new Double3(0, room.Min.Y + 0.5, 0);
        crate.Pbd.Particles[0].Previous = crate.Pbd.Particles[0].Position;
        stop.Step(F.Crates.SettleSeconds + 1, []);
        Assert.Equal(F.Crates.LoadPerCrate, car.Load, 6);
        Assert.DoesNotContain(crate, stop.World.Bodies.All);
    }

    [Fact]
    public void CarryingFreightIsSlowAndKeepsYouOffLadders()
    {
        var stop = new Stop(ModuleKind.Crates);
        stop.Step(2, []);
        var crate = stop.World.Bodies.All.First(b => b.Kind == BodyKind.Cargo);
        var at = crate.Pbd.Particles[0].Position;
        var s = stop.OnTheGround(at + new Double3(0, 0, 0.7));
        s.Yaw = 0; // facing −Z, towards the crate
        stop.Crew.Add(s);
        stop.Step(0.1, [default]);
        stop.Step(0.1, [new PlayerIntent { Buttons = PlayerButtons.Use }]);
        Assert.Equal(1, crate.Carrier);
        stop.Step(0.1, [default]);
        Assert.True(stop.Crew[0].Has(PlayerFlags.Heavy));
        var start = stop.Crew[0].Position;
        stop.Step(1, [new PlayerIntent { MoveZ = 1, Buttons = PlayerButtons.Run }]);
        double speed = (stop.Crew[0].Position - start).Length;
        Assert.InRange(speed, P.CarryHeavy * 0.8, P.CarryHeavy * 1.05);
        // Put it down and you're light again.
        stop.Step(0.1, [new PlayerIntent { Buttons = PlayerButtons.Use }]);
        stop.Step(0.1, [default]);
        Assert.False(stop.Crew[0].Has(PlayerFlags.Heavy));
    }

    [Fact]
    public void TheWinchNeedsTwoOnTheCapstan()
    {
        // With the engine at the buffer stop, the first cargo car stands by the winch, where the sled comes to rest.
        var stop = new Stop(ModuleKind.Winch);
        var site = stop.Site;
        var crank = new PlayerIntent { Buttons = PlayerButtons.Use };
        foreach (var handle in site.Handles)
            stop.Crew.Add(stop.OnTheGround(handle - Double3.Up * 0.9));

        // One alone: it doesn't move.
        stop.Step(5, [crank, default]);
        Assert.Equal(0, site.Progress);
        Assert.False(site.Turning);

        // Both: it hauls at the winch's speed.
        stop.Step(10, [crank, crank]);
        Assert.True(site.Turning);
        Assert.Equal(10 * F.Winch.Speed / F.Winch.HaulMetres, site.Progress, 2);

        // Haul it all the way and a car by the track takes the load: the sled rests between the first two behind the engine.
        var byTheSled = stop.Train.Vehicles.Where(v => v.Kind == VehicleKind.Cargo).Take(2).ToList();
        stop.Step(F.Winch.HaulMetres / F.Winch.Speed, [crank, crank]);
        Assert.Equal(F.Winch.LoadPerSled, byTheSled.Sum(v => v.Load), 6);
        Assert.Equal(F.Winch.Sleds - 1, site.SledsLeft);
    }

    [Fact]
    public void WithTheCarsByTheWinchFullTheSledsLoadHandsOnDownTheTrain()
    {
        // A second winch stop: the cars nearest the winch were filled at the first, and a train can't reorder its cars.
        var stop = new Stop(ModuleKind.Winch);
        var cargo = stop.Train.Vehicles.Where(v => v.Kind == VehicleKind.Cargo).ToList();
        cargo[0].Load = cargo[1].Load = 1;
        var crank = new PlayerIntent { Buttons = PlayerButtons.Use };
        foreach (var handle in stop.Site.Handles)
            stop.Crew.Add(stop.OnTheGround(handle - Double3.Up * 0.9));
        stop.Step(F.Winch.HaulMetres / F.Winch.Speed + 1, [crank, crank]);
        Assert.Equal(F.Winch.Sleds - 1, stop.Site.SledsLeft);
        Assert.Equal(F.Winch.LoadPerSled, cargo[2].Load, 6);
    }

    [Fact]
    public void AClientSeesTheSite()
    {
        var stop = new Stop(ModuleKind.Winch);
        var client = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 6, 0)), stop.Train.Line, 1000, Tuning.Boiler));
        client.EnableRun(Tuning.Run, stop.World.Run!.Route, 600, authority: false, F);
        var controls = new TrainControls();
        var records = WorldRecords.Capture(stop.World, controls, []);
        WorldRecords.Apply(records, client, ref controls, []);
        var mirrored = client.Run!.Sites[stop.Site.Index]!;
        Assert.Equal(stop.Site.SledsLeft, mirrored.SledsLeft);
        Assert.Equal(stop.Site.Stocked, mirrored.Stocked);
        Assert.Equal(stop.Site.Capstan, mirrored.Capstan);
    }

    // GDD §18's set pieces (WP15, ARCHITECTURE §8 note 185).

    [Fact]
    public void TheGrainElevatorsSpoutLoadsOnlyTheCarUnderItWhileItsLeverIsHeld()
    {
        var stop = new Stop(FacilityKind.GrainElevator);
        var site = stop.Site;
        Assert.True(site.Has(ModuleKind.Spout));
        Assert.Equal(F.Spout.Bin, site.Bin, 6);
        var cars = stop.Train.Dynamics.Consist.Vehicles.Where(v => v.Kind == VehicleKind.Cargo).ToList();
        // The first car under the spout, someone at its lever on the ground.
        stop.StandAt(stop.FrontFor(cars[0].Id, site.SpoutAlong));
        Assert.Same(cars[0], stop.World.Run!.CarUnderSpout(stop.Train, site));
        var lever = stop.OnTheGround(site.SpoutLever - Double3.Up * 0.9);
        stop.Crew.Add(lever);
        var hold = new PlayerIntent { Buttons = PlayerButtons.Use };
        // Standing there does nothing; holding it, grain comes down into that car and no other, and it's loud.
        stop.Step(2, [default]);
        Assert.Equal(0, cars[0].Load);
        Assert.False(site.Pouring);
        stop.Step(4, [hold]);
        Assert.True(site.Pouring);
        Assert.True(stop.World.Run!.Machinery);
        Assert.Equal(4 * F.Spout.PourPerSecond, cars[0].Load, 2);
        Assert.Equal(CargoKind.Food, cars[0].Cargo);
        Assert.All(cars.Skip(1), c => Assert.Equal(0, c.Load));
        // One car at a time: walked on a car's length, the next one's under it and the first takes no more.
        stop.StandAt(stop.FrontFor(cars[1].Id, site.SpoutAlong));
        double first = cars[0].Load;
        stop.Step(4, [hold]);
        Assert.Equal(first, cars[0].Load, 6);
        Assert.Equal(4 * F.Spout.PourPerSecond, cars[1].Load, 2);
        // Between cars, it goes on the ballast: the bin empties and nothing's loaded.
        stop.StandAt(stop.FrontFor(cars[1].Id, site.SpoutAlong) + 7);
        Assert.Null(stop.World.Run!.CarUnderSpout(stop.Train, site));
        double bin = site.Bin, loaded = cars.Sum(c => c.Load);
        stop.Step(2, [hold]);
        Assert.Equal(bin - 2 * F.Spout.PourPerSecond, site.Bin, 2);
        Assert.Equal(loaded, cars.Sum(c => c.Load), 6);
        // A full car under it overflows, and it strains the car (spec D.2: "overfill damages car and spills").
        cars[2].Load = 1;
        stop.StandAt(stop.FrontFor(cars[2].Id, site.SpoutAlong));
        stop.Step(4, [hold]);
        Assert.Equal(1, cars[2].Load, 6);
        Assert.Equal(1 - 4 * F.Spout.PourPerSecond * F.Spout.OverfillDamagePerLoad, cars[2].Integrity, 2);
    }

    [Fact]
    public void TheSpoutReachesTheCarsThatGoDownTheSpurShortOfTheBufferStop()
    {
        // "Endless repositioning": every car down the spur comes under the spout with the engine short of the buffer stop.
        var stop = new Stop(FacilityKind.GrainElevator);
        var spur = stop.Train.Line.Branches[stop.Site.Spur];
        foreach (var car in stop.Train.Dynamics.Consist.Vehicles.Where(v => v.Kind == VehicleKind.Cargo))
            Assert.InRange(stop.FrontFor(car.Id, stop.Site.SpoutAlong), spur.Toe, spur.End - 1);
    }

    [Fact]
    public void TheHerdNeedsTwoInThePenAndStirsTheChoirFloor()
    {
        var stop = new Stop(FacilityKind.Slaughterhouse);
        var site = stop.Site;
        var run = stop.World.Run!;
        Assert.True(site.Has(ModuleKind.Ramp));
        Assert.InRange(site.Head, F.Ramp.Head[0], F.Ramp.Head[1]);
        var car = run.CarAtRamp(stop.Train, site);
        Assert.NotNull(car);
        var drive = new PlayerIntent { Buttons = PlayerButtons.Use };
        stop.Crew.Add(stop.OnTheGround(site.Pen + new Double3(1, 0, 0)));
        stop.Crew.Add(stop.OnTheGround(site.Pen - new Double3(1, 0, 0)));
        // One alone gets nowhere with them.
        stop.Step(10, [drive, default]);
        Assert.Equal(site.HeadStart, site.Head);
        Assert.False(site.Herding);
        Assert.Equal(0, stop.World.Choir.Floor);
        // Two: one up the ramp every few seconds, into the car at its top, and the herd's loud the whole time.
        stop.Step(F.Ramp.SecondsPerHead + 0.5, [drive, drive]);
        Assert.Equal(site.HeadStart - 1, site.Head);
        Assert.Equal(F.Ramp.LoadPerHead, car!.Load, 6);
        Assert.Equal(CargoKind.Livestock, car.Cargo);
        Assert.True(run.Machinery);
        Assert.Equal(F.Ramp.ChoirFloor, stop.World.Choir.Floor, 6);
        // Left off, they stay stirred up (some aboard, some not): the floor stays up.
        stop.Step(2, [default, default]);
        Assert.True(site.Stirred);
        Assert.Equal(F.Ramp.ChoirFloor, stop.World.Choir.Floor, 6);
        stop.Step(F.Ramp.SecondsPerHead * site.Head + 1, [drive, drive]);
        Assert.Equal(0, site.Head);
        Assert.False(site.Stirred);
    }

    [Fact]
    public void SomethingAlreadyLivesAtTheSlaughterhouse()
    {
        // GDD §18 "something already lives here": the director's residents weigh the Gaunt up there, and only there.
        var residents = Tuning.Enemies.Director.Residents;
        Assert.True(residents["slaughterhouse"]["gaunt"] > 1);
        Assert.False(residents.ContainsKey("foundry"));
    }

    [Fact]
    public void TheHoseFillsTheCarAndLeaksWithNobodyMindingIt()
    {
        var stop = new Stop(FacilityKind.ChemicalWorks);
        var site = stop.Site;
        var run = stop.World.Run!;
        var car = run.CarAtHose(stop.Train, site);
        Assert.NotNull(car);
        var hold = new PlayerIntent { Buttons = PlayerButtons.Use };
        stop.Crew.Add(stop.OnTheGround(site.HoseStand + new Double3(0.5, 0, 0)));
        // Held long enough, the hose goes on the car by the stand.
        stop.Step(F.Hose.ConnectSeconds + 0.2, [hold]);
        Assert.Equal(car!.Id, site.HoseCar);
        Assert.Equal(1, run.HoseHand);
        // Minded, it fills the car and the pressure stays down.
        stop.Step(10, [default]);
        Assert.Equal(10 * F.Hose.FlowPerSecond, car.Load, 1);
        Assert.Equal(CargoKind.Chemicals, car.Cargo);
        Assert.Equal(0, site.Pressure, 6);
        Assert.False(site.Leaking);
        // Walk off (still within the leak's reach) and the pressure climbs until it leaks: gassing, and the load spoils.
        var off = stop.Crew[0] with { Position = stop.Crew[0].Position + new Double3(0, 0, 4) };
        stop.Crew[0] = off;
        stop.Step(1 / F.Hose.PressureRise + 2, [default]);
        Assert.True(site.Leaking);
        Assert.True(car.CargoIntegrity < 1);
        Assert.Contains(stop.Hurt, d => d.Cause == DeathCause.Leak && d.PlayerId == 1);
        // Back at the stand, held: off it comes, cleanly, and the leak stops as the pressure falls.
        stop.Crew[0] = stop.OnTheGround(site.HoseStand + new Double3(0.5, 0, 0));
        stop.Step(F.Hose.ConnectSeconds + 0.2, [hold]);
        Assert.Equal(-1, site.HoseCar);
        stop.Step(1, [default]);
        Assert.False(site.Leaking);
    }

    [Fact]
    public void MovingOffWithTheHoseOnTearsItAndSpoilsTheLoad()
    {
        var stop = new Stop(FacilityKind.ChemicalWorks);
        var site = stop.Site;
        var car = stop.World.Run!.CarAtHose(stop.Train, site)!;
        stop.Crew.Add(stop.OnTheGround(site.HoseStand + new Double3(0.5, 0, 0)));
        stop.Step(F.Hose.ConnectSeconds + 0.2, [new PlayerIntent { Buttons = PlayerButtons.Use }]);
        stop.Step(5, [default]);
        Assert.Equal(car.Id, site.HoseCar);
        stop.StandAt(stop.Train.Dynamics.Distance - (F.Hose.CarReach + F.Hose.TearSlack + 10));
        stop.Step(0.1, [default]);
        Assert.Equal(-1, site.HoseCar);
        Assert.True(site.Leaking);
        Assert.Equal(1 - F.Hose.TornSpoil, car.CargoIntegrity, 6);
        Assert.InRange(site.Leak, F.Hose.TornSeconds - 1, F.Hose.TornSeconds);
    }

    [Fact]
    public void ACannonFiredIndoorsAtTheChemicalWorksGassesTheGunner()
    {
        // GDD §18 "do not fire indoors": with no chemicals aboard at all, a gun fired from under the works' pipe rack gasses
        // whoever fired it.
        var stop = new Stop(FacilityKind.ChemicalWorks);
        var train = stop.Train;
        // The guard van's gun, down the spur with the rest.
        int gun = train.Vehicles.Last(v => train.Frames[v.Id].Shape.Gun is not null).Id;
        Assert.True(stop.World.Run!.Indoors(train.Frames[gun].Origin));
        var mount = train.Frames[gun].Shape.Gun!.Value;
        var gunner = PlayerMotor.SpawnOnRoof(train, gun, mount.Position.Z - 0.7, P);
        stop.Crew.Add(gunner with { Yaw = Math.PI, Pitch = 0.3, Flags = gunner.Flags | PlayerFlags.Seated });
        stop.Step(0.2, [default]);
        stop.Step(1, [new PlayerIntent { Buttons = PlayerButtons.Fire }]);
        Assert.Contains(stop.Hurt, d => d.Cause == DeathCause.Poisoned && d.PlayerId == 1);
        Assert.Equal(1, stop.World.Attribution.Gasser);
    }

    [Fact]
    public void ThePowderGoesUpWhenItsDroppedHardAndTakesTheKegsBesideIt()
    {
        var stop = new Stop(FacilityKind.MilitaryDepot);
        stop.Step(2, []);
        var kegs = stop.World.Bodies.All.Where(b => b.Kind == BodyKind.Cargo && b.Cargo == CargoKind.Ammunition).ToList();
        Assert.True(kegs.Count >= 2);
        // Set down by hand (a fall from a hand's height): nothing.
        var keg = kegs[0];
        ref var p = ref keg.Pbd.Particles[0];
        p.Position += Double3.Up * 1.2;
        p.Previous = p.Position;
        stop.Step(2, []);
        Assert.Contains(keg, stop.World.Bodies.All);
        // Off a roof: it goes up, and the keg beside it the tick after.
        var beside = kegs.First(k => k != keg && (k.Centre - keg.Centre).Length <= F.Kegs.Chain);
        stop.Crew.Add(stop.OnTheGround(keg.Centre + new Double3(1.5, 0, 0)));
        ref var q = ref keg.Pbd.Particles[0];
        q.Position += Double3.Up * 4;
        q.Previous = q.Position;
        keg.Pbd.Wake();
        stop.Step(2, [default]);
        Assert.DoesNotContain(keg, stop.World.Bodies.All);
        Assert.DoesNotContain(beside, stop.World.Bodies.All);
        Assert.Contains(stop.World.Impacts, i => i.Shooter == -1);
        Assert.Contains(stop.Hurt, d => d.Cause == DeathCause.Keg && d.PlayerId == 1 && d.Amount >= F.Kegs.Damage);
    }

    [Fact]
    public void AClientSeesTheSetPieces()
    {
        var stop = new Stop(FacilityKind.Slaughterhouse);
        var site = stop.Site;
        stop.Crew.Add(stop.OnTheGround(site.Pen + new Double3(1, 0, 0)));
        stop.Crew.Add(stop.OnTheGround(site.Pen - new Double3(1, 0, 0)));
        var drive = new PlayerIntent { Buttons = PlayerButtons.Use };
        stop.Step(F.Ramp.SecondsPerHead * 1.5, [drive, drive]);
        var client = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 6, 0)), stop.Train.Line, 1000, Tuning.Boiler));
        client.EnableRun(Tuning.Run, stop.World.Run!.Route, 600, authority: false, F);
        var controls = new TrainControls();
        WorldRecords.Apply(WorldRecords.Capture(stop.World, controls, []), client, ref controls, []);
        var mirrored = client.Run!.Sites[site.Index]!;
        Assert.Equal(site.Head, mirrored.Head);
        Assert.True(mirrored.Herding);
        Assert.Equal(site.Herd, mirrored.Herd, 3);
        Assert.Equal(site.Pen, mirrored.Pen);
    }

    // GDD §18's switchyard and wreck yard (WP15b, ARCHITECTURE §8 note 187).

    /// <summary>A world on a route with a switchyard, the train standing short of it on the main line.</summary>
    static (World World, Site Site, int Facility) Switchyard(int cars = 6)
    {
        var (route, f) = With(FacilityKind.Switchyard);
        var line = route.Build();
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, cars, 0.5)), line, f.Start - 50, Tuning.Boiler);
        var world = new World(train, Tuning.Combat);
        world.EnableBodies();
        world.EnableRun(Tuning.Run, route, 600, authority: true, F);
        int i = route.Of(FeatureKind.Facility).ToList().IndexOf(f);
        return (world, world.Run!.Sites[i]!, i);
    }

    [Fact]
    public void TheSwitchyardsCarsStandOnItsSidingsAsTheNightBegins()
    {
        var (world, site, i) = Switchyard();
        var train = world.Train;
        var run = world.Run!;
        var tracks = run.YardTracks(i);
        Assert.Equal(site.Spur, tracks[0]);
        var standing = train.Rakes.Where(train.Standing).ToList();
        Assert.NotEmpty(standing);
        // On the yard's other tracks (its own, if that's all it has), at the buffer stop, handbrakes on, part-loaded with mixed cargo.
        var on = tracks.Count > 1 ? tracks.Skip(1).ToHashSet() : [site.Spur];
        foreach (var r in standing)
        {
            Assert.Contains(r.Path, on);
            Assert.True(r.Handbrake);
            Assert.Equal(train.Line.Branches[r.Path].End - F.Rakes.Back, r.Distance, 6);
            Assert.All(r.Consist.Vehicles, v => Assert.True(v.YardCar && v.Id >= train.OwnVehicles && v.Load >= F.Rakes.Load[0] - 1e-9
                && v.Cargo != CargoKind.None && !v.LampLit));
            Assert.InRange(r.Consist.Vehicles.Count, F.Rakes.Cars[0], F.Rakes.Cars[^1]);
        }
        // Never more than the shortest of those sidings takes with the engine and a car of the train's own.
        int most = on.Min(b => SpurDrill.Capacity(T.Geometry, train.Line.Branches[b], 12)) - F.Rakes.Spare;
        Assert.True(standing.Sum(r => r.Consist.Vehicles.Count) <= most);
        Assert.Equal(1, train.TrainRakes);
        // A client stands the same cars, with the same ids, from the route alone.
        var (client, _, _) = Switchyard();
        Assert.Equal(train.Vehicles.Count, client.Train.Vehicles.Count);
        Assert.Equal(train.Capture().Rakes.Select(r => (string.Join(",", r.Vehicles), r.Path, r.Distance)),
            client.Train.Capture().Rakes.Select(r => (string.Join(",", r.Vehicles), r.Path, r.Distance)));
        // Not the crew's to lose: nobody counts them lost at the night's end.
        Assert.Equal(0, run.Tally(world, [PlayerMotor.SpawnInCab(train, P)]).CarsLost);
    }

    [Fact]
    public void CoupledUpToTheStandingCarsComeAwayAheadOfTheEngineAndAreTheTrains()
    {
        var (world, _, _) = Switchyard();
        var train = world.Train;
        var standing = train.Rakes.First(train.Standing);
        var yard = standing.Consist.Vehicles.Select(v => v.Id).ToList();
        // The engine's rake run up the siding at a crawl onto them.
        var state = train.Capture();
        int e = Array.FindIndex(state.Rakes, r => r.Vehicles.Contains(0));
        state.Rakes[e] = state.Rakes[e] with { Path = standing.Path, Distance = standing.RearDistance - T.Geometry.CouplingGap - 1, Velocity = 0.8 };
        train.Restore(state);
        for (int t = 0; t < 5 * SimConstants.TickRate && train.Rakes.Contains(standing); t++)
            train.Step(SimConstants.TickSeconds, new TrainControls { Reverser = 1 });
        Assert.DoesNotContain(standing, train.Rakes);
        Assert.Equal(1, train.TrainRakes);
        Assert.Equal(yard, train.Dynamics.Consist.Vehicles.Take(yard.Count).Select(v => v.Id));
        Assert.True(train.Dynamics.Consist.Vehicles[yard.Count].IsEngine);
        // Brought home, they pay: delivered cars counted, their cargo at its rate.
        var report = world.Run!.Tally(world, [PlayerMotor.SpawnInCab(train, P)]);
        Assert.Equal(train.Dynamics.Consist.Vehicles.Count(v => v.Kind == VehicleKind.Cargo), report.CarsDelivered);
        Assert.Equal(0, report.CarsLost);
    }

    [Fact]
    public void TheWreckIsDarkUntilALampIsOnItAndItsSalvageComesOut()
    {
        var stop = new Stop(FacilityKind.WreckYard);
        var site = stop.Site;
        var run = stop.World.Run!;
        Assert.InRange(site.Heaps.Count, F.Wreck.Heaps[0], F.Wreck.Heaps[^1]);
        // The engine at the buffer stop, headlamp on: the heap beyond the stop is in its beam, the one out wide beside the
        // cars isn't.
        stop.Step(0.2, []);
        Assert.True(site.Heaps[0].Found);
        var dark = site.Heaps.Last();
        Assert.False(dark.Found);
        Assert.True(dark.Salvage > 0);
        Assert.Equal(site.Heaps.Where(h => h.Found).Sum(h => h.SalvageStart), stop.World.Bodies.All.Count(b => b.Kind == BodyKind.Cargo && b.Cargo == CargoKind.Salvage));
        // Lamps down, still dark there; a hand lamp set down by it, and its salvage is found.
        stop.World.LampLit = false;
        stop.Step(0.5, []);
        Assert.False(dark.Found);
        stop.World.Bodies.SpawnItem(dark.Centre + new Double3(F.Wreck.LampReach - 1, 0.3, 0), site.MainDistance, BodyKind.Lamp);
        stop.Step(0.5, []);
        Assert.True(dark.Found);
        Assert.Equal(0, dark.Salvage);
    }

    [Fact]
    public void PullingSalvageOutUnsettlesAHeapUntilItGroansAndShiftsOnWhoeversBy()
    {
        var stop = new Stop(FacilityKind.WreckYard);
        var site = stop.Site;
        var run = stop.World.Run!;
        stop.Step(0.2, []);
        var heap = site.Heaps[0];
        var pieces = stop.World.Bodies.All.Where(b => b.Kind == BodyKind.Cargo && b.Cargo == CargoKind.Salvage).ToList();
        int pulls = (int)Math.Ceiling(1 / F.Wreck.StrainPerPiece - 1e-9);
        Assert.True(pieces.Count >= Math.Min(pulls, heap.SalvageStart));
        // Someone standing right by it the whole time; each piece picked up is pulled out from under the rest.
        stop.Crew.Add(stop.OnTheGround(heap.Centre + new Double3(0.5, 0, 0.5)));
        for (int k = 0; k < pulls && k < pieces.Count; k++)
        {
            pieces[k].Carrier = 1;
            stop.Step(0.1, [default]);
            pieces[k].Carrier = -1;
        }
        if (heap.Stability > 1e-9)
            return; // a heap with fewer pieces than it takes never goes (nothing more to pull)
        // The tell: it groans first, and nobody's hurt yet.
        Assert.True(heap.Groan > 0);
        Assert.DoesNotContain(stop.Hurt, d => d.Cause == DeathCause.Wreckage);
        Assert.NotNull(run.Groaning(heap.Centre));
        stop.Step(F.Wreck.WarnSeconds + 0.2, [default]);
        Assert.Equal(1, heap.Shifts);
        Assert.Equal(F.Wreck.Settle, heap.Stability, 6);
        Assert.Contains(stop.Hurt, d => d.Cause == DeathCause.Wreckage && d.PlayerId == 1 && d.Amount == F.Wreck.Damage);
        Assert.Equal(1, run.WreckBy);
        Assert.Single(stop.Hurt, d => d.Cause == DeathCause.Wreckage);
    }

    [Fact]
    public void AClientSeesTheWreckAndSomethingAlreadyLivesThere()
    {
        var stop = new Stop(FacilityKind.WreckYard);
        stop.Step(0.2, []);
        var client = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 4, 0)), stop.Train.Line, 1000, Tuning.Boiler));
        client.EnableRun(Tuning.Run, stop.World.Run!.Route, 600, authority: false, F);
        var controls = new TrainControls();
        WorldRecords.Apply(WorldRecords.Capture(stop.World, controls, []), client, ref controls, []);
        var mirrored = client.Run!.Sites[stop.Site.Index]!;
        Assert.Equal(stop.Site.Heaps.Select(h => h.State), mirrored.Heaps.Select(h => h.State));
        Assert.Equal(stop.Site.Heaps.Select(h => h.Centre), mirrored.Heaps.Select(h => h.Centre));
        // GDD §18 "already occupied".
        Assert.True(Tuning.Enemies.Director.Residents["wreckYard"]["draggers"] > 1);
    }
}
