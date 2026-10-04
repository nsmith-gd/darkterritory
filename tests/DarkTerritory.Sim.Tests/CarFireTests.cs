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
        // Beside the fire, facing it, Fire held: it's knocked back until it's out.
        n.Crew[1] = n.Crew[1] with { Position = new Double3(room.Centre.X, room.Min.Y, fire.Local.Z + 1.2), Yaw = 0 };
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
    public void TheDriverClubsAStokerOutOfTheFireboxBeforeTheBoilerGoes()
    {
        // v1.1 App. A.5: "open the firebox and club it". The driver bot at the door, swinging, brake held.
        var n = new Night(4, speed: 0, boiler: true);
        n.Train.Boiler.Pressure = 80;
        var driver = new Bots.ConductorBot(null, 0);
        n.Crew[1] = PlayerMotor.SpawnInCab(n.Train, P);
        var stoker = n.World.AddEnemy(id => Stoker.InFirebox(id, n.Train, false, Tuning.Enemies.Stoker));
        for (int s = 0; s < 40 && !stoker.Gone; s++)
            n.Run(1, id => driver.Decide(n.Crew[id], n.World, n.World.Tick, out _), holdSpeed: false);
        Assert.True(stoker.Gone, $"{stoker.Phase} hp {stoker.Health}");
        Assert.False(n.Train.Boiler.Ruptured);
        Assert.True(n.Crew[1].Alive);
        Assert.True(n.Crew[1].Health < P.Health); // it burns
    }

    [Fact]
    public void AWalkerPutsASwarmedLampOutBeforeTheFliesSetTheCarAlight()
    {
        // v1.1 App. A.5: "lamps off when they swarm". Car 3's lamp lit, the flies on it, a walker on car 2's roof.
        var n = new Night(5, speed: 10);
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
        // The cruise (14 m/s) is under the flies' pull-away speed (15): nobody in the car, the driver puts the train over it
        // until they've gone, and the car never catches.
        var n = new Night(5, speed: 14, boiler: true);
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
        // And back down to the cruise after.
        Drive(40);
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
        fire.Extra = 0.97;
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
        fire.Extra = 0.97;
        n.Run(20);
        Assert.True(n.Train.Vehicles[3].Integrity > 0);
        Assert.DoesNotContain(n.World.Impacts, i => i.Shooter == -1);
    }
}
