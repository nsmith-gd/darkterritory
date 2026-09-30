using Ballast;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>T93 (playtest): the roof guns slide on rails along the roofs, and over a coupling onto the next car's.</summary>
public class GunRailTests
{
    static readonly TrainTuning T = Tuning.Train;
    static readonly PlayerTuning P = Tuning.Player;
    static readonly CombatTuning C = Tuning.Combat;
    const double Dt = SimConstants.TickSeconds;

    static World World(int cars = 4, double speed = 0)
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(20_000)]));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, cars, 1)), line, 5_000);
        train.Dynamics.Velocity = speed;
        var w = new World(train, C);
        w.EnableEnemies(Tuning.Enemies with { Director = Tuning.Enemies.Director with { GraceSeconds = 1e9, PaceSeconds = 1e9 } }, null, 1, crew: 1, authority: true);
        return w;
    }

    static int Guard(World w) => w.Train.Vehicles.First(v => v.Kind == VehicleKind.Guard).Id;

    /// <summary>Behind the gun on its car, facing up the train (the way you push it forward).</summary>
    static PlayerState BehindGun(World w, int vehicle)
    {
        var mount = Guns.Mount(w.Train, vehicle)!.Value;
        var s = PlayerMotor.SpawnOnRoof(w.Train, vehicle, mount.Position.Z + 0.7, P);
        s.Yaw = 0;
        return s;
    }

    /// <summary>The host's tick for one player: act, the train, then the motor.</summary>
    static void Walk(World w, ref PlayerState s, PlayerIntent intent, double seconds, double speed = 0)
    {
        for (int i = 0; i < seconds * SimConstants.TickRate; i++)
        {
            w.BeginTick();
            w.CrewAct(ref s, intent, 1);
            w.Train.Dynamics.Velocity = speed;
            w.Step(default);
            PlayerMotor.Step(ref s, intent, w.Train, P, T, Dt, applyLook: false);
        }
    }

    static readonly PlayerIntent Push = new() { MoveZ = 1, Buttons = PlayerButtons.Use };

    [Fact]
    public void UseHeldWalkingAtTheGunPushesItAlongTheRoofAtPushingPace()
    {
        var w = World(speed: 14);
        int guard = Guard(w);
        double z0 = w.Train.Vehicles[guard].Gun.Z;
        var s = BehindGun(w, guard);
        Walk(w, ref s, Push, 2, speed: 14);
        Assert.True(s.Has(PlayerFlags.Pushing));
        double moved = z0 - w.Train.Vehicles[guard].Gun.Z;
        Assert.InRange(moved, P.PushGun * 2 - 0.15, P.PushGun * 2 + 0.05);
        // The pusher kept up with it, so it's still theirs to push (and fire).
        Assert.Equal(guard, Guns.MannedGun(s, w.Train, C.Guns));
    }

    [Fact]
    public void WalkingWithoutUseLeavesTheGunWhereItIs()
    {
        var w = World();
        int guard = Guard(w);
        double z0 = w.Train.Vehicles[guard].Gun.Z;
        var s = BehindGun(w, guard);
        Walk(w, ref s, new PlayerIntent { MoveZ = 1 }, 1);
        Assert.Equal(z0, w.Train.Vehicles[guard].Gun.Z, 9);
    }

    [Fact]
    public void PushedPastTheEndItGoesOverTheCouplingOntoTheNextCarsRail()
    {
        var w = World();
        int guard = Guard(w), ahead = guard - 1;
        var rail = w.Train.Frames[guard].Shape.RoofRail!.Value;
        var s = BehindGun(w, guard);
        // Push it the length of the van, and it's over onto the car in front, at its rear end.
        Walk(w, ref s, Push, (w.Train.Vehicles[guard].Gun.Z - rail.Front) / P.PushGun + 0.5);
        Assert.False(w.Train.Vehicles[guard].HasGun);
        Assert.True(w.Train.Vehicles[ahead].HasGun);
        Assert.Equal(w.Train.Frames[ahead].Shape.RoofRail!.Value.Back, w.Train.Vehicles[ahead].Gun.Z, 6);
        // Still facing back down the line, and it fires from its new car.
        Assert.Equal(1, w.Train.Vehicles[ahead].Gun.Facing);
    }

    [Fact]
    public void ACutCouplingStopsItAtTheEndOfItsRail()
    {
        var w = World();
        int guard = Guard(w);
        w.Train.Uncouple(guard - 1);
        var rail = w.Train.Frames[guard].Shape.RoofRail!.Value;
        var s = BehindGun(w, guard);
        Walk(w, ref s, Push, (w.Train.Vehicles[guard].Gun.Z - rail.Front) / P.PushGun + 1.5);
        Assert.True(w.Train.Vehicles[guard].HasGun);
        Assert.Equal(rail.Front, w.Train.Vehicles[guard].Gun.Z, 6);
    }

    [Fact]
    public void ItWontGoOntoACarThatHasAGun()
    {
        var w = World();
        int guard = Guard(w);
        // The guard van's gun pushed up the train, car by car, to the first car behind the engine.
        for (int i = 0; i < 20 && !w.Train.Vehicles[1].HasGun; i++)
            Guns.Slide(w.Train, w.Train.Vehicles.First(v => v.HasGun && v.Id > 0).Id, -5);
        Assert.True(w.Train.Vehicles[1].HasGun);
        Assert.False(w.Train.Vehicles[guard].HasGun);
        // The engine's pushed back over its tender stops at its rail's end: the car behind has a gun already.
        var engineRail = w.Train.Frames[0].Shape.RoofRail!.Value;
        Guns.Slide(w.Train, 0, engineRail.Back - w.Train.Vehicles[0].Gun.Z + 1);
        Assert.True(w.Train.Vehicles[0].HasGun);
        Assert.Equal(engineRail.Back, w.Train.Vehicles[0].Gun.Z, 6);
    }

    [Fact]
    public void AGunPushedToAnotherCarFiresFromThere()
    {
        var w = World();
        int guard = Guard(w), ahead = guard - 1;
        Guns.Slide(w.Train, guard, -20);
        Assert.True(w.Train.Vehicles[ahead].HasGun);
        Guns.Arm(w.Train, C.Guns);
        Assert.True(w.Train.Vehicles[ahead].HasGun);
        var mount = Guns.Mount(w.Train, ahead)!.Value;
        var s = PlayerMotor.SpawnOnRoof(w.Train, ahead, mount.Position.Z - 0.7, P);
        s.Yaw = Math.PI;
        s.Pitch = 0.2;
        Assert.Equal(ahead, Guns.MannedGun(s, w.Train, C.Guns));
        w.BeginTick();
        w.CrewAct(ref s, new PlayerIntent { Buttons = PlayerButtons.Fire }, 1);
        Assert.Single(w.Shots);
        Assert.Equal(ahead, w.Shots[0].GunVehicle);
    }

    [Fact]
    public void WhereTheGunIsRoundTripsThroughTheSnapshot()
    {
        var w = World();
        int guard = Guard(w);
        Guns.Slide(w.Train, guard, -20);
        var controls = new TrainControls();
        var records = Net.WorldRecords.Quantise(w, ref controls, []);
        var mirror = World();
        var mc = new TrainControls();
        Net.WorldRecords.Apply(records, mirror, ref mc, []);
        for (int v = 0; v < w.Train.Vehicles.Count; v++)
        {
            Assert.Equal(w.Train.Vehicles[v].HasGun, mirror.Train.Vehicles[v].HasGun);
            Assert.Equal(w.Train.Vehicles[v].Gun.Z, mirror.Train.Vehicles[v].Gun.Z, 5);
            Assert.Equal(w.Train.Vehicles[v].Gun.Facing, mirror.Train.Vehicles[v].Gun.Facing);
        }
    }
}
