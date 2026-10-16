using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// Note 838 (the director's 16 Oct playtest, line 21: "Where are the crates that I loaded up? In car one, they're no longer
/// here. What happened? ... That feels really bad"): a casting the Foundry's crane loaded into car 1 is the stop's crane's, and
/// the scene drew a crane's castings only within its draw distance of the crane. Once the train had left the stop, the casting
/// in car 1 (loaded still, paid for at the gates) wasn't drawn. What a crane loaded rides on with the train, drawn with it.
/// </summary>
public class CastingsRideOnTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly Look Look = Look.Load(Content);

    /// <summary>A casting lashed in car 1, and how much is drawn round it, the train <paramref name="beyond"/> m past the stop's crane.</summary>
    static int DrawnAtTheCasting(double beyond, bool art)
    {
        var (route, facility) = Sim.Bots.FacilityWork.Find(Sim.Route.RouteTuning.Load(Content), Sim.Route.FacilityKind.Foundry)!.Value;
        var s = new PrototypeSession(Content, route, 4, enemies: false);
        var site = s.World.Run!.Sites[facility]!;
        var crane = Assert.IsType<Crane>(site.Crane);
        var state = s.Train.Capture();
        s.Train.Restore(state with { Rakes = [state.Rakes[0] with { Path = Sim.Rail.RailLine.MainPath, Distance = site.MainDistance + beyond, Velocity = 0 }] });
        s.Train.RefreshFrames();
        var casting = crane.Castings[0];
        double floor = s.TrainTuning.Geometry.Interior!.FloorHeight;
        (casting.State, casting.Car, casting.At) = (CastingState.Loaded, 1, new Double3(0, floor, 0));
        var frame = s.Train.Frames[1];
        var at = frame.ToWorld(casting.At);
        var eye = frame.ToWorld(new Double3(0, floor + 1.6, -4));
        var mesh = new MeshBuilder();
        new GreyboxScene { Look = art ? Look : null, Time = 0.37, Run = s.World.Run, Route = s.World.Run.Route }.Build(mesh, s.Train, eye);
        var centre = (at + frame.Up * 0.4).RelativeTo(eye);
        return mesh.Flattened().Count(v => Vector3.Distance(v.Position, centre) < 0.6f);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ACastingLoadedIntoACarIsStillDrawnThereOnceTheTrainHasLeftTheStop(bool art)
    {
        int drawn = DrawnAtTheCasting(2_000, art);
        Assert.True(drawn > 0, $"nothing drawn in car 1 where the casting lies, 2 km past its crane (art {art})");
    }
}
