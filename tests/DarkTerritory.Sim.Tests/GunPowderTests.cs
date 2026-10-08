using Ballast;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Powder to the guns (note 374; orchestrator.md §5.1 U4; GDD App. F.3, the director, 7 Oct 2026: "on the train, still
/// relatively boring from point A to point B"): a gun fires what's in its ready rack; the rest of the night's powder is in
/// the guard van's powder locker, carried up a charge at a time.
/// </summary>
public class GunPowderTests
{
    static readonly GunTuning G = Tuning.Combat.Guns;
    static readonly PlayerTuning P = Tuning.Player;

    static int Rear(Night n) => n.Train.Dynamics.Consist.Vehicles[^1].Id;

    /// <summary>Inside the guard van, at its powder locker.</summary>
    static PlayerState AtTheLocker(Night n)
    {
        int van = Rear(n);
        var at = Guns.Locker(n.Train.Frames[van].Shape)!.Value;
        return new PlayerState
        {
            Parent = van,
            Position = at + new Double3(0.6, 0.05, 0.4),
            Surface = Surface.Deck,
            Health = P.Health,
            LineHint = n.Train.Cars[van].FrontDistance,
        };
    }

    /// <summary>On the guard van's roof behind its gun, within reach of it.</summary>
    static PlayerState AtTheGun(Night n)
    {
        int van = Rear(n);
        var mount = Guns.Mount(n.Train, van)!.Value;
        return PlayerMotor.SpawnOnRoof(n.Train, van, mount.Position.Z - mount.Facing.Z * 0.8, P) with { Yaw = Math.PI };
    }

    [Fact]
    public void EachGunLeavesWithItsRackFullAndTheRestInTheLocker()
    {
        Assert.True(G.Rack > 0 && G.Rack < G.Ammo);
        var n = new Night(4, 0);
        foreach (var v in n.Train.Vehicles.Where(v => v.HasGun))
        {
            Assert.Equal(G.Ammo, v.Gun.Ammo);
            Assert.Equal(G.Rack, Guns.Ready(v.Gun, G));
        }
        // The guard van has the locker, the engine none; both guns' powder is in it.
        Assert.NotNull(Guns.Locker(n.Train.Frames[Rear(n)].Shape));
        Assert.Null(Guns.Locker(n.Train.Frames[0].Shape));
        Assert.Equal((G.Ammo - G.Rack) * n.Train.Vehicles.Count(v => v.HasGun), Guns.Stowed(n.Train, G));
    }

    [Fact]
    public void TheRackRunDryTheGunHasNothingToLoadOrFire()
    {
        var n = new Night(4, 0);
        ref var gun = ref n.Train.Vehicles[Rear(n)].Gun;
        gun.Rack = 1;
        var s = AtTheGun(n);
        var seat = new PlayerIntent { Actions = PlayerActions.Seat };
        n.Crew[1] = s;
        n.Run(0.2, _ => seat);
        Assert.True(n.Crew[1].Has(PlayerFlags.Seated));
        n.Run(0.1, _ => new PlayerIntent { Buttons = PlayerButtons.Fire });
        Assert.Single(n.Shots);
        gun = ref n.Train.Vehicles[Rear(n)].Gun;
        Assert.Equal(0, Guns.Ready(gun, G));
        Assert.Equal(0, gun.ReloadNeeded);
        Assert.Equal(G.Ammo - 1, gun.Ammo);
        n.Run(2, _ => new PlayerIntent { Buttons = PlayerButtons.Fire });
        Assert.Single(n.Shots);
    }

    [Fact]
    public void ACharge_TakenFromTheLockerAndHeldAtTheGun_FillsItsRack()
    {
        var n = new Night(4, 0);
        int van = Rear(n);
        n.Train.Vehicles[van].Gun.Rack = 0;
        n.Crew[1] = AtTheLocker(n);
        Assert.Equal(van, Guns.AtLocker(n.Crew[1], n.Train, G));
        n.Run(2.0 / SimConstants.TickRate, _ => new PlayerIntent { Buttons = PlayerButtons.Use });
        var charge = Assert.Single(n.World.Bodies.All, b => b.Kind == BodyKind.Powder);
        Assert.Equal(1, charge.Carrier);
        // Up at the gun with it: Use held, and not before its time.
        n.Crew[1] = AtTheGun(n);
        var use = new PlayerIntent { Buttons = PlayerButtons.Use };
        n.Run(G.ChargeSeconds - 0.3, _ => use);
        Assert.Equal(0, Guns.Ready(n.Train.Vehicles[van].Gun, G));
        n.Run(0.5, _ => use);
        var gun = n.Train.Vehicles[van].Gun;
        Assert.Equal(G.Rack, Guns.Ready(gun, G));
        Assert.Equal(G.Ammo, gun.Ammo);
        // Emptied, it needs its powder, ball and ram again before it fires; and the charge is spent.
        Assert.True(gun.ReloadNeeded > 0);
        Assert.DoesNotContain(n.World.Bodies.All, b => b.Kind == BodyKind.Powder);
    }

    [Fact]
    public void TheEngineGunsShareComesFromTheLockersPool()
    {
        var n = new Night(4, 0);
        int van = Rear(n);
        // The engine gun has fired everything it was stocked with but its rack; the guard van's locker has the guard gun's.
        ref var engine = ref n.Train.Vehicles[0].Gun;
        Assert.True(n.Train.Vehicles[0].HasGun);
        engine.Ammo = 0;
        engine.Rack = 0;
        Assert.Equal(G.Rack, Guns.Fill(n.Train, 0, G));
        Assert.Equal(G.Rack, n.Train.Vehicles[0].Gun.Ammo);
        Assert.Equal(G.Ammo - G.Rack, n.Train.Vehicles[van].Gun.Ammo);
        // Nothing left anywhere: no charge to be had.
        foreach (var v in n.Train.Vehicles.Where(v => v.HasGun))
            v.Gun.Ammo = Guns.Ready(v.Gun, G);
        Assert.Equal(0, Guns.Stowed(n.Train, G));
        n.Crew[1] = AtTheLocker(n);
        n.Run(2.0 / SimConstants.TickRate, _ => new PlayerIntent { Buttons = PlayerButtons.Use });
        Assert.DoesNotContain(n.World.Bodies.All, b => b.Kind == BodyKind.Powder);
    }

    [Fact]
    public void WithNoRackTheGunHasItsWholeStockAsBefore()
    {
        var t = G with { Rack = 0 };
        var gun = new GunState { Mounted = true, Ammo = 24 };
        Assert.Equal(24, Guns.Ready(gun, t));
        Assert.Equal(0, Guns.Stowed(gun, t));
    }

    [Fact]
    public void TheGunnerGoesDownForPowderAndBringsItUp()
    {
        // The guard gun's rack run dry, its gunner in the seat: up, down the hatch ladder beside the gun, along the van to
        // the locker, a charge, back up the ladder and the rack filled at the gun.
        var n = new Night(4, 12);
        int van = Rear(n);
        n.Crew[1] = AtTheGun(n);
        var gunner = new GunnerBot(G) { Me = 1 };
        n.Run(1, id => gunner.Decide(n.Crew[id], n.World, n.World.Tick, out _));
        Assert.Equal(van, Guns.MannedGun(n.Crew[1], n.Train, G));
        n.Train.Vehicles[van].Gun.Rack = 0;
        var steps = new List<string>();
        double? filled = null;
        for (int i = 0; i < 90 * SimConstants.TickRate && filled is null; i++)
        {
            n.Run(1.0 / SimConstants.TickRate, id => gunner.Decide(n.Crew[id], n.World, n.World.Tick, out _));
            if (gunner.PowderStep is { } step && (steps.Count == 0 || steps[^1] != step))
                steps.Add(step);
            if (Guns.Ready(n.Train.Vehicles[van].Gun, G) == G.Rack)
                filled = (i + 1) * SimConstants.TickSeconds;
        }
        Assert.True(filled is not null, $"never filled: {string.Join(" > ", steps)}; at {n.Crew[1].Parent}/{n.Crew[1].Surface} {n.Crew[1].Position}");
        Assert.Equal(["to the hatch", "down", "to the locker", "take", "to the ladder", "up", "to the gun", "charge"], steps.Where(x => x != "along"));
        // A walk of a few seconds each way: the rack's back inside half a minute.
        Assert.True(filled < 30, $"filled at {filled:0.0} s");
    }
}
