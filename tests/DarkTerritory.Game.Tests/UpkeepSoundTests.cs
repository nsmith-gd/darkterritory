using Ballast;
using Ballast.Audio;
using DarkTerritory.Game.Sound;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The upkeep heard (queue #95, note 358): the hands on the jobs the train makes as it runs (D1's notes 331 and 346), read
/// off what the clients are sent: a car's hot box and its lamp's guttering, and each crewmate's hold. The sounds are stand-ins,
/// so what's installed doesn't change what's tested: which cue plays, when, how often.
/// </summary>
public class UpkeepSoundTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TrainTuning T = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
    static readonly UpkeepTuning U = DataFile.Load<UpkeepTuning>(Path.Combine(Content, UpkeepTuning.File));
    const double Dt = SimConstants.TickSeconds;

    sealed class Bench
    {
        readonly HashSet<int> _seen = [];
        public readonly World World;
        public readonly GameAudio Audio;

        public Bench(params string[] sounds)
        {
            var line = new RailLine(new LineDefinition("t", [new TrackSegment(60_000)]));
            World = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 6, 1)), line, 5_000));
            World.Upkeep = U;
            Audio = new GameAudio(Content);
            foreach (var s in sounds)
                Audio.Bank.Add(s.TrimEnd('~'), new SoundDef(5, [new LayerDef(SourceKind.Noise, 0.1)], Loop: s.EndsWith('~'), Duration: 0.1, MaxInstances: 16));
        }

        /// <summary>One update, as a client hears it; the sounds it started.</summary>
        public List<string> Update(params (int, PlayerState)[] crew)
        {
            Audio.CrewStates = crew;
            var frame = World.Train.Frames[2];
            Audio.Update(World, new TrainControls { Reverser = 1 }, Listener.At(frame.ToWorld(new Double3(4, 1.6, 0)), frame.Heading), exposed: true, Dt);
            return [.. Audio.Mixer.Voices.Where(v => _seen.Add(v.Id)).Select(v => v.Name)];
        }

        public bool Playing(string name) => Audio.Mixer.Voices.Any(v => v.Name == name && !v.Stopped);
    }

    [Fact]
    public void AGreasedBoxHissesOffOnceAndOneThatCaughtDoesnt()
    {
        var b = new Bench("crew-upkeep.greased");
        var v = b.World.Train.Vehicles[2];
        v.HotBox = 30;
        b.Update();
        v.HotBox = 0;
        Assert.Equal(["crew-upkeep.greased"], b.Update());
        Assert.Empty(b.Update());
        // Left till it caught: its fire's the sound, not a greasing.
        v.HotBox = U.HotBox.FireAfter - Dt;
        b.Update();
        v.HotBox = 0;
        Assert.Empty(b.Update());
    }

    [Fact]
    public void TheGreaseGunIsHeardWhileACrewmatesAtTheBoxAndNotOnceItsCool()
    {
        var b = new Bench("crew-upkeep.grease~");
        var train = b.World.Train;
        train.Vehicles[2].HotBox = 30;
        var shape = train.Frames[2].Shape;
        var me = new PlayerState
        {
            Parent = PlayerState.World,
            Surface = Surface.Ground,
            Position = train.Frames[2].ToWorld(new Double3(shape.HalfWidth + 0.6, -0.2, shape.HalfLength - U.HotBox.BogieInset)),
            Health = 100,
        };
        Assert.Equal(2, HotBoxes.Within(me, train, U.HotBox));
        Assert.DoesNotContain("crew-upkeep.grease", b.Update((1, me)));
        Assert.Contains("crew-upkeep.grease", b.Update((1, me with { ActionProgress = 1.2 })));
        b.Update((1, me with { ActionProgress = 2.4 }));
        Assert.True(b.Playing("crew-upkeep.grease"));
        // Done: the box is cool, and the gun's put down whatever the hold says.
        train.Vehicles[2].HotBox = 0;
        b.Update((1, me with { ActionProgress = U.HotBox.GreaseSeconds }));
        Assert.False(b.Playing("crew-upkeep.grease"));
    }

    [Fact]
    public void ALampTrimmedIsHeardAndOneThatWentOutIsnt()
    {
        var b = new Bench("crew-upkeep.trim");
        var v = b.World.Train.Vehicles[2];
        v.Gutter = 10;
        b.Update();
        v.Gutter = 0;
        Assert.Equal(["crew-upkeep.trim"], b.Update());
        // Gone out instead: no trim.
        v.Gutter = 40;
        b.Update();
        (v.Gutter, v.LampLit) = (0, false);
        Assert.DoesNotContain("crew-upkeep.trim", b.Update());
    }
}
