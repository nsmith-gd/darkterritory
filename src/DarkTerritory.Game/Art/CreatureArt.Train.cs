using System.Numerics;
using Ballast;
using Ballast.Assets;
using Ballast.Render;
using DarkTerritory.Sim.Enemies;

namespace DarkTerritory.Game.Art;

/// <summary>
/// The train's own creatures (the director's brief of 8 Oct 2026): the Brakeman on the roofs (note 364;
/// tools/blender/brakeman.py), the Knotter in a coupling (note 365; knotter.py) and Hotbox in a truck (note 367; hotbox.py).
/// Each keeps what it's doing in its <see cref="Enemy.Height"/> (its mode); GreyboxScene keeps how long it's been at it and what
/// it did before (the wire doesn't), and what the scene knows that the creature doesn't: the gap the Knotter's across, the
/// brake wheel the Brakeman's at, how fast the train's going.
/// </summary>
public sealed partial class CreatureArt
{
    // Set by Enemy(e) for one draw of one of them: its mode (the sim's), how long it's been in it (s, or −1), and who it's got.
    int? _trainMode;
    double _trainSince = -1;
    Vector3? _knotterVictim;

    /// <summary>What the one about to be drawn was doing before (its mode's number; GreyboxScene keeps it). Used once.</summary>
    public int? ModeWas { get; set; }

    /// <summary>The gap the Knotter about to be drawn is across (m: its car's end to the next car's front; GreyboxScene's, from
    /// where the sim has it, the gap's middle). Used once; 5 m (enemies.json knotter.gap) without it.</summary>
    public float? KnotterSpan { get; set; }

    /// <summary>Where the brake wheel a winding Brakeman's at is from him (his car's frame, m; GreyboxScene's). Used once.</summary>
    public Vector3? BrakemanWheel { get; set; }

    /// <summary>How fast the train's going (m/s; GreyboxScene's): Hotbox's knock, once a wheel turn, and its smoke laid back.</summary>
    public float TrainSpeed { get; set; }

    BrakemanTuning? _brakeman;
    HotboxTuning? _hotboxTuning;

    /// <summary>The Brakeman's tuning, as the content has it (his lash's beat).</summary>
    public BrakemanTuning BrakemanTuning => _brakeman ??= DataFile.Load<EnemyTuning>(Path.Combine(ContentRoot, EnemyTuning.File)).Brakeman;

    /// <summary>Hotbox's tuning, as the content has it (its snap's beat, its wheel's round, its scuttle).</summary>
    public HotboxTuning HotboxTuning => _hotboxTuning ??= DataFile.Load<EnemyTuning>(Path.Combine(ContentRoot, EnemyTuning.File)).Hotbox;

    // The clips' lengths (tools/blender/brakeman.py; CreatureArtTests pins them to the model): his climb (the sim's
    // climbSeconds), his drop over the side, his lash and the moment in it the chain strikes.
    public const double BrakemanClimbSeconds = 2.0, BrakemanDropSeconds = 1.2, BrakemanLashSeconds = 0.8, BrakemanLashStrike = 0.5;

    /// <summary>The Brakeman's mode as his phase has it, for a draw without the sim's (a test's, the greybox's).</summary>
    public static BrakemanMode BrakemanModeOf(SpinePhase phase) => phase switch
    {
        SpinePhase.Alert or SpinePhase.BreakOff => BrakemanMode.Flee,
        SpinePhase.Telegraph or SpinePhase.Commit or SpinePhase.Grab or SpinePhase.Punish => BrakemanMode.Cornered,
        _ => BrakemanMode.Walk,
    };

