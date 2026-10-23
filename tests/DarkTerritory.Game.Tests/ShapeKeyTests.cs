using Ballast;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The playtest of 23 Oct, line 20 (note 1001, the director: "Some of the side doors on the cars now are not being
/// rendered"): a car's body is cooked once per <see cref="SceneArt.ShapeKey"/>, so two shapes that share a key are drawn
/// with whichever was cooked first. Every car the train can have, in every way it's built, keys apart from every other.
/// </summary>
public class ShapeKeyTests
{
    static readonly TrainTuning Tuning = DataFile.Load<TrainTuning>(Path.Combine(DataFile.FindContentRoot(), TrainTuning.File));

    /// <summary>What the body's drawn from: its solids, doors and furniture.</summary>
    static string Built(CarShape s) =>
        string.Join(";", s.Solids.Select(x => $"{x.Part}{x.Box.Min}{x.Box.Max}")) + "|" + string.Join(";", s.DoorList.Select(d => $"{d.Box.Min}{d.Box.Max}"))
        + $"|{s.Stove}|{s.Dining is not null}|{s.Lockers.Count}";

    [Fact]
    public void EveryCarsBodyKeysApart()
    {
        var shapes = new List<(string Name, CarShape Shape)>();
        foreach (var kind in new[] { VehicleKind.Cargo, VehicleKind.Guard, VehicleKind.Utility, VehicleKind.Dining })
            foreach (bool behind in new[] { true, false })
                foreach (var lockers in new[] { null, Tuning.Kit.Lockers })
                {
                    var shape = CarShape.Build(Tuning.Geometry, kind, behind, lockers);
                    shapes.Add(($"{kind}{(behind ? "" : " (last)")}{(lockers is null ? "" : " with lockers")}", shape));
                    if (kind == VehicleKind.Guard && !behind)
                        shapes.Add(($"{kind} (last, dressed)", CarShape.WithLauncher(CarShape.WithHammock(CarShape.WithGramophone(shape)))));
                }
        var clashes = shapes.GroupBy(s => SceneArt.ShapeKey(s.Shape))
            .Where(g => g.Select(s => Built(s.Shape)).Distinct().Count() > 1)
            .Select(g => string.Join(" = ", g.Select(s => s.Name).Distinct())).ToList();
        Assert.True(clashes.Count == 0, "drawn with one another's body: " + string.Join("; ", clashes));
    }
}
