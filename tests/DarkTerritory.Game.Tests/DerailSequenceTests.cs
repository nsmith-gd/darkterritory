using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// T121 playtest: "If we derail let people experience it first hand, then replay the moment from the third person train
/// view". The first beat's eye rides your car as the wreck throws it; the replay shows the train from a few seconds before
/// it came off, from the chase view.
/// </summary>
public class DerailSequenceTests
{
    static readonly WreckTuning T = new() { FirstPersonSeconds = 4, ReplaySeconds = 9, ReplayLeadSeconds = 3, CinematicSeconds = 8 };

    static CarFrame[] Train(double z, double roll = 0)
    {
        var shape = CarShape.Build(DataFile.Load<TrainTuning>(Path.Combine(DataFile.FindContentRoot(), TrainTuning.File)).Geometry, VehicleKind.Cargo, true);
        var up = new Double3(Math.Sin(roll), Math.Cos(roll), 0);
        var right = new Double3(Math.Cos(roll), -Math.Sin(roll), 0);
        return [.. Enumerable.Range(0, 3).Select(i => new CarFrame(i, new Double3(0, 0, z + i * 15), right, up, new Double3(0, 0, 1), default, shape))];
    }

    [Fact]
    public void TheBeatsComeInOrderAndTheRunsEndWaitsForAll()
    {
        Assert.Equal(DerailBeat.FirstPerson, DerailSequence.Beat(T, 0));
        Assert.Equal(DerailBeat.Replay, DerailSequence.Beat(T, 4.5));
        Assert.Equal(DerailBeat.Orbit, DerailSequence.Beat(T, 14));
        Assert.Equal(DerailBeat.None, DerailSequence.Beat(T, 21.5));
        Assert.Equal(1, DerailSequence.OrbitSeconds(T, 14), 6);
    }

    [Fact]
    public void YourEyeRidesYourCarAsItRollsOver()
    {
        var seq = new DerailSequence();
        // Standing in car 1 when it comes off (at z −15 then), looking forward.
        var eye = Camera.LookAt(new Double3(0.3, 1.7, -13), new Double3(0.3, 1.7, -23), 70);
        for (int i = 0; i < 60; i++)
            seq.Record(i / 30.0, Train(-i * 0.5), null, false, eye, 1, T);
        seq.Record(2, Train(-30), null, true, eye, 1, T);
        // A second on, the car's gone 20 m on and over onto its side.
        var rolled = Train(-50, Math.PI / 2);
        var cam = seq.FirstPerson(rolled);
        Assert.True((cam.Position - rolled[1].Origin).Length < 4, $"eye {cam.Position} not with car 1 at {rolled[1].Origin}");
        Assert.NotNull(cam.Orientation);
        // The horizon goes with it: the camera's up is the car's up, sideways now.
        var camUp = System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitY, cam.Orientation!.Value);
        Assert.True(Math.Abs(camUp.X) > 0.9, $"camera up {camUp}");
    }

    [Fact]
    public void TheReplayStartsBeforeItCameOffAndFollowsOnFromTheChaseView()
    {
        var seq = new DerailSequence();
        var eye = Camera.LookAt(new Double3(0, 1.7, 15), new Double3(0, 1.7, 5), 70);
        for (int i = 0; i <= 300; i++)
            seq.Record(i / 30.0, Train(-i * 0.5), null, false, eye, 1, T);
        double off = 10;
        for (int i = 1; i <= 13 * 30; i++)
            seq.Record(off + i / 30.0, Train(-150 - i * 0.1), null, true, eye, 1, T);
        var first = seq.ReplayAt(T.FirstPersonSeconds, T)!.Value;
        Assert.False(first.Off);
        // ReplayLeadSeconds before the derail: the train 45 m short of where it came off.
        Assert.InRange(first.Frames[0].Origin.Z, -150 + T.ReplayLeadSeconds * 15 - 1, -150 + T.ReplayLeadSeconds * 15 + 1);
        var later = seq.ReplayAt(T.FirstPersonSeconds + T.ReplayLeadSeconds + 2, T)!.Value;
        Assert.True(later.Off);
        var cam = seq.ReplayCamera(later.Frames, _ => false);
        Assert.True(cam.Position.Y > 5, "the replay's wide is up over the train");
        // Note 579 ("show the derailment happening in slow motion ... cars basically being blasted off the tracks"): off, it
        // cuts in from the chase view to a wide side on to the front of the train, close enough to see the cars go up.
        var front = later.Frames[0].Origin;
        double across = Math.Abs(Double3.Dot(cam.Position - front, later.Frames[0].Right));
        Assert.InRange((cam.Position - front).Length, 15, 50);
        Assert.True(across > 20, $"the wide's {across:0} m out from the line, not side on");
        Assert.True(Off(cam, front) < 30, "the front of the train's out of the shot");
        // Kept off a hillside.
        var hill = seq.ReplayCamera(later.Frames, _ => false, ground: (_, _) => 40);
        Assert.True(hill.Position.Y >= 43 - 1e-9);
    }

    static double Off(Camera c, Double3 p)
    {
        var to = p - c.Position;
        var dir = System.Numerics.Vector3.Normalize(new System.Numerics.Vector3((float)to.X, (float)to.Y, (float)to.Z));
        return Math.Acos(Math.Clamp(System.Numerics.Vector3.Dot(dir, c.Forward), -1, 1)) * 180 / Math.PI;
    }
}
