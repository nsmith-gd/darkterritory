using System.Numerics;
using Ballast;
using Ballast.Assets;
using Ballast.Render;
using DarkTerritory.Game;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Enemies;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The crew and creature models (tools/blender, content/art/models): each loads, stays inside its class's budget
/// (GDD §27; the pipeline plan's LOD0 numbers), keeps its skeleton inside its template, plays 30 fps clips that loop
/// without a pop, and skins the same way every time. The turntables render each clip at 480×270, lantern-lit in the
/// night's fog, into out/shots/creatures/ for a person to look at (CLAUDE.md: "look at your visual changes").
/// </summary>
public class CreatureArtTests
{
    static readonly string Repo = Path.GetDirectoryName(DataFile.FindContentRoot())!;
    // DT_CONTENT lets a turntable run against a content folder with textures not yet checked in.
    static readonly string Content = Environment.GetEnvironmentVariable("DT_CONTENT") is { Length: > 0 } c ? c : DataFile.FindContentRoot();
    static readonly Look Look = Look.Load(Content);
    static readonly CreatureArt Art = new(Look, Content);

    /// <summary>A model's budget: triangles for its class (LOD0, the art brief), bones for its template, and its clips.</summary>
    public sealed record Budget(int MinTris, int MaxTris, int MinBones, int MaxBones, string[] Loops, string[] Once);

    static readonly Dictionary<string, Budget> Budgets = new()
    {
        // Characters (GDD §27: 4-10k; the crew brief: at most 9k, aim for chunky 3-5k). SK_Human: at most 60 bones.
        // Its actions come from crew_clips.glb, merged on load (note 145).
        ["crew"] = new(2500, 9000, 20, 60, ["idle", "walk", "run", "climb", "shovel", "crouch_idle", "carry", "carry_walk", "drag", "drag_fwd", "door",
            "handbrake", "hatch", "uncouple", "vent", "lever", "push", "held", "gunner", "fall", "mend", "fp_hold", "fp_walk",
            // (Note 375's stumble; note 378's rescues by the grab; note 407's prisoner's shuffle.)
            "stumble", "pull_mouth", "pry_off", "haul_down", "shuffle"], ["dead", "swing", "fp_swing", "jump"]),
        // The crew figure gone wrong (the Climbers and the Deadman): the crew's own rig and clips.
        ["husk"] = new(2500, 9000, 20, 60, ["idle", "walk", "run", "climb", "shovel", "crouch_idle"], ["dead"]),
        ["switchman"] = new(2000, 9000, 20, 60, ["wait", "flee", "grip"], ["throw"]),
        ["hollow"] = new(1500, 5000, 20, 60, ["idle"], ["reach"]),
        ["soot_child"] = new(3000, 9000, 20, 60, ["huddle", "call", "drink"], ["pin"]),
        // The freed survivors (App. D.8): the crew figure redressed, on its rig, with the crew's actions merged on load.
        ["survivor_prisoner"] = new(2500, 9000, 20, 60, ["idle", "walk", "run", "climb", "shovel", "crouch_idle", "carry", "lever"], ["dead"]),
        ["survivor_wildlander"] = new(2500, 9000, 20, 60, ["idle", "walk", "run", "climb", "shovel", "crouch_idle", "carry", "lever"], ["dead"]),
        // Livestock (GDD §19): a prop's budget, packed a dozen to a car; its own small quadruped rig.
        ["sheep"] = new(600, 3000, 12, 30, ["idle", "shuffle", "bleat"], ["startle"]),
        // SK_Quad: 40-55 bones.
        ["cinder_hound"] = new(4000, 8000, 40, 55, ["prowl", "run", "crouch", "bite"], ["lunge", "board", "hit"]),
        // A chain of 8-12, plus a root.
        ["sleeper"] = new(400, 3000, 8, 13, ["dormant", "writhe"], ["lift"]),
        ["clinger"] = new(1500, 6000, 10, 45, ["cling", "drill"], ["punish"]),
        // A limb, not a body (App. A.4: all you see of one): a chain of arm, hand and two-bone fingers.
        ["dragger"] = new(300, 3000, 8, 20, ["grip"], ["reach", "drag"]),
        // A heap of bodies, not a body (App. A.3): the heap, its heads and six arms on a chain rig.
        ["weight"] = new(2500, 8000, 20, 40, ["drag"], ["grab", "release"]),
        // A jointed porcelain doll (App. A.2), a character's budget: SK_Human in a doll's proportions, rigid at the joints.
        ["track_doll"] = new(4000, 10000, 20, 60, ["stand", "beckon", "admire", "giggle", "tamper", "cower"], ["hit"]),
        // A large monster (GDD §27: 8-16k is the ceiling), on a chain: a spine of four, the mouth and its teeth rings, four arms.
        ["car_hugger"] = new(4000, 14000, 20, 40, ["lurk", "feed", "swallow"], ["latch", "release", "hit"]),
        // A character (App. A.5), SK_Human stretched: over two metres on its points, so it stoops indoors and ducks through doors.
        ["tippy_toesie"] = new(3000, 9000, 20, 60, ["stalk", "wait", "flee", "smother", "stoop", "stalk_stoop", "duck"], ["hit", "recoil"]),
        // A character (App. A.4), SK_Human stretched, its right forearm long for the cord: it folds up to fit a coupling gap.
        ["whistler"] = new(3000, 9000, 20, 60, ["fold", "whistle", "watch", "run", "carry"], ["hit"]),
        // A beast's (App. A.6), on its own rig (SK_Ribbit): a throat sac to swell, a jaw, a tongue, long ears.
        ["ribbit"] = new(2000, 8000, 20, 40, ["sit", "hop", "swell", "tongue", "creep", "devour"], ["hit"]),
        // A swarm's (App. A.7): several at once, so light; SK_Human at a child's size, the legs hidden in its strips.
        ["choir"] = new(1000, 3000, 20, 60, ["drift", "swoop", "seize", "besiege"], ["hit"]),
        // A character (App. A.6), SK_Human stretched to near three metres: down on its arms and squatted aboard (note 118).
        ["gaunt"] = new(3000, 9000, 20, 60, ["sleep", "follow", "listen", "attack", "crawl", "squat", "smash"], ["stir", "hit"]),
        // A character's (App. A.8), SK_Human with a jaw and a second pair of arms out of its ribs: down like a spider.
        ["grumbler"] = new(3000, 9000, 30, 60, ["gnaw", "scuttle", "bite", "maul"], ["hit"]),
        // A character's (App. A.5), SK_Human shrunk with a jaw: only ever seen at the firebox door, so light.
        ["stoker"] = new(1500, 5000, 20, 40, ["peer", "reach"], ["hit"]),
        // A hand's (App. A.6), on its own rig (SK_Follower: a palm, a stump, five three-boned fingers, a mouth): hand-sized.
        ["follower"] = new(600, 3000, 15, 25, ["cling", "crawl", "nest"], ["hit"]),
        // A character's (App. A.4), SK_Human drawn out: one of the crew, the gas mask grown into its face.
        ["climber"] = new(2000, 9000, 20, 60, ["run", "scrabble", "walk", "crouch", "grab"], ["hit"]),
        // A swarm's (App. A.5): a moth, up to a couple of dozen of them at a lamp, so light; on its own rig (SK_FireFly).
        ["fire_fly"] = new(600, 1500, 10, 20, ["flutter", "settle"], []),
        // A character's (App. A.8), SK_Human at a man's height: it passes for crew, so it's dressed as one.
        ["passenger"] = new(3000, 9000, 20, 60, ["stand", "walk", "drag", "pin"], ["hit"]),
        // A large monster (GDD §27: 8-16k; docs/design/creatures/moose.md §3), on its own quadruped rig (SK_Moose: the rack a bone
        // a side, the velvet, the sac ridge, the bell); every clip the brief asks for.
        ["moose"] = new(8000, 16000, 30, 55, ["graze", "listen", "warn", "walk", "strut", "search", "trot", "charge", "snag", "ram", "pin", "trainPass"],
            ["squareUp", "overrun", "hit"]),
        // A large monster (GDD §27: 8-16k; docs/design/creatures/gannet.md §3), on its own bird rig (SK_Gannet: five bones a
        // wing, three sacs that swell, a jaw that gapes, the spear's tip a socket); every clip the brief asks for.
        ["gannet"] = new(8000, 16000, 25, 45, ["soar", "circle", "hang", "dive", "stuck", "climb", "bank", "pin"],
            ["fold", "stab", "tearFree", "swoop", "land", "peckWindup", "peck", "driven", "hit", "death"]),
    };

    public static TheoryData<string> Models() => [.. CreatureArt.Names];

    static Model Get(string name) => Art.Get(name) ?? throw new FileNotFoundException($"content/{CreatureArt.Folder}/{name}.glb: run tools/blender/build.sh");

    [Theory]
    [MemberData(nameof(Models))]
    public void LoadsInsideItsBudget(string name)
    {
        var m = Get(name);
        var b = Budgets[name];
        for (int v = 0; v < m.VariantCount; v++)
            Assert.InRange(m.Triangles(v), b.MinTris, b.MaxTris);
        Assert.InRange(m.Skeleton.Count, b.MinBones, b.MaxBones);
        foreach (var clip in b.Loops.Concat(b.Once))
            Assert.Contains(clip, m.ClipNames);
        // Every part's weights sum to one over at most four real bones, and every material names a texture or a colour.
        foreach (var p in m.Parts)
        {
            Assert.Equal(p.Positions.Length * 4, p.Weights.Length);
            for (int i = 0; i < p.Positions.Length; i++)
                Assert.InRange(p.Weights[i * 4] + p.Weights[i * 4 + 1] + p.Weights[i * 4 + 2] + p.Weights[i * 4 + 3], 0.999f, 1.001f);
            Assert.All(p.Indices, i => Assert.InRange(i, 0, p.Positions.Length - 1));
        }
        // Standing on the floor at the origin, facing −Z: the pivot's between the feet (the clinger's is on the hull, the
        // dragger's at the car's edge, the rest of it hanging below; the weight's at the coupler, the heap on the stones; the
        // car hugger's at its mouth on the rear platform, its body down to the rail; a Choir ghost flies, its strips hanging
        // below where it is; the Stoker's is the firebox door, its body in the fire behind and below it; a Fire Fly's is its
        // body's middle, its legs under it, for the glass it settles on).
        if (name is not ("clinger" or "dragger" or "weight" or "car_hugger" or "choir" or "stoker" or "fire_fly"))
        {
            Assert.InRange(m.Min.Y, -0.02f, 0.05f);
            Assert.InRange((m.Min.X + m.Max.X) / 2, -0.25f, 0.25f);
        }
    }

