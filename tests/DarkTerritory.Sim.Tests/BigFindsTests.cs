using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Stops;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The big finds (queue #453; ARCHITECTURE §8 note 719; the director, 14 Oct 2026: "Very big value things should take two to
/// carry. That is a good idea"; App. F, 9 Oct: loot that stays put, so "poor stacking becomes an obstacle ... lethal company
/// style"). A two-person jackpot can't be lifted by one and goes where two take it, and pays as a find; a fragile find knocked
/// hard is worth less and the clerk names its damage; a stop's jackpot house is locked, the wrench opens it, and it keeps more
/// than an ordinary house; loose things in a car slide on a bend, and one by an open side door goes out of it.
/// </summary>
public class BigFindsTests
{
    static readonly LootTuning L = DataFile.Load<LootTuning>(Path.Combine(DataFile.FindContentRoot(), LootTuning.File));
    static readonly PlayerTuning P = Tuning.Player;
    static readonly double Dt = SimConstants.TickSeconds;

    static double PerCar(RouteTier tier) => Tuning.Run.Economy.PerCar[StopLoot.TierKey(tier)];

    /// <summary>The loot with loose things riding in the cars (note 719's part 4), whatever loot.json's flag says.</summary>
    static LootTuning Riding => L with { Ride = L.Ride! with { Enabled = true } };

    /// <summary>The loot with every frontier stop given a jackpot house and a jackpot in it, its extras all of one kind.</summary>
    static LootTuning Sure(string extra = "mantelClock") => L with
    {
        JackpotHouse = L.JackpotHouse! with { Chance = new() { ["frontier"] = 1 }, Items = [extra] },
        Jackpots = L.Jackpots! with { House = 1, Chance = new() { ["frontier"] = 1 } },
    };

    /// <summary>A frontier night standing at its first stop with a jackpot house, the loot <paramref name="loot"/>, and that stop.</summary>
    static (World World, int Stop) AtAJackpotHouse(LootTuning loot)
    {
        for (ulong seed = 1; seed < 100; seed++)
        {
            var route = RouteGenerator.Generate(Tuning.Route, RouteTier.Frontier, seed);
            var world = Night(route, 0, loot);
            var run = world.Run!;
            for (int k = 0; k < run.Stops.Count; k++)
                if (run.LockedHouse(k) >= 0)
                    return (Night(route, (run.Stops[k].Start + run.Stops[k].End) / 2, loot), k);
        }
        throw new InvalidOperationException("no frontier night has a village house to lock");
    }

    static World Night(Route.Route route, double at, LootTuning loot)
    {
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 3, 0)), route.Build(), Math.Max(at, 700), Tuning.Boiler);
        var world = new World(train, Tuning.Combat);
        world.EnableBodies();
        world.EnableRun(Tuning.Run, route, 600, authority: true, FacilityTests.F, loot);
        return world;
    }

    /// <summary>Every crewmate acts and moves, then the bodies and the run, the train standing, for so long.</summary>
    static void Step(World world, List<PlayerState> crew, PlayerIntent[] intents, double seconds)
    {
        for (int t = 0; t < Math.Max(1, Math.Round(seconds / Dt)); t++)
        {
            world.Train.Dynamics.Velocity = 0;
            world.BeginTick();
            for (int i = 0; i < crew.Count; i++)
            {
                var s = crew[i];
                world.CrewAct(ref s, intents[i], i + 1);
                crew[i] = s;
            }
            world.Step(new TrainControls { Reverser = 1, Brake = 1 });
            for (int i = 0; i < crew.Count; i++)
            {
                var s = crew[i];
                PlayerMotor.Step(ref s, intents[i], world.Train, P, Tuning.Train, Dt, applyLook: false);
                crew[i] = s;
            }
            world.StepBodies([.. crew.Select((c, i) => (i + 1, c))]);
            world.StepRun(crew);
        }
    }

    static void Press(World world, List<PlayerState> crew, int who)
    {
        Step(world, crew, [.. crew.Select((_, i) => i == who ? new PlayerIntent { Buttons = PlayerButtons.Use } : default)], Dt);
        Step(world, crew, new PlayerIntent[crew.Count], Dt);
    }

    static PlayerState OnTheGround(World world, Double3 at)
    {
        double hint = world.Train.Dynamics.Distance;
        return PlayerMotor.SpawnOnGround(at with { Y = PlayerMotor.GroundAt(at, world.Train.Line, ref hint) }, world.Train.Line, hint, P);
    }

    /// <summary>Let go of on the first cargo car's floor (its middle, or <paramref name="z"/> along), and left till it's still and stowed.</summary>
    static void PutDown(World world, Body b, double z = 0)
    {
        var car = world.Train.Vehicles.First(v => v.Kind == VehicleKind.Cargo);
        var room = world.Train.Frames[car.Id].Shape.Interior!.Value;
        b.Carrier = b.Second = -1;
        b.Parent = car.Id;
        b.Pbd.Particles[0].InverseMass = 1;
        b.Pbd.Particles[0].Position = b.Pbd.Particles[0].Previous = new Double3(0, room.Min.Y + b.Pbd.Particles[0].Radius + 0.05, z);
        b.Pbd.Wake();
        Step(world, [], [], L.SettleSeconds + 1);
    }

    /// <summary>A client of <paramref name="host"/>'s night: its own train on the same line, the run enabled as a client's is.</summary>
    static World Client(World host, LootTuning loot)
    {
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 3, 0)), host.Train.Line, host.Train.Dynamics.Distance, Tuning.Boiler);
        var client = new World(train, Tuning.Combat);
        client.EnableRun(Tuning.Run, host.Run!.Route, 600, authority: false, FacilityTests.F, loot);
        return client;
    }

    /// <summary>The host's records applied to a client, as a snapshot is.</summary>
    static void Mirror(World host, World client)
    {
        var controls = default(TrainControls);
        WorldRecords.Apply(WorldRecords.Capture(host, controls, []), client, ref controls, []);
    }

    static RunReport Delivered(World world)
    {
        var run = world.Run!;
        run.Mirror(RunPhase.Arrived, RunEnd.Delivered, run.Seconds, -1, false, [], scavenged: run.Scavenged);
        return run.Tally(world, [PlayerMotor.SpawnInCab(world.Train, P)]);
    }

    // ---- two-person jackpots.

    [Fact]
    public void AJackpotCantBeLiftedByOneAndGoesWhereTwoTakeIt()
    {
        var (world, k) = AtAJackpotHouse(Sure());
        var run = world.Run!;
        run.Stock(world.Bodies, k);
        var jackpot = world.Bodies.All.First(Run.Run.IsJackpot);
        var find = run.FindOf(jackpot)!.Value;
        Assert.Contains(find.Item, L.Jackpots!.Items.Keys);
        // About a car-load of goods, more than any find one person carries but a comet fragment.
        Assert.InRange(find.Value, 0.8 * PerCar(RouteTier.Frontier), 1.2 * PerCar(RouteTier.Frontier));
        Assert.True(find.Value > L.Worth.Where(w => w.Key != "cometFragment").Max(w => w.Value) * PerCar(RouteTier.Frontier) * 4);
        Assert.Equal(BodyKind.Heavy, jackpot.Kind);

        // Out on the line ahead of the engine, where nothing's in the way: one at either end of it.
        var line = world.Train.Line;
        var ahead = line.Sample(world.Train.Dynamics.Distance + 30);
        double hint = ahead.Distance;
        var lay = ahead.Position with { Y = PlayerMotor.GroundAt(ahead.Position, line, ref hint) + jackpot.Pbd.Particles[0].Radius };
        jackpot.Pbd.Particles[0].Position = jackpot.Pbd.Particles[0].Previous = lay;
        jackpot.Pbd.Wake();
        var crew = new List<PlayerState> { OnTheGround(world, lay + new Double3(0, 0, 1.0)) with { Yaw = 0 },
            OnTheGround(world, lay - new Double3(0, 0, 1.0)) with { Yaw = Math.PI } };
        Step(world, crew, new PlayerIntent[2], 0.5);
        lay = jackpot.Centre;

        // One takes an end: held, not lifted, and walking off leaves it lying. Throwing it does nothing either.
        Press(world, crew, 0);
        Assert.Equal(1, jackpot.Carrier);
        Assert.False(jackpot.Lifted);
        Step(world, crew, [new PlayerIntent { Buttons = PlayerButtons.Throw }, default], Dt);
        Step(world, crew, [new PlayerIntent { MoveZ = -1 }, default], 0.4);
        Assert.True((jackpot.Centre - lay).Length < 0.05, "one alone moved it");
        // Note 861's watch: one at an end, the other standing by, isn't yet a job for two.
        var shared = new SharedWatch(new SharedTuning());
        IReadOnlyList<(int, PlayerState)> Crew() => [.. crew.Select((c, i) => (i + 1, c))];
        shared.Watch(world.Tick, world, Crew());
        Assert.Equal(0, shared.Report(world).Moments.GetValueOrDefault("pairJob"));

        // The second takes the other end: it comes up between them, and goes where they go.
        crew[0] = OnTheGround(world, lay + new Double3(0, 0, 1.0)) with { Yaw = 0 };
        Step(world, crew, new PlayerIntent[2], 0.1);
        if (jackpot.Carrier != 1)
            Press(world, crew, 0);
        Press(world, crew, 1);
        Assert.True(jackpot.Lifted);
        Assert.Equal(2, jackpot.Second);
        Step(world, crew, [new PlayerIntent { MoveX = 1 }, new PlayerIntent { MoveX = -1 }], 1.5);
        Assert.True((jackpot.Centre - lay).Length > 1.0, "two together didn't move it");
        shared.Watch(world.Tick, world, Crew());
        shared.Watch(world.Tick + 1, world, Crew());
        Assert.Equal(1, shared.Report(world).Moments.GetValueOrDefault("pairJob"));
        var between = (PlayerMotor.WorldPosition(crew[0], world.Train) + PlayerMotor.WorldPosition(crew[1], world.Train)) * 0.5;
        Assert.True(((Bodies.WorldCentre(jackpot, world.Train) - between) with { Y = 0 }).Length < 0.4);
    }

    [Fact]
    public void AJackpotPutDownInACarPaysAsAFindAndIsntLoadedAsFreight()
    {
        var (world, k) = AtAJackpotHouse(Sure());
        var run = world.Run!;
        run.Stock(world.Bodies, k);
        var jackpot = world.Bodies.All.First(Run.Run.IsJackpot);
        var car = world.Train.Vehicles.First(v => v.Kind == VehicleKind.Cargo);
        double load = car.Load;
        PutDown(world, jackpot);
        Assert.True(jackpot.Banked);
        Assert.Equal(load, car.Load, 6);
        double worth = run.FindOf(jackpot)!.Value.Value;
        Assert.Equal(worth, run.Scavenged, 6);
        var report = Delivered(world);
        Assert.Equal(Math.Round(worth), report.Scavenged);
        var prize = Assert.Single(report.Prizes, p => p.Item == run.FindOf(jackpot)!.Value.Item);
        Assert.Equal(L.Name(prize.Item), prize.Name);
    }

    [Fact]
    public void MostStopsHaveAJackpotAndTheJackpotHouseHoldsOneAtItsShare()
    {
        // Seeded per stop, the same every time; never more than `most` at a stop. Note 861 (the 16 Oct evening playtest's line
        // 8): most frontier stops have one, so most stops ask two to carry something together (it was about one a night).
        int nights = 40, stops = 0, withOne = 0, inHouses = 0, houses = 0;
        for (ulong seed = 1; seed <= (ulong)nights; seed++)
        {
            var route = RouteGenerator.Generate(Tuning.Route, RouteTier.Frontier, seed);
            for (int i = 0; i < route.Features.Count; i++)
            {
                if (route.Features[i].Stop is not { } stop)
                    continue;
                int locked = StopLoot.JackpotHouse(L, stop, route.Seed, i, RouteTier.Frontier);
                Assert.Equal(locked, StopLoot.JackpotHouse(L, stop, route.Seed, i, RouteTier.Frontier));
                var at = StopLoot.Jackpots(L, stop, route.Seed, i, RouteTier.Frontier, locked, _ => true);
                Assert.Equal(at, StopLoot.Jackpots(L, stop, route.Seed, i, RouteTier.Frontier, locked, _ => true));
                Assert.True(at.Count <= L.Jackpots!.Most);
                Assert.All(at, j => Assert.True(j.Beside >= 0 || locked >= 0));
                stops++;
                withOne += at.Count > 0 ? 1 : 0;
                houses += locked >= 0 ? 1 : 0;
                inHouses += at.Count(j => j.Beside < 0);
            }
        }
        Assert.True(withOne / (double)stops >= 0.5, $"{withOne} of {stops} frontier stops have a jackpot");
        // A jackpot house holds one at `house`.
        Assert.True(houses > 0);
        Assert.InRange(inHouses / (double)houses, L.Jackpots!.House - 0.2, L.Jackpots.House + 0.2);
    }

    // ---- fragile valuables.

    [Fact]
    public void AFragileFindLosesWorthOnAHardKnockAndTheClerkNamesItsDamage()
    {
        var (world, k) = AtAJackpotHouse(Sure("mantelClock"));
        var run = world.Run!;
        var ahead = world.Train.Line.Sample(world.Train.Dynamics.Distance + 30).Position;
        double hint = world.Train.Dynamics.Distance + 30, ground = PlayerMotor.GroundAt(ahead, world.Train.Line, ref hint);
        int owner = Run.Run.LootOwner(k, Run.Run.ExtraContainer);
        double whole = L.WorthOf("mantelClock", PerCar(RouteTier.Frontier));

        // Put down from the hands' height: a metre's fall, no knock.
        var gentle = world.Bodies.SpawnLoot(ahead with { Y = ground + 0.95 }, hint, owner, L.Radius);
        // Dropped from a car's roof (4 m): knocked once.
        var dropped = world.Bodies.SpawnLoot(ahead with { Y = ground + 4 } + new Double3(2, 0, 0), hint, owner, L.Radius);
        Step(world, [], [], 2);
        Assert.True(gentle.Fragile && dropped.Fragile);
        Assert.Equal(0, gentle.Knocks);
        Assert.Equal(1, dropped.Knocks);
        Assert.Equal(whole, run.FindOf(gentle)!.Value.Value);
        var knocked = run.FindOf(dropped)!.Value;
        Assert.Equal(1, knocked.Knocks);
        Assert.Equal(L.Knocked("mantelClock", whole, 1), knocked.Value);
        Assert.True(knocked.Value < whole * 0.75);
        // Thrown hard at the ground again: another.
        dropped.Pbd.Particles[0].SetVelocity(new Double3(0, -9, 0), Dt);
        dropped.Pbd.Wake();
        Step(world, [], [], 1);
        Assert.Equal(2, dropped.Knocks);
        // Not a comet fragment, a trophy, nor a find that isn't fragile.
        Assert.DoesNotContain("cometFragment", L.Fragile!.Items.Keys);

        // Stowed aboard, each pays what it's worth now; the tally names the knocked one by its damage.
        PutDown(world, gentle, -1.5);
        PutDown(world, dropped, 1.5);
        Assert.True(gentle.Banked && dropped.Banked);
        Assert.Equal(whole + run.FindOf(dropped)!.Value.Value, run.Scavenged, 6);
        var report = Delivered(world);
        Assert.Contains(report.Prizes, p => p.Name == "Mantel clock" && p.Worth == whole);
        Assert.Contains(report.Prizes, p => p.Name == "Mantel clock, smashed" && p.Worth == L.Knocked("mantelClock", whole, 2));
        Assert.Contains($"Find: mantel clock, smashed, {L.Knocked("mantelClock", whole, 2):0}.", Radio.Prizes(report));
        // A client draws it as it is: its knocks are on its body's record.
        var copy = Client(world, Sure("mantelClock"));
        Mirror(world, copy);
        Assert.Equal(2, copy.Bodies.All.First(b => b.Id == dropped.Id).Knocks);
    }

    // ---- the jackpot house.

    [Fact]
    public void AJackpotHouseIsLockedAndTheWrenchOpensItQuietly()
    {
        var (world, k) = AtAJackpotHouse(Sure());
        var walls = world.Train.Walls!;
        var run = world.Run!;
        int key = run.LockedDoors().First();
        Assert.True(walls.Locked(key));
        Assert.True(walls.Shut(key));
        var door = walls.HouseDoors.First(d => d.Key == key);
        Assert.True(walls.LockedHouse(door.House));
        var me = OnTheGround(world, door.At + door.Out * 0.6);
        me = me with { Kit = Player.Kit.With(0, 0, Tool.Crowbar), HeldSlot = 0 };
        Assert.Equal(door, world.DoorInReach(me));
        var crew = new List<PlayerState> { me };
        var use = new PlayerIntent { Buttons = PlayerButtons.Use };

        // A held Use, without the wrench: it stays shut and locked.
        Step(world, crew, [use], run.UnlockSeconds + 1);
        Assert.True(walls.Locked(key));
        Step(world, crew, [default], 0.2);

        // The wrench in hand, held there: the lock's picked, the HUD sees how far, and it opens.
        crew[0] = crew[0] with { Kit = Player.Kit.With(0, 0, Tool.Wrench), HeldSlot = 0 };
        Step(world, crew, [use], run.UnlockSeconds - 1);
        Assert.True(walls.Locked(key));
        Assert.InRange(walls.Picking(key), 0.7, 0.95);
        Step(world, crew, [use], 1.2);
        Assert.False(walls.Shut(key));
        Assert.False(walls.Locked(key));
        Assert.Equal(0, walls.Picking(key));
        // And then it's a door like any other: shut by hand, opened by hand.
        Step(world, crew, [default], 0.2);
        crew[0] = crew[0] with { Kit = 0 };
        Step(world, crew, [use], Tuning.Run.Walls.HouseDoorSeconds + 0.1);
        Assert.True(walls.Shut(key));
        Assert.False(walls.Locked(key));
    }

    [Fact]
    public void AClientSeesTheLockPickedAndOpened()
    {
        var (world, _) = AtAJackpotHouse(Sure());
        var walls = world.Train.Walls!;
        int key = world.Run!.LockedDoors().First();
        walls.SetPicking(key, 0.5);
        var client = Client(world, Sure());
        Assert.True(client.Train.Walls!.Locked(key));
        Mirror(world, client);
        Assert.Equal(0.5, client.Train.Walls.Picking(key), 2);
        walls.SetPicking(key, 0);
        walls.SetShut(key, false);
        Mirror(world, client);
        Assert.False(client.Train.Walls.Locked(key));
        Assert.Equal(0, client.Train.Walls.Picking(key));
    }

    [Fact]
    public void AJackpotHousesThingsBeatAnOrdinaryHousesOnAverageAndItsLikelierToHoldACreature()
    {
        double perCar = PerCar(RouteTier.Frontier);
        double jackpotWorth = 0, ordinaryWorth = 0;
        int jackpotHouses = 0, ordinary = 0, lived = 0, livedLocked = 0, all = 0;
        var dt = Tuning.Enemies.Dwellings;
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var route = RouteGenerator.Generate(Tuning.Route, RouteTier.Frontier, seed);
            var walls = StopWalls.Of(route, route.Build(), tuning: Tuning.Run.Walls);
            for (int i = 0; i < route.Features.Count; i++)
            {
                if (route.Features[i].Stop is not { } stop)
                    continue;
                int locked = StopLoot.JackpotHouse(L, stop, route.Seed, i, RouteTier.Frontier);
                var finds = StopLoot.Village(L, stop, route.Seed, i, perCar, locked);
                double In(int building) => finds.Where(f => stop.Containers[f.Container].Building == building).Sum(f => f.Value);
                for (int b = 0; b < stop.Buildings.Count; b++)
                {
                    var house = stop.Buildings[b];
                    if (house.Kind != BuildingKind.House || !house.Open || !StopWalls.Walled(stop, b))
                        continue;
                    if (b == locked)
                    {
                        jackpotHouses++;
                        jackpotWorth += In(b) + StopLoot.Extras(L, stop, route.Seed, i, locked).Sum(x => L.WorthOf(x, perCar))
                            + StopLoot.Jackpots(L, stop, route.Seed, i, RouteTier.Frontier, locked, _ => true).Where(j => j.Beside < 0)
                                .Sum(j => L.Jackpots!.Items[j.Item].Worth * perCar);
                    }
                    else
                    {
                        ordinary++;
                        ordinaryWorth += In(b);
                    }
                }
                // The house creatures' dice, on every open house of the stop: weighted for the jackpot house, not forced.
                foreach (var h in walls.OpenHouses.Where(h => h.Feature == i))
                {
                    all++;
                    lived += Dwelling.Lives(route, stop, h, dt, null) is not null ? 1 : 0;
                    livedLocked += Dwelling.Lives(route, stop, h, dt, null, jackpot: true) is not null ? 1 : 0;
                }
            }
        }
        Assert.True(jackpotHouses > 5, $"{jackpotHouses} jackpot houses in 40 nights");
        double jackpotMean = jackpotWorth / jackpotHouses, ordinaryMean = ordinaryWorth / ordinary;
        Assert.True(jackpotMean > 3 * ordinaryMean, $"a jackpot house keeps {jackpotMean:0} against an ordinary one's {ordinaryMean:0}");
        // Even without its jackpot it keeps more: its draws and its extras.
        Assert.True(lived > 0 && livedLocked > lived * 1.5 && livedLocked < all, $"{lived} lived in, {livedLocked} as jackpot houses, of {all}");
    }

    // ---- loose loot rides the train.

    /// <summary>A frontier night's train on a bend of radius under 500 m, its whole length on it, and its first cargo car.</summary>
    static (World World, Vehicle Car, int Side) OnABend(LootTuning loot)
    {
        for (ulong seed = 1; seed < 40; seed++)
        {
            var route = RouteGenerator.Generate(Tuning.Route, RouteTier.Frontier, seed);
            var line = route.Build();
            for (double s = 2000; s < line.Length - 2000; s += 20)
            {
                double k = line.Sample(s).Curvature;
                if (Math.Abs(k) < 1 / 500.0 || Enumerable.Range(0, 30).Any(i => Math.Abs(line.Sample(s - 100 + i * 10).Curvature - k) > 1e-4))
                    continue;
                var world = Night(route, s + 80, loot);
                // No stop's loot, nothing else lying about.
                foreach (var b in world.Bodies.All.ToList())
                    world.Bodies.Remove(b);
                var car = world.Train.Vehicles.First(v => v.Kind == VehicleKind.Cargo);
                // Outward: away from the way the line turns, across the car.
                var f = world.Train.Frames[car.Id];
                var inward = line.Sample(s + 10).Tangent - line.Sample(s - 10).Tangent;
                int side = f.DirToLocal(inward).X > 0 ? -1 : 1;
                return (world, car, side);
            }
        }
        throw new InvalidOperationException("no frontier line has a long enough bend");
    }

    static void Ride(World world, double speed, double seconds)
    {
        for (int t = 0; t < Math.Round(seconds / Dt); t++)
        {
            world.Train.Dynamics.Velocity = speed;
            world.BeginTick();
            world.Step(new TrainControls { Reverser = 1, Brake = speed > 0 ? 0 : 1 });
            world.StepBodies([]);
            world.StepRun([]);
        }
    }

    static Body Lying(World world, Vehicle car, double x, double z, int owner = -1)
    {
        var room = world.Train.Frames[car.Id].Shape.Interior!.Value;
        var b = world.Bodies.Put(world.Train, car.Id, BodyKind.Loot, new Double3(x, room.Min.Y + L.Radius, z), L.Radius, 0.4, 0.05);
        b.Owner = owner;
        return b;
    }

    [Fact]
    public void LooseFindsInACarSlideOutwardOnABendAndLieStillOnTheStraight()
    {
        var (world, car, side) = OnABend(Riding);

        var find = Lying(world, car, 0, 0);
        // Settled where they're put, at the bend's speed: 18 m/s round under 500 m is over the grip.
        Ride(world, 0.5, 0.5);
        double x0 = find.Centre.X;
        Ride(world, 18, 3);
        Assert.Equal(car.Id, find.Parent);
        Assert.True((find.Centre.X - x0) * side > 0.3, $"slid {(find.Centre.X - x0) * side:0.00} m outward");
        Assert.True(world.Train.Frames[car.Id].Shape.Interior!.Value.Contains(find.Centre));
        // Against the car's side it stops, and sleeps.
        Ride(world, 18, 3);
        Assert.True(find.Pbd.Asleep || find.Slide.Length < 0.05);

        // With riding off, things lie as they lay: nothing slides across the car on the bend.
        var (still, stillCar, _) = OnABend(L with { Ride = L.Ride! with { Enabled = false } });
        var lying = Lying(still, stillCar, 0, 0);
        Ride(still, 18, 1);
        var at = lying.Centre;
        Ride(still, 18, 3);
        Assert.True(Math.Abs(lying.Centre.X - at.X) < 0.05, $"moved {lying.Centre - at}");
    }

    [Fact]
    public void UnderTheBrakesLooseFindsSlideForwardAndAFragileOneIsKnockedAtTheCarsEnd()
    {
        // A straight, the train at line speed, a find lying loose at the back of the first cargo car and a mantel clock with it.
        World? world = null;
        Vehicle? car = null;
        for (ulong seed = 1; seed < 40 && world is null; seed++)
        {
            var route = RouteGenerator.Generate(Tuning.Route, RouteTier.Frontier, seed);
            var line = route.Build();
            for (double s = 2000; s < line.Length - 2000 && world is null; s += 50)
                if (Enumerable.Range(0, 40).All(i => Math.Abs(line.Sample(s - 200 + i * 10).Curvature) < 1e-5))
                {
                    world = Night(route, s, Sure("mantelClock") with { Ride = Riding.Ride });
                    foreach (var b in world.Bodies.All.ToList())
                        world.Bodies.Remove(b);
                    car = world.Train.Vehicles.First(v => v.Kind == VehicleKind.Cargo);
                }
        }
        Assert.NotNull(world);
        var room = world.Train.Frames[car!.Id].Shape.Interior!.Value;
        var find = Lying(world, car, 0, room.Max.Z - 0.5);
        int k = Enumerable.Range(0, world.Run!.Stops.Count).First(i => world.Run.LockedHouse(i) >= 0);
        var clock = Lying(world, car, 0.5, room.Max.Z - 0.5, Run.Run.LootOwner(k, Run.Run.ExtraContainer));
        Ride(world, 20, 1);
        double z0 = find.Centre.Z;
        Assert.True(Math.Abs(world.Bodies.Felt(car.Id).X) < 0.05, "felt something sideways on a straight");
        // Full brakes from 20 m/s: forward (−Z) they go, all the way to the car's front end.
        for (int t = 0; t < 6 * SimConstants.TickRate; t++)
        {
            world.BeginTick();
            world.Step(new TrainControls { Reverser = 1, Brake = 1 });
            world.StepBodies([]);
            world.StepRun([]);
        }
        Assert.True(find.Centre.Z < z0 - 3, $"slid {z0 - find.Centre.Z:0.0} m forward");
        Assert.True(clock.Fragile);
        Assert.True(clock.Knocks >= 1, "the clock struck the car's end and wasn't knocked");
    }

    [Fact]
    public void OneByAnOpenSideDoorOnTheBendsOutsideFallsOutAndIsntPaidFor()
    {
        var (world, car, side) = OnABend(Riding);
        var shape = world.Train.Frames[car.Id].Shape;
        var room = shape.Interior!.Value;
        var door = shape.DoorList.First(d => d.Box.Max.Z - d.Box.Min.Z > d.Box.Max.X - d.Box.Min.X && Math.Sign(d.Box.Centre.X) == side);
        car.ToggleDoor(door.Index);
        Assert.True(car.DoorOpen(door.Index));
        // A find (a stop's, so it banks), lying just inside the open door.
        var stop = Enumerable.Range(0, world.Run!.Stops.Count).First(k => world.Run.Stops[k].Stop!.Containers.Any(c => c.Zone == StopZone.Village && c.Kind != ContainerKind.Loose));
        int owner = Run.Run.LootOwner(stop, world.Run!.Stops[stop].Stop!.Containers.First(c => c.Zone == StopZone.Village && c.Kind != ContainerKind.Loose).Index);
        world.Run.Stock(world.Bodies, stop, searched: true);
        foreach (var b in world.Bodies.All.Where(b => b.Owner != owner || b.Kind != BodyKind.Loot).ToList())
            world.Bodies.Remove(b);
        var find = world.Bodies.All.First(b => b.Owner == owner);
        find.Parent = car.Id;
        var edge = side > 0 ? room.Max.X - 0.25 : room.Min.X + 0.25;
        find.Pbd.Particles[0].Position = find.Pbd.Particles[0].Previous = new Double3(edge, room.Min.Y + L.Radius, door.Box.Centre.Z);
        find.Pbd.Wake();
        Ride(world, 0.5, L.SettleSeconds + 1);
        Assert.True(find.Banked);
        Assert.True(world.Run.Scavenged > 0);
        // Round the bend at speed: it goes out through the door, onto the line behind, and it's not aboard to pay.
        Ride(world, 18, 4);
        Assert.Equal(PlayerState.World, find.Parent);
        Assert.False(find.Banked);
        Assert.Equal(0, world.Run.Scavenged, 6);
    }

    [Fact]
    public void ABadlyStackedThingToppplesAndTheCapHoldsTheRestStill()
    {
        var (world, car, _) = OnABend(Riding);
        var room = world.Train.Frames[car.Id].Shape.Interior!.Value;
        // One on another, set well off its middle: it rolls off on its own, the train standing.
        var under = Lying(world, car, 0, 0);
        Ride(world, 0, 0.5);
        var over = world.Bodies.Put(world.Train, car.Id, BodyKind.Loot, under.Centre + new Double3(0.14, L.Radius * 2 + 0.01, 0), L.Radius, 0.4, 0.05);
        Ride(world, 0, 2);
        Assert.True(over.Centre.Y < under.Centre.Y + 0.05, "it stayed balanced");
        // Set square on top, it stays.
        var top = world.Bodies.Put(world.Train, car.Id, BodyKind.Loot, under.Centre + Double3.Up * (L.Radius * 2 + 0.01), L.Radius, 0.4, 0.05);
        Ride(world, 0, 2);
        Assert.True(top.Centre.Y > under.Centre.Y + 0.25, "a square stack fell");

        // More than the cap lying loose: no more than the cap move at once.
        var many = Enumerable.Range(0, L.Ride!.LiveCap + 6).Select(i => Lying(world, car, -0.5 + i % 3 * 0.4, -3 + i / 3 * 0.4)).ToList();
        Ride(world, 18, 0.5);
        Assert.True(many.Concat([under, over, top]).Count(b => !b.Pbd.Asleep) <= L.Ride.LiveCap);
    }
}
