using System.Numerics;
using Ballast.Assets;
using Ballast.Render;
using DarkTerritory.Sim.Enemies;

namespace DarkTerritory.Game.Art;

/// <summary>
/// What the scene knows of a Waker that its art needs (GreyboxScene sets it for the draw): whether he's stood at the walls,
/// and, holding the train, where the car he's eating meets his mouth.
/// </summary>
/// <param name="AtWalls">Stopped short of the terminus's gate, under its guns (wakers.stopShort).</param>
/// <param name="Mouth">Holding the train: where he's biting the car he has (camera-relative, as the model matrix is), at the
/// top of the lifted cars' ramp (<see cref="WakerLift"/>). Risen beside who ran: where his hand brings them up to his mouth.</param>
/// <param name="Picker">One risen beside a crewmate off the train (note 710): his own clock, his quarry's id in his extra.</param>
/// <param name="Hand">His, holding them: where the one he took is drawn (camera-relative), his right hand reached onto them.</param>
public readonly record struct WakerScene(bool AtWalls = false, Vector3? Mouth = null, bool Picker = false, Vector3? Hand = null);

/// <summary>
/// THE WAKER · the Peat Man (GDD §8 "A titan"; docs/design/creatures/wakers.md; ARCHITECTURE §8 note 679; tools/blender/waker.py):
/// a bog body sixty metres tall. Lying, face down under the land; rising (TELEGRAPH), pushing himself up out of it, the earth
/// falling off him; running (COMMIT), his walk and his run blended by his speed and played at it (<see cref="WakerGait"/>), so
/// his feet stay where they come down; stood at the walls, swaying, and howling at the guns; holding the train (PUNISH), down
/// on his knees over the line, biting it off a car at a time in four bites a car (<see cref="Bite.Waker"/>, his eat clip's jaws
/// shutting on each): moved along the line and bowed so his mouth is where he's biting (<see cref="WakerReach"/>).
/// </summary>
public sealed partial class CreatureArt
{
    /// <summary>Set by the scene for a Waker's draw, cleared after (a draw without it: the clips as authored, where he stands).</summary>
    public WakerScene WakerIn { get; set; }

    /// <summary>His grab's length (tools/blender/waker.py): down on his knees, both hands on the last car.</summary>
    public const double WakerGrabSeconds = 2.5;

    /// <summary>How far under the land he lies before he rises (m): his whole body's depth, face down.</summary>
    public const double WakerLieDepth = 10;

    /// <summary>At the walls, how long a howl and the stand after it take together (s): the roar, then swaying till the next.</summary>
    public const double WakerRoarEvery = 10;

    /// <summary>His rise's and his eating's lengths as authored (s): played over wakers.riseSeconds and wakers.eatSeconds.</summary>
    public const double WakerRiseClipSeconds = 8, WakerEatClipSeconds = 4;

    /// <summary>How long he takes to come off his run onto the train he's caught (s): moved and bowed onto it over this.</summary>
    const double WakerReachIn = 0.6;

    /// <summary>The clip he plays and how far into it (for a draw with no blend: not running), by his phase.</summary>
    public static (string Clip, double Time, bool Loop) WakerClip(SpinePhase phase, double seconds, double extra, bool atWalls, WakersTuning t) => phase switch
    {
        SpinePhase.Dormant => ("lie", seconds, true),
        SpinePhase.Telegraph => ("rise", Math.Clamp(seconds / Math.Max(0.1, t.RiseSeconds), 0, 1) * WakerRiseClipSeconds, false),
        SpinePhase.Grab or SpinePhase.Punish when extra < WakerGrabSeconds => ("grab", extra, false),
        SpinePhase.Grab or SpinePhase.Punish when extra < t.EatAfter => ("lift", extra - WakerGrabSeconds, true),
        SpinePhase.Grab or SpinePhase.Punish => ("eat", Wrap(extra - t.EatAfter, t.EatSeconds) * WakerEatClipSeconds / Math.Max(1e-6, t.EatSeconds), true),
        _ when atWalls && seconds % WakerRoarEvery < 4 => ("roar", seconds % WakerRoarEvery, false),
        _ when atWalls => ("stand", seconds % WakerRoarEvery - 4, true),
        _ => ("run", 0, true),
    };

    /// <summary>
    /// The clip one risen beside who ran plays (note 710), by his phase and the seconds in it: up out of the land over
    /// wakers.pickRiseSeconds; stood (no one left to take: the roar and the stand) or striding over at wakers.pickSpeed; down on
    /// his knees for them, then lifting them; eating them.
    /// </summary>
    public static (string Clip, double Time, bool Loop) WakerPickerClip(SpinePhase phase, double seconds, bool hasQuarry, WakersTuning t) => phase switch
    {
        SpinePhase.Dormant => ("lie", seconds, true),
        SpinePhase.Telegraph => ("rise", Math.Clamp(seconds / Math.Max(0.1, t.PickRiseSeconds), 0, 1) * WakerRiseClipSeconds, false),
        SpinePhase.Grab when seconds < WakerGrabSeconds => ("grab", seconds, false),
        SpinePhase.Grab => ("lift", seconds - WakerGrabSeconds, true),
        SpinePhase.Punish => ("eat", Wrap(seconds, t.PickEatSeconds) * WakerEatClipSeconds / Math.Max(1e-6, t.PickEatSeconds), true),
        _ when !hasQuarry && seconds % WakerRoarEvery < 4 => ("roar", seconds % WakerRoarEvery, false),
        _ when !hasQuarry => ("stand", seconds % WakerRoarEvery - 4, true),
        _ => ("walk", Wrap(seconds * t.PickSpeed / WakerGait.CycleMetres(t.PickSpeed), 1) * WakerGait.WalkCycleSeconds, true),
    };