    [Fact]
    public void ModelsAreTheirBriefsSize()
    {
        static float Height(Model m) => m.Max.Y - m.Min.Y;
        // Heights and lengths from the brief (GDD §21, App. A): what makes the silhouette read at distance.
        // 1.8 m to the crown of the head, and the gas mask's flying cap and the welder's visor over it (about 1.86 m).
        Assert.InRange(Height(Get("crew")), 1.8f, 1.95f);
        Assert.InRange(Height(Get("switchman")), 1.7f, 2.2f);
        Assert.InRange(Height(Get("hollow")), 1.95f, 2.25f);
        var hound = Get("cinder_hound");
        Assert.InRange(hound.Max.Z - hound.Min.Z, 1.25f, 1.9f); // nose to tail's root and beyond
        Assert.True(hound.Min.Z < -0.5f, "the hound's head is forward (−Z)");
        // The Gannet (gannet.md §3): a 7 m wingspan, the widest thing in the roster, and 2.3 m tall stood on a roof; its
        // metre of spear forward (−Z).
        var gannet = Get("gannet");
        Assert.InRange(gannet.Max.X - gannet.Min.X, 6.8f, 7.2f);
        Assert.InRange(Height(gannet), 2.2f, 2.45f);
        Assert.True(gannet.Min.Z < -2.1f, "the gannet's spear is forward (−Z)");
        // The Moose (moose.md §3): 2.4 m at the shoulder, about 3.4 m to the top of its rack, and the rack 3.2 m across, the
        // widest thing on the ground; its head forward (−Z).
        var moose = Get("moose");
        Assert.InRange(Height(moose), 3.2f, 3.5f);
        Assert.InRange(moose.Max.X - moose.Min.X, 3.1f, 3.35f);
        Assert.True(moose.Min.Z < -1.5f, "the moose's head is forward (−Z)");
        var sleeper = Get("sleeper");
        Assert.InRange(sleeper.Max.X - sleeper.Min.X, 2.5f, 3.1f);
        Assert.InRange(Height(sleeper), 0.2f, 0.45f);
        var clinger = Get("clinger");
        Assert.InRange(clinger.Max.X, 0.3f, 0.7f); // bulging out from the hull at x = 0
        Assert.True(clinger.Min.X > -0.05f, "the clinger doesn't go through the hull");
        var dragger = Get("dragger");
        Assert.InRange(dragger.Max.X, 0.6f, 1.0f); // the hand reaches in over the roof (+X) from outside the eave
        Assert.True(dragger.Min.X > -0.3f && dragger.Min.Y < -0.8f, "the limb comes up from under the car, close by its side");
        // The crew's hand socket is at the right hand, and forward is −Z (the face is in front of the head's centre).
        Assert.True(Get("crew").Skeleton.IndexOf("hand_r_weapon") >= 0);
        Assert.True(Get("crew").Skeleton.Socket[Get("crew").Skeleton.IndexOf("head_hat")]);
        // Each crewmate wears their own colour (the flying cap's dyed leather and the scarf): the paint is its own material on the atlas,
        // and no two of the first eight players share a colour.
        Assert.Contains(Get("crew").Materials, m => m.Name.EndsWith(".paint", StringComparison.Ordinal) && m.Texture == "crew_0");
        var colours = Enumerable.Range(0, 8).Select(Look.Tuning.CrewColour).ToList();
        Assert.Equal(8, colours.Distinct().Count());
    }

    /// <summary>
    /// No crew clip puts an arm or a leg through the body, the coat, the head or the other limbs (Look Review notes: arms
    /// through the chest, the hips and a knee, an arm through the head; note 256). Contact within the coat's slop
    /// (Clearance.Touching) reads as touching; the two-handed swing's forearms meet on the haft, and a forearm rests on a
    /// knee, a little deeper.
    /// </summary>
    [Fact]
    public void TheCrewsLimbsStayOutOfTheirBodies()
    {
        var through = Clearance.Check(Art, "crew").Where(o => o.Depth > 0.1f).ToList();
        Assert.True(through.Count == 0, string.Join("; ", through.Select(o => $"{o.Clip} {o.Pair} {o.Depth:0.000} at {o.At:0.00} s")));
    }

    /// <summary>
    /// The demo's creatures, from their own meshes (Clearance.Mesh: a capsule fitted to each bone's share of the skin):
    /// no part of one goes through another in any clip deeper than reads as touching, beyond how they sit at rest (a
    /// Car Hugger's folded arm through its mouth, a Track Doll's fingers through its head; note 262).
    /// </summary>
    [Theory]
    [InlineData("ribbit")]
    [InlineData("track_doll")]
    [InlineData("car_hugger")]
    [InlineData("whistler")]
    [InlineData("tippy_toesie")]
    [InlineData("choir")]
    public void TheDemoCreaturesStayOutOfThemselves(string name)
    {
        var through = Clearance.Mesh(Get(name)).Where(o => o.Depth > Clearance.Touching).ToList();
        Assert.True(through.Count == 0, string.Join("; ", through.Select(o => $"{o.Clip} {o.Pair} {o.Depth:0.000} at {o.At:0.00} s")));
    }

    /// <summary>
    /// At their full budgets the demo's creatures would put a headset's frame over tuning/perf.json's triangles (each is
    /// drawn for two eyes and the shadows), so each has a distance copy, drawn past look.json's creatureLodMetres: two
    /// fifths of it or so, on the same bones (note 262).
    /// </summary>
    [Theory]
    [InlineData("ribbit")]
    [InlineData("track_doll")]
    [InlineData("car_hugger")]
    [InlineData("whistler")]
    [InlineData("tippy_toesie")]
    [InlineData("choir")]
    public void TheDemoCreaturesHaveADistanceCopy(string name)
    {
        var full = Get(name);
        var lod = Art.LodOf(name);
        Assert.NotNull(lod);
        int a = full.Parts.Sum(p => p.Triangles), b = lod.Parts.Sum(p => p.Triangles);
        Assert.InRange(b, a / 4, a * 6 / 10);
        Assert.Equal(full.Materials.Length, lod.Materials.Length);
        Assert.All(lod.Parts, p => Assert.All(p.Joints, j => Assert.InRange(j, 0, full.Skeleton.Count - 1)));
    }

    [Theory]
    [MemberData(nameof(Models))]
    public void ClipsAre30FpsAndLoopsCloseCleanly(string name)
    {
        var m = Get(name);
        var b = Budgets[name];
        foreach (var clip in m.Clips.Values)
        {
            Assert.Equal(30, clip.Fps);
            Assert.True(clip.Frames >= 2, $"{name}/{clip.Name}: {clip.Frames} frames");
            Assert.Equal(m.Skeleton.Count, clip.Bones);
        }
        foreach (var loop in b.Loops)
        {
            var clip = m.Clip(loop);
            Assert.True(clip.Loops, $"{name}/{loop} doesn't end where it starts");
            // Played past its end it wraps: the pose a period on is the same pose.
            var a = Skinner.Skin(m, loop, 0.2, loop: true);
            var c = Skinner.Skin(m, loop, 0.2 + clip.Duration, loop: true);
            for (int i = 0; i < a.Length; i++)
                Assert.True(Near(a[i], c[i]), $"{name}/{loop}: bone {m.Skeleton.Names[i]} differs a loop later");
        }
    }

    static bool Near(in Matrix4x4 a, in Matrix4x4 b, float eps = 1e-4f)
    {
        for (int r = 0; r < 4; r++)
            for (int k = 0; k < 4; k++)
                if (MathF.Abs(a[r, k] - b[r, k]) > eps)
                    return false;
        return true;
    }

    [Theory]
    [MemberData(nameof(Models))]
    public void SkinningIsDeterministicAndFinite(string name)
    {
        var m = Get(name);
        foreach (var clipName in m.ClipNames)
        {
            var clip = m.Clip(clipName);
            for (int f = 0; f < clip.Frames; f += Math.Max(1, clip.Frames / 7))
            {
                double t = f / 30.0 + 0.004;
                var one = Emit(clipName, t);
                var two = Emit(clipName, t);
                Assert.Equal(one.Length, two.Length);
                for (int i = 0; i < one.Length; i++)
                {
                    Assert.Equal(one[i].Position, two[i].Position);
                    Assert.True(float.IsFinite(one[i].Position.X) && float.IsFinite(one[i].Position.Y) && float.IsFinite(one[i].Position.Z)
                        && float.IsFinite(one[i].Normal.X) && float.IsFinite(one[i].Normal.Y) && float.IsFinite(one[i].Normal.Z),
                        $"{name}/{clipName} frame {f}: vertex {i} isn't finite");
                    Assert.InRange(one[i].Position.Length(), 0, 6); // nothing flung across the scene by a bad weight
                }
            }
        }

        Vertex[] Emit(string clip, double t)
        {
            var mesh = new MeshBuilder();
            Assert.True(Art.Draw(mesh, name, clip, t, true, Matrix4x4.Identity));
            return mesh.Flattened();
        }
    }

