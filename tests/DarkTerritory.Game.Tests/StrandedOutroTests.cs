using Ballast;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// GDD v1.4 App. E.9, the Stranded outro (note 661): its pull-back is over the crew's own stopped train. The director's, at
/// Voss Green on 13 Oct: "it went to a train that was in ... a town. So it wasn't my train".
/// </summary>
public class StrandedOutroTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TrainTuning Trains = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));

    [Fact]
    public void ThePullBackIsOverTheCrewsOwnTrainNotOtherCarsOnTheLine()
    {
        // Derelicts standing 2 km up the line (a yard's, note 294): more vehicles than the train has, so the middle of them all
        // is one of theirs.
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(20_000)]));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Trains, 4, 1)), line, 3_000);
        train.StandDerelicts(RailLine.MainPath, 5_000, 8, 0.5, 0.3);
        Assert.True(train.Frames.Count > 2 * train.Dynamics.Consist.Vehicles.Count);
        var t = new World(train).WreckTuning.Stranded;
        var cam = Views.Stranded(train, t, t.Seconds);
        var rake = train.Dynamics.Consist.Vehicles;
        double nearest = rake.Min(v => (train.Frames[v.Id].Origin - cam.Position).Length);
        Assert.True(nearest < t.BackM + t.HeightM + 20, $"the high wide is {nearest:0} m from the crew's train");
    }
}
