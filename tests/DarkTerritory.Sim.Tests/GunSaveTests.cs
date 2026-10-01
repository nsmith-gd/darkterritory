using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// T103 (T93 playtest: "cannons should be moveable along top rails that connect between cars so a cannon can be saved
/// before decoupling a car"): the Car Hugger on the rear car is the car lost, cut loose or eaten through (App. A.3), so the
/// gunner pushes the guard gun up the rail onto the car ahead before it goes.
/// </summary>
public class GunSaveTests
{
    static readonly PlayerTuning P = Tuning.Player;

    [Fact]
    public void WithTheCarHuggerOnTheGuardVanTheGunnerPushesItsGunOntoTheCarAhead()
    {
        var n = new Night(4, speed: 8);
        int rear = n.Train.Dynamics.Consist.Vehicles[^1].Id, ahead = n.Train.VehicleAhead(rear);
        var mount = Guns.Mount(n.Train, rear)!.Value;
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, rear, mount.Position.Z + 0.7, P) with { Yaw = Math.PI };
        var hugger = n.World.AddEnemy(id => CarHugger.Lurking(id, n.Train.Dynamics.RearDistance + 1, 1, Tuning.Enemies.CarHugger));
        var gunner = new GunnerBot(Tuning.Combat.Guns);
        n.Run(0.5);
        Assert.True(hugger.Latched);
        Assert.True(n.Train.Vehicles[rear].HasGun);
        bool saving = false;
        for (int i = 0; i < 30 && n.Train.Vehicles[rear].HasGun; i++)
        {
            n.Run(1, id => gunner.Decide(n.Crew[id], n.World, n.World.Tick, out _));
            saving |= gunner.Saving;
        }
        Assert.True(saving);
        var g = n.Crew[1];
        Assert.False(n.Train.Vehicles[rear].HasGun, $"it's off the held car (gun at {n.Train.Vehicles[rear].Gun.Z:0.00}, gunner {g.Parent} {g.Surface} {g.Position} yaw {g.Yaw:0.00} hugger {hugger.Phase} latched {hugger.Latched})");
        Assert.True(n.Train.Vehicles[ahead].HasGun, "and on the one ahead");
    }

    [Fact]
    public void PushingAGunPastAHatchLadderDoesntTakeHoldOfIt()
    {
        // The guard van's hatch ladder comes up through the roof on the gun's rail: Use and walking is pushing, not climbing.
        var n = new Night(4, speed: 8);
        int rear = n.Train.Dynamics.Consist.Vehicles[^1].Id;
        var mount = Guns.Mount(n.Train, rear)!.Value;
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, rear, mount.Position.Z + 0.7, P) with { Yaw = 0 };
        n.Run(3, _ => new PlayerIntent { MoveZ = 1, Buttons = PlayerButtons.Use });
        Assert.Equal(Surface.Roof, n.Crew[1].Surface);
        Assert.True(n.Train.Vehicles[rear].Gun.Z < mount.Position.Z - 3, "it pushed the gun on past the ladder");
    }

    [Fact]
    public void WithNothingOnTheRearCarTheGunStaysPut()
    {
        var n = new Night(4, speed: 8);
        int rear = n.Train.Dynamics.Consist.Vehicles[^1].Id;
        var mount = Guns.Mount(n.Train, rear)!.Value;
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, rear, mount.Position.Z + 0.7, P) with { Yaw = Math.PI };
        var gunner = new GunnerBot(Tuning.Combat.Guns);
        double z = n.Train.Vehicles[rear].Gun.Z;
        n.Run(10, id => gunner.Decide(n.Crew[id], n.World, n.World.Tick, out _));
        Assert.True(n.Train.Vehicles[rear].HasGun);
        Assert.Equal(z, n.Train.Vehicles[rear].Gun.Z, 6);
    }
}