    [Fact]
    public void EveryEnemyDrawsInEveryPhaseAndTheCrewInEveryPose()
    {
        var mesh = new MeshBuilder();
        foreach (var kind in Enum.GetValues<EnemyKind>())
            foreach (var phase in Enum.GetValues<SpinePhase>().Where(p => p != SpinePhase.Gone))
                foreach (double t in new[] { 0.0, 0.7, 2.9, 11.3 })
                {
                    mesh.Clear();
                    Assert.True(Art.Enemy(mesh, Matrix4x4.CreateTranslation(0, 0, -5), kind, phase, t, 0.5), $"{kind} {phase}");
                    // A Dragger is out of sight under the car's edge until it reaches, as the greybox has it. The Stoker's in the
                    // firebox: only its work is seen. Fire Flies are only a swarm. (A lurking Car Hugger is seen: a muddied
                    // mound sunk in the low ground by the line, App. A.3 LURK.)
                    bool hidden = kind == EnemyKind.Dragger && phase is not (SpinePhase.Telegraph or SpinePhase.Grab or SpinePhase.Punish)
                        || kind == EnemyKind.Stoker
                        || kind == EnemyKind.FireFlies && phase == SpinePhase.Dormant;
                    // A car fire is an effect (Effects.CarFire): smoke and flame billboards, no triangles of its own.
                    if (kind == EnemyKind.CarFire)
                        Assert.True(mesh.AlphaFx.Count + mesh.AdditiveFx.Count > 0, $"{kind} {phase} drew no smoke or flame");
                    else
                        Assert.True(hidden ? mesh.Flattened().Length == 0 : mesh.Flattened().Length > 0, $"{kind} {phase} drew {mesh.Flattened().Length / 3} triangles");
                }
        foreach (var pose in Enum.GetValues<CrewPose>())
            for (int variant = 0; variant < 4; variant++)
            {
                mesh.Clear();
                Assert.True(Art.Crewmate(mesh, Matrix4x4.Identity, pose, 3.3, variant));
                Assert.InRange(mesh.Flattened().Length / 3, 2000, 9000);
            }
        // Track debris (GDD v1.1 §22, Art/DebrisKit): one heap across the line, which kind by the hazard's id, the same in
        // every phase (it's inert: the v1.0 Sleepers' writhe is gone).
        var debris = new HashSet<int>();
        for (int id = 0; id < DebrisKit.Kinds; id++)
        {
            int Tris(SpinePhase phase)
            {
                mesh.Clear();
                Art.Enemy(mesh, Matrix4x4.Identity, EnemyKind.Sleepers, phase, 0, 0, extra2: id);
                // (The fallen pine is the root plate and WorldKit's spruce laid down: two pieces.)
                Assert.Equal(id == 0 ? 2 : 1, mesh.Instances.Count);
                return mesh.Flattened().Length / 3;
            }
            Assert.Equal(Tris(SpinePhase.Dormant), Tris(SpinePhase.Telegraph));
            debris.Add(Tris(SpinePhase.Dormant));
        }
        Assert.Equal(DebrisKit.Kinds, debris.Count);
        // And one child (GDD v1.1 A.6: a single voice calling).
        mesh.Clear();
        Art.Enemy(mesh, Matrix4x4.Identity, EnemyKind.SootChildren, SpinePhase.Dormant, 0, 0);
        // (One of its two variants, the real child or the Soot Child: most of the model, not its other eyes and hands.)
        Assert.True(mesh.Flattened().Length / 3 >= Get("soot_child").Triangles() * 3 / 4);
    }

    [Fact]
    public void AHeadsetPlayersHandIsWhereTheirHandIs()
    {
        // T47: the crew see a headset's arms. The model's arm reaches the reported hand (up and out, and out in front);
        // without one, the clip's arm is nowhere near it.
        foreach (var hand in new[] { new Vector3(0.35f, 2.05f, -0.45f), new Vector3(-0.22f, 1.2f, -0.5f) })
        {
            var reached = new MeshBuilder();
            var (l, r) = hand.X < 0 ? ((Vector3?)hand, (Vector3?)null) : (null, hand);
            Assert.True(Art.Crewmate(reached, Matrix4x4.Identity, CrewPose.Idle, 0.4, 1, l, r,
                new Vector3(-0.6f, -1, 0.3f), new Vector3(0.6f, -1, 0.3f)));
            var hanging = new MeshBuilder();
            Assert.True(Art.Crewmate(hanging, Matrix4x4.Identity, CrewPose.Idle, 0.4, 1));
            Assert.Equal(hanging.Flattened().Length, reached.Flattened().Length);
            Assert.InRange(reached.Flattened().Min(v => Vector3.Distance(v.Position, hand)), 0, 0.1f);
            Assert.True(hanging.Flattened().Min(v => Vector3.Distance(v.Position, hand)) > 0.25f);
        }

        // And through the scene, as a replicated crewmate: the hand in the frame they face, from their feet.
        var line = Sim.Rail.RailLine.Load(Path.Combine(Content, "lines", "test-loop.json"));
        var tuning = DataFile.Load<Sim.Train.TrainTuning>(Path.Combine(Content, Sim.Train.TrainTuning.File));
        var train = new Sim.Train.TrainOnLine(new Sim.Train.TrainDynamics(Sim.Train.Consist.Uniform(tuning, 2, 1)), line, 1200);
        var feet = train.Frames[1].ToWorld(new Double3(0, train.Frames[1].Shape.RoofHeight, 0));
        var up = new Double3(0.35, 2.05, -0.45);
        var mesh = new MeshBuilder();
        new GreyboxScene { Look = Look, Time = 0.37, Crew = [new Crewmate(3, feet, 0, true, up)] }.Build(mesh, train, feet + new Double3(3, 1.5, 0));
        var at = (feet + up).RelativeTo(feet + new Double3(3, 1.5, 0));
        Assert.InRange(mesh.Flattened().Min(v => Vector3.Distance(v.Position, at)), 0, 0.1f);
    }

    [Fact]
    public void AKilledCreatureGoesOverThenIsGone()
    {
        // GreyboxScene.Deaths: out of the sim at once, but drawn going over and crumbling until Effects.DeathSeconds, then not.
        var tuning = DataFile.Load<Sim.Train.TrainTuning>(Path.Combine(Content, Sim.Train.TrainTuning.File));
        var line = Sim.Rail.RailLine.Load(Path.Combine(Content, "lines", "test-loop.json"));
        var train = new Sim.Train.TrainOnLine(new Sim.Train.TrainDynamics(Sim.Train.Consist.Uniform(tuning, 6, 1)), line, 1200);
        var living = Staging.Threats(train);
        var dead = living.First(e => e.Kind == EnemyKind.Switchman);
        living.Remove(dead);
        var eye = dead.WorldPosition(train) + new Double3(3, 1.6, 2);
        int Drawn(double age, bool kill)
        {
            var scene = new GreyboxScene { Look = Look, Time = 0.37, Enemies = living, Tick = Staging.StrikeTick + (long)Math.Round(age * Sim.SimConstants.TickRate) };
            if (kill)
                scene.Killed(dead, Staging.StrikeTick, new Vector3(1, 0, 0));
            var mesh = new MeshBuilder();
            scene.Build(mesh, train, eye);
            return mesh.Instances.Count(i => i.Asset.Name.Contains("switchman", StringComparison.OrdinalIgnoreCase));
        }
        Assert.Equal(0, Drawn(0.6, kill: false));
        Assert.True(Drawn(0.6, kill: true) > 0, "going over");
        Assert.True(Drawn(Effects.DeathSeconds * 0.8, kill: true) > 0, "crumbling");
        Assert.Equal(0, Drawn(Effects.DeathSeconds + 0.1, kill: true));
    }

    [Fact]
    public void ACinderHoundCarriesItsOwnLightUnderItsKeel()
    {
        // Note 320: at night a pack reads by the pools of ember light each hound runs in, so every hound drawn brings its
        // light, under its body (where it lights its legs and the ground, not its char), not off on its own somewhere.
        var tuning = DataFile.Load<Sim.Train.TrainTuning>(Path.Combine(Content, Sim.Train.TrainTuning.File));
        var line = Sim.Rail.RailLine.Load(Path.Combine(Content, "lines", "test-loop.json"));
        var train = new Sim.Train.TrainOnLine(new Sim.Train.TrainDynamics(Sim.Train.Consist.Uniform(tuning, 6, 1)), line, 1200);
        var pack = Staging.Threats(train).Where(e => e.Kind == EnemyKind.CinderHound && e.Attached < 0).ToList();
        Assert.Equal(3, pack.Count);
        var eye = pack[0].WorldPosition(train) + new Double3(4, 3, 6);
        var scene = new GreyboxScene { Look = Look, Time = 0.37, Enemies = [.. pack] };
        var mesh = new MeshBuilder();
        scene.Build(mesh, train, eye);
        foreach (var hound in pack)
        {
            var at = hound.WorldPosition(train) - eye;
            var feet = new Vector3((float)at.X, (float)at.Y, (float)at.Z);
            var own = mesh.PointLights.Where(l => new Vector2(l.Position.X - feet.X, l.Position.Z - feet.Z).Length() < 0.6f).ToList();
            Assert.True(own.Count == 1, $"hound {hound.Id}: {own.Count} lights over it");
            float up = own[0].Position.Y - feet.Y;
            Assert.InRange(up, 0.15f, 0.6f);
            Assert.True(own[0].Range >= 3, $"reach {own[0].Range}");
        }
    }

    [Fact]
    public void TheChoirDrivenOffIsSeenGoingUpAndAwayThenIsGone()
    {
        // GreyboxScene.Leaving: the sim dismisses the swarm the tick it's driven off (World: quiet held); the scene that saw
        // its ghosts last frame draws them going, higher and higher, until CreatureArt.ChoirLeaveSeconds, then not.
        var tuning = DataFile.Load<Sim.Train.TrainTuning>(Path.Combine(Content, Sim.Train.TrainTuning.File));
        var line = Sim.Rail.RailLine.Load(Path.Combine(Content, "lines", "test-loop.json"));
        var train = new Sim.Train.TrainOnLine(new Sim.Train.TrainDynamics(Sim.Train.Consist.Uniform(tuning, 6, 1)), line, 1200);
        var threats = Staging.Threats(train);
        var ghosts = threats.Where(e => e.Kind == EnemyKind.Choir).ToList();
        Assert.NotEmpty(ghosts);
        var eye = ghosts[0].WorldPosition(train) + new Double3(6, -1, 4);
        var scene = new GreyboxScene { Look = Look, Time = 0.37, Enemies = threats, Tick = Staging.StrikeTick };
        float[] Heights(double after)
        {
            scene.Tick = Staging.StrikeTick + 1 + (long)Math.Round(after * Sim.SimConstants.TickRate);
            var mesh = new MeshBuilder();
            scene.Build(mesh, train, eye);
            return [.. mesh.Instances.Where(i => i.Asset.Name.Contains("choir", StringComparison.OrdinalIgnoreCase)).Select(i => i.Model.Translation.Y)];
        }
        var there = Heights(-1.0 / Sim.SimConstants.TickRate);
        Assert.NotEmpty(there);
        scene.Enemies = [.. threats.Except(ghosts)];
        var going = Heights(0.1);
        var gone = Heights(CreatureArt.ChoirLeaveSeconds * 0.8);
        Assert.Equal(there.Length, going.Length);
        Assert.Equal(there.Length, gone.Length);
        Assert.True(gone.Max() > there.Max() + 4, $"going up: from {there.Max()} to {gone.Max()}");
        // (Going from the frame that first missed them, 0.1 s in.)
        Assert.Empty(Heights(0.1 + CreatureArt.ChoirLeaveSeconds + 0.1));
    }

