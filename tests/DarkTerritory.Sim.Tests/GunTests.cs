using Ballast;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>Spec B.7 guns and GDD App. A.6 Choir.</summary>
public class GunTests
{
    static readonly TrainTuning T = Tuning.Train;
    static readonly PlayerTuning P = Tuning.Player;
    static readonly CombatTuning C = Tuning.Combat;

    static World World(int cars = 10)
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(20_000)]));
        var w = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(T, cars, 1)), line, 5_000), C);
        // The host's (the loudness meter is the host's to keep), with nothing sent.
        w.EnableEnemies(Tuning.Enemies with { Director = Tuning.Enemies.Director with { GraceSeconds = 1e9, PaceSeconds = 1e9 } }, null, 1, crew: 1, authority: true);
        return w;
    }

    static readonly PlayerIntent Reload = new() { Buttons = PlayerButtons.Use };
    static double ReloadSeconds => C.Guns.ReloadSteps * C.Guns.ReloadStepSeconds + 0.2;

    /// <summary>A player standing just behind a gun's pedestal, facing the way it faces.</summary>
    static PlayerState AtGun(World w, int vehicle)
    {
        var mount = w.Train.Frames[vehicle].Shape.Gun!.Value;
        var s = PlayerMotor.SpawnOnRoof(w.Train, vehicle, mount.Position.Z - mount.Facing.Z * 0.7, P);
        s.Yaw = mount.Facing.Z < 0 ? 0 : Math.PI;
        return s;
    }

    static int GuardCar(World w) => w.Train.Vehicles.First(v => v.Kind == VehicleKind.Guard).Id;

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

    [Fact]
    public void TheEngineAndGuardCarCarryTheGuns()
    {
        var w = World();
        Assert.NotNull(w.Train.Frames[0].Shape.Gun);
        Assert.NotNull(w.Train.Frames[GuardCar(w)].Shape.Gun);
        Assert.Equal(10, GuardCar(w));
        Assert.All(w.Train.Vehicles.Where(v => v.HasGun), v => Assert.Equal(C.Guns.Ammo, v.Gun.Ammo));
    }

    [Fact]
    public void OneRoundThenAFullReloadByHand()
    {
        // GDD v1.1 App. C.3: "a full manual reload (powder, ball, ram, fire), so every shot is a timed decision".
        var w = World();
        var s = AtGun(w, 0);
        Assert.Equal(0, Guns.MannedGun(s, w.Train, C.Guns));
        Assert.Single(Hold(w, ref s, Fire, 5));
        Assert.Equal(C.Guns.ReloadSteps, w.Train.Vehicles[0].Gun.ReloadNeeded);
        // Part of the way through, and it still won't fire.
        Hold(w, ref s, Reload, C.Guns.ReloadStepSeconds * (C.Guns.ReloadSteps - 1) + 0.1);
        Assert.Empty(Hold(w, ref s, Fire, 1));
        Hold(w, ref s, Reload, C.Guns.ReloadStepSeconds + 0.2);
        Assert.Equal(0, w.Train.Vehicles[0].Gun.ReloadNeeded);
        Assert.Single(Hold(w, ref s, Fire, 1));
        Assert.Equal(C.Guns.Ammo - 2, w.Train.Vehicles[0].Gun.Ammo);
    }

    [Fact]
    public void AnEmptyGunFiresNothing()
    {
        var w = World();
        w.Train.Vehicles[0].Gun.Ammo = 1;
        var s = AtGun(w, 0);
        Assert.Single(Hold(w, ref s, Fire, 2));
        Hold(w, ref s, Reload, ReloadSeconds);
        Assert.Empty(Hold(w, ref s, Fire, 2));
    }

    [Fact]
    public void AJammedGunFiresNothing()
    {
        var w = World();
        w.Train.Vehicles[0].Gun.Jammed = true;
        var s = AtGun(w, 0);
        Assert.Empty(Hold(w, ref s, Fire, 2));
    }

    [Fact]
    public void YouHaveToBeAtTheGun()
    {
        var w = World();
        var s = PlayerMotor.SpawnOnRoof(w.Train, 3, 0, P);
        Assert.Null(Guns.MannedGun(s, w.Train, C.Guns));
        Assert.Empty(Hold(w, ref s, Fire, 2));
    }

    HitTarget Ahead(World w, double metres, double lateral = 0, double height = 5)
    {
        var t = w.Train.Line.Sample(w.Train.Dynamics.Distance + metres);
        var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
        return new HitTarget(7, t.Position + right * lateral + Double3.Up * height, 1.0);
    }

    [Theory]
    [InlineData(60, true)]
    [InlineData(90, false)]
    public void RangeIsEightyMetres(double metres, bool hits)
    {
        var w = World();
        var s = AtGun(w, 0);
        w.Targets.Add(Ahead(w, metres));
        var shot = Hold(w, ref s, Fire, 0.1).Single();
        Assert.Equal(hits, shot.HitTargetId == 7);
    }

    [Fact]
    public void AimingPastTheTraverseStopsTheGun()
    {
        var w = World();
        var s = AtGun(w, 0);
        s.Yaw = 120 * Math.PI / 180;
        Assert.Empty(Hold(w, ref s, Fire, 1));
        s.Yaw = 95 * Math.PI / 180;
        Assert.NotEmpty(Hold(w, ref s, Fire, 1));
    }

    [Fact]
    public void TheDeadZoneBlocksFiringAlongTheBodyEvenWithFullTraverse()
    {
        var mount = new GunMount(default, new Double3(0, 0, -1));
        var full = C.Guns with { TraverseDegrees = 360 };
        var alongBody = new Double3(Math.Sin(10 * Math.PI / 180), 0, Math.Cos(10 * Math.PI / 180));
        Assert.Equal(AimResult.DeadZone, Guns.CheckAim(mount, alongBody, full));
        var clear = new Double3(Math.Sin(30 * Math.PI / 180), 0, Math.Cos(30 * Math.PI / 180));
        Assert.Equal(AimResult.Ok, Guns.CheckAim(mount, clear, full));
    }

    [Fact]
    public void RoundsStopAtTheTrainsOwnBody()
    {
        // Aim down over the boiler: the round hits the engine before it goes anywhere.
        var w = World();
        var s = AtGun(w, 0);
        s.Pitch = -11 * Math.PI / 180;
        var shot = Hold(w, ref s, Fire, 0.1).Single();
        Assert.True(shot.BlockedByTrain);
        Assert.True(shot.Distance < 20);
    }

    [Fact]
    public void TheFlankCannotBeCoveredFromEitherGun()
    {
        // Spec B.7: the dead zone "is what makes the flank uncoverable regardless of train length...
        // a geometry fact, not a balance number". Try every aim from both guns at something beside each middle car.
        var w = World(10);
        var gunners = new[] { AtGun(w, 0), AtGun(w, GuardCar(w)) };
        // Not vacuous: each gunner can hit something in its own arc (ahead of the engine, behind the guard car).
        var aheadTarget = Ahead(w, 40, 0, 5);
        var behind = w.Train.Line.Sample(w.Train.Dynamics.RearDistance - 40).Position + Double3.Up * 5;
        foreach (var (gunner, target) in new[] { (gunners[0], aheadTarget), (gunners[1], new HitTarget(7, behind, 1.0)) })
        {
            var shot = Guns.TryFire(gunner, Fire, w.Train, C.Guns, ref w.Choir, C.Choir, [target], 0, 1);
            Assert.Equal(7, shot?.HitTargetId);
        }
        for (int car = 2; car <= 8; car++)
        {
            foreach (double side in new[] { -6.0, 6.0 })
            {
                var frame = w.Train.Frames[car];
                var target = new HitTarget(car, frame.ToWorld(new Double3(side, 2.5, 0)), 1.2);
                w.Targets.Clear();
                w.Targets.Add(target);
                foreach (var gunner in gunners)
                {
                    for (double yaw = -180; yaw < 180; yaw += 2)
                        for (double pitch = -10; pitch <= 40; pitch += 5)
                        {
                            var s = gunner with { Yaw = yaw * Math.PI / 180, Pitch = pitch * Math.PI / 180 };
                            w.Train.Vehicles[s.Parent].Gun.Cooldown = 0;
                            var shot = Guns.TryFire(s, Fire, w.Train, C.Guns, ref w.Choir, C.Choir, w.Targets, 0, 1);
                            Assert.False(shot?.HitTargetId == car, $"car {car} side {side} hit from vehicle {s.Parent} at yaw {yaw} pitch {pitch}");
                        }
                }
            }
        }
    }

    [Fact]
    public void EveryRoundFeedsTheMeterAndSilenceLetsItDrain()
    {
        // GDD v1.1 App. A.3: "every cannon shot feeds the loudness meter"; App. C.7: measured over a few seconds.
        var w = World();
        var s = AtGun(w, 0);
        Hold(w, ref s, Fire, 0.2);
        Assert.InRange(w.Choir.Loudness, 0.8 * C.Choir.RoundLoudness, C.Choir.RoundLoudness);
        Assert.True(w.Choir.Loudness >= C.Choir.Threshold);
        var idle = default(PlayerIntent);
        Hold(w, ref s, idle, 6 * C.Choir.WindowSeconds);
        Assert.True(w.Choir.Loudness < 0.05, $"{w.Choir.Loudness}");
        Assert.Equal(0, w.Choir.Build);
        Assert.Equal(ChoirPhase.Distant, w.Choir.Phase(C.Choir));
    }

    [Fact]
    public void SustainedFireBringsTheSwarm()
    {
        // Fire as fast as the reload allows, for a minute or two (T113: it takes buildSeconds held loud): the meter held loud gathers the Choir (App. A.7, C.7).
        var w = World();
        var s = AtGun(w, 0);
        for (int i = 0; i < 30 && !w.Choir.Present; i++)
        {
            Hold(w, ref s, Fire, 0.2);
            Hold(w, ref s, Reload, ReloadSeconds);
        }
        Assert.Equal(ChoirPhase.Swarm, w.Choir.Phase(C.Choir));
    }

    [Fact]
    public void TheCabHatchLeadsUpToTheForwardGun()
    {
        var w = World();
        var s = PlayerMotor.SpawnInCab(w.Train, P);
        var hatch = w.Train.Frames[0].Shape.Ladders.First(l => l.Foot.Y > 0);
        s.Position = hatch.Foot with { X = hatch.Foot.X + 0.3 };
        var climb = new PlayerIntent { MoveZ = 1, Buttons = PlayerButtons.Use };
        for (int i = 0; i < SimConstants.TickRate * 4 && s.Surface != Surface.Roof; i++)
        {
            w.Step(default);
            PlayerMotor.Step(ref s, climb, w.Train, P, T, SimConstants.TickSeconds);
        }
        Assert.Equal(Surface.Roof, s.Surface);
        Assert.Equal(T.Geometry.EngineHeight, s.Position.Y, 6);
    }
}