    /// <summary>
    /// The clip the Brakeman plays, how far into it, and whether it loops (an empty clip: nothing to see): by his mode (the
    /// sim's), how long he's been in it (<paramref name="since"/>) and what he did before (<paramref name="was"/>). Climbing up
    /// the end ladder, the climb; walking the roofs, his stooped quick walk; at a car's wheel, winding it; chased, running
    /// low; cornered, backing with the chain up, and on the sim's beat (<paramref name="phaseSeconds"/>: the telegraph starts
    /// again at each lash) the lash, its strike on the bite; hidden, the drop over the side he went down, then nothing.
    /// </summary>
    public static (string Clip, double Time, bool Loop) BrakemanClip(BrakemanMode mode, BrakemanMode? was, double since, double phaseSeconds, BrakemanTuning t)
    {
        switch (mode)
        {
            case BrakemanMode.Climb:
                return ("climb", Math.Min(since, BrakemanClimbSeconds - 1e-3), false);
            case BrakemanMode.Wind:
                return ("wind", since, true);
            case BrakemanMode.Flee:
                return ("flee", since, true);
            case BrakemanMode.Cornered:
                {
                    double windup = t.LashEvery - BrakemanLashStrike;
                    if (phaseSeconds >= windup && phaseSeconds < windup + BrakemanLashSeconds)
                        return ("lash", phaseSeconds - windup, false);
                    if (since > t.LashEvery && phaseSeconds < BrakemanLashSeconds - BrakemanLashStrike)
                        return ("lash", phaseSeconds + BrakemanLashStrike, false);
                    return ("cornered", since, true);
                }
            case BrakemanMode.Hidden:
                return was is BrakemanMode.Walk or BrakemanMode.Flee or BrakemanMode.Wind && since < BrakemanDropSeconds
                    ? ("drop", since, false) : ("", 0, false);
            default:
                return ("walk", since, true);
        }
    }

    bool Brakeman(MeshBuilder mesh, in Matrix4x4 model, SpinePhase phase, double t)
    {
        var mode = _trainMode is { } m ? (BrakemanMode)m : BrakemanModeOf(phase);
        var was = ModeWas is { } w ? (BrakemanMode?)w : null;
        double since = _trainSince >= 0 ? _trainSince : t;
        var (clip, at, loop) = BrakemanClip(mode, was, since, t, BrakemanTuning);
        // Down under or inside the train, out of sight: not there at all.
        if (clip.Length == 0)
            return true;
        return Draw(mesh, "brakeman", clip, at, loop, model, seed: 364);
    }

    // The Knotter (tools/blender/knotter.py): its knots' centres either side of its middle at rest (m), and how long it is
    // coiled under the coupling before it's forced the cars apart (m: the U it hangs in, coming up out from under).
    public const float KnotterEnd = 2.4f, KnotterCoiled = 3.6f;
    /// <summary>How far in from each end of the gap its knots lie (m): its claws reach on from them over the cars' end sills.</summary>
    public const float KnotterInset = 0.35f;
    // Its clamp onto the sills at the start of the force, and its slip under a foot at the start of the coil (clip lengths).
    public const double KnotterClampSeconds = 0.6, KnotterSlipSeconds = 0.5;
    // How it coils round whoever slipped: its radius (m), its turns, and from their feet how high it climbs them.
    const float CoilRadius = 0.32f, CoilTurns = 1.75f, CoilLow = -0.05f, CoilHigh = 0.85f;

    /// <summary>The Knotter's mode as its phase has it, for a draw without the sim's.</summary>
    public static KnotterMode KnotterModeOf(SpinePhase phase) => phase switch
    {
        SpinePhase.Dormant or SpinePhase.Alert => KnotterMode.Creep,
        SpinePhase.Commit or SpinePhase.Grab or SpinePhase.Punish => KnotterMode.Coil,
        SpinePhase.BreakOff => KnotterMode.Slack,
        _ => KnotterMode.Taut,
    };

