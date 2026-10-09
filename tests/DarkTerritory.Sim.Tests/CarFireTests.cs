using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>Fire and firefighting (GDD v1.1 App. C.5): every car's extinguisher, off its mount and sprayed, puts a fire out.</summary>
public class CarFireTests
{
    static readonly PlayerTuning P = Tuning.Player;

    [Fact]
    public void TakeTheCarsExtinguisherOffItsMountAndSprayTheFireOut()
    {
        var n = new Night(4, speed: 8);
        n.World.MountExtinguishers();
        int car = 2;
        var room = n.Train.Frames[car].Shape.Interior!.Value;
        var ext = Assert.Single(n.World.Bodies.All, b => b.Kind == BodyKind.Extinguisher && b.Parent == car);
        Assert.Equal(car, ext.Home);
        var fire = n.World.AddEnemy(id => CarFire.In(id, n.Train, car, 2, Tuning.Enemies.CarFire));
        // Stood by the extinguisher: Use takes it.
        n.Crew[1] = new PlayerState { Parent = car, Position = ext.Centre with { Y = room.Min.Y, Z = ext.Centre.Z + 0.5 }, Surface = Surface.Deck, Health = P.Health, Pitch = -0.6 };
        n.Run(0.2, _ => new PlayerIntent { Buttons = PlayerButtons.Use });
        n.Run(0.2);
        Assert.Equal(1, ext.Carrier);
        // Beside the fire, looking at it, Fire held: the cell it's aimed at is knocked back until it's out (note 267).
        n.Crew[1] = Aim(n.Crew[1] with { Position = new Double3(room.Centre.X, room.Min.Y, fire.Local.Z + 1.2) }, Cell(n, fire));
        n.Run(Tuning.Enemies.CarFire.ChargeSeconds, _ => new PlayerIntent { Buttons = PlayerButtons.Fire });
        Assert.True(fire.Gone, $"{fire.Phase} at {fire.Extra:0.00}");
        Assert.True(ext.Charge < 1);
    }

