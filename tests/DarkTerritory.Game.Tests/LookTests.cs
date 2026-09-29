using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Game;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>The art pass (T39, GDD §25-28): weathered surfaces that ride with the train and don't bury the scene.</summary>
public class LookTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly Look Look = Look.Load(Content);
    static readonly TrainTuning Tuning = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));

    static GpuContext Gpu()
    {
        try { return new GpuContext("look tests"); }
        catch (GpuUnavailableException e) { Assert.Skip($"no Vulkan: {e.Message}"); throw; }
    }

    /// <summary>Inside the guard van (lamp-lit walls up close), with or without the look, the train at <paramref name="at"/>.</summary>
    static byte[] Inside(GpuContext gpu, Look? look, double at, int w = 160, int h = 90)
    {
        var line = RailLine.Load(Path.Combine(Content, "lines/test-loop.json"));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning, 4, 1)), line, at);
        var guard = train.Frames[^1];
        // A camera in the car's own frame: wherever the train is, it sees the same bit of wall.
        var eye = guard.ToWorld(new Double3(0.4, 2.2, 1.5));
        var camera = Camera.LookAt(eye, guard.ToWorld(new Double3(1.4, 2.0, -1.5)), 75);
        var mesh = new MeshBuilder();
        new GreyboxScene { Look = look, Time = 0.37 }.Build(mesh, train, camera.Position);
        using var renderer = new GreyboxRenderer(gpu, w, h);
        look?.Dress(renderer);
        var lighting = Views.Lighting(train);
        return renderer.Render(mesh, camera, lighting, lighting.FogColor);
    }

    static double[] Luma(byte[] px)
    {
        var l = new double[px.Length / 4];
        for (int i = 0; i < l.Length; i++)
            l[i] = 0.3 * px[i * 4] + 0.59 * px[i * 4 + 1] + 0.11 * px[i * 4 + 2];
        return l;
    }

    static double Mean(double[] v) => v.Average();

    /// <summary>
    /// How much neighbouring 4×4 blocks differ. The blocks average the frame's own ordered dither away (it's a 4×4
    /// pattern), so what's left is the surface: flat colour barely changes block to block, grime and streaks do.
    /// </summary>
    static double Breakup(double[] l, int w)
    {
        int h = l.Length / w, bw = w / 4, bh = h / 4;
        var blocks = new double[bw * bh];
        for (int y = 0; y < bh * 4; y++)
            for (int x = 0; x < bw * 4; x++)
                blocks[y / 4 * bw + x / 4] += l[y * w + x] / 16;
        double sum = 0;
        int n = 0;
        for (int by = 0; by < bh; by++)
            for (int bx = 0; bx + 1 < bw; bx++, n++)
                sum += Math.Abs(blocks[by * bw + bx + 1] - blocks[by * bw + bx]);
        return sum / n;
    }

    [Fact]
    public void EveryMaterialNamesAPaletteColourAndLightsStayClean()
    {
        Assert.Equal(0, Look.Material(Palette.LampAmber).Wear);
        Assert.Equal(0, Look.Material(Palette.FurnaceOrange).Wear);
        // Iron throws a harder specular than wood; paint and wood carry the most wear.
        Assert.True(Look.Material(Palette.IronGrey).Shine > Look.Material(Palette.DeepBrown).Shine);
        Assert.True(Look.Material(Palette.RustRed).Wear >= Look.Material(Palette.TarnishedBrass).Wear);
        // A tinted or dimmed colour takes its family's: a lamp dimmed to a glow in a Vigil is still clean.
        Assert.Equal(Look.Material(Palette.LampAmber), Look.Material(Palette.LampAmber * 0.08f));
        Assert.Equal(Look.Material(Palette.RustRed), Look.Material(Palette.RustRed * 0.9f));
        Assert.Throws<InvalidDataException>(() => new Look(Look.Tuning with { Materials = new() { ["Mauve"] = new() { Wear = 1 } } }));
    }

    [Fact]
    public void WornSurfacesBreakUpWithoutBuryingTheScene()
    {
        using var gpu = Gpu();
        var flat = Luma(Inside(gpu, null, 1200));
        var worn = Luma(Inside(gpu, Look, 1200));
        // GDD §27: grain, grime and staining where there was flat colour...
        Assert.True(Breakup(worn, 160) > Breakup(flat, 160) * 1.5, $"breakup {Breakup(worn, 160):0.00} vs flat {Breakup(flat, 160):0.00}");
        // ...but still a readable room (§32 wants it read fast): not a black hole, and not washed out either. Since the art
        // pass (ARCHITECTURE §8 note 48) the look's room is textured and lit per pixel, so it can be brighter than flat
        // colour by its lamp; it mustn't be much brighter.
        Assert.InRange(Mean(worn), Mean(flat) * 0.6, Mean(flat) * 1.6);
    }

    [Fact]
    public void TheGrimeRidesWithTheCar()
    {
        using var gpu = Gpu();
        // The same bit of the guard van's wall seen from the same place in the car, with the train 300 m further on:
        // the wear is in the car's frame, so it's the same wear, not a pattern the car slides through.
        var here = Inside(gpu, Look, 1200);
        var there = Inside(gpu, Look, 1500);
        int differing = 0;
        for (int i = 0; i < here.Length; i += 4)
            if (Math.Abs(here[i] - there[i]) + Math.Abs(here[i + 1] - there[i + 1]) + Math.Abs(here[i + 2] - there[i + 2]) > 24)
                differing++;
        // Up to a little: the line curves, so the moon and fog through the door come in from elsewhere.
        Assert.True(differing < here.Length / 4 / 10, $"{differing} pixels differ");
    }
    [Fact]
    public void AFacilitysBuildingsLeaveItsYardToItsModules()
    {
        // The gantry crane's far leg and its castings stand in the yard beside the spur (facilities.json "crane"): the
        // buildings of every facility that has one start past it, or the crane and the loads the crew must see are
        // inside a wall. (Others may reach over the track on purpose: a coaling chute, a grain spout.)
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(Content, "tuning/facilities.json")),
            new System.Text.Json.JsonDocumentOptions { CommentHandling = System.Text.Json.JsonCommentHandling.Skip });
        double far = doc.RootElement.GetProperty("crane").GetProperty("span")[1].GetDouble();
        var withCranes = doc.RootElement.GetProperty("kinds").EnumerateObject()
            .Where(k => k.Value.EnumerateArray().Any(m => m.GetString() == "crane"))
            .Select(k => Enum.Parse<Sim.Route.FacilityKind>(k.Name, ignoreCase: true))
            .ToArray();
        Assert.NotEmpty(withCranes);
        foreach (var kind in withCranes)
            foreach (int side in new[] { -1, 1 })
            {
                var piece = Art.StructureKit.Facility(Look, kind, side);
                float near = piece.Vertices.Min(v => v.Position.X * side);
                Assert.True(near > far + 1, $"{kind} on side {side} comes to {near:0.0} m of the spur; the crane reaches {far} m");
            }
    }
}
