using Ballast;
using Ballast.Audio;
using DarkTerritory.Game.Sound;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// A one-shot started on a car rides it (spec A.4 "spatially precise"; note 248): at speed the train doesn't run on from
/// under a giggle in the cab and leave it a car back. The world's own sounds stay where they are.
/// </summary>
public class RidingTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TrainTuning T = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
    static readonly CombatTuning C = DataFile.Load<CombatTuning>(Path.Combine(Content, CombatTuning.File));

    [Fact]
    public void AOneShotOnACarRidesItAndOneOfTheWorldStaysPut()
    {
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 4, 1)), new RailLine(new LineDefinition("t", [new TrackSegment(20_000)])), 5_000);
        train.Dynamics.Velocity = 22;
        var world = new World(train, C);
        var audio = new GameAudio(Content);
        var tone = new SoundDef(4, [new LayerDef(SourceKind.Sine, 0.3, Frequency: 440)], Duration: 2);
        audio.Bank.Add("crew-test.knock", tone);
        audio.Bank.Add("world-test.bang", tone);
        var local = new Double3(0.5, 2, 1);
        var onCar = audio.Mixer.Play("crew-test.knock", train.Frames[2].ToWorld(local))!;
        var fixedAt = train.Frames[2].ToWorld(local);
        var ofWorld = audio.Mixer.Play("world-test.bang", fixedAt)!;
        // Heard the frame they start (as a cue played in GameAudio.Update is), then a second's ticks.
        void Hear() => audio.Update(world, world.Controls, Listener.At(train.Frames[1].Origin + Double3.Up * 2, 0), exposed: true, SimConstants.TickSeconds);
        Hear();
        for (int i = 0; i < SimConstants.TickRate; i++)
        {
            world.BeginTick();
            world.Step(world.Controls);
            Hear();
        }
        // A second on at 22 m/s: the knock is still where it was on the car, the bang 22 m back down the line.
        Assert.True((train.Frames[2].ToLocal(onCar.Position) - local).Length < 0.01);
        Assert.Equal(fixedAt, ofWorld.Position);
        Assert.True((train.Frames[2].ToWorld(local) - fixedAt).Length > 20);
    }
}
