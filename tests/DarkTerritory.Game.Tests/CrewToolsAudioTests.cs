using Ballast;
using Ballast.Audio;
using DarkTerritory.Game.Sound;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The crew's tools heard (ARCHITECTURE §8 note 1052): the cord pulled in car 3 strikes the cab's gong three times, never the
/// overspeed bell; the telltale's relay drops out once as the train parts.
/// </summary>
public class CrewToolsAudioTests
{
    static readonly string Content = DataFile.FindContentRoot();

    static (World World, GameAudio Audio, Listener Ear) Night(TrainTuning tuning)
    {
        var line = RailLine.Load(Path.Combine(Content, "lines", "test-loop.json"));
        var world = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(tuning, 5, 1)), line, 1200));
        var audio = new GameAudio(Content);
        return (world, audio, Listener.At(world.Train.Frames[0].Origin, world.Train.Frames[0].Heading));
    }

    static void Run(World world, GameAudio audio, Listener ear, double seconds, Action<SoundInstance>? heard = null)
    {
        for (int i = 0; i < seconds * SimConstants.TickRate; i++)
        {
            audio.Update(world, new TrainControls { Reverser = 1 }, ear, exposed: true, SimConstants.TickSeconds);
            if (heard is not null)
                foreach (var v in audio.Mixer.Voices)
                    heard(v);
        }
    }

    [Fact]
    public void ThePullRingsTheCarsNumberOnTheCordsOwnGong()
    {
        var tuning = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
        tuning = tuning with { Kit = tuning.Kit with { Cord = true } };
        var (world, audio, ear) = Night(tuning);
        Run(world, audio, ear, 0.2);
        world.Cord = new CordPull(3, 1, 1);
        var strokes = new HashSet<SoundInstance>();
        Run(world, audio, ear, tuning.Kit.CordBell.StrokeSeconds * 5, v =>
        {
            if (v.Name == "up-tells.cord-bell")
                strokes.Add(v);
            Assert.NotEqual("warn-overspeed", v.Name);
        });
        Assert.Equal(3, strokes.Count);
        // The gong is train state's tier, as the overspeed bell is, and the two aren't the same bell.
        var gong = audio.Bank.Get("up-tells.cord-bell")!;
        Assert.Equal(audio.Bank.Get("warn-overspeed")!.Tier, gong.Tier);
    }

    [Fact]
    public void TheTelltalesRelayDropsOutOnceAsTheTrainParts()
    {
        var tuning = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
        tuning = tuning with { Kit = tuning.Kit with { Telltale = true } };
        var (world, audio, ear) = Night(tuning);
        var clicks = new HashSet<SoundInstance>();
        void Heard(SoundInstance v)
        {
            if (v.Name == "up-tells.telltale-out")
                clicks.Add(v);
        }
        Run(world, audio, ear, 0.5, Heard);
        Assert.Empty(clicks);
        Assert.True(world.Train.Uncouple(2, Parting.Pin));
        Run(world, audio, ear, 1, Heard);
        Assert.Single(clicks);
    }
}