    /// <summary>The clip the Knotter plays by its mode and how long it's been in it: creeping up out from under, writhing; forcing
    /// the cars apart, its claws clamping onto the sills, then swelling and twisting; taut across the gap, creaking; slack at a
    /// stand, writhing; coiled round whoever slipped, a strand rolling under their foot, then squeezing.</summary>
    public static (string Clip, double Time, bool Loop) KnotterClip(KnotterMode mode, double since) => mode switch
    {
        KnotterMode.Creep => ("creep", since, true),
        KnotterMode.Force => since < KnotterClampSeconds ? ("clamp", since, false) : ("force", since - KnotterClampSeconds, true),
        KnotterMode.Slack => ("exposed", since, true),
        KnotterMode.Coil => since < KnotterSlipSeconds ? ("slip", since, false) : ("coil", since - KnotterSlipSeconds, true),
        _ => ("taut", since, true),
    };

    /// <summary>
    /// How long its body is laid across a gap <paramref name="span"/> wide (m, knot to knot): coming up under the coupling it
    /// hangs in a U longer than the gap (<see cref="KnotterCoiled"/>), straightening as it forces the cars apart and stretching
    /// once it's straight; taut, the gap; slack at a stand, sagging a little more than it.
    /// </summary>
    public static float KnotterLength(KnotterMode mode, float span) => mode switch
    {
        KnotterMode.Creep or KnotterMode.Force => MathF.Max(span - 2 * KnotterInset, KnotterCoiled),
        KnotterMode.Slack => (span - 2 * KnotterInset) * 1.06f,
        _ => span - 2 * KnotterInset,
    };

    /// <summary>
    /// The line the Knotter's body is laid along (model space: its front knot, on its car's end, toward −Z), the gap
    /// <paramref name="span"/> wide end to end: straight when it's as long as the gap, sagging into a hanging curve when it's
    /// longer; coiled, it runs from its front knot to whoever it's got (<paramref name="victim"/>, their feet), round them
    /// up from their feet in <see cref="CoilTurns"/> turns, and on to its back knot.
    /// </summary>
    public static List<Vector3> KnotterCurve(KnotterMode mode, float span, float length, Vector3? victim)
    {
        float h = MathF.Max(0.3f, span / 2 - KnotterInset);
        var a = new Vector3(0, 0, -h);
        var b = new Vector3(0, 0, h);
        var pts = new List<Vector3>();
        if (mode == KnotterMode.Coil)
        {
            float vz = Math.Clamp(victim?.Z ?? 0, -h + 0.6f, h - 0.6f);
            float vx = Math.Clamp(victim?.X ?? 0, -0.3f, 0.3f);
            pts.Add(a);
            const int n = 40;
            float start = -MathF.PI / 2;
            for (int i = 0; i <= n; i++)
            {
                float k = i / (float)n;
                float phi = start + k * CoilTurns * MathF.Tau;
                pts.Add(new Vector3(vx + MathF.Cos(phi) * CoilRadius, CoilLow + (CoilHigh - CoilLow) * k, vz + MathF.Sin(phi) * CoilRadius));
            }
            pts.Add(b);
            return pts;
        }
        // A hanging curve: down by `sag` in the middle, its length the body's.
        float Length(float sag)
        {
            float s = 0;
            var prev = a;
            for (int i = 1; i <= 48; i++)
            {
                float v = i / 48f;
                var p = new Vector3(0, -sag * 4 * v * (1 - v), -h + 2 * h * v);
                s += Vector3.Distance(prev, p);
                prev = p;
            }
            return s;
        }
        float lo = 0, hi = 4;
        if (length > 2 * h + 1e-3f)
            for (int i = 0; i < 30; i++)
            {
                float mid = (lo + hi) / 2;
                if (Length(mid) < length)
                    lo = mid;
                else
                    hi = mid;
            }
        for (int i = 0; i <= 48; i++)
        {
            float v = i / 48f;
            pts.Add(new Vector3(0, -lo * 4 * v * (1 - v), -h + 2 * h * v));
        }
        return pts;
    }

