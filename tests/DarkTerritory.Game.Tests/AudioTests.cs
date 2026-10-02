using Ballast;
using Ballast.Audio;
using DarkTerritory.Game.Sound;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>Spec A: audio is the primary tell channel, so its guarantees are tests, rendered offline.</summary>
public class AudioTests
{
    static readonly string Content = DataFile.FindContentRoot();

    [Theory]
    [InlineData("chaos")]
    [InlineData("tells")]
    public void EveryTellCutsThroughTheBedForWhoeverHasToHearIt(string scenario)
    {
        // Spec A.3: "Tier 1 is inviolable... the agent harness should test it by generating maximum-chaos
        // states and verifying tell audibility." Chaos: 20 cars at 22 m/s, regulator and brake surging, the
        // valve lifting, the guard gun firing, the Choir in full swarm, and every demo tell at once.
        var sweep = AudioBench.Sweep(Content, scenario, cars: 20, speed: 22, seconds: 4);
        Assert.Equal(AudioBench.TellBands.Count, sweep.Audit.Count);
        Assert.All(sweep.Audit, a => Assert.True(a.WorstOverBedDb >= 6, $"{a.Sound} is only {a.WorstOverBedDb} dB over the bed at the {a.WorstListener}"));
    }

    [Fact]
    public void EverySoundFileLoads()
    {
        var bank = new SoundBank(Path.Combine(Content, "audio", "sounds"));
        Assert.Null(bank.LastError);
        foreach (var name in AudioBench.TellBands.Keys.Concat(["boiler-roar", "chuff", "wheel-rail", "brake", "wind", "slack-clunk", "safety-valve", "gunshot", "shovel"]))
            Assert.NotNull(bank.Get(name));
        Assert.All(AudioBench.TellBands.Keys, t => Assert.Equal(1, bank.Get(t)!.Tier));
    }

    [Fact]
    public void OneSoundRendersAloneToItsEnd()
    {
        // `dt audio render --sound`: a one-shot plays to its end and a tenth of a second on, and is heard.
        var (report, mix) = AudioBench.RenderSound(Content, "gunshot");
        Assert.NotNull(report.EndedAt);
        Assert.InRange(report.Seconds, report.EndedAt.Value + 0.1, report.EndedAt.Value + 0.11);
        Assert.Equal(report.Seconds, mix.Length / 2.0 / Audio.SampleRate, 3);
        Assert.True(report.MixDb > -40);
    }

    [Fact]
    public void EveryEnemyTellIsSentAsFarAsItCanBeHeard()
    {
        // An enemy past the interest radius isn't on your machine, so its tell can't play there. The Choir's
        // voice is the exception: it comes from the world record, which always goes. So is the Long Whistle's horn: the
        // enemy is sent to everyone wherever it is (Enemy.Far, T57).
        var bank = new SoundBank(Path.Combine(Content, "audio", "sounds"));
        var enemies = DataFile.Load<DarkTerritory.Sim.Enemies.EnemyTuning>(Path.Combine(Content, DarkTerritory.Sim.Enemies.EnemyTuning.File));
        Assert.All(AudioBench.TellBands.Keys.Where(t => t is not ("choir-voice" or "train-whistle")),
            t => Assert.True(bank.Get(t)!.MaxDistance <= enemies.InterestRadius, $"{t} carries {bank.Get(t)!.MaxDistance} m, past the {enemies.InterestRadius} m interest radius"));
    }

    [Fact]
    public void TheBedIsDrivenBySpeed()
    {
        double Mix(double speed) => AudioBench.Render(Content, "bed", cars: 6, speed: speed, listenerCar: 3, seconds: 2).Report.MixDb;
        Assert.True(Mix(22) > Mix(2) + 6);
    }

    [Fact]
    public void SlackActionRunsDownTheTrainOneCouplingAtATime()
    {
        // Spec A.2: a change in pull "produces a clunk that travels the length of the train".
        var tuning = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
        var line = RailLine.Load(Path.Combine(Content, "lines", "test-loop.json"));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(tuning, 8, 1)), line, 1200);
        var world = new World(train);
        var audio = new GameAudio(Content);
        var controls = new TrainControls { Reverser = 1 };
        var ear = Listener.At(train.Frames[4].Origin, train.Frames[4].Heading);
        var clunks = new List<(double At, Double3 Where)>();
        var seen = new HashSet<int>();
        for (int tick = 0; tick < SimConstants.TickRate * 3; tick++)
        {
            controls.Throttle = tick >= 15 ? 1 : 0;
            world.BeginTick();
            world.Step(controls);
            audio.Update(world, controls, ear, exposed: true, SimConstants.TickSeconds);
            foreach (var v in audio.Mixer.Voices.Where(v => v.Name == "slack-clunk" && seen.Add(v.Id)))
                clunks.Add((tick * SimConstants.TickSeconds, v.Position));
        }
        Assert.Equal(train.Vehicles.Count - 1, clunks.Count);
        // Front to back, in order.
        var along = clunks.Select(c => GreyboxScene.NearestDistance(line, c.Where, 1200)).ToList();
        Assert.Equal(along.OrderByDescending(s => s), along);
        Assert.True(clunks[^1].At > clunks[0].At + 0.5);
    }
}
