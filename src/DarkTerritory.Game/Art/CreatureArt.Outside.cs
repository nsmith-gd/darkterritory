using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Enemies;

namespace DarkTerritory.Game.Art;

/// <summary>
/// The Mourners, the Freight Beetle and Tower Jaw (GDD §21 OUTSIDE; the director's briefs of 8 Oct 2026; notes 362, 366,
/// 363), the Pickers (note 574) and the house creatures, the Lodger and the Householder (notes 583, 584): each drawn by the
/// sim's mode (replicated in its Height) and how long it's been in it (GreyboxScene watches it change), each facing its own
/// heading.
/// </summary>
public sealed partial class CreatureArt
{
    // Set by Enemy(e) for one draw of these: the sim's mode, and how long it's been in it (s, or −1).
    MournerMode? _mournerMode;
    BeetleMode? _beetleMode;
    TowerJawMode? _towerJawMode;
    PickerMode? _pickerMode;
    LodgerMode? _lodgerMode;
    HouseholderMode? _householderMode;
    double _outsideSince = -1;
    float _outsidePace;
    TowerJawTuning? _towerJaw;
    MeshAsset? _towerWreck;

    /// <summary>Tower Jaw's tuning, as the content has it (its lean, its groan, its gnawing).</summary>
    public TowerJawTuning TowerJawTuning => _towerJaw ??= DataFile.Load<EnemyTuning>(Path.Combine(ContentRoot, EnemyTuning.File)).TowerJaw;

    void ForgetOutside()
    {
        (_mournerMode, _beetleMode, _towerJawMode, _outsideSince, _outsidePace) = (null, null, null, -1, 0);
        (_pickerMode, _lodgerMode, _householderMode) = (null, null, null);
    }

    /// <summary>The live enemy's mode and pace for its draw; the basis it's drawn at (Tower Jaw set back of its jaws).</summary>
    Matrix4x4 OutsideIn(Enemy e, in Matrix4x4 model, float pace)
    {
        _outsidePace = pace;
        switch (e)
        {
            case Mourner m:
                _mournerMode = m.Mode;
                return model;
            case FreightBeetle b:
                _beetleMode = b.Mode;
                return model;
            case TowerJaw j:
                _towerJawMode = j.Mode;
                return model;
            case Picker p:
                _pickerMode = p.Mode;
                return model;
            case Lodger l:
                _lodgerMode = l.Mode;
                return model;
            case Householder h:
                _householderMode = h.Mode;
                return model;
        }
        return model;
    }

