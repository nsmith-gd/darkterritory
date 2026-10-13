using Ballast;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>GDD §22, §23 hazards (note 183): deep cold, wind, fouled guns, broken radios, lamps out.</summary>
public class HazardTests
{
    static readonly PlayerTuning P = Tuning.Player;
    static readonly CombatTuning C = Tuning.Combat;

    sealed class Weather(int cold, double wind = 0, double adhesion = 1) : ITrackConditions
    {
        public double Ground(Double3 world) => 0;
        public double Adhesion(int path, double distance) => adhesion;
        public double Drag(int path, double distance, double speed) => 0;
        public int ColdStep(int path, double distance) => cold;
        public double Wind(int path, double distance) => wind;
    }

    /// <param name="wrench">Note 301's <c>repair.wrench</c>: the wrench mends a radio. Off, the repair kit does (note 200).</param>
    static TrainOnLine Train(ITrackConditions? conditions, int cars = 5, bool wrench = true) =>
        new(new TrainDynamics(Consist.Uniform(Tuning.Train with { Repair = Tuning.Train.Repair with { Wrench = wrench } }, cars, 1)),
            new RailLine(new LineDefinition("t", [new TrackSegment(40_000)])) { Conditions = conditions }, 2_000);

    static double ColdAfter(ITrackConditions? conditions, double seconds)
    {
        var train = Train(conditions);
        var s = PlayerMotor.SpawnOnRoof(train, 2, 0, P);
        for (int i = 0; i < seconds * SimConstants.TickRate; i++)
            PlayerMotor.Step(ref s, default, train, P, Tuning.Train, SimConstants.TickSeconds, applyLook: false);
        return s.Cold;
    }

    [Fact]
    public void DeepColdComesOnFaster()
    {
        double normal = ColdAfter(null, 10), deep = ColdAfter(new Weather(cold: 2), 10);
        Assert.True(normal > 0);
        Assert.Equal(normal * (1 + 2 * P.Cold.PerColdStep), deep, 1);
    }

    [Fact]
    public void DeepColdTakesTheEdgeOffTheFire()
    {
        var bt = Tuning.Boiler;
        Assert.Equal(1, bt.ColdEfficiency(0));
        Assert.Equal(1 - 2 * bt.ColdEfficiencyPerStep, bt.ColdEfficiency(2), 9);
        Assert.Equal(bt.ColdEfficiencyFloor, bt.ColdEfficiency(100));
    }

    [Fact]
    public void FoulingIsAHashTheSameEverywhereAndRoughlyTheChance()
    {
        int fouls = Enumerable.Range(0, 20_000).Count(t => Guns.Fouls((uint)t, 0, 0.04));
        Assert.InRange(fouls, 600, 1000);
        Assert.Equal(Guns.Fouls(1234, 3, 0.5), Guns.Fouls(1234, 3, 0.5));
        Assert.False(Guns.Fouls(1234, 3, 0));
        Assert.True(Guns.Fouls(1234, 3, 1));
    }

