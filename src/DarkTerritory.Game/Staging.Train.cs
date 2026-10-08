using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game;

/// <summary>
/// The train's own, staged for screenshots (<c>dt screenshot --brakeman | --knotter | --hotboxbug</c>; notes 364, 365, 367):
/// the Brakeman on the second car's roof, the Knotter in the coupling behind it, Hotbox in its rear truck; the train as they
/// leave it (its brakes wound, the gap forced, the axle seized), the crew round them, and the cameras that look at them.
/// </summary>
public static partial class Staging
{
    public const int BrakemanId = 364, KnotterId = 365, HotboxId = 367;
    /// <summary>The car they're staged on (the roof view's).</summary>
    public const int TrainCar = 2;
    /// <summary>Crewmates 6 and 7: the Brakeman's chaser and the one who cuts him off, a hand at the Knotter's gap, a prising bar.</summary>
    public const byte ChaserId = 6, FlankerId = 7;

    /// <summary>
    /// The train as the staged ones leave it, before the camera's placed (the Knotter's gap lays the cars out): going at
    /// <paramref name="speed"/> (m/s; or each's own: the Brakeman's 8, the Knotter's 15 taut and standing slack, Hotbox's 9
    /// clamped and standing unfolded); the cars behind the Brakeman wound on; the coupling behind <see cref="TrainCar"/> forced
    /// as far as the Knotter's got (none creeping, half forcing, all of it after); Hotbox's car's axle seized.
    /// </summary>
    public static void TrainfolkTrain(TrainOnLine train, string brakeman, string knotter, string hotbox, double speed = -1)
    {
        if (brakeman.Length + knotter.Length + hotbox.Length == 0)
            return;
        double own = knotter.Length > 0 ? knotter is "slack" or "exposed" ? 0 : 15
            : hotbox.Length > 0 ? hotbox is "unfolded" or "snap" or "prised" ? 0 : 9 : 8;
        train.Dynamics.Velocity = speed >= 0 ? speed : own;
        int car = Math.Min(TrainCar, train.Vehicles.Count - 1);
        if (brakeman.Length > 0)
            for (int c = car + 1; c < train.Vehicles.Count; c++)
                train.Vehicles[c].Wound = true;
        if (brakeman is "wind" && car < train.Vehicles.Count)
            train.Vehicles[car].Wound = false;
        if (knotter.Length > 0 && car + 1 < train.Vehicles.Count)
        {
            double full = new KnotterTuning().Gap - train.Dynamics.Tuning.Geometry.CouplingGap;
            train.Vehicles[car].Knot = knotter switch { "creep" => 0, "force" => full * 0.45, _ => full };
        }
        if (hotbox is "seized" or "unfolded" or "snap" or "prised")
            train.Vehicles[car].Seized = true;
        train.RefreshFrames();
    }

