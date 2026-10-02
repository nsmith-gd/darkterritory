using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>GDD §23's failure table, "Cannon fouls: someone clears it by hand, under fire" (spec B.7).</summary>
public class FoulTests
{
    static readonly TrainTuning T = Tuning.Train;
    static readonly PlayerTuning P = Tuning.Player;
    static readonly CombatTuning C = Tuning.Combat;
    /// <summary>Every pull misfires: the foul under test, not its chance.</summary>
    static readonly CombatTuning Always = C with { Guns = C.Guns with { FoulChance = 1 } };
    static readonly CombatTuning Never = C with { Guns = C.Guns with { FoulChance = 0 } };

    static World World(CombatTuning combat, ulong seed = 1)
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(20_000)]));
        var w = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 4, 1)), line, 5_000), combat);
        w.EnableEnemies(Tuning.Enemies with { Director = Tuning.Enemies.Director with { GraceSeconds = 1e9, PaceSeconds = 1e9 } }, null, seed, crew: 1, authority: true);
        return w;
    }

    /// <summary>A player standing just behind a gun's pedestal, facing the way it faces.</summary>
    static PlayerState AtGun(World w, int vehicle)
    {
        var mount = w.Train.Frames[vehicle].Shape.Gun!.Value;
        var s = PlayerMotor.SpawnOnRoof(w.Train, vehicle, mount.Position.Z - mount.Facing.Z * 0.7, P);
        s.Yaw = mount.Facing.Z < 0 ? 0 : Math.PI;
        return s;
    }

    static List<GunShot> Hold(World w, ref PlayerState s, PlayerIntent intent, double seconds)
    {
        var shots = new List<GunShot>();
        for (int i = 0; i < seconds * SimConstants.TickRate; i++)
        {
            w.BeginTick();
            w.CrewAct(ref s, intent, 1);
            shots.AddRange(w.Shots);
            w.Step(default);
        }
        return shots;
    }

    static readonly PlayerIntent Fire = new() { Buttons = PlayerButtons.Fire };
    static readonly PlayerIntent Use = new() { Buttons = PlayerButtons.Use };

    [Fact]
    public void TheSpecsNumbers()
    {
        // Spec B.7: a misfire one pull in about thirty, cleared in six seconds at the gun.
        Assert.Equal(0.03, C.Guns.FoulChance);
        Assert.Equal(6, C.Guns.FoulClearSeconds);
    }

    [Fact]
    public void RareEnoughToBeACascadeNotAChore()
    {
        // A few percent a pull: a gun that fires all its shot in a night fouls at least once about one night in two.
        Assert.InRange(C.Guns.FoulChance, 0.01, 0.05);
        Assert.InRange(1 - Math.Pow(1 - C.Guns.FoulChance, C.Guns.Ammo), 0.35, 0.65);
        // Clearing one is longer than a reload and far short of mending the boiler: a bad moment, not a stop.
        Assert.InRange(C.Guns.FoulClearSeconds, C.Guns.ReloadSteps * C.Guns.ReloadStepSeconds, Tuning.Boiler.RepairSeconds);
    }

    [Fact]
    public void AMisfireIsADeadClickThatFoulsTheGun()
    {
        var w = World(Always);
        var s = AtGun(w, 0);
        Assert.Empty(Hold(w, ref s, Fire, 2));
        var gun = w.Train.Vehicles[0].Gun;
        Assert.True(gun.Jammed);
        // The one pull that fouled it; pulling at a fouled gun does nothing more.
        Assert.Equal(1, gun.Fouls);
        // Nothing went: the charge is still in it, nothing to reload, and nothing for the Choir to hear.
        Assert.Equal(C.Guns.Ammo, gun.Ammo);
        Assert.Equal(0, gun.ReloadNeeded);
        Assert.Equal(0u, gun.LastShotTick);
        Assert.Equal(0, w.Choir.Loudness, 6);
    }

    [Fact]
    public void SomeoneAtTheGunClearsItByHand()
    {
        var w = World(Always);
        var s = AtGun(w, 0);
        Hold(w, ref s, Fire, 0.2);
        Assert.True(w.Train.Vehicles[0].Gun.Jammed);
        // Most of the way, then let go: it starts over (the reload's rule).
        Hold(w, ref s, Use, C.Guns.FoulClearSeconds - 0.5);
        Assert.True(w.Train.Vehicles[0].Gun.Jammed);
        Assert.True(w.Train.Vehicles[0].Gun.ReloadProgress > 0);
        Hold(w, ref s, default, 0.1);
        Assert.Equal(0, w.Train.Vehicles[0].Gun.ReloadProgress);
        Hold(w, ref s, Use, C.Guns.FoulClearSeconds - 0.5);
        Assert.True(w.Train.Vehicles[0].Gun.Jammed);
        Hold(w, ref s, Use, 0.6);
        Assert.False(w.Train.Vehicles[0].Gun.Jammed);
        // Cleared, the charge that was in it goes at the next pull (this one doesn't misfire).
        w.Combat = Never;
        Assert.Single(Hold(w, ref s, Fire, 0.5));
        Assert.Equal(C.Guns.Ammo - 1, w.Train.Vehicles[0].Gun.Ammo);
    }

    [Fact]
    public void AFouledGunWontBePushedAlongItsRail()
    {
        // Use at a fouled gun clears it, walking or not: it isn't pushed off along the rail with the foul in it (T93).
        var w = World(Always);
        var s = AtGun(w, 0);
        Hold(w, ref s, Fire, 0.2);
        Assert.False(Guns.Pushing(s, new PlayerIntent { MoveZ = 1, Buttons = PlayerButtons.Use }, w.Train, Always.Guns));
    }

    [Fact]
    public void TheRollIsAHashSoEveryMachinePullsTheSameTrigger()
    {
        // The host and a client predicting its own pull roll the same (same seed, car, shot left, fouls so far); about
        // foulChance of all pulls misfire; and a gun just cleared (one more foul) rolls afresh.
        int fouled = 0, pulls = 0, again = 0;
        for (ulong seed = 1; seed <= 50; seed++)
            for (int vehicle = 0; vehicle < 12; vehicle++)
                for (int ammo = 1; ammo <= C.Guns.Ammo; ammo++)
                {
                    var g = new GunState { Mounted = true, Ammo = ammo };
                    bool misfires = Guns.Misfires(seed, vehicle, g, C.Guns);
                    Assert.Equal(misfires, Guns.Misfires(seed, vehicle, g, C.Guns));
                    pulls++;
                    if (!misfires)
                        continue;
                    fouled++;
                    if (Guns.Misfires(seed, vehicle, g with { Fouls = 1 }, C.Guns))
                        again++;
                }
        Assert.InRange(fouled / (double)pulls, C.Guns.FoulChance * 0.85, C.Guns.FoulChance * 1.15);
        Assert.True(again < fouled * 0.2, $"{again} of {fouled} misfired again");
    }

    [Fact]
    public void AGunnerFiringItsWholeStockClearsWhatFoulsAndFiresItAll()
    {
        // The content's chance, a night's worth of pulls: on some seed a gun fouls, and it's cleared and goes on firing.
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var w = World(C, seed);
            var s = AtGun(w, 0);
            int shots = 0;
            for (int i = 0; i < 400 && w.Train.Vehicles[0].Gun.Ammo > 0; i++)
            {
                var g = w.Train.Vehicles[0].Gun;
                shots += Hold(w, ref s, g.Jammed || g.ReloadNeeded > 0 ? Use : Fire, 0.5).Count;
            }
            var gun = w.Train.Vehicles[0].Gun;
            Assert.Equal(0, gun.Ammo);
            Assert.Equal(C.Guns.Ammo, shots);
            if (gun.Fouls > 0)
                return;
        }
        Assert.Fail("no gun fouled in twenty nights of firing all its shot");
    }

    [Fact]
    public void ABotGunnerClearsAFoulLikeAReload()
    {
        // A bot-crewed night mustn't stall on a fouled gun: the gunner clears it by hand, the same Use at the gun.
        var n = new Night(4, speed: 8);
        int rear = n.Train.Dynamics.Consist.Vehicles[^1].Id;
        var mount = Guns.Mount(n.Train, rear)!.Value;
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, rear, mount.Position.Z + 0.7, P) with { Yaw = Math.PI };
        n.Train.Vehicles[rear].Gun.Jammed = true;
        var gunner = new GunnerBot(C.Guns);
        n.Run(C.Guns.FoulClearSeconds + 0.5, id => gunner.Decide(n.Crew[id], n.World, n.World.Tick, out _));
        Assert.False(n.Train.Vehicles[rear].Gun.Jammed);
        Assert.True(n.Train.Vehicles[rear].HasGun);
    }

    [Fact]
    public void AFoulReachesEveryClient()
    {
        var w = World(Always);
        var s = AtGun(w, 0);
        Hold(w, ref s, Fire, 0.2);
        Hold(w, ref s, Use, 1);
        var client = World(Always);
        var controls = new TrainControls();
        Net.WorldRecords.Apply(Net.WorldRecords.Capture(w, controls, []), client, ref controls, []);
        var gun = client.Train.Vehicles[0].Gun;
        Assert.True(gun.Jammed);
        Assert.Equal(1, gun.Fouls);
        Assert.Equal(w.Train.Vehicles[0].Gun.ReloadProgress, gun.ReloadProgress, 6);
    }
}
