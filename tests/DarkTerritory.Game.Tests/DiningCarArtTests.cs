using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The dining car modelled (tools/models/recipes/dining_car.py, note 974, over note 959's greybox): every piece made; the
/// table's cloth and the counter on the sim's own boxes; the chandelier's and candelabra's wicks where the game lights them,
/// and the room drawn from the pieces, its candles burning.
/// </summary>
public class DiningCarArtTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly Look Look = Look.Load(Content);

    static (Vector3 Min, Vector3 Max) Bounds(MeshAsset m) =>
        (m.Vertices.Aggregate(new Vector3(float.MaxValue), (a, v) => Vector3.Min(a, v.Position)),
         m.Vertices.Aggregate(new Vector3(float.MinValue), (a, v) => Vector3.Max(a, v.Position)));

    static readonly string[] Pieces = ["dining_table", "dining_chair", "dining_place", "dining_candelabrum", "dining_chandelier", "bar_counter", "bar_bell", "bar_back"];

    static CarShape DiningShape()
    {
        T Load<T>(string file) => DataFile.Load<T>(Path.Combine(Content, file));
        var loadout = new Loadout(Load<TrainTuning>(TrainTuning.File), Load<BoilerTuning>(BoilerTuning.File),
            Load<Sim.Combat.CombatTuning>(Sim.Combat.CombatTuning.File), Load<Sim.Enemies.EnemyTuning>(Sim.Enemies.EnemyTuning.File));
        var t = Campaign.Apply(Load<CampaignTuning>(CampaignTuning.File), ["diningCar"], loadout).Train;
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(t, 6, 1)), RailLine.Load(Path.Combine(Content, "lines/test-loop.json")), 1200);
        return train.Frames.First(f => f.Shape.Dining is not null).Shape;
    }

    [Fact]
    public void ThePiecesAreMadeAndTheClothAndCounterAreTheSimsTableAndCounter()
    {
        var props = PropArt.Of(Look);
        foreach (var name in Pieces)
            Assert.NotNull(props.Get(name));
        var shape = DiningShape();
        var d = shape.Dining!;
        // The cloth's top on the table's (the sim's), over all of it, hanging a hand down and out round it.
        var (tmin, tmax) = Bounds(props.Get("dining_table")!);
        float tableH = (float)(d.Table.Max.Y - d.Table.Min.Y), tableW = (float)(d.Table.Max.X - d.Table.Min.X);
        Assert.InRange(tmax.Y, tableH, tableH + 0.015f);
        Assert.InRange(tmax.X - tmin.X, tableW + 0.06f, tableW + 0.2f);
        Assert.InRange(tmax.Z - tmin.Z, 6.6f + 0.06f, 6.6f + 0.2f);
        // The counter's top at the sim's counter's (a bottle stands on it), its front to the room (-Z) and its rail out from it.
        var (cmin, cmax) = Bounds(props.Get("bar_counter")!);
        Assert.InRange(cmax.Y, (float)(d.Counter.Max.Y - d.Counter.Min.Y), 1.4f);
        Assert.True(cmin.Z < -0.3f - 0.1f && cmax.Z < 0.32f, $"counter {cmin} .. {cmax}");
        // The back bar stands out from its wall (its origin) into the room, and fits between the counter's end and the car's side.
        var (bmin, bmax) = Bounds(props.Get("bar_back")!);
        Assert.True(bmax.Z <= 0.01f && bmin.Z > -0.5f && bmax.X - bmin.X < 1.2f, $"back bar {bmin} .. {bmax}");
    }

    [Fact]
    public void TheCandlesAreLitAtTheirWicks()
    {
        var props = PropArt.Of(Look);
        // The chandelier's six wicks hang where its light is (TrainKit.ChandelierAt: 0.75 m under the ceiling, the candles on it).
        for (int i = 0; i < 6; i++)
            Assert.InRange(-props.Socket("dining_chandelier", $"flame_{i}")!.Value.Y, 0.6f, 0.67f);
        for (int i = 0; i < 3; i++)
            Assert.InRange(props.Socket("dining_candelabrum", $"flame_{i}")!.Value.Y, 0.3f, 0.45f);
        // The room from the pieces: a flame burns at each wick, the chandelier's over the table's middle.
        var shape = DiningShape();
        var fit = TrainKit.DiningFit(Look, shape);
        int flame = Look.Layer("window_lit");
        var lit = fit.Vertices.Where(v => v.Layer == flame).Select(v => v.Position).ToArray();
        var c = TrainKit.ChandelierAt(shape);
        float ceiling = (float)shape.Interior!.Value.Max.Y;
        for (int i = 0; i < 6; i++)
        {
            var wick = props.Socket("dining_chandelier", $"flame_{i}")!.Value + new Vector3(c.X, ceiling, c.Z);
            Assert.Contains(lit, p => (p - wick).Length() < 0.06f);
        }
        // More than the greybox's boxes: the modelled pieces are in it.
        Assert.True(fit.Vertices.Length > 20_000, $"{fit.Vertices.Length} vertices");
    }
}
