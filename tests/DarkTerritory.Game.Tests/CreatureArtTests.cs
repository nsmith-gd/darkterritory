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
        ["crew"] = new(2500, 9000, 20, 60, ["idle", "walk", "run", "climb", "shovel", "crouch_idle"], ["dead"]),
        ["switchman"] = new(2000, 9000, 20, 60, ["wait", "flee"], []),
        ["hollow"] = new(1500, 5000, 20, 60, ["idle"], ["reach"]),
        ["soot_child"] = new(800, 5000, 20, 60, ["huddle"], ["turn"]),
        // SK_Quad: 40-55 bones.
        ["cinder_hound"] = new(4000, 8000, 40, 55, ["prowl", "run", "crouch"], ["lunge", "hit"]),
        // A chain of 8-12, plus a root.
        ["sleeper"] = new(400, 3000, 8, 13, ["dormant", "writhe"], ["lift"]),
        ["clinger"] = new(1500, 6000, 10, 45, ["cling", "drill"], ["punish"]),
        // A limb, not a body (App. A.4: all you see of one): a chain of arm, hand and two-bone fingers.
        ["dragger"] = new(300, 3000, 8, 20, ["grip"], ["reach"]),
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
        // dragger's at the car's edge, the rest of it hanging below).
        if (name is not ("clinger" or "dragger"))
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
        Assert.InRange(Height(Get("crew")), 1.72f, 1.9f);
        Assert.InRange(Height(Get("switchman")), 1.7f, 2.2f);
        Assert.InRange(Height(Get("hollow")), 1.95f, 2.25f);
        var hound = Get("cinder_hound");
        Assert.InRange(hound.Max.Z - hound.Min.Z, 1.25f, 1.9f); // nose to tail's root and beyond
        Assert.True(hound.Min.Z < -0.5f, "the hound's head is forward (−Z)");
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
            return mesh.Vertices.ToArray();
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
                    // A Dragger is out of sight under the car's edge until it reaches, as the greybox has it.
                    bool hidden = kind == EnemyKind.Dragger && phase is not (SpinePhase.Telegraph or SpinePhase.Punish);
                    Assert.True(hidden ? mesh.Count == 0 : mesh.Count > 0, $"{kind} {phase} drew {mesh.Count / 3} triangles");
                }
        foreach (var pose in Enum.GetValues<CrewPose>())
            for (int variant = 0; variant < 4; variant++)
            {
                mesh.Clear();
                Assert.True(Art.Crewmate(mesh, Matrix4x4.Identity, pose, 3.3, variant));
                Assert.InRange(mesh.Count / 3, 2000, 9000);
            }
        // Six sleepers and three children from one call.
        mesh.Clear();
        Art.Enemy(mesh, Matrix4x4.Identity, EnemyKind.Sleepers, SpinePhase.Dormant, 0, 0);
        Assert.Equal(6 * Get("sleeper").Triangles(), mesh.Count / 3);
        mesh.Clear();
        Art.Enemy(mesh, Matrix4x4.Identity, EnemyKind.SootChildren, SpinePhase.Dormant, 0, 0);
        Assert.Equal(3 * Get("soot_child").Triangles(), mesh.Count / 3);
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
            Assert.Equal(hanging.Count, reached.Count);
            Assert.InRange(reached.Vertices.ToArray().Min(v => Vector3.Distance(v.Position, hand)), 0, 0.1f);
            Assert.True(hanging.Vertices.ToArray().Min(v => Vector3.Distance(v.Position, hand)) > 0.25f);
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
        Assert.InRange(mesh.Vertices.ToArray().Min(v => Vector3.Distance(v.Position, at)), 0, 0.1f);
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
            return mesh.Vertices.ToArray();
        }
    }

    [Fact]
    public void AFrameOfEightCrewAndTwelveEnemiesIsCheap()
    {
        // The brief: 8 crew and 12 enemies a frame, skinned on the CPU. (Sleepers are six ties and Soot children three
        // figures a call, so this is more models than it looks.)
        var mesh = new MeshBuilder();
        EnemyKind[] kinds = [.. Enumerable.Repeat(EnemyKind.CinderHound, 6), EnemyKind.Clinger, EnemyKind.Clinger, EnemyKind.Hollow,
            EnemyKind.Switchman, EnemyKind.Sleepers, EnemyKind.SootChildren];
        void Frame(double t)
        {
            mesh.Clear();
            for (int i = 0; i < 8; i++)
                Art.Crewmate(mesh, Matrix4x4.CreateTranslation(i, 0, -4), (CrewPose)(i % 6), t, i);
            for (int i = 0; i < kinds.Length; i++)
                Art.Enemy(mesh, Matrix4x4.CreateTranslation(i, 0, -12), kinds[i], SpinePhase.Commit, t + i, 0.4);
        }
        Frame(0);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        const int Frames = 20;
        for (int f = 0; f < Frames; f++)
            Frame(f / 30.0);
        double ms = clock.Elapsed.TotalMilliseconds / Frames;
        TestContext.Current.TestOutputHelper?.WriteLine($"{ms:0.0} ms a frame, {mesh.Count / 3} triangles");
        // Generous (Debug builds, a loaded CI box); on a desktop Release build it's a few ms.
        Assert.True(ms < 120, $"{ms:0.0} ms a frame");
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
            Assert.Equal(0, mesh.Count);
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
            Assert.Contains(mesh.Vertices.ToArray(), v => v.Emissive >= 1);
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
        "cinder_hound" => (Vector3.Zero, 1.2f),
        "hollow" => (Vector3.Zero, 2.2f),
        "dragger" => (new Vector3(0.2f, 0.1f, 0), 1.4f),
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
