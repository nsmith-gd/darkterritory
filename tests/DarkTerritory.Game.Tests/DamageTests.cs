using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The consist's wear and tear (pipeline plan, consist kit: "3 damage states per car; scars persist between runs as
/// decal and mask layers"): states and scars read off integrity, the same scars on the same car every time, and a car
/// getting worse keeps the marks it had.
/// </summary>
public class DamageTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly Look Look = Look.Load(Content);
    static readonly TrainTuning Tuning = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));

    [Fact]
    public void ThreeStatesAndAScarThatGrowsWithTheDamage()
    {
        var d = Look.Tuning.Damage;
        Assert.Equal(2, d.States.Length);
        Assert.True(d.States[0] > d.States[1] && d.ScarsFrom > d.ScarsFull);
        Assert.Equal(0, d.StateOf(1));
        Assert.Equal(1, d.StateOf((d.States[0] + d.States[1]) / 2));
        Assert.Equal(2, d.StateOf(0));
        Assert.Equal(0, d.ScarOf(1));
        Assert.Equal(1, d.ScarOf(0));
        // Never better for being worse.
        float last = 0;
        int state = 0;
        for (double i = 1; i >= 0; i -= 0.01)
        {
            Assert.True(d.ScarOf(i) >= last && d.StateOf(i) >= state);
            last = d.ScarOf(i);
            state = d.StateOf(i);
        }
    }

    [Fact]
    public void ACarsDamageIsItsOwnAndWorseKeepsWhatItHad()
    {
        var shape = CarShape.Build(Tuning.Geometry, VehicleKind.Cargo, hasCarBehind: true);
        var damaged = DamageKit.Car(Look, shape, 1, 3).Vertices;
        var wrecked = DamageKit.Car(Look, shape, 2, 3).Vertices;
        Assert.Equal(damaged.Select(v => v.Position), DamageKit.Car(Look, shape, 1, 3).Vertices.Select(v => v.Position));
        Assert.NotEqual(damaged.Select(v => v.Position), DamageKit.Car(Look, shape, 1, 4).Vertices.Select(v => v.Position));
        // The wrecked state is the damaged one's marks and more: getting worse never moves a scar.
        Assert.True(wrecked.Length > damaged.Length);
        Assert.Equal(damaged.Select(v => v.Position), wrecked.Take(damaged.Length).Select(v => v.Position));
        Assert.InRange(wrecked.Length / 3, 50, ArtCatalog.MediumProp.MaxTriangles);

        // On the car's sides, not off in the air, and clear of the side doors (they slide; the marks wouldn't).
        float w = (float)shape.HalfWidth, l = (float)shape.HalfLength, h = (float)shape.RoofHeight;
        var doors = shape.DoorList.Select(x => x.Box).Where(b => b.Max.Z - b.Min.Z > b.Max.X - b.Min.X).ToArray();
        for (int seed = 0; seed < 12; seed++)
            foreach (var v in DamageKit.Car(Look, shape, 2, seed).Vertices)
            {
                var p = v.Position;
                Assert.InRange(MathF.Abs(p.X), w - 0.1f, w + 0.5f);
                Assert.InRange(p.Y, 0, h);
                Assert.InRange(MathF.Abs(p.Z), 0, l);
                foreach (var b in doors)
                    Assert.False(Math.Sign(p.X) == Math.Sign(b.Min.X) && p.Z > b.Min.Z + 0.05 && p.Z < b.Max.Z - 0.05 && p.Y > b.Min.Y + 0.05 && p.Y < b.Max.Y - 0.05,
                        $"seed {seed}: {p} is on a side door");
            }
    }

    [Fact]
    public void TheSceneScarsACarByItsIntegrityAndKeepsItsSeed()
    {
        var line = RailLine.Load(Path.Combine(Content, "lines/test-loop.json"));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning, 4, 1)), line, 1200);
        train.Vehicles[2].Integrity = 0.2;
        var mesh = new MeshBuilder();
        new GreyboxScene { Look = Look, Time = 0.37 }.Build(mesh, train, train.Frames[2].Origin);
        var scarred = mesh.Instances.Where(i => i.Scar.X > 0).ToArray();
        Assert.NotEmpty(scarred);
        Assert.All(scarred, i => Assert.Equal(Look.Tuning.Damage.ScarOf(0.2), i.Scar.X));
        Assert.Single(scarred.Select(i => i.Scar.Y).Distinct()); // one car, one pattern: its body and its doors
        Assert.Contains(mesh.Instances, i => i.Asset.Name == "damage-2");
        Assert.DoesNotContain(mesh.Instances, i => i.Asset.Name == "damage-1");
    }

    [Fact]
    public void ScarsShowWithoutBuryingTheCar()
    {
        GpuContext gpu;
        try { gpu = new GpuContext("damage tests"); }
        catch (GpuUnavailableException e) { Assert.Skip($"no Vulkan: {e.Message}"); throw; }
        using (gpu)
        {
            var whole = Render(gpu, 1);
            var wrecked = Render(gpu, 0.1);
            double a = whole.Average(), b = wrecked.Average();
            // Scorched and holed it's darker, and it's plainly different...
            Assert.True(b < a * 0.97, $"wrecked {b:0.000} vs whole {a:0.000}");
            Assert.True(whole.Zip(wrecked).Count(p => Math.Abs(p.First - p.Second) > 0.02) > whole.Length / 20);
            // ...but it's still a car in the frame, not a black shape.
            Assert.True(b > a * 0.6, $"wrecked {b:0.000} vs whole {a:0.000}");
        }

        static double[] Render(GpuContext gpu, double integrity)
        {
            var line = RailLine.Load(Path.Combine(Content, "lines/test-loop.json"));
            var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning, 4, 1)), line, 1200);
            foreach (var v in train.Vehicles)
                v.Integrity = integrity;
            var car = train.Frames[2];
            // Beside the car in its own frame, a lantern's worth of light on its flank.
            var eye = car.ToWorld(new Double3(-5.5, 2.2, 2));
            var camera = Camera.LookAt(eye, car.ToWorld(new Double3(0, 2, -1)), 60);
            var mesh = new MeshBuilder();
            new GreyboxScene { Look = Look, Time = 0.37 }.Build(mesh, train, camera.Position);
            mesh.PointLights.Add(new PointLight(car.ToWorld(new Double3(-2.5, 2.4, 0)).RelativeTo(camera.Position), new Vector3(1, 0.75f, 0.45f) * 1.5f, 9));
            using var renderer = new GreyboxRenderer(gpu, 240, 135);
            Look.Dress(renderer);
            var lighting = Views.Lighting(train, Look);
            var px = renderer.Render(mesh, camera, lighting, lighting.FogColor);
            var luma = new double[px.Length / 4];
            for (int i = 0; i < luma.Length; i++)
                luma[i] = (0.3 * px[i * 4] + 0.59 * px[i * 4 + 1] + 0.11 * px[i * 4 + 2]) / 255;
            return luma;
        }
    }
}
