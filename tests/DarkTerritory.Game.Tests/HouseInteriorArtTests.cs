using System.Text.Json;
using Ballast;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Stops;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The open houses' insides, as the director asked for them on 8 Oct (GDD App. F.3; ARCHITECTURE §8 note 326): a dim light
/// in each, and finds that stand out by their texture (each kind its own model on its own cell of the finds atlas).
/// </summary>
public class HouseInteriorArtTests
{
    static readonly string Content = DataFile.FindContentRoot();

    [Fact]
    public void EveryKindOfFindIsItsOwnThingOnItsOwnCell()
    {
        // The atlas's cells as the texture tool built them (index.json), in FindKit's order: the content contract.
        var index = JsonDocument.Parse(File.ReadAllText(Path.Combine(Content, "art", "textures", "index.json"))).RootElement;
        var atlas = index.EnumerateArray().Single(e => e.GetProperty("name").GetString() == "finds_atlas");
        var cells = atlas.GetProperty("cells").EnumerateArray().Select(c => c.GetProperty("name").GetString()!).ToArray();
        Assert.Equal(FindKit.Cells, cells);
        // Every item loot.json can deal out has a model of its own and a cell, not the plain bundle.
        var loot = DataFile.Load<LootTuning>(Path.Combine(Content, LootTuning.File));
        var plain = FindKit.Find(null, "nothing", 0.15f).Vertices.Length;
        foreach (var item in loot.Items.Keys)
        {
            Assert.Contains(item, FindKit.Cells);
            var model = FindKit.Find(null, item, 0.15f);
            Assert.True(model.Vertices.Length > 0 && model.Vertices.Length != plain, $"{item} is the plain bundle");
        }
    }

    [Theory]
    [InlineData("frontier:7")]
    [InlineData("deadLines:2")]
    public void EveryOpenHouseHasItsDimLightInsideItClearOfTheFurniture(string spec)
    {
        var route = Routes.Generate(Content, spec, 6);
        int candles = 0, lamps = 0;
        foreach (var f in route.Features.Where(f => f.Stop is not null))
        {
            var stop = f.Stop!;
            for (int i = 0; i < stop.Buildings.Count; i++)
            {
                var b = stop.Buildings[i];
                if (!b.Open || !StopWalls.Walled(stop, i))
                    continue;
                int index = i;
                var kept = stop.Containers.Where(c => c.Building == index).ToList();
                var (x, y, height, lamp) = TownKit.HouseLight(b, kept);
                Assert.True(StopWalls.InParts(b, x, y), $"{spec} at {f.Start:0}: house {i}'s light is outside it");
                // Inside the room, not in a wall, and a candle not in a cupboard or a cabinet.
                foreach (var (wx, wy, hx, hy) in StopWalls.OpenWalls(b))
                    Assert.False(Math.Abs(x - wx) < hx && Math.Abs(y - wy) < hy, $"{spec} at {f.Start:0}: house {i}'s light is in a wall");
                if (!lamp)
                    foreach (var piece in StopWalls.Furniture(b, kept))
                        Assert.False(Math.Abs(x - piece.X) < piece.HalfX + 0.1 && Math.Abs(y - piece.Y) < piece.HalfY + 0.1,
                            $"{spec} at {f.Start:0}: house {i}'s candle is in its {piece.Kind}");
                Assert.InRange(height, 0.1f, 2.6f);
                candles += lamp ? 0 : 1;
                lamps += lamp ? 1 : 0;
            }
        }
        // Candles in the corners, and lamps.
        Assert.True(candles > 0 && lamps > 0, $"{spec}: {candles} candles, {lamps} lamps");
    }
}
