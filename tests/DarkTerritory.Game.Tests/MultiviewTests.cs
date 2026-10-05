using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// Both eyes in one pass (multiview, ARCHITECTURE §8 note 219) against each eye drawn alone (the per-eye fallback): the
/// same pictures, from fewer draw calls. Lavapipe has the feature, so this runs in CI; a device without it skips.
/// The scene is the look's (its kit pieces, the skinned crew and creatures, the effects), but the renderers aren't dressed
/// in its textures: a dressed renderer holds every one, and three took the whole test run past the container's memory
/// with the other GPU tests running alongside. Untextured, every material samples the one white layer, by both paths
/// alike. `dt vr check` draws the dressed scene by both (note 219).
/// </summary>
public class MultiviewTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly Look Look = Look.Load(Content);
    const int W = 240, H = 256;

    static GpuContext Gpu()
    {
        try { return new GpuContext("multiview tests"); }
        catch (GpuUnavailableException e) { Assert.Skip($"no Vulkan: {e.Message}"); throw; }
    }

    /// <summary>The crew on the roof and the threats about (skinned pieces, effects, practical lights), and a body's two eyes.</summary>
    static (MeshBuilder Mesh, Camera Left, Camera Right, FrameLighting Light) Staged(string view)
    {
        var t = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
        var line = RailLine.Load(Path.Combine(Content, "lines", "test-loop.json"));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(t, 6, 1)), line, 1200);
        var scene = new GreyboxScene { Look = Look, Time = 0.37, Crew = Staging.Crew(train, Content), Enemies = Staging.Threats(train) };
        var body = Views.Get(view, train);
        var mesh = new MeshBuilder();
        scene.Build(mesh, train, body.Position);
        // A headset's eyes: the IPD apart along the body's right, each with its own off-centre frustum (towards the nose).
        var right = Vector3.Normalize(Vector3.Cross(body.Forward, Vector3.UnitY));
        var turn = Quaternion.CreateFromYawPitchRoll((float)body.Yaw, (float)body.Pitch, 0);
        Camera Eye(float side) => body with
        {
            EyeOffset = right * side,
            Orientation = turn,
            Fov = side < 0 ? new EyeFov(-0.95f, 0.75f, 0.85f, -0.85f) : new EyeFov(-0.75f, 0.95f, 0.85f, -0.85f),
        };
        return (mesh, Eye(-0.032f), Eye(0.032f), Views.Lighting(train, Look));
    }

    static int Differ(byte[] a, byte[] b, int tolerance)
    {
        int n = 0;
        for (int i = 0; i < a.Length; i++)
            if (Math.Abs(a[i] - b[i]) > tolerance)
                n++;
        return n;
    }

    [Theory]
    [InlineData("roof")]
    [InlineData("cab")]
    public void BothEyesInOnePassMatchEachEyeDrawnAlone(string view)
    {
        using var gpu = Gpu();
        if (!gpu.Multiview)
            Assert.Skip($"{gpu.DeviceName} has no multiview");
        var (mesh, left, right, light) = Staged(view);

        // A different overlay in each eye (a panel's projection is an eye's own): each must keep to its eye.
        var leftOverlay = new Overlay();
        leftOverlay.Rect(10, 10, 40, 30, new Vector4(1, 0, 0, 1));
        var rightOverlay = new Overlay();
        rightOverlay.Rect(W - 50, H - 40, 40, 30, new Vector4(0, 0, 1, 1));

        // The per-eye path as VrView builds it, then the multiview one.
        byte[] aloneLeft, aloneRight;
        (FrameStats, FrameStats) perEye;
        using (var first = new GreyboxRenderer(gpu, W, H, moonShadowSize: 1024))
        using (var second = new GreyboxRenderer(gpu, W, H, moonShadowSize: 1024))
        {
            second.ShadowsFrom = first;
            aloneLeft = first.Render(mesh, left, light, light.FogColor, leftOverlay);
            aloneRight = second.Render(mesh, right, light, light.FogColor, rightOverlay);
            perEye = (first.Stats, second.Stats);
        }
        using var both = new GreyboxRenderer(gpu, W, H, moonShadowSize: 1024, views: 2);
        var eyes = both.RenderEyes(mesh, left, right, light, light.FogColor, leftOverlay, rightOverlay);

        int leftDiffer = Differ(aloneLeft, eyes[0], 2), rightDiffer = Differ(aloneRight, eyes[1], 2);
        TestContext.Current.TestOutputHelper?.WriteLine($"{view}: left {leftDiffer}, right {rightDiffer} of {aloneLeft.Length} channels differ by more than 2; " +
            $"draws per-eye {perEye.Item1.Draws}+{perEye.Item2.Draws} (+{perEye.Item1.LampDraws}+{perEye.Item1.MoonDraws} shadow), multiview {both.Stats.Draws} (+{both.Stats.LampDraws}+{both.Stats.MoonDraws})");
        Assert.True(leftDiffer < aloneLeft.Length / 1000, $"left eye: {leftDiffer} of {aloneLeft.Length} channels differ");
        Assert.True(rightDiffer < aloneRight.Length / 1000, $"right eye: {rightDiffer} of {aloneRight.Length} channels differ");
        // The two eyes aren't one picture twice: they're an IPD apart.
        Assert.True(Differ(eyes[0], eyes[1], 8) > eyes[0].Length / 50, "the eyes look the same: the second view isn't the right eye's");

        // Each overlay in its own eye only.
        static (byte R, byte B) At(byte[] px, int x, int y) => (px[(y * W + x) * 4], px[(y * W + x) * 4 + 2]);
        Assert.Equal((255, 0), At(eyes[0], 30, 25));
        Assert.Equal((0, 255), At(eyes[1], W - 30, H - 25));
        Assert.NotEqual((byte)255, At(eyes[1], 30, 25).R);
        Assert.NotEqual((byte)255, At(eyes[0], W - 30, H - 25).B);

        // One pass for both eyes: what either sees, drawn once, and the shadow maps drawn once.
        Assert.Equal(2, both.Stats.Views);
        Assert.InRange(both.Stats.Draws, Math.Max(perEye.Item1.Draws, perEye.Item2.Draws), perEye.Item1.Draws + perEye.Item2.Draws - 1);
        Assert.Equal(perEye.Item1.LampDraws + perEye.Item2.LampDraws, both.Stats.LampDraws);
        Assert.Equal(perEye.Item1.MoonDraws + perEye.Item2.MoonDraws, both.Stats.MoonDraws);
    }

    [Fact]
    public void ARendererWithoutTheFeatureIsRefusedAndThePerEyePathIsChosen()
    {
        using var gpu = Gpu();
        Assert.Equal(gpu.Multiview ? StereoPath.Multiview : StereoPath.PerEye, VrView.Choose(StereoPath.Multiview, gpu));
        Assert.Equal(StereoPath.PerEye, VrView.Choose(StereoPath.PerEye, gpu));
        if (!gpu.Multiview)
            Assert.Throws<NotSupportedException>(() => new GreyboxRenderer(gpu, W, H, views: 2));
    }
}
