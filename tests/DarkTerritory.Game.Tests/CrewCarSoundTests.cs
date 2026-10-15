using Ballast;
using Ballast.Audio;
using DarkTerritory.Game.Sound;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// K1's crew car heard (queue #506, note 776): the kettle on the big cab's back wall singing as the gauge nears the redline
/// and quiet below it, and the cuckoo clock calling once each time its bird comes out before dawn.
/// </summary>
public class CrewCarSoundTests : IDisposable
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TrainTuning T = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
    static readonly BoilerTuning B = DataFile.Load<BoilerTuning>(Path.Combine(Content, BoilerTuning.File));
    static readonly PlayerTuning P = DataFile.Load<PlayerTuning>(Path.Combine(Content, PlayerTuning.File));
    static readonly CombatTuning C = DataFile.Load<CombatTuning>(Path.Combine(Content, CombatTuning.File));
    static readonly EnemyTuning E = DataFile.Load<EnemyTuning>(Path.Combine(Content, EnemyTuning.File));
    static readonly CrewCarLayout Layout = T.Geometry.Engine.CrewCar;

    readonly string _root = Path.Combine(Path.GetTempPath(), "dt-crewcar-" + Guid.NewGuid().ToString("N"));

    public void Dispose() => Directory.Delete(_root, recursive: true);

    GameAudio Audio()
    {
        Directory.CreateDirectory(Path.Combine(_root, "audio", "sounds"));
        File.Copy(Path.Combine(Content, MixDef.File), Path.Combine(_root, MixDef.File));
        File.Copy(Path.Combine(Content, "audio", "spaces.json"), Path.Combine(_root, "audio", "spaces.json"));
        var audio = new GameAudio(_root);
        foreach (var s in new[] { "crew-car.kettle~", "crew-car.cuckoo" })
            audio.Bank.Add(s.TrimEnd('~'), new SoundDef(3, [new LayerDef(SourceKind.Noise, 0.1)], Loop: s.EndsWith('~'), Duration: 0.1, MaxInstances: 64));
        return audio;
    }

    static Listener InTheCab(TrainOnLine train) => Listener.At(train.Frames[0].ToWorld(new Double3(0, 2.2, 4)), train.Frames[0].Heading);

    [Fact]
    public void TheKettleSingsNearTheRedlineAndIsQuietBelowIt()
    {
        var route = new Route("t", RouteTier.Frontier, 1, new LineDefinition("t", [new TrackSegment(3000)]), [], new RouteWeather(0.01, false, 0, 0), 3600);
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 4, 0.5)), route.Build(), 600, B);
        Assert.True(train.Frames[0].Shape.Cab is { } cab && cab.Max.Z - cab.Min.Z > 8, "the engine has the big cab");
        var world = new World(train, C);
        var audio = Audio();
        float? Singing()
        {
            for (int i = 0; i < 3; i++)
                audio.Update(world, world.Controls, InTheCab(train), exposed: false, SimConstants.TickSeconds);
            return audio.Mixer.Voices.FirstOrDefault(v => v.Name == "crew-car.kettle" && !v.Stopped && !v.Finished)?.Volume;
        }
        train.Boiler.Pressure = B.Redline - Layout.KettleFrom - 1;
        Assert.Null(Singing());
        train.Boiler.Pressure = B.Redline - Layout.KettleFrom / 2;
        var half = Singing();
        train.Boiler.Pressure = B.Redline;
        var full = Singing();
        Assert.NotNull(half);
        Assert.NotNull(full);
        Assert.True(full > half, $"louder at the redline ({full}) than halfway to it ({half})");
        train.Boiler.Pressure = B.WorkingBandMin;
        for (int i = 0; i < SimConstants.TickRate; i++)
            audio.Update(world, world.Controls, InTheCab(train), exposed: false, SimConstants.TickSeconds);
        Assert.DoesNotContain(audio.Mixer.Voices, v => v.Name == "crew-car.kettle" && !v.Stopped && !v.Finished);
    }

    [Fact]
    public void TheCuckooCallsOnceEachTimeItsBirdComesOutBeforeDawn()
    {
        var W = E.Wakers;
        var route = RouteGenerator.Generate(RouteTuning.Load(Content), RouteTier.Frontier, 7) with { DawnSeconds = W.StirSeconds + 20 };
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 4, 0.5)), route.Build(), 9_000, B);
        var world = new World(train, C);
        world.EnableRun(DataFile.Load<RunTuning>(Path.Combine(Content, RunTuning.File)), route, 600, authority: true);
        world.EnableEnemies(E with { Director = E.Director with { GraceMinSeconds = 1e9, GraceMaxSeconds = 1e9 } }, null, 1, crew: 1, authority: true);
        var player = PlayerMotor.SpawnInCab(train, P);
        var audio = Audio();
        var heard = new HashSet<int>();
        var calls = new List<double>();
        while (world.Run!.DawnIn > 0.5)
        {
            world.BeginTick();
            world.CrewAct(ref player, default, 1);
            world.Step(new TrainControls { Reverser = 1, Brake = 1 });
            world.StepRun([player]);
            audio.Update(world, world.Controls, InTheCab(train), exposed: false, SimConstants.TickSeconds);
            foreach (var v in audio.Mixer.Voices.Where(v => v.Name == "crew-car.cuckoo" && heard.Add(v.Id)))
                calls.Add(world.Run.DawnIn);
        }
        // Once on each minute of the last stirSeconds before dawn, and never before them.
        Assert.Equal((int)(W.StirSeconds / Layout.CuckooEvery), calls.Count);
        Assert.All(calls, at => Assert.True(at <= W.StirSeconds + 1e-6, $"called {at:0} s before dawn"));
    }
}