    /// <summary>
    /// Lays the Knotter's posed body along <paramref name="curve"/>: each of its segments carried, as it's posed, from where it
    /// lies at rest (straight down its length, its front knot at −<see cref="KnotterEnd"/>) to its share of the way along the
    /// curve, turned to it; each knot and its claws with its end. The body's stretched or bunched along the curve as the curve
    /// is longer or shorter than it is at rest, its own writhing and twisting kept.
    /// </summary>
    static void LayAlong(Entry e, IReadOnlyList<Vector3> curve)
    {
        var sk = e.Model.Skeleton;
        var pose = e.Pose;
        var acc = new float[curve.Count];
        for (int i = 1; i < curve.Count; i++)
            acc[i] = acc[i - 1] + Vector3.Distance(curve[i - 1], curve[i]);
        float total = MathF.Max(acc[^1], 1e-4f);

        (Vector3 At, Vector3 Along) Sample(float u)
        {
            float s = Math.Clamp(u, 0, 1) * total;
            int i = Array.BinarySearch(acc, s);
            if (i < 0)
                i = ~i;
            i = Math.Clamp(i, 1, curve.Count - 1);
            float seg = MathF.Max(acc[i] - acc[i - 1], 1e-6f);
            var p = Vector3.Lerp(curve[i - 1], curve[i], Math.Clamp((s - acc[i - 1]) / seg, 0, 1));
            // (Its direction over a stretch either side, so a bone turns smoothly round a coil's bend.)
            int i0 = Math.Max(0, i - 2), i1 = Math.Min(curve.Count - 1, i + 1);
            var d = curve[i1] - curve[i0];
            return (p, d.LengthSquared() > 1e-10f ? Vector3.Normalize(d) : Vector3.UnitZ);
        }

        static Matrix4x4 Carry(Vector3 rest, Vector3 at, Vector3 along)
        {
            var up0 = MathF.Abs(along.Y) > 0.95f ? Vector3.UnitX : Vector3.UnitY;
            var right = Vector3.Normalize(Vector3.Cross(up0, along));
            var up = Vector3.Cross(along, right);
            var turn = new Matrix4x4(right.X, right.Y, right.Z, 0, up.X, up.Y, up.Z, 0, along.X, along.Y, along.Z, 0, 0, 0, 0, 1);
            return Matrix4x4.CreateTranslation(-rest) * turn * Matrix4x4.CreateTranslation(at);
        }

        var (front, frontAlong) = Sample(0);
        var (back, backAlong) = Sample(1);
        var frontCarry = Carry(new Vector3(0, 0, -KnotterEnd), front, frontAlong);
        var backCarry = Carry(new Vector3(0, 0, KnotterEnd), back, backAlong);
        for (int bone = 0; bone < sk.Count; bone++)
        {
            string name = sk.Names[bone];
            Matrix4x4 carry;
            if (name.StartsWith("seg_", StringComparison.Ordinal))
            {
                var rest = Matrix4x4.Invert(sk.InverseBind[bone], out var bind) ? bind.Translation : Vector3.Zero;
                var (at, along) = Sample((rest.Z + KnotterEnd) / (2 * KnotterEnd));
                carry = Carry(rest with { X = 0, Y = 0 }, at, along);
            }
            else if (name.EndsWith("_f", StringComparison.Ordinal) || name.StartsWith("claw_f", StringComparison.Ordinal))
                carry = frontCarry;
            else if (name.EndsWith("_b", StringComparison.Ordinal) || name.StartsWith("claw_b", StringComparison.Ordinal))
                carry = backCarry;
            else
                continue;
            pose.World[bone] *= carry;
            pose.Skin[bone] = sk.InverseBind[bone] * pose.World[bone];
        }
    }

