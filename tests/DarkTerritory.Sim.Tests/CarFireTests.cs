using Ballast;
using DarkTerritory.Sim.Enemies;
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
