using Ballast;
using Ballast.Render;
using DarkTerritory.Game;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// Renders real frames through Vulkan. Skips (rather than fails) on machines with no Vulkan at all;
/// CI and cloud sessions install Mesa lavapipe so these run everywhere that matters.
/// </summary>
public class ScreenshotTests
{
    static readonly string Content = DataFile.FindContentRoot();

    static (byte[] Pixels, int W, int H) Shoot(string view, bool withTrain = true)
    {
        var tuning = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
        var line = RailLine.Load(Path.Combine(Content, "lines/test-loop.json"));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(tuning, 6, 1)), line, 1200);

        GpuContext gpu;
        try { gpu = new GpuContext("tests"); }
        catch (GpuUnavailableException e) { Assert.Skip($"no Vulkan: {e.Message}"); throw; }
        using (gpu)
        {
            using var renderer = new GreyboxRenderer(gpu, 160, 90);
            var camera = Views.Get(view, train);
            var mesh = new MeshBuilder();
            if (withTrain)
                new GreyboxScene().Build(mesh, train, camera.Position);
            var lighting = Views.Lighting(train);
            return (renderer.Render(mesh, camera, lighting, lighting.FogColor), 160, 90);
        }
    }

    static (int R, int G, int B) Pixel(byte[] px, int w, int x, int y) => (px[(y * w + x) * 4], px[(y * w + x) * 4 + 1], px[(y * w + x) * 4 + 2]);

    [Theory]
    [InlineData("trackside")]
    [InlineData("roof")]
    [InlineData("cab")]
    [InlineData("chase")]
    [InlineData("ahead")]
    public void EveryViewRendersASceneNotJustFog(string view)
    {
        var (full, w, h) = Shoot(view);
        var (empty, _, _) = Shoot(view, withTrain: false);
        int differing = 0;
        for (int i = 0; i < full.Length; i += 4)
            if (Math.Abs(full[i] - empty[i]) + Math.Abs(full[i + 1] - empty[i + 1]) + Math.Abs(full[i + 2] - empty[i + 2]) > 12)
                differing++;
        Assert.True(differing > w * h / 10, $"{view}: only {differing} pixels differ from an empty frame");
    }

    [Fact]
    public void HeadlampIsTheBrightestThingInTheAheadView()
    {
        var (px, w, h) = Shoot("ahead");
        int best = 0, bx = 0, by = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var (r, g, b) = Pixel(px, w, x, y);
                if (r + g + b > best) { best = r + g + b; bx = x; by = y; }
            }
        var (lr, lg, lb) = Pixel(px, w, bx, by);
        Assert.True(lr > lb + 40, $"brightest pixel at {bx},{by} should be warm amber, got {lr},{lg},{lb}");
        Assert.InRange(bx, w * 0.35, w * 0.65);
    }

    [Fact]
    public void PngRoundTripsSize()
    {
        using var ms = new MemoryStream();
        PngWriter.Write(ms, new byte[4 * 4 * 4], 4, 4, scale: 3);
        var bytes = ms.ToArray();
        Assert.Equal(0x89, bytes[0]);
        Assert.Equal(12, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16)));
    }
}
