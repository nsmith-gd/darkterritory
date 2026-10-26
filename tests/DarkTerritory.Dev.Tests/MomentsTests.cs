using Ballast;
using DarkTerritory.Dev.Moments;
using DarkTerritory.Game;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Dev.Tests;

/// <summary>
/// `dt moments` (queue #256, ARCHITECTURE §8 note 1034): a night's clip-worthy moments scored from what the host keeps of it
/// (the incident report's facts, the creatures' grabs, the crew's places): a death, a grab broken (worth more the later), and
/// someone left behind, once; and the reel's best, kept apart.
/// </summary>
public class MomentsTests
{
    static readonly string Content = DataFile.FindContentRoot();

    [Fact]
    public void ADeathAGrabBrokenLateAndSomeoneLeftBehindAreMomentsAndTheBestLeadTheReel()
    {
        var s = new PrototypeSession(Content, Sim.LineGen.Routes.Generate(Content, "frontier:7", 4), 4, enemies: false);
        var world = s.World;
        var scores = DataFile.Load<MomentScores>(Path.Combine(Path.GetDirectoryName(Content)!, MomentScores.File));
        var scorer = new MomentScorer(scores);
        // Aboard, standing: nobody's left behind.
        var aboard = new PlayerState { Parent = 1, Position = new Ballast.Double3(0, 3, 0), Health = 100 };
        var far = new PlayerState { Parent = PlayerState.World, Position = world.Train.Frames[0].Origin + new Ballast.Double3(0, 0, 400), Health = 100 };
        scorer.Step(world, [(1, aboard), (2, far)]);
        Assert.Empty(scorer.All);
        // A death, a grab that's only a grab (not a moment by itself), and a grab broken after 3 s.
        world.Attribution.Add(new Sim.Run.Incident(IncidentKind.Death, 10, 1, "Swallowed by the Car Hugger", "on car 3"));
        world.Attribution.Add(new Sim.Run.Incident(IncidentKind.Grab, 10, 1, "Grabbed by the Dragger", "on car 2"));
        world.EnemyEvents.Add(new EnemyEvent(world.Tick, 7, EnemyKind.Dragger, SpinePhase.Grab, SpinePhase.BreakOff, 3));
        // The train going, and crewmate 2 on foot 400 m off it: left behind, once.
        world.Train.Dynamics.Velocity = 10;
        scorer.Step(world, [(1, aboard), (2, far)]);
        // (The host clears a tick's events at the next.)
        world.EnemyEvents.Clear();
        scorer.Step(world, [(1, aboard), (2, far)]);
        Assert.Equal(["death", "grabBroken", "leftBehind"], scorer.All.Select(m => m.Kind));
        Assert.Equal(scores.Incidents["death"], scorer.All[0].Score);
        Assert.Equal(scores.GrabBroken + 3 * scores.LatePerSecond, scorer.All[1].Score);
        Assert.Equal(2, scorer.All[2].Who);
        // A grab broken at once is worth less than one broken late.
        world.EnemyEvents.Clear();
        world.EnemyEvents.Add(new EnemyEvent(world.Tick, 8, EnemyKind.Dragger, SpinePhase.Grab, SpinePhase.BreakOff, 0.2));
        scorer.Step(world, [(1, aboard)]);
        Assert.True(scorer.All[^1].Score < scorer.All[1].Score);
        // The reel's best: all at one moment of the night, so the best of them alone (they're kept apart); the rest
        // with nothing kept apart, highest first, in the order they happened.
        Assert.Equal("grabBroken", Assert.Single(scorer.Top(3)).Kind);
        var apart = new MomentScorer(scores with { Apart = 0 });
        apart.Step(world, [(1, aboard)]);
        Assert.Equal(["death", "grabBroken"], apart.Top(2).Select(m => m.Kind));
    }
}
