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
        // Every upgrade the campaign sells does something now (a purchase that does nothing is a bug): it changes the night,
        // or the yard clerk reads it into every tally (note 678: the silly ones whose whole point is that).
        Assert.All(C.Upgrades, u =>
        {
            if (u.Effect.Count > 0 || u.Tally is null)
                Assert.NotEqual(Base, Campaign.Campaign.Apply(C, [u.Id], Base));
            if (u.Tally is not null)
                Assert.Single(Campaign.Campaign.TallyLines(C, [u.Id]));
        });
    }

    [Fact]
    public void TheCataloguesToolsChangeWhatTheySay()
    {
        // Note 678: each tool takes a share of the trouble it answers, never all of it.
        var train = Tuning.Train;
        Assert.Equal(train.Overspeed.LeadSeconds * 1.5, With("speedRecorder").Train.Overspeed.LeadSeconds, 9);
        Assert.Equal(train.BrakeFade.FadePerSecond * 0.5, With("compositionShoes").Train.BrakeFade.FadePerSecond, 9);
        Assert.Equal(train.Fragile!.ShockShare * 0.4, With("sprungCradles").Train.Fragile!.ShockShare, 9);
        Assert.Equal(train.Breach.BoardSeconds * 0.5, With("precutBoards").Train.Breach.BoardSeconds, 9);
        Assert.Equal(Tuning.Enemies.Hotbox.KnockSeconds * 1.5, With("axleOilers").Enemies!.Hotbox.KnockSeconds, 9);
        Assert.Equal(Tuning.Enemies.Hotbox.GlowSeconds * 1.5, With("axleOilers").Enemies!.Hotbox.GlowSeconds, 9);
        Assert.Equal(Tuning.Enemies.Hotbox.RepairSeconds * 0.5, With("axleOilers").Enemies!.Hotbox.RepairSeconds, 9);
        Assert.Equal(Tuning.Enemies.Sleepers.HeavyDamage * 0.5, With("cowcatcher").Enemies!.Sleepers.HeavyDamage, 9);
        Assert.Equal(Tuning.Enemies.Sleepers.MinorDamage * 0.5, With("cowcatcher").Enemies!.Sleepers.MinorDamage, 9);
        Assert.Equal(Tuning.Train.Kit.GrabIrons * 4.0 / 3.0, With("grabIrons").Train.Kit.GrabIrons, 6);
        Assert.Equal(1, Tuning.Train.Kit.GrabIrons);
        Assert.Equal(Tuning.Enemies.CarFire.ChargeSeconds * 1.5, With("bigExtinguishers").Enemies!.CarFire.ChargeSeconds, 9);
        Assert.Equal(Tuning.Enemies.CarFire.RechargeSeconds / 1.5, With("bigExtinguishers").Enemies!.CarFire.RechargeSeconds, 9);
        Assert.Equal(Tuning.Combat.Guns.ReloadStepSeconds * 0.8, With("gunRammers").Combat.Guns.ReloadStepSeconds, 9);
        Assert.All(train.Performance.Zip(With("boosterEngine").Train.Performance), p => Assert.Equal(p.First.Accel * 1.2, p.Second.Accel, 9));
        // The whimsy and the silly: the honk is a third as loud to the Choir, the gold one a quarter louder.
        Assert.Equal(Tuning.Combat.Choir.WhistleLoudness * 0.35, With("theHonk").Combat.Choir.WhistleLoudness, 9);
        Assert.Equal(Tuning.Combat.Choir.WhistleLoudness * 1.25, With("goldWhistle").Combat.Choir.WhistleLoudness, 9);
        Assert.Equal(Tuning.Combat.Guns.FoulChance * 0.5 * 0.85, With("gunCooling", "luckyHorseshoe").Combat.Guns.FoulChance, 9);
    }

    [Fact]
    public void ToolsAreStockedFromTheTierTheyAnswer()
    {
        // Note 678: the town stocks what the line needs. A three-car crew on local work can't buy a speed recorder or gun
        // rammers, however rich; a six-car one (frontier, spec F.4) can buy the first; an eleven-car one, both.
        var rich = Campaign.Campaign.New(C, 1, "t", 1) with { Scrip = 1e9 };
        var refused = Campaign.Campaign.BuyUpgrade(C, rich, "speedRecorder");
        Assert.False(refused.Ok);
        Assert.Contains("frontier", refused.Refused);
        Assert.True(Campaign.Campaign.BuyUpgrade(C, rich, "sprungCradles").Ok);
        Assert.True(Campaign.Campaign.BuyUpgrade(C, rich with { Cars = 6 }, "speedRecorder").Ok);
        Assert.False(Campaign.Campaign.BuyUpgrade(C, rich with { Cars = 6 }, "gunRammers").Ok);
        Assert.True(Campaign.Campaign.BuyUpgrade(C, rich with { Cars = 11 }, "gunRammers").Ok);
        // The silly and the whimsical are for anyone who can pay.
        Assert.All(C.Upgrades.Where(u => u.Kind != UpgradeKind.Tool), u => Assert.Equal(RouteTier.Local, u.FromTier));
    }

    [Fact]
    public void TheHonkAndTheGoldWhistleWontGoTogether()
    {
        var rich = Campaign.Campaign.New(C, 1, "t", 1) with { Scrip = 1e9 };
        var honked = Campaign.Campaign.BuyUpgrade(C, rich, "theHonk");
        Assert.True(honked.Ok);
        var both = Campaign.Campaign.BuyUpgrade(C, honked.State, "goldWhistle");
        Assert.False(both.Ok);
        Assert.Contains("the honk", both.Refused);
        // Either way round.
        Assert.False(Campaign.Campaign.BuyUpgrade(C, Campaign.Campaign.BuyUpgrade(C, rich, "goldWhistle").State, "theHonk").Ok);
    }

    [Fact]
    public void TheArmouredCabHoldsAClimberAtTheGlass()
    {
        Assert.False(Tuning.Train.Composition.CabArmour);
        Assert.True(With("armouredCab").Train.Composition.CabArmour);
        Assert.True(Tuning.Enemies.Climbers.CabGlassSeconds > 0);
    }

    [Fact]
    public void ArmourIIAndIIIAreRanksOfTheConversion()
    {
        // Each plates one more car than the last: three fitted, three plated; and a rank needs the one before it.
        Assert.Equal(Tuning.Train.Composition.ArmouredCars + 3, With("armouredCar", "armouredCar2", "armouredCar3").Train.Composition.ArmouredCars);
        var s = Campaign.Campaign.New(C, 1, "t", 1) with { Cars = 20, Scrip = 1e9 };
        Assert.Contains("armoured car conversion", Campaign.Campaign.BuyUpgrade(C, s, "armouredCar2").Refused, StringComparison.OrdinalIgnoreCase);
        s = Campaign.Campaign.BuyUpgrade(C, s, "armouredCar").State;
        Assert.False(Campaign.Campaign.BuyUpgrade(C, s, "armouredCar3").Ok);
        s = Campaign.Campaign.BuyUpgrade(C, s, "armouredCar2").State;
        Assert.True(Campaign.Campaign.BuyUpgrade(C, s, "armouredCar3").Ok);
    }

    [Fact]
    public void TheCataloguesPricesKeepSpecF3sBands()
    {
        // Spec F.3: a small tool 15-25% of the next car, a major 100-180%. The whimsy sits under a tool; the silly may be
        // priced past anything, which is its joke (note 678). A major whimsy (the BBQ deck, note 698) under a major tool.
        Assert.All(C.Upgrades.Where(u => u.Kind == UpgradeKind.Tool), u =>
            Assert.InRange(u.CostShare, u.Size == UpgradeSize.Small ? 0.15 : 1.0, u.Size == UpgradeSize.Small ? 0.25 : 1.8));
        Assert.All(C.Upgrades.Where(u => u.Kind == UpgradeKind.Whimsy), u => Assert.InRange(u.CostShare, 0.01, u.Size == UpgradeSize.Small ? 0.15 : 1.0));
        Assert.Contains(C.Upgrades, u => u.Kind == UpgradeKind.Silly && u.CostShare > 1.8);
    }

    [Fact]
    public void TheClerkReadsTheSillyOnesIntoEveryTally()
    {
        var report = new Run.RunReport(Run.RunEnd.Delivered, 1000, 12, 3, 1, 2, 2450, 40, 12, 30, -350, 3, 1);
        var owned = Campaign.Campaign.TallyLines(C, ["commendation", "goldWhistle", "tenderCapacity"]);
        Assert.Equal(["Whistle: gold.", "Commendation: framed."], owned);
        var lines = Run.Radio.Tally(report, owned);
        int gold = lines.IndexOf("Whistle: gold."), coal = lines.FindIndex(l => l.StartsWith("Coal:", StringComparison.Ordinal));
        Assert.InRange(gold, 1, coal - 1);
        Assert.Equal(Run.Radio.Tally(report).Count + 2, lines.Count);
    }

    [Fact]
    public void TheBrighterLampReadsTheBoardsAndSeesTheGreaseFurtherOff()
    {
        // Queue #243, note 506: spec F.3's lamp brightness scaled only the retired Sleepers' reveal, and so did nothing. It's
        // the headlamp's reach now (train.json headlampReach), everything it shows: here a tunnel's board and grease on the rail.
        var sight = DataFile.Load<SightTuning>(Path.Combine(DataFile.FindContentRoot(), SightTuning.File));
        var bright = With("lampBrightness").Train;
        Assert.Equal(1, Tuning.Train.HeadlampReach);
        Assert.Equal(1.25, bright.HeadlampReach, 6);
        var route = new Route.Route("t", RouteTier.Frontier, 1, new LineDefinition("t", [new TrackSegment(4000)]),
            [new RouteFeature(FeatureKind.Tunnel, 1500, 1700), new RouteFeature(FeatureKind.Grease, 2400, 2500)], new RouteWeather(0.01, false, 0, 0), 3600);
        var board = Lineside.Boards(sight, route).Single(b => b.Kind == SignKind.LowClearance);
        TrainOnLine At(TrainTuning t, double front) => new(new TrainDynamics(Consist.Uniform(t, 3, 1)), route.Build(), front);
        bool Read(TrainTuning t, double front)
        {
            var lineside = new Lineside(sight, route);
            lineside.See(At(t, front), lamp: true);
            return lineside.Read(board.Id);
        }
        // Past the stock lamp's reach, inside the brighter one's.
        double past = board.Board - sight.LampSignRange - 40;
        Assert.False(Read(Tuning.Train, past));
        Assert.True(Read(bright, past));
        double grease = 2400 - sight.LampGreaseRange - 20;
        Assert.Null(new Lineside(sight, route).GreaseAhead(At(Tuning.Train, grease), lamp: true));
        Assert.Equal(2400, new Lineside(sight, route).GreaseAhead(At(bright, grease), lamp: true));
        // Lamps down, the brighter lamp's no help.
        Assert.Equal(sight.DarkSignRange, new Lineside(sight, route).SignRange(false, bright.HeadlampReach));
    }

    [Fact]
    public void ArmouredTheSmashedLampIsMendedSooner()
    {
        // Note 301, slice 2: the glass goes in with the wrench, worked from the cab's front windows; armour halves the work.
        double rate = Tuning.Train.Repair.LampMendRate;
        bool LitAfter(Loadout l, double seconds)
        {
            var line = new RailLine(new LineDefinition("t", [new TrackSegment(20_000)]));
            var world = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(l.Train, 3, 1)), line, 1_000), l.Combat);
            world.EnableBodies(); // the host's
            // A Climber coming over the tender into the cab (Flank.cs) smashes it for its tuning's time.
            world.SmashLamp(l.Enemies!.Climbers.LampOutSeconds);
            var driver = PlayerMotor.SpawnInCab(world.Train, P) with { HeldSlot = 1 };
            for (int i = 0; i < seconds * SimConstants.TickRate; i++)
            {
                world.BeginTick();
                world.CrewAct(ref driver, new PlayerIntent { Lamp = LampSwitch.On, Buttons = PlayerButtons.Use }, 1);
                world.Step(new TrainControls { Reverser = 1, Brake = 1 });
            }
            return world.LampLit;
        }
        double armoured = With("lampArmour").Enemies!.Climbers.LampOutSeconds / rate;
        Assert.True(LitAfter(With("lampArmour"), armoured + 0.3));
        Assert.False(LitAfter(Base, armoured + 0.3));
        Assert.True(LitAfter(Base, Tuning.Enemies.Climbers.LampOutSeconds / rate + 0.3));
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
            // The loose sets (note 749's radio each off), handed to the two of them below.
            t = t with { Kit = t.Kit with { RadioEach = false } };
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