    static World GunWorld()
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(20_000)]));
        var w = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), line, 5_000), C);
        w.EnableEnemies(Tuning.Enemies with { Director = Tuning.Enemies.Director with { GraceMinSeconds = 1e9, GraceMaxSeconds = 1e9 } }, null, 1, crew: 1, authority: true);
        return w;
    }

    static void Hold(World w, ref PlayerState s, PlayerIntent intent, double seconds)
    {
        for (int i = 0; i < seconds * SimConstants.TickRate; i++)
        {
            w.BeginTick();
            w.CrewAct(ref s, intent, 1);
            w.Step(default);
        }
    }

    [Fact]
    public void AFouledBoreClearsWithUseHeldForItsSeconds()
    {
        var w = GunWorld();
        var mount = w.Train.Frames[0].Shape.Gun!.Value;
        var s = PlayerMotor.SpawnOnRoof(w.Train, 0, mount.Position.Z - mount.Facing.Z * 0.7, P);
        s.Yaw = mount.Facing.Z < 0 ? 0 : Math.PI;
        s.Flags |= PlayerFlags.Seated;
        w.Train.Vehicles[0].Gun.Jammed = true;
        var use = new PlayerIntent { Buttons = PlayerButtons.Use };
        Hold(w, ref s, use, C.Guns.ClearSeconds - 0.5);
        Assert.True(w.Train.Vehicles[0].Gun.Jammed);
        // Letting go starts it over.
        Hold(w, ref s, default, 0.2);
        Hold(w, ref s, use, C.Guns.ClearSeconds - 0.5);
        Assert.True(w.Train.Vehicles[0].Gun.Jammed);
        Hold(w, ref s, use, 0.7);
        Assert.False(w.Train.Vehicles[0].Gun.Jammed);
    }

    static (World World, Body Radio, PlayerState Wearer) Wearing(int id = 1, bool wrench = true)
    {
        var world = new World(Train(null, 6, wrench));
        world.EnableBodies();
        world.Stock();
        var radio = world.Bodies.All.First(b => b.Kind == BodyKind.Radio);
        radio.Carrier = id;
        var s = PlayerMotor.SpawnOnRoof(world.Train, 2, 0, P);
        return (world, radio, s);
    }

    [Fact]
    public void AHardKnockCanBreakTheRadioAndABrokenOneIsDead()
    {
        var (world, radio, s) = Wearing();
        world.StepBodies([(1, s)]);
        Assert.True(world.Bodies.HasRadio(1));
        s.Health = 1;
        world.StepBodies([(1, s)]);
        // The tick and the wearer decide it, the same on every run.
        Assert.Equal(Guns.Fouls(world.Tick, 1001, Math.Min(1, Tuning.Train.Kit.RadioBreakPerDamage * (P.Health - 1))), radio.Broken);
        radio.Broken = true;
        Assert.False(world.Bodies.HasRadio(1));
    }

    [Fact]
    public void GrabbedSomeRadiosBreakAndUnhurtNone()
    {
        int broken = 0, trials = 400;
        for (int id = 1; id <= trials; id++)
        {
            var (world, radio, s) = Wearing(id);
            world.StepBodies([(id, s)]);
            world.StepBodies([(id, s)]);
            Assert.False(radio.Broken);
            s.Flags |= PlayerFlags.Held;
            world.StepBodies([(id, s)]);
            broken += radio.Broken ? 1 : 0;
        }
        double expect = trials * Tuning.Train.Kit.RadioBreakOnGrab;
        Assert.InRange(broken, expect * 0.6, expect * 1.4);
    }

    // Note 200: wind on a roof's footing, the cold step's helper, and mending a broken radio with the repair kit.

    [Fact]
    public void TheWindPushesARoofStanderSideways()
    {
        var calm = Train(new Weather(0, wind: 0));
        var windy = Train(new Weather(0, wind: 1));
        var s0 = PlayerMotor.SpawnOnRoof(calm, 2, 0, P);
        var s1 = PlayerMotor.SpawnOnRoof(windy, 2, 0, P);
        double push = PlayerMotor.WindPush(s1, default, windy, P, Tuning.Train);
        // Standing, the train stopped: the wind across a still train, the walking share of it, in tonight's gust here.
        Assert.Equal(P.Wind.Drift * P.Wind.Still * P.Wind.Walking * PlayerMotor.Gust(s1.LineHint, P.Wind.GustMetres), push, 9);
        Assert.NotEqual(0, push);
        Assert.Equal(0, PlayerMotor.WindPush(s0, default, calm, P, Tuning.Train));
        for (int i = 0; i < SimConstants.TickRate; i++)
        {
            PlayerMotor.Step(ref s0, default, calm, P, Tuning.Train, SimConstants.TickSeconds, applyLook: false);
            PlayerMotor.Step(ref s1, default, windy, P, Tuning.Train, SimConstants.TickSeconds, applyLook: false);
        }
        Assert.Equal(0, s0.Position.X, 6);
        // A second's push, give or take the gust easing as it goes: modest, and never off the roof from the middle.
        Assert.Equal(push, s1.Position.X, 2);
        Assert.Equal(Surface.Roof, s1.Surface);
    }

    [Fact]
    public void RunningCatchesMoreWindAndAHandrailOrAGunSeatLess()
    {
        var train = Train(new Weather(0, wind: 1.5));
        var s = PlayerMotor.SpawnOnRoof(train, 2, 0, P);
        double stood = Math.Abs(PlayerMotor.WindPush(s, default, train, P, Tuning.Train));
        double running = Math.Abs(PlayerMotor.WindPush(s, new PlayerIntent { Buttons = PlayerButtons.Run }, train, P, Tuning.Train));
        Assert.Equal(stood / P.Wind.Walking, running, 9);
        var railed = Tuning.Train with { Composition = Tuning.Train.Composition with { Handrails = true } };
        Assert.Equal(stood * railed.Composition.Rails.Wind, Math.Abs(PlayerMotor.WindPush(s, default, train, P, railed)), 9);
        Assert.Equal(0, PlayerMotor.WindPush(s with { Flags = PlayerFlags.Seated }, default, train, P, Tuning.Train));
        // Inside, or on the ground, no wind on your footing.
        Assert.Equal(0, PlayerMotor.WindPush(s with { Surface = Surface.Deck }, default, train, P, Tuning.Train));
    }

    [Fact]
    public void TheGustsComeFromBothSidesSmoothlyAndTheSameEverywhere()
    {
        double m = P.Wind.GustMetres;
        var gusts = Enumerable.Range(0, 2_000).Select(i => PlayerMotor.Gust(i * 5.0, m)).ToArray();
        Assert.All(gusts, g => Assert.InRange(g, -1, 1));
        Assert.Contains(gusts, g => g > 0.5);
        Assert.Contains(gusts, g => g < -0.5);
        // Eased, never a jump: 5 m of line moves it a little at most.
        for (int i = 1; i < gusts.Length; i++)
            Assert.True(Math.Abs(gusts[i] - gusts[i - 1]) < 0.2, $"a jump at {i * 5} m");
        Assert.Equal(PlayerMotor.Gust(1234.5, m), PlayerMotor.Gust(1234.5, m));
    }

    [Fact]
    public void InAWindAndColdNightPredictionStillMatchesTheHost()
    {
        // The wind's push changes the player's state, so a predicting client must compute it as the host does (its own copy
        // of the line: the hazard set is laid over the line it's given).
        var line = RailLine.Load(Path.Combine(DataFile.FindContentRoot(), "lines/test-loop.json"));
        var cold = Net.HazardSet.Clear with { Name = "cold", ColdStep = 2, Wind = 1.5 };
        var r = Net.Harness.Run(line, Tuning.Train, P, new Net.HarnessOptions { Bots = 8, Seconds = 45, Link = Ballast.Net.LinkConditions.Perfect, Hazards = cold });
        Assert.All(r.Clients, c => Assert.True(c.MaxCorrectionM < 0.001, $"player {c.Id} corrected by {c.MaxCorrectionM} m"));
    }

    [Fact]
    public void TheColdStepIsWhereYouAre()
    {
        var train = Train(new Weather(cold: 3));
        Assert.Equal(3, PlayerMotor.ColdStep(PlayerMotor.SpawnOnRoof(train, 2, 0, P), train));
        var plain = Train(null);
        Assert.Equal(0, PlayerMotor.ColdStep(PlayerMotor.SpawnOnRoof(plain, 2, 0, P), plain));
    }

    static (World World, Body Radio, Body Kit, PlayerState Mender) Mending()
    {
        var (world, radio, s) = Wearing(wrench: false);
        radio.Broken = true;
        var kit = world.Bodies.All.First(b => b.Kind == BodyKind.RepairKit);
        (kit.Carrier, kit.Locker) = (1, -1);
        Assert.Same(radio, world.Bodies.MendableRadio(s, world.Train, null, 1));
        return (world, radio, kit, s);
    }

    static void Act(World w, ref PlayerState s, PlayerIntent intent, double seconds)
    {
        for (int i = 0; i < Math.Round(seconds * SimConstants.TickRate); i++)
        {
            w.BeginTick();
            w.CrewAct(ref s, intent, 1);
        }
    }

    [Fact]
    public void TheRepairKitHeldAtABrokenRadioMendsIt()
    {
        var (w, radio, kit, s) = Mending();
        var use = new PlayerIntent { Buttons = PlayerButtons.Use };
        double mend = Tuning.Train.Kit.RadioMendSeconds;
        Act(w, ref s, use, mend - 1);
        Assert.True(radio.Broken);
        Assert.True(radio.MendTicks > 0);
        // Letting go starts it over, and a long hold's release keeps the kit in hand.
        Act(w, ref s, default, 0.1);
        Assert.Equal(0, radio.MendTicks);
        Assert.Equal(1, kit.Carrier);
        Act(w, ref s, use, mend - 1);
        Assert.True(radio.Broken);
        Act(w, ref s, use, 1.1);
        Assert.False(radio.Broken);
        Assert.True(w.Bodies.HasRadio(1));
        Assert.Equal(1, kit.Carrier);
        // On the body record as the host has it.
        Act(w, ref s, default, 0.1);
        Assert.Null(w.Bodies.MendableRadio(s, w.Train, null, 1));
    }

    [Fact]
    public void TheWrenchHeldAtABrokenRadioMendsItAndATapDoesNothing()
    {
        // Note 301: the kit's gone (none stowed); the wrench in empty hands mends a radio, held still as long as the kit took.
        var (w, radio, s) = Wearing();
        Assert.DoesNotContain(w.Bodies.All, b => b.Kind == BodyKind.RepairKit);
        radio.Broken = true;
        // The crowbar in hand: it's no mending tool.
        Assert.False(Bodies.Mends(s, w.Train, null));
        s.HeldSlot = 1;
        Assert.True(Bodies.Mends(s, w.Train, null));
        Act(w, ref s, new PlayerIntent { Buttons = PlayerButtons.Use }, 0.1);
        Act(w, ref s, default, 0.1);
        Assert.True(radio.Broken);
        Assert.Equal(Tool.Wrench, Kit.Held(s));
        Act(w, ref s, new PlayerIntent { Buttons = PlayerButtons.Use }, Tuning.Train.Kit.RadioMendSeconds + 0.1);
        Assert.False(radio.Broken);
    }

    [Fact]
    public void ATapStillPutsTheKitDownAndMendsNothing()
    {
        var (w, radio, kit, s) = Mending();
        Act(w, ref s, new PlayerIntent { Buttons = PlayerButtons.Use }, 0.1);
        Act(w, ref s, default, 0.1);
        Assert.Equal(-1, kit.Carrier);
        Assert.True(radio.Broken);
    }

    [Fact]
    public void WalkingAboutDoesntMend()
    {
        var (w, radio, _, s) = Mending();
        Act(w, ref s, new PlayerIntent { Buttons = PlayerButtons.Use, MoveX = 1 }, Tuning.Train.Kit.RadioMendSeconds + 1);
        Assert.True(radio.Broken);
    }

    [Fact]
    public void ABrokenRadioLyingInReachIsMendedToo()
    {
        var (w, radio, _, s) = Mending();
        // A crewmate took theirs off to hand it over: it's lying at your feet, and yours is whole.
        radio.Carrier = 2;
        var mine = w.Bodies.All.First(b => b.Kind == BodyKind.Radio && b != radio);
        mine.Carrier = 1;
        radio.Carrier = -1;
        foreach (ref var p in radio.Pbd.Particles.AsSpan())
            p.Position = s.Position + new Ballast.Double3(0, 1.15, -0.6);
        radio.Parent = s.Parent;
        Assert.Same(radio, w.Bodies.MendableRadio(s, w.Train, null, 1));
        Act(w, ref s, new PlayerIntent { Buttons = PlayerButtons.Use }, Tuning.Train.Kit.RadioMendSeconds + 0.1);
        Assert.False(radio.Broken);
    }

    [Fact]
    public void ADerailmentPutsEveryLampOut()
    {
        var world = new World(Train(null, 6));
        foreach (var v in world.Train.Vehicles)
            v.LampLit = true;
        world.Derail("test");
        Assert.All(world.Train.Vehicles, v => Assert.False(v.LampLit));
        Assert.True(world.LampOutSeconds > 1000);
    }
}
