using Ballast;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The human cannonball (note 746; docs/design/upgrades.md §8.3; the director, 15 Oct 2026: "the reload flow to be powder,
/// pack, then either a ball or a player jumps in"): on the guard van's gun, packed, a crewmate climbs in at the muzzle instead
/// of the ball, and climbs out with Jump.
/// </summary>
public class CannonballTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly CampaignTuning C = DataFile.Load<CampaignTuning>(Path.Combine(Content, CampaignTuning.File));
    static readonly Loadout Base = new(Tuning.Train, Tuning.Boiler, Tuning.Combat, Tuning.Enemies);
    static readonly PlayerTuning P = Tuning.Player;
    static readonly PlayerIntent Use = new() { Buttons = PlayerButtons.Use };

    static World World(bool dressed)
    {
        var t = dressed ? Campaign.Campaign.Apply(C, ["humanCannonball"], Base).Train : Tuning.Train;
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(20_000)]));
        return new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(t, 4, 1)), line, 2_000, Tuning.Boiler), Tuning.Combat);
    }

    static int Van(World w) => Assert.Single(w.Train.Vehicles, v => v.Kind == VehicleKind.Guard && v.HasGun).Id;

    /// <summary>On the van's roof at its gun's muzzle, the gun fired and packed (one step, the ball's, left).</summary>
    static PlayerState AtTheMuzzle(World w)
    {
        int van = Van(w);
        w.Train.Vehicles[van].Gun.ReloadNeeded = 1;
        var s = PlayerMotor.SpawnOnRoof(w.Train, van, 0, P);
        var muzzle = HumanCannonball.Muzzle(w.Train, van, Tuning.Combat.Cannonball);
        return s with { Position = new Double3(muzzle.X, s.Position.Y, muzzle.Z) };
    }

    static void Act(World w, ref PlayerState s, PlayerIntent intent, double seconds)
    {
        for (int i = 0; i < seconds * SimConstants.TickRate; i++)
        {
            w.BeginTick();
            w.CrewAct(ref s, intent, 1);
            w.Step(new TrainControls { Reverser = 1, Brake = 1 });
        }
    }

    [Fact]
    public void ItsOnTheCatalogueAndTheReloadEndsWithTheBall()
    {
        var u = Assert.Single(C.Upgrades, u => u.Id == "humanCannonball");
        Assert.Equal(UpgradeKind.Whimsy, u.Kind);
        Assert.NotNull(u.Line);
        Assert.NotNull(u.Tally);
        Assert.True(HumanCannonball.Takes(World(dressed: true).Train, Van(World(dressed: true))));
        Assert.False(HumanCannonball.Takes(World(dressed: false).Train, Van(World(dressed: false))));
    }

    [Fact]
    public void ACrewmateHoldingUseAtThePackedGunsMuzzleClimbsInAndJumpClimbsOut()
    {
        var w = World(dressed: true);
        int van = Van(w);
        var s = AtTheMuzzle(w);
        Assert.Equal(van, HumanCannonball.AtTheMuzzle(s, w.Train, Tuning.Combat.Guns, Tuning.Combat.Cannonball));
        // Not long enough: let go, and the climb starts again.
        Act(w, ref s, Use, Tuning.Combat.Cannonball.ClimbSeconds * 0.5);
        Act(w, ref s, default, 0.1);
        Assert.False(s.Has(PlayerFlags.InCannon));
        Assert.Equal(1, w.Train.Vehicles[van].Gun.ReloadNeeded);
        // Held: in, the gun loaded with them (no ball).
        Act(w, ref s, Use, Tuning.Combat.Cannonball.ClimbSeconds + 0.1);
        Assert.True(s.Has(PlayerFlags.InCannon));
        Assert.Equal(0, w.Train.Vehicles[van].Gun.ReloadNeeded);
        // Held there: walking does nothing.
        var at = s.Position;
        Act(w, ref s, new PlayerIntent { MoveZ = 1 }, 1);
        Assert.True((s.Position - at).Length < 0.05, $"moved {(s.Position - at).Length:F2} m in the barrel");
        // Jump: out, at the muzzle, and the ball's wanted again.
        Act(w, ref s, new PlayerIntent { Buttons = PlayerButtons.Jump }, SimConstants.TickSeconds);
        Assert.False(s.Has(PlayerFlags.InCannon));
        Assert.Equal(1, w.Train.Vehicles[van].Gun.ReloadNeeded);
    }

    [Fact]
    public void WithoutTheUpgradeOrBeforeItsPackedTheMuzzleTakesNobody()
    {
        var plain = World(dressed: false);
        var s = AtTheMuzzle(plain);
        Act(plain, ref s, Use, Tuning.Combat.Cannonball.ClimbSeconds * 2);
        Assert.False(s.Has(PlayerFlags.InCannon));
        var w = World(dressed: true);
        s = AtTheMuzzle(w);
        w.Train.Vehicles[Van(w)].Gun.ReloadNeeded = 3;
        Assert.Null(HumanCannonball.AtTheMuzzle(s, w.Train, Tuning.Combat.Guns, Tuning.Combat.Cannonball));
    }

    /// <summary>
    /// A host's tick of a gunner (1) and whoever's in the gun (2), in the host's order (HostSession.Step): everyone acts, the
    /// world steps, what's owed lands (the launch), everyone's feet move, then the bodies.
    /// </summary>
    static void Tick(World w, ref PlayerState gunner, PlayerIntent fire, ref PlayerState inside)
    {
        w.BeginTick();
        w.CrewAct(ref gunner, fire, 1);
        w.CrewAct(ref inside, default, 2);
        w.Step(new TrainControls { Reverser = 1, Brake = 1 });
        var crew = new Dictionary<int, PlayerState> { [1] = gunner, [2] = inside };
        w.ApplyDamage(id => crew.TryGetValue(id, out var x) ? x : null, (id, x) => crew[id] = x, [1, 2]);
        (gunner, inside) = (crew[1], crew[2]);
        PlayerMotor.Step(ref gunner, fire, w.Train, P, w.Train.Dynamics.Tuning, SimConstants.TickSeconds, applyLook: false);
        PlayerMotor.Step(ref inside, default, w.Train, P, w.Train.Dynamics.Tuning, SimConstants.TickSeconds, applyLook: false);
        w.StepBodies([(1, gunner), (2, inside)]);
    }

    /// <summary>The van's gunner seated at its gun, and a crewmate in it, the gun loaded with them.</summary>
    static (PlayerState Gunner, PlayerState Inside) Loaded(World w)
    {
        int van = Van(w);
        var inside = AtTheMuzzle(w);
        Act(w, ref inside, Use, Tuning.Combat.Cannonball.ClimbSeconds + 0.1);
        Assert.True(inside.Has(PlayerFlags.InCannon));
        var mount = Guns.Mount(w.Train, van)!.Value;
        var gunner = PlayerMotor.SpawnOnRoof(w.Train, van, 0, P);
        gunner = gunner with { Position = new Double3(mount.Position.X, gunner.Position.Y, mount.Position.Z), Flags = gunner.Flags | PlayerFlags.Seated };
        w.StepBodies([(1, gunner), (2, inside)]);
        return (gunner, inside);
    }

    [Fact]
    public void FiredTheyFlyAlongTheAimFacingWhereTheyLandAndLandUnhurt()
    {
        var w = World(dressed: true);
        w.EnableBodies();
        w.Movement = P;
        var (gunner, inside) = Loaded(w);
        int health = inside.Health;
        var fire = new PlayerIntent { Buttons = PlayerButtons.Fire };
        Tick(w, ref gunner, fire, ref inside);
        Assert.True(inside.Has(PlayerFlags.Fired), "not fired");
        Assert.False(inside.Has(PlayerFlags.InCannon));
        Assert.Equal(Tuning.Combat.Guns.ReloadSteps, w.Train.Vehicles[Van(w)].Gun.ReloadNeeded);
        // Through the air, facing where they come down (the look a tick behind the flight, so near enough).
        bool faced = true;
        for (int i = 0; i < 10 * SimConstants.TickRate && inside.Surface == Surface.Air; i++)
        {
            Tick(w, ref gunner, default, ref inside);
            if (inside.Surface != Surface.Air)
                break;
            var v = inside.Velocity;
            double yaw = Math.Atan2(-v.X, -v.Z);
            faced &= Math.Abs(Math.IEEERemainder(inside.Yaw - yaw, 2 * Math.PI)) < 0.05;
        }
        Assert.NotEqual(Surface.Air, inside.Surface);
        Assert.True(faced, "looked away from where they were going");
        Assert.True(inside.Alive, $"died of {inside.Death}");
        Assert.Equal(health, inside.Health);
        Assert.False(inside.Has(PlayerFlags.Fired));
        // Out where the gun pointed, the range away (on a plain line: nothing stepped it back).
        var muzzle = w.Train.Frames[Van(w)].ToWorld(HumanCannonball.Muzzle(w.Train, Van(w), Tuning.Combat.Cannonball));
        var landed = PlayerMotor.WorldPosition(inside, w.Train);
        double out_ = Math.Sqrt((landed.X - muzzle.X) * (landed.X - muzzle.X) + (landed.Z - muzzle.Z) * (landed.Z - muzzle.Z));
        Assert.InRange(out_, Tuning.Combat.Cannonball.Range - 4, Tuning.Combat.Cannonball.Range + 4);
    }

    [Fact]
    public void TheLandingIsNeverOffTheMapOrPastTheLinesEnd()
    {
        var w = World(dressed: true);
        var t = Tuning.Combat.Cannonball with { MapHalfWidth = 10 };
        var from = w.Train.Frames[Van(w)].Origin;
        // Straight out sideways, past a map 10 m wide: brought back to it.
        var side = HumanCannonball.LandingPoint(w.Train, from, new Double3(1, 0, 0), t);
        Assert.InRange(Math.Abs(side.X - from.X), 0, 10.01);
        // Back down the line, its start 5-30 m behind the van: brought in short of the end, on the line's own ground.
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(20_000)]));
        double hint = 0;
        TrainOnLine? near = null;
        for (double head = 40; head < 400 && near is null; head += 2)
        {
            var tr = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 4, 1)), line, head, Tuning.Boiler);
            if (line.Nearest(tr.Frames[^1].Origin, ref hint).Distance is >= 5 and <= 30)
                near = tr;
        }
        Assert.NotNull(near);
        var tail = near.Frames[^1];
        var back = HumanCannonball.LandingPoint(near, tail.Origin, tail.DirToWorld(new Double3(0, 0, 1)), Tuning.Combat.Cannonball);
        Assert.InRange(line.Nearest(back, ref hint).Distance, 1, 20_000);
    }
}
