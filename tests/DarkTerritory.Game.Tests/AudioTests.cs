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
    public void AWreckIsHeardCrashingAndGrinding()
    {
        // T117: "loud, spectacular" (GDD §23). The train comes off half a second in; beside the line you hear the cars
        // hit (wreck-crash) and the steel dragged through the earth (wreck-grind), each well over the train's own sounds.
        var (report, _) = AudioBench.Render(Content, "wreck", cars: 8, speed: 22, listenerCar: 3, seconds: 6);
        Assert.True(report.StemsDb.GetValueOrDefault("wreck-crash", double.NegativeInfinity) > -30, $"crash at {report.StemsDb.GetValueOrDefault("wreck-crash")} dB");
        Assert.True(report.StemsDb.GetValueOrDefault("wreck-grind", double.NegativeInfinity) > -30, $"grind at {report.StemsDb.GetValueOrDefault("wreck-grind")} dB");
        Assert.True(report.StemsDb["wreck-grind"] > report.StemsDb.GetValueOrDefault("wheel-rail", double.NegativeInfinity) + 10);
    }

    [Fact]
    public void TheOperaHitsOnTheReplayDucksUnderTheLaughingAndFadesOut()
    {
        // GDD v1.4 App. E.6 through the real mixer: the whole derailment sequence (note 170), its draw from a fresh bag, and
        // someone laughing on the dead channel over the replay.
        var tuning = DataFile.Load<WreckTuning>(Path.Combine(Content, WreckTuning.File));
        var (report, mix) = AudioBench.Render(Content, "wreck", cars: 8, speed: 22, listenerCar: 3, seconds: tuning.SequenceSeconds + 1);
        var music = report.Music;
        Assert.NotNull(music);
        // At 22 m/s (over 16) the draw favours Gallop and Doom; any track may come, but one did, and on the replay's first frame.
        Assert.InRange(music.StartedAt, music.HitDueAt - tuning.ReplayLeadSeconds - 0.05, music.HitDueAt - tuning.ReplayLeadSeconds + 0.05);
        Assert.InRange(music.HitHeardAt, music.HitDueAt - 0.1, music.HitDueAt + 0.1);
        // Ducked 6 dB (E.10's −6, 50 ms attack) while the dead channel laughs, the game under the 1.2 kHz low-pass meanwhile.
        Assert.InRange(music.DuckDb, -6.1, -5.5);
        Assert.Equal(1, music.GameLowpass, 2);
        // Heard: the opera over the wreck, and the laughing over the opera.
        Assert.True(music.MusicDb > report.StemsDb["wreck-grind"], $"music {music.MusicDb} dB, grind {report.StemsDb["wreck-grind"]} dB");
        Assert.True(report.StemsDb["voice-dead"] > -40);
        // Faded to nothing by the end of the sequence.
        int end = (int)(music.FadedBy * Audio.SampleRate) * 2;
        Assert.True(Meter.Db(mix.AsSpan(end - 4800, 4800)) < Meter.Db(mix.AsSpan(end - 2 * Audio.SampleRate * 2, 4800)) - 12);
    }

    [Fact]
    public void EverySoundFileLoads()
    {
        var bank = new SoundBank(Path.Combine(Content, "audio", "sounds"));
        Assert.Null(bank.LastError);
        foreach (var name in AudioBench.TellBands.Keys.Concat(["boiler-roar", "chuff", "wheel-rail", "brake", "wind", "slack-clunk", "safety-valve", "gunshot", "shovel",
            "cannon-impact", "cannon-splash", "hit-confirm", "doll-shatter", "toy-squeaker", "toy-musicbox", "toy-drummer"]))
            Assert.NotNull(bank.Get(name));
        Assert.All(AudioBench.TellBands.Keys, t => Assert.Equal(1, bank.Get(t)!.Tier));
    }

    [Fact]
    public void WhatLandsIsHeardWhereItLandsOnce()
    {
        // T121: a ball's boom where it came down (a splash in water, the porcelain going for the doll), and a blow's thud on
        // the creature, each once, at the point of it; what was already there when the client first looked is old news.
        var tuning = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
        var line = RailLine.Load(Path.Combine(Content, "lines", "test-loop.json"));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(tuning, 4, 1)), line, 1200);
        var world = new World(train);
        var audio = new GameAudio(Content);
        var ear = Listener.At(train.Frames[0].Origin, train.Frames[0].Heading);
        var old = new DarkTerritory.Sim.Combat.CannonImpact(1, 0, train.Frames[0].Origin, Double3.Up, DarkTerritory.Sim.Combat.ImpactSurface.Ground, 1);
        world.Impacts.Add(old);
        void Update() => audio.Update(world, new TrainControls { Reverser = 1 }, ear, exposed: true, SimConstants.TickSeconds);
        int Playing(string name) => audio.Mixer.Voices.Count(v => v.Name == name);
        Update();
        Assert.Equal(0, Playing("cannon-impact"));
        var ahead = train.Frames[0].ToWorld(new Double3(0, 0, -60));
        world.Impacts.Add(old with { Id = 2, At = ahead, Surface = DarkTerritory.Sim.Combat.ImpactSurface.Creature, Struck = DarkTerritory.Sim.Enemies.EnemyKind.TrackDoll });
        world.Impacts.Add(old with { Id = 3, At = ahead, Surface = DarkTerritory.Sim.Combat.ImpactSurface.Water });
        world.Hits.Add(new DarkTerritory.Sim.Combat.HitConfirm(4, 0, 9, DarkTerritory.Sim.Enemies.EnemyKind.Ribbit, 1, DarkTerritory.Sim.Combat.HitSource.Melee, ahead, Double3.Up, false));
        Update();
        Assert.Equal(1, Playing("cannon-impact"));
        Assert.Equal(1, Playing("doll-shatter"));
        Assert.Equal(1, Playing("cannon-splash"));
        Assert.Equal(1, Playing("hit-confirm"));
        Assert.All(audio.Mixer.Voices.Where(v => v.Name is "cannon-impact" or "hit-confirm"), v => Assert.Equal(ahead, v.Position));
        // Still on the wire next update: not played again.
        Update();
        Assert.Equal(1, Playing("cannon-impact"));
        Assert.Equal(1, Playing("hit-confirm"));
    }

    [Fact]
    public void ANoisyToyPlaysWhileItsCarriedAndStopsWhenPutDown()
    {
        // GDD v1.4 App. C item 4 (note 175): a squeaker, a music box, a wind-up drummer, heard while carried; a quiet toy isn't.
        var tuning = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
        var line = RailLine.Load(Path.Combine(Content, "lines", "test-loop.json"));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(tuning, 4, 1)), line, 1200);
        var world = new World(train);
        var room = train.Frames[2].Shape.Interior!.Value;
        var toys = new[] { DarkTerritory.Sim.Physics.ToyNoise.Squeaker, DarkTerritory.Sim.Physics.ToyNoise.MusicBox, DarkTerritory.Sim.Physics.ToyNoise.Drummer,
            DarkTerritory.Sim.Physics.ToyNoise.None }.Select((noise, i) =>
            {
                var b = world.Bodies.SpawnCrate(train, 2, new Double3(0, room.Min.Y + 0.1, i * 0.5), DarkTerritory.Sim.Physics.BodyKind.Toy);
                b.Noise = noise;
                return b;
            }).ToList();
        var audio = new GameAudio(Content);
        var ear = Listener.At(train.Frames[2].Origin, train.Frames[2].Heading);
        void Update() => audio.Update(world, new TrainControls { Reverser = 1 }, ear, exposed: false, SimConstants.TickSeconds, space: 2);
        int Playing(string name) => audio.Mixer.Voices.Count(v => v.Name == name && !v.Finished);
        Update();
        Assert.Equal(0, Playing("toy-squeaker") + Playing("toy-musicbox") + Playing("toy-drummer"));
        for (int i = 0; i < toys.Count; i++)
            toys[i].Carrier = i + 1;
        Update();
        Update();
        Assert.Equal(1, Playing("toy-squeaker"));
        Assert.Equal(1, Playing("toy-musicbox"));
        Assert.Equal(1, Playing("toy-drummer"));
        toys[2].Carrier = -1;
        Update();
        Assert.Equal(0, Playing("toy-drummer"));
        Assert.Equal(1, Playing("toy-squeaker"));
    }

    [Fact]
    public void TheNoisyToysAreHeardOverTheRoofsWindAndNoneDrownsTheOthers()
    {
        // Note 174: carried on the roof at 15 m/s, each toy is over the wind and the rails (you hear who has it), and the three
        // sit within a few dB of each other (the drummer is the loudest by the meter, not by the speakers).
        var (report, _) = AudioBench.Render(Content, "toys", cars: 6, speed: 15, listenerCar: 3, seconds: 4);
        string[] toys = ["toy-squeaker", "toy-musicbox", "toy-drummer"];
        foreach (var toy in toys)
            Assert.True(report.StemsDb[toy] > report.StemsDb["wind"] + 6, $"{toy} at {report.StemsDb[toy]} dB, wind {report.StemsDb["wind"]}");
        Assert.InRange(toys.Max(t => report.StemsDb[t]) - toys.Min(t => report.StemsDb[t]), 0, 4);
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
