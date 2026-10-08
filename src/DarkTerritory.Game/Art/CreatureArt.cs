using System.Numerics;
using Ballast;
using Ballast.Assets;
using Ballast.Render;
using DarkTerritory.Sim.Enemies;

namespace DarkTerritory.Game.Art;

/// <summary>Who a crewmate is (GDD App. D.8): crew, or freed from a prison car or a halt lockup (a prisoner) or a shelter (a wildlander).</summary>
public enum Survivor : byte { None, Prisoner, Wildlander }

/// <summary>What a crewmate is doing, for which clip their model plays (the actions: tools/blender/crew_clips.py, note 145).</summary>
public enum CrewPose
{
    Idle, Walk, Run, Climb, Shovel, Crouch, Dead, Carry, CarryWalk, Drag, Door, Handbrake, Hatch, Uncouple, Vent, Lever, Push, Held, Gunner, Fall, Swing, Mend, Gap, Extinguish, Lantern, LanternWalk, Haul,
    HaulUp, GapStep, Drive, Whistle, Smash, Pry, Pick, GetUp, TakeDown,
    // Running under stress; a blow taken; a ground switch lever, the coaling chute's, a spout; up a ladder with a body.
    Hurry, Stagger, Throw, Chute, Spout, ClimbCarry,
    // A body over the shoulder, stood and walking (App. C.4).
    Shoulder, ShoulderWalk,
    // The child in the arms, stood and walking (App. C.4).
    Cradle, CradleWalk,
    // The firehole's door hauled open or shut (GDD §12).
    FireDoor,
    // Held, one per GRAB (App. A.1): by a Dragger, a Car Hugger, the Whistler, Tippy Toesie, Ribbits, a Soot Child, the Choir, the Passenger.
    HeldHang, HeldMouth, HeldCarried, HeldCover, HeldFrozen, HeldPinned, HeldSeized, HeldDragged,
    // At the cannon's breech from the seat (note 137): played by the reload's progress, not a clock.
    Reload,
    // The extinguisher at work on a fire, braced, kicking (App. C.5); hung back on its bracket (SceneArt.Crewmate).
    Spray, HangUp,
    // Emotes (GDD §9's yard, note 298): a dance, a wave, a point. Clips of their own when crew_clips has them ("dance",
    // "wave", "point"); until then posed by the arms' IK over a stepping or standing clip (CreatureArt.EmoteArms).
    Dance, Wave, Point,
    // Off the roof on a jump, rising, the leap between cars (the checklist's crew-gap: "a jump between roofs"); stood on a
    // car straining on a bend taken too fast, fighting for footing (App. F.1's overspeed telegraph; note 375).
    Jump, Stumble,
    // A friend hauled free by what has them (App. A.1's rescue, note 378): out of the Car Hugger's mouth, the Tippy Toesie's
    // fingers prised off, hauled down from the Whistler or the Choir.
    PullMouth, PryOff, HaulDown,
}

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
        "track_doll", "car_hugger", "tippy_toesie", "whistler", "ribbit", "choir", "gaunt", "grumbler", "stoker", "follower", "climber", "fire_fly", "passenger",
        "survivor_prisoner", "survivor_wildlander", "sheep", "moose", "gannet"];

    /// <summary>
    /// The figure a crewmate plays as (GDD App. D.8): the crew's own, or, freed from a Holdout, its occupant's for the rest
    /// of the run (tools/models/recipes/survivor_*: the crew figure redressed, on the crew's rig and clips).
    /// </summary>
    public static string FigureOf(Survivor survivor) => survivor switch
    {
        Survivor.Prisoner => "survivor_prisoner",
        Survivor.Wildlander => "survivor_wildlander",
        _ => "crew",
    };

    /// <summary>How high the Follower's nest's hollow is, full grown (tools/models/recipes/follower_nest.py), where it sits.</summary>
    const float FollowerNestTop = 0.6f;

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

    // A Ribbit's tongue goes to its catch's chest (a crewmate's 1.8 m, tools/blender/crew.py), this high (m).
    const float RibbitTongueAt = 1.15f;

    // The cold about a Choir ghost: a faint light, the colour of its skin, so they're seen at night but never glow (§26).
    const float ChoirCold = 0.25f;

    /// <summary>How close to its frozen catch a Ribbit is on them, devouring (m): the sim's hop stops 0.8 m short.</summary>
    public const float RibbitDevourReach = 1.2f;

    /// <summary>
    /// The clip a Ribbit on the attack plays, <paramref name="near"/> metres from the one its pack is after: its tongue out at
    /// them (COMMIT), then, with them frozen (GRAB), creeping in on them low on the sim's quarter-speed hop, and close to, on
    /// them, devouring (App. A.6).
    /// </summary>
    public static string RibbitClip(SpinePhase phase, float near) =>
        phase is SpinePhase.Grab or SpinePhase.Punish ? near > RibbitDevourReach ? "creep" : "devour" : "tongue";

    /// <summary>The toy the Track Doll being drawn has in its hand, or null (GreyboxScene: the one it was given, as it goes).</summary>
    public MeshAsset? DollHolding { get; set; }

    /// <summary>How long one of the Choir's ghosts is seen going when the swarm's driven off (GreyboxScene.Leaving).</summary>
    public const double ChoirLeaveSeconds = 3.0;

    // "Giant toad-rabbits" (GDD §21): the model's a big dog's size, drawn this much bigger (its head at a crewmate's waist).
    const float RibbitScale = 1.4f;

    /// <summary>
    /// The Ribbit's own tongue (tools/blender/ribbit.py: <c>tongue_01</c> its length, <c>tongue_02</c> the club at its end),
    /// out of its mouth to its catch's chest at <paramref name="target"/> (model space; GDD App. A.6 TONGUE): turned on its
    /// root to point at them, then stretched along itself until the club's on them, the club carried out unstretched. With
    /// nobody to reach (<paramref name="target"/> null), it's drawn back in to the length it lies in the mouth.
    /// </summary>
    static void Lash(Entry e, Vector3? target)
    {
        var sk = e.Model.Skeleton;
        int root = sk.IndexOf("tongue_01"), club = sk.IndexOf("tongue_02");
        if (root < 0 || club < 0)
            return;
        var pose = e.Pose;
        if (target is { } aim)
            Skinner.Aim(e.Model, pose, root, club, aim);
        var head = pose.World[root].Translation;
        var along = pose.World[club].Translation - head;
        float now = along.Length();
        if (now < 1e-5f)
            return;
        var d = along / now;
        // Its length at rest: the club's head from the root's in the bind pose.
        float rest = Vector3.Distance(Inverse(sk.InverseBind[club]).Translation, Inverse(sk.InverseBind[root]).Translation);
        float want = target is { } t ? Vector3.Distance(head, t) : rest;
        float k = want / now;
        // Stretch along d about the root: x -> x + (k-1)(x-head).d d (row vectors, so x * M).
        float s = k - 1;
        var outer = Matrix4x4.Identity + new Matrix4x4(s * d.X * d.X, s * d.X * d.Y, s * d.X * d.Z, 0, s * d.Y * d.X, s * d.Y * d.Y,
            s * d.Y * d.Z, 0, s * d.Z * d.X, s * d.Z * d.Y, s * d.Z * d.Z, 0, 0, 0, 0, 0);
        var stretch = Matrix4x4.CreateTranslation(-head) * outer * Matrix4x4.CreateTranslation(head);
        var carry = Matrix4x4.CreateTranslation(along * (k - 1));
        for (int b = 0; b < sk.Count; b++)
        {
            if (b == root)
                pose.World[b] *= stretch;
            else if (Below(sk, b, club))
                pose.World[b] *= carry;
            else
                continue;
            pose.Skin[b] = sk.InverseBind[b] * pose.World[b];
        }

        static Matrix4x4 Inverse(Matrix4x4 m) => Matrix4x4.Invert(m, out var i) ? i : Matrix4x4.Identity;
        static bool Below(Skeleton sk, int b, int ancestor)
        {
            for (; b >= 0; b = sk.Parents[b])
                if (b == ancestor)
                    return true;
            return false;
        }
    }
    Room _room;

    /// <summary>
    /// The firebox door, when it's open: its centre from the firebox's place (the engine's frame; Art/TrainKit.FireDoor),
    /// or null when it's shut. GreyboxScene sets it from the boiler each frame; a Stoker shows only through it.
    /// </summary>
    public Vector3? FireDoorOpen { get; set; }

    // Set by Enemy(e, pace) for the one draw it makes, as _room.
    float _pace;
    // Set by Enemy(e) for a Dragger in its grab's last moment: how far into dragging them under it is (s), or -1.
    double _draggedUnder = -1;
    // A Dragger drags its catch under over its grab's last this long (s: its drag clip).
    const double DraggerDragSeconds = 0.8;

    // A Gaunt or a Grumbler goes (lopes, crawls, scuttles) above this pace (m/s), and stands (listens, squats, bites) below
    // it. At each point of its anger a Gaunt leans in this much more of the way (all of it at the sim's default threshold,
    // enemies.json gaunt.attackAt), its neck let down this far (radians) and its head tipped over this far at the most.
    // A riding Follower is this high on its carrier and this far behind their middle (m: a crewmate's back, tools/blender/
    // crew.py); its nest swells it this much (the full nest, 1 + this times its size).
    const float FollowerUp = 1.35f, FollowerBack = 0.15f, FollowerSwell = 1.5f;

    // The Stoker's own fire, in its mouth and its splits: the sick green of a fire with it in (GreyboxScene.FireColour).
    static readonly Vector3 StokerFire = new(0.35f, 0.6f, 0.22f);
    // A Cinder Hound's own light (note 337): its cracks' ember, redder than a firebox, and how far it reaches.
    static readonly Vector3 HoundEmber = new Vector3(1.0f, 0.38f, 0.12f) * 2.4f;
    const float HoundLightRange = 4.2f;

    const float Going = 0.4f, GauntLeanPerAnger = 0.25f, GauntLean = 0.32f, GauntTilt = 0.6f;

    // Fire Flies round a car's lantern (tools/blender/fire_fly.py; the lantern's tools/models hand_lantern, its flame at the
    // model's origin): at most this many of them, the swarm filling as they linger (all of it by FireFlySwarmFills
    // seconds: the sim's ignite time, enemies.json fireFlies, as the GDD's ~20 s); the settled ones on the glass, a
    // cylinder this wide (m, to their bodies) and this far below and above the flame; the flying ones this far out.
    // The Passenger's walk and drag clips cover the ground at these paces (m/s: tools/blender/passenger.py's, the crew's
    // walk); dragging, it's this far ahead of the one it drags (m: they're at its feet in the sim).
    const float PassengerWalkPace = 1.4f, PassengerDragPace = 1.3f, PassengerStride = 0.8f;

    // A Soot Child gets onto the one it's taken in this long (s: its pin clip), then drinks; clinging, it's this far in
    // front of their feet (m).
    const float SootPinSeconds = 0.6f, SootCling = 0.3f;

    // The Switchman's lever turns on its stand's pivot here (m, in the model's space: by its right foot, at the top of the
    // stand's short post).
    static readonly Vector3 SwitchLeverPivot = new(0.34f, 0.32f, -0.3f);

    const int FireFlyMost = 22;
    // T131 (the director, build 1121: "what were the bubbles?"): the wings' smouldering rims, lit all round against the
    // lamp, drew each moth as a hollow orange ring: a bubble. The rims go to the charred paper (unlit), and the burning tail
    // carries the light instead, shedding FireFlySparks sparks of FireFlyEmber behind each on the wing.
    const int FireFlySparks = 3;
    static readonly Vector3 FireFlyEmber = new(1.0f, 0.55f, 0.18f);

    static MaterialLook FireFlyLook(ModelMaterial m, MaterialLook l) => m.Name switch
    {
        _ when m.Name.Contains("firefly_rim") => l with { Emissive = 0, Colour = new Vector3(0.13f, 0.11f, 0.1f) },
        _ when m.Name.StartsWith("ember_core") => l with { Emissive = Math.Max(l.Emissive, 1) * 1.6f },
        _ => l,
    };
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

    /// <summary>How long a Tippy Toesie pulled off or seen is held in its recoil before it scuttles (s): its clip's length.</summary>
    double TippyRecoil => _models.TryGetValue("tippy_toesie", out var tippy) && tippy.Model.Clips.TryGetValue("recoil", out var c) ? c.Duration : 0;

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
        /// <summary>The distance copy (&lt;name&gt;.lod1.glb), its joints renumbered to this model's: posed by this one's palette.</summary>
        public Entry? Lod { get; init; }
    }

    readonly Dictionary<string, Entry> _models = new();
    readonly Skinner _skinner = new();
    // The flipbook effects some of what's drawn here is made of (a car fire: Effects.CarFire).
    readonly Effects _fx;
    readonly float _texels;

    /// <param name="contentRoot">The content folder; by default the one <paramref name="look"/>'s textures came from.</param>
    public CreatureArt(Look look, string? contentRoot = null)
    {
        Look = look;
        _fx = new Effects(look);
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
            // Clips authored apart from the baked mesh (tools/blender/crew_clips.py: the crew's actions), on the same skeleton.
            // They win over the mesh's own clips of the same name: a clip reworked there (the crew's climb and shovel,
            // Look Review notes) needn't re-bake the mesh.
            var extra = Path.Combine(ContentRoot, Folder, name + "_clips.glb");
            if (File.Exists(extra))
                model = ModelLoader.WithClips(model, ModelLoader.Load(extra), replace: true);
            // The survivors are the crew figure redressed: the crew's actions are theirs too.
            var crewClips = Path.Combine(ContentRoot, Folder, "crew_clips.glb");
            if (name.StartsWith("survivor_", StringComparison.Ordinal) && File.Exists(crewClips))
                model = ModelLoader.WithClips(model, ModelLoader.Load(crewClips), replace: true);
            float wear = WearOf.GetValueOrDefault(name, 0.5f);
            _models[name] = new Entry(model, [.. model.Materials.Select(m => Resolve(m, wear))])
            {
                Lod = LoadLod(Path.Combine(ContentRoot, Folder, name + ".lod1.glb"), model) is { } lod
                    ? new Entry(lod, [.. lod.Materials.Select(m => Resolve(m, wear))]) : null,
            };
        }
    }

    public Look Look { get; }
    public string ContentRoot { get; }

    /// <summary>A model's distance copy (tools/models overbake `lod`), its joints renumbered by name to the full model's so
    /// the full model's pose skins it; null when there's none or a bone of it isn't the full model's.</summary>
    static Model? LoadLod(string path, Model full)
    {
        if (!File.Exists(path))
            return null;
        var lod = ModelLoader.Load(path);
        var map = new int[lod.Skeleton.Count];
        for (int b = 0; b < map.Length; b++)
            if ((map[b] = full.Skeleton.IndexOf(lod.Skeleton.Names[b])) < 0)
                return null;
        foreach (var part in lod.Parts)
            for (int i = 0; i < part.Joints.Length; i++)
                part.Joints[i] = map[part.Joints[i]];
        return lod;
    }

    /// <summary>The distance copy of a loaded model, or null (what the far draw is).</summary>
    public Model? LodOf(string name) => _models.TryGetValue(name, out var m) ? m.Lod?.Model : null;

    /// <summary>A Cinder Hound's board onto the rear car (tools/blender/cinder_hound.py "board", 33 frames at 30).</summary>
    const double HoundBoardSeconds = 1.1;
    Sim.Enemies.WhistlerTuning? _whistler;
    Sim.Enemies.CarFireTuning? _carFire;
    /// <summary>The car fire's tuning, as the content has it (its grid's cell size, its spread clock; note 267).</summary>
    public Sim.Enemies.CarFireTuning CarFire => _carFire ??= DataFile.Load<Sim.Enemies.EnemyTuning>(Path.Combine(ContentRoot, Sim.Enemies.EnemyTuning.File)).CarFire;
    readonly Dictionary<int, MeshAsset> _debris = new();
    MeshAsset? _fallenPine;
    MeshAsset[]? _reeds;

    /// <summary>True when every model is there.</summary>
    public bool Loaded => Names.All(_models.ContainsKey);

    /// <summary>A loaded model by name, or null.</summary>
    public Model? Get(string name) => _models.TryGetValue(name, out var m) ? m.Model : null;

    /// <summary>
    /// Where a model's joints are, in its own frame, at <paramref name="time"/> into a clip: what `dt art reel` frames a
    /// clip on (the skin's posed on the GPU, so the bones are what the CPU has). Empty when the model or clip isn't there.
    /// </summary>
    public IEnumerable<Vector3> Joints(string name, string clip, double time, bool loop)
    {
        if (!_models.TryGetValue(name, out var m) || !m.Model.Clips.TryGetValue(clip, out var c))
            yield break;
        _skinner.Evaluate(m.Model, c, time, loop, m.Pose);
        foreach (var w in m.Pose.World)
            yield return w.Translation;
    }

    /// <summary>A material's texture layer when the look has it; its flat colour (and its glow, if it has one) when not.</summary>
    /// <summary>
    /// The Drift seen in the reeds (GDD §22: "something in the reeds surges toward motion"): round the mass's edge on the
    /// ground beside the train a ring of reeds bowed outward, shoved aside by what's coming up under them, dark at the
    /// root. Spreading, they stir; surging, they thrash and lie flatter, a wave you can watch come.
    /// </summary>
    void DriftReeds(MeshBuilder mesh, Matrix4x4 model, float radius, double t, bool surging)
    {
        _reeds ??= [SettingKit.Reeds(Look, 0), SettingKit.Reeds(Look, 1), SettingKit.Reeds(Look, 2)];
        // Two rows: the crest of the wave and, a step behind it, what it's already flattened.
        int n = 16 + (int)(radius * 5);
        for (int i = 0; i < 2 * n; i++)
        {
            bool crest = i < n;
            float ring = radius + (crest ? 0.9f : -0.4f) + 0.35f * ((i * 5) % 3);
            float a = i * MathF.Tau / n + (crest ? 0 : MathF.PI / n) + 0.13f * (i % 3);
            float x = MathF.Cos(a) * ring, z = MathF.Sin(a) * ring;
            // Only the ground beside the train: the roof's edge is where the reeds start.
            if (MathF.Abs(x) <= CarHalfWidth + 0.6f)
                continue;
            double phase = t * (surging ? 7.5 : 1.6) + i * 1.7;
            float lean = (surging ? 0.75f + 0.25f * (float)Math.Sin(phase) : 0.18f + 0.1f * (float)Math.Sin(phase)) * (crest ? 1 : 1.6f);
            float twist = 0.3f * (float)Math.Sin(phase * 0.7 + i);
            // Bowed away from the centre: tip outward about the tangent of the ring.
            var outward = Vector3.Normalize(new Vector3(x, 0, z));
            var axis = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, outward));
            float size = 1.1f + 0.4f * ((i * 7) % 5) / 4f;
            var at = Matrix4x4.CreateScale(size, size * (surging ? 0.8f : 1f), size) * Matrix4x4.CreateRotationY(twist)
                * Matrix4x4.CreateFromAxisAngle(axis, lean) * Matrix4x4.CreateTranslation(x, -RoofDrop, z) * model;
            mesh.Append(_reeds[i % 3], at);
        }
    }

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
        // Struck: its own hit clip over whatever it was doing, while that runs (Enemy's hitAge).
        if (_hit >= 0 && clip != "hit" && m.Model.Clips.TryGetValue("hit", out var hit) && _hit < hit.Duration)
            (clip, c, time, loop) = ("hit", hit, _hit, false);
        // Dead: its own fall where it has one (the Gannet's crash across the roof), played from the blow and held at its end;
        // else held at the end of its flinch, or still where it was.
        if (_dying)
            (clip, c, time, loop) = m.Model.Clips.TryGetValue("death", out var fall)
                ? ("death", fall, Math.Min(Math.Max(_hit, 0), fall.Duration - 1e-3), false)
                : m.Model.Clips.TryGetValue("hit", out var last)
                ? ("hit", last, Math.Min(Math.Max(_hit, 0), last.Duration - 1e-3), false)
                : (clip, c, 0, false);
        _skinner.Evaluate(m.Model, c, time, loop, m.Pose);
        posed?.Invoke(m);
        // Far from the eye (the scene is built about it: `at`'s origin is the distance), its distance copy, on this pose.
        float lod = Look.Tuning.CreatureLodMetres;
        var drawn = m.Lod is { } far && lod > 0 && at.Translation.LengthSquared() > lod * lod ? far : m;
        Emit(mesh, drawn, clip, at, variant, glow, seed, adjust, skin: m.Pose.Skin);
        return true;
    }

    /// <summary>
    /// Draws a model in the pose it was last given: its bind pose, cooked once per variant and look (<see cref="Bound"/>),
    /// posed on the GPU by the pose's palette. <paramref name="glow"/> goes to the instance (the shader scales the lights
    /// by it), so a pulsing ember doesn't need an asset a frame.
    /// </summary>
    void Emit(MeshBuilder mesh, Entry m, string? clip, in Matrix4x4 at, int variant, float glow, float seed,
        Func<ModelMaterial, MaterialLook, MaterialLook>? adjust = null, bool arms = false, Matrix4x4[]? skin = null)
    {
        var mats = m.Model.Materials;
        for (int i = 0; i < mats.Length; i++)
            m.Scratch[i] = adjust is null ? m.Looks[i] : adjust(mats[i], m.Looks[i]);
        var seedOffset = new Vector3(MathF.Sin(seed * 12.9898f), MathF.Sin(seed * 78.233f), MathF.Sin(seed * 37.719f)) * 97;
        var settings = new EmitSettings(variant % Math.Max(1, m.Model.VariantCount), clip, _texels, seedOffset);
        mesh.Skinned(Bound(m, settings, arms), at, skin ?? m.Pose.Skin, glow, seedOffset * _texels);
    }

    // The bind-pose assets, by model, the parts drawn and the looks (keyed to 1/128th: a hull heating as it's drilled
    // steps through its heat, it doesn't make an asset a frame). Cleared when it grows past a few hundred; the renderer
    // frees what's dropped.
    readonly Dictionary<(Model Model, ulong Parts, int Looks), (MaterialLook[] Looks, MeshAsset Asset)> _bound = new();
    const int MaxBound = 384;

    MeshAsset Bound(Entry m, in EmitSettings settings, bool arms = false)
    {
        ulong parts = 0;
        for (int i = 0; i < m.Model.Parts.Length; i++)
            if (m.Model.Parts[i].DrawnFor(settings.Variant, settings.Clip))
                parts |= 1ul << (i % 64);
        var hash = new HashCode();
        foreach (var l in m.Scratch)
            hash.Add(Quantised(l));
        hash.Add(arms);
        var key = (m.Model, parts, hash.ToHashCode());
        if (_bound.TryGetValue(key, out var hit) && Same(hit.Looks, m.Scratch))
            return hit.Asset;
        if (_bound.Count >= MaxBound)
            _bound.Clear();
        var asset = Skinner.Bind(m.Model, m.Scratch, settings, m.Model.Name);
        if (arms)
            asset = ArmsOf(m.Model, asset);
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
    /// <summary>A square iron bar from <paramref name="a"/> to <paramref name="b"/>, <paramref name="half"/> thick (a lever, a post).</summary>
    static void Bar(MeshBuilder mesh, Vector3 a, Vector3 b, float half, Vector3 colour)
    {
        var along = b - a;
        float length = along.Length();
        if (length < 1e-4f)
            return;
        var up = along / length;
        var side = Vector3.Normalize(Vector3.Cross(up, MathF.Abs(up.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX));
        var back = Vector3.Cross(side, up);
        mesh.Box((a + b) / 2, side, up, back, new Vector3(half, length / 2, half), colour);
    }

    Vector3 BoneAt(string name, string bone, in Matrix4x4 at) =>
        _models.TryGetValue(name, out var m) && m.Model.Skeleton.IndexOf(bone) >= 0
            ? Skinner.Socket(m.Model, m.Pose, bone, at).Translation
            : at.Translation;

    // ----------------------------------------------------------------------------------------------------------------
    // The crew

    /// <summary>The crew clip (tools/blender/crew_clips.py, crew.py) a pose plays.</summary>
    public static string ClipOf(CrewPose pose) => pose switch
    {
        CrewPose.Dance => "dance",
        CrewPose.Wave => "wave",
        CrewPose.Point => "point",
        CrewPose.Walk => "walk",
        CrewPose.Run => "run",
        CrewPose.Climb => "climb",
        CrewPose.Shovel => "shovel",
        CrewPose.Crouch => "crouch_idle",
        CrewPose.Dead => "dead",
        CrewPose.Carry => "carry",
        CrewPose.CarryWalk => "carry_walk",
        CrewPose.Drag => "drag",
        CrewPose.Door => "door",
        CrewPose.Handbrake => "handbrake",
        CrewPose.Hatch => "hatch",
        CrewPose.Uncouple => "uncouple",
        CrewPose.Vent => "vent",
        CrewPose.Lever => "lever",
        CrewPose.Push => "push",
        CrewPose.Held => "held",
        CrewPose.Gunner => "gunner",
        CrewPose.Fall => "fall",
        CrewPose.Swing => "swing",
        CrewPose.Mend => "mend",
        CrewPose.Gap => "gap",
        CrewPose.Extinguish => "extinguish",
        CrewPose.Lantern => "lantern",
        CrewPose.LanternWalk => "lantern_walk",
        CrewPose.Haul => "haul",
        CrewPose.HaulUp => "haul_up",
        CrewPose.GapStep => "gap_step",
        CrewPose.Drive => "drive",
        CrewPose.Whistle => "whistle",
        CrewPose.Smash => "smash",
        CrewPose.Pry => "pry",
        CrewPose.Pick => "pick",
        CrewPose.GetUp => "getup",
        CrewPose.TakeDown => "take_down",
        CrewPose.Spray => "spray",
        CrewPose.HangUp => "hang_up",
        CrewPose.Hurry => "hurry",
        CrewPose.Stagger => "stagger",
        CrewPose.Throw => "throw",
        CrewPose.Chute => "chute",
        CrewPose.Spout => "spout",
        CrewPose.ClimbCarry => "climb_carry",
        CrewPose.Shoulder => "shoulder",
        CrewPose.ShoulderWalk => "shoulder_walk",
        CrewPose.Cradle => "cradle",
        CrewPose.CradleWalk => "cradle_walk",
        CrewPose.FireDoor => "firedoor",
        CrewPose.HeldHang => "held_hang",
        CrewPose.HeldMouth => "held_mouth",
        CrewPose.HeldCarried => "held_carried",
        CrewPose.HeldCover => "held_cover",
        CrewPose.HeldFrozen => "held_frozen",
        CrewPose.HeldPinned => "held_pinned",
        CrewPose.HeldSeized => "held_seized",
        CrewPose.HeldDragged => "held_dragged",
        CrewPose.Reload => "reload",
        CrewPose.Jump => "jump",
        CrewPose.Stumble => "stumble",
        CrewPose.PullMouth => "pull_mouth",
        CrewPose.PryOff => "pry_off",
        CrewPose.HaulDown => "haul_down",
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
    /// <param name="inHand">The tool in their right fist (T108's hotbar; tools/models hand_tools), or null for empty hands.
    /// Put away for the two-handed work (it would be through the crate, the wheel, the gun).</param>
    /// <param name="hanging">Something hung from the right fist by its ring (the hand lamp, tools/models hand_lantern: its
    /// origin at its foot, the ring <see cref="LanternRing"/> over it), upright whatever the wrist does, so it swings with
    /// the hand; where its flame is then is <see cref="LastHanging"/>.</param>
    /// <param name="figure">Whose figure: <see cref="FigureOf"/> (the crew's, or a freed survivor's); the crew's where it isn't built.</param>
    /// <param name="body">A headset player's body under their head (T82, <see cref="VrBody"/>): the clip's figure turned to
    /// the hips, leant, crouched and twisted up the spine to the head, the feet where they're planted. Before the arms, so
    /// the hands are reached for from where the shoulders have gone.</param>
    public bool Crewmate(MeshBuilder mesh, in Matrix4x4 model, CrewPose pose, double time, int variant,
        Vector3? left = null, Vector3? right = null, Vector3 leftPole = default, Vector3 rightPole = default, MeshAsset? inHand = null,
        MeshAsset? hanging = null, string figure = "crew", VrBodyPose? body = null)
    {
        if (!_models.ContainsKey(figure))
            figure = "crew";
        // Each crewmate breathes and steps on their own beat: a fixed offset by variant, not a random one.
        double offset = (variant & 7) * 0.41;
        string clip = ClipOf(pose);
        // A build without crew_clips.glb (or an older one, short of a clip) stands them idle rather than in the greybox.
        if (_models.TryGetValue(figure, out var has) && !has.Model.Clips.ContainsKey(clip))
            clip = clip.StartsWith("held_", StringComparison.Ordinal) && has.Model.Clips.ContainsKey("held") ? "held"
                : clip == "hurry" && has.Model.Clips.ContainsKey("run") ? "run" : clip == "reload" && has.Model.Clips.ContainsKey("gunner") ? "gunner"
                : clip == "spray" && has.Model.Clips.ContainsKey("extinguish") ? "extinguish" : clip == "hang_up" && has.Model.Clips.ContainsKey("take_down") ? "take_down"
                // An emote with no clip of its own (note 298): the dance steps on the spot, the wave and the point stand.
                : clip == "dance" && has.Model.Clips.ContainsKey("walk") ? "walk" : "idle";
        // ... and the arms are posed over it (EmoteArms).
        bool emoteByHand = pose is CrewPose.Dance or CrewPose.Wave or CrewPose.Point && clip != ClipOf(pose);
        var Paint = PaintOf(variant);
        if (!_models.TryGetValue(figure, out var m) || !m.Model.Clips.TryGetValue(clip, out var c))
            return false;
        // Played once from their start (SceneArt passes the time since the act began): getting up, a thing off its bracket,
        // a blow of the tool in hand (note 275: without it a swing was put off by the variant's beat, up to 2.9 s into a
        // 0.8 s clip, and most of the crew were drawn at its end).
        bool fromStart = pose is CrewPose.GetUp or CrewPose.TakeDown or CrewPose.HangUp or CrewPose.Stagger or CrewPose.Reload or CrewPose.FireDoor
            or CrewPose.Swing or CrewPose.Jump;
        _skinner.Evaluate(m.Model, c, fromStart ? time : time + offset, pose is not (CrewPose.Dead or CrewPose.Swing) && !fromStart, m.Pose);
        if (body is { } vr)
            HeadsetBody(m, vr);
        if (emoteByHand)
            EmoteArms(m, pose, time);
        if (left is { } l)
            Reach(m, "l", l, leftPole);
        if (right is { } r)
            Reach(m, "r", r, rightPole);
        Emit(mesh, m, clip, model, variant, 1, variant, Paint);
        if (figure == "crew")
            Marks(mesh, m, model, variant);
        if (pose == CrewPose.Reload)
            ReloadKit(mesh, m, model, time);
        // Where their mouth is and which way they face, for their breath in the cold (SceneArt.Crewmate).
        int headBone = m.Model.Skeleton.IndexOf("head");
        var headAt = headBone >= 0 ? m.Pose.World[headBone].Translation : new Vector3(0, 1.6f, 0);
        LastMouth = Vector3.Transform(headAt + MouthOverHead, model);
        LastFacing = Vector3.Normalize(Vector3.TransformNormal(-Vector3.UnitZ, model));
        if (inHand is not null && OneHanded(pose))
            mesh.Append(inHand, ToolGrip * Skinner.Socket(m.Model, m.Pose, "hand_r_weapon", model));
        if (hanging is not null)
            Hung(mesh, m, hanging, model);
        return true;
    }

    /// <summary>
    /// Hangs <paramref name="hanging"/> from the right fist of the figure last drawn as <paramref name="figure"/> by
    /// <see cref="Draw(MeshBuilder, string, string, double, bool, in Matrix4x4, int, float, float, Func{ModelMaterial, MaterialLook, MaterialLook}?)"/>
    /// at <paramref name="model"/>, as <see cref="Crewmate"/>'s hanging does (a town's lamp-carriers, note 281); its flame
    /// is then <see cref="LastHanging"/>. False when that figure isn't built.
    /// </summary>
    public bool Hang(MeshBuilder mesh, MeshAsset hanging, in Matrix4x4 model, string figure = "crew")
    {
        if (!_models.TryGetValue(figure, out var m))
            return false;
        Hung(mesh, m, hanging, model);
        return true;
    }

    /// <summary>
    /// Appends <paramref name="piece"/>, made in the figure's bind pose (model space), riding <paramref name="bone"/> of the
    /// figure last drawn as <paramref name="figure"/> at <paramref name="model"/>: a townsperson's mask on their head, their
    /// bottle on their back (note 353). False when that figure or bone isn't built.
    /// </summary>
    public bool Wear(MeshBuilder mesh, MeshAsset piece, string bone, in Matrix4x4 model, string figure = "crew")
    {
        if (!_models.TryGetValue(figure, out var m) || m.Model.Skeleton.IndexOf(bone) is var b && b < 0)
            return false;
        mesh.Append(piece, m.Pose.Skin[b] * model);
        return true;
    }

    /// <summary>Where <paramref name="bind"/> (a point of the figure's bind pose, carried by <paramref name="bone"/>) is on the
    /// figure last drawn as <paramref name="figure"/> at <paramref name="model"/>: a hose's ends, one on the head and one on the back.</summary>
    public Vector3 Posed(string bone, Vector3 bind, in Matrix4x4 model, string figure = "crew") =>
        _models.TryGetValue(figure, out var m) && m.Model.Skeleton.IndexOf(bone) is var b && b >= 0
            ? Vector3.Transform(bind, m.Pose.Skin[b] * model) : Vector3.Transform(bind, model);

    readonly Dictionary<(string, string), float> _feetOver = [];

    /// <summary>
    /// How far a clip's feet stand off the floor at its start (m, the lowest foot or ball joint over <see cref="Planted"/>):
    /// what a figure set down on its feet is lowered by. crew_clips.py plants most clips' feet on the floor, but not
    /// crouch_idle's, which is drawn ~0.5 m up (a town's resident crouched at the range, note 353).
    /// </summary>
    public float FeetOver(string name, string clip)
    {
        if (_feetOver.TryGetValue((name, clip), out float over))
            return over;
        if (_models.TryGetValue(name, out var m) && m.Model.Clips.TryGetValue(clip, out var c))
        {
            _skinner.Evaluate(m.Model, c, 0, true, m.Pose);
            float low = float.MaxValue;
            foreach (string bone in (string[])["foot_l", "foot_r", "ball_l", "ball_r"])
                if (m.Model.Skeleton.IndexOf(bone) is var b && b >= 0)
                    low = Math.Min(low, m.Pose.World[b].Translation.Y);
            over = low < float.MaxValue ? Math.Max(0, low - Planted) : 0;
        }
        return _feetOver[(name, clip)] = over;
    }

    /// <summary>The ball joints' height over the floor standing (the idle's: the sole under them).</summary>
    const float Planted = 0.03f;

    void Hung(MeshBuilder mesh, Entry m, MeshAsset hanging, in Matrix4x4 model)
    {
        var fist = Skinner.Socket(m.Model, m.Pose, "hand_r_weapon", model).Translation;
        var up = Vector3.Normalize(new Vector3(model.M21, model.M22, model.M23));
        var hung = model with { M41 = 0, M42 = 0, M43 = 0, M44 = 1 };
        hung.Translation = fist - up * LanternRing;
        mesh.Append(hanging, hung);
        LastHanging = fist - up * (LanternRing - LanternFlame);
    }

    /// <summary>The hand lamp's ring and flame over its foot (tools/models/recipes/hand_lantern.py's sockets, 0.36 m tall).</summary>
    public const float LanternRing = 0.36f, LanternFlame = 0.15f;

    /// <summary>Where the last thing hung from a crewmate's fist has its flame (the draw's space: relative to the eye).</summary>
    public Vector3 LastHanging { get; private set; }

    /// <summary>The last crewmate drawn: their mouth (the draw's space) and the way they face.</summary>
    public Vector3 LastMouth { get; private set; }
    public Vector3 LastFacing { get; private set; } = -Vector3.UnitZ;
    static readonly Vector3 MouthOverHead = new(0, -0.06f, -0.12f);

    /// <summary>What a crewmate can do with a tool still in their fist: get about, crouch, fall, swing it, mend with it, smash or pry a Holdout open.</summary>
    static bool OneHanded(CrewPose pose) =>
        pose is CrewPose.Idle or CrewPose.Walk or CrewPose.Run or CrewPose.Crouch or CrewPose.Fall or CrewPose.Swing or CrewPose.Mend or CrewPose.Door
            or CrewPose.Smash or CrewPose.Pry or CrewPose.Jump or CrewPose.Stumble;

    /// <summary>
    /// A hand tool's axes (tools/models hand_tools: its haft along −Z through the fist, its face up +Y) onto the
    /// hand_r_weapon socket's (its +Y out of the fist's thumb side, along the haft the crew's shovel takes; its −X the
    /// back of the hand), tipped down by <see cref="ToolDroop"/> the way a wrist lets a bar hang.
    /// </summary>
    static readonly Matrix4x4 ToolGrip = Matrix4x4.CreateRotationX(ToolDroop) * new Matrix4x4(0, 0, 1, 0, -1, 0, 0, 0, 0, -1, 0, 0, 0, 0, 0, 1);

    /// <summary>How far a held tool's head drops from square to the fist (rad).</summary>
    const float ToolDroop = -0.6f;

    // ----------------------------------------------------------------------------------------------------------------
    // Your own arms (X3)

    /// <summary>The bones whose skin is drawn as your own arms: the coat's cuffs down, the gloves.</summary>
    static readonly string[] ArmBones = ["lowerarm_l", "lowerarm_r", "hand_l", "hand_r", "fingers_l", "fingers_r", "thumb_l", "thumb_r"];

    /// <summary>The eye over the head bone's root (m, model space: the mask's eyepieces, crew_clips.py's EYE).</summary>
    static readonly Vector3 EyeOverHead = new(0, 0.1f, -0.09f);

    /// <summary>
    /// The crew mesh cut down to its forearms and hands (each triangle whose corners all hang mostly from an arm bone), so
    /// the first-person arms are the crew's own sleeves and gloves and nothing of the body round the eye gets in the way.
    /// </summary>
    static MeshAsset ArmsOf(Model model, MeshAsset whole)
    {
        var keep = new HashSet<int>(ArmBones.Select(model.Skeleton.IndexOf).Where(b => b >= 0));
        var skin = whole.Skin!;
        static int Main(in SkinWeights w) =>
            (int)(w.Weights.X >= w.Weights.Y && w.Weights.X >= w.Weights.Z && w.Weights.X >= w.Weights.W ? w.Joints.X
                : w.Weights.Y >= w.Weights.Z && w.Weights.Y >= w.Weights.W ? w.Joints.Y
                : w.Weights.Z >= w.Weights.W ? w.Joints.Z : w.Joints.W);
        var v = new List<Vertex>();
        var k = new List<SkinWeights>();
        for (int t = 0; t + 2 < whole.Vertices.Length; t += 3)
            if (keep.Contains(Main(skin[t])) && keep.Contains(Main(skin[t + 1])) && keep.Contains(Main(skin[t + 2])))
                for (int c = 0; c < 3; c++)
                {
                    v.Add(whole.Vertices[t + c]);
                    k.Add(skin[t + c]);
                }
        return new MeshAsset(whole.Name + ".arms", [.. v], [.. k]);
    }

    /// <summary>
    /// Your own forearms and hands as you see them (X3), drawn round the eye at the mesh's origin: the crew's own sleeves
    /// and gloves (<see cref="ArmsOf"/>), so what you see of yourself is what the others see of you. At work (an
    /// <paramref name="act"/>: shovelling, carrying, a wheel) the arms do what that act's clip has them do, in the world;
    /// with a tool in hand they hold it up in view and follow your look (crew_clips.py's fp_hold and fp_walk), and a
    /// <paramref name="swing"/> (seconds into one, or negative) is its blow (fp_swing). Hands empty at your sides, nothing
    /// is in view, so nothing's drawn. False without the model.
    /// </summary>
    /// <param name="yaw">The body's facing (rad; 0 looks along −Z).</param>
    /// <param name="pitch">The look's (rad; up positive): the held tool follows it, the work doesn't.</param>
    public bool OwnArms(MeshBuilder mesh, float yaw, float pitch, CrewPose? act, bool moving, double swing, double time, int variant,
        MeshAsset? inHand = null)
    {
        if (!_models.TryGetValue("crew", out var m))
            return false;
        bool working = act is { } a && a is not (CrewPose.Idle or CrewPose.Walk or CrewPose.Run or CrewPose.Crouch or CrewPose.Jump or CrewPose.Stumble);
        (string clip, double t, bool loop, bool follow) = swing >= 0 ? ("fp_swing", swing, false, true)
            : working ? (ClipOf(act!.Value), time, act != CrewPose.Swing, false)
            : inHand is not null ? (moving ? "fp_walk" : "fp_hold", time, true, true)
            : ("", 0, true, false);
        if (clip.Length == 0)
            return true;
        if (!m.Model.Clips.TryGetValue(clip, out var c))
            return false;
        _skinner.Evaluate(m.Model, c, t, loop, m.Pose);
        int head = m.Model.Skeleton.IndexOf("head");
        var eye = (head >= 0 ? m.Pose.World[head].Translation : new Vector3(0, 1.58f, -0.01f)) + EyeOverHead;
        var at = Matrix4x4.CreateTranslation(-eye) * (follow ? Matrix4x4.CreateRotationX(pitch) : Matrix4x4.Identity) * Matrix4x4.CreateRotationY(yaw);
        Emit(mesh, m, clip, at, variant, 1, variant, PaintOf(variant), arms: true);
        if (inHand is not null && (follow || act is { } held && OneHanded(held)))
            mesh.Append(inHand, ToolGrip * Skinner.Socket(m.Model, m.Pose, "hand_r_weapon", at));
        return true;
    }

    /// <summary>How far behind a controller's grip (the fist's middle, OpenXR) the wrist is, back along the grip's +Z (m).</summary>
    const float WristBehindGrip = 0.07f;

    /// <summary>
    /// A headset player's own hands (X3 in a headset; roadmap M4): the crew's own gloves and cuffs (<see cref="ArmsOf"/>),
    /// each arm reached from the body's shoulder to its controller (the wrist just behind the grip, the elbow down and out)
    /// and the hand turned so the fist closes round the grip's axis, as a tool's haft goes through it. Drawn round the
    /// eye at the mesh's origin, the tracking space turned to the body's <paramref name="yaw"/> (VrHands.Place). The tool
    /// in hand sits in the right fist. False without the model (the caller draws the box fists).
    /// </summary>
    public bool HeadsetHands(MeshBuilder mesh, float yaw, in Ballast.Xr.XrHand left, in Ballast.Xr.XrHand right, int variant, MeshAsset? inHand = null)
    {
        if (!_models.TryGetValue("crew", out var m) || !m.Model.Clips.TryGetValue("fp_hold", out var c))
            return false;
        _skinner.Evaluate(m.Model, c, 0, true, m.Pose);
        int head = m.Model.Skeleton.IndexOf("head");
        var eye = (head >= 0 ? m.Pose.World[head].Translation : new Vector3(0, 1.58f, -0.01f)) + EyeOverHead;
        var at = Matrix4x4.CreateTranslation(-eye) * Matrix4x4.CreateRotationY(yaw);
        HoldGrip(m, "r", "hand_r_weapon", right, eye, 1);
        HoldGrip(m, "l", "hand_l_prop", left, eye, -1);
        Emit(mesh, m, "fp_hold", at, variant, 1, variant, PaintOf(variant), arms: true);
        if (inHand is not null && right.Tracked)
            mesh.Append(inHand, ToolGrip * Skinner.Socket(m.Model, m.Pose, "hand_r_weapon", at));
        return true;
    }

    /// <summary>
    /// One arm to a controller: reached (two-bone, from the shoulder) so the wrist sits a hand's length behind the grip,
    /// then the hand turned about the wrist to the grip's frame. OpenXR's grip pose (spec, "grip"): −Z is the way the
    /// straightened index finger points, +X the palm's normal, so the hand's length (wrist to knuckles) runs out along −Z,
    /// and what the fist closes round (the socket's haft, +Y) stands up along +Y, the thumb end on top, as a held tool does.
    /// </summary>
    void HoldGrip(Entry m, string side, string socket, in Ballast.Xr.XrHand hand, Vector3 eye, int s)
    {
        if (!hand.Tracked)
            return;
        // The tracking space is the eye point's, turned with the body; the model's frame is too, so in it the grip is the
        // eye point plus the tracked position, its axes the tracked orientation's.
        var gy = Vector3.Transform(Vector3.UnitY, hand.Orientation);
        var gz = Vector3.Transform(Vector3.UnitZ, hand.Orientation);
        var grip = eye + hand.Position;
        Reach(m, side, grip + gz * WristBehindGrip, ToF(Arms.Pole(s)));
        var sk = m.Model.Skeleton;
        int wrist = sk.IndexOf("hand_" + side), knuckles = sk.IndexOf("fingers_" + side), sock = sk.IndexOf(socket);
        if (wrist < 0 || knuckles < 0 || sock < 0)
            return;
        var length = Vector3.Normalize(m.Pose.World[knuckles].Translation - m.Pose.World[wrist].Translation);
        var w = m.Pose.World[sock];
        var haft = Vector3.Normalize(new Vector3(w.M21, w.M22, w.M23));
        haft = Vector3.Normalize(haft - length * Vector3.Dot(haft, length));
        var up = Vector3.Normalize(gy - -gz * Vector3.Dot(gy, -gz));
        // Row vectors: the hand's frame (length, haft) to the grip's (−Z, +Y): R = Sᵀ·T.
        Bend(m, "hand_" + side, Matrix4x4.Transpose(Frame(length, haft)) * Frame(-gz, up));

        static Matrix4x4 Frame(Vector3 a, Vector3 b)
        {
            var c = Vector3.Cross(a, b);
            return new Matrix4x4(a.X, a.Y, a.Z, 0, b.X, b.Y, b.Z, 0, c.X, c.Y, c.Z, 0, 0, 0, 0, 1);
        }
    }

    static Vector3 ToF(Double3 d) => new((float)d.X, (float)d.Y, (float)d.Z);

    MeshAsset[]? _marks;

    /// <summary>One reload step's length in crew_clips' reload clip (s): the powder, the rammer, the priming.</summary>
    const double ReloadBeat = 1.5;

    /// <summary>
    /// What the gunner has in their hands through the reload's beats (crew_clips.py reload, note 137), so the beat reads
    /// from across the car: the powder bag between both hands, shoved into the breech; the rammer, a long staff run forward
    /// through both fists to the gun, its head going home; the brass vent pick in the right, pricking the charge.
    /// </summary>
    void ReloadKit(MeshBuilder mesh, Entry m, in Matrix4x4 model, double time)
    {
        var right = Skinner.Socket(m.Model, m.Pose, "hand_r_weapon", model).Translation;
        var left = Skinner.Socket(m.Model, m.Pose, "hand_l_prop", model).Translation;
        var forward = Vector3.Normalize(Vector3.TransformNormal(-Vector3.UnitZ, model));
        int beat = (int)(time / ReloadBeat);
        if (beat == 0 && PropArt.Of(Look).Get("powder_bag") is { } bag)
        {
            var at = model with { M41 = 0, M42 = 0, M43 = 0, M44 = 1 };
            at.Translation = (right + left) * 0.5f + forward * 0.06f;
            mesh.Append(bag, at);
            return;
        }
        var k = new Kit(Look, mesh);
        if (beat == 1)
        {
            // The staff through both fists, forward and a little down to the muzzle; the rammer's head at its far end. Its
            // butt just past the back fist: any longer and it ran back through the gunner's own head (a Look Review note).
            var grip = (right + left) * 0.5f;
            var along = Vector3.Normalize(forward - Vector3.UnitY * 0.12f);
            k.Use("wood_crate", new Vector3(0.36f, 0.27f, 0.17f), 0.5f, 0.1f, tile: 0.6f);
            k.Cylinder(grip - along * 0.14f, grip + along * 1.45f, 0.022f, 6);
            k.Use("iron_plate", Palette.IronGrey, 0.5f, 0.3f);
            k.Cylinder(grip + along * 1.45f, grip + along * 1.62f, 0.06f, 8);
            return;
        }
        if (beat == 2)
        {
            // The vent pick: a short brass spike down out of the right fist.
            k.Use("brass", Palette.TarnishedBrass, 0.3f, 0.6f);
            k.Cylinder(right, right + forward * 0.06f - Vector3.UnitY * 0.16f, 0.006f, 5, radiusB: 0.002f);
        }
    }

    /// <summary>
    /// What tells eight masked crew apart in the dark (GDD §26: "the one with the red scarf"): an armband in their own
    /// colour (look.json crewColours), lit a little so it shows by lamplight, and one of four kits on their back or hip
    /// (a satchel, a bedroll, a coil of rope, a tall pack). The kit is chosen so that no two of the eight share both
    /// headgear (the model's variant, variant % 4) and kit, so any two crewmates differ in shape as well as colour.
    /// </summary>
    void Marks(MeshBuilder mesh, Entry m, in Matrix4x4 model, int variant)
    {
        _marks ??= BuildMarks(Look);
        var sk = m.Model.Skeleton;
        int arm = sk.IndexOf("upperarm_l"), back = sk.IndexOf("spine_03"), hip = sk.IndexOf("pelvis");
        if (arm >= 0)
            mesh.Append(_marks[0], m.Pose.World[arm] * model, Look.Tuning.CrewColour(variant));
        int v = variant & 7, kit = (v + v / 4) % 4;
        int on = kit == 0 ? hip : back;
        if (on >= 0)
            mesh.Append(_marks[1 + kit], m.Pose.World[on] * model);
    }

    static MeshAsset[] BuildMarks(Look look)
    {
        // The armband: a band of wool round the upper arm (its bone runs down the arm, +Y), pale for the crew colour to
        // tint. Lit only a breath (Emissive) so it's found by lamplight without glowing like a lamp of its own.
        var band = new Kit(look, 3100);
        band.Use("wool", new Vector3(0.9f), 0.35f, 0.05f, tile: 4f);
        band.Tint = new Vector3(0.85f);
        band.Emissive = 0.12f;
        band.Cylinder(new Vector3(0, 0.05f, 0), new Vector3(0, 0.19f, 0), 0.09f, 12, caps: false);
        band.Cylinder(new Vector3(0, 0.19f, 0), new Vector3(0, 0.05f, 0), 0.088f, 12, caps: false);
        // The kits, in the back's frame (spine_03: +X right, +Y up, +Z behind) or, for the satchel, the hips': worn
        // leather, a blanket rolled and strapped, hemp rope, an oilskin pack, all as dirty as the coats.
        var leather = new Vector3(0.24f, 0.15f, 0.09f);
        var satchel = new Kit(look, 3101);
        satchel.Use("leather", leather, 0.7f, 0.2f, tile: 3f);
        satchel.Tint = new Vector3(0.7f, 0.6f, 0.5f);
        satchel.BevelBox(new Vector3(0.17f, -0.2f, -0.12f), new Vector3(0.27f, 0.02f, 0.14f), 0.02f);
        satchel.Rod(new Vector3(0.2f, 0.02f, 0.1f), new Vector3(-0.12f, 0.55f, 0.16f), 0.018f);
        var bedroll = new Kit(look, 3102);
        bedroll.Use("wool", new Vector3(0.36f, 0.33f, 0.24f), 0.7f, 0.03f, tile: 3f);
        bedroll.Tint = new Vector3(0.55f, 0.5f, 0.4f);
        bedroll.Cylinder(new Vector3(-0.26f, 0.16f, 0.2f), new Vector3(0.26f, 0.16f, 0.2f), 0.085f, 12);
        bedroll.Use("leather", leather, 0.7f, 0.2f, tile: 3f);
        bedroll.Tint = new Vector3(0.6f, 0.5f, 0.42f);
        bedroll.Cylinder(new Vector3(-0.17f, 0.16f, 0.2f), new Vector3(-0.14f, 0.16f, 0.2f), 0.092f, 12);
        bedroll.Cylinder(new Vector3(0.14f, 0.16f, 0.2f), new Vector3(0.17f, 0.16f, 0.2f), 0.092f, 12);
        var rope = new Kit(look, 3103);
        rope.Use("wool", new Vector3(0.45f, 0.38f, 0.25f), 0.7f, 0.03f, tile: 6f);
        rope.Tint = new Vector3(0.75f, 0.62f, 0.42f);
        for (int i = 0; i < 3; i++)
            rope.Cylinder(new Vector3(0.02f * i, -0.02f, 0.16f + 0.025f * i), new Vector3(0.02f * i, -0.02f, 0.19f + 0.025f * i), 0.17f - 0.02f * i, 14, caps: false);
        rope.Rod(new Vector3(-0.15f, 0.1f, 0.18f), new Vector3(0.12f, 0.3f, -0.12f), 0.02f);
        var pack = new Kit(look, 3104);
        pack.Use("coat_oilskin", new Vector3(0.29f, 0.26f, 0.19f), 0.7f, 0.15f, tile: 3f);
        pack.Tint = new Vector3(0.7f, 0.66f, 0.55f);
        pack.BevelBox(new Vector3(-0.15f, -0.32f, 0.14f), new Vector3(0.15f, 0.2f, 0.3f), 0.03f);
        pack.Use("leather", leather, 0.7f, 0.2f, tile: 3f);
        pack.Tint = new Vector3(0.6f, 0.5f, 0.42f);
        pack.BoxAt(new Vector3(0, 0.08f, 0.31f), new Vector3(0.12f, 0.08f, 0.015f));
        return [band.Build("crew-armband"), satchel.Build("crew-satchel"), bedroll.Build("crew-bedroll"), rope.Build("crew-rope"), pack.Build("crew-pack")];
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
    /// A listening Gaunt leant in over who it's listening to, <paramref name="anger"/> (0..1) of the way: its neck let
    /// down from the shoulders (the model faces −Z), the top of it straightened again so the head still reaches out over
    /// them, and its head tipped over on its side (App. A.6 LISTEN's telegraph). Not its back: its forelegs hang from it
    /// (note 132).
    /// </summary>
    static void LeanIn(Entry m, float anger)
    {
        Bend(m, "neck_01", Matrix4x4.CreateRotationX(-GauntLean * anger));
        Bend(m, "neck_03", Matrix4x4.CreateRotationX(0.5f * GauntLean * anger));
        // (Rolled about the skull's own length, the way it's pointing: the jaw's hinge from its nape.)
        var sk = m.Model.Skeleton;
        int head = sk.IndexOf("head"), jaw = sk.IndexOf("jaw");
        var along = head >= 0 && jaw >= 0 ? m.Pose.World[jaw].Translation - m.Pose.World[head].Translation : Vector3.UnitZ;
        Bend(m, "head", Matrix4x4.CreateFromAxisAngle(along.LengthSquared() > 1e-8f ? Vector3.Normalize(along) : Vector3.UnitZ, -GauntTilt * anger));
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

    static readonly string[] Spine = ["spine_01", "spine_02", "spine_03"];

    /// <summary>
    /// The posed model under a headset's head (T82): turned from the feet to the hips' yaw and let down by the crouch; each
    /// spine bone a third of the torso's lean (about the hips' own right, forward) and twist back towards the head; the
    /// neck the rest of the way round, and tipped to the headset's pitch; each leg reached by two-bone IK to its ankle over
    /// where the foot is (the knee forward and a little out), and the foot turned as the clip had it, to its own yaw.
    /// </summary>
    static void HeadsetBody(Entry m, in VrBodyPose b)
    {
        var sk = m.Model.Skeleton;
        int footL = sk.IndexOf("foot_l"), footR = sk.IndexOf("foot_r");
        if (footL < 0 || footR < 0)
            return;
        // The clip's feet as it stands them: the ankles' height over the floor, and how each foot is turned.
        Matrix4x4 restL = m.Pose.World[footL], restR = m.Pose.World[footR];
        float hips = (float)b.Hips;
        Skinner.Place(m.Model, m.Pose, Matrix4x4.CreateRotationY(hips) * Matrix4x4.CreateTranslation(0, -(float)b.Crouch, 0));
        float yaw = hips, twist = (float)b.Twist / Spine.Length, lean = (float)b.Lean / Spine.Length;
        foreach (var bone in Spine)
        {
            yaw += twist;
            var across = new Vector3(MathF.Cos(yaw), 0, -MathF.Sin(yaw));
            Bend(m, bone, Matrix4x4.CreateRotationY(twist) * Matrix4x4.CreateFromAxisAngle(across, -lean));
        }
        // The neck the rest of the way round to the head (the model's own facing), and nodded to the headset's pitch.
        Bend(m, "neck", Matrix4x4.CreateRotationY(-yaw) * Matrix4x4.CreateFromAxisAngle(Vector3.UnitX, (float)b.Nod));
        Leg(m, "l", -1, restL, b.LeftFoot, (float)b.LeftYaw, hips);
        Leg(m, "r", 1, restR, b.RightFoot, (float)b.RightYaw, hips);
    }

    /// <summary>One leg to a foot (model space x and z; y its lift), the knee forward of the hips and out to its side.</summary>
    static void Leg(Entry m, string side, int sign, in Matrix4x4 rest, Double3 foot, float footYaw, float hips)
    {
        var target = new Vector3((float)foot.X, rest.M42 + (float)foot.Y, (float)foot.Z);
        var forward = new Vector3(-MathF.Sin(hips), 0, -MathF.Cos(hips));
        var across = new Vector3(MathF.Cos(hips), 0, -MathF.Sin(hips));
        Reach(m, "thigh_" + side, "calf_" + side, "foot_" + side, target, forward + across * (0.25f * sign) + new Vector3(0, -0.1f, 0));
        int at = m.Model.Skeleton.IndexOf("foot_" + side);
        // Turned back from wherever the knee's swing left it, to the clip's foot turned to its own yaw.
        var now = m.Pose.World[at] with { M41 = 0, M42 = 0, M43 = 0 };
        var want = (rest with { M41 = 0, M42 = 0, M43 = 0 }) * Matrix4x4.CreateRotationY(footYaw);
        if (Matrix4x4.Invert(now, out var undo))
            Bend(m, "foot_" + side, undo * want);
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

    /// <summary>
    /// An emote with no clip of its own (note 298), posed by the arms' IK in the model's space (x right, y up, z behind, from
    /// the feet) over the clip under it: the dance a jig, stepping on the spot with the hips swaying and both fists pumped
    /// over the head in turn; the wave a hand up high, rocking side to side; the point an arm straight out ahead at the
    /// shoulder. Placeholders for the art pass's clips, in the crew's stiff, heavy manner (GDD §31).
    /// </summary>
    static void EmoteArms(Entry m, CrewPose pose, double time)
    {
        float t = (float)time;
        switch (pose)
        {
            case CrewPose.Dance:
                {
                    float beat = MathF.Sin(t * MathF.Tau * 1.1f);
                    Bend(m, "spine_01", Matrix4x4.CreateRotationZ(0.14f * beat));
                    Reach(m, "l", new Vector3(-0.26f, 1.78f + 0.16f * beat, -0.12f), new Vector3(-1, -0.4f, 0.2f));
                    Reach(m, "r", new Vector3(0.26f, 1.78f - 0.16f * beat, -0.12f), new Vector3(1, -0.4f, 0.2f));
                    break;
                }
            case CrewPose.Wave:
                Reach(m, "r", new Vector3(0.34f + 0.11f * MathF.Sin(t * MathF.Tau * 1.8f), 1.94f, -0.06f), new Vector3(1, -0.6f, 0.3f));
                break;
            case CrewPose.Point:
                Reach(m, "r", new Vector3(0.17f, 1.47f, -0.66f), new Vector3(0.6f, -1, 0));
                break;
        }
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
    /// <param name="charred">Dead by fire: the coat and all burnt black, the paint gone with it.</param>
    public bool Corpse(MeshBuilder mesh, ReadOnlySpan<Vector3> joints, int variant, bool charred = false)
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
        Emit(mesh, m, "dead", Matrix4x4.Identity, variant, glow: charred ? 0 : 0.2f, seed: variant,
            adjust: charred ? (_, l) => l with { Colour = l.Colour * new Vector3(0.11f, 0.09f, 0.08f) } : PaintOf(variant));
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
    /// <item>Gaunt, Follower, Grumbler: <paramref name="extra2"/> is the anger, the nest, and feral, as the sim keeps them; track
    /// debris, its id (which kind of debris it is).</item>
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
        {
            // Its spread: the sim's blaze (extra2) over the seconds it takes to jump (enemies.json carFire.spreadSeconds).
            _carFire ??= CarFire;
            return IncidentArt.Draw(mesh, model.Translation, Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitX, model)),
                Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitY, model)), Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, model)),
                kind, phase, t, extra, health, _fx, Math.Clamp(extra2 / Math.Max(1e-6, _carFire.SpreadSeconds), 0, 1));
        }
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
                    if (phase is SpinePhase.Grab or SpinePhase.Punish)
                        // On someone: the leap, then the jaws clamped on them and the head wrenching (the pack fight's bite).
                        (clip, ct, loop) = t < 0.6 ? ("lunge", t, false) : ("bite", t - 0.6, true);
                    else if (aboard)
                    {
                        // Onto the rear car (its board: up off the ballast, scrabbling up the car's end, over the roof's lip), then
                        // the pack fight: crouch (a held beat), lunge, crouch.
                        if (t < HoundBoardSeconds)
                            (clip, loop) = ("board", false);
                        else
                        {
                            double c = (t - HoundBoardSeconds) % 2.0;
                            (clip, ct, loop) = c < 1.1 ? ("crouch", c, true) : ("lunge", c - 1.1, false);
                        }
                    }
                    else
                        clip = phase is SpinePhase.Telegraph or SpinePhase.Commit or SpinePhase.BreakOff ? "run" : "prowl";
                    if (!Draw(mesh, "cinder_hound", clip, ct, loop, model, glow: glow, seed: (float)extra))
                        return false;
                    // A light of its own (note 209's "not yet", note 337): its cracks' heat, under the keel, lighting its legs
                    // and a pool of the ground it runs over orange. At night, in the rear lamp or past it, a pack reads as
                    // the pools moving behind the train, each a hound, before their shapes do. (Not on itself: the light's
                    // inside its hide's normals, so its char stays black and its cracks are what glow on it.)
                    float flick = 0.85f + 0.15f * (float)Math.Sin(t * 13.0 + extra * 2.1 + Math.Sin(t * 4.7) * 1.5);
                    var keel = BoneAt("cinder_hound", "belly", model) - Basis(model).Up * 0.16f;
                    mesh.PointLights.Add(new PointLight(keel, HoundEmber * (glow * flick), HoundLightRange));
                    return true;
                }
            case EnemyKind.Sleepers:
                {
                    // Track debris (GDD v1.1 §22, Art/DebrisKit): a pine down, a rockfall, or a heap of old ties and a rail,
                    // by the hazard's id; there till the engine's over it (it's gone then).
                    int debris = (int)((uint)extra2 % DebrisKit.Kinds);
                    if (!_debris.TryGetValue(debris, out var piece))
                        _debris[debris] = piece = DebrisKit.Of(Look, debris);
                    mesh.Instances.Add(new MeshInstance(piece, model));
                    if (debris == 0)
                    {
                        _fallenPine ??= WorldKit.Pine(Look, 2, DebrisKit.TreeHeight);
                        mesh.Instances.Add(new MeshInstance(_fallenPine, DebrisKit.FallenPine * model));
                    }
                    return true;
                }
            case EnemyKind.Switchman:
                {
                    // At the lever (App. A.7): waiting by it; the derailer's hand on it, gripping, till the train's over the
                    // points (COMMIT, the tell); then it's thrown, heaved over, and the Switchman stands dead still by it,
                    // watching what it's done (PUNISH); called off, it flees.
                    var (clip, loop) = phase switch
                    {
                        SpinePhase.BreakOff => ("flee", true),
                        SpinePhase.Commit => ("grip", true),
                        SpinePhase.Punish => ("throw", false),
                        _ => ("wait", true),
                    };
                    if (!Draw(mesh, "switchman", clip, t, loop, model) && !Draw(mesh, "switchman", phase == SpinePhase.BreakOff ? "flee" : "wait", t, true, model))
                        return false;
                    if (clip is "grip" or "throw")
                    {
                        // The lever, from its stand's pivot by its right foot up into its hand (the stand's post under it).
                        var hand = BoneAt("switchman", "fingers_r", model);
                        var pivot = Vector3.Transform(SwitchLeverPivot, model);
                        var (_, up, _) = Basis(model);
                        Bar(mesh, pivot, hand + Vector3.Normalize(hand - pivot) * 0.12f, 0.018f, Palette.IronGrey);
                        Bar(mesh, pivot - up * SwitchLeverPivot.Y, pivot, 0.05f, Palette.SootBlack);
                    }
                    // Its lantern is the only light on it (App. A.7's "distant figure at the switch").
                    mesh.PointLights.Add(new PointLight(BoneAt("switchman", "lantern", model), Palette.LampAmber * 1.1f, 6f));
                    return true;
                }
            case EnemyKind.SootChildren:
                {
                    // A child in the dark, calling (GDD v1.2 §21, App. A.6; tools/blender/soot_child.py): squatted in the ash
                    // with its arms round its knees, rocking, and when it calls (extra) its head comes up to the train and a
                    // hand out. A Soot Child (extra2 = 1) is the model's variant 1: its eyes black, its hands and feet black
                    // (the tell, from five metres). On someone (GRAB), it's up onto them, locked round them (Enemy(e) puts it
                    // at them), its jaw dropped, drinking.
                    bool soot = extra2 > 0.5;
                    int variant = soot ? 1 : 0;
                    if (phase is SpinePhase.Grab or SpinePhase.Punish)
                        return t < SootPinSeconds ? Draw(mesh, "soot_child", "pin", t, false, model, variant, seed: 5)
                            : Draw(mesh, "soot_child", "drink", t - SootPinSeconds, true, model, variant, seed: 5);
                    return Draw(mesh, "soot_child", extra > 0.5 ? "call" : "huddle", t, true, model, variant, seed: soot ? 5 : 2);
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
                    // The grab's last moment (Enemy(e): its window nearly out), or PUNISH: dragged under, both limbs
                    // yanked back across the roof and whipped down out of sight.
                    double under = _draggedUnder;
                    _draggedUnder = -1;
                    bool dragging = under >= 0 || phase == SpinePhase.Punish;
                    for (int i = 0; i < 2; i++)
                    {
                        var at = Matrix4x4.CreateTranslation(0.12f, 0, (i - 0.5f) * 0.45f) * model;
                        if (dragging)
                            Draw(mesh, "dragger", "drag", Math.Max(0, under) + i * 0.05, false, at, seed: i);
                        else
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
                    // The Climbers (GDD v1.2 §21, App. A.4; tools/blender/climber.py; note 136): long, low, soot-black, six
                    // limbs splayed like a gecko's, hooked hands, an eyeless wedge of a head with a lamprey's sucker under
                    // it. Pacing the train it runs low, snaking; at a gap it goes up between the cars, facing in (Enemy(e)
                    // turns it); on the roofs it creeps flattened for the engine; inside an unlit car (extra −1) it waits
                    // folded in a corner; on a lone player it rears over them, the sucker on their face.
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
                    DriftReeds(mesh, model, r, t, phase is SpinePhase.Telegraph or SpinePhase.Commit or SpinePhase.Punish);
                    return true;
                }
            case EnemyKind.Follower when _models.ContainsKey("follower"):
                {
                    // The Followers (GDD v1.2 §21, App. A.6; tools/blender/follower.py; note 135): a bloated tick, ten legs,
                    // its eyes bunched on its shield. On someone's back (Enemy(e) laid it flat between their shoulder
                    // blades) it clings, and twitches; off it, it scuttles; at its car it spreads over the loot and swells
                    // as its nest builds (extra2), pulsing, kneading it, feeding.
                    bool nesting = phase == SpinePhase.Punish || phase == SpinePhase.Commit && extra2 > 0;
                    float swell = phase == SpinePhase.Punish ? 1 : (float)Math.Clamp(extra2, 0, 1);
                    var at = nesting ? Matrix4x4.CreateScale(1 + FollowerSwell * swell) * model : model;
                    string clip = nesting ? "nest" : phase == SpinePhase.Commit ? "crawl" : "cling";
                    // The nest it's built over the car's loot (tools/models follower_nest), grown with it, the Follower in
                    // its hollow on top: a heap you can find and bludgeon (A.6).
                    if (nesting && PropArt.Of(Look).Get("follower_nest") is { } heap)
                    {
                        float grown = 0.25f + 0.75f * swell;
                        mesh.Append(heap, Matrix4x4.CreateScale(grown, grown * (0.6f + 0.4f * swell), grown) * model);
                        at = Matrix4x4.CreateTranslation(0, FollowerNestTop * grown * (0.6f + 0.4f * swell), 0) * at;
                    }
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
                    // The Gaunt (GDD v1.2 §21, App. A.6; tools/blender/gaunt.py; a thing on stilts, note 132). Asleep, the
                    // heap of branches breathing; stirring, its head up out of it to listen. Woken, it stalks after its
                    // waker while they go and stands over them listening when they stop, its neck let down further and its
                    // head tipped over further at every point of anger (extra2); at its threshold it rears and the forelegs
                    // come down. The cars weren't built for it (note 118): aboard it creeps with its legs folded, squats
                    // to listen, and stabs from the squat.
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
                    // Feeding, the plate it's chewing spits sparks out of its mouth (the checklist's sparks).
                    if (phase is SpinePhase.Commit || phase == SpinePhase.Telegraph && t >= latch)
                    {
                        var (_, hu, hb) = Basis(model);
                        _fx.Grind(mesh, model.Translation + Vector3.Normalize(hu) * 1.0f + Vector3.Normalize(hb) * 0.35f, Vector3.Normalize(hu), Vector3.Normalize(hb), t, (int)extra2);
                    }
                    return phase switch
                    {
                        SpinePhase.Dormant => Draw(mesh, "car_hugger", "lurk", t, true, Matrix4x4.CreateTranslation(0, HuggerLurkLift, 0) * model,
                            adjust: (_, look) => look with { Colour = look.Colour * HuggerLurkMud }),
                        SpinePhase.BreakOff => Draw(mesh, "car_hugger", "release", t, false, model),
                        SpinePhase.Grab or SpinePhase.Punish => Swallowing(Draw(mesh, "car_hugger", "swallow", t, true, model, regrip), model),
                        SpinePhase.Telegraph when t < latch => Draw(mesh, "car_hugger", "latch", t, false, model),
                        SpinePhase.Telegraph => Draw(mesh, "car_hugger", "feed", t - latch, true, model, regrip),
                        _ => Draw(mesh, "car_hugger", "feed", t, true, model, regrip),
                    };
                }
            case EnemyKind.TrackDoll:
                {
                    // The porcelain doll (GDD v1.2 §21, App. A.2; tools/blender/track_doll.py). On the rail it stands stock
                    // still. Aboard it stands over the cargo admiring it and then giggles, by turns; at the empty cab's
                    // controls (extra2 over 0: her escalation, note 268) it tampers; cornered (extra, in a car) it cowers. Its face and glass eyes draw under a material of
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
                        // At the controls extra is how long she's been there (note 268), so the cab's checked first.
                        (clip, ct) = extra2 > 0.5 ? ("tamper", t)
                            : extra > 0.5 ? ("cower", t)
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
                    // With the toy it was given, it's clutching it under its chin (its giggle's hands), and looking at you.
                    if (DollHolding is not null)
                        (clip, ct, watch) = ("giggle", 0.4, true);
                    Action<Entry>? look = null;
                    if (watch && Matrix4x4.Invert(at, out var toModel))
                    {
                        var eye = Vector3.Transform(Vector3.Zero, toModel);
                        look = e => WatchWithHead(e, eye);
                    }
                    bool drawn = Draw(mesh, "track_doll", clip, ct, true, at, look, seed: 3,
                        adjust: (mm, l) => mm.Name.EndsWith(".face", StringComparison.Ordinal) ? l with { Emissive = MathF.Max(l.Emissive, glow) } : l)
                        || Draw(mesh, "track_doll", "stand", ct, true, at, look, seed: 3);
                    // Going with the toy it was given (APPEASED: GreyboxScene's vanishing): held in both hands under its chin.
                    if (drawn && DollHolding is { } toy)
                    {
                        var held = at;
                        held.Translation = (BoneAt("track_doll", "hand_r", at) + BoneAt("track_doll", "hand_l", at)) / 2
                            - Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitY, at)) * 0.1f
                            - Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, at)) * 0.07f;
                        mesh.Instances.Add(new MeshInstance(toy, held));
                    }
                    return drawn;
                }
            case EnemyKind.Whistler when _models.ContainsKey("whistler"):
                {
                    // The gap-dweller (GDD v1.2 §21, App. A.4; tools/blender/whistler.py; a pale many-legged coil, note
                    // 133), on the rail between the cars (Enemy(e) drops it from the sim's gap point). Hidden, it's coiled
                    // under the bridge plate round the drawgear, breathing: there to be found by whoever looks down into
                    // the gap. Whistling (extra), it rears straight up out of the gap, hooks the cord with its forelegs
                    // and yanks it, the siphon blowing; then it watches the gap's mouth, its front lifted, turning in
                    // jerks. Carrying someone off, it runs, snaking, its legs in waves, its front reared up with them held
                    // in its forelegs under it (carry: where its hooks are is where they hang, Clutches); off empty, it runs.
                    string clip = phase switch
                    {
                        SpinePhase.Dormant => "fold",
                        SpinePhase.Telegraph when extra > 0.5 => "whistle",
                        SpinePhase.Grab or SpinePhase.Punish => "carry",
                        SpinePhase.BreakOff => "run",
                        _ => "watch",
                    };
                    if (clip == "carry" && Draw(mesh, "whistler", clip, t, true, model, seed: 41))
                    {
                        _clutch = ((BoneAt("whistler", "hook_r", model) + BoneAt("whistler", "hook_l", model)) / 2,
                            Vector3.Normalize(Vector3.TransformNormal(-Vector3.UnitZ, model)), true);
                        return true;
                    }
                    return Draw(mesh, "whistler", clip == "carry" ? "run" : clip, t, true, model, seed: 41);
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
                            {
                                // Pulled off its victim, or seen, it recoils first, jerked up and back and held a beat
                                // where it was, then scuttles (the checklist's "recoil when pulled off").
                                double recoil = TippyRecoil;
                                if (t < recoil)
                                    return Draw(mesh, "tippy_toesie", "recoil", t, false, model);
                                return Draw(mesh, "tippy_toesie", "flee", t - recoil, true, Matrix4x4.CreateTranslation(0, 0, TippyFleeSpeed * (float)(t - recoil)) * model);
                            }
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
                                // With its catch frozen (GRAB), the leader creeps in on them low, the tongue still out (the sim's
                                // quarter-speed hop), and close to, it's on them, devouring (the tongue's in them, its own clip's).
                                // The tongue is the model's own, aimed at their chest and stretched to it (Lash).
                                float near = prey is { } q ? new Vector2(q.Feet.X - model.Translation.X, q.Feet.Z - model.Translation.Z).Length() : float.MaxValue;
                                string clip = RibbitClip(phase, near);
                                Vector3? chest = null;
                                if (prey is { } p && Matrix4x4.Invert(model, out var toModel))
                                    chest = Vector3.Transform(p.Feet + Vector3.UnitY * RibbitTongueAt, toModel);
                                void Reach(Entry e)
                                {
                                    if (clip != "devour")
                                        Lash(e, chest);
                                }
                                return Draw(mesh, "ribbit", clip, t, true, model, Reach, seed: (float)extra2)
                                    || (clip = "tongue") == "tongue" && Draw(mesh, "ribbit", "tongue", t, true, model, Reach, seed: (float)extra2);
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
                    // One of the Choir's ghosts (GDD v1.2 §21, App. A.7; tools/blender/choir.py; note 134): a bell of veined
                    // membrane drifting like a jellyfish, one child's mouth on its front held open singing, its tendrils
                    // trailing. The voices arriving (TELEGRAPH) it drifts, pulsing, singing; after someone (COMMIT, extra
                    // its target) it swoops at them, and with nobody to take it presses to the shut doors lashing at them;
                    // seizing (GRAB) it's capped on its catch's head, the tendrils wound round it. It bobs; the faint cold
                    // about it is all the light it has (§26: not neon).
                    _prey = null;
                    var at = Matrix4x4.CreateTranslation(0, (float)(0.12 * Math.Sin(t * 2.3 + extra2)), 0) * model;
                    // Driven off (BREAK OFF: GreyboxScene.Leaving carries it away), it goes in its swoop, its cold going out.
                    string clip = phase switch
                    {
                        SpinePhase.Commit when extra >= 0 => "swoop",
                        SpinePhase.Commit => "besiege",
                        SpinePhase.Grab or SpinePhase.Punish => "seize",
                        SpinePhase.BreakOff => "swoop",
                        _ => "drift",
                    };
                    float cold = phase == SpinePhase.BreakOff ? Math.Clamp(1 - (float)(t / ChoirLeaveSeconds), 0, 1) : 1;
                    bool drawn = Draw(mesh, "choir", clip, t, true, at, seed: 71 + (float)extra2);
                    if (drawn)
                    {
                        mesh.PointLights.Add(new PointLight(model.Translation + new Vector3(0, 0.8f, 0), Palette.BlueGrey * (ChoirCold * cold), 2.5f));
                        if (_fx.HasFlames && cold > 0.4f)
                            _fx.ChoirCold(mesh, Vector3.Transform(new Vector3(0, 1.15f, 0), at), t, clip == "swoop", 71 + (float)extra2 * 13);
                    }
                    return drawn;
                }
            case EnemyKind.Moose when _models.ContainsKey("moose"):
                {
                    // THE MOOSE (note 339; tools/blender/moose.py; docs/design/creatures/moose.md §3, §5). Its meter is its
                    // ears and its posture, never the HUD: grazing, head down; listening (aggro over listenAt), the head up and
                    // the ears forward; warning (over warnAt), the ears pinned flat and the sac ridge stood up, a hoof dragged.
                    // Riled it trots after them, squares up (the rack levelled at them, two stamps), charges, skids and wheels
                    // round, jams its rack in what it can't get through, searches where it lost them, rams the car they're
                    // in, and pins whoever it ran down under the rack. Its mode is the sim's (Moose.Mode); how long it's
                    // been at it, GreyboxScene's (its clips start with it).
                    var mode = _mooseMode ?? MooseModeOf(phase);
                    double since = _mooseSince >= 0 ? _mooseSince : t;
                    float pace = _pace;
                    (_mooseMode, _mooseSince, _pace) = (null, -1, 0);
                    var (clip, at, loop) = MooseClip(mode, extra2, pace, t, since, MooseTuning, TrainPassing);
                    TrainPassing = false;
                    return Draw(mesh, "moose", clip, at, loop, model, seed: 311);
                }
            case EnemyKind.Gannet when _models.ContainsKey("gannet"):
                {
                    // THE GANNET (note 340; tools/blender/gannet.py; docs/design/creatures/gannet.md §3, §5). Over a fast
                    // train it soars in the smoke, wings spread, now and then banking round; over a walker it hangs head
                    // down (the calls stop), folds into a dart, sacs swollen, and drops down its line; it stabs and climbs, or
                    // misses and is stuck, the spear in the roof, thrashing, then tears free; marked, it banks round
                    // screaming, swoops, lands on its mark and pins them under a foot, mantled, pecking on the sim's 3 s beat
                    // (Gannet.PecksLanded); driven off, it lurches up. Its mode is the sim's (Gannet.Mode); how long it's been
                    // at it and what it did before, GreyboxScene's (its clips start with it).
                    var mode = _gannetMode ?? GannetModeOf(phase);
                    var was = _gannetWas;
                    double since = _gannetSince >= 0 ? _gannetSince : t;
                    (_gannetMode, _gannetWas, _gannetSince) = (null, null, -1);
                    // Gone off (a slow train, a tunnel, a still roof): not there at all.
                    if (mode == Sim.Enemies.GannetMode.Away)
                        return true;
                    var tune = GannetTuning;
                    var (clip, at, loop) = GannetClip(mode, was, since, phase == SpinePhase.Grab ? t : since, tune);
                    bool drawn = Draw(mesh, "gannet", clip, at, loop, model, glow: GannetGlow(clip, at), seed: 340);
                    // Lit from below (§3: "its belly catches the firebox glow and the sparks"): in the air over the train
                    // it's in the plume the fire lights, so its underside and its wings' are orange against the black sky
                    // (Part Eleven Q6). The fire's light, not its own: none down on a roof.
                    if (drawn && mode is not (Sim.Enemies.GannetMode.Stuck or Sim.Enemies.GannetMode.Pin))
                    {
                        var (_, up, _) = Basis(model);
                        mesh.PointLights.Add(new PointLight(model.Translation - up * GannetUnderlight, Palette.LampAmber * 0.75f, GannetUnderlight * 3f));
                    }
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
                // T131 (the director: "what were the bubbles?"): the sparks it sheds off its burning tail, a short trail
                // behind it on its loop, falling and dimming, so a swarm on the wing reads as embers round the lamp.
                mesh.Emissive = 1;
                for (int j = 1; j <= FireFlySparks; j++)
                {
                    double back = j * 0.07;
                    var spark = Where(t - back) - u * (float)(0.05 * back * j);
                    float fade = 1 - (j - 1f) / FireFlySparks;
                    mesh.Box(spark, r, u, b, new Vector3(0.005f * fade + 0.002f), FireFlyEmber * (0.6f + 0.8f * fade) * (0.8f + 0.2f * MathF.Sin((float)t * 17 + i + j)));
                }
                mesh.Emissive = 0;
            }
            // The model faces -Z with its back +Y: its rows are where X, Y and Z go.
            var z = -fwd;
            var x = Vector3.Cross(up, z);
            var m = new Matrix4x4(x.X, x.Y, x.Z, 0, up.X, up.Y, up.Z, 0, z.X, z.Y, z.Z, 0, at.X, at.Y, at.Z, 1);
            Draw(mesh, "fire_fly", clip, t + k * 7.3, true, Matrix4x4.CreateScale(size) * m, seed: i + (float)seed, adjust: FireFlyLook);
        }
        // The lamp's light through them: brighter, and redder, the more there are.
        float flick = 0.85f + 0.15f * MathF.Sin((float)t * 13 + 1.7f) * MathF.Sin((float)t * 5.3f);
        mesh.PointLights.Add(new PointLight(o, Palette.FurnaceOrange * (0.25f + 0.9f * fill) * flick, 2.5f + 2.5f * fill));
    }

    /// <summary>
    /// Draws a creature in the sim's state. <paramref name="hitAge"/>: seconds since a blow or a ball last landed on it (−1
    /// for none lately): its rig's own <c>hit</c> clip plays over whatever it was doing for as long as that clip runs (the
    /// checklist's hit reacts), except while it has hold of someone.
    /// </summary>
    /// <param name="dying">Killed (GreyboxScene.Deaths): held in its hit pose at the end of it (or, with no hit clip, still).</param>
    /// <param name="modeSeconds">How long a Moose has been in its <see cref="Sim.Enemies.MooseMode"/> (GreyboxScene keeps it;
    /// −1: its phase's time): its square-up, its skid and wheel play from when they began.</param>
    public bool Enemy(MeshBuilder mesh, in Matrix4x4 model, Enemy e, Bite bite = default, Prey? prey = null, Room? room = null, float pace = 0,
        double hitAge = -1, bool dying = false, double modeSeconds = -1)
    {
        // (A Gannet's flinch is a flying one: stuck in the roof or folded into its dive, it's thrashing or a dart already.)
        _hit = !dying && (e.Phase is SpinePhase.Grab or SpinePhase.Punish || e is Sim.Enemies.Gannet { Mode: Sim.Enemies.GannetMode.Stuck or Sim.Enemies.GannetMode.Fold })
            ? -1 : hitAge;
        _dying = dying;
        _mooseSince = modeSeconds;
        _gannetSince = modeSeconds;
        try
        {
            return EnemyIn(mesh, model, e, bite, prey, room, pace);
        }
        finally
        {
            _hit = -1;
            _dying = false;
            (_mooseMode, _mooseSince) = (null, -1);
            (_gannetMode, _gannetWas, _gannetSince) = (null, null, -1);
            GannetWas = null;
        }
    }

    // Set by Enemy(e) for a Gannet's one draw: what it's doing, what it did before, and how long it's been at it (s, or −1).
    Sim.Enemies.GannetMode? _gannetMode, _gannetWas;
    double _gannetSince = -1;
    Sim.Enemies.GannetTuning? _gannet;

    /// <summary>What the Gannet about to be drawn was doing before what it's doing now (GreyboxScene keeps it; the wire
    /// doesn't): out of a dive it's stabbed, off a pin it's been driven. Used once.</summary>
    public Sim.Enemies.GannetMode? GannetWas { get; set; }

    /// <summary>The Gannet's tuning, as the content has it (its fold, its stuck, its bank, its pecks).</summary>
    public Sim.Enemies.GannetTuning GannetTuning => _gannet ??= DataFile.Load<Sim.Enemies.EnemyTuning>(Path.Combine(ContentRoot, Sim.Enemies.EnemyTuning.File)).Gannet;

    /// <summary>A Gannet's mode as its phase has it, for a draw without the sim's (a test's, the greybox's).</summary>
    public static Sim.Enemies.GannetMode GannetModeOf(SpinePhase phase) => phase switch
    {
        SpinePhase.Alert => Sim.Enemies.GannetMode.Hang,
        SpinePhase.Telegraph => Sim.Enemies.GannetMode.Fold,
        SpinePhase.Commit => Sim.Enemies.GannetMode.Stuck,
        SpinePhase.Grab or SpinePhase.Punish => Sim.Enemies.GannetMode.Pin,
        SpinePhase.BreakOff => Sim.Enemies.GannetMode.Climb,
        _ => Sim.Enemies.GannetMode.Soar,
    };

    // The clips' lengths (tools/blender/gannet.py; CreatureArtTests pins them to the model): its fold into the dart, the stab
    // and the tear free, the swoop and the landing, the peck's wind-up and its strike (its contact a few frames in), the lurch
    // off a pin; and the soar's hold between banking round (a circle's one turn).
    public const double GannetFoldSeconds = 0.4, GannetStabSeconds = 0.5, GannetTearFreeSeconds = 0.8, GannetSwoopSeconds = 0.8,
        GannetLandSeconds = 0.6, GannetWindupSeconds = 0.9, GannetPeckSeconds = 0.5, GannetPeckStrike = 0.1, GannetDrivenSeconds = 0.8,
        GannetSoarHold = 8, GannetCircleSeconds = 4;

    /// <summary>How far under a flying Gannet the fire's light on its belly comes from (m): the plume's glow below it.</summary>
    const float GannetUnderlight = 3.2f;

    /// <summary>
    /// The clip a Gannet plays (tools/blender/gannet.py), how far into it, and whether it loops: by its mode (the sim's),
    /// how long it's been in it (<paramref name="modeSeconds"/>) and what it was doing before (<paramref name="was"/>).
    /// Soaring it holds still, banking round now and then (a circle); folding, the fold then the dive; stuck, thrashing,
    /// then the tear free to end on the sim's stuckSeconds; climbing out of a dive it stabs first, off a pin it's driven off
    /// first; banking, the swoop to end on the bank's bankSeconds; pinning, it lands, then pecks on the sim's beat
    /// (<paramref name="grabSeconds"/>, the spine's grab time Gannet.PecksLanded counts: each peck's strike lands on a
    /// peckEvery, the wind-up before it).
    /// </summary>
    public static (string Clip, double Time, bool Loop) GannetClip(Sim.Enemies.GannetMode mode, Sim.Enemies.GannetMode? was, double modeSeconds,
        double grabSeconds, Sim.Enemies.GannetTuning t)
    {
        switch (mode)
        {
            case Sim.Enemies.GannetMode.Hang:
                return ("hang", modeSeconds, true);
            case Sim.Enemies.GannetMode.Fold:
                return modeSeconds < GannetFoldSeconds ? ("fold", modeSeconds, false) : ("dive", modeSeconds - GannetFoldSeconds, true);
            case Sim.Enemies.GannetMode.Stuck:
                {
                    double free = Math.Max(0, t.StuckSeconds - GannetTearFreeSeconds);
                    return modeSeconds < free ? ("stuck", modeSeconds, true) : ("tearFree", modeSeconds - free, false);
                }
            case Sim.Enemies.GannetMode.Bank:
                {
                    double swoop = Math.Max(0, t.BankSeconds - GannetSwoopSeconds);
                    return modeSeconds < swoop ? ("bank", modeSeconds, true) : ("swoop", modeSeconds - swoop, false);
                }
            case Sim.Enemies.GannetMode.Pin:
                {
                    if (modeSeconds < GannetLandSeconds)
                        return ("land", modeSeconds, false);
                    for (int k = 1; k <= t.Pecks; k++)
                    {
                        double start = k * t.PeckEvery - GannetPeckStrike;
                        if (grabSeconds >= start && grabSeconds < start + GannetPeckSeconds)
                            return ("peck", grabSeconds - start, false);
                        if (grabSeconds >= start - GannetWindupSeconds && grabSeconds < start)
                            return ("peckWindup", grabSeconds - (start - GannetWindupSeconds), false);
                    }
                    return ("pin", modeSeconds, true);
                }
            case Sim.Enemies.GannetMode.Climb:
                if (was == Sim.Enemies.GannetMode.Fold && modeSeconds < GannetStabSeconds)
                    return ("stab", modeSeconds, false);
                if (was == Sim.Enemies.GannetMode.Pin && modeSeconds < GannetDrivenSeconds)
                    return ("driven", modeSeconds, false);
                return ("climb", modeSeconds, true);
            default:
                {
                    double u = modeSeconds % (GannetSoarHold + GannetCircleSeconds);
                    return u < GannetSoarHold ? ("soar", u, true) : ("circle", u - GannetSoarHold, false);
                }
        }
    }

    /// <summary>How bright the Gannet's sacs are through its skin (its emissive surfaces' glow): swollen tight in the fold and
    /// the dive, the wind-up and the scream of the bank; throbbing while it pins; as they are otherwise.</summary>
    static float GannetGlow(string clip, double time) => clip switch
    {
        "fold" or "dive" or "peckWindup" => 1.7f,
        "bank" or "swoop" or "stuck" => 1.3f,
        "pin" or "land" or "peck" => 1.15f + 0.3f * (float)Math.Max(0, Math.Sin(time * 5.2)),
        _ => 1f,
    };

    /// <summary>
    /// Where the one a Gannet pins has their chest, from it (m: across to its right, and ahead): under its right foot's web
    /// (gannet.py PIN_FOOT). Laid on their back along its heading, their head ahead of it where the pecks land and their feet
    /// back under it (crew_clips.py held_pinned: the chest <see cref="PinnedChest"/> behind where they are).
    /// </summary>
    public static readonly Vector2 GannetPinChest = new(0.3f, 0.82f);

    /// <summary>How far behind where they are someone laid on their back (held_pinned) has their chest (m, along their
    /// facing: their feet are a metre ahead of it, their head half a metre behind).</summary>
    public const float PinnedChest = 0.21f;

    // Set by Enemy(e) for a Moose's one draw, as _pace: what it's doing and how long it's been at it (s, or −1).
    Sim.Enemies.MooseMode? _mooseMode;
    double _mooseSince = -1;
    Sim.Enemies.MooseTuning? _moose;

    /// <summary>The train's going by the Moose about to be drawn, within its trainPassAt (GreyboxScene sets it for each one):
    /// grazing, it takes the train for a rival, tossing its head and bellowing after it (moose.py trainPass). Used once.</summary>
    public bool TrainPassing { get; set; }

    /// <summary>The Moose's tuning, as the content has it (its tells' thresholds, its speeds).</summary>
    public Sim.Enemies.MooseTuning MooseTuning => _moose ??= DataFile.Load<Sim.Enemies.EnemyTuning>(Path.Combine(ContentRoot, Sim.Enemies.EnemyTuning.File)).Moose;

    /// <summary>A Moose's mode as its phase has it, for a draw without the sim's (a test's, the greybox's): grazing, after
    /// someone, squaring up, charging, pinning, going home.</summary>
    public static Sim.Enemies.MooseMode MooseModeOf(SpinePhase phase) => phase switch
    {
        SpinePhase.Alert => Sim.Enemies.MooseMode.Hunt,
        SpinePhase.Telegraph => Sim.Enemies.MooseMode.SquareUp,
        SpinePhase.Commit => Sim.Enemies.MooseMode.Charge,
        SpinePhase.Grab or SpinePhase.Punish => Sim.Enemies.MooseMode.Pin,
        SpinePhase.BreakOff => Sim.Enemies.MooseMode.Home,
        _ => Sim.Enemies.MooseMode.Graze,
    };

    /// <summary>
    /// The clip a Moose plays (tools/blender/moose.py), and how far into it: by its mode, its aggro (grazing: <c>graze</c>,
    /// then <c>listen</c> over listenAt, <c>warn</c> over warnAt) and how fast it's going (<paramref name="pace"/>, m/s: a
    /// walk, a trot over halfway from its search pace to its hunting one; stood, it glares or listens); grazing with the train
    /// going by (<paramref name="trainPassing"/>), short of warning, <c>trainPass</c>. The square-up, the
    /// skid and the wheel play once from when its mode began (<paramref name="modeSeconds"/>); grazing keeps its phase's time.
    /// </summary>
    public static (string Clip, double Time, bool Loop) MooseClip(Sim.Enemies.MooseMode mode, double aggro, float pace, double phaseSeconds,
        double modeSeconds, Sim.Enemies.MooseTuning t, bool trainPassing = false)
    {
        double trotFrom = (t.SearchSpeed + t.HuntSpeed) / 2;
        string Moving(string stood) => pace > trotFrom ? "trot" : pace > Going ? "walk" : stood;
        return mode switch
        {
            Sim.Enemies.MooseMode.Graze => (trainPassing && aggro < t.WarnAt ? "trainPass" : aggro >= t.WarnAt ? "warn" : aggro >= t.ListenAt ? "listen" : "graze",
                phaseSeconds, true),
            Sim.Enemies.MooseMode.Home => (pace > Going ? "strut" : "graze", modeSeconds, true),
            Sim.Enemies.MooseMode.Hunt => (Moving("warn"), modeSeconds, true),
            Sim.Enemies.MooseMode.Search => (pace > Going ? "search" : "listen", modeSeconds, true),
            // Ramming the car they're in: a ram each ramEvery seconds (the clip's length) once it's stood at its side.
            Sim.Enemies.MooseMode.Ram => (Moving("ram"), modeSeconds, true),
            Sim.Enemies.MooseMode.SquareUp => ("squareUp", modeSeconds, false),
            Sim.Enemies.MooseMode.Charge => ("charge", modeSeconds, true),
            Sim.Enemies.MooseMode.Wheel => ("overrun", modeSeconds, false),
            Sim.Enemies.MooseMode.Snag => ("snag", modeSeconds, true),
            _ => ("pin", modeSeconds, true),
        };
    }

    /// <summary>How far in front of a pinning Moose the one under its rack has their feet (m, along its heading): on their
    /// back, their head toward it, the muzzle over their face and the palms either side of their chest (moose.py's pin, its
    /// muzzle down at the ground 1.4 m out, its knees a metre out; crew_clips.py held_pinned, the head 1.6 m behind the feet).</summary>
    public const float MoosePinReach = 2.0f;

    /// <summary>
    /// Who each Moose drawn this frame has pinned (the player's id): where their feet are (camera-relative) and the way
    /// they're laid (their facing: away from it, so they lie with their head under its rack). GreyboxScene turns them so, and
    /// clears it each frame with <see cref="Clutches"/>.
    /// </summary>
    public Dictionary<int, (Vector3 Feet, Vector3 Forward)> Pins { get; } = new();

    // Drawing the dead: no clip runs on (Draw holds the hit clip's last frame, or the clip's first).
    bool _dying;

    // How long since the creature being drawn was struck (s), or −1: Draw plays its hit clip over its own while it runs.
    double _hit = -1;

    /// <summary>
    /// Who each creature drawn this frame has hold of (the player's id), and where: a Whistler carrying them off, its
    /// forelegs' hooks and the way it's running (tools/blender/whistler.py carry, its sockets hook_r and hook_l: Hung, by the
    /// armpits, <see cref="CarriedUnderarm"/> over their feet); a Car Hugger swallowing them, its mouth and the way into it
    /// (stood <see cref="SwallowReach"/> back from it on their own floor, bent into it). Camera-relative. GreyboxScene puts
    /// them there, and clears it each frame.
    /// </summary>
    public Dictionary<int, (Vector3 At, Vector3 Forward, bool Hung)> Clutches { get; } = new();

    /// <summary>How far in front of the Car Hugger's mouth someone it's swallowing stands: their head in it
    /// (crew_clips.py held_mouth, bent double, the head 0.62-0.82 m ahead of their feet).</summary>
    public const float SwallowReach = 0.72f;

    // The Car Hugger just drawn swallowing: its mouth bone (over the end door's line), and the way into the mouth is its
    // back (it faces the car).
    bool Swallowing(bool drawn, in Matrix4x4 model)
    {
        if (drawn)
        {
            var into = Vector3.TransformNormal(Vector3.UnitZ, model) with { Y = 0 };
            if (into.LengthSquared() > 1e-8f)
                _clutch = (BoneAt("car_hugger", "mouth", model), Vector3.Normalize(into), false);
        }
        return drawn;
    }

    /// <summary>How far over their feet someone carried off by the Whistler is held: under the arms (whistler.py
    /// CARRY_UNDERARM; crew_clips.py held_carried lifts them 0.1 m to it).</summary>
    public const float CarriedUnderarm = 1.42f;

    // The clutch of the Whistler just drawn in its carry, for Clutches.
    (Vector3 At, Vector3 Forward, bool Hung)? _clutch;

    bool EnemyIn(MeshBuilder mesh, in Matrix4x4 model, Enemy e, Bite bite, Prey? prey, Room? room, float pace)
    {
        var m = model;
        switch (e.Kind)
        {
            case EnemyKind.Choir when _models.ContainsKey("choir"):
                {
                    // Swooping, it faces who it's after (the loose basis faces the train: at the doors, it faces them);
                    // driven off, away from the train.
                    if (e.Phase == SpinePhase.BreakOff)
                        m = Matrix4x4.CreateRotationY(MathF.PI) * model;
                    else if (prey is { } p && e.Phase is SpinePhase.Commit or SpinePhase.Grab)
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
            case EnemyKind.SootChildren when prey is { } held && e.Phase is SpinePhase.Grab or SpinePhase.Punish:
                {
                    // On them: in front of them, facing them, a little way off their chest (the clip lifts it to them and
                    // locks it round them: tools/blender/soot_child.py); the sim has it at their feet.
                    var at = held.Feet + held.Forward * SootCling;
                    var placed = model;
                    placed.Translation = at;
                    m = Facing(placed, held.Feet);
                    break;
                }
            case EnemyKind.Dragger when e.Phase == SpinePhase.Grab && e.GrabWindow - e.PhaseSeconds < DraggerDragSeconds:
                // How far into its drag under it is (its last DraggerDragSeconds of the grab's window).
                _draggedUnder = DraggerDragSeconds - (e.GrabWindow - e.PhaseSeconds);
                break;
            case EnemyKind.FireFlies:
                // Each swarm its own way round its lamp (by its id).
                return Enemy(mesh, m, e.Kind, e.Phase, e.PhaseSeconds, e.Extra, e.Health, aboard: true, extra2: e.Id);
            case EnemyKind.Stoker when e is Sim.Enemies.Stoker { Boarding: true } && _models.ContainsKey("stoker"):
                {
                    // Boarding at the tender (note 263): crouched on the coal and creeping across the footplate to the fire
                    // door, facing it (the engine's −Z), lit by its own sick glow: the telegraph, in the open.
                    if (Draw(mesh, "stoker", "peer", e.PhaseSeconds, true, model, seed: 13))
                    {
                        float flicker = 0.8f + 0.2f * (float)Math.Sin(e.PhaseSeconds * 17.0);
                        mesh.PointLights.Add(new PointLight(Vector3.Transform(new Vector3(0, 0.4f, 0), model), StokerFire * flicker * 1.6f, 2.2f));
                    }
                    return true;
                }
            case EnemyKind.Stoker when _models.ContainsKey("stoker") && FireDoorOpen is { } door:
                // At the open door, looking out of it into the cab: the firebox stands against the cab's front wall (note
                // 280), so the cab's behind it (+Z), and the model (facing −Z) is turned round to it. Its peer clip was made
                // for a door 0.7 m over the floor; this one's lower (TrainKit.FireDoorUp), so it's lifted the difference, its
                // hands on the floor and its head in the top of the hole.
                m = Matrix4x4.CreateRotationY(MathF.PI) * Matrix4x4.CreateTranslation(door + new Vector3(0, TrainKit.StokerDoorLift, 0)) * model;
                break;
            case EnemyKind.Follower when _models.ContainsKey("follower") && prey is { } carrier && e.Phase is SpinePhase.Dormant or SpinePhase.Telegraph:
                {
                    // Riding: flat between its carrier's shoulder blades, its belly to them and its head up (the model's −Y
                    // into their back, its −Z, the way it faces, up), where their friends can see it and they can't.
                    var r = carrier.Right;
                    var f = carrier.Forward;
                    var o = carrier.At(0, FollowerUp, FollowerBack);
                    m = new Matrix4x4(r.X, r.Y, r.Z, 0, -f.X, -f.Y, -f.Z, 0, 0, -1, 0, 0, o.X, o.Y, o.Z, 1);
                    break;
                }
            case EnemyKind.Moose when _models.ContainsKey("moose"):
                {
                    // It faces its heading (GreyboxScene turns its basis to the sim's Moose.Yaw), going as fast as it's been
                    // going. Pinning someone (App. A.6 GRAB), it's stood over them: turned to them where the sim has them, its
                    // rack on them, and they're laid on their back under it with their head toward it (Pins).
                    _mooseMode = (e as Sim.Enemies.Moose)?.Mode;
                    _pace = pace;
                    if (e.Phase is SpinePhase.Grab or SpinePhase.Punish && e.Holding >= 0 && prey is { } held)
                    {
                        var (_, up, _) = Basis(model);
                        var to = (held.Feet - model.Translation) with { Y = 0 };
                        var f = to.LengthSquared() > 1e-6f ? Vector3.Normalize(to) : Vector3.Normalize(-Vector3.TransformNormal(Vector3.UnitZ, model) with { Y = 0 });
                        var right = Vector3.Normalize(Vector3.Cross(up, -f));
                        var stood = held.Feet - f * MoosePinReach;
                        m = Basis(stood with { Y = model.Translation.Y }, right, up, -f);
                        Pins[e.Holding] = (held.Feet, f);
                    }
                    break;
                }
            case EnemyKind.Gannet when _models.ContainsKey("gannet"):
                {
                    // It faces its heading (GreyboxScene turns its basis to the sim's Lateral in the frame it's in). Pinning
                    // someone (App. A.6 GRAB), it's stood on them: its right foot's web on their chest where the sim has them,
                    // and they're laid on their back along its heading, their head out in front of it where the pecks land
                    // (Pins; their feet back under it).
                    _gannetMode = (e as Sim.Enemies.Gannet)?.Mode;
                    _gannetWas = GannetWas;
                    GannetWas = null;
                    if (e.Phase is SpinePhase.Grab or SpinePhase.Punish && e.Holding >= 0 && prey is { } held)
                    {
                        var (_, up, b) = Basis(model);
                        var f = Vector3.Normalize(-b with { Y = 0 });
                        var right = Vector3.Normalize(Vector3.Cross(f, up));
                        var stood = held.Feet + f * (PinnedChest - GannetPinChest.Y) - right * GannetPinChest.X;
                        m = Basis(stood with { Y = held.Feet.Y }, right, up, -f);
                        Pins[e.Holding] = (held.Feet, -f);
                    }
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
                        // There with its victim (the run to it done: Extra2's seconds, T128's nest where the land allows;
                        // without one, enemies.json whistler nestDistance at runSpeed), its
                        // nest under it on the land (GreyboxScene puts the carry on the ground, and its trail behind it) (tools/models whistler_nest: the hollow, the sleepers, the bones, the strands).
                        _whistler ??= DataFile.Load<Sim.Enemies.EnemyTuning>(Path.Combine(ContentRoot, Sim.Enemies.EnemyTuning.File)).Whistler;
                        if (e.Phase is SpinePhase.Grab or SpinePhase.Punish && e.PhaseSeconds >= (e.Extra2 > 0 ? e.Extra2 : _whistler.NestDistance / _whistler.RunSpeed))
                        {
                            if (PropArt.Of(Look).Get("whistler_nest") is { } nest)
                                mesh.Instances.Add(new MeshInstance(nest, model));
                            // Done running: over its catch, its front up, watching the way it came (its "watch").
                            return Enemy(mesh, m, e.Kind, SpinePhase.Commit, e.PhaseSeconds, 0, e.Health, aboard: false, extra2: e.Extra2);
                        }
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
                    // Hidden between tries (the sim's Dormant): nowhere, but for the moment it's seen recoiling and
                    // scuttling off from where it was (never placed yet: never seen).
                    if (e.Phase == SpinePhase.Dormant && (e.Local == default || e.PhaseSeconds >= TippyRecoil + TippyFleeShow))
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
        _clutch = null;
        bool drawn = Enemy(mesh, m, e.Kind, e.Phase, e.PhaseSeconds, e.Extra, e.Health, aboard: e.Attached >= 0,
            extra2: e.Kind == EnemyKind.Sleepers ? e.Id : e.Extra2);
        if (_clutch is { } clutch && e.Holding >= 0)
            Clutches[e.Holding] = clutch;
        _clutch = null;
        return drawn;
    }

    static (Vector3 Right, Vector3 Up, Vector3 Back) Basis(in Matrix4x4 m) =>
        (Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitX, m)), Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitY, m)), Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, m)));
}
