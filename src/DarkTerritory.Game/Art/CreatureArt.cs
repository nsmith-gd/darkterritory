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
    public static readonly string[] Names = ["crew", "cinder_hound", "sleeper", "clinger", "hollow", "switchman", "soot_child", "dragger", "husk", "weight",
        "track_doll", "car_hugger", "tippy_toesie", "whistler", "ribbit", "choir", "gaunt", "grumbler", "stoker", "follower", "climber", "fire_fly", "passenger"];

    /// <summary>A haunting Track Doll's turns aboard (App. A.2 HAUNT): this long over the cargo, then this long giggling.</summary>
    const double DollAdmires = 12, DollGiggles = 5;
    /// <summary>
    /// The Track Doll's head turns to whoever's looking, in clicks of this much (degrees): a porcelain head on its socket,
    /// round as far as it takes, behind it included (GDD §31: still when watched, then too-fast corrections).
    /// </summary>
    const float DollHeadClick = 30;
    /// <summary>On the rail it beckons the train in once it's this close (m, from the eye).</summary>
    const float DollBeckons = 40;
    /// <summary>Aboard, bent over the cargo, it looks round at you when you're this close (m).</summary>
    const float DollNotices = 8;

    /// <summary>
    /// A lurking Car Hugger's lift (m): its origin is where its mouth will be on the car. Lying flat it's sunk to its
    /// shoulders in the low ground by the line, and muddied (<see cref="HuggerLurkMud"/>): in the lamp it's a mound
    /// that could be anything, not a thing on the ground (App. A.3 LURK: its tell is the grinding once it's on).
    /// </summary>
    const float HuggerLurkLift = -0.2f, HuggerLurkMud = 0.3f;

    // How far forward of where they took hold a Car Hugger's hands have gone, following its car's eaten edge (Art/BiteKit):
    // set by Enemy(e, bite) for the one draw it makes.
    float _biteGrip;
    static readonly string[] HuggerArms = ["a", "b", "c", "d"];

    /// <summary>
    /// Who a Tippy Toesie is after (GreyboxScene, for its target: the crewmate whose id is its Extra), camera-relative:
    /// their feet and the way they face. It faces them; on them, it's stood tight behind with a hand over their mouth.
    /// </summary>
    public readonly record struct Prey(Vector3 Feet, Vector3 Forward)
    {
        public Vector3 Right => Vector3.Normalize(Vector3.Cross(Forward, Vector3.UnitY));
        /// <summary>A point in their frame (x right, y up, z back), camera-relative.</summary>
        public Vector3 At(float x, float y, float z) => Feet + Right * x + Vector3.UnitY * y - Forward * z;
    }

    // Set by Enemy(e, prey) for the one draw it makes, as _biteGrip.
    Prey? _prey;

    // A Ribbit's tongue goes to its catch's chest (a crewmate's 1.8 m, tools/blender/crew.py), this high, this thick at the
    // root (m), sagging this much of its length.
    const float RibbitTongueAt = 1.15f, RibbitTongueThick = 0.035f, RibbitTongueSag = 0.06f;

    // The cold about a Choir ghost: a faint light, the colour of its skin, so they're seen at night but never glow (§26).
    const float ChoirCold = 0.25f;

    // "Giant toad-rabbits" (GDD §21): the model's a big dog's size, drawn this much bigger (its head at a crewmate's waist).
    const float RibbitScale = 1.4f;

    /// <summary>
    /// The tongue, out from the mouth <paramref name="a"/> to its catch at <paramref name="b"/> (camera-relative): wet,
    /// dark red, thinning to its tip, sagging a little, and twitching taut as it reels (GDD App. A.6 TONGUE).
    /// </summary>
    void Tongue(MeshBuilder mesh, Vector3 a, Vector3 b, float t)
    {
        var k = new Kit(Look, mesh);
        k.Use("flesh", new Vector3(0.22f, 0.05f, 0.05f), 0.2f, 0.85f);
        const int n = 8;
        float len = Vector3.Distance(a, b);
        float sag = RibbitTongueSag * len * (0.7f + 0.3f * MathF.Sin(t * 9));
        Vector3 At(float u) => Vector3.Lerp(a, b, u) - Vector3.UnitY * (sag * 4 * u * (1 - u));
        for (int i = 0; i < n; i++)
        {
            float u0 = i / (float)n, u1 = (i + 1) / (float)n;
            k.Cylinder(At(u0), At(u1), RibbitTongueThick * (1 - 0.55f * u0), 8, caps: false, radiusB: RibbitTongueThick * (1 - 0.55f * u1));
        }
        // Its tip spread over them, a pad.
        k.Cylinder(b - Vector3.Normalize(b - a) * 0.02f, b + Vector3.Normalize(b - a) * 0.02f, RibbitTongueThick * 1.4f, 8);
    }
    Room _room;

    /// <summary>
    /// The firebox door, when it's open: its centre from the firebox's place (the engine's frame; Art/TrainKit.FireDoor),
    /// or null when it's shut. GreyboxScene sets it from the boiler each frame; a Stoker shows only through it.
    /// </summary>
    public Vector3? FireDoorOpen { get; set; }

    // Set by Enemy(e, pace) for the one draw it makes, as _room.
    float _pace;

    // A Gaunt or a Grumbler goes (lopes, crawls, scuttles) above this pace (m/s), and stands (listens, squats, bites) below
    // it. At each point of its anger a Gaunt leans in this much more of the way (all of it at the sim's default threshold,
    // enemies.json gaunt.attackAt), its back bent forward this far (radians) and its head tipped over this far at the most.
    // A riding Follower is this high on its carrier and this far behind their middle (m: a crewmate's back, tools/blender/
    // crew.py); its nest swells it this much (the full nest, 1 + this times its size).
    const float FollowerUp = 1.35f, FollowerBack = 0.15f, FollowerSwell = 1.5f;

    // The Stoker's own fire, in its mouth and its splits: the sick green of a fire with it in (GreyboxScene.FireColour).
    static readonly Vector3 StokerFire = new(0.35f, 0.6f, 0.22f);

    const float Going = 0.4f, GauntLeanPerAnger = 0.25f, GauntLean = 0.45f, GauntTilt = 0.6f;

    // Fire Flies round a car's lantern (tools/blender/fire_fly.py; the lantern's tools/models hand_lantern, its flame at the
    // model's origin): at most this many of them, the swarm filling as they linger (all of it by FireFlySwarmFills
    // seconds: the sim's ignite time, enemies.json fireFlies, as the GDD's ~20 s); the settled ones on the glass, a
    // cylinder this wide (m, to their bodies) and this far below and above the flame; the flying ones this far out.
    // The Passenger's walk and drag clips cover the ground at these paces (m/s: tools/blender/passenger.py's, the crew's
    // walk); dragging, it's this far ahead of the one it drags (m: they're at its feet in the sim).
    const float PassengerWalkPace = 1.4f, PassengerDragPace = 1.3f, PassengerStride = 0.8f;

    const int FireFlyMost = 22;
    const float FireFlySwarmFills = 20, FireFlyGlass = 0.085f, FireFlyGlassBelow = 0.07f, FireFlyGlassAbove = 0.08f, FireFlyOrbit = 0.45f;

    /// <summary>
    /// The room a creature stands in, in its car (GreyboxScene, for a Tippy Toesie: it's taller than the train was built
    /// for, note 110): under a roof (a car's interior, the cab) it stoops; under a lintel it ducks, facing
    /// <see cref="Through"/> (out through the door, along the car frame's x or z) when it has nobody to face.
    /// </summary>
    public readonly record struct Room(bool Indoors, bool Doorway, float Headroom, Vector3 Through)
    {
        /// <summary>Out in the open: nothing over it.</summary>
        public static readonly Room Open = new(false, false, float.PositiveInfinity, default);

        /// <summary>Where <paramref name="local"/> (feet, in the car's frame) is in <paramref name="shape"/>.</summary>
        public static Room Of(Sim.Train.CarShape shape, Double3 local)
        {
            float headroom = float.PositiveInfinity;
            bool indoors = false;
            foreach (var room in new[] { shape.Interior, shape.Cab })
                if (room is { } r && local.X > r.Min.X - 0.05 && local.X < r.Max.X + 0.05 && local.Z > r.Min.Z - 0.05 && local.Z < r.Max.Z + 0.05
                    && local.Y >= r.Min.Y - 0.3 && local.Y < r.Max.Y)
                {
                    indoors = true;
                    headroom = MathF.Min(headroom, (float)(r.Max.Y - local.Y));
                }
            // A lintel: something thin across one way, its underside a door's height or so over the feet, and the feet
            // within a stride of it (the car's end and side doors, the cab's doorways: the standard doorway).
            bool doorway = false;
            Vector3 through = default;
            foreach (var solid in shape.Solids)
            {
                var b = solid.Box;
                double under = b.Min.Y - local.Y, dx = b.Max.X - b.Min.X, dz = b.Max.Z - b.Min.Z;
                if (under < 1.5 || under > 2.6 || Math.Min(dx, dz) > 0.3)
                    continue;
                bool acrossX = dx < dz; // the wall it's in runs along z (a side door); else across the car (an end door)
                double off = acrossX ? local.X - b.Centre.X : local.Z - b.Centre.Z;
                double along = acrossX ? local.Z : local.X, lo = acrossX ? b.Min.Z : b.Min.X, hi = acrossX ? b.Max.Z : b.Max.X;
                if (Math.Abs(off) > 0.6 || along < lo - 0.15 || along > hi + 0.15)
                    continue;
                doorway = true;
                headroom = MathF.Min(headroom, (float)under);
                // Out: away from the middle of the car.
                double outward = acrossX ? Math.Sign(b.Centre.X) : Math.Sign(b.Centre.Z);
                through = acrossX ? new Vector3((float)outward, 0, 0) : new Vector3(0, 0, (float)outward);
            }
            return new Room(indoors, doorway, headroom, through);
        }
    }

    // Seen, Tippy Toesie's off (the sim has it hidden at once, Interior.cs): it's seen scuttling off for this long (s), at
    // this speed (m/s), before it's gone. Smothering, it stands this far behind its victim's heels (m), its hand over
    // their mouth: a crewmate's, 1.8 m tall (tools/blender/crew.py), at this height, this far in front of their middle.
    const float TippyFleeShow = 0.5f, TippyFleeSpeed = 4f, TippyBehind = 0.42f, PreyMouthY = 1.6f, PreyMouthFore = 0.12f;

    static float SmoothStep(float a, float b, float x)
    {
        float t = Math.Clamp((x - a) / (b - a), 0, 1);
        return t * t * (3 - 2 * t);
    }

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
        ["track_doll"] = 0.35f,
        ["car_hugger"] = 0.3f,
        ["tippy_toesie"] = 0.1f,   // (its dirt is baked: the grime's rust would warm the plaster)
        ["whistler"] = 0.3f,
        ["ribbit"] = 0.25f,
        ["choir"] = 0.3f,
        ["gaunt"] = 0.4f,
        ["grumbler"] = 0.5f,
        ["stoker"] = 0.2f,
        ["follower"] = 0.2f,
        ["climber"] = 0.3f,
        ["fire_fly"] = 0.1f,
        ["passenger"] = 0.35f,
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
        float glow = 1, float seed = 0, Func<ModelMaterial, MaterialLook, MaterialLook>? adjust = null) =>
        Draw(mesh, name, clip, time, loop, at, null, variant, glow, seed, adjust);

    /// <summary><see cref="Draw(MeshBuilder, string, string, double, bool, in Matrix4x4, int, float, float, Func{ModelMaterial, MaterialLook, MaterialLook}?)"/>, with the clip's pose worked on before it's drawn (a reach, a look).</summary>
    bool Draw(MeshBuilder mesh, string name, string clip, double time, bool loop, in Matrix4x4 at, Action<Entry>? posed, int variant = 0,
        float glow = 1, float seed = 0, Func<ModelMaterial, MaterialLook, MaterialLook>? adjust = null)
    {
        if (!_models.TryGetValue(name, out var m) || !m.Model.Clips.TryGetValue(clip, out var c))
            return false;
        _skinner.Evaluate(m.Model, c, time, loop, m.Pose);
        posed?.Invoke(m);
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

    /// <summary>The model turned about its own up to face <paramref name="target"/> (camera-relative, as its translation is).</summary>
    static Matrix4x4 Facing(in Matrix4x4 model, Vector3 target)
    {
        var (r, _, b) = Basis(model);
        var to = target - model.Translation;
        float x = Vector3.Dot(to, r), z = Vector3.Dot(to, b);
        if (x * x + z * z < 1e-6f)
            return model;
        // (CreateRotationY(a) takes the model's forward, -Z, to (-sin a, -cos a) in its basis.)
        var m = Matrix4x4.CreateRotationY(MathF.Atan2(-x, -z)) * model;
        m.Translation = model.Translation;
        return m;
    }

    /// <summary>A posed bone and everything hung off it turned by <paramref name="rotation"/> (model space) about the bone's head.</summary>
    static void Bend(Entry m, string bone, in Matrix4x4 rotation)
    {
        var sk = m.Model.Skeleton;
        int at = sk.IndexOf(bone);
        if (at < 0)
            return;
        var pivot = m.Pose.World[at].Translation;
        var turn = Matrix4x4.CreateTranslation(-pivot) * rotation * Matrix4x4.CreateTranslation(pivot);
        for (int b = 0; b < sk.Count; b++)
            for (int p = b; p >= 0; p = sk.Parents[p])
                if (p == at)
                {
                    m.Pose.World[b] *= turn;
                    m.Pose.Skin[b] = sk.InverseBind[b] * m.Pose.World[b];
                    break;
                }
    }

    /// <summary>
    /// A listening Gaunt leant in over who it's listening to, <paramref name="anger"/> (0..1) of the way: its back bent
    /// forward from the chest (the model faces −Z) and its head tipped over on its side (App. A.6 LISTEN's telegraph).
    /// </summary>
    static void LeanIn(Entry m, float anger)
    {
        Bend(m, "spine_03", Matrix4x4.CreateRotationX(-GauntLean * anger));
        Bend(m, "head", Matrix4x4.CreateRotationZ(GauntTilt * anger));
    }

    /// <summary>
    /// The doll's head turned on its neck to face <paramref name="eye"/> (model space), the turn stepped in clicks of
    /// <see cref="DollHeadClick"/> (so it jumps round as you move, and holds between): about the model's up, from the
    /// way the clip has it facing (the model faces −Z).
    /// </summary>
    static void WatchWithHead(Entry m, Vector3 eye)
    {
        var sk = m.Model.Skeleton;
        int head = sk.IndexOf("head");
        if (head < 0)
            return;
        var pivot = m.Pose.World[head].Translation;
        var to = eye - pivot;
        if (to.X * to.X + to.Z * to.Z < 1e-4f)
            return;
        float yaw = MathF.Atan2(-to.X, -to.Z);
        float click = DollHeadClick * MathF.PI / 180;
        yaw = MathF.Round(yaw / click) * click;
        if (MathF.Abs(yaw) < 1e-4f)
            return;
        Bend(m, "head", Matrix4x4.CreateRotationY(yaw));
    }

    /// <summary>One arm of the posed model to a hand position (model space) by two-bone IK, the elbow toward the pole.</summary>
    static void Reach(Entry m, string side, Vector3 target, Vector3 pole) => Reach(m, "upperarm_" + side, "lowerarm_" + side, "hand_" + side, target, pole);

    /// <summary>A two-bone chain (upper, lower, and the bone at its end) to a target, the middle joint toward the pole.</summary>
    static void Reach(Entry m, string upperBone, string lowerBone, string handBone, Vector3 target, Vector3 pole)
    {
        var sk = m.Model.Skeleton;
        int upper = sk.IndexOf(upperBone), lower = sk.IndexOf(lowerBone), hand = sk.IndexOf(handBone);
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
                {
                    // In the firebox (GDD v1.2 §21, App. A.5; tools/blender/stoker.py): seen only through the door, when it's
                    // open (FireDoorOpen; Enemy(e) put the model at the door). Else it's only its work: the gauge, the
                    // wrong glow the scene gives the fire, the hiss. In the fire it watches out of the door, its fingers
                    // over the lip; feeding (COMMIT), an arm comes out over the lip, groping for whoever's opened it.
                    if (FireDoorOpen is null || phase is not (SpinePhase.Telegraph or SpinePhase.Commit))
                        return true;
                    if (Draw(mesh, "stoker", phase == SpinePhase.Commit ? "reach" : "peer", t, true, model, seed: 13))
                    {
                        // The fire in its mouth and its splits lights its own face, the wrong colour, flickering.
                        float flicker = 0.8f + 0.2f * (float)Math.Sin(t * 17.0 + Math.Sin(t * 5.3) * 2);
                        mesh.PointLights.Add(new PointLight(Vector3.Transform(new Vector3(0, -0.05f, -0.12f), model), StokerFire * flicker, 0.9f));
                    }
                    return true;
                }
            case EnemyKind.Climber when _models.ContainsKey("climber"):
                {
                    // The Climbers (GDD v1.2 §21, App. A.4; tools/blender/climber.py): one of the crew gone wrong, the gas
                    // mask grown into its face. Pacing the train it runs bent double on all fours; at a gap it scrabbles
                    // up between the cars, facing in (Enemy(e) turns it); on the roofs it walks crouched for the engine;
                    // inside an unlit car (extra −1) it waits folded in a corner; on a lone player it grabs.
                    bool inside = extra < 0;
                    string clip = phase switch
                    {
                        SpinePhase.Telegraph => "scrabble",
                        SpinePhase.Grab or SpinePhase.Punish => "grab",
                        SpinePhase.Commit when inside => "crouch",
                        SpinePhase.Commit => "walk",
                        _ => "run",
                    };
                    return Draw(mesh, "climber", clip, t, true, model, seed: 17);
                }
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
            case EnemyKind.Follower when _models.ContainsKey("follower"):
                {
                    // The Followers (GDD v1.2 §21, App. A.6; tools/blender/follower.py): a hand gone wrong, a mouth on its
                    // back. On someone's back (Enemy(e) laid it flat between their shoulder blades) it clings, and twitches;
                    // off it, it scuttles on its fingertips; at its car it spreads over the loot and swells as its nest
                    // builds (extra2), kneading it, eating.
                    bool nesting = phase == SpinePhase.Punish || phase == SpinePhase.Commit && extra2 > 0;
                    float swell = phase == SpinePhase.Punish ? 1 : (float)Math.Clamp(extra2, 0, 1);
                    var at = nesting ? Matrix4x4.CreateScale(1 + FollowerSwell * swell) * model : model;
                    string clip = nesting ? "nest" : phase == SpinePhase.Commit ? "crawl" : "cling";
                    return Draw(mesh, "follower", clip, t, true, at, seed: 29);
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
            case EnemyKind.Passenger when _models.ContainsKey("passenger"):
                {
                    // The Passenger (GDD v1.2 §21, App. A.8; tools/blender/passenger.py): a conductor off a train lost long
                    // ago, its scarf dyed the colour of the crewmate it copies (extra), as theirs is. Hanging about it's dead
                    // still, or it walks the crew's walk as fast as it's been going (Enemy(e)'s pace); with someone (GRAB)
                    // it hauls them by the collar a stride ahead of them (the sim has them at its feet), and stopped at the
                    // caboose's coupling it heaves at the pin.
                    float pace = _pace;
                    _pace = 0;
                    var paint = PaintOf((int)Math.Round(extra));
                    bool going = pace > Going;
                    if (phase is SpinePhase.Commit or SpinePhase.Grab or SpinePhase.Punish)
                    {
                        var ahead = Matrix4x4.CreateTranslation(0, 0, -PassengerStride) * model;
                        return going ? Draw(mesh, "passenger", "drag", t * Math.Max(1, pace / PassengerDragPace), true, ahead, adjust: paint)
                            : Draw(mesh, "passenger", "pin", t, true, ahead, adjust: paint);
                    }
                    return going ? Draw(mesh, "passenger", "walk", t * pace / PassengerWalkPace, true, model, adjust: paint)
                        : Draw(mesh, "passenger", "stand", t, true, model, adjust: paint);
                }
            case EnemyKind.Passenger:
                // One of the crew (App. A.7 BLEND): the crew figure in the look of whoever it copies (extra), walking its loop.
                // In play it's drawn through the crew's own path (GreyboxScene.AsCrewmate), gait and all.
                return Crewmate(mesh, model, phase == SpinePhase.Telegraph ? CrewPose.Walk : CrewPose.Idle, t, (int)Math.Round(extra));
            case EnemyKind.Gaunt when _models.ContainsKey("gaunt"):
                {
                    // The Gaunt (GDD v1.2 §21, App. A.6; tools/blender/gaunt.py). Asleep, the heap breathing; stirring, its
                    // head up out of it to look. Woken, it lopes after its waker while they go and stands over them
                    // listening when they stop, leant in further and its head tipped over further at every point of
                    // anger (extra2); at its threshold the fists come down. The cars weren't built for it (note 118): aboard
                    // it drags itself along on its arms, squats to listen, and smashes from the squat.
                    var room = _room;
                    float pace = _pace;
                    _room = Room.Open;
                    _pace = 0;
                    bool low = room.Indoors || room.Doorway;
                    float anger = Math.Clamp((float)extra2 * GauntLeanPerAnger, 0, 1);
                    string clip = phase switch
                    {
                        SpinePhase.Dormant => "sleep",
                        SpinePhase.Alert => "stir",
                        SpinePhase.Commit or SpinePhase.Grab or SpinePhase.Punish => low ? "smash" : "attack",
                        _ when pace > Going => low ? "crawl" : "follow",
                        _ => low ? "squat" : "listen",
                    };
                    Action<Entry>? lean = anger > 0 && clip is "listen" or "squat" ? m => LeanIn(m, anger) : null;
                    // (Stirring it comes up once and stays up, watching.)
                    return Draw(mesh, "gaunt", clip, t, clip != "stir", model, lean, seed: 47);
                }
            case EnemyKind.Gaunt:
                {
                    // (No model: the Hollow drawn out, spindly. Asleep it's curled up, squashed low and breathing slowly;
                    // woken it follows, leant in as it angers; striking, it reaches.)
                    if (!_models.ContainsKey("hollow"))
                        return false;
                    bool asleep = phase is SpinePhase.Dormant;
                    float lean = Math.Clamp((float)extra2 * GauntLeanPerAnger, 0, 1) * 0.5f;
                    var at = (asleep ? Matrix4x4.CreateScale(0.9f, 0.38f + 0.02f * (float)Math.Sin(t * 1.3), 0.9f) : Matrix4x4.CreateScale(0.78f, 1.32f, 0.78f))
                        * Matrix4x4.CreateRotationX(-lean) * model;
                    bool striking = phase is SpinePhase.Commit or SpinePhase.Grab or SpinePhase.Punish;
                    return Draw(mesh, "hollow", striking ? "reach" : "idle", striking ? t : t * 0.3, !striking, at, seed: 31,
                        adjust: (_, l) => l with { Colour = l.Colour * 0.55f });
                }
            case EnemyKind.CarHugger:
                {
                    // The Car Hugger (GDD v1.2 §21, App. A.3; tools/blender/car_hugger.py), its origin its mouth. Lurking, it
                    // lies sunk in the low ground by the line; latched on the rear car it comes up onto it and clamps on, its
                    // mouth over the end door, then eats, the teeth grinding; with someone in front of its mouth it
                    // swallows; cut loose, it lets go.
                    if (!_models.TryGetValue("car_hugger", out var hugger))
                        return false;
                    double latch = hugger.Model.Clips.TryGetValue("latch", out var l) ? l.Duration : 0;
                    // Eaten into its car, its hands have gone forward with the side walls' torn edge: each arm reaches
                    // on to where its hand held, that much further along the car (the clip still works the fingers).
                    float grip = _biteGrip;
                    _biteGrip = 0;
                    Action<Entry>? regrip = grip <= 0 ? null : e =>
                    {
                        var sk = e.Model.Skeleton;
                        foreach (var n in HuggerArms)
                        {
                            int lower = sk.IndexOf($"arm_{n}_02"), hand = sk.IndexOf($"hand_{n}"), upper = sk.IndexOf($"arm_{n}_01");
                            if (lower < 0 || hand < 0 || upper < 0)
                                continue;
                            var wrist = e.Pose.World[hand].Translation;
                            var pole = e.Pose.World[lower].Translation - (e.Pose.World[upper].Translation + wrist) * 0.5f;
                            Reach(e, $"arm_{n}_01", $"arm_{n}_02", $"hand_{n}", wrist - Vector3.UnitZ * grip, pole);
                        }
                    };
                    return phase switch
                    {
                        SpinePhase.Dormant => Draw(mesh, "car_hugger", "lurk", t, true, Matrix4x4.CreateTranslation(0, HuggerLurkLift, 0) * model,
                            adjust: (_, look) => look with { Colour = look.Colour * HuggerLurkMud }),
                        SpinePhase.BreakOff => Draw(mesh, "car_hugger", "release", t, false, model),
                        SpinePhase.Grab or SpinePhase.Punish => Draw(mesh, "car_hugger", "swallow", t, true, model, regrip),
                        SpinePhase.Telegraph when t < latch => Draw(mesh, "car_hugger", "latch", t, false, model),
                        SpinePhase.Telegraph => Draw(mesh, "car_hugger", "feed", t - latch, true, model, regrip),
                        _ => Draw(mesh, "car_hugger", "feed", t, true, model, regrip),
                    };
                }
            case EnemyKind.TrackDoll:
                {
                    // The porcelain doll (GDD v1.2 §21, App. A.2; tools/blender/track_doll.py). On the rail it stands stock
                    // still. Aboard it stands over the cargo admiring it and then giggles, by turns; at the empty cab's
                    // controls (extra2) it tampers; cornered (extra) it cowers. Its face and glass eyes draw under a material of
                    // their own, lit a little from within, so the white face reads in the lamp out to 200 m through the fog
                    // (§21); aboard, close to, it's barely there.
                    // Whatever it's doing, its head turns to the one looking at it (the eye: the model's translation is from
                    // it), in clicks; bent over the cargo, only once they're close; at the controls, now and then, a
                    // glance back; cowering, never.
                    float dist = model.Translation.Length();
                    string clip = !aboard && dist < DollBeckons ? "beckon" : "stand";
                    double ct = t;
                    bool watch = true;
                    if (phase == SpinePhase.Punish)
                    {
                        double turn = t % (DollAdmires + DollGiggles);
                        (clip, ct) = extra > 0.5 ? ("cower", t)
                            : extra2 > 0.5 ? ("tamper", t)
                            : turn < DollAdmires ? ("admire", turn) : ("giggle", turn - DollAdmires);
                        watch = clip switch { "cower" => false, "admire" => dist < DollNotices, "tamper" => t % 5 < 1.2, _ => true };
                    }
                    // TELEGRAPH is the lamp catching the glaze (the sim enters it with the lamp on, within 200 m): the face
                    // shines out of the fog far off and eases to a sheen as the train closes on it (the model's translation is
                    // from the eye). Unrevealed, or aboard, it's only porcelain.
                    float far = SmoothStep(15, 120, model.Translation.Length());
                    float glow = phase == SpinePhase.Telegraph ? 0.15f + 0.75f * far : 0.05f;
                    // On the rail it faces the train coming at it (a thing on the line faces down it, the way the train goes).
                    var at = aboard ? model : Matrix4x4.CreateRotationY(MathF.PI) * model;
                    Action<Entry>? look = null;
                    if (watch && Matrix4x4.Invert(at, out var toModel))
                    {
                        var eye = Vector3.Transform(Vector3.Zero, toModel);
                        look = e => WatchWithHead(e, eye);
                    }
                    return Draw(mesh, "track_doll", clip, ct, true, at, look, seed: 3,
                        adjust: (mm, l) => mm.Name.EndsWith(".face", StringComparison.Ordinal) ? l with { Emissive = MathF.Max(l.Emissive, glow) } : l)
                        || Draw(mesh, "track_doll", "stand", ct, true, at, look, seed: 3);
                }
            case EnemyKind.Whistler when _models.ContainsKey("whistler"):
                {
                    // The gap-dweller (GDD v1.2 §21, App. A.4; tools/blender/whistler.py), its feet on the rail between the
                    // cars (Enemy(e) drops it from the sim's gap point). Hidden, it's folded small under the bridge plate,
                    // breathing: there to be found by whoever looks down into the gap. Whistling (extra), the long arm
                    // shoots up out of the gap and yanks the cord; then it watches the gap's mouth, the head turning in
                    // jerks. Carrying someone off, it runs on all fours.
                    string clip = phase switch
                    {
                        SpinePhase.Dormant => "fold",
                        SpinePhase.Telegraph when extra > 0.5 => "whistle",
                        SpinePhase.Grab or SpinePhase.Punish or SpinePhase.BreakOff => "run",
                        _ => "watch",
                    };
                    return Draw(mesh, "whistler", clip, t, true, model, seed: 41);
                }
            case EnemyKind.Whistler:
                {
                    // (No model: the husk crouched small, running long and low.)
                    bool running = phase is SpinePhase.Grab or SpinePhase.Punish or SpinePhase.BreakOff;
                    var at = Matrix4x4.CreateScale(0.8f, 1.15f, 0.8f) * Matrix4x4.CreateRotationX(running ? -0.6f : 0) * model;
                    string clip = running ? "run" : "crouch_idle";
                    return Draw(mesh, "husk", clip, t * (running ? 1.6 : 0.5), true, at, variant: 2, seed: 41, adjust: (_, l) => l with { Colour = l.Colour * 0.5f })
                        || Draw(mesh, "crew", clip, t, true, at, variant: 2, seed: 41, adjust: (_, l) => l with { Colour = l.Colour * new Vector3(0.25f, 0.24f, 0.24f) });
                }
            case EnemyKind.TippyToesie when _models.ContainsKey("tippy_toesie"):
                {
                    // The starved thing on its points (GDD v1.2 §21, App. A.5; tools/blender/tippy_toesie.py). Stalking it
                    // tiptoes, a step and a long hold, a finger to where its mouth should be; on its victim it's bent over
                    // them from behind, its right hand over their mouth and its left on their shoulder, rocking them (the
                    // hands reach where they are: Enemy(e) set the prey); seen, it scuttles off and is gone. It's taller
                    // than the train was built for (2.45 m on its points; the doorway is 2.1, a car 2.75 under its roof):
                    // indoors it stoops, and through a door it folds right down and ducks (Room; note 110).
                    var prey = _prey;
                    var room = _room;
                    _prey = null;
                    _room = Room.Open;
                    switch (phase)
                    {
                        case SpinePhase.Dormant or SpinePhase.BreakOff:
                            return Draw(mesh, "tippy_toesie", "flee", t, true, Matrix4x4.CreateTranslation(0, 0, TippyFleeSpeed * (float)t) * model);
                        case SpinePhase.Grab or SpinePhase.Punish:
                            {
                                Action<Entry>? smother = null;
                                if (prey is { } p && Matrix4x4.Invert(model, out var toModel))
                                {
                                    var mouth = Vector3.Transform(p.At(0, PreyMouthY, -PreyMouthFore), toModel);
                                    var shoulder = Vector3.Transform(p.At(-0.2f, PreyMouthY - 0.18f, 0), toModel);
                                    smother = e =>
                                    {
                                        // The right arm round the side of their head, the elbow down and out; the left's too.
                                        Reach(e, "r", mouth, new Vector3(0.7f, -0.4f, 0.2f));
                                        Reach(e, "l", shoulder, new Vector3(-0.7f, -0.5f, 0.2f));
                                    };
                                }
                                return Draw(mesh, "tippy_toesie", "smother", t, true, model, smother);
                            }
                        case SpinePhase.Telegraph or SpinePhase.Commit:
                            return Draw(mesh, "tippy_toesie", room.Doorway ? "duck" : room.Indoors ? "stalk_stoop" : "stalk", t, true, model)
                                || Draw(mesh, "tippy_toesie", "stalk", t, true, model);
                        default:
                            return Draw(mesh, "tippy_toesie", room.Doorway ? "duck" : room.Indoors ? "stoop" : "wait", t, true, model)
                                || Draw(mesh, "tippy_toesie", "wait", t, true, model);
                    }
                }
            case EnemyKind.TippyToesie:
                {
                    // (No model: the husk at child height, walking at a crawl, heels up.)
                    var at = Matrix4x4.CreateScale(0.7f, 0.85f, 0.7f) * Matrix4x4.CreateTranslation(0, 0.08f, 0) * model;
                    bool holding = phase is SpinePhase.Grab or SpinePhase.Punish;
                    string clip = holding ? "idle" : phase == SpinePhase.BreakOff ? "run" : "walk";
                    return Draw(mesh, "husk", clip, t * (clip == "walk" ? 0.35 : 1), true, at, variant: 4, seed: 51, adjust: (_, l) => l with { Colour = l.Colour * 0.7f })
                        || Draw(mesh, "crew", clip, t, true, at, variant: 4, seed: 51, adjust: (_, l) => l with { Colour = l.Colour * new Vector3(0.3f, 0.28f, 0.28f) });
                }
            case EnemyKind.FireFlies when _models.ContainsKey("fire_fly"):
                {
                    if (phase is SpinePhase.Dormant or SpinePhase.Gone)
                        return true;
                    FireFlies(mesh, model, t, extra2);
                    return true;
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
            case EnemyKind.Ribbit when _models.ContainsKey("ribbit"):
                {
                    // The toad-rabbit (GDD v1.2 §21, App. A.6; tools/blender/ribbit.py). Nobody to go for, it sits, dead
                    // still but for its throat; after someone, it hops in the sim's bursts (Enemy(e) phases the clip to its
                    // id); lined up to strike (TELEGRAPH), it sits up tall and its sac swells; on them, mouth gaping, its
                    // tongue's out to them (drawn here, from its mouth to their chest) and it reels.
                    var prey = _prey;
                    _prey = null;
                    bool hunting = extra >= 0;
                    switch (phase)
                    {
                        case SpinePhase.Telegraph:
                            return Draw(mesh, "ribbit", "swell", t, true, model, seed: (float)extra2);
                        case SpinePhase.Commit or SpinePhase.Grab or SpinePhase.Punish:
                            {
                                Vector3? mouth = null;
                                var at = model;
                                bool drawn = Draw(mesh, "ribbit", "tongue", t, true, at, e =>
                                {
                                    int jaw = e.Model.Skeleton.IndexOf("tongue_02");
                                    if (jaw >= 0)
                                        mouth = Vector3.Transform(e.Pose.World[jaw].Translation, at);
                                }, seed: (float)extra2);
                                if (drawn && mouth is { } a && prey is { } p)
                                    Tongue(mesh, a, p.Feet + Vector3.UnitY * RibbitTongueAt, (float)t);
                                return drawn;
                            }
                        default:
                            return Draw(mesh, "ribbit", hunting && phase == SpinePhase.Dormant ? "hop" : "sit", t, true, model, seed: (float)extra2);
                    }
                }
            case EnemyKind.Ribbit:
                {
                    // (No model: the hound's body squat and wide, mottled olive.) Lined up to strike it
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
            case EnemyKind.Grumbler when _models.ContainsKey("grumbler"):
                {
                    // The Grumbler (GDD v1.2 §21, App. A.8; tools/blender/grumbler.py): a dock labourer gone face-down like a
                    // spider, the elbows and knees up over its back, a second pair of arms out through its ribs. On the
                    // crates (and aboard, eating the cargo) it gnaws, its head down in one. Hit, it's feral (extra2): it
                    // rears up (the telegraph), then scuttles after them and bites when it's on them, and on one it's beaten
                    // down it mauls.
                    float pace = _pace;
                    _pace = 0;
                    bool feral = extra2 > 0.5;
                    string clip = phase switch
                    {
                        SpinePhase.Grab or SpinePhase.Punish => "maul",
                        SpinePhase.Commit => pace > Going ? "scuttle" : "bite",
                        SpinePhase.BreakOff => "scuttle",
                        _ => feral ? "bite" : "gnaw",
                    };
                    return Draw(mesh, "grumbler", clip, t, true, model, seed: 61);
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
            case EnemyKind.Choir when _models.ContainsKey("choir"):
                {
                    // One of the Choir's ghosts (GDD v1.2 §21, App. A.7; tools/blender/choir.py): a choir child in a filthy
                    // surplice, legless, its strips streaming, its jaw dropped in a silent O. The voices arriving (TELEGRAPH)
                    // it drifts, circling, singing; after someone (COMMIT, extra its target) it swoops at them, and with
                    // nobody to take it beats on the shut doors; seizing (GRAB) it's wrapped round its catch's head. It
                    // bobs; the faint cold about it is all the light it has (§26: not neon).
                    _prey = null;
                    var at = Matrix4x4.CreateTranslation(0, (float)(0.12 * Math.Sin(t * 2.3 + extra2)), 0) * model;
                    string clip = phase switch
                    {
                        SpinePhase.Commit when extra >= 0 => "swoop",
                        SpinePhase.Commit => "besiege",
                        SpinePhase.Grab or SpinePhase.Punish => "seize",
                        _ => "drift",
                    };
                    bool drawn = Draw(mesh, "choir", clip, t, true, at, seed: 71 + (float)extra2);
                    if (drawn)
                        mesh.PointLights.Add(new PointLight(model.Translation + new Vector3(0, 0.8f, 0), Palette.BlueGrey * ChoirCold, 2.5f));
                    return drawn;
                }
            case EnemyKind.Choir:
                {
                    // (No model: the Hollow's figure, child-sized and pale, bobbing in the air with a cold light of its own.)
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
    /// <param name="bite">A Car Hugger's car's (Art/BiteKit): its head goes in as far as it's eaten.</param>
    /// <param name="pace">How fast it's been going (m/s; GreyboxScene eases it from frame to frame): a Gaunt lopes after
    /// its waker while it's going and stands over them while it's not.</param>
    /// <summary>
    /// The Fire Flies at a lit lamp (GDD v1.2 §21, App. A.5 SWARM): moths, come in out of the dark to it. At first a few,
    /// beating round it in loops, blundering into the glass; the longer they linger the more of them, and more of those
    /// settle on the glass, wings folded (the eyes on them looking out), crawling, till the lamp's a cluster of them and
    /// its light comes through them orange: the TELEGRAPH's glow. Each its own way round and at its own beat (by its
    /// index and the swarm's seed), so no two keep time. <paramref name="lamp"/>: the flame, in its car's basis.
    /// </summary>
    void FireFlies(MeshBuilder mesh, in Matrix4x4 lamp, double t, double seed)
    {
        var (r, u, b) = Basis(lamp);
        var o = lamp.Translation;
        float fill = Math.Clamp((float)t / FireFlySwarmFills, 0, 1);
        int count = (int)MathF.Round(5 + (FireFlyMost - 5) * fill);
        // The settled share grows faster than the swarm: by the time it's full, most are on the glass.
        int settled = (int)MathF.Round(count * (0.15f + 0.6f * MathF.Sqrt(fill)));
        for (int i = 0; i < count; i++)
        {
            float k = (float)((i * 0.6180339 + seed * 0.37) % 1);
            float k2 = (float)((i * 0.7548777 + seed * 0.11) % 1);
            float size = 0.85f + 0.3f * k2;
            Vector3 at, up, fwd;
            string clip;
            if (i < settled)
            {
                // On the glass: round it at its own angle and height, its back out, its head up and to one side; it
                // creeps (a little way round every so often, as its settle clip lurches).
                double creep = Math.Floor((t + k * 3.2) / 3.2) * 0.12 * (k2 - 0.5);
                double a = k * Math.PI * 2 + creep;
                float h = -FireFlyGlassBelow + (FireFlyGlassBelow + FireFlyGlassAbove) * k2;
                var radial = r * (float)Math.Cos(a) + b * (float)Math.Sin(a);
                var round = Vector3.Cross(u, radial);
                float tilt = (k - 0.5f) * 1.4f;
                at = o + radial * FireFlyGlass + u * h;
                up = radial;
                fwd = Vector3.Normalize(u * MathF.Cos(tilt) + round * MathF.Sin(tilt));
                clip = "settle";
            }
            else
            {
                // On the wing: a loop round the lamp, in and out and up and down at its own rates, now and then dashing
                // in at the glass; facing the way it's going.
                double w = (0.9 + 0.8 * k) * (k2 < 0.5 ? 1 : -1);
                Vector3 Where(double s)
                {
                    double a = k * Math.PI * 2 + s * w;
                    double bash = Math.Pow(Math.Max(0, Math.Sin(s * (1.3 + k2) + k * 9)), 8);
                    float rad = FireFlyGlass + 0.03f + (FireFlyOrbit * (0.4f + 0.6f * k2) - 0.03f) * (float)(0.7 + 0.3 * Math.Sin(s * 2.1 + k * 5)) * (float)(1 - 0.9 * bash);
                    float h = (float)(0.12 * Math.Sin(s * 1.7 + k * 4) + 0.06 * Math.Sin(s * 4.3 + k2 * 7));
                    return o + (r * (float)Math.Cos(a) + b * (float)Math.Sin(a)) * rad + u * h;
                }
                at = Where(t);
                var v = Where(t + 0.05) - at;
                fwd = v.LengthSquared() > 1e-10f ? Vector3.Normalize(v) : r;
                up = Vector3.Normalize(u - fwd * Vector3.Dot(u, fwd));
                clip = "flutter";
            }
            // The model faces -Z with its back +Y: its rows are where X, Y and Z go.
            var z = -fwd;
            var x = Vector3.Cross(up, z);
            var m = new Matrix4x4(x.X, x.Y, x.Z, 0, up.X, up.Y, up.Z, 0, z.X, z.Y, z.Z, 0, at.X, at.Y, at.Z, 1);
            Draw(mesh, "fire_fly", clip, t + k * 7.3, true, Matrix4x4.CreateScale(size) * m, seed: i + (float)seed);
        }
        // The lamp's light through them: brighter, and redder, the more there are.
        float flick = 0.85f + 0.15f * MathF.Sin((float)t * 13 + 1.7f) * MathF.Sin((float)t * 5.3f);
        mesh.PointLights.Add(new PointLight(o, Palette.FurnaceOrange * (0.25f + 0.9f * fill) * flick, 2.5f + 2.5f * fill));
    }

    public bool Enemy(MeshBuilder mesh, in Matrix4x4 model, Enemy e, Bite bite = default, Prey? prey = null, Room? room = null, float pace = 0)
    {
        var m = model;
        switch (e.Kind)
        {
            case EnemyKind.Choir when _models.ContainsKey("choir"):
                {
                    // Swooping, it faces who it's after (the loose basis faces the train: at the doors, it faces them).
                    if (prey is { } p && e.Phase is SpinePhase.Commit or SpinePhase.Grab)
                    {
                        var (r, _, b) = Basis(model);
                        var to = p.Feet - model.Translation;
                        float x = Vector3.Dot(to, r), z = Vector3.Dot(to, b);
                        if (x * x + z * z > 1e-6f)
                        {
                            var at = model.Translation;
                            m = Matrix4x4.CreateRotationY(MathF.Atan2(-x, -z)) * model;
                            m.Translation = at;
                        }
                    }
                    return Enemy(mesh, m, e.Kind, e.Phase, e.PhaseSeconds, e.Extra, e.Health, aboard: false, extra2: e.Id);
                }
            case EnemyKind.Passenger when _models.ContainsKey("passenger"):
                // Facing as it goes (Extra2, in its car's frame: the crew's own reading, GreyboxScene.AsCrewmate).
                m = Matrix4x4.CreateRotationY((float)e.Extra2) * model;
                _pace = pace;
                break;
            case EnemyKind.FireFlies:
                // Each swarm its own way round its lamp (by its id).
                return Enemy(mesh, m, e.Kind, e.Phase, e.PhaseSeconds, e.Extra, e.Health, aboard: true, extra2: e.Id);
            case EnemyKind.Stoker when _models.ContainsKey("stoker") && FireDoorOpen is { } door:
                // At the open door, looking out of it into the cab (the model faces −Z: turned to the engine's +Z, back).
                m = Matrix4x4.CreateRotationY(MathF.PI) * Matrix4x4.CreateTranslation(door) * model;
                break;
            case EnemyKind.Follower when _models.ContainsKey("follower") && prey is { } carrier && e.Phase is SpinePhase.Dormant or SpinePhase.Telegraph:
                {
                    // Riding: flat between its carrier's shoulder blades, its palm to them and its fingers up (the model's
                    // −Y into their back, its −Z, the fingers' way, up), where their friends can see it and they can't.
                    var r = carrier.Right;
                    var f = carrier.Forward;
                    var o = carrier.At(0, FollowerUp, FollowerBack);
                    m = new Matrix4x4(r.X, r.Y, r.Z, 0, -f.X, -f.Y, -f.Z, 0, 0, -1, 0, 0, o.X, o.Y, o.Z, 1);
                    break;
                }
            case EnemyKind.Grumbler when _models.ContainsKey("grumbler"):
                {
                    // Feral, it faces who it's after (the nearest of the crew: who hit it isn't sent to clients), and it
                    // scuttles or bites as fast as it's been going (GreyboxScene's pace).
                    if (prey is { } p && e.Phase is SpinePhase.Commit or SpinePhase.Grab or SpinePhase.Punish)
                        m = Facing(model, p.Feet);
                    _pace = pace;
                    break;
                }
            case EnemyKind.Gaunt when _models.ContainsKey("gaunt"):
                {
                    // Woken, it faces its waker (the loose basis faces the train); aboard, it goes as the room lets it (note
                    // 118); and it goes as fast as it's been going (GreyboxScene's pace).
                    if (prey is { } p && e.Phase is not (SpinePhase.Dormant or SpinePhase.Alert))
                        m = Facing(model, p.Feet);
                    _room = room ?? Room.Open;
                    _pace = pace;
                    break;
                }
            case EnemyKind.Ribbit when _models.ContainsKey("ribbit"):
                {
                    // It faces who the pack's after; hopping, its leap is in step with the sim's (Ribbit.Hop: a leap on
                    // the half seconds its id puts it on).
                    if (prey is { } p)
                    {
                        var (r, _, b) = Basis(model);
                        var to = p.Feet - model.Translation;
                        float x = Vector3.Dot(to, r), z = Vector3.Dot(to, b);
                        if (x * x + z * z > 1e-6f)
                        {
                            var at = model.Translation;
                            m = Matrix4x4.CreateRotationY(MathF.Atan2(-x, -z)) * model;
                            m.Translation = at;
                        }
                        _prey = p;
                    }
                    return Enemy(mesh, Matrix4x4.CreateScale(RibbitScale) * m, e.Kind, e.Phase, e.PhaseSeconds + (e.Id & 1) * 0.5, e.Extra, e.Health,
                        aboard: false, extra2: e.Id);
                }
            case EnemyKind.Whistler when _models.ContainsKey("whistler"):
                {
                    // In its gap its origin is the sim's gap point, over the rail: its feet go on the rail, and it faces
                    // whoever's looking (the eye: the model's translation is from it), so the one who checks the gap finds
                    // its face turned up to them. Carrying someone, it runs away from the train (the loose basis faces it).
                    if (e.Attached < 0)
                    {
                        m = Matrix4x4.CreateRotationY(MathF.PI) * model;
                        break;
                    }
                    m = Matrix4x4.CreateTranslation(0, -(float)e.Local.Y, 0) * model;
                    if (Matrix4x4.Invert(model, out var toModel))
                    {
                        var eye = Vector3.Transform(Vector3.Zero, toModel);
                        if (eye.X * eye.X + eye.Z * eye.Z > 1e-6f)
                            m = Matrix4x4.CreateRotationY(MathF.Atan2(-eye.X, -eye.Z)) * m;
                    }
                    break;
                }
            case EnemyKind.TippyToesie when _models.ContainsKey("tippy_toesie"):
                {
                    // Hidden between tries (the sim's Dormant): nowhere, but for the moment it's seen scuttling off from
                    // where it was (never placed yet: never seen).
                    if (e.Phase == SpinePhase.Dormant && (e.Local == default || e.PhaseSeconds >= TippyFleeShow))
                        return true;
                    _room = room ?? Room.Open;
                    var (r, _, b) = Basis(model);
                    if (prey is not { } p)
                    {
                        // Nobody to face, in a doorway: out through it, the way it's going.
                        if (_room.Doorway && _room.Through != default)
                            m = Matrix4x4.CreateRotationY(MathF.Atan2(-_room.Through.X, -_room.Through.Z)) * model;
                        break;
                    }
                    // On them: tight behind, facing the way they face. Else: facing them, wherever they are.
                    bool on = e.Phase is SpinePhase.Grab or SpinePhase.Punish;
                    var origin = on ? p.Feet - p.Forward * TippyBehind : model.Translation;
                    var to = on ? p.Forward : p.Feet - origin;
                    float x = Vector3.Dot(to, r), z = Vector3.Dot(to, b);
                    if (x * x + z * z < 1e-6f)
                        break;
                    // (CreateRotationY(a) takes the model's forward, -Z, to (-sin a, -cos a) in its basis.)
                    m = Matrix4x4.CreateRotationY(MathF.Atan2(-x, -z)) * model;
                    m.Translation = origin;
                    _prey = p;
                    break;
                }
            case EnemyKind.CarHugger when bite.Any:
                m = Matrix4x4.CreateTranslation(0, 0, -bite.Advance) * model;
                _biteGrip = bite.Grip;
                break;
            case EnemyKind.Dragger when e.Local.X > 0:
                m = Matrix4x4.CreateRotationY(MathF.PI) * model;
                break;
            case EnemyKind.Climber when e.Phase == SpinePhase.Telegraph:
                // At the gap, facing in at the couplers.
                m = Matrix4x4.CreateRotationY(e.Local.X > 0 ? MathF.PI / 2 : -MathF.PI / 2) * model;
                break;
            case EnemyKind.Climber when e.Attached == Sim.Enemies.Enemy.Loose && e.Phase is SpinePhase.Dormant or SpinePhase.Alert or SpinePhase.BreakOff:
                // Pacing the train on its side of the line (extra2, −1 or +1): running along it, not at it (the loose basis
                // faces the train; its right is the train's way, ahead, on the +1 side).
                m = Matrix4x4.CreateRotationY(-MathF.Sign((float)e.Extra2 == 0 ? 1 : (float)e.Extra2) * MathF.PI / 2) * model;
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
