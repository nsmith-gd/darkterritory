using Ballast;
using Ballast.Audio;
using DarkTerritory.Game.Sound;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The sounds of how a night can go wrong that main's GDD v1.4 work left silent (ARCHITECTURE §8 note 215): a powder
/// blast of its own, and the Stranded outro's cooling boiler and lamps going out (App. E.9).
/// </summary>
public class EndingSoundTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TrainTuning Trains = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));

    static TrainOnLine Train(int cars = 5)
    {
        var line = RailLine.Load(Path.Combine(Content, "lines", "test-loop.json"));
        return new TrainOnLine(new TrainDynamics(Consist.Uniform(Trains, cars, 1)), line, 1200);
    }

    [Fact]
    public void APowderBlastIsItsOwnAndABallsImpactIsABalls()
    {
        // World.Blast (a keg or a powder car going up) is nobody's shot; a cannonball's impact has its shooter.
        var train = Train();
        var world = new World(train);
        var audio = new GameAudio(Content);
        var ear = Listener.At(train.Frames[0].Origin, train.Frames[0].Heading);
        void Update() => audio.Update(world, new TrainControls { Reverser = 1 }, ear, exposed: true, SimConstants.TickSeconds);
        int Playing(string name) => audio.Mixer.Voices.Count(v => v.Name == name);
        Update();
        var at = train.Frames[2].Origin;
        world.Impacts.Add(new CannonImpact(1, 0, at, Double3.Up, ImpactSurface.Train, -1));
        Update();
        Assert.Equal(1, Playing("powder-blast"));
        Assert.Equal(0, Playing("cannon-impact"));
        world.Impacts.Add(new CannonImpact(2, 0, at, Double3.Up, ImpactSurface.Ground, 1));
        Update();
        Assert.Equal(1, Playing("cannon-impact"));
        Assert.Equal(1, Playing("powder-blast"));
    }

    [Fact]
    public void StrandedTheBoilerTicksAsItCoolsAndTheLampsGoOutLastCarFirst()
    {
        // GDD v1.4 App. E.9: "wind and the boiler ticking as it cools. No music"; the lamps go out one by one, the last car
        // first and the engine last, as the picture has them (Views.StrandedLampsOut).
        var train = Train(cars: 5);
        var audio = new GameAudio(Content);
        var t = new StrandedOutroTuning();
        var lamps = new List<Double3>();
        var seen = new HashSet<int>();
        for (double s = 0; s <= t.Seconds + 0.5; s += SimConstants.TickSeconds)
        {
            audio.Stranded(train, t, s);
            foreach (var v in audio.Mixer.Voices.Where(v => v.Name == "lamp-out" && seen.Add(v.Id)))
                lamps.Add(v.Position);
            if (s > 0.5)
                Assert.Contains(audio.Mixer.Voices, v => v.Name == "boiler-tick" && !v.Finished);
        }
        Assert.DoesNotContain(audio.Mixer.Voices, v => v.Name.StartsWith("music", StringComparison.Ordinal));
        Assert.Equal(train.Frames.Count, lamps.Count);
        // Last car first, the engine last.
        Assert.True((lamps[0] - train.Frames[^1].Origin).Length < (lamps[0] - train.Frames[0].Origin).Length);
        Assert.True((lamps[^1] - train.Frames[0].Origin).Length < 10);
        // Over: the ticking stops.
        audio.Stranded(train, t, -1);
        Assert.DoesNotContain(audio.Mixer.Voices, v => v.Name == "boiler-tick" && !v.Finished);
    }
}
