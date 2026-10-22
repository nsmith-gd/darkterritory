using Ballast;
using Ballast.Audio;
using DarkTerritory.Game.Sound;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// A bot's answer to a ping spoken (queue #687, note 987; Finn's note 936): the bot who takes a ping says its answer over the
/// radio as it comes on it, once, in its own voice all night (the answer's take by the bot's id), on the installed bank.
/// </summary>
public class PingVoiceTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TrainTuning Train = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));

    static World Night()
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(20_000)]));
        return new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Train, 4, 1)), line, 2_000));
    }

    static readonly int Blocks = (int)Math.Ceiling(Audio.SampleRate * SimConstants.TickSeconds / Audio.Block);

    /// <summary>A few ticks heard from the cab; the voices that started over them.</summary>
    static List<SoundInstance> Hear(GameAudio audio, World world, HashSet<int> seen, int ticks = 3)
    {
        var started = new List<SoundInstance>();
        var ear = world.Train.Frames[0].ToWorld(new Double3(0, 2.5, 0));
        for (int i = 0; i < ticks; i++)
        {
            world.Tick++;
            audio.Update(world, new TrainControls { Reverser = 1, Brake = 1 }, Listener.At(ear, 0), exposed: true, SimConstants.TickSeconds);
            audio.Interface(world, new PlayerState { Health = 100 }, 0);
            for (int b = 0; b < Blocks; b++)
                audio.Mixer.Render(new float[Audio.Block * 2]);
            started.AddRange(audio.Mixer.Voices.Where(v => seen.Add(v.Id)));
        }
        return started;
    }

    static void Take(World world, int id, int bot)
    {
        int k = world.Pings.FindIndex(p => p.Id == id);
        world.Pings[k] = world.Pings[k] with { Taker = bot };
    }

    [Fact]
    public void ABotTakingAPingSaysItsAnswerOnceInItsOwnVoice()
    {
        var world = Night();
        var audio = new GameAudio(Content);
        var seen = new HashSet<int>();
        Hear(audio, world, seen);
        // Pinged: the chirp, and nobody's answered yet.
        world.Pings.Add(new PingEvent(1, world.Tick, 0, PingKind.Fire, 2, Double3.Zero));
        var heard = Hear(audio, world, seen);
        Assert.Single(heard, v => v.Name == "ui-pings.mark");
        Assert.DoesNotContain(heard, v => v.Name.StartsWith("voice-bot", StringComparison.Ordinal));
        // Bot 3 takes it: "On the fire!" in the fourth voice, once; held on, no more.
        Take(world, 1, 3);
        heard = Hear(audio, world, seen);
        var fire = Assert.Single(heard, v => v.Name.StartsWith("voice-bot", StringComparison.Ordinal));
        Assert.Equal("voice-bot.fire.v3", fire.Name);
        Assert.True(fire.Def.Flat, "over the radio, flat");
        var takes = audio.Bank.Samples.Takes(audio.Bank.Get("voice-bot.fire")!.Layers[0].Sample);
        Assert.Equal(4, takes.Count);
        Assert.Equal(takes[3], fire.Def.Layers[0].Sample);
        Assert.DoesNotContain(Hear(audio, world, seen, 20), v => v.Name.StartsWith("voice-bot", StringComparison.Ordinal));
        // The same bot on the spur keeps its voice; another bot on a house has its own.
        world.Pings.Add(new PingEvent(2, world.Tick, 0, PingKind.Points, 99, Double3.Zero, Taker: 3));
        world.Pings.Add(new PingEvent(3, world.Tick, 0, PingKind.Town, 0, Double3.Zero, Taker: 5));
        heard = Hear(audio, world, seen);
        Assert.Contains(heard, v => v.Name == "voice-bot.spur.v3");
        Assert.Contains(heard, v => v.Name == "voice-bot.town.v1");
        Assert.Equal(takes[3].Replace("fire", "spur"), audio.Bank.Get("voice-bot.spur.v3")!.Layers[0].Sample);
    }

    [Fact]
    public void AnEarThatJoinsDoesNotHearAnswersAlreadyGiven()
    {
        var world = Night();
        world.Pings.Add(new PingEvent(1, world.Tick, 0, PingKind.Crane, 0, Double3.Zero, Taker: 2));
        var audio = new GameAudio(Content);
        var seen = new HashSet<int>();
        Assert.DoesNotContain(Hear(audio, world, seen, 10), v => v.Name.StartsWith("voice-bot", StringComparison.Ordinal));
    }
}
