using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Art;

/// <summary>
/// The art pass as the scene draws it: the kit's cooked pieces placed by transform each frame, with what moves (doors,
/// the gun's facing, the lamps' glow) placed from the sim's state. One per <see cref="Look"/>; pieces are cooked the
/// first time they're wanted and kept.
/// </summary>
public sealed partial class SceneArt(Look look)
{
    readonly Dictionary<string, MeshAsset> _pieces = new();

    public Look Look { get; } = look;

    /// <summary>What a village find is (loot.json's item key), from the run: its model (FindKit). Null, the plain bundle.</summary>
    public Func<Sim.Physics.Body, string?>? FindItem { get; set; }

    /// <summary>The line and its lineside.</summary>
    public WorldArt World { get; } = new(look);

    CreatureArt? _creatures;
    readonly Dictionary<byte, (Double3 At, int Car, double Time, float Speed)> _crewMotion = new();

    /// <summary>The crew and the creatures, skinned (content/art/models, tools/blender); loaded on first use.</summary>
    public CreatureArt Creatures => _creatures ??= new CreatureArt(Look);

    /// <summary>
    /// A crewmate as the crew model, walking or running by how fast they've moved since last drawn (the snapshot
    /// doesn't say; this is presentation only, so a frame's lag in the gait doesn't matter). On a car that's over the car,
    /// in its frame (note 211): stood still on a train at speed is stood still. False without the model.
    /// </summary>
    /// <param name="swung">Seconds since a blow of theirs landed (a HitConfirm by them, App. C.2), or negative: their swing
    /// is played round it, so another crewmate's blow is seen as well as felt.</param>
    public bool Crewmate(MeshBuilder mesh, Crewmate c, Double3 eye, double time, double swung = -1)
    {
        float speed = 0;
        // Measured in the frame they stand in: the car's, or the ground's (World). The step they change frames on (a car to
        // the next, off onto the ballast) has its two samples in different frames, so it keeps the pace they had.
        var at = c.Car != Sim.Player.PlayerState.World ? c.Local : c.Feet;
        if (_crewMotion.TryGetValue(c.Id, out var last) && time > last.Time)
        {
            speed = last.Speed;
            if (last.Car == c.Car)
            {
                var d = at - last.At;
                float moved = (float)Math.Sqrt(d.X * d.X + d.Z * d.Z);
                float now = moved / (float)(time - last.Time);
                // Smoothed a little, so the gait doesn't flicker between clips on one jittery snapshot.
                speed = float.Lerp(last.Speed, now, 0.35f);
            }
        }
        _crewMotion[c.Id] = (at, c.Car, time, speed);
        var pose = c.Act switch
        {
            CrewPose.Carry => speed < 0.4f ? CrewPose.Carry : CrewPose.CarryWalk,
            CrewPose.Shoulder => speed < 0.4f ? CrewPose.Shoulder : CrewPose.ShoulderWalk,
            CrewPose.Cradle => speed < 0.4f ? CrewPose.Cradle : CrewPose.CradleWalk,
            CrewPose.Lantern => speed < 0.4f ? CrewPose.Lantern : CrewPose.LanternWalk,
            // Across the plate, short careful steps; stood on it, balancing (GDD §32).
            CrewPose.Gap => speed < 0.4f ? CrewPose.Gap : CrewPose.GapStep,
            { } act => act,
            // Running with something waking close by, hunched and hurried (GDD §31).
            null => speed < 0.4f ? CrewPose.Idle : speed < 2.6f ? CrewPose.Walk : c.Stressed ? CrewPose.Hurry : CrewPose.Run,
        };
        // An emote (note 298), stood still with nothing else on.
        if (pose == CrewPose.Idle && c.Emote != Sim.Player.Emote.None)
            pose = c.Emote switch { Sim.Player.Emote.Dance => CrewPose.Dance, Sim.Player.Emote.Wave => CrewPose.Wave, _ => CrewPose.Point };
        // The extinguisher at work: braced into it and kicking with the jet while the fire's going down under it (GreyboxScene
        // sees that: Spraying), not stood with it on the hip. Come to its bracket already carrying it, it's being hung back:
        // lifted up onto it, not off it (TakeDown is the sim's "at the mount with it" either way), and that plays on through
        // the drop, until it's done, rather than snapping back to stood (App. C.5; the checklist's "a distinct hang-back").
        if (pose == CrewPose.Extinguish && Spraying?.Contains(c.Id) == true)
            pose = CrewPose.Spray;
        var before = _crewActSince.TryGetValue(c.Id, out var wasDoing) ? wasDoing : default;
        if (pose == CrewPose.TakeDown && before.Pose is CrewPose.Extinguish or CrewPose.Spray or CrewPose.HangUp)
            pose = CrewPose.HangUp;
        else if (before.Pose == CrewPose.HangUp && pose is CrewPose.Idle or CrewPose.Walk && time - before.Time < HangUpSeconds)
            pose = CrewPose.HangUp;
        // Over the top of a jump (rising it's the sim's Jump, then nothing till they're falling fast): the leap held through
        // it, so the walk doesn't flicker in between.
        if (before.Pose == CrewPose.Jump && pose is CrewPose.Idle or CrewPose.Walk or CrewPose.Run or CrewPose.Hurry && time - before.Time < JumpHoldSeconds)
            pose = CrewPose.Jump;
        // Stood or walking on a car straining on a bend taken too fast (BendStrain; App. F.1's overspeed telegraph), past the
        // judder: fighting for footing, arms out (note 375). The sim's lurch throws them off later still (Lineside).
        if (pose is CrewPose.Idle or CrewPose.Walk or CrewPose.Run or CrewPose.Hurry or CrewPose.Gap or CrewPose.GapStep
            && BendStrain is { } bends && c.Car >= 0 && c.Car < bends.Count && bends[c.Car].Stress >= StumbleAt)
            pose = CrewPose.Stumble;
        LastPose = pose;
        // A blow taken (their health down since last drawn): rocked back a step, unless their hands are busy with something.
        if (_crewHealth.TryGetValue(c.Id, out int was) && c.Health < was && c.Alive)
            _staggered[c.Id] = time;
        _crewHealth[c.Id] = c.Health;
        bool free = c.Act is null or CrewPose.Carry or CrewPose.Lantern;
        if (free && _staggered.TryGetValue(c.Id, out double hit) && time - hit < StaggerSeconds)
            pose = CrewPose.Stagger;
        // At the firehole as its door moves: the one who swung it (whoever's stood there; the sim doesn't say who).
        if (FireDoorAt is { } door && FireDoorSince is >= 0 and < FireDoorSeconds && c.Act is null or CrewPose.Shovel or CrewPose.Drive
            && ((c.Feet - door) with { Y = 0 }).Length < FireDoorReach)
            pose = CrewPose.FireDoor;
        // Their own blow (it landed swungBeforeHit into the clip): played round it, wherever they stand.
        if (free && swung >= 0 && swung + SwingHitAt < SwingSeconds)
            pose = CrewPose.Swing;
        // When this act began, for the ones played once from the start (getting up, a thing off its bracket).
        if (!_crewActSince.TryGetValue(c.Id, out var since) || since.Pose != pose)
            _crewActSince[c.Id] = since = (pose, time);
        double clipTime = pose switch
        {
            CrewPose.GetUp or CrewPose.TakeDown or CrewPose.HangUp => time - since.Time,
            CrewPose.Stagger => time - _staggered.GetValueOrDefault(c.Id, since.Time),
            CrewPose.FireDoor => FireDoorSince >= 0 ? FireDoorSince : time - since.Time,
            // A staged swing (dt screenshot --act swing) says how far into it they are; a played one, from its blow.
            CrewPose.Swing => swung >= 0 ? swung + SwingHitAt : c.Phase,
            // The reload's beats follow the gun's own progress, not a clock (CrewActs.ReloadPhase).
            CrewPose.Reload => c.Phase,
            // A staged leap says how far into it they are (dt screenshot --act jump); a lived one, from the spring.
            CrewPose.Jump => c.Phase > 0 ? c.Phase : time - since.Time,
            // Up a ladder by how far up it they are, not by the clock: one cycle of crew_clips' climb is two rungs climbed,
            // so the hands and feet stay on the rungs at any pace and stop when the climber does (a Look Review note).
            CrewPose.Climb or CrewPose.ClimbCarry => at.Y / ClimbCycleRise * ClimbCycleSeconds,
            CrewPose.Dance or CrewPose.Wave or CrewPose.Point => c.EmoteSeconds,
            _ => time,
        };
        var right = new Vector3((float)Math.Cos(c.Yaw), 0, (float)-Math.Sin(c.Yaw));
        var back = new Vector3((float)Math.Sin(c.Yaw), 0, (float)Math.Cos(c.Yaw));
        var m = CreatureArt.Basis(c.Feet.RelativeTo(eye), right, Vector3.UnitY, back);
        // A headset player's hands where they are (T47), the same way GreyboxScene's figure has them; the other arm (and
        // everyone's, on a keyboard) stays with the clip's swing.
        Vector3? left = null, rightHand = null;
        if (c.Reach is { } reach)
        {
            // Hands on what the act works (the regulator and the brake, the whistle cord: CrewActs.Crewmate), each to its side.
            var (l, r) = Arms.Hands(reach.A, reach.B);
            left = ToF(l);
            rightHand = ToF(r);
        }
        else if (c.Hand != default || c.Other != default)
        {
            var (l, r) = Arms.Hands(c.Hand, c.Other);
            if (l != Arms.Hanging(-1))
                left = ToF(l);
            if (r != Arms.Hanging(1))
                rightHand = ToF(r);
        }
        var lamp = c.Lamp ? PropArt.Of(Look).Get("hand_lantern") : null;
        bool drawn = Creatures.Crewmate(mesh, m, pose, clipTime, c.Variant, left, rightHand, ToF(Arms.Pole(-1)), ToF(Arms.Pole(1)), ToolProp(c.Holding),
            hanging: lamp, figure: CreatureArt.FigureOf(c.Survivor), body: HeadsetBody(c, pose, time));
        // Their breath in the cold (GDD §26): out on the beat of their breathing, a puff of vapour from the mouth that
        // goes out the way they face and rises, gone in a second and a half; harder breathing (running, hauling) quicker.
        if (drawn && Breath > 0 && c.Alive)
            Look.Art.Effects.Breath(mesh, Creatures.LastMouth, Creatures.LastFacing, Breath, time, c.Id,
                pose is CrewPose.Run or CrewPose.Haul or CrewPose.HaulUp or CrewPose.Shovel or CrewPose.Smash or CrewPose.Pry);
        if (drawn && lamp is not null)
        {
            // Its glow where it hangs, swinging with the hand; and its light there next frame (GreyboxScene's practical lights
            // are laid before the crew are drawn: a frame behind is nothing at a walk).
            var flame = Creatures.LastHanging;
            float flicker = Flicker(time, c.Id);
            mesh.Billboard(flame, 0.7f * flicker, 0, new Vector4(Palette.LampAmber * 0.55f * flicker, 1), -1, FxBlend.Additive);
            _lampHands[c.Id] = (eye + new Double3(flame.X, flame.Y, flame.Z), time);
        }
        else
            _lampHands.Remove(c.Id);
        return drawn;
    }

