using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// Loading in the fog (ARCHITECTURE §8 note 479; the director, 8 Oct: "We have all this fog we can certainly do good
/// loading where we need to with this fog"). The fog's reach on the CPU is scene.frag's fog solved for distance; the line's
/// cells past it are cooked off the frame and drawn as they come, the same as cooked in it; the far land is cooked once;
/// a walled town's frame stays inside tuning/perf.json's counts.
/// </summary>
public class FogLoadingTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly Look Look = Look.Load(Content);
    static readonly TrainTuning Trains = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));

    static FrameLighting Night(Sim.Route.Route route, TrainOnLine train)
    {
        var light = Views.Lighting(train, Look);
        light.FogDensity = Views.FogDensity(route, train);
        return light;
    }

    [Fact]
    public void TheFogsReachIsWhereItCloses()
    {
        var route = Sim.LineGen.Routes.Generate(Content, "frontier:7", 4);
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Trains, 4, 1)), route.Build(), 3000);
        var light = Night(route, train);
        Assert.True(light.FogCurve > 1 && light.FogFloor < 1 && light.FogHeightFalloff > 0, "look.json's height fog and curve");
        double eyeY = train.Frames[0].ToWorld(default).Y + 3;
        foreach (double height in new[] { -2.0, 0, 2, 10, 25, 60 })
        {
            double reach = light.FogReach(height, eyeY);
            // At its reach a surface takes the fog's colour all but a hundredth; a little nearer, less; further, more.
            Assert.Equal(0.99, light.FogAt(reach, height, eyeY), 6);
            Assert.True(light.FogAt(reach * 0.8, height, eyeY) < 0.99);
            Assert.True(light.FogAt(reach * 1.2, height, eyeY) > 0.99);
        }
        // The fog thins going up: a tower's top shows further than a yard's fence.
        Assert.True(light.FogReach(25, eyeY) > light.FogReach(0, eyeY) * 1.15);
        // The night's visibility (linegen's fog range, 90-120 m on the frontier) is well inside it: things there still show.
        Assert.InRange(light.FogReach(0, eyeY), 150, 320);
        // No fog, no reach.
        Assert.True(double.IsPositiveInfinity((light with { FogDensity = 0 }).FogReach(0, eyeY)));
    }

    static int[] Cells(MeshBuilder mesh) =>
        [.. mesh.Instances.Select(i => i.Asset.Name).Where(n => n.StartsWith("cell-", StringComparison.Ordinal)).Select(n => int.Parse(n[5..])).Order()];

    [Fact]
    public void CellsPastTheFogAreCookedOffTheFrameAndDrawnTheSame()
    {
        var route = Sim.LineGen.Routes.Generate(Content, "frontier:7", 4);
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Trains, 4, 1)), route.Build(), 3000);
        var eye = Views.Get("roof", train).Position;
        // As it was: everything in the draw distance, cooked in the frame.
        var now = new GreyboxScene { Look = Look.Load(Content), Time = 0.37, Route = route };
        var all = new MeshBuilder();
        now.Build(all, train, eye);
        var every = Cells(all);
        Assert.True(every.Length >= 7, $"cells {string.Join(",", every)}");
        // In the night's fog: only what the fog shows, at once; the rest come in from the workers, in later frames.
        var light = Night(route, train);
        var fogged = new GreyboxScene { Look = Look.Load(Content), Time = 0.37, Route = route, Fog = light };
        var mesh = new MeshBuilder();
        fogged.Build(mesh, train, eye);
        var first = Cells(mesh);
        Assert.True(first.Length < every.Length, $"all {first.Length} cells cooked in the first frame");
        double reach = light.FogReach(25, eye.Y);
        Assert.All(every.Where(i => !first.Contains(i)), i =>
            Assert.True(Math.Abs((i + 0.5) * Art.WorldArt.CellLength - train.Dynamics.Distance) > reach - Art.WorldArt.CellLength,
                $"cell {i} is in the fog's reach and wasn't drawn"));
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (Cells(mesh).Length < every.Length && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(50);
            fogged.Build(mesh, train, eye);
        }
        Assert.Equal(every, Cells(mesh));
        // Cooked on a worker or in the frame, a cell is the same cell.
        var cooked = mesh.Instances.Where(i => i.Asset.Name.StartsWith("cell-", StringComparison.Ordinal)).ToDictionary(i => i.Asset.Name);
        foreach (var i in all.Instances.Where(i => i.Asset.Name.StartsWith("cell-", StringComparison.Ordinal)))
        {
            Assert.Equal(i.Asset.Vertices.Length, cooked[i.Asset.Name].Asset.Vertices.Length);
            Assert.Equal(i.Model, cooked[i.Asset.Name].Model);
        }
        Assert.Equal(all.Instances.Count, mesh.Instances.Count);
    }

    [Fact]
    public void TheFarLandIsCookedOnceAndStandsWhereItDid()
    {
        var route = Sim.LineGen.Routes.Generate(Content, "frontier:7", 4);
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Trains, 4, 1)), route.Build(), 3000);
        var scene = new GreyboxScene { Look = Look.Load(Content), Time = 0.37, Route = route };
        var a = new MeshBuilder();
        var b = new MeshBuilder();
        var eye = Views.Get("roof", train).Position;
        scene.Build(a, train, eye);
        // Five metres on: the same cooked hills (the same assets), each where it stood.
        var on = eye + new Double3(5, 0, 0);
        scene.Timings = [];
        scene.Build(b, train, on);
        static List<MeshInstance> Far(MeshBuilder m) => [.. m.Instances.Where(i => i.Asset.Name.StartsWith("farland-", StringComparison.Ordinal))];
        var (fa, fb) = (Far(a), Far(b));
        Assert.True(fa.Count >= 5, $"{fa.Count} cells of far land");
        Assert.True(fa.Sum(i => i.Asset.Triangles) > 1000);
        foreach (var x in fa)
        {
            var y = Assert.Single(fb, i => i.Asset.Name == x.Asset.Name);
            Assert.Same(x.Asset, y.Asset);
            // Drawn about the eye: moved by the eye's move, so the same place in the world.
            Assert.True(Vector3.Distance(x.Model.Translation - new Vector3(5, 0, 0), y.Model.Translation) < 1e-2f);
        }
        // None of it in the frame's own soup any more (it was rebuilt there every frame: note 479).
        Assert.Equal(0, scene.Timings["land"].Triangles);
    }

    [Fact]
    public void AFarPieceIsClusteredToItsSizeAndKeepsItsLight()
    {
        // The hand lamp a far townsperson carries (note 479): a third of it or less, the same size.
        var lantern = Art.PropArt.Of(Look).Get("hand_lantern");
        Assert.NotNull(lantern);
        var far = lantern.Clustered(Look.Tuning.ClusteredLodCell / 4);
        Assert.InRange(far.Triangles, 12, lantern.Triangles / 3);
        Assert.True(Math.Abs(far.Bounds.Radius - lantern.Bounds.Radius) < Look.Tuning.ClusteredLodCell / 2);
        // A lit window in a wall: the light's corners never merge into the wall's, so the window still shines.
        var k = new Art.Kit(Look);
        k.Use("plank_weathered", Palette.DeepBrown);
        k.Box(new Vector3(-2, 0, -0.1f), new Vector3(2, 3, 0.1f));
        k.Emissive = 1;
        k.Box(new Vector3(-0.4f, 1.2f, -0.12f), new Vector3(0.4f, 2.0f, -0.1f));
        var wall = k.Build("wall-with-a-window");
        var clustered = wall.Clustered(0.3f);
        Assert.True(clustered.Triangles < wall.Triangles);
        Assert.Contains(clustered.Vertices, v => v.Emissive > 0);
        Assert.Contains(clustered.Vertices, v => v.Emissive == 0);
        Assert.Throws<InvalidOperationException>(() => new MeshAsset("skinned", [new Vertex()], [new SkinWeights()]).Clustered(0.1f));
    }

    static GpuContext Gpu()
    {
        try { return new GpuContext("fog tests"); }
        catch (GpuUnavailableException e) { Assert.Skip($"no Vulkan: {e.Message}"); throw; }
    }

    /// <summary>
    /// A walled town of the biggest (local:5's), from the square, down its street, down a lane and from over its gate, at
    /// night: a flat screen's frame inside the triangles and draws allowed (`dt perf --route local:5 --town ...` measured
    /// 2.6M before note 479, its people and their lanterns most of it, in every shadow map).
    /// </summary>
    [Fact]
    public void AWalledTownsFrameDrawsInsideTheBudget()
    {
        using var gpu = Gpu();
        var tuning = DataFile.Load<PerfTuning>(Path.Combine(Content, PerfTuning.File));
        var route = Sim.LineGen.Routes.Generate(Content, "local:5", 6);
        var line = route.Build();
        var consist = Consist.Uniform(Trains, 6, 1);
        var town = Staging.DepartureTown(Content, route, line, consist.LengthMetres, out double depart);
        Assert.NotNull(town);
        Assert.NotNull(town.Plan.Bounds);
        var train = new TrainOnLine(new TrainDynamics(consist), line, depart);
        var scene = new GreyboxScene { Look = Look, Time = 0.37, Route = route, Town = town, Crew = Staging.Crew(train, Content), Enemies = Staging.Threats(train) };
        var light = Night(route, train);
        var target = tuning.Pc;
        using var renderer = new GreyboxRenderer(gpu, 240, (int)Math.Round(240.0 * target.Height / target.Width), moonShadowSize: 2048);
        var mesh = new MeshBuilder();
        foreach (var where in new[] { "square", "houses", "crooked", "over" })
        {
            var camera = Staging.TownCamera(town, where);
            scene.Build(mesh, train, camera.Position);
            renderer.Render(mesh, camera, light, light.FogColor);
            var s = renderer.Stats;
            int triangles = s.Triangles + s.LampTriangles + s.MoonTriangles + s.HandTriangles;
            int draws = Math.Max(Math.Max(s.Draws, s.HandDraws), Math.Max(s.LampDraws, s.MoonDraws));
            TestContext.Current.TestOutputHelper?.WriteLine($"{where}: {triangles} triangles ({s.Triangles} seen, {s.MoonTriangles} moon, {s.LampTriangles} lamp), {draws} draws at most");
            Assert.True(triangles <= tuning.MaxFrameTriangles, $"{where}: {triangles} triangles a frame (at most {tuning.MaxFrameTriangles})");
            Assert.True(draws <= tuning.MaxPassDraws, $"{where}: {draws} draws in a pass (at most {tuning.MaxPassDraws})");
        }
    }
}
