using System.Numerics;
using Ballast;
using Ballast.Assets;
using Ballast.Render;
using DarkTerritory.Sim.Enemies;

namespace DarkTerritory.Game.Art;

/// <summary>What a crewmate is doing, for which clip their model plays.</summary>
public enum CrewPose { Idle, Walk, Run, Climb, Shovel, Crouch, Dead }

/// <summary>
/// The crew and the creatures as skinned models (content/art/models/*.glb, built by tools/blender/build.sh), posed
/// on the CPU and added to the frame's triangle soup where GreyboxScene drew its boxes. Each model occupies the place
/// its greybox stand-in did (same origin, same footprint), so gameplay reads the same; what changes is that a thing is
/// now recognisable by its outline in the fog (GDD §26.1, §29) and moves the way §31 asks: the crew heavy and a bit
/// stiff, the monsters still, then abrupt.
/// </summary>
/// <remarks>
/// Every method returns false when its model isn't there (not built yet, or a clip missing), and the caller draws the
/// greybox instead. Clips play at 30 fps, stepped (no blending between frames): the era's look (GDD §25). Nothing here
/// reads a clock or a random number: the pose is a pure function of what's passed in.
/// </remarks>
public sealed class CreatureArt
{
    public const string Folder = "art/models";
    /// <summary>The models this draws, by file name (content/art/models/&lt;name&gt;.glb).</summary>
    public static readonly string[] Names = ["crew", "cinder_hound", "sleeper", "clinger", "hollow", "switchman", "soot_child", "dragger"];

    /// <summary>Wear shown over each model's textures (the shader's grime): crew middling, monsters by how they're made.</summary>
    static readonly Dictionary<string, float> WearOf = new()
    {
        ["crew"] = 0.5f,
        ["cinder_hound"] = 0.45f,
        ["sleeper"] = 0.6f,
        ["clinger"] = 0.4f,
        ["hollow"] = 0.6f,
        ["switchman"] = 0.55f,
        ["soot_child"] = 0.5f,
        ["dragger"] = 0.2f,
    };

    sealed class Entry(Model model, MaterialLook[] looks)
    {
        public Model Model { get; } = model;
        public MaterialLook[] Looks { get; } = looks;
        public MaterialLook[] Scratch { get; } = new MaterialLook[looks.Length];
        public Pose Pose { get; } = new(model.Skeleton.Count);
    }

    readonly Dictionary<string, Entry> _models = new();
    readonly Skinner _skinner = new();
    readonly float _texels;

    /// <param name="contentRoot">The content folder; by default the one <paramref name="look"/>'s textures came from.</param>
    public CreatureArt(Look look, string? contentRoot = null)
    {
        Look = look;
        _texels = look.Tuning.TexelsPerMetre;
        contentRoot ??= look.TextureRoot is { } t ? Path.GetDirectoryName(Path.GetDirectoryName(t)) : null;
        contentRoot ??= DataFile.FindContentRoot();
        ContentRoot = contentRoot!;
        foreach (var name in Names)
        {
            var path = Path.Combine(ContentRoot, Folder, name + ".glb");
            if (!File.Exists(path))
                continue;
            var model = ModelLoader.Load(path);
            _models[name] = new Entry(model, [.. model.Materials.Select(m => Resolve(m, WearOf.GetValueOrDefault(name, 0.5f)))]);
        }
    }

    public Look Look { get; }
    public string ContentRoot { get; }

    /// <summary>True when every model is there.</summary>
    public bool Loaded => Names.All(_models.ContainsKey);

    /// <summary>A loaded model by name, or null.</summary>
    public Model? Get(string name) => _models.TryGetValue(name, out var m) ? m.Model : null;