    /// <summary>
    /// A headset crewmate's body under their head (T82, <see cref="VrBody"/>), stood still: walking and running are the
    /// clips', and so is any act (the lever, the shovel, a hold). Their feet are kept planted from frame to frame in the
    /// frame they stand in, and planted afresh when they start, stop or move frames.
    /// </summary>
    VrBodyPose? HeadsetBody(in Crewmate c, CrewPose pose, double time) =>
        _strides.Pose(c, c.Alive && c.Act is null && pose == CrewPose.Idle, time, Look.VrBody);

    readonly VrStrides _strides = new();

    /// <summary>The pace-chosen pose of the crewmate last drawn (before a stagger, a swing or the fire door take over).</summary>
    public CrewPose LastPose { get; private set; }

    readonly Dictionary<int, (Double3 At, double Time)> _lampHands = new();
    readonly Dictionary<byte, int> _crewHealth = new();
    readonly Dictionary<byte, double> _staggered = new();

    /// <summary>The firehole's door in the world (GreyboxScene sets it with the cab), and how long since it last moved (s).</summary>
    public Double3? FireDoorAt { get; set; }
    public double FireDoorSince { get; set; } = -1;

    /// <summary>crew_clips.py's firedoor clip (21 frames); how near the door's foot someone stands to be the one at it (m).</summary>
    const double FireDoorSeconds = 21 / 30.0, FireDoorReach = 1.3;

    /// <summary>crew_clips.py's climb and climb_carry: one 40-frame cycle per two rungs (TrainKit.RungPitch) climbed.</summary>
    const double ClimbCycleSeconds = 40 / 30.0, ClimbCycleRise = 2 * TrainKit.RungPitch;

    /// <summary>crew_clips.py's stagger (20 frames) and swing (24 frames, the blow landing at frame 11), in seconds.</summary>
    const double StaggerSeconds = 20 / 30.0, SwingSeconds = 24 / 30.0, SwingHitAt = 11 / 30.0;

    /// <summary>How much a breath shows tonight, 0..1 (look.json atmosphere.cold, GreyboxScene.Cold).</summary>
    public float Breath { get; set; }

    /// <summary>
    /// A car of livestock (GDD §19 "makes noise constantly", the slaughterhouse's): sheep packed down the load side in its
    /// pen (tools/blender/sheep.py), each on its own beat: mostly shifting and shuffling, every few seconds one bleating,
    /// now and then one startled by the noise. The noise is seen as well as heard.
    /// </summary>
    void Livestock(MeshBuilder mesh, in CarFrame frame, Double3 eye, long tick)
    {
        var m = FrameMatrix(frame, eye);
        double t = (tick < 0 ? 0 : tick) / (double)Sim.SimConstants.TickRate;
        int n = 0;
        foreach (var solid in frame.Shape.Solids.Where(x => x.Part == PartKind.Cargo))
        {
            var box = solid.Box;
            int count = Math.Max(1, (int)((box.Max.Z - box.Min.Z) / 0.95));
            double step = (box.Max.Z - box.Min.Z) / count;
            for (int i = 0; i < count; i++, n++)
            {
                uint h = (uint)(n * 2654435761u ^ (uint)frame.Index * 40503u);
                double z = box.Min.Z + step * (i + 0.5);
                double x = box.Centre.X + (i % 2 == 0 ? -0.14 : 0.14) + ((int)((h >> 4) % 3) - 1) * 0.04;
                float yaw = ((h & 1) == 0 ? 0 : MathF.PI) + ((int)((h >> 8) % 7) - 3) * 0.12f;
                // Its beat: a 7 s round of idling and shuffling, a bleat in it at its own offset, a startle one round in five.
                double phase = t + (h % 97) * 0.13;
                int round = (int)(phase / 7);
                double inRound = phase - round * 7;
                bool startled = (uint)(round * 31 + h) % 5 == 0;
                var (clip, ct) = inRound < 2.4 ? ("bleat", inRound) : startled && inRound < 3.7 ? ("startle", inRound - 2.4)
                    : ((h + (uint)round) % 2 == 0 ? "shuffle" : "idle", inRound);
                var at = Matrix4x4.CreateRotationY(yaw) * Matrix4x4.CreateTranslation((float)x, (float)box.Min.Y + 0.05f, (float)z) * m;
                Creatures.Draw(mesh, "sheep", clip, ct, true, at, seed: (int)(h % 13));
            }
        }
    }

    /// <summary>What a gutted car's paint and boards go to: charcoal, a little warm where the timber's charred through.</summary>
    static readonly Vector3 CharTint = new(0.34f, 0.3f, 0.28f);

    /// <summary>The comet material's light (GDD §19): a sick, unnatural green, nothing like a lamp's.</summary>
    static readonly Vector3 CometGlow = new(0.35f, 0.95f, 0.45f);

    /// <summary>
    /// The couplers that have been cut (T91): each vehicle end (its id × 2, + 1 for the rear) with nothing coupled to it
    /// where the car it was cut from (the next id along) is still about, in another rake. A train's own ends stay shut.
    /// </summary>
    public static HashSet<int> Cuts(TrainOnLine train)
    {
        var cut = new HashSet<int>();
        var ids = train.Vehicles.Select(v => v.Id).ToHashSet();
        foreach (var rake in train.Rakes)
        {
            var vs = rake.Consist.Vehicles;
            if (vs.Count == 0)
                continue;
            if (ids.Contains(vs[0].Id - 1) && !vs.Any(v => v.Id == vs[0].Id - 1))
                cut.Add(vs[0].Id * 2);
            if (ids.Contains(vs[^1].Id + 1) && !vs.Any(v => v.Id == vs[^1].Id + 1))
                cut.Add(vs[^1].Id * 2 + 1);
        }
        return cut;
    }

    /// <summary>
    /// Where crewmate <paramref name="carrier"/>'s hand lamp hangs (world), drawn from their fist last frame (GDD §31: the light
    /// moves with it); null if they weren't drawn with it.
    /// </summary>
    public Double3? LampInHand(int carrier, double time) =>
        _lampHands.TryGetValue(carrier, out var h) && time - h.Time < 0.5 ? h.At : null;

    readonly Dictionary<byte, (CrewPose Pose, double Time)> _crewActSince = new();

    /// <summary>The crew whose extinguisher is at work on a fire this frame (GreyboxScene: a fire going down with it in reach).</summary>
    public IReadOnlySet<int>? Spraying { get; set; }

    /// <summary>Each car's strain on a bend taken too fast (GreyboxScene's, BendStrain.PerCar), by frame: the crew on it stumble.</summary>
    public IReadOnlyList<(float Stress, int Outer)>? BendStrain { get; set; }

    // How strained a car is before the crew on it stumble: past halfway to derailing, where the eye's judder is plain
    // (BendStrain.Offset starts at 0.2) and the flanges' haze comes on.
    const float StumbleAt = 0.5f;

    // How long a jump's leap is held from its start (s): over the top, until they're falling or down (crew_clips.py's jump).
    const double JumpHoldSeconds = 0.75;

    // How long hanging the extinguisher back on its bracket takes (s): crew_clips.py's hang_up, 40 frames at 30.
    const double HangUpSeconds = 40 / 30.0;

    /// <summary>Your own forearms and hands in view, with the tool in them (X3). False without the crew model.</summary>
    public bool OwnArms(MeshBuilder mesh, in OwnView own, double time) =>
        Creatures.OwnArms(mesh, own.Yaw, own.Pitch, own.Act, own.Moving, own.Swing, time, own.Variant, ToolProp(own.Holding));

    /// <summary>A hotbar tool's model (tools/models hand_tools), or null for none.</summary>
    /// <summary>A headset player's own gloved hands on their controllers, the tool in hand in the right (CreatureArt.HeadsetHands).</summary>
    public bool HeadsetHands(MeshBuilder mesh, float yaw, in Ballast.Xr.XrControllerState controllers, int variant, Sim.Player.Tool holding) =>
        Creatures.HeadsetHands(mesh, yaw, controllers.Left, controllers.Right, variant, ToolProp(holding));

    MeshAsset? ToolProp(Sim.Player.Tool tool) => tool switch
    {
        Sim.Player.Tool.Crowbar => PropArt.Of(Look).Get("tool_crowbar"),
        Sim.Player.Tool.Shovel => PropArt.Of(Look).Get("tool_shovel"),
        Sim.Player.Tool.Wrench => PropArt.Of(Look).Get("tool_wrench"),
        _ => null,
    };

    static Vector3 ToF(Double3 d) => new((float)d.X, (float)d.Y, (float)d.Z);

    /// <summary>Smoke, steam, sparks, the lamp's beam, drifting fog.</summary>
    public Effects Effects { get; } = new(look);

    /// <summary>A cooked piece by name, made by <paramref name="make"/> the first time.</summary>
    public MeshAsset Piece(string key, Func<MeshAsset> make)
    {
        if (!_pieces.TryGetValue(key, out var piece))
            _pieces[key] = piece = make();
        return piece;
    }

    /// <summary>Every piece cooked so far (for budgets: `dt art check`).</summary>
    public IReadOnlyDictionary<string, MeshAsset> Pieces => _pieces;

    /// <summary>Pivot to handle of the brake valve's handle and of the reverser (tools/models cab_levers): each swings
    /// about its pivot, below its handle, so the handle travels where the sim's does (CabLevers), near enough.</summary>
    const float BrakeLever = 0.25f, ReverserLever = 0.9f;

    /// <summary>
    /// The driver's controls, modelled (tools/models cab_levers), where the sim has them (T29): the regulator's handle
    /// slid back along its rack as it opens, the brake valve's handle and the reverser swung about their pivots, and
    /// the blow-off valve on the cab wall at the vent. False where the models aren't built (the greybox draws them).
    /// </summary>
    /// <param name="cordPulled">A crewmate's on the whistle cord (CrewActs.CrewWhistling): it's hauled down. The Whistler's
    /// blast leaves it hanging (App. A.4).</param>
    /// <param name="shovelRacked">The fireman's shovel is home (the boiler's ShovelOut, the other way about; note 275): stood
    /// against the cab wall beside the tool rack, blade down.</param>
    /// <summary>The shovel stood by its rack, from the rack's interactable (the cab wall at +X): blade down, leaning in.</summary>
    static readonly Matrix4x4 ShovelStood = Matrix4x4.CreateRotationX(-MathF.PI / 2) * Matrix4x4.CreateRotationZ(-0.2f)
        * Matrix4x4.CreateTranslation(0.02f, 0.76f, 0.6f);