    bool Knotter(MeshBuilder mesh, in Matrix4x4 model, SpinePhase phase, double t)
    {
        var mode = _trainMode is { } m ? (KnotterMode)m : KnotterModeOf(phase);
        double since = _trainSince >= 0 ? _trainSince : t;
        float span = KnotterSpan ?? 5f;
        var victim = _knotterVictim;
        (KnotterSpan, _knotterVictim) = (null, null);
        var (clip, at, loop) = KnotterClip(mode, since);
        // Killed, it unlays where it hung, gone slack (its death's own twisting kept).
        float length = _dying ? (span - 2 * KnotterInset) * 1.1f : KnotterLength(mode, span);
        var curve = KnotterCurve(_dying && mode == KnotterMode.Coil ? KnotterMode.Slack : mode, span, length, victim);
        return Draw(mesh, "knotter", clip, at, loop, model, e => LayAlong(e, curve), seed: 365);
    }

    // Hotbox (tools/blender/hotbox.py): its unfold out of the truck, its prising out onto the ballast and how far out that leaves
    // it (m), its snap and the moment in it the mandibles close, its knock (one a wheel turn), and how fast it scuttles off.
    public const double HotboxUnfoldSeconds = 1.0, HotboxPrisedSeconds = 1.0, HotboxSnapSeconds = 0.667, HotboxSnapStrike = 0.3,
        HotboxKnockSeconds = 0.5;
    const float HotboxPrisedOut = 1.0f, HotboxScuttle = 2.5f;

    /// <summary>Hotbox's mode as its phase has it, for a draw without the sim's.</summary>
    public static HotboxMode HotboxModeOf(SpinePhase phase) => phase switch
    {
        SpinePhase.Telegraph or SpinePhase.Commit or SpinePhase.Grab or SpinePhase.Punish => HotboxMode.Unfolded,
        SpinePhase.BreakOff => HotboxMode.Prised,
        SpinePhase.Alert => HotboxMode.Glow,
        _ => HotboxMode.Knock,
    };

    /// <summary>
    /// The clip Hotbox plays, how far in, whether it loops, and how bright its belly is (the instance's glow over its emission
    /// map): clamped in its truck it knocks once a wheel turn at <paramref name="speed"/> (still, at a stand), its belly a dull
    /// ember; glowing, swollen and venting, bright; seized, white-hot; unfolded at a stand, out onto the ballast and then
    /// there, its snap on the sim's beat (<paramref name="phaseSeconds"/>: the telegraph starts again at each bite); prised,
    /// levered out and over, then off at a scuttle.
    /// </summary>
    public static (string Clip, double Time, bool Loop, float Glow) HotboxClip(HotboxMode mode, double since, double phaseSeconds, float speed, HotboxTuning t)
    {
        float pulse = (float)(0.5 + 0.5 * Math.Sin(since * 4.1));
        switch (mode)
        {
            case HotboxMode.Knock:
                {
                    double turns = Math.Abs(speed) / (Math.PI * t.WheelDiameter);
                    return turns < 0.2 ? ("clamped", since, true, 0.3f + 0.1f * pulse) : ("knock", since * turns * HotboxKnockSeconds, true, 0.35f + 0.25f * pulse);
                }
            case HotboxMode.Glow:
                return ("glow", since, true, 1.5f + 0.5f * pulse);
            case HotboxMode.Seized:
                return ("glow", since * 1.6, true, 2.4f + 0.6f * pulse);
            case HotboxMode.Unfolded:
                {
                    if (since < HotboxUnfoldSeconds)
                        return ("unfold", since, false, 1.3f);
                    double windup = t.SnapEvery - HotboxSnapStrike;
                    if (phaseSeconds >= windup && phaseSeconds < windup + HotboxSnapSeconds)
                        return ("snap", phaseSeconds - windup, false, 1.6f);
                    if (since > t.SnapEvery && phaseSeconds < HotboxSnapSeconds - HotboxSnapStrike)
                        return ("snap", phaseSeconds + HotboxSnapStrike, false, 1.6f);
                    return ("out", since, true, 1.1f + 0.3f * pulse);
                }
            default:
                return since < HotboxPrisedSeconds ? ("prised", since, false, 0.9f) : ("scuttle", since - HotboxPrisedSeconds, true, 0.7f);
        }
    }

