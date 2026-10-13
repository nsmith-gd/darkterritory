using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Stops;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The light indoors (queue #211, ARCHITECTURE §8 note 475; the director, 8 Oct: "it feels almost impossible to see anything
/// ... we do need interior lighting to be functional"): inside an open village house and an open barn, rendered with the
/// look as the game draws them, the room reads, not black. Each measured on frontier:7, from where `dt screenshot --cottage`
/// and `--barn` stand (their 1280x720 means were 5.6 and 4.6 out of 255 before, about 11 and 27 after).
/// </summary>
public class InteriorLightTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly Look Look = Look.Load(Content);
    static readonly TrainTuning Trains = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));

    static GpuContext Gpu()
    {
        try { return new GpuContext("interior light tests"); }
        catch (GpuUnavailableException e) { Assert.Skip($"no Vulkan: {e.Message}"); throw; }
    }

    /// <summary>The mean luma (0..255) of a small frame from <paramref name="eye"/> towards <paramref name="at"/>, the train standing at the stop.</summary>
    static double Seen(GpuContext gpu, Sim.Route.Route route, RailLine line, double s, Double3 eye, Double3 at)
    {
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Trains, 4, 1)), line, s);
        var camera = Camera.LookAt(eye, at, 80);
        var mesh = new MeshBuilder();
        new GreyboxScene { Look = Look, Route = route, Time = 0.37 }.Build(mesh, train, camera.Position);
        using var renderer = new GreyboxRenderer(gpu, 160, 90);
        Look.Dress(renderer);
        var lighting = Views.Lighting(train, Look);
        var px = renderer.Render(mesh, camera, lighting, lighting.FogColor);
        double sum = 0;
        for (int i = 0; i < px.Length / 4; i++)
            sum += 0.3 * px[i * 4] + 0.59 * px[i * 4 + 1] + 0.11 * px[i * 4 + 2];
        return sum / (px.Length / 4);
    }

    [Fact]
    public void InsideAnOpenHouseAndABarnTheRoomReads()
    {
        using var gpu = Gpu();
        var route = Sim.LineGen.Routes.Generate(Content, "frontier:7", 6);
        var line = route.Build();
        var stops = route.Features.Where(f => f.Stop is not null).ToList();
        // The first open house, from just in at its door across the room (dt screenshot --cottage 0).
        var (hf, house, _) = stops.SelectMany(f => f.Stop!.Buildings.Select((b, i) => (f, b, i)))
            .First(x => x.b.Open && StopWalls.Walled(x.f.Stop!, x.i));
        var (_, inside) = StopWalls.Doorways(house).First();
        Double3 InHouse(Sim.Route.RouteFeature f, StopBuilding b, double x, double y, double up) => Run.StopWorld(line, f, StopWalls.InHouse(b, x, y), up);
        double cottage = Seen(gpu, route, line, hf.Start, InHouse(hf, house, inside.X, inside.Y, 1.75), InHouse(hf, house, -inside.X * 0.8, -inside.Y * 0.8, 1.0));
        // The first open barn or shed, from its middle at its back wall (where its hayloft or bench is).
        var (bf, barn, _) = stops.SelectMany(f => f.Stop!.Buildings.Select((b, i) => (f, b, i)))
            .First(x => StopWalls.OpenShed(x.b) && StopWalls.Shelled(x.f.Stop!, x.i) && x.f.Stop!.Holdouts.All(h => h.Building != x.i));
        int door = StopWalls.ShedDoorSide(barn);
        double shed = Seen(gpu, route, line, bf.Start, InHouse(bf, barn, 0, door * barn.Width * 0.3, 1.7), InHouse(bf, barn, 0, -door * barn.Width / 2, 1.2));
        // Dim, not black: what the old fill and candle left was a mean of about 5 (a black frame with a smudge of light).
        Assert.True(cottage > 8, $"inside the open house: mean luma {cottage:0.0}");
        Assert.True(shed > 12, $"inside the open barn: mean luma {shed:0.0}");
        // And not bright: a lamp-lit car's inside is about 28.
        Assert.True(cottage < 40 && shed < 45, $"too bright: house {cottage:0.0}, barn {shed:0.0}");
    }
}
