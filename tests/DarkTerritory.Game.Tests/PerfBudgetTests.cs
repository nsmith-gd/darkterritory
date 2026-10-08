using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The frame-rate targets' budgets (tuning/perf.json; ARCHITECTURE §8 note 86) where they hold on any machine: what a
/// frame draws. Every standard view, with the crew on the roof and the threats about, flat and in a headset, stays inside
/// the triangles and draws allowed; the culling keeps what's on screen; the eyes' shared shadows change nothing. The
/// times are `dt perf`'s, on real hardware.
/// </summary>
public class PerfBudgetTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly PerfTuning Tuning = DataFile.Load<PerfTuning>(Path.Combine(Content, PerfTuning.File));
    static readonly Look Look = Look.Load(Content);

    static GpuContext Gpu()
    {
        try { return new GpuContext("perf tests"); }
        catch (GpuUnavailableException e) { Assert.Skip($"no Vulkan: {e.Message}"); throw; }
    }

    static (TrainOnLine Train, GreyboxScene Scene, FrameLighting Light) Staged()
    {
        var t = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
        var line = RailLine.Load(Path.Combine(Content, "lines", "test-loop.json"));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(t, 6, 1)), line, 1200);
        var scene = new GreyboxScene { Look = Look, Time = 0.37, Crew = Staging.Crew(train, Content), Enemies = Staging.Threats(train) };
        return (train, scene, Views.Lighting(train, Look));
    }

    // Counts don't depend on the resolution, only on the shape of the view: each target drawn small, at its own aspect.
    static (int W, int H) Small(PerfTarget target) => (240, (int)Math.Round(240.0 * target.Height / target.Width));

    [Fact]
    public void EveryViewDrawsInsideTheFrameBudget()
    {
        using var gpu = Gpu();
        var (train, scene, light) = Staged();
        var mesh = new MeshBuilder();
        foreach (var target in new[] { Tuning.Pc, Tuning.Vr })
        {
            var (w, h) = Small(target);
            var eyes = Enumerable.Range(0, target.Eyes).Select(_ => new GreyboxRenderer(gpu, w, h, moonShadowSize: target.Eyes > 1 ? 1024 : 2048)).ToList();
            for (int e = 1; e < eyes.Count; e++)
                eyes[e].ShadowsFrom = eyes[0];
            try
            {
                foreach (var view in Views.Names)
                {
                    var camera = Views.Get(view, train);
                    scene.Build(mesh, train, camera.Position);
                    // And again with a hand lamp in your own fist (note 436): its cube's drawn round the eye, the worst of it.
                    foreach (bool lamp in new[] { false, true })
                    {
                        if (lamp)
                            mesh.ShadowLight = new PointLight(new Vector3(0.25f, -0.6f, -0.2f), Palette.LampAmber * 1.8f, 7);
                        int triangles = 0, draws = 0;
                        foreach (var eye in eyes)
                        {
                            eye.Render(mesh, camera, light, light.FogColor);
                            var s = eye.Stats;
                            // (The hand lamp's cube: what each of its faces drew.)
                            triangles += s.Triangles + s.LampTriangles + s.MoonTriangles + s.HandTriangles;
                            draws = Math.Max(draws, Math.Max(Math.Max(s.Draws, s.HandDraws), Math.Max(s.LampDraws, s.MoonDraws)));
                        }
                        string with = lamp ? " with a hand lamp" : "";
                        TestContext.Current.TestOutputHelper?.WriteLine($"{target.Fps} fps {view}{with}: {triangles} triangles, {draws} draws at most in a pass");
                        Assert.True(triangles <= Tuning.MaxFrameTriangles, $"{target.Fps} fps, {view}{with}: {triangles} triangles a frame (at most {Tuning.MaxFrameTriangles})");
                        Assert.True(draws <= Tuning.MaxPassDraws, $"{target.Fps} fps, {view}{with}: {draws} draws in a pass (at most {Tuning.MaxPassDraws})");
                    }
                }
            }
            finally
            {
                foreach (var eye in eyes)
                    eye.Dispose();
            }
        }
    }

    [Fact]
    public void CullingKeepsWhatsInViewAndDropsWhatsBehind()
    {
        var camera = Camera.LookAt(Double3.Zero, new Double3(0, 0, -10), 65);
        Span<Vector4> planes = stackalloc Vector4[6];
        GreyboxRenderer.FrustumPlanes(camera.ViewProjection(16f / 9), planes);
        Assert.True(GreyboxRenderer.Visible(planes, new Vector4(0, 0, -20, 1)));        // straight ahead
        Assert.False(GreyboxRenderer.Visible(planes, new Vector4(0, 0, 20, 1)));        // behind
        Assert.False(GreyboxRenderer.Visible(planes, new Vector4(40, 0, -10, 1)));      // well off to the side
        Assert.True(GreyboxRenderer.Visible(planes, new Vector4(40, 0, -10, 35)));      // ...but big enough to reach in
        Assert.True(GreyboxRenderer.Visible(planes, new Vector4(0, 0, 2, 3)));          // round the camera itself
        // An orthographic box (the moon's) the same way.
        var ortho = Matrix4x4.CreateLookAt(new Vector3(0, 80, 0), Vector3.Zero, -Vector3.UnitZ) * Matrix4x4.CreateOrthographic(110, 110, 0.1f, 160);
        GreyboxRenderer.FrustumPlanes(ortho, planes);
        Assert.True(GreyboxRenderer.Visible(planes, new Vector4(50, 0, 50, 1)));
        Assert.False(GreyboxRenderer.Visible(planes, new Vector4(70, 0, 0, 1)));
    }

    [Fact]
    public void TheSecondEyeDrawsTheSameWithTheFirstEyesShadows()
    {
        using var gpu = Gpu();
        var (train, scene, light) = Staged();
        var mesh = new MeshBuilder();
        var body = Views.Get("roof", train);
        scene.Build(mesh, train, body.Position);
        var left = body;
        left.EyeOffset = new Vector3(-0.032f, 0, 0);
        var right = body;
        right.EyeOffset = new Vector3(0.032f, 0, 0);
        using var first = new GreyboxRenderer(gpu, 240, 256, moonShadowSize: 1024);
        using var second = new GreyboxRenderer(gpu, 240, 256, moonShadowSize: 1024);
        using var alone = new GreyboxRenderer(gpu, 240, 256, moonShadowSize: 1024);
        foreach (var r in new[] { first, second, alone })
            Look.Dress(r);
        second.ShadowsFrom = first;
        first.Render(mesh, left, light, light.FogColor);
        var shared = second.Render(mesh, right, light, light.FogColor);
        var own = alone.Render(mesh, right, light, light.FogColor);
        Assert.Equal(0, second.Stats.LampTriangles + second.Stats.MoonTriangles);
        Assert.True(alone.Stats.MoonTriangles > 0, "the moon casts in this view");
        int differ = 0;
        for (int i = 0; i < own.Length; i++)
            if (Math.Abs(own[i] - shared[i]) > 2)
                differ++;
        Assert.True(differ < own.Length / 1000, $"{differ} of {own.Length} channels differ");
    }
}