    /// <summary>A material's texture layer when the look has it; its flat colour (and its glow, if it has one) when not.</summary>
    MaterialLook Resolve(ModelMaterial m, float wear)
    {
        int layer = string.IsNullOrEmpty(m.Texture) ? -1 : Look.Layer(m.Texture);
        return layer >= 0
            ? new MaterialLook(layer, m.Tint, m.Emissive, m.Shine, wear)
            : new MaterialLook(-1, m.BaseColour, Math.Max(m.Emissive, m.Glow), m.Shine, wear);
    }

    /// <summary>A basis like GreyboxScene.DrawEnemy's: axes as rows (model +X right, +Y up, +Z back), origin camera-relative.</summary>
    public static Matrix4x4 Basis(Vector3 origin, Vector3 right, Vector3 up, Vector3 back) => new(
        right.X, right.Y, right.Z, 0,
        up.X, up.Y, up.Z, 0,
        back.X, back.Y, back.Z, 0,
        origin.X, origin.Y, origin.Z, 1);

    /// <summary>
    /// Poses and draws one model. <paramref name="glow"/> scales its lights (lamps, eyes, embers); <paramref name="seed"/>
    /// moves its grime so two of the same thing don't wear alike. Returns false when the model or clip isn't there.
    /// </summary>
    public bool Draw(MeshBuilder mesh, string name, string clip, double time, bool loop, in Matrix4x4 at, int variant = 0,
        float glow = 1, float seed = 0, Func<ModelMaterial, MaterialLook, MaterialLook>? adjust = null)
    {
        if (!_models.TryGetValue(name, out var m) || !m.Model.Clips.TryGetValue(clip, out var c))
            return false;
        _skinner.Evaluate(m.Model, c, time, loop, m.Pose);
        Emit(mesh, m, clip, at, variant, glow, seed, adjust);
        return true;
    }

    /// <summary>Draws a model in the pose it was last given.</summary>
    void Emit(MeshBuilder mesh, Entry m, string? clip, in Matrix4x4 at, int variant, float glow, float seed,
        Func<ModelMaterial, MaterialLook, MaterialLook>? adjust = null)
    {
        var mats = m.Model.Materials;
        for (int i = 0; i < mats.Length; i++)
        {
            var l = m.Looks[i];
            if (mats[i].Emissive > 0 || mats[i].Glow > 0)
                l = l with { Colour = l.Colour * glow };
            m.Scratch[i] = adjust is null ? l : adjust(mats[i], l);
        }
        var seedOffset = new Vector3(MathF.Sin(seed * 12.9898f), MathF.Sin(seed * 78.233f), MathF.Sin(seed * 37.719f)) * 97;
        _skinner.Emit(mesh, m.Model, m.Pose, at, m.Scratch, new EmitSettings(variant % Math.Max(1, m.Model.VariantCount), clip, _texels, seedOffset));
    }

    /// <summary>Where a bone of the model last drawn by name is, in camera-relative space (the Switchman's lantern).</summary>
    Vector3 BoneAt(string name, string bone, in Matrix4x4 at) =>
        _models.TryGetValue(name, out var m) && m.Model.Skeleton.IndexOf(bone) >= 0
            ? Skinner.Socket(m.Model, m.Pose, bone, at).Translation
            : at.Translation;

    // ----------------------------------------------------------------------------------------------------------------
    // The crew

    static string ClipOf(CrewPose pose) => pose switch
    {
        CrewPose.Walk => "walk",
        CrewPose.Run => "run",
        CrewPose.Climb => "climb",
        CrewPose.Shovel => "shovel",
        CrewPose.Crouch => "crouch_idle",
        CrewPose.Dead => "dead",
        _ => "idle",
    };

