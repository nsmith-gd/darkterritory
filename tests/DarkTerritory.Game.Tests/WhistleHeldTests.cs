using Ballast;
using Ballast.Audio;
using DarkTerritory.Game.Sound;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// Note 839 (the director, 16 Oct 2026: "When I let go of H, it just keeps whistling ... when I let go of the key, it
/// immediately stops the whistle. That way we can actually do the longs and the shorts"): the train's whistle is a recorded
/// blast, and a crewmate's pull on it played the whole take whatever its length. Let go, it stops with the blowing; the
/// Whistler's (no hand on the cord) still rings out.
/// </summary>
public class WhistleHeldTests
{
    static readonly string Content = DataFile.FindContentRoot();

    [Fact]
    public void ACrewmatesShortPullIsAShortBlastAndTheWhistlersRingsOut()
    {
        var tuning = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
        var line = RailLine.Load(Path.Combine(Content, "lines", "test-loop.json"));
        var world = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(tuning, 4, 1)), line, 1200));
        var audio = new GameAudio(Content);
        var ear = Listener.At(world.Train.Frames[0].Origin, world.Train.Frames[0].Heading);
        void Tick()
        {
            world.WhistleSeconds = Math.Max(0, world.WhistleSeconds - SimConstants.TickSeconds);
            audio.Update(world, new TrainControls { Reverser = 1 }, ear, exposed: true, SimConstants.TickSeconds);
        }
        SoundInstance? Whistle() => audio.Mixer.Voices.FirstOrDefault(v => v.Name == "train-whistle" && !v.Finished && !v.Stopped);
        // Two shorts on the cord: each its own blast, the first stopped before the second.
        world.Whistled(tuning.Whistle.TailSeconds + 0.2, by: 1);
        Tick();
        var first = Assert.IsType<SoundInstance>(Whistle());
        for (int i = 0; i < 15; i++)
            Tick();
        Assert.True(first.Stopped, "the first short still sounding half a second after it was let go");
        Assert.Null(Whistle());
        world.Whistled(tuning.Whistle.TailSeconds + 0.2, by: 1);
        Tick();
        Assert.NotSame(first, Assert.IsType<SoundInstance>(Whistle()));
        for (int i = 0; i < 15; i++)
            Tick();
        Assert.Null(Whistle());
        // The Whistler's (no hand on the cord): the take plays on past the blowing.
        world.Whistled(0.2);
        Tick();
        var his = Assert.IsType<SoundInstance>(Whistle());
        for (int i = 0; i < 10; i++)
            Tick();
        Assert.Equal(0, world.WhistleSeconds);
        Assert.False(his.Stopped);
    }
}