    /// <summary>
    /// The staged ones (dt screenshot): <c>--brakeman walk|wind|flee|cornered|lash|climb|drop</c> on <see cref="TrainCar"/>'s
    /// roof, at its rear brake wheel winding it, or walking, running from crewmate 6, cornered between 6 and 7 (and lashing);
    /// <c>--knotter creep|force|taut|slack|coil</c> in the coupling behind it, coiled round crewmate 4 slipped off its back;
    /// <c>--hotboxbug knock|glow|seized|unfolded|snap|prised</c> in its rear truck on the right, out on the ballast with crewmate
    /// 6 at it with a bar. Returns the enemies and the crew.
    /// </summary>
    public static (List<Enemy> Enemies, List<Crewmate> Crew) Trainfolk(List<Enemy> threats, IEnumerable<Crewmate> crew, TrainOnLine train,
        string brakeman, string knotter, string hotbox)
    {
        var people = crew.Where(c => c.Id is not (LoneId or ChaserId or FlankerId)).ToList();
        var frame = train.Frames[Math.Min(TrainCar, train.Frames.Count - 1)];
        int car = frame.Index;
        var shape = frame.Shape;
        double roof = shape.RoofHeight, l = shape.HalfLength;
        Crewmate Standing(byte id, double x, double z, double yaw, Sim.Player.Tool tool = Sim.Player.Tool.Crowbar, Art.CrewPose? act = null,
            double y = double.NaN)
        {
            var local = new Double3(x, double.IsNaN(y) ? roof : y, z);
            var fwd = frame.DirToWorld(new Double3(-Math.Sin(yaw), 0, -Math.Cos(yaw)));
            return new Crewmate(id, frame.ToWorld(local), Math.Atan2(-fwd.X, -fwd.Z), true, Act: act, Holding: tool, Car: car, Local: local);
        }
        if (brakeman.Length > 0)
        {
            threats.RemoveAll(e => e is Brakeman);
            var t = new BrakemanTuning();
            var wheel = shape.Interactables.First(i => i.Kind == Sim.Train.InteractableKind.Handbrake).Position;
            var (mode, phase, seconds, z) = brakeman switch
            {
                "walk" => (BrakemanMode.Walk, SpinePhase.Dormant, 1.3, l - 4.0),
                "wind" => (BrakemanMode.Wind, SpinePhase.Dormant, 0.35, wheel.Z),
                "flee" => (BrakemanMode.Flee, SpinePhase.Alert, 0.9, -1.0),
                "cornered" => (BrakemanMode.Cornered, SpinePhase.Telegraph, 1.1, -1.0),
                "lash" => (BrakemanMode.Cornered, SpinePhase.Telegraph, t.LashEvery - Art.CreatureArt.BrakemanLashStrike + 0.42, -1.0),
                "climb" => (BrakemanMode.Climb, SpinePhase.Dormant, 0.9, l - 0.5),
                "drop" => (BrakemanMode.Hidden, SpinePhase.Dormant, 0.55, -1.0),
                _ => throw new ArgumentException($"--brakeman {brakeman}: walk, wind, flee, cornered, lash, climb or drop"),
            };
            var b = new Brakeman(BrakemanId);
            double top = shape.TopAt(0, z)?.Top ?? roof;
            // (Working toward the engine: facing forward, −Z; running from a chaser behind him, the same.)
            b.Restore(phase, seconds, t.Health, car, new Double3(0, top, z), train.Dynamics.Distance, 0, (double)mode, 0, 1);
            threats.Add(b);
            if (brakeman is "flee")
                people.Add(Standing(ChaserId, 0.2, z + 6, 0, act: Art.CrewPose.Run));
            if (brakeman is "cornered" or "lash")
            {
                people.Add(Standing(ChaserId, 0.25, z + 3.6, 0));
                people.Add(Standing(FlankerId, -0.25, z - 3.4, Math.PI));
            }
        }
        if (knotter.Length > 0)
        {
            threats.RemoveAll(e => e is Knotter);
            var t = new KnotterTuning();
            var g = train.Dynamics.Tuning.Geometry;
            double gap = g.CouplingGap + train.Vehicles[car].Knot;
            var (mode, phase, seconds) = knotter switch
            {
                "creep" => (KnotterMode.Creep, SpinePhase.Alert, 1.6),
                "force" => (KnotterMode.Force, SpinePhase.Dormant, 2.5),
                "taut" => (KnotterMode.Taut, SpinePhase.Telegraph, 3.0),
                "slack" or "exposed" => (KnotterMode.Slack, SpinePhase.Telegraph, 2.0),
                "coil" => (KnotterMode.Coil, SpinePhase.Grab, 0.9),
                _ => throw new ArgumentException($"--knotter {knotter}: creep, force, taut, slack or coil"),
            };
            var k = new Knotter(KnotterId);
            k.Restore(phase, seconds, t.Health, car, new Double3(g.PlateX, g.CouplerHeight, l + gap / 2), train.Dynamics.Distance, 0, (double)mode, 0, 0,
                holding: mode == KnotterMode.Coil ? LoneId : -1, grabWindow: t.CoilSeconds);
            threats.Add(k);
            if (mode == KnotterMode.Coil)
            {
                // Slipped off its back a third of the way across, half down the far side of it, held.
                var local = new Double3(g.PlateX + 0.15, g.CouplerHeight - 0.35, l + gap * 0.38);
                var fwd = frame.DirToWorld(new Double3(0.6, 0, -0.8));
                people.Add(new Crewmate(LoneId, frame.ToWorld(local), Math.Atan2(-fwd.X, -fwd.Z), true, Act: Art.CrewPose.HeldSeized, Car: car, Local: local));
                // And a friend on the plate's end at the car's edge, reaching for them.
                people.Add(Standing(ChaserId, g.PlateX - 0.1, l + 0.35, Math.PI, Sim.Player.Tool.None, Art.CrewPose.Crouch, g.CouplerHeight));
            }
            else
            {
                // A crewmate at the car's end holding the lamp out over the gap, looking down at it: its light on the rope.
                people.Add(Standing(ChaserId, -0.45, l - 0.45, Math.PI, Sim.Player.Tool.None, Art.CrewPose.Lantern) with { Lamp = true });
            }
        }
        if (hotbox.Length > 0)
        {
            threats.RemoveAll(e => e is Hotbox);
            var t = new HotboxTuning();
            var (mode, phase, seconds) = hotbox switch
            {
                "knock" => (HotboxMode.Knock, SpinePhase.Dormant, 3.0),
                "glow" => (HotboxMode.Glow, SpinePhase.Dormant, 2.0),
                "seized" => (HotboxMode.Seized, SpinePhase.Dormant, 2.0),
                "unfolded" => (HotboxMode.Unfolded, SpinePhase.Telegraph, 1.6),
                "snap" => (HotboxMode.Unfolded, SpinePhase.Telegraph, t.SnapEvery - Art.CreatureArt.HotboxSnapStrike + 0.05),
                "prised" => (HotboxMode.Prised, SpinePhase.BreakOff, 1.4),
                _ => throw new ArgumentException($"--hotboxbug {hotbox}: knock, glow, seized, unfolded, snap or prised"),
            };
            var heat = mode switch { HotboxMode.Knock => 30, HotboxMode.Glow => t.KnockSeconds + 40, _ => t.KnockSeconds + t.GlowSeconds + 5 };
            var bug = Hotbox.In(HotboxId, train, car, rear: true, side: 1, t);
            bug.Restore(phase, seconds, t.Health, car, bug.Local, train.Dynamics.Distance, 1, (double)mode, heat, 0);
            threats.Add(bug);
            if (mode is HotboxMode.Unfolded or HotboxMode.Prised)
                people.Add(Standing(ChaserId, shape.HalfWidth + 0.7, l - t.BogieInset + 1.5, 0.6, Sim.Player.Tool.Crowbar, Art.CrewPose.Pry, 0));
        }
        return (threats, people);
    }

