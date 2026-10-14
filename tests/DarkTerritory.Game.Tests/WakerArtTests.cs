using System.Numerics;
using Ballast;
using Ballast.Assets;
using Ballast.Render;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Enemies;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The Waker, the Peat Man (GDD §8 "A titan"; ARCHITECTURE §8 note 679; tools/blender/waker.py): his stride covers the ground
/// it's played at (his feet stay where they come down, at any speed the Sim gives him), and holding the train, the end of the
/// car he's eating is at his mouth wherever <see cref="WakerLift"/>'s ramp has it.
/// </summary>
public class WakerArtTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly Look Look = Look.Load(Content);
    static readonly CreatureArt Art = new(Look, Content);
    static readonly WakersTuning Tuning = DataFile.Load<EnemyTuning>(Path.Combine(Content, EnemyTuning.File)).Wakers;

    static Model Waker => Art.Get("waker") ?? throw new FileNotFoundException("content/art/models/waker.glb: tools/models/build.sh waker");

    /// <summary>Where a bone's head is (model space) <paramref name="time"/> into a clip.</summary>
    static Vector3 BoneAt(Model m, string clip, double time, string bone)
    {
        var pose = new Pose(m.Skeleton.Count);
        new Skinner().Evaluate(m, m.Clip(clip), time, true, pose);
        return pose.World[m.Skeleton.IndexOf(bone)].Translation;
    }

    /// <summary>
    /// A planted foot is carried back under him at the speed his cycle covers the ground: walk and run alike, so played at
    /// <see cref="WakerGait"/>'s rate (his speed over the cycle's metres) the foot stays still on the ground (from the right
    /// foot's coming down, at 0, till its heel lifts, seven tenths of its stance on).
    /// </summary>
    [Theory]
    [InlineData("walk", WakerGait.WalkCycleMetres, WakerGait.WalkCycleSeconds, 0.05, 0.4)]
    [InlineData("run", WakerGait.RunCycleMetres, WakerGait.RunCycleSeconds, 0.03, 0.22)]
    public void APlantedFootIsCarriedBackAtTheSpeedTheCycleCovers(string clip, double metres, double seconds, double from, double to)
    {
        var m = Waker;
        Assert.Equal(seconds, m.Clip(clip).Duration, 2);
        var a = BoneAt(m, clip, from * seconds, "foot_r");
        var b = BoneAt(m, clip, to * seconds, "foot_r");
        // On the ground the whole way (the ankle's height stood), and going back (+Z: he faces −Z) at the cycle's speed.
        Assert.InRange(a.Y, 0.07f, 0.09f);
        Assert.InRange(b.Y, 0.07f, 0.09f);
        double speed = (b.Z - a.Z) * WakerGait.Scale / ((to - from) * seconds);
        Assert.Equal(metres / seconds, speed, metres / seconds * 0.04);
    }

    /// <summary>His run's cycle is wakers.topSpeed's: at the top of his ramp the run plays as authored.</summary>
    [Fact]
    public void HisRunIsHisTopSpeedAndHisFeetKeepTime()
    {
        const double run = WakerGait.RunCycleMetres / WakerGait.RunCycleSeconds;
        Assert.Equal(run, Tuning.TopSpeed, 0.1);
        // The cycles run on, ever faster up his ramp, steady after it: the right foot on each whole one.
        double last = -1;
        for (double s = 0; s < Tuning.RampSeconds + 20; s += 0.5)
        {
            double c = WakerGait.Cycles(s, Tuning);
            Assert.True(c > last || s == 0, $"{s} s: {c} cycles");
            last = c;
        }
        double steady = WakerGait.Cycles(Tuning.RampSeconds + 10, Tuning) - WakerGait.Cycles(Tuning.RampSeconds, Tuning);
        Assert.Equal(10 * Tuning.TopSpeed / WakerGait.RunCycleMetres, steady, 3);
        // A footfall every half cycle: the thud's just after one at the half, and the most it's ever been is a step.
        double half = WakerGait.CycleSeconds(Tuning.TopSpeed) / 2;
        for (double s = Tuning.RampSeconds; s < Tuning.RampSeconds + 8; s += 0.37)
            Assert.InRange(WakerGait.SinceFootfall(s, Tuning), 0, half + 1e-6);
        // Slow, heavy steps: a step takes him more than a second and a half at any speed he runs at.
        foreach (double v in new[] { Tuning.StartSpeed, 12.0, 18.0, Tuning.TopSpeed })
            Assert.True(WakerGait.CycleSeconds(v) / 2 > 1.5, $"{v} m/s: a step in {WakerGait.CycleSeconds(v) / 2:0.00} s");
    }

    /// <summary>
    /// Holding the train, he's moved along the line and bowed so the end of the car he's eating is at his mouth: low on the
    /// rail as he takes it, high up the ramp once the lift has the cars up (WakerLift.Step a car), and far ahead as he eats on
    /// down the train.
    /// </summary>
    [Theory]
    [InlineData(3.0, 4.0, -8.0)]
    [InlineData(5.0, 9.0, -20.0)]
    [InlineData(9.0, 20.0, -24.0)]
    [InlineData(17.0, 28.0, -40.0)]
    [InlineData(30.0, 12.0, -95.0)]
    public void TheCarsEndIsAtHisMouth(double held, double up, double ahead)
    {
        var target = new Vector3(0.3f, (float)up, (float)ahead);
        var mesh = new MeshBuilder();
        var at = Matrix4x4.CreateTranslation(5, -2, -30);
        Art.WakerIn = new WakerScene(Mouth: Vector3.Transform(target, at));
        try
        {
            Assert.True(Art.Enemy(mesh, at, EnemyKind.Waker, SpinePhase.Punish, held, held));
        }
        finally
        {
            Art.WakerIn = default;
        }
        var mouth = Art.WakerMouth(Art.WakerDrawn);
        Assert.NotNull(mouth);
        float off = Vector3.Distance(mouth.Value, Vector3.Transform(target, at));
        Assert.True(off < 1.5f, $"{held} s in, the car's end {up} m up and {-ahead} m ahead: his mouth {off:0.0} m off it");
    }

    /// <summary>
    /// Note 710: one risen beside whoever ran, holding them, has his right hand on them wherever his lift has them, from down at
    /// the ground to up at his mouth, his mouth come down to meet them by the end.
    /// </summary>
    [Theory]
    [InlineData(0.6, 0.15)]
    [InlineData(2.0, 0.5)]
    [InlineData(3.6, 0.9)]
    public void WhoHeTookIsInHisHand(double seconds, double k)
    {
        var t = Art.WakersTuning;
        var at = Matrix4x4.CreateTranslation(5, -2, -30);
        // Him facing −Z from his feet; who he took lifted from where they stood (his reach in front) toward his mouth.
        var from = new Vector3(0, 0, -(float)t.PickReach);
        var mouth = new Vector3(0, (float)t.PickMouthHeight, -(float)t.PickMouthOut);
        float s = (float)(k * k * (3 - 2 * k));
        var held = from + (mouth - from) * s + Vector3.UnitY;
        var mesh = new MeshBuilder();
        Art.WakerIn = new WakerScene(Mouth: Vector3.Transform(mouth, at), Picker: true, Hand: Vector3.Transform(held, at));
        try
        {
            Assert.True(Art.Enemy(mesh, at, EnemyKind.Waker, SpinePhase.Grab, seconds, 2));
        }
        finally
        {
            Art.WakerIn = default;
        }
        var hand = Art.WakerHand(Art.WakerDrawn);
        Assert.NotNull(hand);
        float off = Vector3.Distance(hand.Value, Vector3.Transform(held, at));
        Assert.True(off < 4f, $"{seconds} s into the lift, who he took {held.Y:0} m up: his hand {off:0.0} m off them");
    }

    /// <summary>
    /// The car he's eating is bitten off, not dropped out of sight (the director, 13 Oct 2026): from its rear end a quarter of
    /// it at a bite, the whole of it (underframe and trucks too), as his jaws shut; the other cars are left alone, and a Car
    /// Hugger's bite still comes off its car's own eating.
    /// </summary>
    [Fact]
    public void TheCarHesEatingIsBittenOffAQuarterAtABite()
    {
        var shape = Sim.Train.CarShape.Build(DataFile.Load<Sim.Train.TrainTuning>(Path.Combine(Content, Sim.Train.TrainTuning.File)).Geometry,
            Sim.Train.VehicleKind.Cargo, hasCarBehind: true);
        float l = (float)shape.HalfLength, rear = shape.Platform is { } p ? (float)p.Max.Z : l;
        Assert.False(Bite.Waker(shape, 0, 1).Any);
        Assert.False(Bite.Waker(shape, 0.24, 1).Any);
        float last = 0;
        foreach (double through in new[] { 0.26, 0.51, 0.76 })
        {
            var bite = Bite.Waker(shape, through, 1);
            Assert.True(bite.Centre > last + 1, $"{through}: a bite further in");
            last = bite.Centre;
            // Down to the rails: the trucks and the underframe go with the rest.
            Assert.True(bite.Eats(new Vector3(0, 0.3f, rear - 0.2f)), "the underframe at the end");
            Assert.True(bite.Eats(new Vector3(bite.HalfWidth - 0.05f, 2, rear - 0.2f)), "a side wall at the end");
        }
        Assert.Equal((rear + l) * 3 / 4, last, 2);
        // The front of the car is there till the last bite, which the sim's eating takes.
        Assert.False(Bite.Waker(shape, 0.99, 1).Eats(new Vector3(0, 2, -l + 0.5f)));
        var art = new SceneArt(Look);
        art.WakerEating = (5, 0.6);
        Assert.True(art.BiteOf(shape, null, 5).Any);
        Assert.False(art.BiteOf(shape, null, 4).Any);
        art.WakerEating = null;
        Assert.False(art.BiteOf(shape, null, 5).Any);
    }

    /// <summary>Every phase of his draws him: lying, rising, running (slow and at speed), at the walls, holding the train.</summary>
    [Fact]
    public void EveryPhaseDrawsHim()
    {
        var mesh = new MeshBuilder();
        var at = Matrix4x4.CreateTranslation(0, 0, -200);
        foreach (var (phase, seconds, walls) in new[] { (SpinePhase.Dormant, 1.0, false), (SpinePhase.Telegraph, 1.0, false),
                     (SpinePhase.Telegraph, 7.0, false), (SpinePhase.Commit, 2.0, false), (SpinePhase.Commit, 60.0, false),
                     (SpinePhase.Commit, 60.0, true), (SpinePhase.Commit, 66.0, true), (SpinePhase.Punish, 1.0, false), (SpinePhase.Punish, 12.0, false) })
        {
            mesh.Clear();
            Art.WakerIn = new WakerScene(walls);
            Assert.True(Art.Enemy(mesh, at, EnemyKind.Waker, phase, seconds, phase == SpinePhase.Punish ? seconds : 0), $"{phase} {seconds} s");
            Assert.NotEmpty(mesh.Instances);
            Art.WakerIn = default;
        }
        foreach (var (phase, s, extra) in new[] { (SpinePhase.Dormant, 0.0, 0.0), (SpinePhase.Telegraph, 4.0, 0.0), (SpinePhase.Punish, 2.0, 2.0),
                     (SpinePhase.Punish, 3.0, 3.0),
                     (SpinePhase.Punish, 13.0, 13.0) })
            Assert.Contains(CreatureArt.WakerClip(phase, s, extra, false, Tuning).Clip, Waker.ClipNames);
        Assert.Equal("lift", CreatureArt.WakerClip(SpinePhase.Punish, 4, 4, false, Tuning).Clip);
        Assert.Equal("eat", CreatureArt.WakerClip(SpinePhase.Punish, Tuning.EatAfter + 0.5, Tuning.EatAfter + 0.5, false, Tuning).Clip);
        Assert.Equal("roar", CreatureArt.WakerClip(SpinePhase.Commit, 61, 0, true, Tuning).Clip);
        Assert.Equal("stand", CreatureArt.WakerClip(SpinePhase.Commit, 66, 0, true, Tuning).Clip);
        Assert.Equal(CreatureArt.WakerRiseClipSeconds, Waker.Clip("rise").Duration, 2);
        Assert.Equal(CreatureArt.WakerEatClipSeconds, Waker.Clip("eat").Duration, 2);
        Assert.Equal(CreatureArt.WakerGrabSeconds, Waker.Clip("grab").Duration, 2);
    }
}
