using Ballast;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The fun batch's presentation (ARCHITECTURE §8 note 714): the conga's line walking after its leader, and the yard
/// photographer's picture, taken once as she comes out of the yard to him, on a machine that saw her in it.
/// </summary>
public class FunBatchArtTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TrainTuning T = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
    static readonly CombatTuning C = DataFile.Load<CombatTuning>(Path.Combine(Content, CombatTuning.File));

    static World World()
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(20_000)]));
        return new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 4, 1)), line, 5_000), C);
    }

    static PlayerState Stood(double z) => new() { Parent = PlayerState.World, Position = new Double3(0, 0, z), Health = 100, Surface = Surface.Ground };

    [Fact]
    public void TheCongaFollowsTheOneWhoStartedBefore()
    {
        var world = World();
        world.Emotes.Add(new EmoteEvent(1, world.Tick, 1, Emote.Conga));
        world.Emotes.Add(new EmoteEvent(2, world.Tick, 2, Emote.Conga));
        var crew = new List<(int Id, PlayerState State)> { (1, Stood(0)), (2, Stood(6)), (3, Stood(9)) };
        // The leader leads; whoever isn't dancing it isn't in it.
        Assert.Null(Conga.Follow(1, crew[0].State, world, crew));
        Assert.Null(Conga.Follow(3, crew[2].State, world, crew));
        // The second walks on after the first (toward −Z, ahead of them here).
        var step = Assert.NotNull(Conga.Follow(2, crew[1].State, world, crew));
        Assert.True(step.MoveZ != 0 || step.MoveX != 0);
        // Closed up behind them, they stand.
        crew[1] = (2, Stood(Conga.Gap));
        Assert.Null(Conga.Follow(2, crew[1].State, world, crew));
    }

    [Fact]
    public void ThePhotographIsTakenOnceAsSheComesOutToHimHavingBeenSeenInTheYard()
    {
        var route = RouteGenerator.Generate(RouteTuning.Load(Content), RouteTier.Frontier, 7);
        string[] dress = ["yardPhotographer"];
        var inYard = new PrototypeSession(Content, route, enemies: false);
        double gate = inYard.World.Run!.YardLength;
        Assert.Equal(Sim.Run.RunPhase.Yard, inYard.World.Run.Phase);
        var outside = new PrototypeSession(Content, route, enemies: false, at: gate + YardPhotographer.PastGate - YardPhotographer.ShootAt + 1);
        outside.World.Run!.Mirror(Sim.Run.RunPhase.Underway, Sim.Run.RunEnd.None, 30, -1, false, [.. Enumerable.Repeat(0.0, outside.World.Run.FacilityCount)]);

        // Not bought, nothing; joined on the way (never saw the yard), nothing.
        Assert.False(new YardPhotographer().Due(outside, []));
        Assert.False(new YardPhotographer().Due(outside, dress));

        var p = new YardPhotographer();
        Assert.False(p.Due(inYard, dress));
        Assert.True(p.Due(outside, dress));
        var frame = Enumerable.Repeat((byte)200, 640 * 360 * 4).ToArray();
        var photo = p.Keep(frame, 640, 360, now: 100);
        Assert.Equal((YardPhotographer.Width, YardPhotographer.Height), (photo.Width, photo.Height));
        // Taken: not again, the flash going off and the print on the HUD a while.
        Assert.False(p.Due(outside, dress));
        Assert.Equal(1, p.Flash(100), 9);
        Assert.Equal(0, p.Flash(100 + YardPhotographer.FlashSeconds), 9);
        Assert.True(p.Shown(100 + YardPhotographer.ShowSeconds - 0.1));
        Assert.False(p.Shown(100 + YardPhotographer.ShowSeconds + 0.1));
        // Sepia: warm, red over green over blue; the corners darker than the middle.
        int mid = (YardPhotographer.Height / 2 * YardPhotographer.Width + YardPhotographer.Width / 2) * 4;
        Assert.True(photo.Rgba[mid] > photo.Rgba[mid + 1] && photo.Rgba[mid + 1] > photo.Rgba[mid + 2]);
        Assert.True(photo.Rgba[0] < photo.Rgba[mid]);
        p.Clear();
        Assert.Null(p.Photo);
    }

    [Fact]
    public void HeStandsPastTheGateOffTheLineFacingBackAtTheEngine()
    {
        var route = RouteGenerator.Generate(RouteTuning.Load(Content), RouteTier.Frontier, 7);
        var s = new PrototypeSession(Content, route, enemies: false);
        double gate = s.World.Run!.YardLength;
        var (feet, facing) = YardPhotographer.Stand(s.Train.Line, gate);
        var there = s.Train.Line.Sample(gate + YardPhotographer.PastGate);
        Assert.Equal(YardPhotographer.Side, (feet - there.Position).Length, 3);
        // Facing back down the line, toward the yard.
        Assert.True(Double3.Dot(facing, there.Tangent) < -0.8);
        // His lens looks at the engine.
        var lens = YardPhotographer.Lens(s.Train.Line, gate, s.Train);
        var toEngine = (s.Train.Frames[0].Origin - lens.Position).Normalized;
        var forward = lens.Forward;
        Assert.True(Double3.Dot(new Double3(forward.X, forward.Y, forward.Z), toEngine) > 0.9);
    }
}
