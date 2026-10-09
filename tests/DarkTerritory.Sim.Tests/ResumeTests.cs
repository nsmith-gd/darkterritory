using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// A resumed night's train as its save left it (queue #218, ARCHITECTURE §8 note 481; spec E "Autosave per POI, on
/// successful departure", "Crash: ... rolls back to last POI autosave").
/// </summary>
public class ResumeTests
{
    static readonly TrainTuning T = Tuning.Train;

    /// <summary>
    /// An engine and four cars (ids 0 to 4) at 5 km, and two derelicts standing at 6 km (ids 5 and 6: a blocked siding's, note
    /// 294, which stand wherever they're put, as a switchyard's do on their siding).
    /// </summary>
    static TrainOnLine Night()
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(20_000)]));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 4, 1)), line, 5_000);
        train.StandDerelicts(RailLine.MainPath, 6_000, 2, 0.5, 0.3);
        return train;
    }

    static RakeState Rake(int[] ids, double at, bool handbrake = false, int path = RailLine.MainPath) => new(ids, at, 0, 1, handbrake, false, path);

    [Fact]
    public void TheRakesComeBackAsTheyLeftThePickedUpCarsAheadOfTheEngineAndACutCarWhereItWasLeft()
    {
        var train = Night();
        Assert.Equal(5, train.OwnVehicles);
        Assert.True(train.StandingCar(5));
        // As a save found it: the two picked up ahead of the engine, the train's last car cut off 300 m back.
        Assert.True(train.Resume([Rake([5, 6, 0, 1, 2, 3], 7_000), Rake([4], 6_500, handbrake: true)]));
        Assert.Equal([5, 6, 0, 1, 2, 3], train.Dynamics.Consist.Vehicles.Select(v => v.Id));
        Assert.Equal(7_000, train.Dynamics.Distance);
        Assert.Equal(0, train.Dynamics.Velocity);
        Assert.False(train.StandingCar(5));
        var cut = train.RakeOf(4);
        Assert.NotSame(train.Dynamics, cut);
        Assert.Equal(6_500, cut.Distance);
        Assert.True(cut.Handbrake);
        Assert.Equal(2, train.TrainRakes);
        // At rest where it was put: nothing slides across the screen from where the train was built.
        Assert.All(train.Rakes, r => Assert.Equal(r.Distance, r.PreviousDistance));

        // And it runs on: the engine propels its picked-up cars away, and the cut car stands where it was left.
        for (int i = 0; i < 10 * SimConstants.TickRate; i++)
            train.Step(SimConstants.TickSeconds, new TrainControls { Throttle = 1, Reverser = 1 });
        Assert.True(train.Dynamics.Distance > 7_010, $"the engine's rake is at {train.Dynamics.Distance:0.0}");
        Assert.Equal([5, 6, 0, 1, 2, 3], train.Dynamics.Consist.Vehicles.Select(v => v.Id));
        Assert.Equal(6_500, cut.Distance, 6);
    }

    [Theory]
    [InlineData("a car missing")]
    [InlineData("a car twice")]
    [InlineData("a car past the train")]
    [InlineData("a track the line hasn't")]
    [InlineData("nothing")]
    public void ASaveThatIsntThisTrainsChangesNothing(string wrong)
    {
        var train = Night();
        RakeState[] rakes = wrong switch
        {
            "a car missing" => [Rake([0, 1, 2, 3], 7_000), Rake([5, 6], 6_000)],
            "a car twice" => [Rake([0, 1, 2, 3, 4], 7_000), Rake([4, 5, 6], 6_000)],
            "a car past the train" => [Rake([0, 1, 2, 3, 4], 7_000), Rake([5, 6, 7], 6_000)],
            "a track the line hasn't" => [Rake([0, 1, 2, 3, 4], 7_000), Rake([5, 6], 6_000, path: 3)],
            _ => [],
        };
        Assert.False(train.Resume(rakes));
        Assert.Equal(2, train.Rakes.Count);
        Assert.Equal([0, 1, 2, 3, 4], train.Dynamics.Consist.Vehicles.Select(v => v.Id));
        Assert.Equal(5_000, train.Dynamics.Distance);
        Assert.True(train.StandingCar(5));
    }
}
