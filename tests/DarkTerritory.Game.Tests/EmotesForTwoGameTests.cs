using System.Numerics;
using Ballast;
using DarkTerritory.Game;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// Emotes for two, and the edges, as the crew see them (note 722): the wheel's twelve places with U1's three kept, a met high
/// five drawn at the gap between the two, a dance-off's lead, a sitter put on the edge facing out, a leaner side on in the
/// doorway; and the views stage what the sim's edge rules allow.
/// </summary>
public sealed class EmotesForTwoGameTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly PlayerTuning P = DataFile.Load<PlayerTuning>(Path.Combine(Content, PlayerTuning.File));
    static readonly TrainTuning T = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));

    static TrainOnLine Train() => new(new TrainDynamics(Consist.Uniform(T, 4, 1)), RailLine.Load(Path.Combine(Content, "lines/test-loop.json")), 600);

    [Fact]
    public void TheWheelHasTwelvePlacesTheOldSpokesWhereTheyWereAndU1sThreeKept()
    {
        var at = EmoteWheel.Spokes.ToDictionary(s => s.Emote, s => s.At);
        Assert.Equal(new Vector2(0, -1), at[Emote.Dance]);
        Assert.Equal(new Vector2(-1, 0), at[Emote.Wave]);
        Assert.Equal(new Vector2(1, 0), at[Emote.Point]);
        Assert.Equal(new Vector2(0, 1), at[Emote.Aboard]);
        foreach (var e in new[] { Emote.HighFive, Emote.FistBump, Emote.SitEdge, Emote.LeanOut })
            Assert.True(at.ContainsKey(e), $"{e} on the wheel");
        // No two places the same, U1's included, and all on the clock's twelve.
        var places = EmoteWheel.Spokes.Select(s => s.At).Concat(EmoteWheel.Kept.Select(k => k.At)).ToList();
        Assert.Equal(11, places.Count);
        Assert.Equal(places.Count, places.Distinct().Count());
        Assert.All(places, p => Assert.Contains(p, Enumerable.Range(1, 12).Select(EmoteWheel.Clock)));
        // Leaning at each picks it.
        foreach (var (emote, _, dir) in EmoteWheel.Spokes)
        {
            var wheel = new EmoteWheel();
            wheel.Update(true, dir.X * 70, dir.Y * 70);
            Assert.Equal(emote, wheel.Update(false, 0, 0));
        }
    }

    [Fact]
    public void AMetHighFiveIsDrawnAtTheGapAndADanceOffWithItsLead()
    {
        var train = Train();
        // (Tick 0 is no meet's: a record's meet of 0 says it isn't met.)
        var world = new World(train) { EmoteTuning = P.Emotes, Tick = 100 };
        var dave = PlayerMotor.SpawnOnRoof(train, 2, 0, P) with { Yaw = 0 };
        var priya = PlayerMotor.SpawnOnRoof(train, 2, -1.2, P) with { Yaw = Math.PI };
        List<(int, PlayerState)> crew = [(1, dave), (2, priya)];
        world.Emotes.Add(new EmoteEvent(1, world.Tick, 1, Emote.HighFive, world.Tick, 2));
        world.Emotes.Add(new EmoteEvent(2, world.Tick, 2, Emote.HighFive, world.Tick, 1));
        var drawn = CrewActs.Crewmate(1, dave, world, train.Frames, null, crew);
        Assert.Equal(Emote.HighFive, drawn.Emote);
        Assert.True(drawn.EmoteWith.Met);
        Assert.Equal(1.2, drawn.EmoteWith.Gap, 3);
        // Two dancing face to face are a dance-off; one of them leads.
        world.Emotes.Clear();
        world.Emotes.Add(new EmoteEvent(3, world.Tick, 1, Emote.Dance));
        world.Emotes.Add(new EmoteEvent(4, world.Tick, 2, Emote.Dance));
        var a = CrewActs.Crewmate(1, dave, world, train.Frames, null, crew).EmoteWith;
        var b = CrewActs.Crewmate(2, priya, world, train.Frames, null, crew).EmoteWith;
        Assert.Equal((2, 2), (a.Dancers, b.Dancers));
        Assert.NotEqual(a.Leading, b.Leading);
    }

    [Fact]
    public void ASitterIsPutOnTheEdgeFacingOutAndAnEdgeEmoteAwayFromOneIsntDrawn()
    {
        var train = Train();
        var world = new World(train) { EmoteTuning = P.Emotes };
        var frame = train.Frames[2];
        var s = PlayerMotor.SpawnOnRoof(train, 2, 0, P, frame.Shape.HalfWidth - 0.4) with { Yaw = 0.7 };
        world.Emotes.Add(new EmoteEvent(1, world.Tick, 5, Emote.SitEdge));
        var drawn = CrewActs.Crewmate(5, s, world, train.Frames);
        Assert.Equal(Emote.SitEdge, drawn.Emote);
        // Out at the roof's side, facing out over it (+X: yaw −π/2 from the car's heading).
        Assert.True(drawn.Local.X > s.Position.X, $"{drawn.Local.X:0.00} vs {s.Position.X:0.00}");
        Assert.Equal(-Math.PI / 2, Math.IEEERemainder(drawn.Yaw - frame.Heading, Math.Tau), 3);
        // Down the roof's middle, no edge: not drawn sat in the air.
        var middle = s with { Position = s.Position with { X = 0 } };
        Assert.Equal(Emote.None, CrewActs.Crewmate(5, middle, world, train.Frames).Emote);
    }

    [Fact]
    public void TheViewsStageWhatTheEdgeRulesAllow()
    {
        var train = Train();
        var sit = Staging.Emotes(train, Content, "sitedge");
        Assert.Equal(2, sit.Count);
        Assert.All(sit, c => Assert.Equal(Emote.SitEdge, c.Emote));
        // The one in the door sits out on its landing's edge, past the car's side.
        int car = Staging.SideDoorCar(train);
        Assert.True(sit[1].Local.X > train.Frames[car].Shape.HalfWidth, $"sat at {sit[1].Local.X:0.00}");
        var lean = Staging.Emotes(train, Content, "leanout");
        Assert.Equal(2, lean.Count);
        Assert.All(lean, c => Assert.NotEqual(0, c.EmoteWith.Out));
        Assert.Equal(5, Staging.Emotes(train, Content, "highfive").Count);
        Assert.Equal(1, Staging.Emotes(train, Content, "danceoff").Count(c => c.EmoteWith.Leading));
    }
}
