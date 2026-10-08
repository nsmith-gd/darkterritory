using Ballast;
using Ballast.Net;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The last of spec F.3's upgrades (ARCHITECTURE §8 note 196): lamp armour, gun cooling, repair kit charges, radio range and
/// the powered switch thrower. Each changes the tuning it should, and the night with it.
/// </summary>
public class UpgradeTests
{
    static readonly CampaignTuning C = DataFile.Load<CampaignTuning>(Path.Combine(DataFile.FindContentRoot(), CampaignTuning.File));
    static readonly Loadout Base = new(Tuning.Train, Tuning.Boiler, Tuning.Combat, Tuning.Enemies);
    static readonly PlayerTuning P = Tuning.Player;
    static readonly JunctionTuning J = Tuning.Route.Junctions;

    static Loadout With(params string[] upgrades) => Campaign.Campaign.Apply(C, upgrades, Base);

    [Fact]
    public void EachUpgradeChangesItsTuning()
    {
        Assert.Equal(Tuning.Enemies.Climbers.LampOutSeconds * 0.5, With("lampArmour").Enemies!.Climbers.LampOutSeconds, 6);
        Assert.Equal(Tuning.Combat.Guns.FoulChance * 0.5, With("gunCooling").Combat.Guns.FoulChance, 9);
        Assert.Equal(Tuning.Boiler.RepairSeconds * 0.6, With("repairKit").Boiler.RepairSeconds, 6);
        Assert.Equal(0, Tuning.Train.Kit.RadioReach);
        Assert.Equal(150, With("radioRange").Train.Kit.RadioReach, 6);
        Assert.False(Tuning.Train.Composition.SwitchThrower);
        Assert.True(With("poweredSwitchThrower").Train.Composition.SwitchThrower);
        // Every upgrade the campaign sells does something now (a purchase that does nothing is a bug).
        Assert.All(C.Upgrades, u =>
        {
            Assert.NotEmpty(u.Effect);
            Assert.NotEqual(Base, Campaign.Campaign.Apply(C, [u.Id], Base));
        });
    }

