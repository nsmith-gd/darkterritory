using Ballast;
using Ballast.Render;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.LineGen;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The water with life in it (ARCHITECTURE §8 note 424): its surface stays put under the camera (it slid along with the
/// train before), carries its kind's current and openness to the wind for the shader's ripples and swell, and a lake's
/// water runs on under the land past its shore, never out over it.
/// </summary>
public class WaterArtTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly Look Look = Look.Load(Content);

    static List<Vertex> Water(Sim.Route.Route route, Double3 eye, out int layer)
    {
        var art = new WorldArt(Look);
        var mesh = new MeshBuilder();
        art.Plan(mesh, route.Build(), route, eye, 0, 600, 100);
        int water = Look.Layer("water_dark");
        layer = water;
        return [.. mesh.Vertices.ToArray().Where(v => (int)v.Layer == water)];
    }

    [Fact]
    public void OnlyWaterMovesAsWater()
    {
        Assert.Equal(2, GreyboxRenderer.Motion("water_dark"));
        Assert.Equal(1, GreyboxRenderer.Motion("spruce_card"));
        Assert.Equal(1, GreyboxRenderer.Motion("pine_bough"));
        Assert.Equal(0, GreyboxRenderer.Motion("tar"));
        Assert.Equal(0, GreyboxRenderer.Motion("shore_shingle"));
    }

    [Fact]
    public void TheWaterStaysPutUnderTheCameraAndMovesAsItsKindDoes()
    {
        // frontier:3: its lakes beside the line at 6.7 km, and the sea.
        var route = Routes.Generate(Content, "frontier:3", 6);
        var line = route.Build();
        var plan = route.Plan!;
        var lake = plan.Lakes.OrderBy(l => Math.Abs(l.X - line.Sample(6734).Position.X) + Math.Abs(l.Z - line.Sample(6734).Position.Z)).First();
        foreach (var at in new[] { 6600.0, 6750 })
        {
            var eye = line.Sample(at).Position + new Double3(0, 3, 0);
            var water = Water(route, eye, out _);
            Assert.NotEmpty(water);
            foreach (var v in water)
            {
                // Its texture coordinates are where it lies in the world (wrapped at 4096 m, as the grime's are), whoever's looking.
                double dx = v.Uv.X - (v.Position.X + eye.X), dz = v.Uv.Y - (v.Position.Z + eye.Z);
                Assert.True(Math.Abs(dx / 4096 - Math.Round(dx / 4096)) < 1e-4 && Math.Abs(dz / 4096 - Math.Round(dz / 4096)) < 1e-4,
                    $"water at {v.Position.X + eye.X:0},{v.Position.Z + eye.Z:0} is mapped at {v.Uv} from {at:0}");
                Assert.InRange(v.Blend, 0f, 1f);
            }
            // The lake's: still, as open to the wind as a lake is, and it runs on under the land past its shore, never out
            // over it (its rim stands over it: tiers.json lakes.rimM).
            var kind = Look.Tuning.Water.Of("lake");
            var terrain = ((PlanConditions)line.Conditions!).Terrain;
            var its = water.Where(v => Math.Abs(v.Position.Y + eye.Y - lake.LevelM) < 0.01
                && TerrainField.LakeMetric(lake, v.Position.X + eye.X, v.Position.Z + eye.Z) < 1.2).ToList();
            Assert.NotEmpty(its);
            foreach (var v in its)
            {
                Assert.Equal(0, v.Surface.X);
                Assert.Equal(0, v.Surface.Y);
                Assert.Equal(kind.Open, v.Surface.Z);
                double x = v.Position.X + eye.X, z = v.Position.Z + eye.Z;
                if (TerrainField.LakeMetric(lake, x, z) > 1)
                    Assert.True(terrain.Height(x, z) > lake.LevelM, $"{lake.Id}'s water stands over its shore at {x:0},{z:0}");
            }
        }
    }

    [Fact]
    public void ARiverRunsUnderItsSpanAndTheSeaLiesOpen()
    {
        // frontier:7's tidal river at 8.1 km: it runs across under the line, one way or the other, at its kind's current, and
        // laps at its banks (its blend 1 at either end of its crossing, less out on it).
        var route = Routes.Generate(Content, "frontier:7", 6);
        var line = route.Build();
        var river = route.Plan!.Water.First(w => w.Type == "tidal");
        var mid = line.Sample((river.S0 + river.S1) / 2);
        var eye = mid.Position + new Double3(0, 3, 0);
        var kind = Look.Tuning.Water.Of("tidal");
        var water = Water(route, eye, out _).Where(v => Math.Abs(v.Position.Y + eye.Y - river.LevelM) < 0.01).ToList();
        Assert.NotEmpty(water);
        foreach (var v in water)
        {
            float speed = MathF.Sqrt(v.Surface.X * v.Surface.X + v.Surface.Y * v.Surface.Y);
            Assert.InRange(speed, kind.Current * 0.99f, kind.Current * 1.01f);
            Assert.True(Math.Abs(v.Surface.X * mid.Tangent.X + v.Surface.Y * mid.Tangent.Z) < 0.25 * kind.Current, "across the line, not along it");
            Assert.Equal(kind.Open, v.Surface.Z);
        }
        Assert.Contains(water, v => v.Blend == 1);
        Assert.Contains(water, v => v.Blend < 1);

        // local:1's sea at 9.4 km: the open Atlantic, its lap along the beach's foot and none out on it.
        var coast = Routes.Generate(Content, "local:1", 6);
        var shore = coast.Plan!.Shores.First(sh => sh.Kind == ShoreKind.Sea && sh.S0 < 9400 && sh.S1 > 9400);
        var at = coast.Build().Sample(9400).Position + new Double3(0, 3, 0);
        var sea = Water(coast, at, out _).Where(v => Math.Abs(v.Position.Y + at.Y - shore.LevelM) < 0.01).ToList();
        Assert.NotEmpty(sea);
        Assert.All(sea, v => Assert.Equal(Look.Tuning.Water.Of("sea").Open, v.Surface.Z));
        Assert.Contains(sea, v => v.Blend == 1);
        Assert.Contains(sea, v => v.Blend == 0);
    }
}