    bool Hotbox(MeshBuilder mesh, in Matrix4x4 model, SpinePhase phase, double t, double seed)
    {
        var mode = _trainMode is { } m ? (HotboxMode)m : HotboxModeOf(phase);
        double since = _trainSince >= 0 ? _trainSince : t;
        var (clip, at, loop, glow) = HotboxClip(mode, since, t, TrainSpeed, HotboxTuning);
        var placed = model;
        // Prised, it runs off out into the dark (out from the car: its +X), from where it landed on the ballast.
        if (mode == HotboxMode.Prised && since >= HotboxPrisedSeconds)
            placed = Matrix4x4.CreateTranslation(HotboxPrisedOut + HotboxScuttle * (float)(since - HotboxPrisedSeconds), 0, 0) * model;
        bool drawn = Draw(mesh, "hotbox", clip, at, loop, placed, glow: _dying ? 0.2f : glow, seed: 367);
        if (!drawn || _dying)
            return drawn;
        // Its heat lights the wheels and the ballast round it; glowing and seized it smokes there, the hot box's own smoke
        // (Effects.HotBoxSmoke), laid back by the train's going.
        var (r, u, b) = Basis(model);
        var at3 = placed.Translation + r * 0.12f;
        mesh.PointLights.Add(new PointLight(at3, Palette.FurnaceOrange * (0.35f + 0.35f * glow), 1.6f + 0.7f * glow));
        if (mode is HotboxMode.Glow or HotboxMode.Seized)
            _fx.HotBoxSmoke(mesh, at3 + u * 0.15f, u, b, mode == HotboxMode.Seized ? 1f : 0.6f, TrainSpeed, t, 367 + (int)seed);
        return true;
    }

    /// <summary>
    /// One of the train's own as the sim has it (Enemy(e)): its mode for the draw, and where it's drawn: the Brakeman turned to
    /// the way he's going (its Lateral), and at a wheel stood beside it facing it; the Knotter's victim, in its model space;
    /// Hotbox turned out to its side of the car (the model's +X its back, out from the truck).
    /// </summary>
    Matrix4x4 Trainfolk(Enemy e, in Matrix4x4 model, Prey? prey)
    {
        _trainMode = (int)e.Height;
        switch (e)
        {
            case Sim.Enemies.Brakeman { Mode: BrakemanMode.Wind } when BrakemanWheel is { } wheel:
                // Stood half a metre off the wheel toward the roof's middle, facing across the car at it (the clip's wheel is
                // in front of him: brakeman.py WHEEL_AT).
                BrakemanWheel = null;
                return Matrix4x4.CreateRotationY(MathF.PI / 2) * Matrix4x4.CreateTranslation(wheel.X + BrakemanStand, 0, wheel.Z) * model;
            case Sim.Enemies.Brakeman:
                BrakemanWheel = null;
                return Matrix4x4.CreateRotationY((float)e.Lateral) * model;
            case Sim.Enemies.Knotter:
                if (prey is { } held && Matrix4x4.Invert(model, out var toModel))
                    _knotterVictim = Vector3.Transform(held.Feet, toModel);
                return model;
            case Sim.Enemies.Hotbox hotbox:
                return hotbox.Side < 0 ? Matrix4x4.CreateRotationY(MathF.PI) * model : model;
            default:
                return model;
        }
    }

    /// <summary>How far from a brake wheel's centre a winding Brakeman stands (m: brakeman.py's WHEEL_AT, 0.5 m ahead of him).</summary>
    public const float BrakemanStand = 0.5f;

    void ForgetTrainfolk()
    {
        (_trainMode, _trainSince, _knotterVictim) = (null, -1, null);
        (ModeWas, KnotterSpan, BrakemanWheel) = (null, null, null);
    }
}
