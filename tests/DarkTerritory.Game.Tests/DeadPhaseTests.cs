using Ballast;
using Ballast.Audio;
using DarkTerritory.Game.Sound;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// GDD v1.4 App. D.6 and D.7 on a client (note 179): the dead see the whole queue and where they are in it; each Call Out the
/// host counts is heard once, from its Holdout.
/// </summary>
public class DeadPhaseTests
{
    static readonly string Content = DataFile.FindContentRoot();

    static World Night()
    {
        var route = RouteGenerator.Generate(RouteTuning.Load(Content), RouteTier.Frontier, 1);
        var tuning = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
        var world = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(tuning, 3, 0)), route.Build(), 600));
        world.EnableHoldouts(DataFile.Load<HoldoutTuning>(Path.Combine(Content, HoldoutTuning.File)), route);
        return world;
    }

    [Fact]
    public void TheDeadSeeTheWholeQueueAndTheirPlaceInIt()
    {
        var world = Night();
        world.Names[3] = "Sam";
        world.Names[5] = "Ana";
        Assert.Null(Hud.QueueLine(world, world.Holdouts!, 1));
        world.Holdouts!.MirrorQueue([(3, false), (1, false), (5, true)]);
        Assert.Equal("QUEUE: 1 SAM   2 YOU   3 ANA (JOINING)", Hud.QueueLine(world, world.Holdouts, 1));
    }

    [Fact]
    public void EachCallOutIsHeardOnceFromItsHoldout()
    {
        var world = Night();
        var h = world.Holdouts!.All[0];
        var audio = new GameAudio(Content);
        var ear = Listener.At(h.Inside + new Double3(10, 1.6, 0), 0);
        // A call in the prisoner's own voice set or a bout of banging (note 193), or note 179's synth shout and bang under them.
        static bool IsCall(Ballast.Audio.SoundInstance v) => v.Name.StartsWith("voice-prisoner-sets.", StringComparison.Ordinal)
            || v.Name is "voice-callout.bang" or "holdout-shout" or "holdout-bang";
        var heard = new Dictionary<int, Double3>();
        void Update(int ticks = 1)
        {
            for (int i = 0; i < ticks; i++)
            {
                audio.Update(world, new TrainControls { Reverser = 1 }, ear, exposed: true, SimConstants.TickSeconds);
                foreach (var v in audio.Mixer.Voices.Where(IsCall))
                    heard.TryAdd(v.Id, v.Position);
            }
        }
        // Already called before this client looked: old news.
        world.Holdouts.Mirror(h.Index, HoldoutState.Occupied, 3, 0, calls: 2);
        Update(5);
        Assert.Empty(heard);
        world.Holdouts.Mirror(h.Index, HoldoutState.Occupied, 3, 0, calls: 3);
        Update(SimConstants.TickRate * 2);
        Assert.NotEmpty(heard);
        Assert.All(heard.Values, at => Assert.True((at - h.Inside).Length < 2, $"heard at {at}, the Holdout's inside is {h.Inside}"));
        // Once a call: nothing more until the count goes up again.
        int once = heard.Count;
        Update(SimConstants.TickRate * 2);
        Assert.Equal(once, heard.Count);
    }
}
