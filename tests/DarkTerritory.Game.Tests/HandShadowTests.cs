using System.Numerics;
using Ballast;
using Ballast.Render;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The hand lamp's shadows (MeshBuilder.ShadowLight, ARCHITECTURE §8 note 436; GDD §31): a lamp hung half a metre over a
/// floor, posts round it and a plate under it, seen from above. What's behind a post or under the plate is dark with the
/// lamp shadowed and lit with it plain; the open floor is lit alike either way (no acne: the cube's faces meet cleanly).
/// </summary>
public class HandShadowTests
{
    const int W = 256, H = 256;
    const float Floor = -6, LampOver = 0.5f;
    static readonly Vector3 Lamp = new(0, Floor + LampOver, 0);

    static GpuContext Gpu()
    {
        try { return new GpuContext("hand shadow tests"); }
        catch (GpuUnavailableException e) { Assert.Skip($"no Vulkan: {e.Message}"); throw; }
    }

    // Looking straight down from the origin (the scene's origin is the camera's: the geometry's camera-relative).
    static readonly Camera Above = Camera.LookAt(new Double3(0, 0, 0), new Double3(0, -1, -0.0001), 90);

    static MeshBuilder Scene(bool occluders, bool shadowed)
    {
        var mesh = new MeshBuilder();
        mesh.AxisBox(new Vector3(-7, Floor - 0.1f, -7), new Vector3(7, Floor, 7), new Vector3(0.5f));
        if (occluders)
        {
            // A post a metre off along each way the floor runs (the cube's ±X and ±Z faces), taller than the lamp...
            foreach (var (x, z) in new[] { (1f, 0f), (-1f, 0f), (0f, 1f), (0f, -1f) })
                mesh.AxisBox(new Vector3(x - 0.06f, Floor, z - 0.06f), new Vector3(x + 0.06f, Floor + 0.8f, z + 0.06f), new Vector3(0.5f));
            // ...and a plate halfway down under it (its −Y face).
            mesh.AxisBox(new Vector3(-0.1f, Floor + 0.24f, -0.1f), new Vector3(0.1f, Floor + 0.26f, 0.1f), new Vector3(0.5f));
        }
        var light = new PointLight(Lamp, new Vector3(1, 0.72f, 0.38f) * 1.8f, 7);
        if (shadowed)
            mesh.ShadowLight = light;
        else
            mesh.PointLights.Add(light);
        return mesh;
    }

    static FrameLighting Dark()
    {
        var l = FrameLighting.Night;
        (l.MoonStrength, l.Ambient, l.LampRange, l.FogDensity) = (0, 0.01f, 0.01f, 0);
        return l;
    }

    static float Luma(byte[] px, Vector3 at)
    {
        var clip = Vector4.Transform(new Vector4(at, 1), Above.ViewProjection((float)W / H));
        int x = (int)((clip.X / clip.W * 0.5f + 0.5f) * W), y = (int)((clip.Y / clip.W * 0.5f + 0.5f) * H);
        int i = (Math.Clamp(y, 0, H - 1) * W + Math.Clamp(x, 0, W - 1)) * 4;
        return (px[i] + px[i + 1] + px[i + 2]) / 3f;
    }

    [Fact]
    public void WhatsBehindSomethingFromTheHandLampIsInItsShadow()
    {
        using var gpu = Gpu();
        using var renderer = new GreyboxRenderer(gpu, W, H, moonShadowSize: 1024);
        var light = Dark();
        var shadowed = renderer.Render(Scene(true, true), Above, light, light.FogColor);
        Assert.True(renderer.Stats.HandDraws > 0, "the hand lamp's cube wasn't drawn");
        var plain = renderer.Render(Scene(true, false), Above, light, light.FogColor);
        var output = TestContext.Current.TestOutputHelper;

        // Behind each post, and under the plate (its shadow's twice its width on the floor; from up here the plate hides
        // only the middle of it): a face each.
        foreach (var behind in new[] { new Vector3(1.6f, Floor, 0), new Vector3(-1.6f, Floor, 0), new Vector3(0, Floor, 1.6f), new Vector3(0, Floor, -1.6f), new Vector3(0.155f, Floor, 0) })
        {
            float s = Luma(shadowed, behind), p = Luma(plain, behind);
            output?.WriteLine($"behind {behind}: {s:0} shadowed, {p:0} plain");
            Assert.True(p > 40, $"{behind} isn't lit by the plain lamp ({p:0}): the test's framing is off");
            Assert.True(s < p * 0.5f, $"{behind}: {s:0} shadowed against {p:0} plain, not in the shadow");
        }
        // Between the posts, and just off the plate's: lit alike.
        foreach (var open in new[] { new Vector3(1.2f, Floor, 1.2f), new Vector3(-1.2f, Floor, -1.2f), new Vector3(0.45f, Floor, 0.3f), new Vector3(-2.5f, Floor, 2.2f) })
        {
            float s = Luma(shadowed, open), p = Luma(plain, open);
            output?.WriteLine($"open {open}: {s:0} shadowed, {p:0} plain");
            Assert.True(Math.Abs(s - p) <= Math.Max(4, p * 0.08f), $"{open}: {s:0} shadowed against {p:0} plain, shadowed where nothing's in the way");
        }
    }

    [Fact]
    public void OnTheOpenFloorTheShadowedLampLightsAsThePlainOne()
    {
        using var gpu = Gpu();
        using var renderer = new GreyboxRenderer(gpu, W, H, moonShadowSize: 1024);
        var light = Dark();
        var shadowed = renderer.Render(Scene(false, true), Above, light, light.FogColor);
        var plain = renderer.Render(Scene(false, false), Above, light, light.FogColor);
        int differ = 0;
        for (int i = 0; i < shadowed.Length; i++)
            if (Math.Abs(shadowed[i] - plain[i]) > 6)
                differ++;
        TestContext.Current.TestOutputHelper?.WriteLine($"{differ} of {shadowed.Length} channels differ by more than 6");
        // (Acne, or a seam where two of the cube's faces meet, would be a speckle or a line across the lit floor.)
        Assert.True(differ < shadowed.Length / 500, $"{differ} of {shadowed.Length} channels differ: the lamp shadows the open floor");
    }
}
