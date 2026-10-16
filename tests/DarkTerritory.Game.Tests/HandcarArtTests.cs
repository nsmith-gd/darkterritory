using Ballast;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// Note 816: the modelled handcar as the scene has it from the train: hung on the van, hooked by its two chains at the van's
/// roof end over its back (and turned no wheel); on the rails, off the chains, its wheels turned by the distance it's run.
/// </summary>
public sealed class HandcarArtTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TrainTuning T = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));

    [Fact]
    public void HungByItsChainsFromTheVanAndRolledOnTheRails()
    {
        var t = T with { Handcar = T.Handcar with { Always = true, Parked = false } };
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(t, 4, 1)), new RailLine(new LineDefinition("t", [new TrackSegment(20_000)])), 5_000);
        var scene = new GreyboxScene();
        scene.HandcarFrom(train);
        var hooks = Assert.NotNull(scene.HandcarHooks);
        var van = train.Frames[train.HandcarHost];
        var trolley = train.Frames[train.Handcar!.Id];
        // At the van's roof end, either side of its middle, over the trolley hung behind it.
        foreach (var hook in (ReadOnlySpan<Double3>)[hooks.Left, hooks.Right])
        {
            var local = van.ToLocal(hook);
            Assert.InRange(local.Y, van.Shape.RoofHeight - 0.5, van.Shape.RoofHeight);
            Assert.True(local.Z > 0, $"hooked at the van's back end ({local.Z:0.00})");
            Assert.True(hook.Y > trolley.Origin.Y, "over the trolley");
        }
        Assert.Equal(1.2, (hooks.Right - hooks.Left).Length, 3);
        Assert.Equal(0, scene.HandcarRolled);
        // Set on the rails: off its chains, its wheels turned by its run.
        Assert.True(train.SetHandcarOnRails());
        scene.HandcarFrom(train);
        Assert.Null(scene.HandcarHooks);
        Assert.Equal(train.HandcarRake!.Distance, scene.HandcarRolled);
    }
}