    /// <summary>
    /// A crewmate at <paramref name="model"/> (feet at its origin, facing its −Z, as GreyboxScene.DrawCrewmate places the
    /// box figure). <paramref name="variant"/> picks cap or helmet, with or without a scarf (variant % 4), so a crew of
    /// eight isn't eight twins; <paramref name="time"/> is any running clock for the clip.
    /// </summary>
    /// <param name="left">A headset player's left hand (T47), in the model's space (from the feet: x right, y up, z behind),
    /// or null to leave the arm to the clip; likewise <paramref name="right"/>. The arm reaches it by two-bone IK on the
    /// model's own shoulder and arm lengths, the elbow bent toward <paramref name="leftPole"/>/<paramref name="rightPole"/>.</param>
    public bool Crewmate(MeshBuilder mesh, in Matrix4x4 model, CrewPose pose, double time, int variant,
        Vector3? left = null, Vector3? right = null, Vector3 leftPole = default, Vector3 rightPole = default)
    {
        // Each crewmate breathes and steps on their own beat: a fixed offset by variant, not a random one.
        double offset = (variant & 7) * 0.41;
        string clip = ClipOf(pose);
        if (left is null && right is null)
            return Draw(mesh, "crew", clip, time + offset, pose != CrewPose.Dead, model, variant, seed: variant);
        if (!_models.TryGetValue("crew", out var m) || !m.Model.Clips.TryGetValue(clip, out var c))
            return false;
        _skinner.Evaluate(m.Model, c, time + offset, pose != CrewPose.Dead, m.Pose);
        if (left is { } l)
            Reach(m, "l", l, leftPole);
        if (right is { } r)
            Reach(m, "r", r, rightPole);
        Emit(mesh, m, clip, model, variant, 1, variant);
        return true;
    }

    /// <summary>One arm of the posed model to a hand position (model space) by two-bone IK, the elbow toward the pole.</summary>
    static void Reach(Entry m, string side, Vector3 target, Vector3 pole)
    {
        var sk = m.Model.Skeleton;
        int upper = sk.IndexOf("upperarm_" + side), lower = sk.IndexOf("lowerarm_" + side), hand = sk.IndexOf("hand_" + side);
        if (upper < 0 || lower < 0 || hand < 0)
            return;
        var shoulder = m.Pose.World[upper].Translation;
        float a = Vector3.Distance(shoulder, m.Pose.World[lower].Translation);
        float b = Vector3.Distance(m.Pose.World[lower].Translation, m.Pose.World[hand].Translation);
        var to = target - shoulder;
        float d = to.Length();
        if (d < 1e-4f || a < 1e-4f || b < 1e-4f)
            return;
        var dir = to / d;
        float reach = Math.Clamp(d, MathF.Abs(a - b) + 1e-3f, (a + b) * 0.999f);
        // Along the reach, x from the shoulder; then out towards the pole by h (law of cosines), as Arms.Solve.
        float x = (a * a - b * b + reach * reach) / (2 * reach);
        float h = MathF.Sqrt(MathF.Max(0, a * a - x * x));
        var bend = pole - dir * Vector3.Dot(pole, dir);
        bend = bend.LengthSquared() < 1e-8f ? Vector3.Normalize(Vector3.Cross(dir, Vector3.UnitZ) + new Vector3(0, -1e-3f, 0)) : Vector3.Normalize(bend);
        var elbow = shoulder + dir * x + bend * h;
        Skinner.Aim(m.Model, m.Pose, upper, lower, elbow);
        Skinner.Aim(m.Model, m.Pose, lower, hand, shoulder + dir * reach);
    }

    /// <summary>How many joints a ragdoll has (Sim.Physics.Bodies' skeleton), in the order <see cref="Corpse"/> reads them.</summary>
    public const int RagdollJoints = 11;

    // Which bone reaches which ragdoll joint: (bone, the bone whose head it swings, the joint that head goes to).
    static readonly (string Bone, string Toward, int Joint)[] Limbs =
    [
        ("neck", "head_hat", 0),
        ("upperarm_l", "lowerarm_l", 3), ("lowerarm_l", "hand_l", 4),
        ("upperarm_r", "lowerarm_r", 5), ("lowerarm_r", "hand_r", 6),
        ("thigh_l", "calf_l", 7), ("calf_l", "foot_l", 8),
        ("thigh_r", "calf_r", 9), ("calf_r", "foot_r", 10),
    ];

