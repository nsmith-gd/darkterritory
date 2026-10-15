using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The big cab as the crew car, drawn (ARCHITECTURE §8 note 716): the run map laid flat on the chart table, the crew's lamps
/// along its front end, the back wall's gimmicks on the back wall, and the kettle steaming only as it sings.
/// </summary>
public class CrewCarArtTests
{
    static readonly string Content = DataFile.FindContentRoot();

    static CarFrame Engine()
    {
        var t = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
        var line = RailLine.Load(Path.Combine(Content, "lines", "test-loop.json"));
        return new TrainOnLine(new TrainDynamics(Consist.Uniform(t, 3, 1)), line, 1200).Frames[0];
    }

    [Fact]
    public void TheMapLiesFlatOnTheChartTableReadFromTheDriversSide()
    {
        var shape = Engine().Shape;
        var table = TrainKit.ChartTable(shape)!.Value;
        var (corner, w, h) = TrainKit.MapPlate(shape);
        var onCab = TrainKit.MapToCab(shape);
        foreach (var p in new[] { corner, corner + new Vector3(w, 0, 0), corner + new Vector3(0, h, 0), corner + new Vector3(w, h, 0) })
        {
            var at = Vector3.Transform(p, onCab);
            Assert.InRange(at.X, (float)table.Min.X, (float)table.Max.X);
            Assert.InRange(at.Z, (float)table.Min.Z, (float)table.Max.Z);
            Assert.InRange(at.Y, (float)table.Max.Y, (float)table.Max.Y + 0.02f);
        }
        // Face up; the night runs from the back of the table to its front, the way the train goes; its top edge across the
        // table from the driver's side (+X), the turn a turn and never a mirror.
        Assert.True(Vector3.TransformNormal(Vector3.UnitZ, onCab).Y > 0.99f);
        Assert.True(Vector3.TransformNormal(Vector3.UnitX, onCab).Z < -0.99f);
        Assert.True(Vector3.TransformNormal(Vector3.UnitY, onCab).X < -0.99f);
        Assert.True(onCab.GetDeterminant() > 0);
    }

    [Fact]
    public void TheCrewsLampsStandAlongTheTablesFrontEndClearOfTheMap()
    {
        var shape = Engine().Shape;
        var table = TrainKit.ChartTable(shape)!.Value;
        var (corner, w, h) = TrainKit.MapPlate(shape);
        var onCab = TrainKit.MapToCab(shape);
        float mapFront = new[] { Vector3.Transform(corner + new Vector3(w, 0, 0), onCab).Z, Vector3.Transform(corner + new Vector3(w, h, 0), onCab).Z }.Min();
        foreach (int count in new[] { 1, 4, 8 })
        {
            var lamps = Enumerable.Range(0, count).Select(i => TrainKit.CrewLamp(shape, i, count)).ToList();
            Assert.All(lamps, l =>
            {
                Assert.InRange(l.X, (float)table.Min.X, (float)table.Max.X);
                Assert.InRange(l.Z, (float)table.Min.Z, mapFront);
                Assert.True(l.Y > table.Max.Y);
            });
            Assert.Equal(count, lamps.Select(l => l.X).Distinct().Count());
        }
    }

    [Fact]
    public void TheBackWallsGimmicksAreOnTheBackWall()
    {
        var shape = Engine().Shape;
        var cab = shape.Cab!.Value;
        var points = new[] { TrainKit.KettleSpout(shape), TrainKit.CuckooDoor(shape), TrainKit.GramophoneShelf(shape) }
            .Concat(Enumerable.Range(0, 8).Select(i => TrainKit.CapHook(shape, i)));
        foreach (var p in points)
        {
            Assert.True(cab.Contains(new Double3(p.X, p.Y, p.Z)), $"{p} is out of the cab");
            // Behind the gallery: on the back wall's own floor.
            Assert.True(p.Z > TrainKit.GalleryFloor(shape)!.Value.Max.Z, $"{p} is up on the gallery");
        }
        // The gramophone's place is clear: the kettle's on the shelf's other half.
        Assert.True(Vector3.Distance(TrainKit.KettleSpout(shape), TrainKit.GramophoneShelf(shape)) > 0.4f);
    }

    [Fact]
    public void TheKettleSteamsOnlyWhileItSings()
    {
        var look = Look.Load(Content);
        var mesh = new MeshBuilder();
        look.Art.Effects.KettleSteam(mesh, Vector3.Zero, Vector3.UnitY, -Vector3.UnitZ, 0, 1.0);
        Assert.Empty(mesh.AlphaFx);
        look.Art.Effects.KettleSteam(mesh, Vector3.Zero, Vector3.UnitY, -Vector3.UnitZ, 0.3f, 1.0);
        int some = mesh.AlphaFx.Count;
        Assert.True(some > 0);
        mesh = new MeshBuilder();
        look.Art.Effects.KettleSteam(mesh, Vector3.Zero, Vector3.UnitY, -Vector3.UnitZ, 1, 1.0);
        Assert.True(mesh.AlphaFx.Count > some, "no more steam at the redline than near it");
    }
}
