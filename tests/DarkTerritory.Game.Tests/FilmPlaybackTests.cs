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
