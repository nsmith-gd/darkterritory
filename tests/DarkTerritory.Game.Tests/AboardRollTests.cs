using Ballast;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// Note 740 (the director, 14 Oct: "We need some way to signal to the driver that everybody is on the train"; GDD §17):
/// "Aboard!" is called, not shown. The crew see each call with its name; in the cab, after a horn at a stand, the roll of
/// who's called back, and ALL ABOARD once every living crewmate has.
/// </summary>
public class AboardRollTests
{
    static readonly TrainTuning Train = DataFile.Load<TrainTuning>(Path.Combine(DataFile.FindContentRoot(), TrainTuning.File));

    static World Standing()
    {
        var t = Train;
        var world = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(t, 3, 1)), new RailLine(new LineDefinition("t", [new TrackSegment(5_000)])), 1_000));
        world.Names[1] = "Priya";
        world.Names[2] = "Joe";
        world.Names[3] = "Ann";
        return world;
    }

    static readonly IReadOnlyList<RosterLine> Crew =
    [
        new(1, "YOU", "", true, You: true),
        new(2, "Joe", "", true),
        new(3, "Ann", "", true),
    ];

    [Fact]
    public void AfterTheHornTheCabHearsWhoCalledBackAndThenAllAboard()
    {
        var world = Standing();
        world.Tick = 100;
        world.Whistled(1.0, 1);
        Assert.Contains(("ABOARD: NOBODY YET (0/2)", false), Hud.AboardLines(world, 1, inCab: true, Crew));
        world.Tick = 130;
        world.Emotes.Add(new EmoteEvent(1, 130, 2, Emote.Aboard));
        var lines = Hud.AboardLines(world, 1, inCab: true, Crew);
        Assert.Contains(("JOE: ABOARD!", false), lines);
        Assert.Contains(("ABOARD: JOE (1/2)", false), lines);
        // Out of the cab, the call is heard, and there's no roll.
        Assert.DoesNotContain(Hud.AboardLines(world, 1, inCab: false, Crew), l => l.Text.StartsWith("ABOARD:", StringComparison.Ordinal));
        world.Tick = 160;
        world.Emotes.Add(new EmoteEvent(2, 160, 3, Emote.Aboard));
        Assert.Contains(("ALL ABOARD", true), Hud.AboardLines(world, 1, inCab: true, Crew));
    }

    [Fact]
    public void NobodyCountedWhoHasntCalledNorTheDeadNorBeforeTheHorn()
    {
        var world = Standing();
        world.Tick = 50;
        world.Emotes.Add(new EmoteEvent(1, 50, 2, Emote.Aboard));
        world.Tick = 100;
        world.Whistled(1.0, 1);
        // Joe called before the horn: not on this roll. Ann's dead: not waited for.
        IReadOnlyList<RosterLine> crew = [Crew[0], Crew[1], Crew[2] with { Alive = false }];
        Assert.Contains(("ABOARD: NOBODY YET (0/1)", false), Hud.AboardLines(world, 1, inCab: true, crew));
    }
}
