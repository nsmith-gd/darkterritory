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
    // The night's stock of shot (note 374), as the guns were before note 689 made their shot endless.
    static readonly CombatTuning Finite = C with { Guns = C.Guns with { InfiniteAmmo = false } };

    static World World(int cars = 10, CombatTuning? combat = null)
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(20_000)]));
        var w = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(T, cars, 1)), line, 5_000), combat ?? C);
        // The host's (the loudness meter is the host's to keep), with nothing sent.
        w.EnableEnemies(Tuning.Enemies with { Director = Tuning.Enemies.Director with { GraceMinSeconds = 1e9, GraceMaxSeconds = 1e9 } }, null, 1, crew: 1, authority: true);
        return w;
    }

    static readonly PlayerIntent Reload = new() { Buttons = PlayerButtons.Use };
    static double ReloadSeconds => C.Guns.ReloadSteps * C.Guns.ReloadStepSeconds + 0.2;

    /// <summary>A player sat in a gun's seat (T112), facing the way it faces.</summary>
    static PlayerState AtGun(World w, int vehicle)
    {
        var s = StandingAtGun(w, vehicle);
        s.Flags |= PlayerFlags.Seated;
        return s;
    }

    /// <summary>A player standing just behind a gun's pedestal, facing the way it faces.</summary>
    static PlayerState StandingAtGun(World w, int vehicle)
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
        // Note 689 (the director, 13 Oct: "infinite ammo"): two shots took nothing from the gun's stock.
        Assert.True(C.Guns.InfiniteAmmo);
        Assert.Equal(C.Guns.Ammo, w.Train.Vehicles[0].Gun.Ammo);
    }

    // ---- One can, two do it faster (note 752; GDD §12 Fluidity; the director, 14 Oct 2026: "absolutely the right direction").

    /// <summary>A crewmate standing beside a gun's pivot (off its seat), within reach, turned to it.</summary>
    static PlayerState Beside(World w, int vehicle, double side = 1)
    {
        var mount = Guns.Mount(w.Train, vehicle)!.Value;
        var s = PlayerMotor.SpawnOnRoof(w.Train, vehicle, mount.Position.Z, P, side * 0.7);
        s.Yaw = side * Math.PI / 2;
        return s;
    }

    /// <summary>A gun on <paramref name="vehicle"/> fired, and how many ticks its reload takes with these crewmates acting (in this order).</summary>
    static int TicksToLoad(World w, int vehicle, params (int Id, PlayerState State, PlayerIntent Intent)[] crew)
    {
        w.Train.Vehicles[vehicle].Gun.ReloadNeeded = C.Guns.ReloadSteps;
        w.Train.Vehicles[vehicle].Gun.ReloadProgress = 0;
        var states = crew.Select(c => c.State).ToArray();
        for (int tick = 1; tick <= 20 * SimConstants.TickRate; tick++)
        {
            w.BeginTick();
            for (int i = 0; i < crew.Length; i++)
                w.CrewAct(ref states[i], crew[i].Intent, crew[i].Id);
            w.Step(default);
            if (w.Train.Vehicles[vehicle].Gun.ReloadNeeded == 0)
                return tick;
        }
        return -1;
    }

    static int SoloTicks => (int)Math.Round(C.Guns.ReloadSteps * C.Guns.ReloadStepSeconds * SimConstants.TickRate);

    [Fact]
    public void AGunnerAloneReloadsInTheTimeItAlwaysTook()
    {
        var w = World();
        var gunner = AtGun(w, 0);
        Assert.InRange(TicksToLoad(w, 0, (1, gunner, Reload)), SoloTicks, SoloTicks + C.Guns.ReloadSteps);
        // And standing at it, out of the seat, the same.
        Assert.InRange(TicksToLoad(w, 0, (1, Beside(w, 0), Reload)), SoloTicks, SoloTicks + C.Guns.ReloadSteps);
    }

    [Fact]
    public void ALoaderBesideAMannedGunReloadsItWhileTheGunnerAims()
    {
        // The gunner keeps the seat and aims (no Use), and their aiming doesn't start the loader's step over.
        var w = World();
        var gunner = AtGun(w, 0);
        var loader = Beside(w, 0);
        Assert.Equal(0, Guns.MannedGun(loader, w.Train, C.Guns));
        Assert.False(loader.Has(PlayerFlags.Seated));
        Assert.InRange(TicksToLoad(w, 0, (1, gunner, default), (2, loader, Reload)), SoloTicks, SoloTicks + C.Guns.ReloadSteps);
        Assert.InRange(TicksToLoad(w, 0, (1, loader, Reload), (2, gunner, default)), SoloTicks, SoloTicks + C.Guns.ReloadSteps);
    }

    [Fact]
    public void GunnerAndLoaderTogetherReloadAtTheLoaderRateAndAThirdAddsNothing()
    {
        Assert.True(C.Guns.LoaderRate > 1);
        int pair = (int)Math.Round(SoloTicks / C.Guns.LoaderRate);
        var w = World();
        var gunner = AtGun(w, 0);
        var loader = Beside(w, 0);
        int both = TicksToLoad(w, 0, (1, gunner, Reload), (2, loader, Reload));
        Assert.InRange(both, pair - C.Guns.ReloadSteps, pair + C.Guns.ReloadSteps);
        // Whoever's turn comes first.
        Assert.Equal(both, TicksToLoad(w, 0, (1, loader, Reload), (2, gunner, Reload)));
        // A third pair of hands, on the gun's other side: no faster.
        Assert.Equal(both, TicksToLoad(w, 0, (1, gunner, Reload), (2, loader, Reload), (3, Beside(w, 0, side: -1), Reload)));
        // With loaderRate 1 (off), two are as one.
        var off = World(combat: C with { Guns = C.Guns with { LoaderRate = 1 } });
        Assert.InRange(TicksToLoad(off, 0, (1, AtGun(off, 0), Reload), (2, Beside(off, 0), Reload)), SoloTicks, SoloTicks + C.Guns.ReloadSteps);
    }

    [Fact]
    public void LetGoByEveryHandTheStepStartsOver()
    {
        var w = World();
        var gunner = AtGun(w, 0);
        var loader = Beside(w, 0);
        w.Train.Vehicles[0].Gun.ReloadNeeded = C.Guns.ReloadSteps;
        for (int i = 0; i < SimConstants.TickRate / 2; i++)
        {
            w.BeginTick();
            w.CrewAct(ref gunner, default, 1);
            w.CrewAct(ref loader, Reload, 2);
            w.Step(default);
        }
        Assert.True(w.Train.Vehicles[0].Gun.ReloadProgress > 0.4);
        w.BeginTick();
        w.CrewAct(ref gunner, default, 1);
        w.CrewAct(ref loader, default, 2);
        w.Step(default);
        Assert.Equal(0, w.Train.Vehicles[0].Gun.ReloadProgress);
        Assert.Equal(C.Guns.ReloadSteps, w.Train.Vehicles[0].Gun.ReloadNeeded);
    }

    [Fact]
    public void AHandMeantForTheHatchBesideTheGunIsntTheGuns()
    {
        // Use works the thing looked at (note 267; note 752): the guard van's gun pushed onto the car ahead and along to its
        // roof hatch, a loader beside it looking down at the hatch's lid works the lid, not the reload; looking level at the
        // gun, the reload.
        var w = World();
        Guns.Slide(w.Train, GuardCar(w), -100);
        int g = w.Train.Vehicles.First(v => v.HasGun && v.Id != 0).Id;
        Assert.NotEqual(GuardCar(w), g);
        var hatch = w.Train.Frames[g].Shape.Interactables.First(i => i.Kind == InteractableKind.Hatch && i.LookDegrees < 0);
        var rail = w.Train.Frames[g].Shape.RoofRail!.Value;
        w.Train.Vehicles[g].Gun.Z = Math.Clamp(hatch.Position.Z, rail.Front, rail.Back);
        var loader = Beside(w, g) with { Pitch = -1.2 };
        Assert.Equal(g, Guns.MannedGun(loader, w.Train, C.Guns));
        Assert.NotNull(CrewActions.LookedAt(loader, w.Train));
        Assert.Equal(-1, TicksToLoad(w, g, (1, loader, Reload)));
        loader.Pitch = 0;
        Assert.Null(CrewActions.LookedAt(loader, w.Train));
        Assert.InRange(TicksToLoad(w, g, (1, loader, Reload)), SoloTicks, SoloTicks + C.Guns.ReloadSteps);
    }

    [Fact]
    public void AnEmptyGunFiresNothing()
    {
        var w = World(combat: Finite);
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

    // Spec B.7's effective range (T121: 220 m, so the forward gun reaches the Track Doll from where the lamp shows her).
    [Theory]
    [InlineData(60, true)]
    [InlineData(200, true)]
    [InlineData(235, false)]
    public void RangeIsTwoHundredAndTwentyMetres(double metres, bool hits)
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
        // T112: seated, the view itself is held at the traverse's stop, and the gun is laid there and no further. The guard
        // van's gun, facing back down the line: the forward gun turns all the way round (note 596).
        int van = GuardCar(w);
        var s = AtGun(w, van);
        s.Yaw = Math.PI + 120 * Math.PI / 180;
        Hold(w, ref s, default, 3);
        double stop = C.Guns.TraverseDegrees / 2;
        Assert.Equal(180 + stop, s.Yaw * 180 / Math.PI, 6);
        Assert.Equal(stop, w.Train.Vehicles[van].Gun.Traverse * 180 / Math.PI, 6);
        var shot = Hold(w, ref s, Fire, 0.2).Single();
        var local = w.Train.Frames[van].DirToLocal(shot.Direction);
        Assert.Equal(stop - 180, Math.Atan2(-local.X, -local.Z) * 180 / Math.PI, 3);
    }

    [Fact]
    public void TheForwardGunTurnsAllTheWayRoundTheShortWay()
    {
        // Note 596 (the director's in-game notes, 9 Oct 2026): the engine's forward gun turns the full circle. Laid round to
        // its right past its side to 170°, then the view taken on round past its back to 190° (−170°): the carriage goes on
        // the short way, 20°, not back the long way round through the front.
        Assert.Equal(360, C.Guns.ForwardTraverseDegrees);
        var w = World();
        var s = AtGun(w, 0);
        s.Yaw = 170 * Math.PI / 180;
        Hold(w, ref s, default, 170 / C.Guns.TraverseDegreesPerSecond + 0.3);
        Assert.Equal(170, w.Train.Vehicles[0].Gun.Traverse * 180 / Math.PI, 6);
        s.Yaw = 190 * Math.PI / 180;
        Hold(w, ref s, default, 20 / C.Guns.TraverseDegreesPerSecond + 0.1);
        Assert.Equal(-170, w.Train.Vehicles[0].Gun.Traverse * 180 / Math.PI, 6);
        // And it fires anywhere off the train's own line: square off its left, at 270°.
        s.Yaw = 270 * Math.PI / 180;
        Hold(w, ref s, default, 3);
        var shot = Hold(w, ref s, Fire, 0.2).Single();
        var local = w.Train.Frames[0].DirToLocal(shot.Direction);
        Assert.Equal(-90, Math.Atan2(-local.X, -local.Z) * 180 / Math.PI, 3);
        // Straight back down its own train is the dead zone (spec B.7), whatever the traverse.
        Assert.Equal(AimResult.DeadZone, Guns.CheckAim(Guns.Mount(w.Train, 0)!.Value, new Double3(0.05, 0, 1).Normalized, C.Guns));
    }

    [Fact]
    public void TheSeatedGunnersGunFollowsTheViewAtItsOwnPaceAndFiresWhereTheBarrelPoints()
    {
        // T112: the mouse leads, the carriage turns after it at traverseDegreesPerSecond; a shot goes where the barrel is.
        var w = World();
        var s = AtGun(w, 0);
        s.Yaw = 70 * Math.PI / 180;
        var early = Hold(w, ref s, Fire, 0.1).Single();
        var local = w.Train.Frames[0].DirToLocal(early.Direction);
        double laid = Math.Atan2(-local.X, -local.Z) * 180 / Math.PI;
        Assert.InRange(laid, 1, 70 * 0.2);
        Hold(w, ref s, default, 70 / C.Guns.TraverseDegreesPerSecond + 0.2);
        Assert.Equal(70, w.Train.Vehicles[0].Gun.Traverse * 180 / Math.PI, 6);
        // The seat's on the carriage: the gunner turned with it, still behind the breech.
        var mount = Guns.Mount(w.Train, 0)!.Value;
        var seat = Guns.SeatAt(mount, w.Train.Vehicles[0].Gun, C.Guns, s.Position.Y);
        Assert.Equal(seat.X, s.Position.X, 9);
        Assert.Equal(seat.Z, s.Position.Z, 9);
        Assert.True(seat.X > mount.Position.X + 0.5, "turned left, the seat swings out to the right of the pivot");
    }

    [Fact]
    public void OnlyASeatedGunnerFiresAndJumpGetsThemUp()
    {
        var w = World();
        var s = StandingAtGun(w, 0);
        Assert.Empty(Hold(w, ref s, Fire, 0.5));
        // Held for a few ticks (a host repeats a lost intent): sat, and still sat.
        Hold(w, ref s, new PlayerIntent { Actions = PlayerActions.Seat }, 3.0 / SimConstants.TickRate);
        Assert.True(s.Has(PlayerFlags.Seated));
        Assert.NotEmpty(Hold(w, ref s, Fire, 0.2));
        // Up out of the seat on Jump, without leaving the roof.
        var jump = new PlayerIntent { Buttons = PlayerButtons.Jump };
        PlayerMotor.Step(ref s, jump, w.Train, P, T, SimConstants.TickSeconds);
        Assert.False(s.Has(PlayerFlags.Seated));
        Assert.Equal(Surface.Roof, s.Surface);
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
        // The rear gun slid to the front of its rail, laid back down along its own roof: the round hits the van before it
        // goes anywhere. (Cab forward, note 276, the forward gun leads the train: nothing of it is ahead to aim down over.)
        var w = World();
        int last = w.Train.Frames.Count - 1;
        w.Train.Vehicles[last].Gun.Z = w.Train.Frames[last].Shape.RoofRail!.Value.Front;
        var mount = Guns.Mount(w.Train, last)!.Value;
        var s = PlayerMotor.SpawnOnRoof(w.Train, last, mount.Position.Z - mount.Facing.Z * 0.7, P);
        s.Yaw = Math.PI;
        s.Flags |= PlayerFlags.Seated;
        s.Pitch = -11 * Math.PI / 180;
        Hold(w, ref s, default, 1); // laid (T112)
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
                            var s = gunner;
                            // The barrel laid on the trial aim (T112: it fires where it points), loaded each time.
                            ref var g = ref w.Train.Vehicles[s.Parent].Gun;
                            double face = Guns.FacingYaw(Guns.Mount(w.Train, s.Parent)!.Value);
                            (g.Traverse, g.Elevation) = (yaw * Math.PI / 180 - face, pitch * Math.PI / 180);
                            (g.Cooldown, g.ReloadNeeded, g.Ammo) = (0, 0, C.Guns.Ammo);
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
            // Its rack kept fed (note 374): a walker bringing the powder up as it goes.
            w.Train.Vehicles[0].Gun.Rack = C.Guns.Rack;
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

    // ---- Up out of a turned-round gun (note 832; the director's 13 Oct evening playtest, line 23).

    /// <summary>The crew's acts, the world's tick and the motor, a tick at a time, as the host runs them.</summary>
    static void Ride(World w, ref PlayerState s, PlayerIntent intent, double seconds, TrainControls controls = default, Action<PlayerState>? each = null)
    {
        for (int i = 0; i < Math.Max(1, Math.Round(seconds * SimConstants.TickRate)); i++)
        {
            w.BeginTick();
            w.CrewAct(ref s, intent, 1);
            w.Step(controls);
            PlayerMotor.Step(ref s, intent, w.Train, P, T, SimConstants.TickSeconds, applyLook: false);
            each?.Invoke(s);
        }
    }

    [Fact]
    public void UpOutOfTheForwardGunTurnedRoundYoureStoodOnTheCabRoofNotOffItsFront()
    {
        // "When I jump off a turret, if I turn the turret around, it causes me to jump off backwards, and I end up jumping off
        // the front of the train": turned round, the forward gun's seat is at the cab roof's front edge, a press of Jump lasts
        // a few ticks, and the train slowing under someone in the air carries them on over it.
        var w = World();
        w.Train.Dynamics.Velocity = 12;
        var s = AtGun(w, 0);
        s.Yaw = Math.PI;
        Ride(w, ref s, default, 180 / C.Guns.TraverseDegreesPerSecond + 0.5);
        Assert.Equal(Math.PI, Math.Abs(w.Train.Vehicles[0].Gun.Traverse), 3);
        double front = w.Train.Frames[0].Shape.Solids.Where(x => x.Part == PartKind.CabRoof && x.Box.ContainsXZ(s.Position)).Min(x => x.Box.Min.Z);
        Assert.True(s.Position.Z - front < P.Edge.Lip, $"the seat's {s.Position.Z - front:0.00} m from the roof's front");

        var braking = new TrainControls { Brake = 1, Reverser = 1 };
        bool flew = false;
        Ride(w, ref s, new PlayerIntent { Buttons = PlayerButtons.Jump }, 8.0 / SimConstants.TickRate, braking, x => flew |= x.Surface == Surface.Air);
        Ride(w, ref s, default, 2, braking, x => flew |= x.Surface == Surface.Air);
        Assert.False(s.Has(PlayerFlags.Seated));
        Assert.False(flew, "the press that got them up leapt them");
        Assert.Equal(0, s.Parent);
        Assert.Equal(Surface.Roof, s.Surface);
        Assert.True(s.Position.Z - front >= P.Edge.Lip - 1e-9, $"stood {s.Position.Z - front:0.00} m from the roof's front");
    }

    [Fact]
    public void TheJumpThatGetsYouUpIsALeapOnlyOnceItsLetGo()
    {
        var w = World();
        var s = AtGun(w, 0);
        var jump = new PlayerIntent { Buttons = PlayerButtons.Jump };
        Ride(w, ref s, jump, 0.5);
        Assert.False(s.Has(PlayerFlags.Seated));
        Assert.Equal(Surface.Roof, s.Surface);
        // Let go, and pressed again: a jump.
        Ride(w, ref s, default, 1.0 / SimConstants.TickRate);
        Assert.False(s.Has(PlayerFlags.GotUp));
        Ride(w, ref s, jump, 1.0 / SimConstants.TickRate);
        Assert.Equal(Surface.Air, s.Surface);
    }
}
