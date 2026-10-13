using Ballast;
using Ballast.Audio;
using DarkTerritory.Game.Sound;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// Note 663: a dropped pin or a Knotter's coupling let go is heard where it parted, the coupling torn, once, as the news says
/// it; a crewmate's own cut isn't (its hold has its sound).
/// </summary>
public class NewsAudioTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TrainTuning T = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
    const string Tear = "state-derail.tear";

    [Fact]
    public void ADroppedPinIsHeardTornWhereItPartedOnceAndACrewmatesCutIsnt()
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(20_000)]));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 4, 1)), line, 5_000);
        train.Dynamics.Velocity = 10;
        var world = new World(train);
        var audio = new GameAudio(Content);
        audio.Bank.Add(Tear, new SoundDef(4, [new LayerDef(SourceKind.Sine, 0.3, Frequency: 440)], Duration: 0.1, MaxInstances: 64));
        // Where it was heard, and car 3's front then (where it was coupled to car 2).
        var heard = new List<(Double3 At, Double3 Front)>();
        var seen = new HashSet<int>();
        void Run(double seconds)
        {
            for (int i = 0; i < seconds / SimConstants.TickSeconds; i++)
            {
                world.BeginTick();
                world.Step(world.Controls);
                audio.Update(world, world.Controls, Listener.At(train.Frames[1].Origin + Double3.Up * 5, 0), true, SimConstants.TickSeconds, PlayerMotor.Outside);
                foreach (var v in audio.Mixer.Voices)
                    if (seen.Add(v.Id) && v.Name == Tear)
                        heard.Add((v.Position, train.Frames[3].ToWorld(new Double3(0, 1, -train.Frames[3].Shape.HalfLength))));
            }
        }
        Run(0.5);
        train.Uncouple(2, Parting.Pin);
        Run(1);
        var (at, front) = Assert.Single(heard);
        Assert.True((at - front).Length < 1, $"{at} vs {front}");

        train.Uncouple(1, Parting.Crew, 0);
        Run(1);
        Assert.Single(heard);
    }
}
