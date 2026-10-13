using System.Numerics;
using Ballast;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The comet's green leaking out of its car's seams (the art checklist's comet "next"; GDD §19 "attracts everything; should
/// look like it"): round every shut door and the shut roof hatch, on the outside, and not round one that's open.
/// </summary>
public class CometArtTests
{
    static readonly string Content = DataFile.FindContentRoot();

    static CarShape Cargo()
    {
        var t = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(t, 3, 1)), Sim.LineGen.Routes.Generate(Content, "frontier:7", 3).Build(), 900,
            DataFile.Load<Sim.Train.BoilerTuning>(Path.Combine(Content, Sim.Train.BoilerTuning.File)));
        return train.Frames[1].Shape;
    }

    [Fact]
    public void EveryShutDoorAndTheHatchAreOutlinedOnTheOutside()
    {
        var shape = Cargo();
        Assert.True(shape.DoorList.Count >= 4, "a cargo car has its end doors and its side doors");
        var seams = SceneArt.CometSeams(shape, 0).ToList();
        foreach (var door in shape.DoorList)
        {
            var c = new Vector3((float)door.Box.Centre.X, (float)door.Box.Centre.Y, (float)door.Box.Centre.Z);
            var near = seams.Where(s => s.Normal != Vector3.UnitY && Vector3.Distance((s.A + s.B) / 2, c) < 1.6f).ToList();
            Assert.True(near.Count >= 4, $"door {door.Index}: {near.Count} seams round it");
            // On the wall's outer face: out from the car's middle, the way its normal points.
            Assert.All(near, s => Assert.True(Vector3.Dot((s.A + s.B) / 2, s.Normal) > Vector3.Dot(c, s.Normal), $"door {door.Index}: a seam inside its wall"));
        }
        Assert.Contains(seams, s => s.Normal == Vector3.UnitY);
    }

    [Fact]
    public void AnOpenDoorHasNoSeamsItsLightPoursOutInstead()
    {
        var shape = Cargo();
        var side = shape.DoorList.First(d => d.Box.Max.Z - d.Box.Min.Z > d.Box.Max.X - d.Box.Min.X);
        var c = new Vector3((float)side.Box.Centre.X, (float)side.Box.Centre.Y, (float)side.Box.Centre.Z);
        int Round(int open) => SceneArt.CometSeams(shape, open).Count(s => s.Normal != Vector3.UnitY && Vector3.Distance((s.A + s.B) / 2, c) < 1.6f);
        Assert.True(Round(0) >= 4);
        Assert.Equal(0, Round(1 << side.Index));
        // The hatch open: no seams on the roof.
        Assert.DoesNotContain(SceneArt.CometSeams(shape, 1 << CarShape.HatchBit), s => s.Normal == Vector3.UnitY);
    }
}