    [Fact]
    public void ArmouredTheSmashedLampIsLitAgainSooner()
    {
        bool LitAfter(Loadout l, double seconds)
        {
            var line = new RailLine(new LineDefinition("t", [new TrackSegment(20_000)]));
            var world = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(l.Train, 3, 1)), line, 1_000), l.Combat);
            // A Climber coming over the tender into the cab (Flank.cs) smashes it for its tuning's time.
            world.SmashLamp(l.Enemies!.Climbers.LampOutSeconds);
            var driver = PlayerMotor.SpawnInCab(world.Train, P);
            for (int i = 0; i < seconds * SimConstants.TickRate; i++)
            {
                world.BeginTick();
                world.CrewAct(ref driver, new PlayerIntent { Lamp = LampSwitch.On }, 1);
                world.Step(new TrainControls { Reverser = 1, Brake = 1 });
            }
            return world.LampLit;
        }
        double armoured = With("lampArmour").Enemies!.Climbers.LampOutSeconds;
        Assert.True(LitAfter(With("lampArmour"), armoured + 1));
        Assert.False(LitAfter(Base, armoured + 1));
        Assert.True(LitAfter(Base, Tuning.Enemies.Climbers.LampOutSeconds + 1));
    }

    [Fact]
    public void ACooledGunFoulsLessOften()
    {
        int Fouls(Loadout l)
        {
            // The forward gun, fired and fired: no reload between, and every foul cleared as it comes, so only the bore's
            // chance differs. The same ticks on the same gun, so a cooled gun fouls on a subset of the ticks a hot one does. No
            // ready rack (note 374): every round at the gun.
            var combat = l.Combat with { Guns = l.Combat.Guns with { ReloadSteps = 0, Ammo = 100_000, Rack = 0 } };
            var line = new RailLine(new LineDefinition("t", [new TrackSegment(20_000)]));
            var w = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(l.Train, 6, 1)), line, 5_000), combat);
            w.EnableEnemies(Tuning.Enemies with { Director = Tuning.Enemies.Director with { GraceMinSeconds = 1e9, GraceMaxSeconds = 1e9 } }, null, 1, crew: 1, authority: true);
            var mount = w.Train.Frames[0].Shape.Gun!.Value;
            var s = PlayerMotor.SpawnOnRoof(w.Train, 0, mount.Position.Z - mount.Facing.Z * 0.7, P);
            s.Yaw = mount.Facing.Z < 0 ? 0 : Math.PI;
            s.Flags |= PlayerFlags.Seated;
            int fouls = 0, shots = 0;
            for (int i = 0; i < 1_000 * SimConstants.TickRate; i++)
            {
                w.BeginTick();
                w.CrewAct(ref s, new PlayerIntent { Buttons = PlayerButtons.Fire }, 1);
                shots += w.Shots.Count;
                w.Step(default);
                if (w.Train.Vehicles[0].Gun.Jammed)
                {
                    fouls++;
                    w.Train.Vehicles[0].Gun.Jammed = false;
                }
            }
            Assert.True(shots > 800);
            return fouls;
        }
        int hot = Fouls(Base), cooled = Fouls(With("gunCooling"));
        Assert.True(hot > 20);
        Assert.InRange(cooled, hot * 0.3, hot * 0.7);
    }

    [Fact]
    public void ABetterKitMendsTheBoilerFaster()
    {
        bool MendedAfter(Loadout l, double seconds)
        {
            var line = new RailLine(new LineDefinition("t", [new TrackSegment(50_000)]));
            var world = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(l.Train, 3, 1)), line, 1_000, l.Boiler));
            world.EnableBodies();
            world.Stock();
            world.Train.Boiler.Ruptured = true;
            var firebox = world.Train.Frames[0].Shape.Interactables.First(i => i.Kind == InteractableKind.Firebox).Position;
            var s = PlayerMotor.SpawnInCab(world.Train, P);
            s.Position = s.Position with { X = firebox.X + 0.15, Z = firebox.Z + 0.45 }; // behind the fire door (note 280: at the cab's front)
            // The wrench in hand (note 301: it mends the boiler; the upgrade's speed is its).
            s.HeldSlot = 1;
            Assert.Equal(Tool.Wrench, Kit.Held(s));
            for (int i = 0; i < seconds * SimConstants.TickRate; i++)
            {
                world.BeginTick();
                world.CrewAct(ref s, new PlayerIntent { Buttons = PlayerButtons.Use }, 1);
                world.Step(new TrainControls { Reverser = 1, Brake = 1 });
            }
            return !world.Train.Boiler.Ruptured;
        }
        double quick = With("repairKit").Boiler.RepairSeconds;
        Assert.True(MendedAfter(With("repairKit"), quick + 0.5));
        Assert.False(MendedAfter(Base, quick + 0.5));
    }

    [Fact]
    public void TheRadioCarriesIntoATunnelsMouthWithTheUpgrade()
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(50_000)]));
        int Heard(TrainTuning t, double fromMouth)
        {
            var net = new LoopbackNetwork();
            TrainOnLine Train() => new(new TrainDynamics(Consist.Uniform(t, 12, 1)), line, 1_000);
            var world = new World(Train());
            world.EnableBodies();
            world.Stock();
            var host = new HostSession(net.CreateHost(), world, t, P);
            var talker = new ClientSession(net.CreateClient(), Train(), t, P);
            var listener = new ClientSession(net.CreateClient(), Train(), t, P);
            void Step(int ticks)
            {
                for (int i = 0; i < ticks; i++)
                {
                    net.Advance(SimConstants.TickSeconds);
                    host.Step();
                    talker.Step(default);
                    listener.Step(default);
                }
            }
            Step(30);
            host.SetPlayerState(listener.PlayerId!.Value, PlayerMotor.SpawnOnRoof(host.Train, 11, 0, P));
            Step(5);
            // A long tunnel the rear of the train is backing into: its mouth fromMouth m behind car 11's front.
            double at = host.Train.Cars[11].FrontDistance;
            host.Route = new Route.Route("t", RouteTier.Local, 1, new LineDefinition("t", [new TrackSegment(50_000)]),
                [new RouteFeature(FeatureKind.Tunnel, at - 2_000, at + fromMouth)], new RouteWeather(0, false, 0, 0), 3600);
            var radios = world.Bodies.All.Where(b => b.Kind == BodyKind.Radio).ToList();
            radios[0].Carrier = talker.PlayerId!.Value;
            radios[1].Carrier = listener.PlayerId!.Value;
            listener.VoiceFrames.Clear();
            talker.SendVoice(1, radio: true, new byte[] { 1, 2, 3 });
            Step(3);
            return listener.VoiceFrames.Count(f => f.Path.HasFlag(VoicePath.Radio));
        }
        var ranged = With("radioRange").Train;
        // Just inside: dead without it (spec A.5), heard with it.
        Assert.Equal(0, Heard(Tuning.Train, 60));
        Assert.Equal(1, Heard(ranged, 60));
        // Deeper than its reach, dead either way.
        Assert.Equal(0, Heard(ranged, ranged.Kit.RadioReach + 50));
    }

    [Fact]
    public void TheRadioCarriesDownAMineSpurAsFarAsItsReach()
    {
        const double Toe = 5_200;
        var route = new Route.Route("t", RouteTier.DeadLines, 1, new LineDefinition("t", [new TrackSegment(20_000)]),
            [new RouteFeature(FeatureKind.Facility, 5_000, 5_700, Facility: FacilityKind.MineHead)], new RouteWeather(0, false, 0, 0), 3600)
        {
            Branches = [new BranchDefinition(BranchKind.Spur, Toe, +1,
                [new TrackSegment(J.DivergeLength, -J.DivergeRadius), new TrackSegment(J.DivergeLength, J.DivergeRadius), new TrackSegment(400)])],
        };
        var line = route.Build();
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 3, 1)), line, 1_000);
        var run = new Run.Run(Tuning.Run, route);
        PlayerState Down(double metres)
        {
            double hint = Toe + metres;
            return PlayerMotor.SpawnOnGround(line.Sample(0, Toe + metres).Position, line, hint, P);
        }
        double reach = With("radioRange").Train.Kit.RadioReach;
        Assert.True(run.Underground(Down(100), train));
        Assert.False(run.Underground(Down(100), train, reach));
        Assert.True(run.Underground(Down(reach + 60), train, reach));
    }

    /// <summary>A main line with one dead line off it, set for the dead line (the Switchman's work), and a train short of it.</summary>
    static World Junction(TrainTuning t, double short_, double speed = 0)
    {
        const double Toe = 3_000;
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(8_000)]),
            [new BranchDefinition(BranchKind.DeadLine, Toe, +1,
                [new TrackSegment(J.DivergeLength, -J.DivergeRadius), new TrackSegment(J.DivergeLength, J.DivergeRadius), new TrackSegment(500)])]);
        var world = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(t, 4, 1)), line, Toe - short_));
        world.EnableBodies();
        world.EnableSwitches(J);
        world.Train.Dynamics.Velocity = speed;
        world.SetSwitch(0, true);
        return world;
    }

    /// <summary>Someone at the powered thrower's lever in the cab.</summary>
    static PlayerState AtLever(World world)
    {
        var at = world.Train.Frames[0].Shape.Interactables.First(i => i.Kind == InteractableKind.Points).Position;
        var s = PlayerMotor.SpawnInCab(world.Train, P);
        s.Position = s.Position with { X = Math.Clamp(at.X, -1.05, 1.05), Z = at.Z };
        return s;
    }

    static void Hold(World world, ref PlayerState s, double seconds, bool coast = false)
    {
        for (int i = 0; i < seconds * SimConstants.TickRate; i++)
        {
            world.BeginTick();
            world.CrewAct(ref s, new PlayerIntent { Buttons = PlayerButtons.Use }, 1);
            world.Step(new TrainControls { Reverser = 1, Brake = coast ? 0 : 1 });
        }
    }

    [Fact]
    public void ThePoweredThrowerSetsThePointsAheadFromTheCab()
    {
        var fitted = With("poweredSwitchThrower").Train;
        // Fitted, stopped 150 m short: the driver puts the points back for the main line without stepping down.
        var world = Junction(fitted, 150);
        var s = AtLever(world);
        Assert.True(PlayerMotor.InCab(s, world.Train));
        Assert.Equal(0, world.Switches!.InReach(s, world.Train));
        Hold(world, ref s, J.ThrowSeconds - 0.2);
        Assert.True(world.Train.Diverging(0));
        Hold(world, ref s, 0.4);
        Assert.False(world.Train.Diverging(0));
        Assert.True(PlayerMotor.InCab(s, world.Train));

        // Without it, the same lever does nothing: it's the ground for them.
        var plain = Junction(Tuning.Train, 150);
        var p = AtLever(plain);
        Assert.Null(plain.Switches!.InReach(p, plain.Train));
        Hold(plain, ref p, J.ThrowSeconds + 0.5);
        Assert.True(plain.Train.Diverging(0));

        // Further than its reach, nothing.
        var far = Junction(fitted, fitted.Composition.Thrower.Reach + 50);
        var f = AtLever(far);
        Assert.Null(far.Switches!.InReach(f, far.Train));
        Hold(far, ref f, J.ThrowSeconds + 0.5);
        Assert.True(far.Train.Diverging(0));

        // Running faster than it allows, it won't throw (rolling on, but still well short of the points when it's let go).
        var fast = Junction(fitted, 199, speed: fitted.Composition.Thrower.MaxSpeed + 4);
        var q = AtLever(fast);
        Hold(fast, ref q, J.ThrowSeconds + 0.3, coast: true);
        Assert.True(fast.Train.Diverging(0));
        Assert.True(fast.Train.Dynamics.Speed > fitted.Composition.Thrower.MaxSpeed);
    }

    [Fact]
    public void ThePoweredThrowerWontMoveThePointsUnderAWheel()
    {
        // The engine's front is over the toe (so the Switchman's setting couldn't go on either): the points are under it, and
        // there are none ahead for the lever to reach.
        var world = Junction(With("poweredSwitchThrower").Train, -5);
        Assert.False(world.Train.Diverging(0));
        var s = AtLever(world);
        Assert.Null(world.Switches!.InReach(s, world.Train));
        Hold(world, ref s, J.ThrowSeconds + 0.5);
        Assert.False(world.Train.Diverging(0));
    }
}