    /// <summary>
    /// A dead crewmate as their body lies (spec C.1: it persists where they fell, to be carried back): the crew model
    /// fitted to the ragdoll's joints (camera-relative: head, chest, pelvis, left elbow and hand, right elbow and hand,
    /// left knee and foot, right knee and foot). The torso is turned to the pelvis→chest line and the line across the
    /// shoulders and hips, then each limb bone swung onto its joint, parents first. <paramref name="variant"/> is whose
    /// body it is, so it wears what they wore. Their lamp is down to an ember: dead from a distance, but findable.
    /// </summary>
    public bool Corpse(MeshBuilder mesh, ReadOnlySpan<Vector3> joints, int variant)
    {
        if (joints.Length < RagdollJoints || !_models.TryGetValue("crew", out var m))
            return false;
        var model = m.Model;
        var sk = model.Skeleton;
        int pelvis = sk.IndexOf("pelvis"), neck = sk.IndexOf("neck"), armL = sk.IndexOf("upperarm_l"), armR = sk.IndexOf("upperarm_r");
        if (pelvis < 0 || neck < 0 || armL < 0 || armR < 0)
            return false;
        _skinner.Evaluate(model, null, 0, false, m.Pose);

        // The torso: the bind pose's pelvis-up and left-to-right, onto the body's.
        var hip = m.Pose.World[pelvis].Translation;
        var bindUp = Vector3.Normalize(m.Pose.World[neck].Translation - hip);
        var bindRight = Vector3.Normalize(m.Pose.World[armR].Translation - m.Pose.World[armL].Translation);
        var up = joints[1] - joints[2];
        if (up.LengthSquared() < 1e-8f)
            up = Vector3.UnitY;
        up = Vector3.Normalize(up);
        var across = joints[5] - joints[3] + (joints[9] - joints[7]);
        across -= up * Vector3.Dot(across, up);
        if (across.LengthSquared() < 1e-8f)
        {
            // Shoulders and hips folded onto the spine: any square to it will do.
            across = Vector3.Cross(up, MathF.Abs(up.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitZ);
        }
        var right = Vector3.Normalize(across);
        var turn = Matrix4x4.Transpose(Frame(bindRight, bindUp)) * Frame(right, up);
        Skinner.Place(model, m.Pose, Matrix4x4.CreateTranslation(-hip) * turn * Matrix4x4.CreateTranslation(joints[2]));

        foreach (var (bone, toward, joint) in Limbs)
        {
            int b = sk.IndexOf(bone), t = sk.IndexOf(toward);
            if (b >= 0 && t >= 0)
                Skinner.Aim(model, m.Pose, b, t, joints[joint]);
        }
        Emit(mesh, m, "dead", Matrix4x4.Identity, variant, glow: 0.2f, seed: variant);
        return true;

        // Rows right, up, back: takes model +X, +Y, +Z onto them (right squared to up first).
        static Matrix4x4 Frame(Vector3 r, Vector3 u)
        {
            r = Vector3.Normalize(r - u * Vector3.Dot(r, u));
            var b = Vector3.Cross(r, u);
            return new Matrix4x4(r.X, r.Y, r.Z, 0, u.X, u.Y, u.Z, 0, b.X, b.Y, b.Z, 0, 0, 0, 0, 1);
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // The enemies

    /// <summary>
    /// An enemy at its basis (GreyboxScene.DrawEnemy's o, r, u, b), its clip chosen by kind and spine phase.
    /// <list type="bullet">
    /// <item>Cinder hound: feet at the origin (on the ballast, or the roof when boarded: no extra lift). Dormant and alert
    /// it prowls; telegraphing and committing it runs (the sim has it matching the train's speed, so a standing crouch
    /// would slide); boarded (punish) it crouches, lunges, crouches. Embers pulse hotter as it closes.</item>
    /// <item>Sleepers: six ties 2.6 m apart along −Z from the origin, dormant, writhing once telegraphed, lifting when the
    /// engine's on them.</item>
    /// <item>Clinger: on the hull at the origin, bulging to +X (turn the basis for a clinger on the −X side);
    /// <paramref name="extra"/> is drill progress 0..1: the drill works faster, and its tip heats, as it gets through.</item>
    /// <item>Hollow: the origin is the cab's centre, so it stands 1.35 m below it on the deck. Telegraphing it hasn't come
    /// down yet: soot falls from the stack. Hunting, it stands unnaturally still and reaches.</item>
    /// <item>Switchman: feet at the origin, lantern swinging while it waits; it flees when broken off.</item>
    /// <item>Climber: feet at the origin; in the cab (punish, <paramref name="extra"/> below 0) the origin is the cab's centre.</item>
    /// <item>Ferryman: feet at the origin, lantern swung hard while it waves; aboard (punish) the origin is the cab's centre.</item>
    /// <item>Soot children: three huddled as GreyboxScene places them, facing the basis's −X (the car when they're on
    /// its +X side; turn the basis for the other); they turn their heads up at the doors while they call.</item>
    /// </list>
    /// </summary>
    public bool Enemy(MeshBuilder mesh, in Matrix4x4 model, EnemyKind kind, SpinePhase phase, double phaseSeconds, double extra)
    {
        double t = phaseSeconds;
        float pulse = (float)(0.5 + 0.5 * Math.Sin(t * 9));
        switch (kind)
        {
            case EnemyKind.CinderHound:
                {
                    // The heat they hunt by is what you see of them at night (greybox: embers along the flanks).
                    float glow = phase switch
                    {
                        SpinePhase.Punish => 0.9f + 0.7f * pulse,
                        SpinePhase.Commit => 1.25f + 0.25f * (float)Math.Sin(t * 5),
                        SpinePhase.Telegraph => 1.05f,
                        _ => 0.75f,
                    };
                    string clip;
                    double ct = t;
                    bool loop = true;
                    if (phase == SpinePhase.Punish)
                    {
                        // Onto the roof in one leap, then the mauling: crouch (a held beat), lunge, crouch.
                        if (t < 0.6)
                            (clip, loop) = ("lunge", false);
                        else
                        {
                            double c = (t - 0.6) % 2.0;
                            (clip, ct, loop) = c < 1.1 ? ("crouch", c, true) : ("lunge", c - 1.1, false);
                        }
                    }
                    else
                        clip = phase is SpinePhase.Telegraph or SpinePhase.Commit or SpinePhase.BreakOff ? "run" : "prowl";
                    return Draw(mesh, "cinder_hound", clip, ct, loop, model, glow: glow, seed: (float)extra);
                }
            case EnemyKind.Sleepers:
                {
                    if (!_models.ContainsKey("sleeper"))
                        return false;
                    string clip = phase switch
                    {
                        SpinePhase.Dormant or SpinePhase.Alert => "dormant",
                        SpinePhase.Telegraph => "writhe",
                        _ => "lift",
                    };
                    for (int i = 0; i < 6; i++)
                    {
                        // Not quite in step and not quite square to the rail: ties laid by something that isn't a gang.
                        float yaw = (float)(Math.Sin(i * 2.3) * 0.035);
                        float dx = (float)(Math.Sin(i * 1.7) * 0.06);
                        var at = Matrix4x4.CreateRotationY(yaw) * Matrix4x4.CreateTranslation(dx, 0, -i * 2.6f) * model;
                        Draw(mesh, "sleeper", clip, t + i * 0.37, clip != "lift", at, seed: i);
                    }
                    return true;
                }
            case EnemyKind.Clinger:
                {
                    double drilled = Math.Clamp(extra, 0, 1);
                    // The drill point heats the hull as it works through: the tell you see, as the scraping is the one you
                    // hear (App. A.4).
                    var hot = Palette.FurnaceOrange * (phase == SpinePhase.Punish ? 0.7f + 0.5f * pulse : 0.15f + 0.9f * (float)drilled);
                    MaterialLook Heat(ModelMaterial mat, MaterialLook l) => mat.Texture == "hot" ? l with { Colour = hot, Layer = -1 } : l;
                    return phase switch
                    {
                        SpinePhase.Dormant or SpinePhase.Alert or SpinePhase.BreakOff => Draw(mesh, "clinger", "cling", t, true, model, adjust: Heat),
                        SpinePhase.Punish => Draw(mesh, "clinger", "punish", t, false, model, adjust: Heat),
                        _ => Draw(mesh, "clinger", "drill", t * (phase == SpinePhase.Commit ? 3.0 : 0.7 + 1.3 * drilled), true, model, adjust: Heat),
                    };
                }
            case EnemyKind.Hollow:
                {
                    if (!_models.ContainsKey("hollow"))
                        return false;
                    if (phase == SpinePhase.Telegraph)
                    {
                        // Not down yet: the fire gutters and soot falls into the cab from the stack.
                        var (r, u, b) = (new Vector3(model.M11, model.M12, model.M13), new Vector3(model.M21, model.M22, model.M23), new Vector3(model.M31, model.M32, model.M33));
                        for (int i = 0; i < 4; i++)
                        {
                            var p = model.Translation + r * (float)(0.3 * Math.Sin(i * 1.7)) + u * (float)(1.0 - (t * 2 + i * 0.4) % 1.6) + b * (float)(0.3 * Math.Cos(i * 2.3));
                            mesh.Box(p, r, u, b, new Vector3(0.04f, 0.2f, 0.04f), Palette.SootBlack);
                        }
                        return true;
                    }
                    var at = Matrix4x4.CreateTranslation(0, -1.35f, 0) * model;
                    // Still, still, still, then it reaches: every few seconds, too fast.
                    double cyc = t % 3.4;
                    return cyc < 2.3
                        ? Draw(mesh, "hollow", "idle", t, true, at)
                        : Draw(mesh, "hollow", "reach", cyc - 2.3, false, at);
                }
            case EnemyKind.Switchman:
                {
                    bool fleeing = phase == SpinePhase.BreakOff;
                    if (!Draw(mesh, "switchman", fleeing ? "flee" : "wait", t, true, model))
                        return false;
                    // Its lantern is the only light on it (App. A.7's "distant figure at the switch").
                    mesh.PointLights.Add(new PointLight(BoneAt("switchman", "lantern", model), Palette.LampAmber * 1.1f, 6f));
                    return true;
                }
            case EnemyKind.SootChildren:
                {
                    if (!_models.ContainsKey("soot_child"))
                        return false;
                    bool calling = phase is SpinePhase.Telegraph or SpinePhase.Commit or SpinePhase.Punish;
                    for (int i = 0; i < 3; i++)
                    {
                        float x = (i - 1) * 0.45f, z = i % 2 * 0.35f;
                        // Facing the car (−X), each a little off, the middle one turned furthest.
                        float yaw = MathF.PI / 2 + (i - 1) * 0.28f + (i == 1 ? 0.1f : 0);
                        var at = Matrix4x4.CreateRotationY(yaw) * Matrix4x4.CreateTranslation(x, 0, z) * model;
                        // They look up one after another, not together.
                        Draw(mesh, "soot_child", calling ? "turn" : "huddle", calling ? Math.Max(0, t - i * 0.3) : t + i * 1.3, !calling, at, seed: i);
                    }
                    return true;
                }
            case EnemyKind.Dragger:
                {
                    if (!_models.ContainsKey("dragger"))
                        return false;
                    // Under the lip until it reaches (App. A.4): nothing to see. Then one limb up over the eave and down
                    // on the roof; grabbing, two, further in and gripping, out of step.
                    if (phase is not (SpinePhase.Telegraph or SpinePhase.Punish))
                        return true;
                    if (phase == SpinePhase.Telegraph)
                        return Draw(mesh, "dragger", "reach", t, false, model);
                    for (int i = 0; i < 2; i++)
                    {
                        var at = Matrix4x4.CreateTranslation(0.12f, 0, (i - 0.5f) * 0.45f) * model;
                        Draw(mesh, "dragger", "grip", t + i * 0.37, true, at, seed: i);
                    }
                    return true;
                }
            case EnemyKind.Lamplighter:
                {
                    // Tall, thin, blacker than the dark (App. A.6): the Hollow's figure drawn out taller still, standing
                    // still out at the lineside, reaching once it's coming for the lamp. Nothing of it shows at night but
                    // the eyes, and those only when they've caught the lamp: the tell.
                    if (!_models.ContainsKey("hollow"))
                        return false;
                    bool eyes = phase is SpinePhase.Telegraph or SpinePhase.Commit or SpinePhase.Punish;
                    var at = Matrix4x4.CreateScale(0.85f, 1.18f, 0.85f) * model;
                    Draw(mesh, "hollow", eyes ? "reach" : "idle", t, !eyes, at, seed: 7);
                    if (eyes)
                    {
                        var head = BoneAt("hollow", "head", at);
                        var right = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitX, model));
                        var up = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitY, model));
                        var back = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, model));
                        var shine = Palette.SignalGreen * 1.6f;
                        mesh.Emissive = 1;
                        foreach (float side in new[] { -1f, 1f })
                            mesh.Box(head + right * (side * 0.045f) - back * 0.1f + up * 0.02f, right, up, back, new Vector3(0.022f, 0.016f, 0.01f), shine);
                        mesh.Emissive = 0;
                    }
                    return true;
                }
            case EnemyKind.Deadman:
                {
                    // Outside, tracking the cab, it isn't seen: its tell is the cab (the lamp dims, the controls click).
                    // At the controls it's a crewman, or was: stood at the backhead, still, one hand on the regulator. The
                    // origin is the cab's centre, as the Hollow's is.
                    if (phase is not (SpinePhase.Commit or SpinePhase.Punish))
                        return true;
                    var at = Matrix4x4.CreateTranslation(0.35f, -1.35f, -0.4f) * model;
                    return Draw(mesh, "crew", "idle", t * 0.2, true, at, variant: 5, seed: 11, adjust: (_, l) => l with { Colour = l.Colour * 0.45f });
                }
            case EnemyKind.Ferryman:
                {
                    // A railwayman too tall for his coat, lantern raised (App. A.2): the Switchman's figure drawn out taller,
                    // stood on the line swinging the lantern hard while it waves the train down, lowering it and walking
                    // when it steps aside. Aboard (the origin is the cab's centre, as the Hollow's), stood on the deck.
                    bool aboard = phase == SpinePhase.Punish;
                    var at = Matrix4x4.CreateScale(1.08f, 1.16f, 1.08f) * (aboard ? Matrix4x4.CreateTranslation(0, -1.35f, 0) : Matrix4x4.Identity) * model;
                    bool waving = phase is SpinePhase.Dormant or SpinePhase.Telegraph;
                    bool lit = phase is SpinePhase.Telegraph or SpinePhase.Commit or SpinePhase.Punish;
                    // The lantern burns brighter than the Switchman's: it's meant to be seen from far up the line.
                    if (!Draw(mesh, "switchman", waving || aboard ? "wait" : "flee", waving ? t * 2.2 : t, true, at, glow: lit ? 2.2f : 0.3f, seed: 3))
                        return false;
                    if (lit)
                        mesh.PointLights.Add(new PointLight(BoneAt("switchman", "lantern", at), Palette.LampAmber * 2.2f, 12f));
                    return true;
                }
            case EnemyKind.Stoker:
                // In the firebox: never seen, only its work (the gauge, the wrong glow the scene gives the fire, the hiss).
                return true;
            case EnemyKind.Climber:
                {
                    // A crewman gone wrong (App. A.4): drawn out thin, soot-black, running bent double alongside; climbing at
                    // the gap (the scrabbling: fast, and facing into the train); walking the roofs; crouched in a car.
                    // Bent forward from the feet running and walking: nothing upright about it.
                    float hunch = phase is SpinePhase.Telegraph or SpinePhase.Punish ? 0 : -0.42f;
                    var at = Matrix4x4.CreateScale(0.86f, 1.08f, 0.86f) * Matrix4x4.CreateRotationX(hunch)
                        * (phase == SpinePhase.Punish && extra < 0 ? Matrix4x4.CreateTranslation(0, -1.35f, 0) : Matrix4x4.Identity) * model;
                    var (clip, speed) = phase switch
                    {
                        SpinePhase.Telegraph => ("climb", 2.4),
                        SpinePhase.Commit => ("walk", 1.3),
                        SpinePhase.Punish => ("crouch_idle", 1.0),
                        _ => ("run", 1.2),
                    };
                    return Draw(mesh, "crew", clip, t * speed, true, at, variant: 3, seed: 17, adjust: (_, l) => l with { Colour = l.Colour * new Vector3(0.28f, 0.26f, 0.25f) });
                }
            case EnemyKind.LongWhistle:
                // "Never visible; operates from ahead on the line" (App. A.2): the horn is all of it.
                return true;
            case EnemyKind.Rattle:
                // "Pure audio tell" (App. A.5): in the coupling, never seen. Drawn, as nothing.
                return true;
        }
        return false;
    }

    /// <summary>
    /// <see cref="Enemy(MeshBuilder, in Matrix4x4, EnemyKind, SpinePhase, double, double)"/> for a live enemy, turning
    /// the basis for which side of the car or the line it's on: a Clinger on a car's −X side bulges to −X, Soot children
    /// face the car they're calling at, and the Switchman turns to face the train coming up the line.
    /// </summary>
    public bool Enemy(MeshBuilder mesh, in Matrix4x4 model, Enemy e)
    {
        var m = model;
        switch (e.Kind)
        {
            case EnemyKind.Dragger when e.Local.X > 0:
            case EnemyKind.Clinger when e.Local.X < 0:
            case EnemyKind.SootChildren when e.Lateral < 0:
                m = Matrix4x4.CreateRotationY(MathF.PI) * model;
                break;
            case EnemyKind.Climber when e.Phase == SpinePhase.Telegraph:
                // At the gap, facing in at the couplers.
                m = Matrix4x4.CreateRotationY(e.Local.X > 0 ? MathF.PI / 2 : -MathF.PI / 2) * model;
                break;
            case EnemyKind.Climber when e.Phase == SpinePhase.Punish:
                // In the cab (the origin is its centre, as the Hollow's): stood 1.35 m below it. Flagged through extra.
                return Enemy(mesh, m, e.Kind, e.Phase, e.PhaseSeconds, e.Attached == 0 ? -1 : 0);
            case EnemyKind.Ferryman when !((Sim.Enemies.Ferryman)e).Aboard:
                // Facing down the line at the train coming.
                m = Matrix4x4.CreateRotationY(MathF.PI) * model;
                break;
            case EnemyKind.Switchman:
                // Face back down the line at the train, turned in towards the track.
                m = Matrix4x4.CreateRotationY(MathF.PI - Math.Sign(e.Lateral) * 0.6f) * model;
                break;
        }
        return Enemy(mesh, m, e.Kind, e.Phase, e.PhaseSeconds, e.Extra);
    }
}
