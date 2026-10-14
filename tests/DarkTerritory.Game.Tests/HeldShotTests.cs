using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// Held (note 580; the director, 9 Oct 2026: "When you are being held by something the camera should cut to third person and
/// show the thing holding you"): the shot frames you and what has you, from your back's side, off the ground, and in the
/// car's room when you're inside one.
/// </summary>
public class HeldShotTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TrainTuning T = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));

    static Camera Eyes(Double3 feet, double yaw) =>
        new() { Position = feet + Double3.Up * Game.Eyes.Height, Yaw = yaw, FovYDegrees = 75, Near = 0.05f, Far = 2000 };

    /// <summary>Degrees off the shot's middle that a point is.</summary>
    static double Off(Camera c, Double3 p)
    {
        var to = p - c.Position;
        var dir = Vector3.Normalize(new Vector3((float)to.X, (float)to.Y, (float)to.Z));
        return Math.Acos(Math.Clamp(Vector3.Dot(dir, c.Forward), -1, 1)) * 180 / Math.PI;
    }

    [Theory]
    [InlineData(3.0, 0.0, 1.0)]
    [InlineData(0.0, 5.0, 2.2)]
    [InlineData(-6.0, 2.0, 3.5)]
    public void BothYouAndWhatHasYouAreInTheShotOverYourShoulder(double hx, double hz, double size)
    {
        var you = new Double3(10, 0, 20);
        var holder = you + new Double3(hx, 0, hz);
        var eyes = Eyes(you, yaw: 0);
        var shot = HeldShot.Frame(you, holder, eyes, size: size, ground: (_, _) => 0);
        // Within the shot's height of its middle, both of you (its half-width is wider still).
        Assert.True(Off(shot, you + Double3.Up * 0.9) < HeldShot.Fov / 2, $"you're {Off(shot, you + Double3.Up * 0.9):0}° off the middle");
        Assert.True(Off(shot, holder + Double3.Up * 0.9) < HeldShot.Fov / 2, $"it's {Off(shot, holder + Double3.Up * 0.9):0}° off the middle");
        // Out of you, over the ground, and on your side of it: over your shoulder at it, not over its at you.
        Assert.True((shot.Position - you).Length > 2.5, "the camera's on top of you");
        Assert.True(shot.Position.Y >= 1.2, "the camera's in the ground");
        Assert.True((shot.Position - you).Length < (shot.Position - holder).Length, "the camera's nearer it than you");
    }

    [Fact]
    public void PinnedWhereYouStandItStillLooksAtYou()
    {
        var you = new Double3(0, 0, 0);
        var shot = HeldShot.Frame(you, you, Eyes(you, yaw: 1.0), size: HeldShot.Size(EnemyKind.Moose), ground: (_, _) => 0);
        Assert.True(Off(shot, you + Double3.Up * 0.9) < 5);
        Assert.True((shot.Position - you).Length > 2.5);
    }

    [Fact]
    public void InsideACarTheCameraStaysInItsRoom()
    {
        // The Car Hugger has you at the rear car's end door: the shot is taken from inside the car, never through its wall.
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(5_000)]));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 3, 1)), line, 2_000);
        var rear = train.Frames.Last(f => f.Shape.Interior is not null);
        var room = rear.Shape.Interior!.Value;
        var you = rear.ToWorld(new Double3(0.3, room.Min.Y, rear.Shape.HalfLength - 1.4));
        var mouth = rear.ToWorld(new Double3(0, room.Min.Y, rear.Shape.HalfLength + 0.8));
        foreach (double yaw in new[] { 0.0, Math.PI / 2, Math.PI, -Math.PI / 2 })
        {
            var shot = HeldShot.Frame(you, mouth, Eyes(you, yaw), rear, HeldShot.Size(EnemyKind.CarHugger));
            var local = rear.ToLocal(shot.Position);
            Assert.True(room.Contains(local), $"facing {yaw:0.0}: the camera's at {local}, outside the room {room.Min}..{room.Max}");
            Assert.True(Off(shot, you + Double3.Up * 0.9) < HeldShot.Fov / 2, $"facing {yaw:0.0}: you're out of the shot");
        }
    }
}