    public bool CabControls(MeshBuilder mesh, in CarFrame frame, Double3 eye, TrainControls controls, bool wrenchRacked = true, bool cordPulled = false,
        bool shovelRacked = true)
    {
        var props = PropArt.Of(Look);
        if (frame.Shape.Levers is not { } levers || props.Get("lever_regulator") is not { } regulator)
            return false;
        var m = FrameMatrix(frame, eye);
        mesh.Append(regulator, Matrix4x4.CreateTranslation(ToF(levers.RegulatorAt(controls.Throttle))) * m);
        void Swung(string lever, string stand, Double3 rest, Double3 now, float length)
        {
            var pivot = ToF(rest) - new Vector3(0, length, 0);
            float angle = MathF.Asin(Math.Clamp((float)(now.Z - rest.Z) / length, -1, 1));
            if (props.Get(stand) is { } s)
                mesh.Append(s, Matrix4x4.CreateTranslation(pivot) * m);
            if (props.Get(lever) is { } l)
                mesh.Append(l, Matrix4x4.CreateRotationX(angle) * Matrix4x4.CreateTranslation(pivot) * m);
        }
        Swung("lever_brake", "brake_stand", levers.Brake, levers.BrakeAt(controls.Brake), BrakeLever);
        Swung("lever_reverser", "reverser_quadrant", levers.Reverser, levers.ReverserAt(controls.Reverser), ReverserLever);
        // T101: the brake reads at a glance, a red-painted handle on its lever. (These are for whoever's on the engine:
        // farther off they're a few pixels, and the headset's frame budget has no room for them.)
        bool near = (frame.Origin - eye).Length < 30;
        if (near)
            mesh.Append(Piece("brake-grip", () => TrainKit.Grip(Look, Palette.SignalRed)), Matrix4x4.CreateTranslation(ToF(levers.BrakeAt(controls.Brake))) * m);
        // The whistle cord down from the cab roof over the driver (GDD §12), hauled down while a crewmate blows it.
        if (near && frame.Shape.Cab is { } roof)
        {
            var handle = TrainKit.WhistleCordHandle(frame.Shape, cordPulled);
            float length = (float)(roof.Max.Y - handle.Y);
            mesh.Append(Piece($"whistle-cord-{length:0.00}", () => TrainKit.WhistleCord(Look, length)), Matrix4x4.CreateTranslation(ToF(handle)) * m);
        }
        // The tool rack (T109), and the wrench on it while it's there.
        foreach (var i in frame.Shape.Interactables.Where(i => i.Kind == InteractableKind.ToolRack && near))
        {
            var at = Matrix4x4.CreateTranslation(ToF(i.Position)) * m;
            mesh.Append(Piece("tool-rack", () => TrainKit.ToolRack(Look)), at);
            if (wrenchRacked)
                mesh.Append(Piece("wrench", () => TrainKit.Wrench(Look)), at);
            // The shovel (note 275): its blade (the tool's −Z) down on the floor, the D-grip up, leant back to the wall.
            if (shovelRacked && props.Get("tool_shovel") is { } shovel)
                mesh.Append(shovel, ShovelStood * at);
        }
        foreach (var i in frame.Shape.Interactables.Where(i => i.Kind == InteractableKind.Vent))
        {
            // The blow-off on its standpipe up from the running board, a red wheel on it, and a marker lamp over it so
            // it's found in the dark from the cab's window (T101): the lamp from anywhere.
            var at = ToF(i.Position);
            // T109: in the cab, where the firebox lights it, it needs no lamp.
            bool inCab = frame.Shape.Cab is { } cabBox && cabBox.Contains(i.Position + new Double3(0, 0.2, 0));
            if (near)
                mesh.Append(inCab ? Piece("vent-stand-cab", () => TrainKit.VentStand(Look, 1.1f, lamp: false)) : Piece("vent-stand", () => TrainKit.VentStand(Look, 1.1f)),
                    Matrix4x4.CreateTranslation(at) * m);
            if (props.Get("vent_valve") is { } vent)
                mesh.Append(vent, Matrix4x4.CreateTranslation(at + new Vector3(0, 1.1f, 0)) * m);
            if (inCab)
                continue;
            var lamp = Vector3.Transform(at + new Vector3(0, 1.55f, 0), m);
            mesh.PointLights.Add(new PointLight(lamp, new Vector3(1.0f, 0.35f, 0.15f) * 0.8f, 3.5f));
            mesh.Billboard(lamp, 0.22f, 0, new Vector4(1.0f, 0.35f, 0.15f, 1), -1, FxBlend.Additive);
        }
        return true;
    }

    /// <summary>A car frame's transform to camera-relative space: its axes as rows, its origin relative to the eye.</summary>
    public static Matrix4x4 FrameMatrix(in CarFrame frame, Double3 eye)
    {
        var o = frame.Origin.RelativeTo(eye);
        return new Matrix4x4(
            (float)frame.Right.X, (float)frame.Right.Y, (float)frame.Right.Z, 0,
            (float)frame.Up.X, (float)frame.Up.Y, (float)frame.Up.Z, 0,
            (float)frame.Back.X, (float)frame.Back.Y, (float)frame.Back.Z, 0,
            o.X, o.Y, o.Z, 1);
    }

    static string ShapeKey(CarShape s) =>
        $"{s.HalfWidth:0.###}x{s.HalfLength:0.###}x{s.RoofHeight:0.###}:{s.Solids.Count}:{s.Solids.Any(x => x.Part == PartKind.Coupler)}";

    /// <summary>
    /// A loose body that isn't a ragdoll (crates, freight, a lamp, a radio) as its prop, turned by its yaw in its parent's
    /// frame. A lamp lights its surroundings and glows. Returns false for what the kit doesn't draw (the dead).
    /// </summary>
    /// <summary>
    /// Facility freight comes in a few kinds of case (machine parts, ammunition, sacks of grain, medical stores): the one
    /// its cargo comes in (GDD §19 "physically aboard and readable", the facility's, Body.Cargo); a stop's loot crates,
    /// and the cargo with no case of its own yet, keep one by their id.
    /// </summary>
    static readonly string[] FreightKinds = ["freight_parts", "freight_ammo", "freight_sacks", "freight_medical"];

    static string Freight(Sim.Physics.Body b) => b.Cargo switch
    {
        CargoKind.Food or CargoKind.Grain => "freight_sacks",
        CargoKind.Ammunition => "freight_ammo",
        CargoKind.Heavy or CargoKind.Salvage or CargoKind.Ore => "freight_parts",
        _ => FreightKinds[(int)((uint)b.Id * 2654435761u % (uint)FreightKinds.Length)],
    };

    /// <summary>The quiet toys (App. C.4), one each by its id: the rag bear, the pull-along horse, the porcelain doll.</summary>
    static readonly string[] Toys = ["toy_bear", "toy_horse", "toy_doll"];

    /// <summary>
    /// The model a toy's drawn as: a noisy one looks like what it sounds like (App. C.4, C.7; Body.Noise: the squeeze pig,
    /// the music box, the wind-up drummer); a quiet one's picked by its id.
    /// </summary>
    public static string ToyModel(int bodyId, Sim.Physics.ToyNoise noise) => noise switch
    {
        Sim.Physics.ToyNoise.Squeaker => "toy_squeaker",
        Sim.Physics.ToyNoise.MusicBox => "toy_musicbox",
        Sim.Physics.ToyNoise.Drummer => "toy_drummer",
        _ => Toys[(int)((uint)bodyId * 2654435761u % (uint)Toys.Length)],
    };

    /// <summary>The toy a toy body is drawn as (as <see cref="Body"/> draws it), or null.</summary>
    public MeshAsset? Toy(int bodyId, Sim.Physics.ToyNoise noise = default) => PropArt.Of(Look).Get(ToyModel(bodyId, noise));

    /// <summary>How far an extinguisher's model stands up off its body's middle: its foot on the floor, its 0.15 m body.</summary>
    const float ExtinguisherLift = 0.15f;

