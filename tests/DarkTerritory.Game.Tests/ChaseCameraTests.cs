using Ballast;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The chase view and the derailment's replay frame the train itself (the audit's playthrough found them 17 km off): a
/// switchyard's standing cars sit at the end of the train's frames (note 187), and "the last car" was one of them.
/// </summary>
public class ChaseCameraTests
{
    static readonly string Content = DataFile.FindContentRoot();

    [Fact]
    public void TheChaseViewIsOnTheTrainNotASwitchyardsStandingCars()
    {
        // frontier:7 has a switchyard: its standing cars are frames past the train's own.
        var route = Sim.LineGen.Routes.Generate(Content, "frontier:7", 6);
        var session = new PrototypeSession(Content, route, cars: 6, enemies: false);
        var train = session.Train;
        Assert.True(train.Frames.Count > train.OwnVehicles, "no standing cars on frontier:7 to test against");
        var engine = train.Frames[0].Origin;
        Assert.True((Views.Get("chase", train).Position - engine).Length < 250, "the chase view isn't on the train");
        // The replay frames what the train's frames are, standing cars and all, without them.
        var sequence = new DerailSequence();
        var shot = sequence.ReplayCamera([.. train.Frames], train.StandingCar);
        Assert.True((shot.Position - engine).Length < 250, "the replay isn't on the train");
    }
}