    static double Wrap(double x, double span) => span <= 0 ? 0 : x - Math.Floor(x / span) * span;

    /// <summary>How far under the land he's drawn <paramref name="k"/> (0..1) through his rise (m): all of him at first, out by halfway.</summary>
    public static double WakerSink(double k)
    {
        double s = Math.Clamp(k / 0.45, 0, 1);
        return WakerLieDepth * (1 - s * s * (3 - 2 * s));
    }

    bool DrawWaker(MeshBuilder mesh, in Matrix4x4 model, SpinePhase phase, double t, double extra)
    {
        var tune = WakersTuning;
        var m = _models["waker"];
        var scale = Matrix4x4.CreateScale(WakerGait.Scale);
        if (WakerIn.Picker)
            return DrawWakerPicker(mesh, m, model, phase, t, extra >= 0, tune);
        var (clip, time, loop) = WakerClip(phase, t, extra, WakerIn.AtWalls, tune);
        if (clip == "run")
        {
            // His walk and his run, blended by his speed, each at the phase his feet are in.
            if (!m.Model.Clips.TryGetValue("walk", out var walk) || !m.Model.Clips.TryGetValue("run", out var run))
                return false;
            double phaseOf = WakerGait.Phase(t, tune);
            _skinner.Evaluate(m.Model, walk, phaseOf * walk.Duration, run, phaseOf * run.Duration,
                (float)WakerGait.RunWeight(WakerGait.Speed(t, tune)), true, m.Pose);
            return DrawPosed(mesh, m, "run", scale * model, 679);
        }
        if (!m.Model.Clips.TryGetValue(clip, out var c))
            return false;
        _skinner.Evaluate(m.Model, c, time, loop, m.Pose);
        var at = scale * model;
        if (phase == SpinePhase.Telegraph || phase == SpinePhase.Dormant)
            at = scale * Matrix4x4.CreateTranslation(0, -(float)(phase == SpinePhase.Dormant ? WakerLieDepth : WakerSink(t / Math.Max(0.1, tune.RiseSeconds))), 0) * model;
        else if (WakerIn.Mouth is { } mouth && Matrix4x4.Invert(model, out var inv))
        {
            // Off his run onto the car he's caught, then on the car's end wherever the lift has it.
            double k = Math.Clamp(extra / WakerReachIn, 0, 1);
            at = WakerReach(m, Vector3.Transform(mouth, inv), (float)(k * k * (3 - 2 * k)), model);
        }
        return DrawPosed(mesh, m, clip, at, 679);
    }

    /// <summary>
    /// One risen beside who ran (note 710), stood loose where the sim has his feet: sunk while he rises, and from the grab on,
    /// bowed so his mouth comes down onto the one in his hand as the lift brings them up (all the way by the eating).
    /// </summary>
    bool DrawWakerPicker(MeshBuilder mesh, Entry m, in Matrix4x4 model, SpinePhase phase, double t, bool hasQuarry, WakersTuning tune)
    {
        var scale = Matrix4x4.CreateScale(WakerGait.Scale);
        var (clip, time, loop) = WakerPickerClip(phase, t, hasQuarry, tune);
        if (!m.Model.Clips.TryGetValue(clip, out var c))
            return false;
        _skinner.Evaluate(m.Model, c, time, loop, m.Pose);
        var at = scale * model;
        if (phase is SpinePhase.Telegraph or SpinePhase.Dormant)
            at = scale * Matrix4x4.CreateTranslation(0, -(float)(phase == SpinePhase.Dormant ? WakerLieDepth : WakerSink(t / Math.Max(0.1, tune.PickRiseSeconds))), 0) * model;
        else if (phase is SpinePhase.Grab or SpinePhase.Punish && WakerIn.Mouth is { } mouth && Matrix4x4.Invert(model, out var inv))
        {
            double k = phase == SpinePhase.Punish ? 1 : Math.Clamp(t / Math.Max(0.1, tune.PickLiftSeconds), 0, 1);
            at = WakerReach(m, Vector3.Transform(mouth, inv), (float)(k * k * (3 - 2 * k)), model);
            // His right hand on the one he's lifting, wherever the lift has them.
            if (phase == SpinePhase.Grab && WakerIn.Hand is { } hand && Matrix4x4.Invert(at, out var toModel))
                ReachArm(m, "r", Vector3.Transform(hand, toModel));
        }
        return DrawPosed(mesh, m, clip, at, 679);
    }