    public bool Body(MeshBuilder mesh, IReadOnlyList<CarFrame> frames, Sim.Physics.Body b, Double3 eye, double heavyHalf, double time)
    {
        bool onCar = b.Parent != Sim.Player.PlayerState.World && b.Parent < frames.Count;
        if (!onCar && b.Parent != Sim.Player.PlayerState.World)
            return true;
        if (b.Kind == Sim.Physics.BodyKind.Ragdoll)
            return Corpse(mesh, frames, b, eye, onCar, time);
        // A hand lamp someone's carrying is drawn in their fist (Crewmate), not where the sim holds it.
        if (b.Kind == Sim.Physics.BodyKind.Lamp && b.Carrier >= 0 && LampInHand(b.Carrier, time) is not null)
            return true;
        var local = b.Pbd.Particles[0].Position;
        var at = onCar ? frames[b.Parent].ToWorld(local) : local;
        if ((at - eye).Length > 250)
            return true;
        // (A load in the wreck film tumbles: its own up, note 370.)
        var up = b.Tilt.Length > 0.5 ? b.Tilt : onCar ? frames[b.Parent].Up : Double3.Up;
        double yaw = b.Yaw + (onCar ? frames[b.Parent].Heading : 0);
        var u = new Vector3((float)up.X, (float)up.Y, (float)up.Z);
        var right = Vector3.Normalize(Vector3.Cross(new Vector3((float)Math.Sin(yaw), 0, (float)Math.Cos(yaw)), u));
        var back = Vector3.Cross(right, u);
        var o = at.RelativeTo(eye);
        var m = new Matrix4x4(right.X, right.Y, right.Z, 0, u.X, u.Y, u.Z, 0, back.X, back.Y, back.Z, 0, o.X, o.Y, o.Z, 1);
        var props = PropArt.Of(Look);
        if (b.Kind == Sim.Physics.BodyKind.Extinguisher && props.Get("extinguisher") is { } extinguisher)
        {
            // Stood up on its foot (carried, where the hands have it: crew_clips' extinguish); back on its mount (lying where the sim stands it, in its car), stood in the cradle
            // with its glass to the room, the way it hangs there (World.ExtinguisherMount, on the floor: its middle 0.3 m up).
            var stood = b.Carrier >= 0 ? m : Matrix4x4.CreateTranslation(0, ExtinguisherLift, 0) * m;
            if (onCar && b.Carrier < 0 && frames[b.Parent].Shape.Interior is { } room)
            {
                var mount = Sim.World.ExtinguisherMount(frames[b.Parent].Shape, room);
                if (Math.Abs(local.X - mount.X) < 0.35 && Math.Abs(local.Z - mount.Z) < 0.35)
                    stood = Matrix4x4.CreateRotationY(MathF.PI / 2) * Matrix4x4.CreateTranslation(ToF(mount) + new Vector3(0, ExtinguisherLift * 2, 0))
                        * FrameMatrix(frames[b.Parent], eye);
            }
            mesh.Append(extinguisher, stood);
            Charge(mesh, props, b.Charge, stood);
            return true;
        }
        // A rescued child (GDD §19, App. A.6: the real half of the Soot Children's roll, the most valuable cargo there is):
        // the same child as the lure, the model's variant 0, its own eyes and its hands only dirty, huddled, its arms round
        // its knees; carried, in the arms (App. C.4): clinging to whoever has it, its legs round their waist and its face on
        // their shoulder (the clutch: its origin at their feet, out in front; a carried body's frame already faces back at
        // whoever carries it; Bodies.ChildAt holds its middle there). (The body's a ball 0.35 m round its middle.)
        if (b.Kind == Sim.Physics.BodyKind.Child && (b.Carrier >= 0
                ? Creatures.Draw(mesh, "soot_child", "clutch", time, true,
                    Matrix4x4.CreateTranslation(0, -(float)Sim.Physics.Bodies.ChildHeight, 0) * m, 0, seed: 2)
                : Creatures.Draw(mesh, "soot_child", "huddle", time, true, Matrix4x4.CreateTranslation(0, -0.35f, 0) * m, 0, seed: 2)))
            return true;
        // What the crew carry: the modelled props (tools/models make: stores_crate, freight_*, heavy_crate,
        // field_radio, train_stores' toys and repair kit) where they're built, centred on the body like the kit's; the
        // kit's pieces where not.
        var piece = b.Kind switch
        {
            Sim.Physics.BodyKind.Cargo => props.Get(Freight(b)) ?? Piece("prop-cargo", () => PropKit.Cargo(Look)),
            Sim.Physics.BodyKind.Toy => props.Get(ToyModel(b.Id, b.Noise)) ?? PropArt.Of(Look).Get("hand_lantern")
                ?? Piece("prop-lantern", () => PropKit.Lantern(Look)),
            Sim.Physics.BodyKind.Heavy => props.Get("heavy_crate") ?? Piece($"prop-heavy-{heavyHalf:0.00}", () => PropKit.Heavy(Look, (float)heavyHalf)),
            Sim.Physics.BodyKind.Crate => props.Get("stores_crate") ?? Piece("prop-crate", () => PropKit.Crate(Look)),
            Sim.Physics.BodyKind.Radio => props.Get("field_radio") ?? Piece("prop-radio", () => PropKit.Radio(Look)),
            // The engineer's toolbox (train_stores' repair_kit, GDD §12), lying where it was put down or dropped.
            Sim.Physics.BodyKind.RepairKit => props.Get("repair_kit") ?? Piece("prop-crate", () => PropKit.Crate(Look)),
            // A charge for a gun's rack (note 374): a powder bag from the shot locker.
            Sim.Physics.BodyKind.Powder => props.Get("powder_bag") ?? Piece("prop-crate", () => PropKit.Crate(Look)),
            // A village find as what it is (FindKit; the director, 8 Oct: finds that stand out by their texture).
            Sim.Physics.BodyKind.Loot when FindItem?.Invoke(b) is { } item => Piece($"find-{item}", () => FindKit.Find(Look, item, 0.15f)),
            Sim.Physics.BodyKind.Loot => Piece("prop-loot", () => PropKit.Loot(Look, 0.15f)),
            // The hand lamp: the sourced lantern (tools/models hand_lantern) where it's built.
            _ => PropArt.Of(Look).Get("hand_lantern") ?? Piece("prop-lantern", () => PropKit.Lantern(Look)),
        };
        mesh.Append(piece, m);
        if (b.Kind == Sim.Physics.BodyKind.Lamp)
        {
            // A hand lamp's glow round it, flickering a little (pipeline: "dynamic point lights with flicker curves").
            float flicker = Flicker(time, b.Id);
            mesh.Billboard(o, 0.7f * flicker, 0, new Vector4(Palette.LampAmber * 0.55f * flicker, 1), -1, FxBlend.Additive);
        }
        return true;
    }

    /// <summary>
    /// An extinguisher's charge (App. C.5 "limited"; Body.Charge, to the percent) in its sight glass, readable at a glance:
    /// the water standing that high between the glass's glands, a red float on it, nothing in it when it's spent.
    /// </summary>
    void Charge(MeshBuilder mesh, PropArt props, double charge, Matrix4x4 at)
    {
        if (props.Socket("extinguisher", "glass_lo") is not { } lo || props.Socket("extinguisher", "glass_hi") is not { } hi || charge <= 0.005)
            return;
        float h = (hi.Y - lo.Y) * (float)Math.Clamp(charge, 0, 1);
        var column = Piece("charge-column", () => TrainKit.SightWater(Look));
        mesh.Append(column, Matrix4x4.CreateScale(1, h, 1) * Matrix4x4.CreateTranslation(lo) * at);
        var bob = Piece("charge-float", () => TrainKit.SightFloat(Look));
        mesh.Append(bob, Matrix4x4.CreateTranslation(lo + new Vector3(0, h, 0)) * at);
    }

    int? _fullStock;

    /// <summary>
    /// A car's fittings that aren't its body (GDD §12 "where powder and shot are stored, where tools and fire extinguishers
    /// hang"): the extinguisher's board and cradle in every car with a room (World.ExtinguisherMount), and in a gun car the
    /// powder and shot locker, holding what's left of the gun's stock (Gun.Ammo of combat.json's guns "ammo": a ball's well and a
    /// bag's place emptied as it goes). One whose floor a Car Hugger's eaten (<paramref name="bite"/>) has gone with it.
    /// </summary>
    public void Fittings(MeshBuilder mesh, in CarFrame frame, Double3 eye, Vehicle? vehicle, Bite bite = default)
    {
        if (frame.Shape.Interior is not { } room || (frame.Origin - eye).Length > 60)
            return;
        var props = PropArt.Of(Look);
        var m = FrameMatrix(frame, eye);
        var spot = ToF(Sim.World.ExtinguisherMount(frame.Shape, room));
        if (props.Get("extinguisher_mount") is { } board && !bite.Eats(spot + new Vector3(0, 0.6f, 0)))
            mesh.Instances.Add(new MeshInstance(board, Matrix4x4.CreateTranslation(spot) * m));
        if (!(vehicle?.HasGun ?? frame.Shape.Gun is not null) || props.Get("shot_locker") is not { } locker)
            return;
        // Along the left wall in the front corner, ahead of the guard van's tool lockers and clear of the end door and the
        // stores, out from the wall as far as its lid leans back past its hinges (0.4 m), so the lid rests against it.
        var at = Matrix4x4.CreateTranslation((float)room.Min.X + 0.42f, (float)room.Min.Y + 0.1f, (float)room.Min.Z + 0.55f);
        if (bite.Eats(at.Translation + new Vector3(0, 0.3f, 0)))
            return;
        mesh.Instances.Add(new MeshInstance(locker, at * m));
        _fullStock ??= Math.Max(1, DataFile.Load<Sim.Combat.CombatTuning>(Path.Combine(props.ContentRoot, Sim.Combat.CombatTuning.File)).Guns.Ammo);
        double left = vehicle is null ? 1 : Math.Clamp((double)vehicle.Gun.Ammo / _fullStock.Value, 0, 1);
        int balls = (int)Math.Ceiling(left * 12), bags = (int)Math.Ceiling(left * 6);
        if (props.Get("cannon_ball") is { } ball)
            for (int k = 0; k < balls; k++)
                if (props.Socket("shot_locker", $"ball_{k}") is { } c)
                    mesh.Append(ball, Matrix4x4.CreateTranslation(c) * at * m);
        if (props.Get("powder_bag") is { } bag)
            for (int k = 0; k < bags; k++)
                if (props.Socket("shot_locker", $"bag_{k}") is { } c)
                    mesh.Append(bag, Matrix4x4.CreateRotationY(k * 1.3f) * Matrix4x4.CreateTranslation(c) * at * m);
    }