    [Fact]
    public void AHoundScatteredByABallRunsOffTheLineIntoTheDarkThenIsGone()
    {
        // GreyboxScene.Fleeing (note 451): a ball landing near a runner has it gone from the sim that tick; the scene that saw
        // it last frame draws it running on, falling behind a running train and peeling off on its own side, turned the way
        // it's going, until CreatureArt.HoundRunOffSeconds, then not.
        var tuning = DataFile.Load<Sim.Train.TrainTuning>(Path.Combine(Content, Sim.Train.TrainTuning.File));
        var line = Sim.Rail.RailLine.Load(Path.Combine(Content, "lines", "test-loop.json"));
        var train = new Sim.Train.TrainOnLine(new Sim.Train.TrainDynamics(Sim.Train.Consist.Uniform(tuning, 6, 1)), line, 1200);
        var pack = Staging.Run(train);
        var hound = pack.OfType<CinderHound>().First(h => h.Phase == SpinePhase.Commit);
        var near = train.Line.Sample(hound.LineDistance);
        var start = hound.WorldPosition(train);
        var eye = start + new Double3(0, 6, 0) + Double3.Cross(near.Tangent, Double3.Up).Normalized * (Math.Sign(hound.Lateral) * -12);
        var scene = new GreyboxScene { Look = Look, Time = 0.37, Enemies = pack, Tick = Staging.StrikeTick };
        // The hounds drawn, each by where it stands (each is one model instance) and the way it faces (its model's −Z).
        List<(Vector3 At, Vector3 Facing)> Hounds(double after)
        {
            scene.Tick = Staging.StrikeTick + 1 + (long)Math.Round(after * Sim.SimConstants.TickRate);
            var mesh = new MeshBuilder();
            scene.Build(mesh, train, eye);
            return [.. mesh.Instances.Where(i => i.Asset.Name.Contains("cinder_hound", StringComparison.OrdinalIgnoreCase))
                .Select(i => (i.Model.Translation, Vector3.Normalize(-Vector3.TransformNormal(Vector3.UnitZ, i.Model) with { Y = 0 })))];
        }
        var there = Hounds(-1.0 / Sim.SimConstants.TickRate);
        Assert.Equal(pack.Count(e => e is CinderHound), there.Count);
        // Running with the train, it faces along the line: that's the model's −Z.
        var rel = start - eye;
        var was = there.MinBy(h => (h.At - new Vector3((float)rel.X, (float)rel.Y, (float)rel.Z)).Length());
        var along = Vector3.Normalize(new Vector3((float)near.Tangent.X, 0, (float)near.Tangent.Z));
        var outward = Vector3.Normalize(new Vector3((float)(start.X - near.Position.X), 0, (float)(start.Z - near.Position.Z)));
        Assert.True(Vector3.Dot(was.Facing, along) > 0.9f, $"facing {was.Facing} along {along}");
        // Scattered at 21 m/s (the scene's speed is the train's: this one's standing, so it runs on ahead of where it was).
        pack.Remove(hound);
        scene.Scattered(hound, Staging.StrikeTick + 1, 21);
        var off = Hounds(2).Except(there).ToList();
        var gone = Assert.Single(off);
        var moved = gone.At - was.At;
        Assert.True(Vector3.Dot(moved, outward) > 9, $"out across the line: {Vector3.Dot(moved, outward)} m");
        Assert.True(Vector3.Dot(moved, along) > 20, $"on along it, slowing: {Vector3.Dot(moved, along)} m");
        Assert.True(Vector3.Dot(gone.Facing, outward) > 0.5f, $"turned off the line: facing {gone.Facing}, out {outward}");
        Assert.Equal(there.Count - 1, Hounds(CreatureArt.HoundRunOffSeconds + 0.2).Count);
    }

