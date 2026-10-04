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
        int Playing() => audio.Mixer.Voices.Count(v => v.Name is "holdout-shout" or "holdout-bang" && !v.Finished);
        void Update() => audio.Update(world, new TrainControls { Reverser = 1 }, ear, exposed: true, SimConstants.TickSeconds);
        // Already called before this client looked: old news.
        world.Holdouts.Mirror(h.Index, HoldoutState.Occupied, 3, 0, calls: 2);
        Update();
        Assert.Equal(0, Playing());
        world.Holdouts.Mirror(h.Index, HoldoutState.Occupied, 3, 0, calls: 3);
        Update();
        Assert.Equal(1, Playing());
        Assert.All(audio.Mixer.Voices.Where(v => v.Name is "holdout-shout" or "holdout-bang"), v => Assert.Equal(h.Inside, v.Position));
        Update();
        Assert.Equal(1, Playing());
    }
}
