using Ballast;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// GDD v1.4 App. E.4 and E.5 on screen (note 176): the film's cars and crew where its recording has them, the car a subject
/// is tumbling inside cut away for their shot (O2), and a light rig on them (O12).
/// </summary>
public class FilmPlaybackTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TrainTuning Train = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
    static readonly PlayerTuning P = DataFile.Load<PlayerTuning>(Path.Combine(Content, PlayerTuning.File));
    static readonly WreckTuning W = DataFile.Load<WreckTuning>(Path.Combine(Content, WreckTuning.File));

    internal static (World World, WreckFilm Film) Shot()
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(2000), new TrackSegment(600, 1 / 300.0), new TrackSegment(2000)]));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Train, 5, 1)), line, 2250);
        train.Dynamics.Velocity = 18;
        train.RefreshFrames();
        var world = new World(train) { WreckTuning = W };
        world.EnableBodies();
        world.BeginTick();
        // One in car 2, one on car 4's roof.
        var room = train.Frames[2].Shape.Interior!.Value;
        var inside = new PlayerState { Parent = 2, Position = new Double3(0, room.Min.Y + 0.05, 0), Surface = Surface.Deck, Health = P.Health };
        var roof = PlayerMotor.SpawnOnRoof(train, 4, 0, P);
        world.CrewAct(ref inside, default, 0);
        world.CrewAct(ref roof, default, 1);
        world.Derail("test");
        return (world, world.ShootFilm()!);
    }

    [Fact]
    public void TheFilmsCarsAndCrewAreWhereItsRecordingHasThem()
    {
        var (world, film) = Shot();
        double at = film.Recorded * 0.6;
        var frames = DerailSequence.FilmFrames(film, at, world.Train.Frames);
        var (a, b, u) = film.At(at);
        for (int i = 0; i < film.Start.Cars.Count; i++)
            Assert.True((frames[film.Start.Cars[i].Vehicle].Origin - Double3.Lerp(a.Cars[i].Origin, b.Cars[i].Origin, u)).Length < 1e-9);
        var bodies = DerailSequence.FilmBodies(film, at);
        Assert.Equal(film.Start.Players.Select(p => p.Id), bodies.Select(x => x.Owner));
        Assert.All(bodies, x => Assert.Equal(Sim.Physics.BodyKind.Ragdoll, x.Kind));
        Assert.All(bodies, x => Assert.Equal(11, x.Pbd.Particles.Length));
    }

    [Fact]
    public void TheWholeSequenceRunsFirstPersonReplayThenTheCutShotByShot()
    {
        // `dt film` (note 232) renders frame by frame along this: the beats back to back, the film's shots in its order.
        var (_, film) = Shot();
        var beats = DerailSequence.Timeline(W, film);
        Assert.Equal([DerailBeat.FirstPerson, DerailBeat.Replay], beats.Take(2).Select(b => b.Beat));
        Assert.Equal(film.Cut, beats.Skip(2).Select(b => b.Shot));
        Assert.Equal(0, beats[0].From);
        for (int i = 1; i < beats.Count; i++)
            Assert.Equal(beats[i - 1].To, beats[i].From, 9);
        Assert.Equal(DerailSequence.Length(W, film), beats[^1].To, 9);
        Assert.Equal(ShotKind.Cause, beats[^1].Shot!.Kind);
        // Sampled at a low frame rate, every frame's beat is the timeline's, and the frame count is the length's.
        const int fps = 4;
        int frames = (int)Math.Ceiling(DerailSequence.Length(W, film) * fps - 1e-9);
        Assert.Equal((int)Math.Ceiling((W.FirstPersonSeconds + W.ReplaySeconds + film.CutLength) * fps - 1e-9), frames);
        for (int f = 0; f < frames; f++)
        {
            double at = f / (double)fps;
            var beat = beats.Last(b => b.From <= at + 1e-9);
            Assert.Equal(beat.Beat, DerailSequence.Beat(W, at, film));
            if (beat.Shot is { } shot)
                Assert.Same(shot, film.CutAt(DerailSequence.FilmSeconds(W, at))!.Value.Shot);
        }
        Assert.Equal(DerailBeat.None, DerailSequence.Beat(W, frames / (double)fps, film));
        // Without a film the orbit stands in for the cut.
        Assert.Equal(DerailBeat.Orbit, DerailSequence.Timeline(W, null)[^1].Beat);
    }

    [Fact]
    public void ACrewmateInsideStandsOnTheFloorAndARoofRiderIsNotFlyingTillThrown()
    {
        // Note 232: inside, the floor was the box's foot, so they fell through to the rails; and the peak read the pelvis
        // over the ground, so a roof rider was "in the air" from the first frame.
        var (world, film) = Shot();
        double Ground(double x, double z)
        {
            double hint = film.Start.Along;
            return PlayerMotor.GroundAt(new Double3(x, 0, z), world.Train.Line, ref hint);
        }
        int inside = film.Start.Players.ToList().FindIndex(p => p.Inside == 2);
        var car = film.Start.Cars.First(c => c.Vehicle == 2);
        Assert.True(car.Floor > 0.5, $"car 2's floor at {car.Floor}");
        for (int f = 0; f < 6; f++)
        {
            var (o, _, up, _) = film.Frames[f].Cars[film.Start.Cars.ToList().IndexOf(car)];
            Assert.All(film.Frames[f].Ragdolls[inside], j => Assert.True(Double3.Dot(j - o, up) > car.Floor - 0.05));
        }
        int roof = film.Start.Players.ToList().FindIndex(p => p.Inside < 0);
        Assert.True(film.Clearance(0, roof, Ground) < WreckFilm.AirborneAbove, "stood on the roof isn't in the air");
        Assert.True(film.Clearance(0, inside, Ground) < WreckFilm.AirborneAbove, "stood on the floor isn't in the air");
    }

    [Fact]
    public void ASubjectInsideACarIsSeenThroughItAndLit()
    {
        var (world, film) = Shot();
        var shot = film.Cut.First(s => s.Kind == ShotKind.Player && s.Subject == 0);
        double at = shot.At(shot.Real / 2);
        var frames = DerailSequence.FilmFrames(film, at, world.Train.Frames);
        var camera = DerailSequence.FilmCamera(shot, shot.Real / 2);
        Assert.Contains(2, DerailSequence.FilmCutAway(film, shot, at, frames, camera.Position));
        // The wide shots cut nothing away.
        var settle = film.Cut.First(s => s.Kind == ShotKind.Settle);
        Assert.Empty(DerailSequence.FilmCutAway(film, settle, settle.From, frames, settle.Camera));
        // O12: a key and a rim on the subject.
        Assert.Equal(2, DerailSequence.FilmLights(film, shot, at, camera.Position).Count);
    }
}