    /// <summary>
    /// These kinds' draw (null for any other kind): at <paramref name="model"/>, its feet at the origin facing −Z, by the
    /// sim's mode where Enemy(e) set it, else its phase's (<see cref="MournerModeOf"/>...). <paramref name="extra2"/> is
    /// the enemy's id where it's needed to tell one of a kind from the next (Enemy(e) passes it; a Mourner's group lead).
    /// </summary>
    bool? Outside(MeshBuilder mesh, in Matrix4x4 model, EnemyKind kind, SpinePhase phase, double t, double extra, double extra2)
    {
        double since = _outsideSince >= 0 ? _outsideSince : t;
        switch (kind)
        {
            case EnemyKind.Mourners when _models.ContainsKey("mourner"):
                {
                    // THE MOURNERS (note 362; tools/blender/mourner.py; docs/design/creatures/mourners.md §3, §5): a nervous
                    // huddle. Coming in, a darting run; waiting their chance, hunched and rocking, the heads jerking round;
                    // edging in, the slow bent creep; started back from someone near, a jump back, arms up; dragging the body,
                    // hands hooked in its clothes, leant back and hauling in tugs; leaving, a scatter. Each its own size and its
                    // own time (by its id), so the group's never in step.
                    var mode = _mournerMode ?? MournerModeOf(phase);
                    int id = (int)extra2;
                    var (clip, at, loop) = MournerClip(mode, since + (mode is MournerMode.Wait or MournerMode.Creep or MournerMode.Follow ? id * 0.37 : 0), _outsidePace);
                    float size = 0.93f + 0.14f * Hash01(id, 1);
                    return Draw(mesh, "mourner", clip, at, loop, Matrix4x4.CreateScale(size) * model, seed: 362 + id);
                }
            case EnemyKind.FreightBeetle when _models.ContainsKey("freight_beetle"):
                {
                    // THE FREIGHT BEETLE (note 366; tools/blender/freight_beetle.py; docs/design/creatures/freight-beetle.md §3,
                    // §5): settled with its feelers working; walking round its load; braced behind it, head down (the
                    // telegraph); heaving it on in slow strides; reared back off it when someone's at its head.
                    var mode = _beetleMode ?? BeetleModeOf(phase);
                    var (clip, at, loop) = BeetleClip(mode, since, _outsidePace);
                    return Draw(mesh, "freight_beetle", clip, at, loop, model, seed: 366);
                }
            case EnemyKind.TowerJaw when _models.ContainsKey("tower_jaw"):
                {
                    // TOWER JAW (note 363; tools/blender/tower_jaw.py; docs/design/creatures/tower-jaw.md §3, §5): side-on at
                    // the post, its head working, chips flying; head up and turning when someone comes; reared, incisors
                    // bared, slapping its tail (the telegraph); the lunge and bite; the heavy lope off. Where the sim keeps
                    // it is its jaws at the post (gnawAt off the leg): the body's set back of it. Once it's brought the tower
                    // down it's the wreck across the line (TowerWreck), not a creature.
                    var mode = _towerJawMode ?? TowerJawModeOf(phase);
                    if (mode == TowerJawMode.Wreck)
                        return TowerWreck(mesh, model);
                    var (clip, at, loop) = TowerJawClip(mode, since, _outsidePace);
                    var back = Matrix4x4.CreateTranslation(0, 0, TowerJawBack) * model;
                    bool drawn = Draw(mesh, "tower_jaw", clip, at, loop, back, glow: 1f + 0.25f * (float)Math.Max(0, Math.Sin(t * 1.7)), seed: 363);
                    if (drawn && clip == "gnaw")
                        Chips(mesh, model, at);
                    return drawn;
                }
            case EnemyKind.Pickers when _models.ContainsKey("picker"):
                {
                    // THE PICKERS (note 574; tools/blender/picker.py; docs/design/creatures/pickers.md §3): up out of a drain;
                    // scuttling on all fours, or crouched with the head jerking round; under a crate, its hands up its sides,
                    // the little legs pattering (a heavy one's second beside it, hoisting at its edge); the bite and the
                    // scatter; down the drain head first, and gone. Several at once, each its own size and its own time (by
                    // its id), so the group's never in step.
                    var mode = _pickerMode ?? PickerModeOf(phase);
                    var was = ModeWas is { } w ? (PickerMode?)w : null;
                    int id = (int)extra2;
                    if (PickerClip(mode, was, since, _outsidePace, id * 0.37) is not { } play)
                        return true;
                    float size = 0.92f + 0.16f * Hash01(id, 2);
                    return Draw(mesh, "picker", play.Clip, play.Time, play.Loop, Matrix4x4.CreateScale(size) * model, seed: 574 + id);
                }
            case EnemyKind.Lodger when _models.ContainsKey("lodger"):
                {
                    // THE LODGER (note 583; tools/blender/lodger.py): folded into its corner, breathing; rearing up with its
                    // jaw dropped open, shrieking (the telegraph); the lunge; the loping chase; bashing at a door; the slow
                    // walk back to its room.
                    var mode = _lodgerMode ?? LodgerModeOf(phase);
                    var (clip, at, loop) = LodgerClip(mode, since, _outsidePace);
                    return Draw(mesh, "lodger", clip, at, loop, model, seed: 583);
                }
            case EnemyKind.Householder when _models.ContainsKey("householder"):
                {
                    // THE HOUSEHOLDER (note 584; tools/blender/householder.py): sat at its table, its hands flat on it, still,
                    // its eyes on you (the head turned to whoever's looking); getting up, slowly; the stiff walk after
                    // whoever took from it; both hands on them; the walk back to its chair. Sat, a blow doesn't move it.
                    var mode = _householderMode ?? HouseholderModeOf(phase);
                    var (clip, at, loop) = HouseholderClip(mode, since);
                    if (mode is HouseholderMode.Sit or HouseholderMode.Watch or HouseholderMode.Rise)
                        _hit = -1;
                    float look = HouseholderLooks(mode);
                    var placed = model;
                    return Draw(mesh, "householder", clip, at, loop, model, m => LookAtEye(m, placed, look), seed: 584);
                }
            case EnemyKind.Mourners or EnemyKind.FreightBeetle or EnemyKind.TowerJaw or EnemyKind.Pickers or EnemyKind.Lodger or EnemyKind.Householder:
                return false;
            case EnemyKind.Waker when _models.ContainsKey("waker"):
                // THE WAKER, the Peat Man (note 679; tools/blender/waker.py; CreatureArt.Waker.cs).
                return DrawWaker(mesh, model, phase, t, extra);
            case EnemyKind.Waker:
                return WakerStandIn(mesh, model, phase, t, WakersTuning.RiseSeconds);
        }
        return null;
    }

