using Ballast;
using Ballast.Audio;
using DarkTerritory.Game.Sound;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>The La Cucaracha horn (ARCHITECTURE §8 note 701): "the whistle plays it. Every pull, all of it."</summary>
public class DressAudioTests
{
    static readonly string Content = DataFile.FindContentRoot();

    [Fact]
    public void EveryPullPlaysAllOfItOnTheWhistlesTier()
    {
        var tuning = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
        tuning = tuning with { Composition = tuning.Composition with { Dress = ["cucarachaHorn"] } };
        var line = RailLine.Load(Path.Combine(Content, "lines", "test-loop.json"));
        var world = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(tuning, 4, 1)), line, 1200));
        var audio = new GameAudio(Content);
        var ear = Listener.At(world.Train.Frames[0].Origin, world.Train.Frames[0].Heading);
        void Tick()
        {
            world.WhistleSeconds = Math.Max(0, world.WhistleSeconds - SimConstants.TickSeconds);
            audio.Update(world, new TrainControls { Reverser = 1 }, ear, exposed: true, SimConstants.TickSeconds);
        }
        SoundInstance? Horn() => audio.Mixer.Voices.FirstOrDefault(v => v.Name == "train-horn-cucaracha" && !v.Finished);
        // A tap on the cord: the whole phrase, and no whistle.
        world.Whistled(0.1);
        Tick();
        var horn = Assert.IsType<SoundInstance>(Horn());
        Assert.True(horn.Clip!.Seconds > 4, $"the phrase is {horn.Clip.Seconds:0.0} s");
        for (int i = 0; i < 30; i++)
            Tick();
        Assert.Equal(0, world.WhistleSeconds);
        Assert.Same(horn, Horn());
        Assert.DoesNotContain(audio.Mixer.Voices, v => v.Name == "train-whistle" && !v.Finished);
        // It's still the whistle: its tier and its reach.
        var whistle = audio.Bank.Get("train-whistle")!;
        var def = audio.Bank.Get("train-horn-cucaracha")!;
        Assert.Equal(whistle.Tier, def.Tier);
        Assert.Equal(whistle.MaxDistance, def.MaxDistance);
    }

    [Fact]
    public void ThePhraseIsLaCucaracha()
    {
        // Da-da-da DUM DUM, twice, then the run down: its notes' onsets, by the clip's loudness rising.
        var clip = BandSynth.Cucaracha();
        Assert.Equal(clip, BandSynth.Cucaracha());
        int window = Audio.SampleRate / 100, onsets = 0;
        double was = 0;
        bool sounding = false;
        for (int i = 0; i + window < clip.Length; i += window)
        {
            double level = 0;
            for (int j = i; j < i + window; j++)
                level = Math.Max(level, Math.Abs(clip[j]));
            if (!sounding && level > 0.15 && level > was * 1.5)
            {
                onsets++;
                sounding = true;
            }
            else if (level < 0.05)
                sounding = false;
            was = level;
        }
        // Ten notes then seven, each its own start (the release between them).
        Assert.InRange(onsets, 15, 17);
    }
}
