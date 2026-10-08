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

    static readonly Enemies.EnemyTuning Quiet = Tuning.Enemies with { Director = Tuning.Enemies.Director with { GraceMinSeconds = 1e9, GraceMaxSeconds = 1e9 } };

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
    public void AnIdleGunnerKeepsTheGunLaidOnItsLane()
    {
        // Note 447: seated with nothing to shoot, the view drifted and the carriage stood at the end of its arc (95–100° round),
        // so the first hound in range cost the carriage's 1.4 s swing before a shot. Idle, it's laid along its facing.
        var n = new Night(4, 20);
        int van = Rear(n);
        n.Crew[1] = AtTheGun(n) with { Yaw = Math.PI / 2 };
        var gunner = new GunnerBot(G) { Me = 1 };
        n.Run(4, id => gunner.Decide(n.Crew[id], n.World, n.World.Tick, out _));
        Assert.True(n.Crew[1].Has(PlayerFlags.Seated));
        var gun = n.Train.Vehicles[van].Gun;
        Assert.True(Math.Abs(gun.Traverse) * 180 / Math.PI < 3, $"the carriage stands {gun.Traverse * 180 / Math.PI:0} degrees round");
    }

    [Fact]
    public void AGunnerKeepsItsGunWhileARunIsStillComingInWithAHoundAboard()
    {
        // Note 447: a hound aboard used to take both gunners off their guns to the pack fight, and the rest of the run came in
        // unanswered. With more coming in on the ground, the gun's theirs; only once nothing's coming does it go to the pack.
        var n = new Night(4, 20);
        int van = Rear(n);
        n.Crew[1] = AtTheGun(n);
        var gunner = new GunnerBot(G) { Me = 1 };
        n.Run(1, id => gunner.Decide(n.Crew[id], n.World, n.World.Tick, out _));
        var shape = n.Train.Frames[2].Shape;
        var aboard = n.World.AddEnemy(e => new Enemies.CinderHound(e, 900) { Health = Tuning.Enemies.CinderHounds.Health });
        aboard.Restore(Enemies.SpinePhase.Commit, 0, Tuning.Enemies.CinderHounds.Health, 2, new Double3(0.6, shape.RoofHeight, 0), 0, 0, 0, 900, 0);
        var coming = n.World.AddEnemy(e => new Enemies.CinderHound(e, 900) { Health = Tuning.Enemies.CinderHounds.Health });
        coming.Restore(Enemies.SpinePhase.Telegraph, 0, Tuning.Enemies.CinderHounds.Health, -1, default, n.Train.Dynamics.RearDistance - 300, 3, 0, 900, 0);
        n.Run(3, id => gunner.Decide(n.Crew[id], n.World, n.World.Tick, out _));
        Assert.True(n.Crew[1].Has(PlayerFlags.Seated) && Guns.MannedGun(n.Crew[1], n.Train, G) == van, $"off the gun: {n.Crew[1].Surface} on {n.Crew[1].Parent}");
    }

    [Fact]
    public void TheGuardGunnerDucksDownTheHatchUnderATrussDraggerAndIsBackAtTheGun()
    {
        // Note 448 (note 442's "not yet"): a seated gunner is on its car's roof, and a truss Dragger drops on whoever's on the
        // roof of the car passing under it. The guard gun's gunner bot, seated, at 20 m/s with a Dragger on a chord 150 m ahead:
        // down the hatch ladder below the roof before the van's under it, so it drops on nobody; and back in the seat after.
        var n = new Night(4, 20);
        int van = Rear(n);
        n.Crew[1] = AtTheGun(n);
        var gunner = new GunnerBot(G) { Me = 1 };
        n.Run(1, id => gunner.Decide(n.Crew[id], n.World, n.World.Tick, out _));
        Assert.True(n.Crew[1].Has(PlayerFlags.Seated));
        var dragger = n.World.AddEnemy(id => Enemies.Dragger.OnTruss(id, n.Train.Dynamics.Distance + 150, 1, Tuning.Enemies.Draggers.Drop));
        bool ducked = false, grabbed = false;
        for (int i = 0; i < 30 * SimConstants.TickRate && !(dragger.Gone && n.Crew[1].Has(PlayerFlags.Seated)); i++)
        {
            n.Run(1.0 / SimConstants.TickRate, id => gunner.Decide(n.Crew[id], n.World, n.World.Tick, out _));
            ducked |= gunner.Ducking && n.Crew[1].Surface == Surface.Ladder && n.Crew[1].Parent == van;
            grabbed |= dragger.Target == 1 || n.Crew[1].Has(PlayerFlags.Held);
        }
        var s = n.Crew[1];
        Assert.False(grabbed, "the Dragger dropped on the gunner");
        Assert.True(ducked, "never down the hatch");
        Assert.True(dragger.Gone);
        Assert.True(s.Alive, $"died of {s.Death}");
        Assert.True(s.Has(PlayerFlags.Seated) && Guns.MannedGun(s, n.Train, G) == van, $"not back at the gun: {s.Surface} on {s.Parent}");
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

    /// <summary>
    /// A 12-round string from <paramref name="gunCar"/>'s gun, its gunner bot seated at it, a shot every
    /// <paramref name="every"/> s (the round taken off as firing does), with a walker bot (id 2, starting on car 2's roof)
    /// bringing the powder. Returns the rounds fired before the rack ran dry (12: never), the walker's steps, and the charges.
    /// </summary>
    static (int Fired, List<string> Steps, int Charges) String(Night n, int gunCar, PlayerState gunnerAt, double every, bool asCrew = false)
    {
        n.Crew[1] = gunnerAt;
        n.Crew[2] = PlayerMotor.SpawnOnRoof(n.Train, 2, 0, P);
        var calls = new CrewCalls();
        var gunner = asCrew ? (GunnerBot)BotCrew.Make(1, 4, calls, Tuning.Combat, P, 1) : new GunnerBot(G);
        gunner.Me = 1;
        var walker = asCrew ? (RoofWalkerBot)BotCrew.Make(2, 4, calls, Tuning.Combat, P, 1) : new RoofWalkerBot(7);
        walker.Me = 2;
        walker.Calls = calls;
        PlayerIntent Think(int id)
        {
            List<(int, PlayerState)> others = [.. n.Crew.Where(c => c.Key != id).Select(c => (c.Key, c.Value))];
            if (id == 1)
            {
                gunner.Crew = others;
                return gunner.Decide(n.Crew[1], n.World, n.World.Tick, out _);
            }
            walker.Crew = others;
            return walker.Decide(n.Crew[2], n.World, n.World.Tick, out _);
        }
        n.Run(2, Think);
        Assert.True(n.Crew[1].Has(PlayerFlags.Seated), "the gunner took its seat");
        Assert.Equal(gunCar, Guns.MannedGun(n.Crew[1], n.Train, G));
        var steps = new List<string>();
        int fired = 0, charges = 0;
        ref var gun = ref n.Train.Vehicles[gunCar].Gun;
        for (int i = 0; fired < 12 && i < 12 * every * SimConstants.TickRate + 1; i++)
        {
            int before = Guns.Ready(n.Train.Vehicles[gunCar].Gun, G);
            n.Run(1.0 / SimConstants.TickRate, Think);
            if (Guns.Ready(n.Train.Vehicles[gunCar].Gun, G) > before)
                charges++;
            if (walker.FeedStep is { } step && (steps.Count == 0 || steps[^1] != step))
                steps.Add(step);
            if ((i + 1) % (int)(every * SimConstants.TickRate) == 0)
            {
                if (Guns.Ready(n.Train.Vehicles[gunCar].Gun, G) <= 0)
                    break;
                n.Train.Vehicles[gunCar].Gun.Ammo--;
                n.Train.Vehicles[gunCar].Gun.Rack--;
                fired++;
            }
        }
        Assert.True(n.Crew[1].Has(PlayerFlags.Seated), $"the gunner left its seat for powder ({gunner.PowderStep}) after {fired}: {string.Join(" > ", steps)}");
        return (fired, steps, charges);
    }

    [Fact]
    public void AWalkerBringsTheGuardGunItsPowderAndTheGunnerKeepsFiring()
    {
        // Note 377 (orchestrator.md §5.1 U4's two-person gun): the rack at 2, a walker goes down for a charge and fills it
        // from beside the gun, so a 12-round string (twice the rack) never runs it dry and the gunner never leaves the seat.
        var n = new Night(4, 12);
        int van = Rear(n);
        var (fired, steps, charges) = String(n, van, AtTheGun(n), every: 6);
        Assert.True(fired == 12, $"ran dry after {fired}: {string.Join(" > ", steps)}; walker at {n.Crew[2].Parent}/{n.Crew[2].Surface} {n.Crew[2].Position}");
        Assert.True(charges >= 1, $"{charges} charges");
        Assert.Contains("take", steps);
        Assert.Contains("charge", steps);
    }

    [Fact]
    public void AsTheHarnessMakesThemTheWalkerStillBringsIt()
    {
        var n = new Night(4, 12);
        var (fired, steps, charges) = String(n, Rear(n), AtTheGun(n), every: 6, asCrew: true);
        Assert.True(fired == 12, $"ran dry after {fired}: {string.Join(" > ", steps)}; walker at {n.Crew[2].Parent}/{n.Crew[2].Surface} {n.Crew[2].Position}");
    }

    [Fact]
    public void AWalkerCarriesTheEngineGunsPowderTheLengthOfTheTrain()
    {
        // The forward gun's share is in the guard van's locker too: a walk back along the roofs, down and up, and forward to
        // the cab roof, with the charge. (The director quiet: it's the walk that's tested, not what comes at it on the way.)
        var n = new Night(4, 12, enemies: Quiet);
        var mount = Guns.Mount(n.Train, 0)!.Value;
        var seat = PlayerMotor.SpawnOnRoof(n.Train, 0, mount.Position.Z - mount.Facing.Z * 0.8, P) with { Yaw = 0 };
        var (fired, steps, charges) = String(n, 0, seat, every: 12);
        Assert.True(fired == 12, $"ran dry after {fired}: {string.Join(" > ", steps)}; walker at {n.Crew[2].Parent}/{n.Crew[2].Surface} {n.Crew[2].Position}");
        Assert.True(charges >= 2, $"{charges} charges");
    }

    [Fact]
    public void OnlyOneWalkerGoesAndAGunnerWithAChargeOnItsWayWaitsForIt()
    {
        // Who goes is the lowest id among the walkers, worked out alike by all; whoever has a charge in hand is it.
        var n = new Night(4, 12);
        int van = Rear(n);
        n.Crew[1] = AtTheGun(n);
        var gunner = new GunnerBot(G) { Me = 1 };
        n.Run(2, id => gunner.Decide(n.Crew[id], n.World, n.World.Tick, out _));
        Assert.True(n.Crew[1].Has(PlayerFlags.Seated));
        List<(int, PlayerState)> crew = [(1, n.Crew[1]), (3, PlayerMotor.SpawnOnRoof(n.Train, 2, 0, P)), (2, PlayerMotor.SpawnOnRoof(n.Train, 3, 0, P))];
        Assert.Null(PowderCarry.Carrier(n.World, crew, G));
        n.Train.Vehicles[van].Gun.Rack = G.FeedAt;
        Assert.Equal(van, PowderCarry.Wanting(n.World, crew.Select(c => c.Item2), G));
        Assert.Equal(2, PowderCarry.Carrier(n.World, crew, G));
        Assert.Equal(3, PowderCarry.Carrier(n.World, crew, G, id => id == 3));
        // Walker 3 with a charge in hand: it's the one, whoever's lower.
        n.World.Bodies.SpawnCrate(n.Train, 2, new Double3(0, 0, 0), BodyKind.Powder).Carrier = 3;
        Assert.Equal(3, PowderCarry.Carrier(n.World, crew, G));
        // The gunner, its rack dry, waits for it instead of going down itself.
        n.Train.Vehicles[van].Gun.Rack = 0;
        gunner.Crew = crew.Where(c => c.Item1 != 1).ToList();
        n.Run(1, id => gunner.Decide(n.Crew[id], n.World, n.World.Tick, out _));
        Assert.Null(gunner.PowderStep);
        Assert.True(n.Crew[1].Has(PlayerFlags.Seated));
    }
}
