using Ballast;
using Ballast.Render;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Stops;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The generated stops as the art pass draws them (level-design P1-P14): the ground under a stop is level, since the sim
/// walks people out to its village at rail height; and the lineside cells round a stop build its buildings.
/// </summary>
public class StopArtTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly Route Night = RouteGenerator.Generate(RouteTuning.Load(Content), RouteTier.Frontier, 7);

    [Fact]
    public void TheGroundUnderAStopIsLevelOutToItsVillage()
    {
        var stops = Night.Features.Where(f => f.Stop is not null).ToList();
        Assert.NotEmpty(stops);
        foreach (var f in stops)
            foreach (var b in f.Stop!.Buildings)
            {
                double s = f.Start + b.S;
                Assert.Equal(1, WorldArt.Flat(Night, s));
                // Past the formation's ditch the profile is rail height; the hills are levelled away.
                if (Math.Abs(b.D) > 6)
                    Assert.InRange(WorldArt.Ground(Night, s, (float)b.D, 18), -0.01f, 0.01f);
            }
    }

    [Fact]
    public void EveryBuildingOfAStopStandsInItsCell()
    {
        var art = new WorldArt(Look.Load(Content));
        var line = Night.Build();
        var yard = Night.Features.First(f => f.Stop is { HasYard: true, HasVillage: true });
        var eye = line.Sample(yard.Start).Position;
        var mesh = new MeshBuilder();
        art.Lineside(mesh, line, Night, eye, yard.Start - 250, yard.End + 250, 7, 18);
        var tall = mesh.Vertices.ToArray().Where(v => v.Position.Y > 2.5f).Select(v => v.Position).ToList();
        foreach (var b in yard.Stop!.Buildings.Where(b => b.Kind != BuildingKind.Well))
        {
            // Walls up over its footprint: the trees are kept off it, so what stands there is the building.
            var at = Sim.Run.Run.StopWorld(line, yard, b.Centre).RelativeTo(eye);
            double reach = double.Hypot(b.Length, b.Width) / 2 + 0.5;
            Assert.True(tall.Any(p => double.Hypot(p.X - at.X, p.Z - at.Z) <= reach), $"{b.Kind} at {b.S:0},{b.D:0} isn't drawn");
        }
    }
}