    /// <summary>
    /// The cameras on them (dt screenshot): <c>brakeman</c> from up the roof ahead of him at his wheel; <c>brakemancorner</c>
    /// over the shoulder of one of the two who've cornered him; <c>knotter</c> from the next car's roof down into the gap
    /// it holds at speed; <c>knotterslip</c> from the car's end over the one it's coiled round; <c>hotboxbug</c> from the
    /// lineside beside its truck; <c>hotboxout</c> low on the ballast at it, out of the truck at a stand.
    /// </summary>
    public static Camera TrainfolkCamera(TrainOnLine train, string view)
    {
        var frame = train.Frames[Math.Min(TrainCar, train.Frames.Count - 1)];
        var shape = frame.Shape;
        double roof = shape.RoofHeight, l = shape.HalfLength, w = shape.HalfWidth;
        var wheel = shape.Interactables.First(i => i.Kind == Sim.Train.InteractableKind.Handbrake).Position;
        var g = train.Dynamics.Tuning.Geometry;
        double gap = g.CouplingGap + train.Vehicles[frame.Index].Knot;
        var t = new HotboxTuning();
        double truck = l - t.BogieInset;
        return view switch
        {
            "brakeman" => Camera.LookAt(frame.ToWorld(new Double3(-1.7, roof + 1.45, wheel.Z - 2.2)), frame.ToWorld(new Double3(0.05, roof + 0.6, wheel.Z)), 52),
            "brakemancorner" => Camera.LookAt(frame.ToWorld(new Double3(1.4, roof + 1.7, -1.0 + 4.6)), frame.ToWorld(new Double3(-0.1, roof + 0.9, -1.0)), 55),
            "knotter" => Camera.LookAt(frame.ToWorld(new Double3(1.3, roof + 1.5, l + gap + 0.2)), frame.ToWorld(new Double3(g.PlateX, g.CouplerHeight, l + gap * 0.5)), 52),
            "knotterslip" => Camera.LookAt(frame.ToWorld(new Double3(w + 2.2, 2.3, l + gap * 0.5 + 1.9)), frame.ToWorld(new Double3(g.PlateX, g.CouplerHeight + 0.2, l + gap * 0.4)), 58),
            "hotboxbug" => Camera.LookAt(frame.ToWorld(new Double3(w + 2.4, 0.9, truck + 2.6)), frame.ToWorld(new Double3(w - t.Inboard, t.AxleHeight, truck)), 50),
            _ => Camera.LookAt(frame.ToWorld(new Double3(w + 1.5, 0.35, truck - 1.0)), frame.ToWorld(new Double3(w - 0.1, -0.1, truck - 0.15)), 55),
        };
    }
}
