using System.Numerics;
using Ballast;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Towns;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// A house turned off true (ARCHITECTURE §8 note 490) is drawn where its walls stand: its body turned inside its mesh about
/// its middle, its yard square to its lot, its door lamp and chimney smoke with its body.
/// </summary>
public class TownTurnArtTests
{
    static readonly string Content = DataFile.FindContentRoot();

    [Fact]
    public void ATurnedHouseIsDrawnWhereItsWallsStand()
    {
        var route = Sim.LineGen.Routes.Generate(Content, "local:5", 6);
        var line = route.Build();
        var towns = TownContent.Load(Content)!;
        double gate = route.GateOr(Sim.Route.RouteTuning.Load(Content).YardLength);
        var roster = DataFile.Load<Sim.Enemies.EnemyTuning>(Path.Combine(Content, Sim.Enemies.EnemyTuning.File)).Director.Roster;
        var town = new Town(TownGenerator.Generate(towns, TownSite.Of(route, gate, roster, towns)), towns.Tuning, line, towns.Looks);
        var turned = town.Plan.Houses.Where(h => Math.Abs(h.Turn) > 0.03).Take(12).ToList();
        Assert.True(turned.Count >= 6, $"{turned.Count} houses turned more than 2° in local:5's town");
        foreach (var h in turned)
        {
            var eye = town.World(h.S, h.D, 1.7) + new Double3(3, 0, -5);
            var m = WorldArt.HouseAt(line, eye, town, h);
            // Its body's corners, as the Sim stands them and as the art draws them.
            foreach (var (u, v) in new[] { (-h.Width / 2, 0.0), (h.Width / 2, 0.0), (-h.Width / 2, h.Depth), (h.Width / 2, h.Depth) })
            {
                var (s, d) = h.Body(u, v);
                var sim = town.World(s, d).RelativeTo(eye);
                var art = Vector3.Transform(MaritimeKit.BodyPoint(h, u, v), m);
                Assert.True(Vector3.Distance(sim with { Y = 0 }, art with { Y = 0 }) < 0.02f, $"house {h.Id} ({h.Turn * 180 / Math.PI:0.0}°): corner ({u:0.0}, {v:0.0}) drawn {Vector3.Distance(sim with { Y = 0 }, art with { Y = 0 }):0.00} m off its wall");
            }
            // Its yard square to its lot: a yard thing's corner as the Sim has it (Rail) is where the mesh has it unturned.
            foreach (var y in h.Yard.Take(2))
            {
                var (s, d) = h.Rail(y.U0, y.V0);
                var sim = town.World(s, d).RelativeTo(eye);
                var art = Vector3.Transform(new Vector3((float)(h.Side * y.U0), 0, (float)(y.V0 - h.Depth / 2)), m);
                Assert.True(Vector3.Distance(sim with { Y = 0 }, art with { Y = 0 }) < 0.02f, $"house {h.Id}'s {y.Kind} drawn off its lot");
            }
        }
    }
}
