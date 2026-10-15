using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The dead's views (GDD App. D.10; the director, 14 Oct 2026: "a third and fourth camera option ... a third person wide shot
/// of the train, and ... a third person above and behind a little bit of ... a character. We should also be able to view
/// other players' or bots' dead bodies"; note 743): the wide has the whole train in frame from beside the line; over the
/// shoulder is above and behind the crewmate, inside the car's room when they're in one; a body's seen from over it.
/// </summary>
public class DeadViewTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TrainTuning T = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));

    static TrainOnLine Train(int cars) =>
        new(new TrainDynamics(Consist.Uniform(T, cars, 1)), new RailLine(new LineDefinition("t", [new TrackSegment(5_000)])), 2_000);

    /// <summary>Degrees off the shot's middle, across (yaw) and up and down (pitch), that a point is.</summary>
    static (double Across, double Up) Off(Camera c, Double3 p)
    {
        var to = p - c.Position;
        var d = new Vector3((float)to.X, (float)to.Y, (float)to.Z);
        var f = c.Forward;
        var right = Vector3.Normalize(Vector3.Cross(f, Vector3.UnitY));
        var up = Vector3.Cross(right, f);
        double x = Vector3.Dot(d, right), y = Vector3.Dot(d, up), z = Vector3.Dot(d, f);
        return (Math.Atan2(Math.Abs(x), z) * 180 / Math.PI, Math.Atan2(Math.Abs(y), z) * 180 / Math.PI);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(8)]
    [InlineData(14)]
    public void TheWideHasTheWholeTrainInFrameFromBesideTheLine(int cars)
    {
        var train = Train(cars);
        var frames = train.Frames;
        var wide = DeadViews.Wide(frames, (_, _) => 0);
        // A 16:9 frame at the view's height: half the height up and down, a little under twice that across.
        double halfUp = DeadViews.Fov / 2, halfAcross = Math.Atan(Math.Tan(halfUp * Math.PI / 180) * 16 / 9) * 180 / Math.PI;
        foreach (var f in frames)
            foreach (double z in new[] { -f.Shape.HalfLength, f.Shape.HalfLength })
            {
                var (across, up) = Off(wide, f.ToWorld(new Double3(0, 1, z)));
                Assert.True(across < halfAcross - 2 && up < halfUp, $"car {f.Index}'s end at {z:0}: {across:0}° across, {up:0}° up, out of the frame");
            }
        // Out beside the line, not up the track: well off to the side of the train's middle, and above it.
        var middle = frames[frames.Count / 2];
        var side = middle.ToLocal(wide.Position);
        Assert.True(Math.Abs(side.X) >= 25, $"the camera's only {Math.Abs(side.X):0} m off the line");
        Assert.True(side.Y > 5, "the camera's down at the rails");
        Assert.True(wide.Far >= 2000);
    }

    [Fact]
    public void OverTheShoulderIsAboveAndBehindTheCrewmate()
    {
        var feet = new Double3(4, 0, 30);
        foreach (double yaw in new[] { 0.0, 1.2, Math.PI, -2.0 })
        {
            var shot = DeadViews.Shoulder(feet, yaw, ground: (_, _) => 0);
            var forward = new Double3(-Math.Sin(yaw), 0, -Math.Cos(yaw));
            var rel = shot.Position - feet;
            Assert.True(Double3.Dot(rel, forward) < -1.5, $"facing {yaw:0.0}: the camera isn't behind them");
            Assert.True(rel.Y > 2, $"facing {yaw:0.0}: the camera isn't above them");
            Assert.True(Off(shot, feet + Double3.Up * 0.9).Up < DeadViews.Fov / 2, $"facing {yaw:0.0}: they're out of the shot");
            // And looking the way they face.
            Assert.True(Double3.Dot(new Double3(shot.Forward.X, 0, shot.Forward.Z), forward) > 0.8);
        }
    }

    [Fact]
    public void OverTheShoulderInACarStaysInItsRoom()
    {
        var train = Train(3);
        var car = train.Frames.Last(f => f.Shape.Interior is not null);
        var room = car.Shape.Interior!.Value;
        foreach (double z in new[] { -car.Shape.HalfLength + 1.2, 0.0, car.Shape.HalfLength - 1.2 })
            foreach (double yaw in new[] { 0.0, Math.PI / 2, Math.PI, -Math.PI / 2 })
            {
                var feet = car.ToWorld(new Double3(0.2, room.Min.Y, z));
                var shot = DeadViews.Shoulder(feet, yaw, car, (_, _) => 0);
                var local = car.ToLocal(shot.Position);
                Assert.True(room.Contains(local), $"at {z:0.0} facing {yaw:0.0}: the camera's at {local}, outside the room {room.Min}..{room.Max}");
                Assert.True(Off(shot, feet + Double3.Up * 0.9).Up < DeadViews.Fov / 2, $"at {z:0.0} facing {yaw:0.0}: they're out of the shot");
            }
    }

    [Fact]
    public void ABodyIsSeenFromOverItWhereItLies()
    {
        var body = new Double3(-30, 0.3, 120);
        var shot = DeadViews.OverBody(body, new Double3(0, 0, -1), ground: (_, _) => 0);
        Assert.True(shot.Position.Y > body.Y + 2, "the camera isn't over the body");
        var (across, up) = Off(shot, body);
        Assert.True(across < 1 && up < 1, "the body isn't in the middle of the shot");
    }

    [Fact]
    public void TheViewKeyGoesRoundAllFour()
    {
        var seen = new List<DeadView>();
        var v = DeadView.Eyes;
        for (int i = 0; i < 4; i++)
        {
            seen.Add(v);
            v = DeadViews.Next(v);
        }
        Assert.Equal(DeadView.Eyes, v);
        Assert.Equal([DeadView.Eyes, DeadView.Chase, DeadView.Wide, DeadView.Shoulder], seen);
    }
}
