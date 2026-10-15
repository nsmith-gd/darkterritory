using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Towns;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// A harbour town's Hope Tower drawn (queue #547, note 819): the tower is its fixture's size, so what's solid is what's
/// drawn; its Flame burns in its basket, lights it, and is seen far off over the town's own glow; and a town type's
/// landmarks are drawn once, by its form, not again as a box of their size by the square (as they were till note 819).
/// </summary>
public class HopeTowerArtTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly Look Look = Look.Load(Content);

    static (Sim.Rail.RailLine Line, Town Town) Harbour()
    {
        var route = Sim.LineGen.Routes.Generate(Content, "frontier:7", 6);
        var line = route.Build();
        var loaded = TownContent.Load(Content)!;
        var towns = loaded with { Tuning = loaded.Tuning with { Forms = loaded.Tuning.Forms with { Force = "harbour" } } };
        double gate = route.GateOr(Sim.Route.RouteTuning.Load(Content).YardLength);
        var roster = DataFile.Load<Sim.Enemies.EnemyTuning>(Path.Combine(Content, Sim.Enemies.EnemyTuning.File)).Director.Roster;
        var town = new Town(TownGenerator.Generate(towns, TownSite.Of(route, gate, roster, towns)), towns.Tuning, line, towns.Looks);
        town.Lay(line);
        return (line, town);
    }

    [Fact]
    public void TheTowerIsItsFixturesSizeAndCheap()
    {
        var tower = HopeKit.Tower(Look);
        Assert.InRange(tower.Triangles, 500, ArtCatalog.LargeProp.MaxTriangles);
        var lo = new Vector3(float.MaxValue);
        var hi = new Vector3(float.MinValue);
        foreach (var v in tower.Vertices)
        {
            lo = Vector3.Min(lo, v.Position);
            hi = Vector3.Max(hi, v.Position);
        }
        var (hs, hd, h) = TownFixtures.Size("lighthouse");
        Assert.True(lo.X >= -hs - 0.3 && hi.X <= hs + 0.3, $"{lo.X:0.00}..{hi.X:0.00} across, its box ±{hs}");
        Assert.True(lo.Z >= -hd - 1.7 && hi.Z <= hd + 0.3, $"{lo.Z:0.00}..{hi.Z:0.00} along, its box ±{hd} (the door's steps a stride out)");
        Assert.InRange(hi.Y, h - 0.6, h + 0.3);
        Assert.InRange(HopeKit.Fire.Y, HopeKit.BasketFloor, HopeKit.BasketBrim);
    }

    [Fact]
    public void ItsFlameBurnsInTheBasketLightsItAndIsSeenFarOff()
    {
        var (line, town) = Harbour();
        var tower = Assert.Single(town.Plan.Fixtures, f => f.Kind == "lighthouse");
        var foot = town.World(tower.S, tower.D);
        // From the head's lanes below it: one Flame, in its basket, at full.
        var near = foot + new Double3(30, 1.7, 30);
        var (fire, _, full) = Assert.Single(WorldArt.Flames(line, near, town));
        var basket = (foot + Double3.Up * HopeKit.Fire.Y).RelativeTo(near);
        Assert.True((fire - basket).Length() < 0.01f, $"the Flame {fire} not in the basket {basket}");
        Assert.Equal(1, full);
        var mesh = new MeshBuilder();
        Look.Art.Effects.Beacon(mesh, fire, HopeKit.BasketTop, 12.5, 7, full);
        // Its light over the gallery and thrown wide over the roofs below.
        Assert.Contains(mesh.PointLights, l => l.Position.Y > fire.Y && l.Range >= 40);
        Assert.Contains(mesh.PointLights, l => l.Range >= 100);
        // From a kilometre off it's still drawn, and its glow on the fog is over the tower, higher than the town's own.
        var far = foot + new Double3(1000, 3, 0);
        var (_, _, faint) = Assert.Single(WorldArt.Flames(line, far, town));
        Assert.InRange(faint, 0.05f, 0.9f);
        var glows = WorldArt.Glow(town).ToList();
        var flame = glows.MaxBy(g => g.At.Y);
        Assert.True(Math.Abs(flame.At.X - foot.X) < 0.5 && Math.Abs(flame.At.Z - foot.Z) < 0.5, "the highest glow isn't over the tower");
        Assert.True(flame.At.Y - foot.Y > HopeKit.Fire.Y, "the Flame's glow under its fire");
        Assert.True(flame.Strength >= glows.Max(g => g.Strength), "the Flame's glow fainter than the town's");
    }

    [Fact]
    public void ATownTypesLandmarksAreDrawnOnceNotBoxedByTheSquare()
    {
        var (line, town) = Harbour();
        var eye = town.World(town.Plan.Square.S0, 0, 2);
        var mesh = new MeshBuilder();
        Look.Art.World.Square(mesh, line, eye, town, 0, line.Length);
        Assert.DoesNotContain(mesh.Instances, i => i.Asset.Name.StartsWith("square-lighthouse") || i.Asset.Name.StartsWith("square-chapel")
            || i.Asset.Name.StartsWith("square-beacon") || i.Asset.Name.StartsWith("square-boatshed"));
        var forms = new MeshBuilder();
        Look.Art.World.Forms(forms, line, eye, town, 0, line.Length, 12.5);
        Assert.Contains(forms.Instances, i => i.Asset.Name == "hope-tower");
    }
}