    /// <summary>
    /// A Waker (note 588; docs/design/creatures/wakers.md) where his model isn't there (note 679's is): the land standing
    /// up, a car a mouthful. Rising, it heaves up out of the ground; running, it's bent over the line, long arms reaching;
    /// holding the train, the arms up at the last car and its head down to it. Earth, unlit.
    /// </summary>
    static bool WakerStandIn(MeshBuilder mesh, in Matrix4x4 model, SpinePhase phase, double t, double riseSeconds)
    {
        var o = model.Translation;
        var r = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitX, model));
        var u = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitY, model));
        var b = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, model));
        var earth = Palette.DeepBrown * 0.55f;
        double sink = WakerRise.Sink(phase, t, riseSeconds);
        bool holds = phase is SpinePhase.Punish or SpinePhase.Grab;
        void Box(double x, double y, double z, double hx, double hy, double hz, Vector3 colour) =>
            mesh.Box(o + r * (float)x + u * (float)(y - sink) + b * (float)z, r, u, b, new Vector3((float)hx, (float)hy, (float)hz), colour);
        Box(0, 11, 6, 7, 6, 11, earth);
        Box(0, 16, -6, 5, 4, 5, earth * 0.9f);
        Box(0, holds ? 13 : 15, -12, 3, 3, 4, earth * 0.6f);
        Box(0, holds ? 11 : 13, -15.5, 2.4, 0.8, 0.6, earth * 0.3f);
        foreach (double x in new[] { -5.5, 5.5 })
        {
            Box(x, holds ? 12 : 7, holds ? -14 : -11, 1.2, holds ? 3 : 7, 1.2, earth * 0.8f);
            Box(x * 0.9, 5, 12, 1.6, 5, 1.6, earth * 0.8f);
        }
        return true;
    }

    static float Hash01(int id, int k) => 0.5f + 0.5f * MathF.Sin(id * 12.9898f + k * 78.233f);

    // ----------------------------------------------------------------------------------------------------------------
    // The Mourners

    // Their clips' lengths (tools/blender/mourner.py): the startle's jump back.
    public const double MournerStartleSeconds = 0.6;

    /// <summary>A Mourner's mode as its phase has it, for a draw without the sim's (a test's, the greybox's).</summary>
    public static MournerMode MournerModeOf(SpinePhase phase) => phase switch
    {
        SpinePhase.Dormant => MournerMode.Come,
        SpinePhase.Alert => MournerMode.Creep,
        SpinePhase.Commit or SpinePhase.Grab or SpinePhase.Punish => MournerMode.Drag,
        SpinePhase.BreakOff => MournerMode.Leave,
        _ => MournerMode.Wait,
    };

    /// <summary>
    /// The clip a Mourner plays, how far into it, and whether it loops: by its mode (the sim's), how long it's been in it
    /// and how fast it's going (<paramref name="pace"/>, m/s: waiting in their ring they shift about, at a creep).
    /// </summary>
    public static (string Clip, double Time, bool Loop) MournerClip(MournerMode mode, double modeSeconds, float pace) => mode switch
    {
        MournerMode.Come or MournerMode.Leave => ("scatter", modeSeconds, true),
        MournerMode.Creep => ("creep", modeSeconds, true),
        MournerMode.Drag => ("drag", modeSeconds, true),
        MournerMode.Startle when modeSeconds < MournerStartleSeconds => ("startle", modeSeconds, false),
        MournerMode.Startle => pace > 2 ? ("scatter", modeSeconds, true) : ("wait", modeSeconds, true),
        _ => pace > 2.2f ? ("scatter", modeSeconds, true) : pace > 0.35f ? ("creep", modeSeconds, true) : ("wait", modeSeconds, true),
    };

    // ----------------------------------------------------------------------------------------------------------------
    // The Freight Beetle

    public const double BeetleStartleSeconds = 0.8;

    /// <summary>The Freight Beetle's mode as its phase has it, for a draw without the sim's.</summary>
    public static BeetleMode BeetleModeOf(SpinePhase phase) => phase switch
    {
        SpinePhase.Alert => BeetleMode.Walk,
        SpinePhase.Telegraph => BeetleMode.Brace,
        SpinePhase.Commit or SpinePhase.Grab or SpinePhase.Punish => BeetleMode.Push,
        SpinePhase.BreakOff => BeetleMode.Away,
        _ => BeetleMode.Idle,
    };

    /// <summary>
    /// The clip the Freight Beetle plays: settled, its feelers working; walking (turning on the spot when it isn't getting
    /// anywhere: setting itself behind its load); braced; pushing; reared back, then settled; driven off, walking away.
    /// </summary>
    public static (string Clip, double Time, bool Loop) BeetleClip(BeetleMode mode, double modeSeconds, float pace) => mode switch
    {
        BeetleMode.Walk or BeetleMode.Away => pace < 0.25f && modeSeconds > 0.3 ? ("turn", modeSeconds, true) : ("walk", modeSeconds, true),
        BeetleMode.Brace => ("brace", modeSeconds, true),
        BeetleMode.Push => ("push", modeSeconds, true),
        BeetleMode.Startle when modeSeconds < BeetleStartleSeconds => ("startle", modeSeconds, false),
        _ => ("idle", modeSeconds, true),
    };

    // ----------------------------------------------------------------------------------------------------------------
    // Tower Jaw

    /// <summary>How far back of where the sim keeps it (its jaws at the post, enemies.json towerJaw gnawAt off the leg) its
    /// body's drawn (m): tower_jaw.py's incisors are 1.55 m ahead of its feet, and gnawing it leans into the post's face.</summary>
    public const float TowerJawBack = 0.85f;

    // Its clips' lengths (tools/blender/tower_jaw.py): the head up and round (the turn's first swing), the lunge.
    public const double TowerJawTurnSeconds = 0.45, TowerJawLungeSeconds = 0.8;

    /// <summary>Tower Jaw's mode as its phase has it, for a draw without the sim's.</summary>
    public static TowerJawMode TowerJawModeOf(SpinePhase phase) => phase switch
    {
        SpinePhase.Telegraph or SpinePhase.Grab => TowerJawMode.Threat,
        SpinePhase.Commit => TowerJawMode.Lunge,
        SpinePhase.BreakOff => TowerJawMode.Away,
        SpinePhase.Punish => TowerJawMode.Wreck,
        _ => TowerJawMode.Gnaw,
    };

    /// <summary>
    /// The clip Tower Jaw plays: gnawing; come near, its head comes up and round, then it rears and slaps (the threat);
    /// the lunge, then back to its threat; driven off, the heavy lope (or, stood, its head up listening).
    /// </summary>
    public static (string Clip, double Time, bool Loop) TowerJawClip(TowerJawMode mode, double modeSeconds, float pace) => mode switch
    {
        TowerJawMode.Threat when modeSeconds < TowerJawTurnSeconds => ("turn", modeSeconds, false),
        TowerJawMode.Threat => ("threat", modeSeconds - TowerJawTurnSeconds, true),
        TowerJawMode.Lunge when modeSeconds < TowerJawLungeSeconds => ("lunge", modeSeconds, false),
        TowerJawMode.Lunge => ("threat", modeSeconds - TowerJawLungeSeconds, true),
        TowerJawMode.Away => pace > 0.5f ? ("retreat", modeSeconds, true) : ("turn", modeSeconds, true),
        _ => ("gnaw", modeSeconds, true),
    };

    /// <summary>
    /// How far a coaling tower Tower Jaw's gnawing leans over toward the line (radians): none till it's <c>leanFrom</c>
    /// gnawed through, then more and more to <see cref="TowerMaxLean"/> as it goes; its last <c>groanSeconds</c> (of the
    /// slowest tier's gnawing) it shudders, groaning.
    /// </summary>
    public static float TowerLean(double gnawed, TowerJawTuning t, double time)
    {
        if (gnawed <= t.LeanFrom)
            return 0;
        double k = Math.Clamp((gnawed - t.LeanFrom) / Math.Max(1e-6, 1 - t.LeanFrom), 0, 1);
        double lean = TowerMaxLean * k * k;
        double groan = 1 - t.GroanSeconds / Math.Max(1, t.GnawFor(Sim.Route.RouteTier.Local));
        if (gnawed >= groan)
            lean += TowerMaxLean * 0.08 * Math.Sin(time * 11) * Math.Sin(time * 2.3);
        return (float)lean;
    }

    /// <summary>The most a gnawed coaling tower leans before it goes (radians, about 9°).</summary>
    public const double TowerMaxLean = 0.16;

    /// <summary>The coaling tower Tower Jaw's brought down across the line (StructureKit.CoalingTowerFallen), at
    /// <paramref name="model"/>: its origin on the line at the spout, +X out to the side it stood on.</summary>
    public bool TowerWreck(MeshBuilder mesh, in Matrix4x4 model)
    {
        _towerWreck ??= StructureKit.CoalingTowerFallen(Look, cleared: false);
        mesh.Instances.Add(new MeshInstance(_towerWreck, model));
        return true;
    }

    /// <summary>Wood chips flung off the post as it gnaws, each on its own short arc off its teeth (in its model's frame:
    /// the post is ahead of it, −Z).</summary>
    static void Chips(MeshBuilder mesh, in Matrix4x4 model, double time)
    {
        var (r, u, b) = Basis(model);
        var o = model.Translation;
        for (int i = 0; i < 7; i++)
        {
            double age = (time * 1.9 + i * 0.37) % 1.0;
            float side = (i % 2 == 0 ? 1 : -1) * (0.4f + 0.3f * Hash01(i, 3));
            var start = o + u * 0.75f - b * 0.12f;
            var p = start + r * (side * (float)age * 1.2f) + b * ((float)age * 0.5f) + u * (float)(1.4 * age - 2.6 * age * age);
            mesh.Box(p, r, u, b, new Vector3(0.035f, 0.012f, 0.025f), Palette.TarnishedBrass * 0.7f);
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // The Pickers

    // Their clips' lengths (tools/blender/picker.py): up out of the drain (the sim's emergeSeconds), taking a crate up, the
    // bite, down the drain.
    public const double PickerEmergeSeconds = 0.8, PickerGrabSeconds = 0.4, PickerBiteSeconds = 0.4, PickerDescendSeconds = 0.5;

    /// <summary>A Picker's mode as its phase has it, for a draw without the sim's (a test's, the greybox's).</summary>
    public static PickerMode PickerModeOf(SpinePhase phase) => phase switch
    {
        SpinePhase.Dormant => PickerMode.Emerge,
        SpinePhase.Alert => PickerMode.Wait,
        SpinePhase.Grab or SpinePhase.Punish => PickerMode.Carry,
        SpinePhase.BreakOff => PickerMode.Scatter,
        _ => PickerMode.Scuttle,
    };

    /// <summary>
    /// The clip a Picker plays, how far into it, and whether it loops: by its mode (the sim's), what it did before (its bite
    /// is a tick of the sim's before it scatters; its grab starts its carry), how long it's been at it and how fast it's
    /// going; <paramref name="offset"/> puts each of a group out of step with the rest. Null: down its drain, out of sight.
    /// </summary>
    public static (string Clip, double Time, bool Loop)? PickerClip(PickerMode mode, PickerMode? was, double modeSeconds, float pace, double offset = 0) => mode switch
    {
        PickerMode.Emerge when modeSeconds < PickerEmergeSeconds => ("emerge", modeSeconds, false),
        PickerMode.Carry when modeSeconds < PickerGrabSeconds && was is not (PickerMode.Carry or PickerMode.Heave) => ("grab", modeSeconds, false),
        PickerMode.Carry => ("carry", modeSeconds + offset, true),
        PickerMode.Heave => ("heave", modeSeconds + offset, true),
        PickerMode.Bite => ("bite", modeSeconds, false),
        PickerMode.Scatter when was == PickerMode.Bite && modeSeconds < PickerBiteSeconds => ("bite", modeSeconds, false),
        PickerMode.Scatter when pace > 0.3f => ("scatter", modeSeconds + offset, true),
        PickerMode.Down when modeSeconds < PickerDescendSeconds && was is not PickerMode.Down => ("descend", modeSeconds, false),
        PickerMode.Down => null,
        _ => pace > 0.3f ? ("scuttle", modeSeconds + offset, true) : ("idle", modeSeconds + offset, true),
    };

    // ----------------------------------------------------------------------------------------------------------------
    // The Lodger

    // Its clips' lengths (tools/blender/lodger.py): the lunge, after which it lopes on.
    public const double LodgerLungeSeconds = 0.6;

    /// <summary>The Lodger's mode as its phase has it, for a draw without the sim's.</summary>
    public static LodgerMode LodgerModeOf(SpinePhase phase) => phase switch
    {
        SpinePhase.Telegraph => LodgerMode.Shriek,
        SpinePhase.Commit => LodgerMode.Lunge,
        SpinePhase.Grab or SpinePhase.Punish => LodgerMode.Chase,
        SpinePhase.BreakOff => LodgerMode.Return,
        _ => LodgerMode.Hidden,
    };

    /// <summary>
    /// The clip the Lodger plays: folded in its corner, breathing; the shriek (held at its end, jaw open, if it goes on); the
    /// lunge, then the lope; at a door, bashing; walking back. Stood still in the chase it prowls (the slow walk).
    /// </summary>
    public static (string Clip, double Time, bool Loop) LodgerClip(LodgerMode mode, double modeSeconds, float pace) => mode switch
    {
        LodgerMode.Shriek => ("shriek", modeSeconds, false),
        LodgerMode.Lunge when modeSeconds < LodgerLungeSeconds => ("lunge", modeSeconds, false),
        LodgerMode.Lunge => ("chase", modeSeconds - LodgerLungeSeconds, true),
        LodgerMode.Chase => pace > 0.4f ? ("chase", modeSeconds, true) : ("return", modeSeconds, true),
        LodgerMode.Break => ("break", modeSeconds, true),
        LodgerMode.Return => ("return", modeSeconds, true),
        _ => ("hidden", modeSeconds, true),
    };

    // ----------------------------------------------------------------------------------------------------------------
    // The Householder

    /// <summary>The Householder's mode as its phase has it, for a draw without the sim's.</summary>
    public static HouseholderMode HouseholderModeOf(SpinePhase phase) => phase switch
    {
        SpinePhase.Alert => HouseholderMode.Watch,
        SpinePhase.Telegraph => HouseholderMode.Rise,
        SpinePhase.Commit => HouseholderMode.Hunt,
        SpinePhase.Grab or SpinePhase.Punish => HouseholderMode.Grab,
        SpinePhase.BreakOff => HouseholderMode.Return,
        _ => HouseholderMode.Sit,
    };

    /// <summary>The clip the Householder plays: sat at its table; watching; getting up (held stood at its end); the hunt's stiff
    /// walk; both hands on someone; the walk back.</summary>
    public static (string Clip, double Time, bool Loop) HouseholderClip(HouseholderMode mode, double modeSeconds) => mode switch
    {
        HouseholderMode.Watch => ("watch", modeSeconds, true),
        HouseholderMode.Rise => ("rise", modeSeconds, false),
        HouseholderMode.Hunt => ("hunt", modeSeconds, true),
        HouseholderMode.Grab => ("grab", modeSeconds, true),
        HouseholderMode.Return => ("return", modeSeconds, true),
        _ => ("sit", modeSeconds, true),
    };

    /// <summary>How far the Householder's head turns after whoever's looking (GDD §31: its face too still, its eyes always on
    /// you): a little sat at its table, all the way watching or getting up, most of the way hunting; not with its hands on
    /// someone, or going back.</summary>
    public static float HouseholderLooks(HouseholderMode mode) => mode switch
    {
        HouseholderMode.Sit => 0.4f,
        HouseholderMode.Watch or HouseholderMode.Rise => 1f,
        HouseholderMode.Hunt => 0.7f,
        _ => 0f,
    };

    /// <summary>The most the Householder's head turns from its shoulders (radians), and tips up or down at you.</summary>
    const float HouseholderTurn = 1.3f, HouseholderTip = 0.4f;

    /// <summary>
    /// A posed figure's head and neck turned (<paramref name="look"/> of the way) to the eye (the scene's built about it: the
    /// origin), as far as <see cref="HouseholderTurn"/>, and tipped up or down at it.
    /// </summary>
    static void LookAtEye(Entry m, Matrix4x4 at, float look)
    {
        int head = m.Model.Skeleton.IndexOf("head");
        if (look <= 0 || head < 0 || !Matrix4x4.Invert(at, out var inv))
            return;
        var eye = Vector3.Transform(Vector3.Zero, inv);
        var from = m.Pose.World[head].Translation;
        var to = eye - from;
        float flat = MathF.Sqrt(to.X * to.X + to.Z * to.Z);
        if (flat < 0.05f)
            return;
        // (The model faces −Z: CreateRotationY(a) takes that to (−sin a, 0, −cos a).)
        float yaw = Math.Clamp(MathF.Atan2(-to.X, -to.Z), -HouseholderTurn, HouseholderTurn) * look;
        float tip = Math.Clamp(MathF.Atan2(to.Y, flat), -HouseholderTip, HouseholderTip) * look;
        Bend(m, "neck", Matrix4x4.CreateRotationY(yaw * 0.35f));
        Bend(m, "head", Matrix4x4.CreateRotationY(yaw * 0.65f));
        var right = Vector3.Transform(Vector3.UnitX, Matrix4x4.CreateRotationY(yaw));
        Bend(m, "head", Matrix4x4.CreateFromAxisAngle(right, tip));
    }
}
