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

    static TrainOnLine Train(ITrackConditions? conditions, int cars = 5) =>
        new(new TrainDynamics(Consist.Uniform(Tuning.Train, cars, 1)), new RailLine(new LineDefinition("t", [new TrackSegment(40_000)])) { Conditions = conditions }, 2_000);

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

    static (World World, Body Radio, PlayerState Wearer) Wearing(int id = 1)
    {
        var world = new World(Train(null, 6));
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