    [Fact]
    public void AWalkerOnTheNextRoofGetsInTakesTheExtinguisherAndPutsItOut()
    {
        // The bot crew's answer (T88): from car 2's roof, the smoke in car 3 (the telegraph), in by the end door, the
        // extinguisher off its mount, sprayed from the aisle, before it's taken hold.
        var n = new Night(5, speed: 10);
        n.World.MountExtinguishers();
        var bot = new Bots.RoofWalkerBot(3, Tuning.Player.Cold) { Me = 1 };
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, 2, 0, P);
        var fire = n.World.AddEnemy(id => CarFire.In(id, n.Train, 3, 2, Tuning.Enemies.CarFire));
        for (int s = 0; s < 60 && !fire.Gone; s++)
            n.Run(1, id => bot.Decide(n.Crew[id], n.World, n.World.Tick, out _));
        Assert.True(fire.Gone, $"{fire.Phase} at {fire.Extra:0.00}");
        Assert.True(n.Crew[1].Alive);
        Assert.True(n.Train.Vehicles[3].CargoIntegrity > 0.9);
        // And its lamp's out (note 188): the fire's most likely the Fire Flies', and they come back to a lit lamp.
        Assert.False(n.Train.Vehicles[3].LampLit);
    }

    [Fact]
    public void TheDriverVentsAndStarvesTheStokerOutBeforeTheBoilerGoes()
    {
        // Stoker v3 (note 271): the driver bot never opens the door on it; it holds the vent and the brake and fires nothing
        // until it's starved out, and nobody's hurt.
        var n = new Night(4, speed: 0, boiler: true);
        n.Train.Boiler.Pressure = 80;
        n.Train.Boiler.Firebox = 5;
        var driver = new Bots.ConductorBot(null, 0);
        n.Crew[1] = PlayerMotor.SpawnInCab(n.Train, P);
        var stoker = n.World.AddEnemy(id => Stoker.InFirebox(id, n.Train, false, Tuning.Enemies.Stoker));
        for (int s = 0; s < 90 && !stoker.Gone; s++)
            n.Run(1, id => driver.Decide(n.Crew[id], n.World, n.World.Tick, out _), holdSpeed: false);
        Assert.True(stoker.Gone, $"{stoker.Phase}, the fire {n.Train.Boiler.Firebox:0.0}, the gauge {n.Train.Boiler.Pressure:0}");
        Assert.False(n.Train.Boiler.Ruptured);
        Assert.Equal(P.Health, n.Crew[1].Health);
    }

    [Fact]
    public void AWalkerPutsASwarmedLampOutBeforeTheFliesSetTheCarAlight()
    {
        // v1.1 App. A.5: "lamps off when they swarm". Car 3's lamp lit, the flies on it, a walker on car 2's roof. Stopped: they
        // come to nothing else, and go on their own once it's under way (note 269).
        var n = new Night(5, speed: 0);
        var bot = new Bots.RoofWalkerBot(3, Tuning.Player.Cold) { Me = 1 };
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, 2, 0, P);
        var flies = n.World.AddEnemy(id => FireFlies.OnLamp(id, n.Train, 3));
        n.Run(Tuning.Enemies.FireFlies.IgniteSeconds + 2, id => bot.Decide(n.Crew[id], n.World, n.World.Tick, out _));
        Assert.True(flies.Gone);
        Assert.False(n.Train.Vehicles[3].LampLit);
        Assert.DoesNotContain(n.World.ActiveEnemies, e => e is CarFire && !e.Gone);
    }
    [Fact]
    public void AWalkerTakesTheExtinguisherFromBesideTheCrewLockersAndPutsTheFireOut()
    {
        // Note 188: car 1's extinguisher stands just ahead of the crew lockers (note 173), and the walker made for the spot
        // just aft of it, inside the lockers, for as long as the car burned. From the aisle beside it, facing the wall.
        var n = new Night(5, speed: 10);
        n.World.MountExtinguishers();
        int car = n.Train.Dynamics.Consist.Vehicles[1].Id;
        Assert.NotEmpty(n.Train.Frames[car].Shape.Lockers);
        var bot = new Bots.RoofWalkerBot(3, Tuning.Player.Cold) { Me = 1 };
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, car + 1, 0, P);
        var fire = n.World.AddEnemy(id => CarFire.In(id, n.Train, car, 2, Tuning.Enemies.CarFire));
        for (int s = 0; s < 60 && !fire.Gone; s++)
            n.Run(1, id => bot.Decide(n.Crew[id], n.World, n.World.Tick, out _));
        Assert.True(fire.Gone, $"{fire.Phase} at {fire.Extra:0.00}; the walker {bot.TendStep ?? bot.WarmUpStep} on {n.Crew[1].Parent} at {n.Crew[1].Position}");
        Assert.True(n.Crew[1].Alive);
    }

    [Fact]
    public void WalkersOnASideDoorsStepsGoInAndPutTheFireOut()
    {
        // Note 437: frontier:7's 8-bot hot run. Four walkers in at car 1's side door (for a bag) stood on its steps, outside
        // the walls, when the car caught: they made for its extinguishers on a slant, into the door's jamb, and stood against
        // it for 20 s with the car alight round them. Straight in through the doorway first.
        var n = new Night(5, speed: 15); // running: past what anyone runs, so nobody steps out of the doorway (note 380)
        n.World.MountExtinguishers();
        int car = n.Train.Dynamics.Consist.Vehicles[1].Id;
        var shape = n.Train.Frames[car].Shape;
        int door = Bots.StopHand.SideDoor(shape, -1)!.Value;
        n.Train.Vehicles[car].ToggleDoor(door);
        var (at, _) = Bots.WarmUp.Inside(shape, door);
        var room = shape.Interior!.Value;
        var fire = n.World.AddEnemy(id => CarFire.In(id, n.Train, car, 2, Tuning.Enemies.CarFire));
        var bots = new List<Bots.RoofWalkerBot>();
        for (int i = 1; i <= 3; i++)
        {
            bots.Add(new Bots.RoofWalkerBot(i, Tuning.Player.Cold) { Me = i });
            // Where they stood: out past the wall, at the doorway's forward edge.
            var opening = shape.DoorList.First(d => d.Index == door).Box;
            n.Crew[i] = new PlayerState
            {
                Parent = car,
                Position = new Double3(room.Min.X - 0.42 + 0.02 * i, Tuning.Train.Geometry.Interior!.FloorHeight, opening.Min.Z + 0.25 + 0.05 * i),
                Yaw = 3.13,
                Surface = Surface.Deck,
                Health = P.Health
            };
        }
        for (int s = 0; s < 60 && !fire.Gone; s++)
        {
            foreach (var b in bots)
                b.Crew = [.. n.Crew.Select(kv => (kv.Key, kv.Value))];
            n.Run(1, id => bots[id - 1].Decide(n.Crew[id], n.World, n.World.Tick, out _));
        }
        Assert.True(fire.Gone, $"{fire.Phase} at {fire.Extra:0.00}; " + string.Join("; ", bots.Select(b => $"{b.TendStep ?? b.WarmUpStep} at {n.Crew[b.Me].Position}")));
        Assert.All(n.Crew.Values, c => Assert.True(c.Alive, $"died of {c.Death}"));
    }

    [Fact]
    public void AnExtinguisherPutDownInTheAisleIsTakenFromTheAisle()
    {
        // Note 188: put down spent in the aisle, it recharges where it lies. In from it (the side away from its wall, the way
        // to one on its mount) is the cargo's stack: the walker walked at the stack for as long as it was let. From aft of it.
        var n = new Night(5, speed: 0);
        int car = 3;
        var room = n.Train.Frames[car].Shape.Interior!.Value;
        var ext = n.World.Bodies.SpawnCrate(n.Train, car, new Double3(Tuning.Train.Geometry.Interior!.DoorX, room.Min.Y + 0.1, -3), BodyKind.Extinguisher);
        ext.Home = car;
        var bot = new Bots.RoofWalkerBot(3, Tuning.Player.Cold) { Me = 1 };
        n.Crew[1] = new PlayerState { Parent = car, Position = new Double3(Tuning.Train.Geometry.Interior.DoorX, room.Min.Y, 5), Surface = Surface.Deck, Health = P.Health };
        var fire = n.World.AddEnemy(id => CarFire.In(id, n.Train, car, 0, Tuning.Enemies.CarFire));
        for (int s = 0; s < 40 && !fire.Gone; s++)
            n.Run(1, id => bot.Decide(n.Crew[id], n.World, n.World.Tick, out _));
        Assert.True(fire.Gone, $"{fire.Phase} at {fire.Extra:0.00}; the walker {bot.TendStep ?? bot.WarmUpStep} at {n.Crew[1].Position}");
    }

    [Fact]
    public void AWalkerGetsIntoTheGuardVanOffItsRearPlatformAndPutsItsFireOut()
    {
        // Note 188: the guard van, last, has no car behind it and so no plate; its rear door opens onto its platform. Nobody
        // went in: the walkers paced its roof with the fire under them and it spread forward. From car 4's roof, along to the
        // van's, down onto the platform (slowly: nothing beyond it), in at the rear door, the van's extinguisher, sprayed.
        var n = new Night(5, speed: 10);
        n.World.MountExtinguishers();
        int van = n.Train.Dynamics.Consist.Vehicles[^1].Id;
        Assert.Equal(VehicleKind.Guard, n.Train.Vehicles[van].Kind);
        var bot = new Bots.RoofWalkerBot(3, Tuning.Player.Cold) { Me = 1 };
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, van - 1, 0, P);
        var fire = n.World.AddEnemy(id => CarFire.In(id, n.Train, van, 0, Tuning.Enemies.CarFire));
        for (int s = 0; s < 60 && !fire.Gone; s++)
            n.Run(1, id => bot.Decide(n.Crew[id], n.World, n.World.Tick, out _));
        Assert.True(fire.Gone, $"{fire.Phase} at {fire.Extra:0.00}; the walker {n.Crew[1].Surface} on {n.Crew[1].Parent} at {n.Crew[1].Position}");
        Assert.True(n.Crew[1].Alive);
        Assert.Equal(van, n.Crew[1].Parent);
    }

    [Fact]
    public void TheDriverDrivesAwayFromFireFliesWhereTheLineAllows()
    {
        // GDD §21 "lamps off when they swarm. Or drive away"; App. A.5 BREAK OFF "the train pulls away at speed" (note 188).
        // They come only to a stopped train, and getting under way is pulling away (note 269): nobody in the car, the driver
        // gets the train moving, they go, and the car never catches.
        var n = new Night(5, speed: 0, boiler: true);
        n.Train.Boiler.Pressure = 85;
        var driver = new Bots.ConductorBot(null, 0);
        n.Crew[1] = PlayerMotor.SpawnInCab(n.Train, P);
        var flies = n.World.AddEnemy(id => FireFlies.OnLamp(id, n.Train, 3));
        double top = 0;
        // The cab's controls as the host works them from the driver's intent.
        void Drive(double seconds)
        {
            for (int t = 0; t < seconds * SimConstants.TickRate; t++)
            {
                var intent = driver.Decide(n.Crew[1], n.World, n.World.Tick, out _);
                if (CabControls.Clears(n.Controls, n.Train, CabControls.ReleasesBrake(intent, n.Crew[1], n.Train)))
                    n.Controls.Brake = 0;
                CabControls.Apply(ref n.Controls, intent, n.Crew[1], n.Train);
                n.Run(SimConstants.TickSeconds, _ => intent, holdSpeed: false);
                top = Math.Max(top, n.Train.Dynamics.Speed);
            }
        }
        for (int s = 0; s < Tuning.Enemies.FireFlies.IgniteSeconds + 5 && !flies.Gone; s++)
            Drive(1);
        Assert.True(flies.Gone, $"top speed {top:0.0} m/s");
        Assert.True(n.Train.Vehicles[3].LampLit);
        Assert.DoesNotContain(n.World.ActiveEnemies, e => e is CarFire && !e.Gone);
        Assert.InRange(top, Tuning.Enemies.FireFlies.PullAwaySpeed, n.Train.Dynamics.Tuning.MaxSpeed - 3);
        // And on to the cruise after, no faster (from a stand it overshoots before it settles).
        Drive(90);
        Assert.InRange(n.Train.Dynamics.Speed, 12, 15);
    }

    [Fact]
    public void APowderCarAtFullBlazeGoesUpAndKillsWhoeversNearIt()
    {
        // GDD §19 "gunpowder and shot: explodes", B.9 "every fire is worse" (note 182). Car 3 full of powder, alight and left:
        // at full blaze it goes up. Whoever's inside it is killed, the car and its cargo are gone, the cars either side catch,
        // and every client sees a blast there. A crewmate three cars off is untouched; a rescued child in it is unharmed.
        var t = Tuning.Enemies.CarFire;
        var n = new Night(6, speed: 8);
        int car = 3;
        n.Train.Vehicles[car].Cargo = CargoKind.Ammunition;
        var room = n.Train.Frames[car].Shape.Interior!.Value;
        var child = n.World.Bodies.SpawnCrate(n.Train, car, new Double3(room.Centre.X, room.Min.Y + 0.1, room.Max.Z - 1), BodyKind.Child);
        var fire = n.World.AddEnemy(id => CarFire.In(id, n.Train, car, 2, t));
        n.Crew[1] = new PlayerState { Parent = car, Position = new Double3(room.Centre.X, room.Min.Y, fire.Local.Z + 1), Surface = Surface.Deck, Health = P.Health };
        n.Crew[2] = PlayerMotor.SpawnOnRoof(n.Train, car + 3 > 6 ? 0 : car + 3, 0, P);
        n.Crew[3] = PlayerMotor.SpawnOnRoof(n.Train, car + 1, -4, P);
        fire.Ablaze(0.97);
        for (int s = 0; s < 60 && n.Train.Vehicles[car].Integrity > 0; s++)
            n.Run(1);
        Assert.Equal(0, n.Train.Vehicles[car].Integrity);
        Assert.Equal(0, n.Train.Vehicles[car].CargoIntegrity);
        Assert.False(n.Crew[1].Alive);
        Assert.Equal(DeathCause.Exploded, n.Crew[1].Death);
        Assert.Equal(P.Health, n.Crew[2].Health);
        // On the next car's roof, a car's length off: inside the blast's reach, hurt by it (or killed, near its end).
        Assert.True(n.Crew[3].Health < P.Health);
        Assert.Contains(n.World.ActiveEnemies, e => e is CarFire && !e.Gone && e.Attached == car - 1);
        Assert.Contains(n.World.ActiveEnemies, e => e is CarFire && !e.Gone && e.Attached == car + 1);
        Assert.Contains(n.World.Impacts, i => i.Shooter == -1 && i.Surface == Combat.ImpactSurface.Train);
        Assert.Contains(child, n.World.Bodies.All);
        // Within explodeRadius of the blast and no further.
        var at = n.World.Impacts.First(i => i.Shooter == -1).At;
        Assert.True((PlayerMotor.WorldPosition(n.Crew[2], n.Train) - at).Length > t.ExplodeRadius);
        Assert.True((PlayerMotor.WorldPosition(n.Crew[3], n.Train) - at).Length < t.ExplodeRadius);
    }

    [Fact]
    public void AFireInACarOfGoodsNeverExplodes()
    {
        var n = new Night(6, speed: 8);
        var fire = n.World.AddEnemy(id => CarFire.In(id, n.Train, 3, 2, Tuning.Enemies.CarFire));
        fire.Ablaze(0.97);
        n.Run(20);
        Assert.True(n.Train.Vehicles[3].Integrity > 0);
        Assert.DoesNotContain(n.World.Impacts, i => i.Shooter == -1);
    }

    /// <summary>Where a fire not yet lit on its cells starts: the floor cell under it (note 267).</summary>
    static Double3 Cell(Night n, CarFire fire) =>
        FireGrid.Of(n.Train, fire.Attached, Tuning.Enemies.CarFire.CellSize)!.Centre[FireGrid.Of(n.Train, fire.Attached, Tuning.Enemies.CarFire.CellSize)!.FloorAt(fire.Local)];

    /// <summary>A crewmate turned to look at a point in their car (the spray goes from the eye along the look).</summary>
    static PlayerState Aim(PlayerState s, Double3 at)
    {
        var d = at - (s.Position + Double3.Up * Tuning.Train.Pick.EyeHeight);
        return s with { Yaw = Math.Atan2(-d.X, -d.Z), Pitch = Math.Atan2(d.Y, Math.Sqrt(d.X * d.X + d.Z * d.Z)) };
    }

    /// <summary>A crewmate in car 2 with its extinguisher in hand, and a fire on its floor.</summary>
    static (Night N, CarFire Fire, Body Ext, Box Room) Armed()
    {
        var n = new Night(4, speed: 8);
        n.World.MountExtinguishers();
        int car = 2;
        var room = n.Train.Frames[car].Shape.Interior!.Value;
        var ext = Assert.Single(n.World.Bodies.All, b => b.Kind == BodyKind.Extinguisher && b.Parent == car);
        var fire = n.World.AddEnemy(id => CarFire.In(id, n.Train, car, 2, Tuning.Enemies.CarFire));
        n.Crew[1] = new PlayerState { Parent = car, Position = ext.Centre with { Y = room.Min.Y, Z = ext.Centre.Z + 0.5 }, Surface = Surface.Deck, Health = P.Health, Pitch = -0.6 };
        n.Run(0.2, _ => new PlayerIntent { Buttons = PlayerButtons.Use });
        n.Run(0.2);
        Assert.Equal(1, ext.Carrier);
        return (n, fire, ext, room);
    }

    [Fact]
    public void ASecondOfSprayPutsOutTheCellItsAimedAt()
    {
        // The director, 8 Oct 2026 (note 467): "Holding fire extinguisher on fire still doesnt feel like its doing anything.
        // should be 1s per grid to put out." The car well alight round it, a cell at full blaze aimed at for a second is out.
        var (n, fire, ext, room) = Armed();
        fire.Ablaze(0.97);
        var grid = FireGrid.Of(n.Train, 2, Tuning.Enemies.CarFire.CellSize)!;
        int cell = grid.FloorAt(fire.Local);
        var at = grid.Centre[cell];
        n.Crew[1] = Aim(n.Crew[1] with { Position = new Double3(room.Centre.X, room.Min.Y, at.Z - 2) }, at);
        n.Run(1.0 + SimConstants.TickSeconds, _ => new PlayerIntent { Buttons = PlayerButtons.Fire });
        Assert.Contains(cell, fire.Sprayed);
        Assert.Equal(0, fire.Heat[cell]);
        Assert.False(fire.Gone); // the rest of the car's still alight: one cell a second
    }

    [Fact]
    public void TheSprayPutsOutTheCellItsAimedAtAndNotTheFireBehindYou()
    {
        // App. F.1 (the director's decision of 6 Oct 2026): "the extinguisher puts out the cell you aim at". Beside the fire,
        // looking down the car away from it, the charge goes on boards that aren't burning, and the fire's still there.
        var (n, fire, ext, room) = Armed();
        var at = Cell(n, fire);
        n.Crew[1] = n.Crew[1] with { Position = new Double3(room.Centre.X, room.Min.Y, at.Z - 1.2) };
        n.Crew[1] = Aim(n.Crew[1], new Double3(at.X, room.Min.Y, at.Z - 4));
        n.Run(Tuning.Enemies.CarFire.ChargeSeconds, _ => new PlayerIntent { Buttons = PlayerButtons.Fire });
        Assert.False(fire.Gone);
        Assert.True(ext.Charge < 0.05, $"charge {ext.Charge:0.00}");
        Assert.All(fire.Sprayed, c => Assert.NotEqual(FireGrid.Of(n.Train, 2, Tuning.Enemies.CarFire.CellSize)!.FloorAt(fire.Local), c));
    }

    [Fact]
    public void LeftAloneAFireClimbsTheWallsAndRunsAlongTheRoof()
    {
        // App. F.1: cells on the floor, walls and roof, never mid-air; fire climbs, so the roof's cells catch before the far
        // end of the floor does.
        var n = new Night(5, speed: 8);
        var fire = n.World.AddEnemy(id => CarFire.In(id, n.Train, 2, 0, Tuning.Enemies.CarFire));
        var grid = FireGrid.Of(n.Train, 2, Tuning.Enemies.CarFire.CellSize)!;
        n.Run(1);
        Assert.Single(fire.Heat, h => h > 0); // one cell, on the floor, where it was set
        Assert.Equal(FireFace.Floor, grid.Face[Array.FindIndex(fire.Heat, h => h > 0)]);
        n.Run(60);
        Assert.Contains(Enumerable.Range(0, grid.Count), i => grid.Face[i] is FireFace.Left or FireFace.Right && fire.Heat[i] > 0.3);
        Assert.Contains(Enumerable.Range(0, grid.Count), i => grid.Face[i] == FireFace.Ceiling && fire.Heat[i] > 0.3);
        Assert.True(fire.Extra < 1, $"the whole car at {fire.Extra:0.00} after a minute");
        // Every burning cell's on a surface of the room: never mid-air.
        var room = grid.Room;
        Assert.All(Enumerable.Range(0, grid.Count).Where(i => fire.Heat[i] > 0), i =>
        {
            var c = grid.Centre[i];
            Assert.True(Math.Abs(c.Y - room.Min.Y - FireGrid.FloorPad) < 1e-6 || Math.Abs(c.Y - room.Max.Y) < 1e-6 || Math.Abs(c.X - room.Min.X) < 1e-6 || Math.Abs(c.X - room.Max.X) < 1e-6);
        });
    }

    [Fact]
    public void WhatBurnsChars()
    {
        // App. F.1: "burnt cells char the textures". The car keeps its char after the fire, and every client is told.
        var n = new Night(5, speed: 8);
        var fire = n.World.AddEnemy(id => CarFire.In(id, n.Train, 2, -3, Tuning.Enemies.CarFire).Ablaze(1, 1));
        n.Run(30);
        var car = n.Train.Vehicles[2];
        var grid = FireGrid.Of(n.Train, 2, Tuning.Enemies.CarFire.CellSize)!;
        Assert.Equal(grid.Count, car.Char.Length);
        Assert.Contains(car.Char, c => c > 0);
        int far = grid.FloorAt(new Double3(0, 0, grid.Room.Max.Z - 0.1));
        Assert.Equal(0, car.Char[far]);
        var client = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 5, 1)), n.Train.Line, 2_000), Tuning.Combat);
        client.EnableEnemies(Tuning.Enemies, route: null, 1, crew: 4, authority: false);
        var controls = new TrainControls();
        WorldRecords.Apply(WorldRecords.Capture(n.World, controls, []), client, ref controls, []);
        Assert.Equal(car.Char, client.Train.Vehicles[2].Char);
        var seen = Assert.Single(client.ActiveEnemies.OfType<CarFire>());
        Assert.Equal(fire.Heat.Length, seen.Heat.Length);
        for (int i = 0; i < fire.Heat.Length; i++)
            Assert.Equal(fire.Heat[i] > 0, seen.Heat[i] > 0);
    }

    [Fact]
    public void ItJumpsTheCouplingFromAnEndWallIntoTheNextCarsNearEnd()
    {
        // Its way out of a car is through its ends (note 267): alight against car 2's rear wall, it takes car 3, at its front.
        var n = new Night(5, speed: 8);
        var t = Tuning.Enemies.CarFire;
        var fire = n.World.AddEnemy(id => CarFire.In(id, n.Train, 2, 99, t).Ablaze(1, 1.5));
        n.Run(t.SpreadSeconds + 2);
        var next = Assert.Single(n.World.ActiveEnemies.OfType<CarFire>(), e => e.Attached != 2 && !e.Gone);
        Assert.Equal(n.Train.VehicleBehind(2), next.Attached);
        Assert.True(next.Local.Z < 0, $"caught at {next.Local.Z:0.0}: the near end is the front, -Z");
    }

    [Fact]
    public void CellsPackForTheWire()
    {
        double[] cells = [0, 0.01, 0.5, 1, 0.2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0.99, 0.3];
        var back = CarFire.Unpack(CarFire.Pack(cells), 0);
        Assert.Equal(cells.Length, back.Length);
        for (int i = 0; i < cells.Length; i++)
        {
            Assert.Equal(cells[i] > 0, back[i] > 0); // alight shows alight
            Assert.InRange(back[i] - cells[i], 0, 1.0 / ((1 << CarFire.Bits) - 1) + 1e-9);
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(6)]
    public void AtAStopTheNearestHandPutsTheFireFliesLampOutWhateverItsPart(int seed)
    {
        // Note 496: a crew of four's hands at a stop are the shunter and the winch pair, none a crate hand, and only a crate
        // hand ever went to trouble in a car: held on the main at frontier:7's Talbot Foundry, cutting the train and boarding
        // the cab, nobody put out the lamp the Fire Flies came to (seed 2: car 2, every car of the train alight by the end).
        // Now the nearest hand goes, whatever its part: the lamp's out, or the fire they lit is put out in its smoke.
        // Note 526 (seed 6, car 1): the nearest, not the first whose client saw the flies (that was the shunter at the switch,
        // 22 s off); and in at the side door to the room, not stood in the doorway pressing a lamp key that counts only in the
        // room, or walked between the doorway and the middle by the room's box while the car burned.
        // The nights as the director dealt them before the six of notes 362–367 (and the stops' own since) joined its roster: a kind more in the deal
        // reshuffles every night (seed 6 has no Fire Flies with them in), and these two are the cases the notes were written on.
        EnemyKind[] six = [EnemyKind.Mourners, EnemyKind.TowerJaw, EnemyKind.Brakeman, EnemyKind.Knotter, EnemyKind.FreightBeetle, EnemyKind.Hotbox,
            // And the yard's and the houses' (notes 583–586, 592): a stop's own, they change what the bots do there.
            EnemyKind.Pickers, EnemyKind.Lodger, EnemyKind.Householder, EnemyKind.HollowHouse, EnemyKind.Hanger];
        var enemies = Tuning.Enemies with
        {
            Director = Tuning.Enemies.Director with { Roster = [.. Enum.GetValues<EnemyKind>().Except(six).Select(Director.Key)] },
        };
        var flies = new Dictionary<int, int>(); // swarm → its car
        var lit = new HashSet<int>();           // cars alight (a fire past its smoke)
        CrewOfTwoTests.Night("frontier:7", 10, 520, null, bots: 4, seed: seed, enemies: enemies,
            upkeep: DataFile.Load<UpkeepTuning>(Path.Combine(DataFile.FindContentRoot(), UpkeepTuning.File)), each: world =>
            {
                foreach (var e in world.ActiveEnemies)
                    if (e is FireFlies { Gone: false } f)
                        flies.TryAdd(f.Id, f.Attached);
                    else if (e is CarFire { Gone: false, Phase: SpinePhase.Punish } fire)
                        lit.Add(fire.Attached);
            });
        Assert.NotEmpty(flies);
        Assert.True(!flies.Values.Any(lit.Contains), $"Fire Flies came to cars {string.Join(",", flies.Values)}; alight: {string.Join(",", lit)}");
    }
}
