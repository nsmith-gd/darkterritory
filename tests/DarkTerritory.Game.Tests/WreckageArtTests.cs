using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// ARCHITECTURE §8 note 394: the wreck yard's heaps (note 187) are drawn as the train's own cars, wrecked, lying as the
/// sim's heap lies: on its side where it is, rolled further each time it's shifted.
/// </summary>
public class WreckageArtTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TrainTuning Tuning = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));

    static Site WreckYard()
    {
        // frontier:1's first facility is a wreck yard.
        var night = Sim.LineGen.Routes.Generate(Content, "frontier:1", 6);
        var line = night.Build();
        var run = new Run(DataFile.Load<RunTuning>(Path.Combine(Content, RunTuning.File)), night);
        run.EnableSites(DataFile.Load<FacilityTuning>(Path.Combine(Content, FacilityTuning.File)), line);
        return run.Sites.First(s => s is { Heaps.Count: > 0 })!;
    }

    static IReadOnlyList<CarFrame> Cars() =>
        new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning, 3, 1)), new RailLine(new LineDefinition("t", [new TrackSegment(5_000)])), 1_000).Frames;

    [Fact]
    public void EachHeapIsACarOnItsSideWhereTheSimHasItAndTipsFurtherAsItShifts()
    {
        var site = WreckYard();
        var art = new SceneArt(Look.Load(Content));
        var eye = site.Heaps[0].Centre + new Double3(0, 2, 0);
        var mesh = new MeshBuilder();
        Assert.True(art.Wreckage(mesh, site, Cars(), eye, 0, 1_000));
        // Its body and its wrecked damage, for every heap.
        Assert.Equal(2 * site.Heaps.Count, mesh.Instances.Count);
        for (int i = 0; i < site.Heaps.Count; i++)
        {
            var m = mesh.Instances[2 * i].Model;
            var up = new Vector3(m.M21, m.M22, m.M23);
            // On its side (the roof's way out over the ground), lying where the heap is.
            Assert.InRange(up.Y, -0.35f, 0.35f);
            var middle = new Vector3(m.M41, m.M42, m.M43) + up * 1.6f;
            var heap = site.Heaps[i].Centre.RelativeTo(eye);
            Assert.True(Vector3.Distance(middle with { Y = 0 }, heap with { Y = 0 }) < 2.5f, $"heap {i} drawn {Vector3.Distance(middle, heap):0.0} m off");
        }
        // Shifted twice, the first is over further than it was.
        double before = mesh.Instances[0].Model.M22;
        site.Heaps[0].Mirror(new HeapState(site.Heaps[0].Salvage, true, 1, 0, 2));
        var again = new MeshBuilder();
        art.Wreckage(again, site, Cars(), eye, 0, 1_000);
        Assert.True(again.Instances[0].Model.M22 < before - 0.2, $"up.Y {before:0.00} then {again.Instances[0].Model.M22:0.00}");
    }

    [Fact]
    public void WithoutACarToDrawItAsTheGreyboxDrawsIt()
    {
        var site = WreckYard();
        var engineOnly = Cars().Where(f => f.Shape.Cab is not null).ToList();
        Assert.False(new SceneArt(Look.Load(Content)).Wreckage(new MeshBuilder(), site, engineOnly, site.Heaps[0].Centre, 0, 1_000));
    }
}
