using Ballast;
using Ballast.Audio;
using DarkTerritory.Game.Sound;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// Leaving a night (GameAudio.EndNight): its bed and loops stop rather than playing on under the menus, and the next night
/// starts them afresh rather than holding on to the stopped ones.
/// </summary>
public class EndNightTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TrainTuning Tuning = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));

    static World Night()
    {
        var line = RailLine.Load(Path.Combine(Content, "lines", "test-loop.json"));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning, 4, 1)), line, 1200);
        return new World(train, DataFile.Load<CombatTuning>(Path.Combine(Content, CombatTuning.File)));
    }

    static void Run(GameAudio audio, World world, int ticks)
    {
        var frame = world.Train.Frames[1];
        var ear = Listener.At(frame.ToWorld(new Double3(0, frame.Shape.RoofHeight + 1.65, 0)), frame.Heading);
        var block = new float[Audio.Block * 2];
        for (int i = 0; i < ticks; i++)
        {
            audio.Update(world, world.Controls, ear, exposed: true, SimConstants.TickSeconds);
            audio.Mixer.Render(block);
        }
    }

    static bool Playing(GameAudio audio, string name) => audio.Mixer.Voices.Any(v => v.Name == name && !v.Finished);

    /// <summary>The boiler's roar: the synth's, or the recordings that take its place (GameAudio.Train).</summary>
    static bool Roaring(GameAudio audio) => Playing(audio, "boiler-roar") || Playing(audio, "bed-boiler-roar.roar-low") || Playing(audio, "bed-boiler-roar.roar-high");

    [Fact]
    public void TheNightsSoundsStopWhenItEndsAndStartAgainWithTheNext()
    {
        var audio = new GameAudio(Content);
        Run(audio, Night(), 10);
        Assert.True(Roaring(audio));

        audio.EndNight();
        audio.Mixer.Render(new float[Audio.Block * 2]);
        Assert.DoesNotContain(audio.Mixer.Voices, v => !v.Finished);

        Run(audio, Night(), 10);
        Assert.True(Roaring(audio));
    }
}
