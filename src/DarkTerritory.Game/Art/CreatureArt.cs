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
/// on the CPU (the bones: clips, IK, ragdolls) and skinned on the GPU (each model's bind pose an asset, drawn with the
/// pose's palette: MeshBuilder.Skinned), where GreyboxScene drew its boxes. Each model occupies the place
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
    /// <summary>The Drift's mat, drawn from a car's roof: past this half-width (m) it's on the ground, this far down.</summary>
    const float CarHalfWidth = 1.6f, RoofDrop = 3.6f;
    /// <summary>The models this draws, by file name (content/art/models/&lt;name&gt;.glb).</summary>
    public static readonly string[] Names = ["crew", "cinder_hound", "sleeper", "clinger", "hollow", "switchman", "soot_child", "dragger", "husk", "weight"];

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
        ["husk"] = 0.7f,
        ["weight"] = 0.3f,
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

    /// <summary>
    /// Draws a model in the pose it was last given: its bind pose, cooked once per variant and look (<see cref="Bound"/>),
    /// posed on the GPU by the pose's palette. <paramref name="glow"/> goes to the instance (the shader scales the lights
    /// by it), so a pulsing ember doesn't need an asset a frame.
    /// </summary>
    void Emit(MeshBuilder mesh, Entry m, string? clip, in Matrix4x4 at, int variant, float glow, float seed,
        Func<ModelMaterial, MaterialLook, MaterialLook>? adjust = null)
    {
        var mats = m.Model.Materials;
        for (int i = 0; i < mats.Length; i++)
            m.Scratch[i] = adjust is null ? m.Looks[i] : adjust(mats[i], m.Looks[i]);
        var seedOffset = new Vector3(MathF.Sin(seed * 12.9898f), MathF.Sin(seed * 78.233f), MathF.Sin(seed * 37.719f)) * 97;
        var settings = new EmitSettings(variant % Math.Max(1, m.Model.VariantCount), clip, _texels, seedOffset);
        mesh.Skinned(Bound(m, settings), at, m.Pose.Skin, glow, seedOffset * _texels);
    }

    // The bind-pose assets, by model, the parts drawn and the looks (keyed to 1/128th: a hull heating as it's drilled
    // steps through its heat, it doesn't make an asset a frame). Cleared when it grows past a few hundred; the renderer
    // frees what's dropped.
    readonly Dictionary<(Model Model, ulong Parts, int Looks), (MaterialLook[] Looks, MeshAsset Asset)> _bound = new();
    const int MaxBound = 384;

    MeshAsset Bound(Entry m, in EmitSettings settings)
    {
        ulong parts = 0;
        for (int i = 0; i < m.Model.Parts.Length; i++)
            if (m.Model.Parts[i].DrawnFor(settings.Variant, settings.Clip))
                parts |= 1ul << (i % 64);
        var hash = new HashCode();
        foreach (var l in m.Scratch)
            hash.Add(Quantised(l));
        var key = (m.Model, parts, hash.ToHashCode());
        if (_bound.TryGetValue(key, out var hit) && Same(hit.Looks, m.Scratch))
            return hit.Asset;
        if (_bound.Count >= MaxBound)
            _bound.Clear();
        var asset = Skinner.Bind(m.Model, m.Scratch, settings, m.Model.Name);
        _bound[key] = ([.. m.Scratch], asset);
        return asset;
    }

    static bool Same(MaterialLook[] a, MaterialLook[] b)
    {
        for (int i = 0; i < a.Length; i++)
            if (Quantised(a[i]) != Quantised(b[i]))
                return false;
        return true;
    }

    static MaterialLook Quantised(MaterialLook l)
    {
        static float Q(float v) => MathF.Round(v * 128) / 128;
        return l with { Colour = new Vector3(Q(l.Colour.X), Q(l.Colour.Y), Q(l.Colour.Z)), Emissive = Q(l.Emissive), Shine = Q(l.Shine), Wear = Q(l.Wear) };
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
        var Paint = PaintOf(variant);
        if (left is null && right is null)
            return Draw(mesh, "crew", clip, time + offset, pose != CrewPose.Dead, model, variant, seed: variant, adjust: Paint);
        if (!_models.TryGetValue("crew", out var m) || !m.Model.Clips.TryGetValue(clip, out var c))
            return false;
        _skinner.Evaluate(m.Model, c, time + offset, pose != CrewPose.Dead, m.Pose);
        if (left is { } l)
            Reach(m, "l", l, leftPole);
        if (right is { } r)
            Reach(m, "r", r, rightPole);
        Emit(mesh, m, clip, model, variant, 1, variant, Paint);
        return true;
    }

    /// <summary>
    /// A crewmate's own colour (look.json crewColours, by player id): the flying cap's leather and the scarf, the model's
    /// ".paint" material, tinted, so eight masked heads can be told apart.
    /// </summary>
    Func<ModelMaterial, MaterialLook, MaterialLook> PaintOf(int variant)
    {
        var colour = Look.Tuning.CrewColour(variant);
        return (mm, l) => mm.Name.EndsWith(".paint", StringComparison.Ordinal) ? l with { Colour = l.Colour * colour } : l;
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
        Emit(mesh, m, "dead", Matrix4x4.Identity, variant, glow: 0.2f, seed: variant, adjust: PaintOf(variant));
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
    /// <item>Switchman: feet at the origin, lantern swinging while it waits; it flees when broken off.</item>
    /// <item>Climber: feet at the origin; inside a car (<paramref name="extra"/> below 0) it crouches.</item>
    /// <item>Soot child: one, feet at the origin; <paramref name="extra2"/> 1 is a Soot Child (black eyes), 0 a real child.</item>
    /// <item>Gaunt, Follower, Grumbler: <paramref name="extra2"/> is the anger, the nest, and feral, as the sim keeps them.</item>
    /// <item>Car Hugger, Track Doll, Whistler, Tippy Toesie, Ribbit, Choir ghost: feet (or heap) at the origin.</item>
    /// <item>Fire Flies: the origin is the lamp they swarm.</item>
    /// </list>
    /// </summary>
    public bool Enemy(MeshBuilder mesh, in Matrix4x4 model, EnemyKind kind, SpinePhase phase, double phaseSeconds, double extra, double health = 1,
        bool aboard = false, double extra2 = 0)
    {
        double t = phaseSeconds;
        // The in-car incidents are effects, not creatures with a model (Art/IncidentArt).
        if (kind is EnemyKind.CarFire)
            return IncidentArt.Draw(mesh, model.Translation, Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitX, model)),
                Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitY, model)), Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, model)),
                kind, phase, t, extra, health);
        float pulse = (float)(0.5 + 0.5 * Math.Sin(t * 9));
        switch (kind)
        {
            case EnemyKind.CinderHound:
                {
                    // The heat they hunt by is what you see of them at night (greybox: embers along the flanks).
                    float glow = phase switch
                    {
                        SpinePhase.Grab or SpinePhase.Punish => 0.9f + 0.7f * pulse,
                        SpinePhase.Commit when aboard => 1.1f + 0.3f * pulse,
                        SpinePhase.Commit => 1.25f + 0.25f * (float)Math.Sin(t * 5),
                        SpinePhase.Telegraph => 1.05f,
                        _ => 0.75f,
                    };
                    string clip;
                    double ct = t;
                    bool loop = true;
                    if (aboard || phase is SpinePhase.Grab or SpinePhase.Punish)
                    {
                        // Onto the roof in one leap, then the pack fight: crouch (a held beat), lunge, crouch.
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
                    // One child in the dark, calling (GDD v1.1 A.6). A Soot Child's eyes are black and its hands and feet
                    // blackened (extra2 = 1): the tell, readable from five metres. Pinning someone, it's turned, bent over them.
                    bool soot = extra2 > 0.5;
                    bool drinking = phase is SpinePhase.Grab or SpinePhase.Punish;
                    var at = drinking ? Matrix4x4.CreateRotationX(-0.5f) * model : model;
                    if (!Draw(mesh, "soot_child", drinking || extra > 0.5 ? "turn" : "huddle", t, !drinking, at, seed: soot ? 5 : 2,
                            adjust: soot ? (_, l) => l with { Colour = l.Colour * 0.5f } : null))
                        return false;
                    var head = BoneAt("soot_child", "head", at);
                    var (rr, uu, bb) = Basis(model);
                    foreach (float side in new[] { -1f, 1f })
                        mesh.Box(head + rr * (side * 0.035f) - bb * 0.08f, rr, uu, bb, new Vector3(0.016f, 0.012f, 0.008f), soot ? Palette.SootBlack : Palette.BoardEnamel);
                    return true;
                }
            case EnemyKind.Dragger:
                {
                    if (!_models.ContainsKey("dragger"))
                        return false;
                    // Under the lip until it reaches (App. A.4): nothing to see. Then one limb up over the eave and down
                    // on the roof; grabbing, two, further in and gripping, out of step.
                    if (phase is not (SpinePhase.Telegraph or SpinePhase.Grab or SpinePhase.Punish))
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
            case EnemyKind.Stoker:
                // In the firebox: never seen, only its work (the gauge, the wrong glow the scene gives the fire, the hiss).
                return true;
            case EnemyKind.Climber:
                {
                    // A crewman gone wrong (App. A.4): drawn out thin, soot-black, running bent double alongside; climbing at
                    // the gap (the scrabbling: fast, and facing into the train); walking the roofs; crouched in a car.
                    // Bent forward from the feet running and walking: nothing upright about it.
                    bool inside = extra < 0;
                    float hunch = phase is SpinePhase.Telegraph || inside ? 0 : -0.42f;
                    var at = Matrix4x4.CreateScale(0.86f, 1.08f, 0.86f) * Matrix4x4.CreateRotationX(hunch) * model;
                    var (clip, speed) = phase switch
                    {
                        SpinePhase.Telegraph => ("climb", 2.4),
                        SpinePhase.Grab or SpinePhase.Punish => ("crouch_idle", 2.0),
                        SpinePhase.Commit when inside => ("crouch_idle", 1.0),
                        SpinePhase.Commit => ("walk", 1.3),
                        _ => ("run", 1.2),
                    };
                    return Draw(mesh, "husk", clip, t * speed, true, at, variant: 3, seed: 17, adjust: (_, l) => l with { Colour = l.Colour * 0.8f })
                        || Draw(mesh, "crew", clip, t * speed, true, at, variant: 3, seed: 17, adjust: (_, l) => l with { Colour = l.Colour * new Vector3(0.28f, 0.26f, 0.25f) });
                }
            case EnemyKind.Drift:
                {
                    // Not a creature (App. A.4): the ground come up over the roof and down the sides, a mat of dark limbs spread
                    // round where it's centred, as wide as it's spread (extra). Surging, they reach one way.
                    if (!_models.ContainsKey("dragger"))
                        return false;
                    float r = (float)Math.Clamp(extra, 1, 12);
                    int n = 5 + (int)(r * 1.5);
                    for (int i = 0; i < n; i++)
                    {
                        float a = i * 2.39996f, d = r * MathF.Sqrt((i + 0.5f) / n);
                        float x = MathF.Cos(a) * d, z = MathF.Sin(a) * d;
                        // Past the car's sides it's down on the ballast beside the train, not hanging in the air at the roof.
                        float y = MathF.Abs(x) > CarHalfWidth ? -RoofDrop : -0.1f;
                        var limb = Matrix4x4.CreateScale(1.4f) * Matrix4x4.CreateRotationZ(MathF.PI / 2) * Matrix4x4.CreateRotationY(a)
                            * Matrix4x4.CreateTranslation(x, y, z) * model;
                        Draw(mesh, "dragger", "grip", t * (phase == SpinePhase.Dormant ? 0.2 : 0.9) + i * 0.37, true, limb, seed: 40 + i,
                            adjust: (_, l) => l with { Colour = l.Colour * 0.35f });
                    }
                    return true;
                }
            case EnemyKind.Follower:
                {
                    // A hand-sized lump (GDD v1.1 A.6): on someone's back it twitches; off, it crawls; nesting, a heap of
                    // husk grown over the loot, bigger as the nest builds (extra2).
                    float size = phase == SpinePhase.Punish ? 0.3f + 0.4f * (float)Math.Clamp(extra2, 0, 1) : 0.18f;
                    var at = Matrix4x4.CreateScale(size) * model;
                    double speed = phase == SpinePhase.Telegraph ? 3.0 : 1.0;
                    return Draw(mesh, "husk", "crouch_idle", t * speed, true, at, variant: 6, seed: 23, adjust: (_, l) => l with { Colour = l.Colour * 0.5f })
                        || Draw(mesh, "crew", "crouch_idle", t * speed, true, at, variant: 6, seed: 23, adjust: (_, l) => l with { Colour = l.Colour * new Vector3(0.22f, 0.2f, 0.2f) });
                }
            case EnemyKind.Passenger:
                // One of the crew (App. A.7 BLEND): the crew figure in the look of whoever it copies (extra), walking its loop.
                // In play it's drawn through the crew's own path (GreyboxScene.AsCrewmate), gait and all.
                return Crewmate(mesh, model, phase == SpinePhase.Telegraph ? CrewPose.Walk : CrewPose.Idle, t, (int)Math.Round(extra));
            case EnemyKind.Gaunt:
                {
                    // Spindly, too tall, the Hollow's figure drawn out (GDD v1.1 A.6). Asleep it's curled up (squashed low and
                    // breathing slowly); woken it follows, and its anger (extra2, 0..1) leans it in; striking, it reaches.
                    if (!_models.ContainsKey("hollow"))
                        return false;
                    bool asleep = phase is SpinePhase.Dormant;
                    float lean = (float)Math.Clamp(extra2, 0, 1) * 0.5f;
                    var at = (asleep ? Matrix4x4.CreateScale(0.9f, 0.38f + 0.02f * (float)Math.Sin(t * 1.3), 0.9f) : Matrix4x4.CreateScale(0.78f, 1.32f, 0.78f))
                        * Matrix4x4.CreateRotationX(-lean) * model;
                    bool striking = phase is SpinePhase.Commit or SpinePhase.Grab or SpinePhase.Punish;
                    return Draw(mesh, "hollow", striking ? "reach" : "idle", striking ? t : t * 0.3, !striking, at, seed: 31,
                        adjust: (_, l) => l with { Colour = l.Colour * 0.55f });
                }
            case EnemyKind.CarHugger:
                {
                    // A heap of bog bodies (the weight model) low beside the line; latched on the rear car it grabs, then eats.
                    if (phase is SpinePhase.Dormant)
                        return true;
                    var at = phase is SpinePhase.Alert or SpinePhase.Telegraph ? Matrix4x4.CreateTranslation(0, -0.3f, 0) * model : model;
                    if (_models.TryGetValue("weight", out var w))
                    {
                        double grab = w.Model.Clips.TryGetValue("grab", out var g) ? g.Duration : 0;
                        return phase switch
                        {
                            SpinePhase.BreakOff => Draw(mesh, "weight", "release", t, false, at),
                            SpinePhase.Alert or SpinePhase.Telegraph => Draw(mesh, "weight", "drag", t * 0.4, true, at),
                            _ when t < grab => Draw(mesh, "weight", "grab", t, false, at),
                            _ => Draw(mesh, "weight", "drag", t - grab, true, at),
                        };
                    }
                    if (!_models.ContainsKey("dragger"))
                        return false;
                    for (int i = 0; i < 3; i++)
                    {
                        var limb = Matrix4x4.CreateScale(1.7f) * Matrix4x4.CreateRotationZ((i - 1) * 0.5f) * Matrix4x4.CreateRotationY(MathF.PI / 2)
                            * Matrix4x4.CreateTranslation((i - 1) * 0.35f, -0.2f, 0.2f) * at;
                        Draw(mesh, "dragger", "grip", t * 0.6 + i * 0.4, true, limb, seed: 20 + i);
                    }
                    return true;
                }
            case EnemyKind.TrackDoll:
                {
                    // A large porcelain doll (GDD v1.1 A.2): the crew figure, too big and pale, stood stock still; the face is
                    // what the lamp catches out to 200 m (a faint glow of its own, so the render shows it past the beam).
                    var at = Matrix4x4.CreateScale(1.25f) * model;
                    var white = new Vector3(0.86f, 0.84f, 0.8f);
                    if (!Draw(mesh, "crew", phase == SpinePhase.Punish ? "walk" : "idle", phase == SpinePhase.Punish ? t * 0.6 : 0.1, true, at, variant: 1, seed: 3,
                            adjust: (_, l) => l with { Colour = white }))
                        return false;
                    var head = BoneAt("crew", "head", at);
                    var (r, u, b) = Basis(model);
                    mesh.Emissive = 1;
                    mesh.Box(head - b * 0.1f, r, u, b, new Vector3(0.1f, 0.12f, 0.02f), white * 0.9f);
                    mesh.Emissive = 0;
                    foreach (float side in new[] { -1f, 1f })
                        mesh.Box(head + r * (side * 0.045f) - b * 0.125f + u * 0.02f, r, u, b, new Vector3(0.018f, 0.012f, 0.006f), Palette.SootBlack);
                    return true;
                }
            case EnemyKind.Whistler:
                {
                    // Hidden in the gap it's crouched small; carrying someone off it runs, long and low (GDD v1.1 A.4).
                    bool running = phase is SpinePhase.Grab or SpinePhase.Punish or SpinePhase.BreakOff;
                    var at = Matrix4x4.CreateScale(0.8f, 1.15f, 0.8f) * Matrix4x4.CreateRotationX(running ? -0.6f : 0) * model;
                    string clip = running ? "run" : "crouch_idle";
                    return Draw(mesh, "husk", clip, t * (running ? 1.6 : 0.5), true, at, variant: 2, seed: 41, adjust: (_, l) => l with { Colour = l.Colour * 0.5f })
                        || Draw(mesh, "crew", clip, t, true, at, variant: 2, seed: 41, adjust: (_, l) => l with { Colour = l.Colour * new Vector3(0.25f, 0.24f, 0.24f) });
                }
            case EnemyKind.TippyToesie:
                {
                    // Small and thin, tiptoeing (GDD v1.1 A.5): the husk at child height, walking at a crawl, heels up. Its hand
                    // over someone's mouth, it's stood tight behind them.
                    var at = Matrix4x4.CreateScale(0.7f, 0.85f, 0.7f) * Matrix4x4.CreateTranslation(0, 0.08f, 0) * model;
                    bool holding = phase is SpinePhase.Grab or SpinePhase.Punish;
                    string clip = holding ? "idle" : phase == SpinePhase.BreakOff ? "run" : "walk";
                    return Draw(mesh, "husk", clip, t * (clip == "walk" ? 0.35 : 1), true, at, variant: 4, seed: 51, adjust: (_, l) => l with { Colour = l.Colour * 0.7f })
                        || Draw(mesh, "crew", clip, t, true, at, variant: 4, seed: 51, adjust: (_, l) => l with { Colour = l.Colour * new Vector3(0.3f, 0.28f, 0.28f) });
                }
            case EnemyKind.FireFlies:
                {
                    // A swarm of sparks round a lamp (GDD v1.1 A.5), brighter and tighter the longer they linger.
                    if (phase is SpinePhase.Dormant or SpinePhase.Gone)
                        return true;
                    var (r, u, b) = Basis(model);
                    float spread = phase == SpinePhase.Alert ? 1.6f : 0.7f;
                    mesh.Emissive = 1;
                    for (int i = 0; i < 24; i++)
                    {
                        double a = i * 2.39996 + t * (1.5 + (i % 5) * 0.3);
                        float rad = spread * (0.4f + 0.6f * (float)((i * 0.618) % 1));
                        var p = model.Translation + r * (float)(Math.Cos(a) * rad) + u * (float)(Math.Sin(a * 1.3 + i) * rad * 0.5) + b * (float)(Math.Sin(a) * rad);
                        mesh.Box(p, r, u, b, new Vector3(0.018f), Palette.FurnaceOrange * (1.2f + (float)Math.Sin(t * 11 + i)));
                    }
                    mesh.Emissive = 0;
                    mesh.PointLights.Add(new PointLight(model.Translation, Palette.FurnaceOrange * 0.8f, 4f));
                    return true;
                }
            case EnemyKind.Ribbit:
                {
                    // A giant toad-rabbit (GDD v1.1 A.6): the hound's body squat and wide, mottled olive. Lined up to strike it
                    // crouches (the throats swell: the tell); tongues out, it lunges; otherwise it hops in bursts.
                    var at = Matrix4x4.CreateScale(1.25f, 0.7f, 0.8f) * model;
                    var (clip, loop, ct) = phase switch
                    {
                        SpinePhase.Telegraph => ("crouch", true, t),
                        SpinePhase.Grab or SpinePhase.Punish => ("lunge", false, t % 1.2),
                        _ => ((t % 1.1) < 0.5 ? "lunge" : "crouch", (t % 1.1) >= 0.5, t % 1.1 < 0.5 ? t % 1.1 : t % 1.1 - 0.5),
                    };
                    if (!Draw(mesh, "cinder_hound", clip, ct, loop, at, glow: 0.1f, seed: (float)extra,
                            adjust: (_, l) => l with { Colour = Palette.MuddyOlive * 0.8f }))
                        return false;
                    if (phase == SpinePhase.Telegraph)
                    {
                        var (r, u, b) = Basis(model);
                        float swell = 0.1f + 0.08f * (float)Math.Min(1, t);
                        mesh.Box(model.Translation + u * 0.45f - b * 0.55f, r, u, b, new Vector3(swell, swell * 0.8f, swell), Palette.MuddyOlive * 1.3f);
                    }
                    return true;
                }
            case EnemyKind.Grumbler:
                {
                    // A labourer scuttling like a spider (GDD v1.1 A.8): the husk bent flat on all fours, gnawing; feral
                    // (extra2), it runs.
                    bool feral = extra2 > 0.5;
                    var at = Matrix4x4.CreateScale(1.05f, 0.9f, 1.05f) * Matrix4x4.CreateRotationX(feral ? -0.9f : -1.1f) * model;
                    string clip = feral ? "run" : "crouch_idle";
                    return Draw(mesh, "husk", clip, t * (feral ? 1.5 : 2.5), true, at, variant: 7, seed: 61, adjust: (_, l) => l with { Colour = l.Colour * 0.65f })
                        || Draw(mesh, "crew", clip, t, true, at, variant: 7, seed: 61, adjust: (_, l) => l with { Colour = l.Colour * new Vector3(0.3f, 0.26f, 0.24f) });
                }
            case EnemyKind.Choir:
                {
                    // One of the Choir's small flying ghosts (GDD v1.1 A.7): the Hollow's figure, child-sized and pale, bobbing
                    // in the air with a cold light of its own.
                    if (!_models.ContainsKey("hollow"))
                        return false;
                    var at = Matrix4x4.CreateScale(0.45f, 0.55f, 0.45f) * Matrix4x4.CreateTranslation(0, (float)(0.15 * Math.Sin(t * 2.3 + extra)), 0) * model;
                    var pale = Palette.BlueGrey * 1.6f;
                    Draw(mesh, "hollow", phase is SpinePhase.Grab or SpinePhase.Commit ? "reach" : "idle", t, true, at, seed: 71, glow: 1.5f,
                        adjust: (_, l) => l with { Colour = pale });
                    mesh.PointLights.Add(new PointLight(model.Translation + new Vector3(0, 0.5f, 0), Palette.BlueGrey * 0.6f, 3f));
                    return true;
                }
        }
        return false;
    }

    /// <summary>
    /// <see cref="Enemy(MeshBuilder, in Matrix4x4, EnemyKind, SpinePhase, double, double)"/> for a live enemy, turning
    /// the basis for which side of the car or the line it's on: a Dragger on a car's +X side reaches over that edge, and the Switchman turns to face the train coming up the line.
    /// </summary>
    public bool Enemy(MeshBuilder mesh, in Matrix4x4 model, Enemy e)
    {
        var m = model;
        switch (e.Kind)
        {
            case EnemyKind.Dragger when e.Local.X > 0:
                m = Matrix4x4.CreateRotationY(MathF.PI) * model;
                break;
            case EnemyKind.Climber when e.Phase == SpinePhase.Telegraph:
                // At the gap, facing in at the couplers.
                m = Matrix4x4.CreateRotationY(e.Local.X > 0 ? MathF.PI / 2 : -MathF.PI / 2) * model;
                break;
            case EnemyKind.Switchman:
                // Face back down the line at the train, turned in towards the track.
                m = Matrix4x4.CreateRotationY(MathF.PI - Math.Sign(e.Lateral) * 0.6f) * model;
                break;
        }
        return Enemy(mesh, m, e.Kind, e.Phase, e.PhaseSeconds, e.Extra, e.Health, aboard: e.Attached >= 0, extra2: e.Extra2);
    }

    static (Vector3 Right, Vector3 Up, Vector3 Back) Basis(in Matrix4x4 m) =>
        (Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitX, m)), Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitY, m)), Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, m)));
}