    [Theory]
    [InlineData(EnemyKind.Climber, "climber", true)]
    [InlineData(EnemyKind.Whistler, "whistler", true)]
    [InlineData(EnemyKind.CinderHound, "cinder_hound", true)]
    [InlineData(EnemyKind.Ribbit, "ribbit", false)]
    [InlineData(EnemyKind.Gaunt, "gaunt", false)]
    [InlineData(EnemyKind.Switchman, "switchman", false)]
    public void OneLetGoOfInSightIsSeenGoingOffIntoTheDarkThenIsGone(EnemyKind kind, string asset, bool aboard)
    {
        // GreyboxScene.Retreating (note 458): the sim has one gone the tick it's done with it (a Climber outnumbered, a Whistler
        // found in its gap, a hound over the side, a pack that's eaten, the Gaunt with its loot, a Switchman at the lever). The
        // scene that saw it last frame draws it going: down off the train, out from the line, until CreatureArt.Retreat's time.
        var tuning = DataFile.Load<Sim.Train.TrainTuning>(Path.Combine(Content, Sim.Train.TrainTuning.File));
        var line = Sim.Rail.RailLine.Load(Path.Combine(Content, "lines", "test-loop.json"));
        var train = new Sim.Train.TrainOnLine(new Sim.Train.TrainDynamics(Sim.Train.Consist.Uniform(tuning, 6, 1)), line, 1200);
        int car = 2;
        var f = train.Frames[car];
        var e = Enemy.Blank(kind, 90, kind == EnemyKind.Ribbit ? 60 : 0);
        if (aboard)
            e.Restore(SpinePhase.Commit, 1, 3, car, new Double3(0.3, f.Shape.RoofHeight, 2), 0, 0, 0, 0, 0);
        else
        {
            var at = f.ToWorld(new Double3(f.Shape.HalfWidth + 4, 0, 0));
            double hint = train.Cars[car].FrontDistance;
            e.Restore(SpinePhase.Telegraph, 1, 3, Enemy.Loose, at with { Y = Sim.Player.PlayerMotor.GroundAt(at, line, ref hint) }, 0, 0, 0, -1, 0);
        }
        var start = e.WorldPosition(train);
        var eye = f.ToWorld(new Double3(-12, 6, 0));
        var staged = new List<Enemy> { e };
        var scene = new GreyboxScene { Look = Look, Time = 0.37, Enemies = staged, Tick = Staging.StrikeTick };
        List<Vector3> Drawn(double after)
        {
            scene.Tick = Staging.StrikeTick + 1 + (long)Math.Round(after * Sim.SimConstants.TickRate);
            var mesh = new MeshBuilder();
            scene.Build(mesh, train, eye);
            return [.. mesh.Instances.Where(i => i.Asset.Name.Contains(asset, StringComparison.OrdinalIgnoreCase)).Select(i => i.Model.Translation)];
        }
        // Where it's drawn: its model's place (the middle of them, were it several).
        static Vector3 At(List<Vector3> drawn)
        {
            Assert.NotEmpty(drawn);
            return drawn.Aggregate(Vector3.Zero, (a, b) => a + b) / drawn.Count;
        }
        var was = At(Drawn(-1.0 / Sim.SimConstants.TickRate));
        staged.Remove(e);
        scene.Retreated(e, (uint)(Staging.StrikeTick + 1), 0);
        var going = At(Drawn(1));
        // Out from the line on its own side (the eye's on the train's other side): further from the eye, and off the train.
        var rel = start - eye;
        var from = new Vector3((float)rel.X, (float)rel.Y, (float)rel.Z);
        double outward = CreatureArt.Retreat(kind)!.Value.Out;
        Assert.True((going - from).Length() > 0.8 * outward, $"{kind}: moved {(going - from).Length():0.0} m from where it was");
        Assert.True(going.Length() > was.Length() + 0.5 * outward, $"{kind}: away from the eye: {was.Length():0.0} → {going.Length():0.0} m");
        Assert.Empty(Drawn(CreatureArt.Retreat(kind)!.Value.Seconds + 0.2));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AClimberTheSimLetsGoOfIsSeenGoingAndOneKilledFallsInstead(bool killed)
    {
        // GreyboxScene.Remember (note 458): gone from the sim between frames and not killed, it's let go of (Retreating);
        // killed by a blow (a HitConfirm that says so), it falls where it was (Deaths) and doesn't also run off.
        var tuning = DataFile.Load<Sim.Train.TrainTuning>(Path.Combine(Content, Sim.Train.TrainTuning.File));
        var line = Sim.Rail.RailLine.Load(Path.Combine(Content, "lines", "test-loop.json"));
        var train = new Sim.Train.TrainOnLine(new Sim.Train.TrainDynamics(Sim.Train.Consist.Uniform(tuning, 6, 1)), line, 1200);
        var f = train.Frames[2];
        var climber = new Climber(91);
        climber.Restore(SpinePhase.Commit, 1, 1, 2, new Double3(0.3, f.Shape.RoofHeight, 2), 0, 0, 0, 2, -1);
        var eye = f.ToWorld(new Double3(-12, 6, 0));
        var staged = new List<Enemy> { climber };
        var scene = new GreyboxScene { Look = Look, Time = 0.37, Enemies = staged, Tick = Staging.StrikeTick };
        int Drawn(long tick)
        {
            scene.Tick = tick;
            var mesh = new MeshBuilder();
            scene.Build(mesh, train, eye);
            return mesh.Instances.Count(i => i.Asset.Name.Contains("climber", StringComparison.OrdinalIgnoreCase));
        }
        Assert.Equal(1, Drawn(Staging.StrikeTick));
        staged.Clear();
        if (killed)
            scene.Hits = [new Sim.Combat.HitConfirm(1, (uint)Staging.StrikeTick + 1, climber.Id, EnemyKind.Climber, 1, Sim.Combat.HitSource.Melee, climber.WorldPosition(train), eye, true)];
        Assert.Equal(1, Drawn(Staging.StrikeTick + 1));
        // The beat the scene saw begin that frame: one, and the right one.
        Assert.Equal(killed ? "killed" : "retreated", Assert.Single(scene.NewBeats).Beat);
        scene.Hits = null;
        // A second on, it's still seen: falling, or going.
        Assert.Equal(1, Drawn(Staging.StrikeTick + 1 + (long)Sim.SimConstants.TickRate));
    }

    [Fact]
    public void ACarHuggerClubbedToDeathDropsOffItsCarsEndAndIsLeftThere()
    {
        // Note 458: killed (from the rear platform, the guard van's door), it isn't one Deaths rolled where it was (Falls
        // leaves it out: it's latched on the car's end), and it vanished 1-2 m from the crew. Now its grip's gone: it's left
        // where it was in the world, clear of the car's end, and drops onto the track as the train runs on.
        var tuning = DataFile.Load<Sim.Train.TrainTuning>(Path.Combine(Content, Sim.Train.TrainTuning.File));
        var line = Sim.Rail.RailLine.Load(Path.Combine(Content, "lines", "test-loop.json"));
        var train = new Sim.Train.TrainOnLine(new Sim.Train.TrainDynamics(Sim.Train.Consist.Uniform(tuning, 6, 1)), line, 1200);
        int rear = train.Dynamics.Consist.Vehicles[^1].Id;
        var f = train.Frames[rear];
        var hugger = new CarHugger(46);
        hugger.Restore(SpinePhase.Commit, 4, 12, rear, new Double3(0, 1.0, f.Shape.HalfLength + 0.4), 0, 0, 0, 0, 0);
        var eye = f.ToWorld(new Double3(-8, 3, f.Shape.HalfLength + 6));
        var staged = new List<Enemy> { hugger };
        var scene = new GreyboxScene { Look = Look, Time = 0.37, Enemies = staged, Tick = Staging.StrikeTick };
        List<Vector3> Drawn(double after)
        {
            scene.Tick = Staging.StrikeTick + (long)Math.Round(after * Sim.SimConstants.TickRate);
            var mesh = new MeshBuilder();
            scene.Build(mesh, train, eye);
            return [.. mesh.Instances.Where(i => i.Asset.Name.Contains("car_hugger", StringComparison.OrdinalIgnoreCase)).Select(i => i.Model.Translation)];
        }
        var was = Assert.Single(Drawn(-1.0 / Sim.SimConstants.TickRate));
        staged.Clear();
        scene.Killed(hugger, (uint)Staging.StrikeTick, new Vector3(0, 0, 1));
        var down = Assert.Single(Drawn(0.8));
        Assert.True(down.Y < was.Y - 0.5, $"dropped: {was.Y:0.00} → {down.Y:0.00}");
        // Clear of the car's end: further back from the car's middle than it hung.
        var middle = f.ToWorld(default) - eye;
        var mid = new Vector3((float)middle.X, (float)middle.Y, (float)middle.Z);
        Assert.True((down - mid).Length() > (was - mid).Length() + 1, $"clear of the end: {(was - mid).Length():0.0} → {(down - mid).Length():0.0} m");
        Assert.Empty(Drawn(Effects.DeathSeconds + 0.2));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ACarCutLooseBurningBurnsOnAsItRollsAway(bool cut)
    {
        // Note 458 (GreyboxScene.Riding): the sim's done with a fire the tick its car's off the train, and its flames went
        // out the moment the car was cut. Cut loose, it burns on, on the car, as the Car Hugger rides its car away. Not cut,
        // a fire gone is one put out: nothing's left of it.
        var tuning = DataFile.Load<Sim.Train.TrainTuning>(Path.Combine(Content, Sim.Train.TrainTuning.File));
        var line = Sim.Rail.RailLine.Load(Path.Combine(Content, "lines", "test-loop.json"));
        var train = new Sim.Train.TrainOnLine(new Sim.Train.TrainDynamics(Sim.Train.Consist.Uniform(tuning, 6, 1)), line, 1200);
        int car = 4;
        var fire = CarFire.In(24, train, car, 1.5, new CarFireTuning());
        fire.Restore(SpinePhase.Punish, 5, 1, car, fire.Local, 0, 0, 0, 0.7, 0);
        var eye = train.Frames[car].ToWorld(new Double3(-6, 3, 0));
        var staged = new List<Enemy> { fire };
        var scene = new GreyboxScene { Look = Look, Time = 0.37, Enemies = staged, Tick = Staging.StrikeTick };
        int Flames(long tick)
        {
            scene.Tick = tick;
            var mesh = new MeshBuilder();
            scene.Build(mesh, train, eye);
            return mesh.AdditiveFx.Count;
        }
        int burning = Flames(Staging.StrikeTick);
        staged.Clear();
        int none = Flames(Staging.StrikeTick + 1000);
        Assert.True(burning > none, $"burning {burning}, no fire {none}");
        // Again, the fire there; then its car's cut from the train (its front end open: the car ahead's in another rake),
        // and the sim's done with it.
        scene = new GreyboxScene { Look = Look, Time = 0.37, Enemies = staged = [fire], Tick = Staging.StrikeTick };
        Flames(Staging.StrikeTick);
        if (cut)
            Assert.True(train.Uncouple(train.VehicleAhead(car)));
        staged.Clear();
        int after = Flames(Staging.StrikeTick + 1);
        int later = Flames(Staging.StrikeTick + 2 * (long)Sim.SimConstants.TickRate);
        if (cut)
            Assert.True(after > none && later > none, $"cut loose: {after}, then {later}, against {none}");
        else
            Assert.Equal(none, later);
    }

    [Fact]
    public void TheOneTheCarHuggerSwallowsIsBentIntoItsMouthWhereverTheyWereCaught()
    {
        // GreyboxScene.Hung: the sim holds them wherever in reach they were caught; the scene stands them SwallowReach in
        // front of the mouth on their own floor, facing into it.
        var tuning = DataFile.Load<Sim.Train.TrainTuning>(Path.Combine(Content, Sim.Train.TrainTuning.File));
        var line = Sim.Rail.RailLine.Load(Path.Combine(Content, "lines", "test-loop.json"));
        var train = new Sim.Train.TrainOnLine(new Sim.Train.TrainDynamics(Sim.Train.Consist.Uniform(tuning, 6, 1)), line, 1200);
        var threats = Staging.Hugger(Staging.Threats(train), "swallow");
        var caught = Staging.Swallowed(train);
        var eye = caught.Feet + new Double3(1, 1.5, -3);
        var scene = new GreyboxScene { Look = Look, Time = 0.37, Enemies = threats, Crew = [caught], Tick = Staging.StrikeTick };
        scene.Build(new MeshBuilder(), train, eye);
        Assert.True(Look.Art.Creatures!.Clutches.TryGetValue(caught.Id, out var mouth));
        Assert.False(mouth.Hung);
        var at = eye + new Double3(mouth.At.X, mouth.At.Y, mouth.At.Z);
        // Where the scene stands them: in front of the mouth, along the way into it, not where the sim caught them.
        var feet = (at - new Double3(mouth.Forward.X, 0, mouth.Forward.Z) * CreatureArt.SwallowReach) with { Y = caught.Feet.Y };
        Assert.True((feet - caught.Feet).Length > 0.5, "moved from where they were caught");
        var rear = train.Frames[train.Dynamics.Consist.Vehicles[^1].Id];
        var local = rear.ToLocal(feet);
        Assert.InRange(local.X, -0.9, 0.1);
        Assert.InRange(rear.Shape.HalfLength - local.Z, 0.2, 1.6);
    }

    /// <summary>
    /// The Moose (note 339, docs/design/creatures/moose.md): no HUD shows its meter, its ears and its posture do. Grazing, it
    /// browses; past listenAt its head's up (listen), past warnAt the ears are flat and the ridge up (warn). Riled, it goes as
    /// fast as it's going (a walk, a trot), squares up, charges, skids round, jams its rack, rams, pins; and none of it puts
    /// a part of it through another (its rack is its own two bones, fitted as capsules from its mesh).
    /// </summary>
    [Fact]
    public void TheMooseShowsItsTemperAndStaysOutOfItself()
    {
        var t = Art.MooseTuning;
        string Clip(MooseMode mode, double aggro = 100, double pace = 0) => CreatureArt.MooseClip(mode, aggro, (float)pace, 1, 1, t).Clip;
        Assert.Equal("graze", Clip(MooseMode.Graze, 0));
        Assert.Equal("graze", Clip(MooseMode.Graze, t.ListenAt - 1));
        Assert.Equal("listen", Clip(MooseMode.Graze, t.ListenAt));
        Assert.Equal("warn", Clip(MooseMode.Graze, t.WarnAt));
        // A train going by, a rival: it tosses its head after it.
        Assert.Equal("trainPass", CreatureArt.MooseClip(MooseMode.Graze, 0, 0, 1, 1, t, trainPassing: true).Clip);
        Assert.Equal("trot", Clip(MooseMode.Hunt, pace: t.HuntSpeed));
        Assert.Equal("walk", Clip(MooseMode.Hunt, pace: t.SearchSpeed));
        Assert.Equal("warn", Clip(MooseMode.Hunt));
        Assert.Equal("search", Clip(MooseMode.Search, pace: t.SearchSpeed));
        Assert.Equal("strut", Clip(MooseMode.Home, 0, t.SearchSpeed));
        Assert.Equal("ram", Clip(MooseMode.Ram));
        Assert.Equal(("squareUp", 1.7, false), CreatureArt.MooseClip(MooseMode.SquareUp, 100, 0, 3, 1.7, t));
        Assert.Equal(("overrun", 0.4, false), CreatureArt.MooseClip(MooseMode.Wheel, 100, 0, 3, 0.4, t));
        Assert.Equal("charge", Clip(MooseMode.Charge, pace: t.ChargeSpeed));
        Assert.Equal("snag", Clip(MooseMode.Snag));
        Assert.Equal("pin", Clip(MooseMode.Pin));
        // A ram's clip is one ram: the sim's ramEvery.
        Assert.Equal(t.RamEvery, Get("moose").Clip("ram")!.Duration, 2);
        var model = Get("moose");
        foreach (var mode in Enum.GetValues<MooseMode>())
            foreach (double pace in new[] { 0, t.SearchSpeed, t.HuntSpeed })
                foreach (double aggro in new[] { 0, t.ListenAt, t.WarnAt })
                    Assert.Contains(CreatureArt.MooseClip(mode, aggro, (float)pace, 0, 0, t).Clip, model.ClipNames);
        var through = Clearance.Mesh(model).Where(o => o.Depth > Clearance.Touching).ToList();
        Assert.True(through.Count == 0, string.Join("; ", through.Select(o => $"{o.Clip} {o.Pair} {o.Depth:0.000} at {o.At:0.00} s")));
    }

    [Fact]
    public void TheOneTheMoosePinsIsUnderItsRack()
    {
        // GreyboxScene: the sim holds them where it ran them down; the scene stands the Moose over them (MoosePinReach back,
        // facing them) and lays them on their back with their head toward it (CreatureArt.Pins, held_pinned).
        var tuning = DataFile.Load<Sim.Train.TrainTuning>(Path.Combine(Content, Sim.Train.TrainTuning.File));
        var line = Sim.Rail.RailLine.Load(Path.Combine(Content, "lines", "test-loop.json"));
        var train = new Sim.Train.TrainOnLine(new Sim.Train.TrainDynamics(Sim.Train.Consist.Uniform(tuning, 6, 1)), line, 1200);
        var threats = Staging.Moose([], train, "pin");
        var pinned = Staging.MooseCrewmate(train, "pin");
        var eye = pinned.Feet + new Double3(4, 1.6, 1);
        var scene = new GreyboxScene { Look = Look, Time = 0.37, Enemies = threats, Crew = [pinned with { Act = null }] };
        var mesh = new MeshBuilder();
        scene.Build(mesh, train, eye);
        Assert.True(Look.Art.Creatures!.Pins.TryGetValue(pinned.Id, out var pin));
        var moose = (Moose)threats.Single();
        var to = new Vector3((float)(pinned.Feet.X - moose.Local.X), 0, (float)(pinned.Feet.Z - moose.Local.Z));
        Assert.True(Vector3.Dot(Vector3.Normalize(to), pin.Forward) > 0.99f, "laid facing away from it, their head toward it");
        Assert.Equal(CrewPose.HeldPinned, CrewActs.HeldPose(EnemyKind.Moose));
    }

    /// <summary>
    /// The Gannet (note 340, docs/design/creatures/gannet.md §5): what it does is the sim's mode, and its clips play it out.
    /// Folding, the fold then the dive, its spear's tip the sim's dive point; stuck, thrashing with the spear in the planks,
    /// then the tear free ending with stuckSeconds; out of a dive it stabs, off a pin it's driven, else it climbs; the bank
    /// ends in the swoop; and pinning, each peck strikes on the beat Gannet.PecksLanded counts, its wind-up before it. No
    /// part of it goes through another in any clip.
    /// </summary>
    [Fact]
    public void TheGannetFoldsStrikesAndPecksOnTheSimsBeat()
    {
        var t = Art.GannetTuning;
        var model = Get("gannet");
        string Clip(GannetMode mode, double seconds, GannetMode? was = null) => CreatureArt.GannetClip(mode, was, seconds, seconds, t).Clip;
        // The lengths GannetClip times things by are the clips'.
        foreach (var (clip, seconds) in new[] { ("fold", CreatureArt.GannetFoldSeconds), ("stab", CreatureArt.GannetStabSeconds),
            ("tearFree", CreatureArt.GannetTearFreeSeconds), ("swoop", CreatureArt.GannetSwoopSeconds), ("land", CreatureArt.GannetLandSeconds),
            ("peckWindup", CreatureArt.GannetWindupSeconds), ("peck", CreatureArt.GannetPeckSeconds), ("driven", CreatureArt.GannetDrivenSeconds),
            ("circle", CreatureArt.GannetCircleSeconds) })
            Assert.Equal(seconds, model.Clip(clip)!.Duration, 3);
        foreach (var mode in Enum.GetValues<GannetMode>().Where(m => m != GannetMode.Away))
            foreach (var was in new GannetMode?[] { null, GannetMode.Fold, GannetMode.Pin })
                foreach (double s in new[] { 0, 0.3, 1, 2.9, 3.0, 5.95, 11.95, 20 })
                    Assert.Contains(CreatureArt.GannetClip(mode, was, s, s, t).Clip, model.ClipNames);
        Assert.Equal("soar", Clip(GannetMode.Soar, 1));
        Assert.Equal("circle", Clip(GannetMode.Soar, CreatureArt.GannetSoarHold + 1));
        Assert.Equal("hang", Clip(GannetMode.Hang, 1));
        Assert.Equal("fold", Clip(GannetMode.Fold, 0.1));
        Assert.Equal("dive", Clip(GannetMode.Fold, t.FoldSeconds - 0.1));
        Assert.Equal("stuck", Clip(GannetMode.Stuck, 1));
        Assert.Equal("tearFree", Clip(GannetMode.Stuck, t.StuckSeconds - 0.1));
        Assert.Equal("stab", Clip(GannetMode.Climb, 0.1, GannetMode.Fold));
        Assert.Equal("driven", Clip(GannetMode.Climb, 0.1, GannetMode.Pin));
        Assert.Equal("climb", Clip(GannetMode.Climb, 0.1, GannetMode.Stuck));
        Assert.Equal("bank", Clip(GannetMode.Bank, 1));
        Assert.Equal("swoop", Clip(GannetMode.Bank, t.BankSeconds - 0.1));
        Assert.Equal("land", Clip(GannetMode.Pin, 0.1));
        Assert.Equal("pin", Clip(GannetMode.Pin, 1.5));
        var g = new Gannet(1);
        for (int k = 1; k <= t.Pecks; k++)
        {
            double beat = k * t.PeckEvery;
            g.Restore(SpinePhase.Grab, beat + 0.01, t.Health, 2, default, 0, 0, (double)GannetMode.Pin, -1, 1, holding: 1);
            Assert.Equal(k, g.PecksLanded(t));
            // The strike's contact frame lands on the beat the sim counts the peck on; the wind-up's before it.
            var (clip, at, loop) = CreatureArt.GannetClip(GannetMode.Pin, null, beat, beat, t);
            Assert.Equal(("peck", CreatureArt.GannetPeckStrike, false), (clip, Math.Round(at, 6), loop));
            Assert.Equal("peckWindup", Clip(GannetMode.Pin, beat - CreatureArt.GannetPeckStrike - 0.3));
        }
        // The spear's tip is where the sim has it in the dive (its origin) and buried in the planks under it when stuck.
        int tip = model.Skeleton.IndexOf("beak_tip");
        Vector3 Tip(string clip, double time, bool loop = true) => Art.Joints("gannet", clip, time, loop).ElementAt(tip);
        Assert.InRange(Tip("dive", 0.2).Length(), 0, 0.06f);
        Assert.InRange(Tip("fold", CreatureArt.GannetFoldSeconds, loop: false).Length(), 0, 0.06f);
        Assert.InRange(Tip("stuck", 0.5).Y, -0.45f, -0.2f);
        var through = Clearance.Mesh(model).Where(o => o.Depth > Clearance.Touching).ToList();
        Assert.True(through.Count == 0, string.Join("; ", through.Select(o => $"{o.Clip} {o.Pair} {o.Depth:0.000} at {o.At:0.00} s")));
    }

    [Fact]
    public void TheOneTheGannetPinsIsUnderItsFootAndItsPecksLandOnTheirHead()
    {
        // GreyboxScene: the sim has it stood on them where it came down; the scene stands it with its right foot's web on their
        // chest, turned along their car, and lays them on their back along its heading, their head out in front of it
        // (CreatureArt.Pins, held_pinned).
        var tuning = DataFile.Load<Sim.Train.TrainTuning>(Path.Combine(Content, Sim.Train.TrainTuning.File));
        var line = Sim.Rail.RailLine.Load(Path.Combine(Content, "lines", "test-loop.json"));
        var train = new Sim.Train.TrainOnLine(new Sim.Train.TrainDynamics(Sim.Train.Consist.Uniform(tuning, 6, 1)), line, 1200);
        var threats = Staging.Gannet([], train, "pin");
        var pinned = Staging.GannetWalker(train, "pin");
        var eye = pinned.Feet + new Double3(4, 2, 1);
        var scene = new GreyboxScene { Look = Look, Time = 0.37, Enemies = threats, Crew = [pinned with { Act = null }] };
        var mesh = new MeshBuilder();
        scene.Build(mesh, train, eye);
        Assert.True(Look.Art.Creatures!.Pins.TryGetValue(pinned.Id, out var pin));
        var ahead = train.Frames[Staging.GannetCar].Back * -1;
        Assert.True(Vector3.Dot(pin.Forward, -new Vector3((float)ahead.X, (float)ahead.Y, (float)ahead.Z)) > 0.99f,
            "laid along the car, their head out ahead of it under its spear");
        Assert.Equal(CrewPose.HeldPinned, CrewActs.HeldPose(EnemyKind.Gannet));
        // Their chest and head as held_pinned lays them (behind where the sim has them, their feet ahead): under the web of
        // its right foot, and their face where the peck's strike lands.
        var crew = Get("crew");
        var lying = Art.Joints("crew", "held_pinned", 0.3, true).ToList();
        float chest = lying[crew.Skeleton.IndexOf("spine_03")].Z, head = lying[crew.Skeleton.IndexOf("head")].Z;
        Assert.InRange(chest, CreatureArt.PinnedChest - 0.05f, CreatureArt.PinnedChest + 0.05f);
        var model = Get("gannet");
        var foot = Art.Joints("gannet", "pin", 0.5, true).ElementAt(model.Skeleton.IndexOf("toes_r"));
        Assert.InRange(foot.X, CreatureArt.GannetPinChest.X - 0.05f, CreatureArt.GannetPinChest.X + 0.05f);
        Assert.InRange(-foot.Z, CreatureArt.GannetPinChest.Y - 0.25f, CreatureArt.GannetPinChest.Y + 0.05f);
        var strike = Art.Joints("gannet", "peck", CreatureArt.GannetPeckStrike, false).ElementAt(model.Skeleton.IndexOf("beak_tip"));
        // (The face a hand on from the head's joint, at the skull's base.)
        float headAhead = CreatureArt.GannetPinChest.Y + (head - chest) + 0.1f;
        Assert.InRange(strike.X, CreatureArt.GannetPinChest.X - 0.1f, CreatureArt.GannetPinChest.X + 0.1f);
        Assert.InRange(-strike.Z, headAhead - 0.15f, headAhead + 0.15f);
        Assert.InRange(strike.Y, 0, 0.4f);
    }

    [Fact]
    public void TheTippyToesiePulledOffRecoilsWhereItWasThenScuttles()
    {
        // App. A.5 "any friend hits or pulls it -> it flees": the sim hides it again at once (Dormant). Where it was, it's
        // seen jerked back off them and held a beat (its recoil), not moving; then it scuttles off, and then it's gone.
        var tuning = DataFile.Load<Sim.Train.TrainTuning>(Path.Combine(Content, Sim.Train.TrainTuning.File));
        var line = Sim.Rail.RailLine.Load(Path.Combine(Content, "lines", "test-loop.json"));
        var train = new Sim.Train.TrainOnLine(new Sim.Train.TrainDynamics(Sim.Train.Consist.Uniform(tuning, 6, 1)), line, 1200);
        Vector3? Tippy(double ago)
        {
            var threats = Staging.Tippy(Staging.Threats(train), train, string.Create(System.Globalization.CultureInfo.InvariantCulture, $"recoil:{ago}"));
            var at = threats.OfType<TippyToesie>().First().WorldPosition(train);
            var scene = new GreyboxScene { Look = Look, Time = 0.37, Enemies = threats };
            var mesh = new MeshBuilder();
            var eye = at + new Double3(2, 1.5, 3);
            scene.Build(mesh, train, eye);
            var drawn = mesh.Instances.Where(i => i.Asset.Name.Contains("tippy_toesie", StringComparison.OrdinalIgnoreCase)).ToList();
            return drawn.Count == 0 ? null : drawn[0].Model.Translation;
        }
        var start = Tippy(0.02);
        Assert.NotNull(start);
        // Held where it was through its recoil...
        Assert.True(Vector3.Distance(start!.Value, Tippy(0.3)!.Value) < 0.01f, "still, recoiling");
        // ...then off, still seen past the old half second of scuttle...
        Assert.True(Tippy(0.6) is { } going && Vector3.Distance(start.Value, going) > 0.2f, "scuttling off");
        // ...and gone.
        Assert.Null(Tippy(1.5));
    }

    [Fact]
    public void TheCarHuggerCutLooseRidesItsCarOffNotBlinkingOut()
    {
        // GreyboxScene.Riding (App. A.3, "cut loose, it goes with its car into the dark"): the sim's done with it the tick its
        // car's cut from the train; the scene keeps it clamped on that car as it falls behind. Gone off a car still in the
        // train (killed, say), it isn't.
        var tuning = DataFile.Load<Sim.Train.TrainTuning>(Path.Combine(Content, Sim.Train.TrainTuning.File));
        var line = Sim.Rail.RailLine.Load(Path.Combine(Content, "lines", "test-loop.json"));
        int Huggers(bool cut)
        {
            var train = new Sim.Train.TrainOnLine(new Sim.Train.TrainDynamics(Sim.Train.Consist.Uniform(tuning, 6, 1)), line, 1200);
            var threats = Staging.Threats(train);
            var hugger = threats.OfType<CarHugger>().First(h => h.Attached >= 0);
            int car = hugger.Attached;
            var eye = train.Frames[car].ToWorld(new Double3(-5.5, 1.7, train.Frames[car].Shape.HalfLength + 7.5));
            var scene = new GreyboxScene { Look = Look, Time = 0.37, Enemies = threats, Tick = Staging.StrikeTick };
            scene.Build(new MeshBuilder(), train, eye);
            if (cut)
                Assert.True(train.Uncouple(train.VehicleAhead(car)));
            hugger.Restore(SpinePhase.Gone, 0, hugger.Health, car, hugger.Local, 0, 0, 0, hugger.Extra, hugger.Extra2);
            var mesh = new MeshBuilder();
            scene.Tick = Staging.StrikeTick + Sim.SimConstants.TickRate;
            scene.Build(mesh, train, eye);
            return mesh.Instances.Count(i => i.Asset.Name.Contains("car_hugger", StringComparison.OrdinalIgnoreCase));
        }
        Assert.True(Huggers(cut: true) > 0, "still on its car, cut loose");
        Assert.Equal(0, Huggers(cut: false));
    }

    [Fact]
    public void TheRibbitWithItsCatchFrozenCreepsInOnThemThenDevoursThem()
    {
        // App. A.6 GRAB: the tongues hold them and the leader hops in at a quarter speed, stopping 0.8 m short. Out in the
        // pack's line it creeps; on them it devours (and its own tongue's in them, not drawn out to them).
        var tuning = DataFile.Load<Sim.Train.TrainTuning>(Path.Combine(Content, Sim.Train.TrainTuning.File));
        var line = Sim.Rail.RailLine.Load(Path.Combine(Content, "lines", "test-loop.json"));
        var train = new Sim.Train.TrainOnLine(new Sim.Train.TrainDynamics(Sim.Train.Consist.Uniform(tuning, 6, 1)), line, 1200);
        var them = Staging.Lone(train).Feet;
        float Leader(string mode)
        {
            var leader = Staging.Ribbits(Staging.Threats(train), mode, train).OfType<Ribbit>().MinBy(r => r.Id)!;
            var off = (leader.Local - them) with { Y = 0 };
            return (float)off.Length;
        }
        Assert.Equal("creep", CreatureArt.RibbitClip(SpinePhase.Grab, Leader("tongue")));
        Assert.Equal("devour", CreatureArt.RibbitClip(SpinePhase.Grab, Leader("devour")));
        Assert.Equal("tongue", CreatureArt.RibbitClip(SpinePhase.Commit, Leader("devour")));
        // Where the sim's hop stops is close enough to be on them.
        Assert.Equal("devour", CreatureArt.RibbitClip(SpinePhase.Grab, 0.8f));
    }

    [Fact]
    public void TheTrackDollFlickersOutWhereItWasNotWalkingOff()
    {
        // GreyboxScene.Vanishing: come at, the sim moves it to another car from one tick to the next; the scene that saw it
        // last frame draws it where it was for a flicker (and its dust), then only where it is.
        var tuning = DataFile.Load<Sim.Train.TrainTuning>(Path.Combine(Content, Sim.Train.TrainTuning.File));
        var line = Sim.Rail.RailLine.Load(Path.Combine(Content, "lines", "test-loop.json"));
        var train = new Sim.Train.TrainOnLine(new Sim.Train.TrainDynamics(Sim.Train.Consist.Uniform(tuning, 6, 1)), line, 1200);
        var threats = Staging.Threats(train);
        var doll = threats.OfType<Sim.Enemies.TrackDoll>().First(d => d.Attached == 0);
        threats.RemoveAll(e => e is Sim.Enemies.TrackDoll && e != doll);
        var room = train.Frames[2].Shape.Interior!.Value;
        doll.Restore(SpinePhase.Punish, 3, 1, 2, room.Centre with { Y = room.Min.Y }, 0, 0, 0, 0, 0);
        var eye = doll.WorldPosition(train) + new Double3(0.5, 1.4, -4);
        var scene = new GreyboxScene { Look = Look, Time = 0.37, Enemies = threats };
        int Dolls(double at)
        {
            scene.Tick = Staging.StrikeTick + (long)Math.Round(at * Sim.SimConstants.TickRate);
            var mesh = new MeshBuilder();
            scene.Build(mesh, train, eye);
            return mesh.Instances.Count(i => i.Asset.Name.Contains("track_doll", StringComparison.OrdinalIgnoreCase));
        }
        int one = Dolls(0);
        Assert.True(one > 0);
        // Come at: it's in car 3 now.
        doll.Restore(SpinePhase.Punish, 3, 1, 3, train.Frames[3].Shape.Interior!.Value.Centre with { Y = room.Min.Y }, 0, 0, 0, 0, 0);
        Assert.Equal(2 * one, Dolls(1.0 / Sim.SimConstants.TickRate));
        Assert.Equal(one, Dolls(0.5));
        Assert.Equal(one, Dolls(Effects.VanishSeconds + 0.2));
    }

    [Fact]
    public void TheDeadLieAsTheirRagdollLies()
    {
        // The staged body (Staging.Bodies: a crewmate dead on car 3's roof, settled for 90 ticks), as the scene draws it.
        var tuning = DataFile.Load<Sim.Train.TrainTuning>(Path.Combine(Content, Sim.Train.TrainTuning.File));
        var line = Sim.Rail.RailLine.Load(Path.Combine(Content, "lines", "test-loop.json"));
        var train = new Sim.Train.TrainOnLine(new Sim.Train.TrainDynamics(Sim.Train.Consist.Uniform(tuning, 6, 1)), line, 1200);
        var body = Staging.Bodies(train, Content).All.Single(b => b.Kind == Sim.Physics.BodyKind.Ragdoll);
        var frame = train.Frames[body.Parent];
        var origin = frame.ToWorld(body.Pbd.Particles[2].Position);
        var joints = body.Pbd.Particles.Select(p => frame.ToWorld(p.Position).RelativeTo(origin)).ToArray();
        Assert.Equal(CreatureArt.RagdollJoints, joints.Length);

        var one = Draw(joints);
        Assert.InRange(one.Length / 3, 2000, 9000);
        Assert.Equal(one.Select(v => v.Position), Draw(joints).Select(v => v.Position)); // deterministic
        // All of it lies where the body is (a bone swung the wrong way would put a limb through the roof or into the air)...
        var min = joints.Aggregate(Vector3.Min) - new Vector3(0.45f);
        var max = joints.Aggregate(Vector3.Max) + new Vector3(0.45f);
        foreach (var v in one)
        {
            Assert.True(float.IsFinite(v.Position.X) && float.IsFinite(v.Position.Y) && float.IsFinite(v.Position.Z));
            Assert.True(v.Position.X >= min.X && v.Position.Y >= min.Y && v.Position.Z >= min.Z
                && v.Position.X <= max.X && v.Position.Y <= max.Y && v.Position.Z <= max.Z, $"{v.Position} is outside the body");
        }
        // ...and the head, hands and feet reach their joints.
        foreach (int j in new[] { 0, 4, 6, 8, 10 })
            Assert.InRange(one.Min(v => Vector3.Distance(v.Position, joints[j])), 0, 0.15f);

        // A body folded to a point (every joint together) still draws, and nothing in it is NaN.
        var folded = Draw(new Vector3[CreatureArt.RagdollJoints]);
        Assert.All(folded, v => Assert.True(float.IsFinite(v.Position.X + v.Position.Y + v.Position.Z + v.Normal.X + v.Normal.Y + v.Normal.Z)));

        static Vertex[] Draw(Vector3[] joints)
        {
            var mesh = new MeshBuilder();
            Assert.True(Art.Corpse(mesh, joints, 9));
            return mesh.Flattened();
        }
    }

    [Fact]
    public void AFrameOfEightCrewAndTwelveEnemiesIsCheap()
    {
        // The brief: 8 crew and 12 enemies a frame. Posed on the CPU (bones only), skinned on the GPU: the frame-rate
        // targets (tuning/perf.json) leave the main thread's drawing a few milliseconds. (Sleepers are six ties, so this is
        // more models than it looks.)
        var mesh = new MeshBuilder();
        EnemyKind[] kinds = [.. Enumerable.Repeat(EnemyKind.CinderHound, 4), EnemyKind.Ribbit, EnemyKind.Ribbit, EnemyKind.CarHugger,
            EnemyKind.Gaunt, EnemyKind.Switchman, EnemyKind.Sleepers, EnemyKind.SootChildren, EnemyKind.TrackDoll];
        void Frame(double t)
        {
            mesh.Clear();
            for (int i = 0; i < 8; i++)
                Art.Crewmate(mesh, Matrix4x4.CreateTranslation(i, 0, -4), (CrewPose)(i % 6), t, i);
            for (int i = 0; i < kinds.Length; i++)
                Art.Enemy(mesh, Matrix4x4.CreateTranslation(i, 0, -12), kinds[i], SpinePhase.Commit, t + i, 0.4);
        }
        Frame(0);
        // The best of twenty frames, not their mean: what a frame costs, rather than how often a CI box (every test
        // assembly at once on two cores) took the thread away. A mean there ran to 166 ms for an 11 ms frame. Up to
        // sixty while none has come in under budget: on a box that busy even the best of twenty can land in a stretch
        // where another assembly holds every core.
        const int Frames = 20, MaxFrames = 60;
        double ms = double.MaxValue;
        for (int f = 0; f < MaxFrames && (f < Frames || ms >= 30); f++)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            Frame(f / 30.0);
            ms = Math.Min(ms, clock.Elapsed.TotalMilliseconds);
        }
        TestContext.Current.TestOutputHelper?.WriteLine($"{ms:0.0} ms a frame, {mesh.Flattened().Length / 3} triangles");
        // Generous (Debug builds, a loaded CI box); skinned on the CPU it was about 11 ms on a desktop Release build, and
        // this allowed 120.
        Assert.True(ms < 30, $"{ms:0.0} ms a frame");
    }

    [Fact]
    public void WithoutModelsItSaysSoAndTheGreyboxDraws()
    {
        var empty = Directory.CreateTempSubdirectory("dt-no-models");
        try
        {
            var art = new CreatureArt(Look, empty.FullName);
            var mesh = new MeshBuilder();
            Assert.False(art.Loaded);
            Assert.False(art.Crewmate(mesh, Matrix4x4.Identity, CrewPose.Walk, 0, 0));
            Assert.False(art.Enemy(mesh, Matrix4x4.Identity, EnemyKind.CinderHound, SpinePhase.Commit, 0, 0));
            Assert.Empty(mesh.Flattened());
        }
        finally
        {
            empty.Delete(true);
        }
    }

    [Fact]
    public void TheLightsAreLights()
    {
        // The crew's chest lamp, the Switchman's lantern glass and the Hollow's eyes are pure light; hound embers glow.
        foreach (var (name, clip) in new[] { ("crew", "idle"), ("switchman", "wait"), ("hollow", "idle") })
        {
            var mesh = new MeshBuilder();
            Art.Draw(mesh, name, clip, 0, true, Matrix4x4.Identity);
            Assert.Contains(mesh.Flattened(), v => v.Emissive >= 1);
        }
        var hound = Get("cinder_hound");
        Assert.Contains(hound.Materials, m => m.Texture == "ember_crack");
    }

    // ------------------------------------------------------------------------------------------------------------
    // Turntables

    static GpuContext Gpu()
    {
        try { return new GpuContext("creature tests"); }
        catch (GpuUnavailableException e) { Assert.Skip($"no Vulkan: {e.Message}"); throw; }
    }

    const int W = 480, H = 270;

    /// <summary>
    /// One PNG per clip: three angles on the top row, three moments of the clip on the middle row, and the same thing
    /// walked back through the fog on the bottom (8, 15, 25 m), all lit by a single warm lantern near it.
    /// </summary>
    [Theory]
    [MemberData(nameof(Models))]
    public void Turntable(string name)
    {
        var m = Get(name);
        using var gpu = Gpu();
        using var renderer = new GreyboxRenderer(gpu, W, H);
        Look.Dress(renderer);
        string dir = Path.Combine(Repo, "out", "shots", "creatures");
        Directory.CreateDirectory(dir);
        var b = Budgets[name];
        foreach (var clip in m.ClipNames)
        {
            bool loop = b.Loops.Contains(clip);
            double dur = m.Clip(clip).Duration;
            var sheet = new byte[W * 3 * H * 3 * 4];
            for (int row = 0; row < 3; row++)
                for (int col = 0; col < 3; col++)
                {
                    double angle, time, dist;
                    // The clinger is seen from outside the car (+X); everything else walks round.
                    double[] angles = name == "clinger" ? [55, 90, 130] : [35, 100, 215];
                    if (row == 0)
                        (angle, time, dist) = (angles[col], dur * 0.4, 0);
                    else if (row == 1)
                        (angle, time, dist) = (name == "clinger" ? 75 : 60, loop ? dur * col / 3.0 : dur * col / 2.0, 0);
                    else
                        (angle, time, dist) = (name == "clinger" ? 70 : 25, dur * 0.4, new[] { 8.0, 15.0, 25.0 }[col]);
                    var px = Shot(renderer, name, clip, time, loop, angle, dist);
                    for (int y = 0; y < H; y++)
                        Array.Copy(px, y * W * 4, sheet, ((row * H + y) * W * 3 + col * W) * 4, W * 4);
                }
            PngWriter.Write(Path.Combine(dir, $"{name}-{clip}.png"), sheet, W * 3, H * 3);
        }
    }

    /// <summary>Where the model sits and how big it is, for framing (the clinger on a hull, the sleeper across rails).</summary>
    static (Vector3 Base, float Size) Framing(string name) => name switch
    {
        "clinger" => (new Vector3(0, 1.3f, 0), 1.7f),
        "sleeper" => (Vector3.Zero, 3.0f),
        "soot_child" => (Vector3.Zero, 0.9f),
        "sheep" => (Vector3.Zero, 0.9f),
        "cinder_hound" => (Vector3.Zero, 1.2f),
        "hollow" => (Vector3.Zero, 2.2f),
        "dragger" => (new Vector3(0.2f, 0.1f, 0), 1.4f),
        "weight" => (new Vector3(0, 0.42f, 0.9f), 2.6f),
        "car_hugger" => (new Vector3(-0.2f, 0.4f, 1.5f), 3.6f),
        "tippy_toesie" => (Vector3.Zero, 2.5f),
        "whistler" => (Vector3.Zero, 2.3f),
        "ribbit" => (Vector3.Zero, 1.1f),
        "choir" => (Vector3.Zero, 1.3f),
        "gaunt" => (Vector3.Zero, 2.6f),
        "grumbler" => (Vector3.Zero, 1.9f),
        "stoker" => (new Vector3(0, -0.4f, 0.3f), 1.3f),
        "follower" => (Vector3.Zero, 0.3f),
        "climber" => (Vector3.Zero, 1.9f),
        "fire_fly" => (Vector3.Zero, 0.16f),
        "moose" => (Vector3.Zero, 3.4f),
        "gannet" => (Vector3.Zero, 3.6f),
        _ => (Vector3.Zero, 1.9f),
    };

    static byte[] Shot(GreyboxRenderer renderer, string name, string clip, double time, bool loop, double angleDeg, double far)
    {
        var (at, size) = Framing(name);
        float a = (float)(angleDeg * Math.PI / 180);
        // The camera orbits the model; angle 0 looks at its face (the model faces −Z).
        var dir = new Vector3(MathF.Sin(a), 0, -MathF.Cos(a));
        float dist = far > 0 ? (float)far : size * 1.35f + 0.8f;
        float lookY = name switch { "sleeper" => 0.15f, "clinger" => 1.3f, _ => size * 0.5f };
        var target = new Double3(at.X, lookY, at.Z);
        var eyeV = new Vector3(at.X, 0, at.Z) + dir * dist + new Vector3(0, far > 0 ? 1.7f : lookY + size * 0.25f, 0);
        var eye = new Double3(eyeV.X, eyeV.Y, eyeV.Z);
        var camera = Camera.LookAt(eye, target, far > 0 ? 40 : 50);
        var mesh = new MeshBuilder { Style = Look.Style };
        var e = new Vector3((float)eye.X, (float)eye.Y, (float)eye.Z);
        mesh.SurfaceOrigin = e;
        // The ground, and what the thing lives on: a car's side for the clinger, rails for the sleeper.
        mesh.Quad(new Vector3(-40, 0, -40) - e, new Vector3(-40, 0, 40) - e, new Vector3(40, 0, 40) - e, new Vector3(40, 0, -40) - e, Palette.MuddyOlive);
        if (name == "clinger")
            mesh.Box(new Vector3(-0.1f, 1.6f, 0) - e, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, new Vector3(0.1f, 1.6f, 3.5f), Palette.RustRed);
        if (name == "sleeper")
            foreach (float x in new[] { -0.72f, 0.72f })
                mesh.Box(new Vector3(x, 0.12f, 0) - e, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, new Vector3(0.04f, 0.07f, 6), Palette.IronGrey);
        // One warm lantern, off to the camera's left and a little in front of the subject.
        var side = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, dir));
        var lamp = new Vector3(at.X, 0, at.Z) + dir * 1.6f + side * 1.3f + new Vector3(0, Math.Max(1.2f, lookY + 0.6f), 0);
        mesh.PointLights.Add(new PointLight(lamp - e, Palette.LampAmber * 1.9f, 9f));
        var model = Matrix4x4.CreateTranslation(at - e);
        Assert.True(Art.Draw(mesh, name, clip, time, loop, model, variant: 0));
        if (name == "crew")
        {
            // The other variants stand behind, so one sheet shows the helmet and the scarf too.
            Art.Draw(mesh, name, clip, time + 0.5, loop, Matrix4x4.CreateTranslation(new Vector3(1.2f, 0, 1.8f) - e), variant: 3);
        }
        var lighting = Look.Apply(FrameLighting.Night);
        lighting.LampIntensity = 0;
        lighting.LampPosition = new Double3(0, -100, 0);
        return renderer.Render(mesh, camera, lighting, lighting.FogColor);
    }
}