    /// <summary>
    /// His arm on <paramref name="side"/> reached so his hand's at <paramref name="target"/> (his model's frame), as far as it goes:
    /// a two-bone reach, the elbow bent the way the clip had it.
    /// </summary>
    static void ReachArm(Entry m, string side, Vector3 target)
    {
        var sk = m.Model.Skeleton;
        int upper = sk.IndexOf($"upperarm_{side}"), lower = sk.IndexOf($"lowerarm_{side}"), hand = sk.IndexOf($"hand_{side}");
        if (upper < 0 || lower < 0 || hand < 0)
            return;
        var a = m.Pose.World[upper].Translation;
        var b = m.Pose.World[lower].Translation;
        var c = m.Pose.World[hand].Translation;
        float l1 = Vector3.Distance(a, b), l2 = Vector3.Distance(b, c);
        var to = target - a;
        float d = Math.Clamp(to.Length(), MathF.Abs(l1 - l2) + 1e-3f, l1 + l2 - 1e-3f);
        var dir = Vector3.Normalize(to);
        var bend = b - a - dir * Vector3.Dot(b - a, dir);
        var pole = bend.LengthSquared() > 1e-8f ? Vector3.Normalize(bend) : Vector3.Normalize(Vector3.Cross(dir, Vector3.UnitX));
        float x = (l1 * l1 - l2 * l2 + d * d) / (2 * d);
        var elbow = a + dir * x + pole * MathF.Sqrt(MathF.Max(0, l1 * l1 - x * x));
        Skinner.Aim(m.Model, m.Pose, upper, lower, elbow);
        Skinner.Aim(m.Model, m.Pose, lower, hand, a + dir * d);
    }

    /// <summary>
    /// His pose moved along the line (his own −Z) and bowed at the small of his back so his mouth is at <paramref name="target"/>
    /// (his own frame, m), <paramref name="weight"/> of the way there from where the clip has it: the catch fitted to
    /// <see cref="WakerLift"/>'s ramp, whatever height the car he's eating is at. Returns where he's drawn.
    /// </summary>
    Matrix4x4 WakerReach(Entry m, Vector3 target, float weight, in Matrix4x4 model)
    {
        var sk = m.Model.Skeleton;
        int spine = sk.IndexOf("spine_01"), mouth = sk.IndexOf("mouth");
        var scale = Matrix4x4.CreateScale(WakerGait.Scale);
        if (spine < 0 || mouth < 0)
            return scale * model;
        float s = WakerGait.Scale;
        var s0 = m.Pose.World[spine].Translation * s;
        var m0 = m.Pose.World[mouth].Translation * s;
        float r = Vector3.Distance(s0, m0);
        // Along the line far enough that the mouth, swung about the small of his back, reaches it: ahead of his spine.
        float dx = target.X - s0.X, dy = target.Y - s0.Y;
        float flat = MathF.Sqrt(MathF.Max(0, r * r - dx * dx - dy * dy));
        float slide = (target.Z + flat - s0.Z) * weight;
        var to = target - new Vector3(0, 0, slide);
        var want = Vector3.Normalize(to - s0);
        var had = Vector3.Normalize(m0 - s0);
        var dir = Vector3.Normalize(Vector3.Lerp(had, want, weight));
        Skinner.Aim(m.Model, m.Pose, spine, mouth, (s0 + dir * r) / s);
        return scale * Matrix4x4.CreateTranslation(0, 0, slide) * model;
    }

    /// <summary>Where his mouth is in the pose last drawn (camera-relative): the catch's tests, and what the lift feeds.</summary>
    public Vector3? WakerMouth(in Matrix4x4 at) =>
        _models.TryGetValue("waker", out var m) && m.Model.Skeleton.IndexOf("mouth") >= 0 ? Skinner.Socket(m.Model, m.Pose, "mouth", at).Translation : null;

    /// <summary>Where his right hand is in the pose last drawn (camera-relative): what he's lifting (note 710's tests).</summary>
    public Vector3? WakerHand(in Matrix4x4 at) =>
        _models.TryGetValue("waker", out var m) && m.Model.Skeleton.IndexOf("hand_r") >= 0 ? Skinner.Socket(m.Model, m.Pose, "hand_r", at).Translation : null;

    /// <summary>The pose already set on <paramref name="m"/>, drawn (its distance copy far off).</summary>
    bool DrawPosed(MeshBuilder mesh, Entry m, string clip, in Matrix4x4 at, float seed)
    {
        _wakerDrawn = at;
        float lod = m.LodFrom;
        var drawn = m.Lod is { } far && lod > 0 && at.Translation.LengthSquared() > lod * lod ? far : m;
        Emit(mesh, drawn, clip, at, 0, 1, seed, skin: m.Pose.Skin);
        return true;
    }

    Matrix4x4 _wakerDrawn;

    /// <summary>Where the last Waker drawn was placed (camera-relative, scaled): with <see cref="WakerMouth"/>, where his mouth went.</summary>
    public Matrix4x4 WakerDrawn => _wakerDrawn;
}
