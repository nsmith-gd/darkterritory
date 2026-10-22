using System.Numerics;
using Ballast;
using Ballast.Render;
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
        Assert.Equal(0, drawn.EmoteWith.Across, 3);
        // Stood a little to one side of each other (note 807), each has the other the same way across: to their right.
        var aside = PlayerMotor.SpawnOnRoof(train, 2, -1.2, P, 0.3) with { Yaw = Math.PI };
        List<(int, PlayerState)> offset = [(1, dave), (2, aside)];
        var mine = CrewActs.Crewmate(1, dave, world, train.Frames, null, offset).EmoteWith;
        var theirs = CrewActs.Crewmate(2, aside, world, train.Frames, null, offset).EmoteWith;
        Assert.Equal(0.3, mine.Across, 3);
        Assert.Equal(mine.Across, theirs.Across, 3);
        Assert.Equal(Math.Sqrt(1.2 * 1.2 + 0.3 * 0.3), mine.Gap, 3);
        // Two dancing face to face are a dance-off; one of them leads.
        world.Emotes.Clear();
        world.Emotes.Add(new EmoteEvent(3, world.Tick, 1, Emote.Dance));
        world.Emotes.Add(new EmoteEvent(4, world.Tick, 2, Emote.Dance));
        var a = CrewActs.Crewmate(1, dave, world, train.Frames, null, crew).EmoteWith;
        var b = CrewActs.Crewmate(2, priya, world, train.Frames, null, crew).EmoteWith;
        Assert.Equal((2, 2), (a.Dancers, b.Dancers));
        Assert.NotEqual(a.Leading, b.Leading);
        // Which turn it is and how far into it, from the host's tick (emotes.danceOffBeat's 2 s: 60 ticks), the same for both.
        Assert.Equal((1, 40.0 / 30), (a.Round, a.OnBeat));
        Assert.Equal((a.Round, a.OnBeat), (b.Round, b.OnBeat));
    }

    /// <summary>
    /// Note 807: the two hands of a met high five meet on the point between the two, palm to palm (their wrists 6 cm apart, a
    /// palm's thickness each side, and no further than 9 cm at emotes.reach's 1.5 m), and a fist bump's knuckles touch (its wrists
    /// a fist's 10 cm each side); however far apart within reach and however far to one side, and never leant in so far that the
    /// heads knock.
    /// </summary>
    [Fact]
    public void TheTwoHandsMeetBetweenThemWhereverTheyStand()
    {
        var art = new CreatureArt(Look.Load(Content), Content);
        var mesh = new MeshBuilder();
        foreach (var pose in new[] { CrewPose.HighFive, CrewPose.FistBump })
            foreach (var (gap, across) in new[] { (0.9, 0.0), (1.14, 0.3), (1.5, 0.0), (1.5, 0.5) })
            {
                // One at the origin facing −Z; the other ahead of them and across to their right, turned round to face them.
                var d = new EmoteDetail(Met: true, SinceMeet: 0.13, Gap: gap, Across: across);
                var other = Matrix4x4.CreateRotationY(MathF.PI) * Matrix4x4.CreateTranslation((float)across, 0, -(float)Math.Sqrt(gap * gap - across * across));
                Assert.True(art.Crewmate(mesh, Matrix4x4.Identity, pose, 1.0, 1, emote: d));
                var (hand, head) = (art.Joint("hand_r", Matrix4x4.Identity), art.Joint("head", Matrix4x4.Identity));
                Assert.True(art.Crewmate(mesh, other, pose, 1.0, 2, emote: d));
                var (theirHand, theirHead) = (art.Joint("hand_r", other), art.Joint("head", other));
                float apart = Vector3.Distance(hand, theirHand);
                if (pose == CrewPose.HighFive)
                    Assert.InRange(apart, 0.05f, 0.09f);
                else
                    Assert.InRange(apart, 0.19f, 0.21f);
                Assert.True(Vector3.Distance(head, theirHead) > 0.45f, $"{pose} {gap}/{across}: heads {Vector3.Distance(head, theirHead):0.00} apart");
            }
    }

    /// <summary>
    /// Note 807: each half of an emote for two is its own clip on its own clock: offered from the offer, met from the meet; a
    /// dance-off's lead in a move for the turn that's never the one danced just before it nor theirs the last time round, the
    /// rest cheering on the same beat; the sit and the lean held on each one's own beat, the lean mirrored to the left.
    /// </summary>
    [Fact]
    public void EachHalfOfAnEmoteIsItsOwnClipOnItsOwnClock()
    {
        Assert.Equal(("high_five", 1.2, true), CreatureArt.EmoteClip(CrewPose.HighFive, default, 1.2));
        Assert.Equal(("high_five_met", 0.3, true), CreatureArt.EmoteClip(CrewPose.HighFive, new EmoteDetail(Met: true, SinceMeet: 0.3), 1.2));
        Assert.Equal(("fist_bump", 2.0, true), CreatureArt.EmoteClip(CrewPose.FistBump, default, 2.0));
        Assert.Equal(("fist_bump_met", 0.1, true), CreatureArt.EmoteClip(CrewPose.FistBump, new EmoteDetail(Met: true, SinceMeet: 0.1), 2.0));
        Assert.Equal(("dance_off_cheer", 0.5, true), CreatureArt.EmoteClip(CrewPose.DanceOff, new EmoteDetail(Dancers: 2, Round: 7, OnBeat: 0.5), 9));
        for (int n = 2; n <= 6; n++)
            for (int round = 1; round < 40; round++)
            {
                int Move(int r) => CreatureArt.DanceOffMoveOf(new EmoteDetail(Dancers: n, Round: r));
                Assert.NotEqual(Move(round - 1), Move(round));
                if (round >= n)
                    Assert.NotEqual(Move(round - n), Move(round));
                var lead = CreatureArt.EmoteClip(CrewPose.DanceOff, new EmoteDetail(Dancers: n, Leading: true, Round: round, OnBeat: 0.5), 9)!.Value;
                Assert.Equal(("dance_off", Move(round) * 2 + 0.5), (lead.Clip, lead.Time));
            }
        Assert.Equal(("sit_edge", 4.0, false), CreatureArt.EmoteClip(CrewPose.SitEdge, default, 4.0));
        Assert.Equal("lean_out", CreatureArt.EmoteClip(CrewPose.LeanOut, new EmoteDetail(Out: 1), 1)!.Value.Clip);
        Assert.Equal("lean_out_left", CreatureArt.EmoteClip(CrewPose.LeanOut, new EmoteDetail(Out: -1), 1)!.Value.Clip);
        Assert.Null(CreatureArt.EmoteClip(CrewPose.Dance, default, 1));
    }

    /// <summary>
    /// Note 807: a leaner's drawn along the sill so the doorway's jamb is where crew_clips' lean_out has its hand: open out
    /// through the doorway just short of <see cref="CrewActs.JambAhead"/> ahead of them, the car's side just past it; the cab's
    /// doorway the same.
    /// </summary>
    [Fact]
    public void ALeanerIsDrawnWithTheJambWhereTheirHandGoes()
    {
        var train = Train();
        foreach (var c in Staging.Emotes(train, Content, "leanout"))
        {
            var frame = train.Frames[c.Car];
            double body = Math.IEEERemainder(c.Yaw - frame.Heading, Math.Tau);
            int ahead = Math.Abs(body) < 0.1 ? -1 : 1, side = c.Local.X > 0 ? 1 : -1;
            bool Open(double along) => new[] { 0.5, 1.3 }.All(up => Sim.Player.Rescue.Clear(train, c.Car,
                frame.ToWorld(new Double3(c.Local.X - side * 0.4, c.Local.Y + up, c.Local.Z + ahead * along)),
                frame.ToWorld(new Double3(c.Local.X + side * 0.4, c.Local.Y + up, c.Local.Z + ahead * along))));
            Assert.True(Open(CrewActs.JambAhead - 0.1), $"leaner {c.Id}: open short of the jamb");
            Assert.False(Open(CrewActs.JambAhead + 0.1), $"leaner {c.Id}: the car's side past it");
        }
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