    /// <summary>
    /// A lineside board (sight.json; GDD §22, the line's own warnings) as the plan's boards are drawn (SignKit): a posted
    /// speed in its figures, the tunnel's yellow LOW board, the mail crane's MAIL and an arrow to its side, the terminus's
    /// END. Paint that sends the lamp back when <paramref name="lit"/> (it's within the lamp's reading range, exactly when the
    /// sim reads it). <paramref name="foot"/> is the post's foot (camera-relative), <paramref name="right"/> across the line
    /// and <paramref name="toward"/> the way it faces (back at the oncoming train).
    /// </summary>
    public bool LinesideBoard(MeshBuilder mesh, Sim.Route.Sign sign, Vector3 foot, Vector3 right, Vector3 toward, bool lit)
    {
        var (type, text) = sign.Kind switch
        {
            Sim.Route.SignKind.SpeedLimit => ("speedBoard", sign.LimitKmh.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            Sim.Route.SignKind.LowClearance => ("restricted", "LOW"),
            Sim.Route.SignKind.Drop => ("mailDrop", sign.Drop is { Side: < 0 } ? "< MAIL" : "MAIL >"),
            Sim.Route.SignKind.Terminus => ("lineClosed", "END"),
            _ => ("restricted", "?"),
        };
        var board = Piece($"board-{type}-{text}", () => SignKit.Board(Look, type, text, 1.0f, 3.9f));
        var up = Vector3.UnitY;
        mesh.Instances.Add(new MeshInstance(board, new Matrix4x4(right.X, right.Y, right.Z, 0, up.X, up.Y, up.Z, 0, toward.X, toward.Y, toward.Z, 0,
            foot.X, foot.Y, foot.Z, 1), lit ? 7 : 1));
        return true;
    }

    /// <summary>
    /// A mail crane (tools/models mail_crane) at <paramref name="foot"/>, its arms reaching <paramref name="inward"/> (toward
    /// the line), and the bag hung in their clamps, tinted by what's in it: mail canvas, coal black, rounds olive, spares
    /// brass. <paramref name="caught"/>: seconds since the hook took it. The bag's snatched in through the door going by
    /// (carried along at the train's <paramref name="train"/> velocity, in over the cess, gone inside), and the arms, let go,
    /// fall to hang by the post with a bounce on their hinges, as a real crane's do: a crane that's been worked reads empty
    /// from up the line. <paramref name="hung"/> false: no word of a catch, but the bag's to be gone. False without the models.
    /// </summary>
    public bool MailCrane(MeshBuilder mesh, Vector3 foot, Vector3 inward, Sim.Route.DropKind kind, double? caught = null, Vector3 train = default,
        bool hung = true)
    {
        var props = PropArt.Of(Look);
        if (props.Get("mail_crane") is not { } crane || props.Get("mail_bag") is not { } bag)
            return false;
        var up = Vector3.UnitY;
        var side = Vector3.Cross(inward, up);
        var m = new Matrix4x4(inward.X, inward.Y, inward.Z, 0, up.X, up.Y, up.Z, 0, side.X, side.Y, side.Z, 0, foot.X, foot.Y, foot.Z, 1);
        mesh.Instances.Add(new MeshInstance(crane, m));
        var top = props.Socket("mail_crane", "bag_top") ?? new Vector3(0.85f, 2.65f, 0);
        var low = props.Socket("mail_crane", "bag_foot") ?? new Vector3(0.85f, 2.05f, 0);
        // The arms (mail_crane_arm, hinged at the collars' faces): level while they hold the bag, then dropping.
        if (props.Get("mail_crane_arm") is { } arm)
        {
            float fall = caught is { } c ? ArmFall((float)c) : 0;
            foreach (var y in (ReadOnlySpan<float>)[top.Y + 0.08f, low.Y - 0.08f])
                mesh.Instances.Add(new MeshInstance(arm, Matrix4x4.CreateRotationZ(-fall) * Matrix4x4.CreateTranslation(MailArmPivot, y, 0) * m));
        }
        var tint = kind switch
        {
            Sim.Route.DropKind.Coal => new Vector3(0.32f, 0.3f, 0.29f),
            Sim.Route.DropKind.Ammo => new Vector3(0.62f, 0.68f, 0.45f),
            Sim.Route.DropKind.Spares => new Vector3(0.95f, 0.75f, 0.45f),
            _ => new Vector3(0.9f, 0.9f, 0.88f),
        };
        var held = Matrix4x4.CreateTranslation((top + low) / 2) * m;
        if (caught is not { } since)
        {
            if (hung)
                mesh.Instances.Add(new MeshInstance(bag, held, Tint: tint));
        }
        else if (since < MailSnatch)
        {
            // Off the clamps with the hook: away with the car, swung in over the cess and kicked up as it's jerked off,
            // tumbling end over end into the doorway.
            float s = (float)since, k = MathF.Min(1, s / (float)MailSnatch);
            var off = train * s + inward * (1.15f * (1 - (1 - k) * (1 - k))) + up * (0.35f * MathF.Sin(k * MathF.PI) - 0.25f * k);
            var spin = Matrix4x4.CreateRotationZ(2.4f * k);
            mesh.Instances.Add(new MeshInstance(bag, spin * held * Matrix4x4.CreateTranslation(off), Tint: tint));
        }
        return true;
    }

    /// <summary>The arms' hinge, out from the post's axis (tools/models mail_crane's PIVOT); how long the bag's in the air.</summary>
    const float MailArmPivot = 0.12f;
    const double MailSnatch = 0.45;

    /// <summary>
    /// How far a crane's arm has fallen (radians below level) <paramref name="t"/> s after the bag's gone: the drop under
    /// gravity about its hinge (a quarter turn in ~0.3 s), then a bounce or two against the post, settled by a second.
    /// </summary>
    static float ArmFall(float t)
    {
        const float Down = MathF.PI / 2, Drop = 0.3f;
        if (t < Drop)
            return Down * (t / Drop) * (t / Drop);
        float after = t - Drop;
        return Down - 0.32f * MathF.Exp(-after * 6) * MathF.Abs(MathF.Sin(after * 14));
    }

    readonly Vector3[] _joints = new Vector3[CreatureArt.RagdollJoints];

    /// <summary>Whose bodies died by fire (the Stoker, a burning car, powder going up): drawn charred, still smouldering.</summary>
    public IReadOnlySet<int>? Burned { get; set; }

    /// <summary>A ragdoll as the crew model lying as its joints lie; false (the greybox's bones) if the model isn't there.</summary>
    bool Corpse(MeshBuilder mesh, IReadOnlyList<CarFrame> frames, Sim.Physics.Body b, Double3 eye, bool onCar, double time)
    {
        var ps = b.Pbd.Particles;
        if (ps.Length < _joints.Length)
            return false;
        var near = onCar ? frames[b.Parent].ToWorld(ps[2].Position) : ps[2].Position;
        if ((near - eye).Length > 250)
            return true;
        for (int i = 0; i < _joints.Length; i++)
            _joints[i] = (onCar ? frames[b.Parent].ToWorld(ps[i].Position) : ps[i].Position).RelativeTo(eye);
        bool charred = Burned?.Contains(b.Owner) == true;
        if (charred)
        {
            // Embers still in the coat at the chest and the hips, and a thin smoke off them (the near ones only).
            float glow = 0.6f + 0.4f * MathF.Sin(b.Owner * 1.7f + (float)time * 3.1f);
            foreach (int j in new[] { 1, 2, 7, 9 })
                mesh.Billboard(_joints[j] + new Vector3(0, 0.12f, 0), 0.4f * glow, 0, new Vector4(Palette.LampAmber * new Vector3(3.2f, 1.3f, 0.5f) * glow, 1), -1, FxBlend.Additive);
            for (int k = 0; k < 3; k++)
            {
                float rise = (float)((time * 0.5 + k / 3.0) % 1.0);
                mesh.Billboard(_joints[1] + new Vector3(0.1f * MathF.Sin(k * 2.1f + rise * 3), 0.2f + rise * 1.6f, 0), 0.3f + rise * 0.6f, rise,
                    new Vector4(new Vector3(0.12f), 0.45f * (1 - rise)), -1, FxBlend.Alpha);
            }
        }
        return Creatures.Corpse(mesh, _joints, b.Owner, charred);
    }

    /// <summary>
    /// The backhead's dials as the engine stands (pipeline "diegetic readouts: needle and lever meshes"): each needle
    /// turned through its dial's 270° sweep by its fraction (pressure, heat, water, speed; 0..1).
    /// </summary>
    public void Gauges(MeshBuilder mesh, in CarFrame engine, Double3 eye, ReadOnlySpan<float> fractions)
    {
        if (engine.Shape.Cab is null || (engine.Origin - eye).Length > 30)
            return;
        var m = FrameMatrix(engine, eye);
        // The gauge lamp under the cab roof (T101): the dials lit enough to read whatever the fire's doing; at the cab's
        // front over the work (note 280).
        var cab = engine.Shape.Cab!.Value;
        mesh.PointLights.Add(new PointLight(Vector3.Transform(new Vector3(0.3f, (float)cab.Max.Y - 0.3f, (float)(cab.Min.Z + 1.3)), m), new Vector3(1.0f, 0.78f, 0.5f) * 0.55f, 3.2f));
        var needle = Piece("needle", () => TrainKit.Needle(Look));
        // The one set (note 280: the firebox is at the front, so whoever's firing reads the same dials the driver does), over
        // the right-hand front window, facing back into the cab as the engine's frame does.
        for (int i = 0; i < 4 && i < fractions.Length; i++)
            Dial(mesh, needle, TrainKit.DriverGauge(engine.Shape, i), TrainKit.DriverGaugeRadius, m, i, fractions[i]);
    }

    /// <summary>One dial's reading: its needle, or (the tender's, <paramref name="index"/> 2) the coal's level in its glass.</summary>
    static void Dial(MeshBuilder mesh, MeshAsset needle, Vector3 g, float gr, Matrix4x4 frame, int index, float fraction)
    {
        if (index == 2)
        {
            // The tender's glass (gauge_face's "water" cell, labelled TENDER): no needle, a level standing in the tube
            // as high as the coal left, lit amber so it reads against the dark glass. The tube's place on the face
            // in the dial's radii (tools/art/texgen/mat_paper.py: the tube's cell pixels over the face's 0.92 of it).
            float f = Math.Clamp(fraction, 0, 1);
            float x0 = -0.42f * gr, x1 = -0.245f * gr, bottom = -0.50f * gr, top = 0.43f * gr;
            float y1 = bottom + (top - bottom) * f;
            if (y1 - bottom > 0.002f)
            {
                var centre = Vector3.Transform(g + new Vector3((x0 + x1) / 2, (bottom + y1) / 2, 0.016f), frame);
                var ax = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitX, frame));
                var ay = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitY, frame));
                var az = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, frame));
                float e = mesh.Emissive;
                mesh.Emissive = 0.35f;
                mesh.Box(centre, ax, ay, az, new Vector3((x1 - x0) / 2, (y1 - bottom) / 2, 0.002f), new Vector3(0.42f, 0.2f, 0.05f));
                mesh.Emissive = e;
            }
            return;
        }
        // From 7:30 round to 4:30, clockwise as you face it: the dial faces its frame's +Z (out into the cab).
        float angle = (0.75f - 1.5f * Math.Clamp(fraction, 0, 1)) * MathF.PI;
        mesh.Append(needle, Matrix4x4.CreateScale(gr / 0.11f) * Matrix4x4.CreateRotationZ(angle) * Matrix4x4.CreateTranslation(g) * frame);
    }

    /// <summary>
    /// A car's two lanterns, hanging on their chains from the carlines where its lights are; red glass under emergency
    /// lighting. One whose ceiling a Car Hugger's eaten (<paramref name="bite"/>) has gone with it.
    /// </summary>
    /// <param name="lit">The lamps are lit: out, the lanterns hang dark (their glass unlit, no glow round them).</param>
    public void CarLamps(MeshBuilder mesh, in CarFrame frame, Double3 eye, bool emergency, Bite bite = default, bool lit = true)
    {
        if (frame.Shape.Interior is not { } room || (frame.Origin - eye).Length > 80)
            return;
        var m = FrameMatrix(frame, eye);
        var lamps = Piece($"lamps:{room.Min.Y:0.00}:{room.Max.Y:0.00}:{room.HalfSize.Z:0.00}:{room.Centre.Z:0.00}", () =>
        {
            var k = new Kit(Look, 71);
            // The sourced hand lantern hung by its ring where it's built (tools/models), the kit's cage where it isn't.
            var props = PropArt.Of(Look);
            var sourced = props.Get("hand_lantern");
            var lantern = sourced ?? PropKit.Lantern(Look);
            var ring = sourced is not null ? props.Socket("hand_lantern", "hang") ?? new Vector3(0, 0.36f, 0) : new Vector3(0, 0.26f, 0);
            var flame = sourced is not null ? props.Socket("hand_lantern", "lamp") ?? new Vector3(0, 0.15f, 0) : Vector3.Zero;
            var chain = Chain(Look);
            foreach (var at in LampPositions(room))
            {
                // The flame where the light is (LampPositions); the ring under the chain.
                var foot = at - flame;
                float top = foot.Y + ring.Y;
                float drop = (float)room.Max.Y - top;
                k.Append(chain, Matrix4x4.CreateScale(0.5f, drop / 0.18f, 0.5f) * Matrix4x4.CreateTranslation(new Vector3(at.X, top + drop / 2, at.Z)));
                k.Append(lantern, Matrix4x4.CreateTranslation(foot));
            }
            return k.Build("car-lamps");
        });
        var (cut, floor) = bite.Any ? (bite.Shader, bite.Floor) : (Vector4.Zero, 0f);
        mesh.Instances.Add(new MeshInstance(lamps, m, lit ? 1 : 0, emergency ? new Vector3(0.6f, 0.08f, 0.05f) : default,
            Scar: new Vector2(0, bite.Seed), Bite: cut, BiteFloor: floor));
        if (!lit)
            return;
        var glow = emergency ? new Vector3(0.35f, 0.04f, 0.03f) : Palette.LampAmber * 0.35f;
        foreach (var at in LampPositions(room))
            if (!bite.Eats(at with { Y = (float)room.Max.Y - 0.05f }))
                mesh.Billboard(Vector3.Transform(at, m), 0.6f, 0, new Vector4(glow, 1), -1, FxBlend.Additive);
    }

    /// <summary>Where a car's lanterns' flames are, in its frame: the Fire Flies gather on the nearer (GreyboxScene).</summary>
    public static IEnumerable<Vector3> LampPositions(Box room)
    {
        foreach (double z in new[] { -room.HalfSize.Z * 0.5, room.HalfSize.Z * 0.5 })
            yield return new Vector3(0, (float)room.Max.Y - 0.42f, (float)(room.Centre.Z + z));
    }

    static MeshAsset Chain(Look? look)
    {
        var k = new Kit(look, 70);
        k.Use("rust_heavy", Palette.SootBlack, 0.6f, 0.4f);
        k.Rod(new Vector3(0, -0.09f, 0), new Vector3(0, 0.09f, 0), 0.01f);
        return k.Build("chain");
    }

    /// <summary>A flame's flicker, 0.85..1.05, different for each light and steady enough not to strobe.</summary>
    public static float Flicker(double time, int id)
    {
        double t = time * 7 + id * 13.7;
        return (float)(0.95 + 0.05 * Math.Sin(t) + 0.03 * Math.Sin(t * 2.7 + 1.3) + 0.02 * Math.Sin(t * 5.3 + 0.4));
    }

    /// <summary>
    /// A car: its body from the kit, its doors where the vehicle has them (shut in the doorway, or slid aside), and its gun
    /// turned the way it faces. Returns false when the kit can't draw this car (so the greybox does).
    /// </summary>
    /// <summary>
    /// The headlamp out (switched off, or smashed by a Climber: World.LampShining false): dark glass over the engine's lit
    /// lens, a crack star across it, so from the line the train's eye reads shut.
    /// </summary>
    public void HeadlampOut(MeshBuilder mesh, in CarFrame engine, Double3 eye)
    {
        if (engine.Shape.Cab is null || (engine.Origin - eye).Length > 300)
            return;
        mesh.Instances.Add(new MeshInstance(Piece("headlamp-out", () =>
        {
            var k = new Kit(Look, 43);
            k.Use("lamp_lens", new Vector3(0.05f, 0.045f, 0.04f), 0.3f, 0.8f, tile: 0.64f);
            k.Tint = new Vector3(0.06f, 0.055f, 0.05f);
            k.Panel(Vector3.Zero, -Vector3.UnitZ, Vector3.UnitY, 0.66f, 0.66f);
            k.Use("iron_plate", new Vector3(0.5f, 0.5f, 0.5f), 0.3f, 0.5f);
            k.Tint = new Vector3(0.35f, 0.35f, 0.36f);
            for (int i = 0; i < 6; i++)
            {
                float a = i * MathF.Tau / 6 + 0.3f * (i % 2);
                var tip = new Vector3(MathF.Cos(a), MathF.Sin(a), 0) * (0.18f + 0.1f * (i % 3));
                k.Rod(new Vector3(0.04f, 0.06f, -0.004f), tip + new Vector3(0.04f, 0.06f, -0.004f), 0.004f);
            }
            return k.Build("headlamp-out");
        }), Matrix4x4.CreateTranslation(0, TrainKit.HeadlampY, (float)-engine.Shape.HalfLength - 0.06f) * FrameMatrix(engine, eye)));
    }

    /// <summary>The boiler's torn flank while she's ruptured (TrainKit.RuptureTear at its seam; Effects.Rupture its steam).</summary>
    public void RuptureTear(MeshBuilder mesh, in CarFrame engine, Double3 eye)
    {
        if (engine.Shape.Cab is null || (engine.Origin - eye).Length > 300)
            return;
        mesh.Instances.Add(new MeshInstance(Piece("rupture-tear", () => TrainKit.RuptureTear(Look)),
            Matrix4x4.CreateScale(TrainKit.RuptureSize(engine.Shape)) * Matrix4x4.CreateTranslation(TrainKit.RuptureSeam(engine.Shape))
            * FrameMatrix(engine, eye)));
    }

    /// <summary>
    /// The firebox door shut (the boiler's FireDoorOpen false; the Stoker's "keep it hot, keep it shut"): two iron leaves
    /// over the firehole, strapped and handled, meeting in the middle, the fire's light only at the seam between them and
    /// through the peephole. Open, the backhead's own leaves stand ajar (tools/models cab_backhead) and the fire shows.
    /// </summary>
    public void FireDoorShut(MeshBuilder mesh, in CarFrame engine, Double3 eye, float fire, Vector3 fireColour)
    {
        if (engine.Shape.Cab is null || (engine.Origin - eye).Length > 40)
            return;
        // In the backhead's frame: the door's leaves face out of the back wall into the cab (note 276).
        var m = TrainKit.BackheadFrame(engine.Shape) * FrameMatrix(engine, eye);
        var at = TrainKit.FireDoorLocal(engine.Shape);
        mesh.Instances.Add(new MeshInstance(Piece("firedoor-shut", () =>
        {
            var k = new Kit(Look, 61);
            float w = TrainKit.FireDoorHalfWidth + 0.03f, h = TrainKit.FireDoorHalfHeight + 0.03f;
            foreach (int side in new[] { -1, 1 })
            {
                float x0 = side < 0 ? -w : 0.006f, x1 = side < 0 ? -0.006f : w;
                k.Use("iron_smokebox", Palette.SootBlack, 0.8f, 0.35f, tile: 0.6f);
                k.Box(new Vector3(x0, -h, 0.01f), new Vector3(x1, h, 0.045f));
                // Two straps across each leaf and its hinge knuckles at the outer edge.
                k.Use("rust_heavy", Palette.IronGrey, 0.7f, 0.4f);
                foreach (float y in new[] { -h * 0.55f, h * 0.55f })
                    k.Box(new Vector3(x0 + 0.01f, y - 0.022f, 0.045f), new Vector3(x1 - 0.01f, y + 0.022f, 0.055f));
                float hx = side * (w + 0.01f);
                foreach (float y in new[] { -h * 0.55f, h * 0.55f })
                    k.Cylinder(new Vector3(hx, y - 0.05f, 0.03f), new Vector3(hx, y + 0.05f, 0.03f), 0.02f, 6);
                // The handle, a loop of rod near the meeting edge.
                k.Use("brass", Palette.TarnishedBrass, 0.6f, 0.6f);
                float gx = side * 0.06f;
                k.Rod(new Vector3(gx, -0.05f, 0.055f), new Vector3(gx, -0.05f, 0.1f), 0.01f, 5);
                k.Rod(new Vector3(gx, 0.05f, 0.055f), new Vector3(gx, 0.05f, 0.1f), 0.01f, 5);
                k.Rod(new Vector3(gx, -0.05f, 0.1f), new Vector3(gx, 0.05f, 0.1f), 0.012f, 5);
            }
            return k.Build("firedoor-shut");
        }), Matrix4x4.CreateTranslation(at) * m));
        // The fire's light where it gets out: the seam down the middle and the peephole in the right leaf.
        if (fire > 0)
        {
            var ax = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitX, m));
            var ay = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitY, m));
            var az = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, m));
            float e = mesh.Emissive;
            mesh.Emissive = 1;
            var glow = fireColour * (0.25f + 0.75f * fire);
            mesh.Box(Vector3.Transform(at + new Vector3(0, 0, 0.03f), m), ax, ay, az, new Vector3(0.004f, TrainKit.FireDoorHalfHeight + 0.02f, 0.016f), glow);
            mesh.Box(Vector3.Transform(at + new Vector3(0.14f, 0.08f, 0.047f), m), ax, ay, az, new Vector3(0.018f, 0.012f, 0.002f), glow);
            mesh.Emissive = e;
        }
    }

    /// <summary>The engine's modelled moving parts (tools/models engine_parts), or null when they aren't there to turn.</summary>
    (MeshAsset Wheel, MeshAsset Rod)? GearParts =>
        PropArt.Of(Look).Get("driver_wheel") is { } wheel && PropArt.Of(Look).Get("coupling_rod") is { } rod ? (wheel, rod) : null;

    /// <summary>
    /// The engine's running gear, turning with its going (the checklist's train motion: "wheels turn, rods move"): the four
    /// pairs of drivers rolled <paramref name="distance"/> along the line, the coupling rods carried round on their crank
    /// pins, and the main rods from the third pair's pins back to the crossheads sliding in their guides. Close enough to
    /// see it only; past that the still gear baked in a far engine would do, but the engine's always near.
    /// </summary>
    public void Gear(MeshBuilder mesh, in CarFrame frame, Double3 eye, double distance, float glow = 1)
    {
        if (frame.Shape.Cab is null || GearParts is not { } parts || (frame.Origin - eye).Length > 400)
            return;
        var shape = frame.Shape;
        var m = FrameMatrix(frame, eye);
        float turn = (float)(distance / TrainKit.DriverRadius % (2 * Math.PI));
        var drivers = TrainKit.Drivers(shape);
        // The main rod's length: as the still engine lays it, crosshead to pin at the rest crank.
        var restPin = TrainKit.CrankPin(1, 0);
        float length = Vector2.Distance(new Vector2(TrainKit.CrossheadY, TrainKit.CrossheadRestZ(shape)),
            new Vector2(TrainKit.DriverRadius + restPin.Y, drivers[2] + restPin.Z));
        var mainRod = Piece($"engine-main-rod:{length:0.000}", () => TrainKit.MainRod(Look, length));
        foreach (int side in new[] { -1, 1 })
        {
            foreach (float z in drivers)
                mesh.Instances.Add(new MeshInstance(parts.Wheel, TrainKit.DriverAt(side, new Vector3(side * TrainKit.HalfGauge, TrainKit.DriverRadius, z), turn) * m, glow));
            var pin = TrainKit.CrankPin(side, turn);
            float x = side * (TrainKit.HalfGauge + 0.2f);
            var rodAt = new Vector3(x, TrainKit.DriverRadius + pin.Y, (drivers[0] + drivers[^1]) / 2 + pin.Z);
            mesh.Instances.Add(new MeshInstance(parts.Rod, (side > 0 ? Matrix4x4.Identity : Matrix4x4.CreateRotationY(MathF.PI)) * Kit.At(rodAt) * m, glow));
            // The main rod: big end on the third pair's pin, little end on the crosshead, which slides level in its guides.
            var big = new Vector3(TrainKit.RodX(side), TrainKit.DriverRadius + pin.Y, drivers[2] + pin.Z);
            float dy = TrainKit.CrossheadY - big.Y;
            var little = new Vector3(big.X, TrainKit.CrossheadY, big.Z - MathF.Sqrt(MathF.Max(0, length * length - dy * dy)));
            var along = Vector3.Normalize(little - big);
            var lay = Matrix4x4.CreateFromQuaternion(Rotation(Vector3.UnitZ, along)) * Kit.At(big);
            mesh.Instances.Add(new MeshInstance(mainRod, lay * m, glow));
        }
    }

    static Quaternion Rotation(Vector3 from, Vector3 to)
    {
        float d = Vector3.Dot(from, to);
        if (d > 0.9999f)
            return Quaternion.Identity;
        var axis = Vector3.Cross(from, to);
        if (axis.LengthSquared() < 1e-8f)
            axis = Vector3.UnitY;
        return Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.Acos(Math.Clamp(d, -1, 1)));
    }

    // Each door's last setting and when the scene saw it change (presentation only: the sim's doors are open or shut).
    readonly Dictionary<(int Vehicle, int Door), (bool Open, long Since)> _doors = new();
    const double DoorSeconds = 0.6;

    /// <summary>
    /// How far a door is open, 0..1, eased: it slides over <see cref="DoorSeconds"/> from when the scene first saw it change.
    /// One first seen (or with no clock, <paramref name="tick"/> −1) is where it's set.
    /// </summary>
    float Opening(int vehicle, int door, bool open, long tick)
    {
        if (tick < 0)
            return open ? 1 : 0;
        if (!_doors.TryGetValue((vehicle, door), out var was))
            _doors[(vehicle, door)] = was = (open, long.MinValue / 2);
        else if (was.Open != open)
            _doors[(vehicle, door)] = was = (open, tick);
        double k = Math.Clamp((tick - was.Since) * Sim.SimConstants.TickSeconds / DoorSeconds, 0, 1);
        k = k * k * (3 - 2 * k);
        return (float)(open ? k : 1 - k);
    }

    /// <summary>How far an open roof hatch's lid is swung over on its hinges (T99): a little past upright.</summary>
    const float OpenHatch = MathF.PI * 100 / 180;

    /// <param name="cutEnds">Which of its couplers have been cut (T91; 1 the front, 2 the rear): drawn with the knuckle swung open
    /// and the hose parted.</param>
    /// <param name="charred">How charred it is by a fire (0..1, <see cref="Effects.Burning.Char"/>): the body's paint and boards
    /// blackened toward soot, the scars of the burn the mask draws.</param>
    /// <param name="openLockers">Crew lockers drawn open whatever their doors are doing (the Stranded outro's empty locker).</param>
    /// <param name="utility">A utility car (GDD §10): its crew fit-out in place of a load (<see cref="TrainKit.UtilityFit"/>).</param>
    /// <param name="taggedLockers">Crew lockers with something on their shelves (note 264): a tag hangs off a shut one's handle.</param>
    /// <param name="handrails">The train has roof handrails (spec F.3, note 184): drawn along a car's roof edges.</param>
    public bool Car(MeshBuilder mesh, in CarFrame frame, Double3 eye, Vehicle? vehicle, bool emergency, long tick = -1, int cutEnds = 0,
        float charred = 0, bool utility = false, uint openLockers = 0, bool handrails = false, bool dark = false, uint taggedLockers = 0)
    {
        // Its lamps out: its lit windows (the guard van's) go dark with them, as under emergency lighting.
        float lamps = emergency || dark ? 0.06f : 1;
        var shape = frame.Shape;
        var m = FrameMatrix(frame, eye);
        bool engine = shape.Cab is not null;
        if (!engine && shape.Interior is null)
            return false;
        // An armoured car (note 184) wears the armoured livery under its plate, whatever its place in the train.
        bool armoured = !engine && vehicle is { Armoured: true };
        var livery = armoured ? TrainKit.Livery.Armoured : TrainKit.LiveryOf(frame.Index);
        int variant = frame.Index % 2;
        string key = engine ? $"engine:{ShapeKey(shape)}" : $"car:{ShapeKey(shape)}:{livery}:{variant}:{shape.Gun is not null}";
        // The load's drawn apart from the body (TrainKit.Load), in its cargo's cases, when the scene knows the cargo.
        bool loadApart = !engine && vehicle is not null;
        // The engine's drivers and rods are drawn apart, turning (Gear), where their modelled parts are there to turn.
        bool turning = engine && GearParts is not null;
        var body = Piece(loadApart ? key + ":empty" : turning ? key + ":turning" : key,
            () => engine ? TrainKit.Engine(Look, shape, 0, gear: !turning) : TrainKit.Car(Look, shape, livery, variant, load: !loadApart));
        // Wear and tear off the car's integrity (look.json "damage"): the scar mask over the body and doors, seeded by
        // the car so its scars stay where they are, and past the first state the torn plate the mask can't draw.
        // What a Car Hugger ate of it (App. A.3 FEED) is gone, not battered: the scars and torn plate are the rest of the loss.
        var damage = Look.Tuning.Damage;
        double eaten = engine ? 0 : vehicle?.Eaten ?? 0;
        double integrity = Math.Min(1, (vehicle?.Integrity ?? 1) + eaten);
        int seed = vehicle?.Id ?? frame.Index;
        // A fire's char scars it as hard as the burn went, whatever the integrity says (it's the boards that went).
        var scar = new Vector2(MathF.Max(damage.ScarOf(integrity), charred * 0.85f), Bite.ScarSeed(seed));
        var bite = Bite.For(Look.Tuning.Bite, shape, vehicle, frame.Index);
        var (cut, floor) = bite.Any ? (bite.Shader, bite.Floor) : (Vector4.Zero, 0f);
        var soot = charred > 0 ? Vector3.Lerp(Vector3.One, CharTint, charred) : default;
        // Under emergency lighting the headlamp and tail lamp have no power.
        mesh.Instances.Add(new MeshInstance(body, m, lamps, Tint: soot, Scar: scar, Bite: cut, BiteFloor: floor));
        // The engine's dressing (note 338: its plough, housings, pipes and grilles), over it and casting no shadow.
        if (engine)
        {
            mesh.Instances.Add(new MeshInstance(Piece($"engine-dress:{ShapeKey(shape)}", () => TrainKit.EngineDress(Look, shape)), m, lamps, Tint: soot,
                Scar: scar, Shadowless: true));
            // A lamp in each corridor under the hood (note 338), lit with the train's lamps, close enough to matter.
            if ((frame.Origin - eye).Length < 60 && lamps > 0.5f)
                foreach (var lamp in TrainKit.CorridorLamps(shape))
                    mesh.PointLights.Add(new PointLight(Vector3.Transform(lamp, m), new Vector3(1.0f, 0.62f, 0.32f) * 0.55f, 4.5f));
        }
        // Its couplers, each end's shut or cut (TrainKit.CouplerEnds): the knuckle open on a car that's been let go.
        if (PropArt.Of(Look).Get("coupler_knuckle") is { } shut && (frame.Origin - eye).Length < 160)
        {
            var open = PropArt.Of(Look).Get("coupler_open") ?? shut;
            var (front, rear) = TrainKit.CouplerEnds(shape);
            mesh.Instances.Add(new MeshInstance((cutEnds & 1) != 0 ? open : shut, front * m, emergency ? 0.06f : 1, Scar: scar));
            mesh.Instances.Add(new MeshInstance((cutEnds & 2) != 0 ? open : shut, rear * m, emergency ? 0.06f : 1, Scar: scar));
        }
        // Its number, the vehicle's id (the cars counted back from the engine as they left; a car keeps its number when
        // the ones ahead of it are cut away), worn and eaten with the body.
        // A utility car (GDD §10): fitted out for the crew where a load would go (TrainKit.UtilityFit).
        if (utility && !engine)
            mesh.Instances.Add(new MeshInstance(Piece($"utility:{ShapeKey(shape)}", () => TrainKit.UtilityFit(Look, shape)), m,
                emergency ? 0.06f : 1, Scar: scar, Bite: cut, BiteFloor: floor));
        else if (loadApart)
        {
            var cargo = vehicle!.Cargo;
            mesh.Instances.Add(new MeshInstance(Piece($"load:{ShapeKey(shape)}:{cargo}:{variant}", () => TrainKit.Load(Look, shape, cargo, variant)), m,
                emergency ? 0.06f : 1, Scar: scar, Bite: cut, BiteFloor: floor));
            if (cargo == CargoKind.Livestock && (frame.Origin - eye).Length < 120)
                Livestock(mesh, frame, eye, tick);
            // Comet-derived material (GDD §19 "attracts everything; should look like it"): a sick green light from the cracks
            // in its caskets, breathing slowly, out through the car's gaps and doors, and a glow over each stack.
            if (cargo == CargoKind.Comet && (frame.Origin - eye).Length < 220)
            {
                float breath = 0.75f + 0.25f * MathF.Sin((float)(tick < 0 ? 0 : tick) * 0.05f + frame.Index);
                foreach (var solid in shape.Solids.Where(x => x.Part == PartKind.Cargo))
                {
                    var top = frame.ToWorld(new Double3(solid.Box.Centre.X, solid.Box.Max.Y + 0.1, solid.Box.Centre.Z));
                    var at = top.RelativeTo(eye);
                    mesh.PointLights.Add(new PointLight(at, CometGlow * 1.4f * breath, 7f));
                    mesh.Billboard(at, 1.1f * breath, 0, new Vector4(CometGlow * 0.35f * breath, 1), -1, FxBlend.Additive);
                }
            }
        }
        // The plate over it (TrainKit.ArmourPlate), and the roof handrails (TrainKit.RoofHandrails): note 184.
        if (armoured)
            mesh.Instances.Add(new MeshInstance(Piece($"armour:{ShapeKey(shape)}", () => TrainKit.ArmourPlate(Look, shape)), m,
                emergency ? 0.06f : 1, Tint: soot, Scar: scar, Bite: cut, BiteFloor: floor));
        if (handrails && !engine && (frame.Origin - eye).Length < 200)
            mesh.Instances.Add(new MeshInstance(Piece($"handrails:{ShapeKey(shape)}", () => TrainKit.RoofHandrails(Look, shape)), m,
                emergency ? 0.06f : 1, Scar: scar, Bite: cut, BiteFloor: floor));
        int number = frame.Index;
        if (!engine && Look.Layer("stencil_numerals") >= 0)
            mesh.Instances.Add(new MeshInstance(Piece($"number:{ShapeKey(shape)}:{number}", () => TrainKit.CarNumber(Look, shape, number)), m,
                emergency ? 0.06f : 1, Scar: scar, Bite: cut, BiteFloor: floor));
        int state = damage.StateOf(integrity);
        if (state > 0)
            mesh.Instances.Add(new MeshInstance(Piece($"damage:{ShapeKey(shape)}:{state}:{seed}",
                () => engine ? DamageKit.Engine(Look, shape, state, seed) : DamageKit.Car(Look, shape, state, seed)), m, Scar: scar, Bite: cut, BiteFloor: floor));
        if (bite.Any)
            mesh.Instances.Add(new MeshInstance(Piece($"bite:{ShapeKey(shape)}:{livery}:{seed}:{bite.Centre:0.00}:{bite.Side:0.00}",
                () => bite.Edge(Look, shape, livery, seed)), m, Scar: scar));

        foreach (var door in shape.DoorList)
        {
            bool open = vehicle?.DoorOpen(door.Index) ?? false;
            // Sliding over, not snapping (the checklist's doors: "open and shut, readable"): how far across it's got.
            float slid = Opening(vehicle?.Id ?? frame.Index, door.Index, open, tick);
            var box = door.Box;
            // An end door it's eaten past is gone with its wall.
            if (bite.Eats(new Vector3((float)box.Centre.X, (float)box.Centre.Y, (float)box.Centre.Z)))
                continue;
            bool side = box.Max.Z - box.Min.Z > box.Max.X - box.Min.X;
            if (slid > 0)
            {
                // Out from the wall first, then along it: the step out done in the first fifth of the slide.
                double outward = Math.Min(1, slid * 5), along = slid;
                var move = side
                    ? new Double3((box.Min.X < 0 ? -0.12 : 0.12) * outward, 0, (box.Max.Z - box.Min.Z) * along)
                    : new Double3((box.Max.X - box.Min.X) * along, 0, (box.Min.Z < 0 ? 0.12 : -0.12) * outward);
                box = new Box(box.Min + move, box.Max + move);
            }
            var size = new Vector3((float)(box.Max.X - box.Min.X), (float)(box.Max.Y - box.Min.Y), (float)(box.Max.Z - box.Min.Z));
            var leaf = Piece($"door:{side}:{size.X:0.##}x{size.Y:0.##}x{size.Z:0.##}", () => TrainKit.Door(Look, size, side));
            var c = box.Centre;
            mesh.Instances.Add(new MeshInstance(leaf, Matrix4x4.CreateTranslation((float)c.X, (float)c.Y, (float)c.Z) * m, Scar: scar));
        }
        // The crew lockers (note 173): the row's cabinets in the car's frame, and each door on its hinge, shut or swung out
        // into the aisle, lettered with its grade.
        if (shape.Lockers.Count > 0)
        {
            var lockers = shape.Lockers;
            mesh.Instances.Add(new MeshInstance(Piece($"lockers:{ShapeKey(shape)}:{lockers.Count}", () => LockerKit.Row(Look, lockers, shape.LockerShelves)), m,
                emergency ? 0.06f : 1, Scar: scar, Bite: cut, BiteFloor: floor));
            float w = (float)(lockers[0].Box.Max.Z - lockers[0].Box.Min.Z), h = (float)(lockers[0].Box.Max.Y - lockers[0].Box.Min.Y);
            float px = LockerKit.LetterPixel(lockers, w);
            foreach (var bay in lockers)
            {
                if (bite.Eats(ToF(bay.Box.Centre)))
                    continue;
                bool open = (vehicle?.LockerOpen(bay.Index) ?? false) || (openLockers & (1u << bay.Index)) != 0;
                var leaf = Piece($"locker-door:{bay.Name}:{w:0.###}x{h:0.###}:{px:0.#####}", () => LockerKit.Door(Look, bay.Name, w, h, px));
                mesh.Instances.Add(new MeshInstance(leaf, LockerKit.DoorAt(bay, open) * m, emergency ? 0.06f : 1, Scar: scar));
                // Note 267 ("there needs to be some telegraphing that there's a repair kit inside"): something on its shelves,
                // a stores tag hangs off the shut door's handle; the prompt at the door names what.
                if (!open && (taggedLockers & (1u << bay.Index)) != 0)
                    mesh.Instances.Add(new MeshInstance(Piece($"locker-tag:{w:0.###}x{h:0.###}", () => LockerKit.Tag(Look, w, h)), LockerKit.DoorAt(bay, false) * m, emergency ? 0.06f : 1));
            }
        }
        // A cargo car's roof hatch (T99): two leaves meeting on the centreline, shut in the opening, or open, each swung up
        // on its hinges at its side a little past upright, so from the roof or the crane's cab you can see it's open.
        if (shape.Hatch is { } hatch)
        {
            var size = new Vector3((float)(hatch.Max.X - hatch.Min.X) / 2, (float)(hatch.Max.Y - hatch.Min.Y), (float)(hatch.Max.Z - hatch.Min.Z));
            var lid = Piece($"hatch:{size.X:0.##}x{size.Y:0.##}x{size.Z:0.##}", () => TrainKit.HatchLid(Look, size));
            var c = hatch.Centre;
            bool open = vehicle?.DoorOpen(CarShape.HatchBit) == true;
            float lift = Opening(vehicle?.Id ?? frame.Index, CarShape.HatchBit, open, tick);
            foreach (int side in new[] { -1, 1 })
            {
                // The leaf's hinges are on its +X: the left leaf is the right one turned about.
                var turn = side < 0 ? Matrix4x4.CreateRotationY(MathF.PI) : Matrix4x4.Identity;
                Matrix4x4 at;
                if (lift > 0)
                {
                    var swing = Matrix4x4.CreateRotationZ(-OpenHatch * lift);
                    var hinge = new Vector3(size.X, (float)hatch.Max.Y, 0);
                    at = swing * Matrix4x4.CreateTranslation(hinge - Vector3.Transform((size / 2) with { Z = 0 }, swing)) * turn;
                }
                else
                    at = Matrix4x4.CreateTranslation(size.X / 2, (float)c.Y, 0) * turn;
                mesh.Instances.Add(new MeshInstance(lid, at * Matrix4x4.CreateTranslation((float)c.X, 0, (float)c.Z) * m, Scar: scar));
            }
        }
        // The gun rail along the roof (T93), and the gun wherever it's been pushed along it.
        if (shape.RoofRail is { } rail)
            mesh.Instances.Add(new MeshInstance(Piece($"rail:{ShapeKey(shape)}", () => TrainKit.RoofRail(Look, shape, rail)), m, Scar: scar, Bite: cut, BiteFloor: floor));
        if ((vehicle is null ? shape.Gun : vehicle.HasGun ? Sim.Combat.Guns.Mount(shape, vehicle.Gun) : null) is { } gun)
        {
            float yaw = MathF.Atan2(-(float)gun.Facing.X, -(float)gun.Facing.Z);
            var at = gun.Position;
            var gunM = Matrix4x4.CreateRotationY(yaw) * Matrix4x4.CreateTranslation((float)at.X, (float)at.Y, (float)at.Z) * m;
            // The roof eaten out from under it, it's fallen in: down on the floor, nose up against the wall's torn edge.
            if (bite.Eats(new Vector3((float)at.X, (float)at.Y - 0.3f, (float)at.Z)) && shape.Interior is { } room)
                gunM = Matrix4x4.CreateRotationX(0.45f) * Matrix4x4.CreateRotationZ(0.2f) * Matrix4x4.CreateRotationY(yaw)
                    * Matrix4x4.CreateTranslation((float)at.X, (float)room.Min.Y + 0.45f, (float)at.Z) * m;
            // The cannon (note 137) in its pieces: the mount on the roof, the carriage turned to its aim, the barrel
            // elevated on it, a powder chamber in the breech while it's loaded. The aim is the seated gunner's (T112).
            var barrelM = gunM;
            if (TrainKit.Cannon(Look) is { } cannon)
            {
                float traverse = (float)(vehicle?.Gun.Traverse ?? 0), elevation = (float)(vehicle?.Gun.Elevation ?? 0);
                var carriageM = Matrix4x4.CreateRotationY(traverse) * gunM;
                barrelM = Matrix4x4.CreateRotationX(elevation) * carriageM;
                mesh.Instances.Add(new MeshInstance(cannon.Mount, gunM));
                mesh.Instances.Add(new MeshInstance(cannon.Carriage, carriageM));
                // Its shield, turning with it (note 338).
                mesh.Instances.Add(new MeshInstance(Piece("gun-shield", () => TrainKit.GunShield(Look)), carriageM, Shadowless: true));
                mesh.Instances.Add(new MeshInstance(cannon.Barrel, barrelM));
                if (vehicle is null || vehicle.Gun.ReloadNeeded <= 0)
                    mesh.Instances.Add(new MeshInstance(cannon.Chamber, Matrix4x4.CreateTranslation(TrainKit.CannonChamber) * barrelM));
            }
            else
                mesh.Instances.Add(new MeshInstance(Piece("gun", () => TrainKit.Gun(Look)), gunM));
            // The shot (pipeline VFX: "muzzle flash", additive): with the art's flipbooks, the flash, its light, the wad
            // and the powder smoke the gun car runs out of (Effects.CannonShot); without them, a hot star and its light.
            if (vehicle is { Gun.LastShotTick: > 0 } v && tick >= v.Gun.LastShotTick)
            {
                var muzzle = Vector3.Transform(TrainKit.CannonMuzzle, barrelM);
                double age = (tick - v.Gun.LastShotTick) * Sim.SimConstants.TickSeconds;
                if (Effects.HasFlames)
                    Effects.CannonShot(mesh, muzzle, Vector3.Normalize(Vector3.TransformNormal(-Vector3.UnitZ, barrelM)),
                        Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, m)), Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitY, m)),
                        (float)frame.Velocity.Length, age, v.Gun.LastShotTick);
                else if (tick - v.Gun.LastShotTick <= 2)
                {
                    float fade = 1 - (tick - v.Gun.LastShotTick) / 3f;
                    int shot = (int)(v.Gun.LastShotTick % 4);
                    mesh.Billboard(muzzle, 0.9f * fade, shot * 0.8f, new Vector4(1.0f, 0.75f, 0.4f, fade), -1, FxBlend.Additive);
                    mesh.Billboard(muzzle, 0.35f, shot * 1.3f, new Vector4(1.2f, 1.0f, 0.8f, fade), -1, FxBlend.Additive, stretch: 2.5f);
                    mesh.PointLights.Add(new PointLight(muzzle, new Vector3(1.6f, 1.1f, 0.6f) * fade, 12));
                }
            }
        }
        return true;
    }
}
