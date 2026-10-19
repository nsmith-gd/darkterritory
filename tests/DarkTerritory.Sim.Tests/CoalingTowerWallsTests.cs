using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The coaling tower is solid (note 758; the director, 14 Oct, `docs/playtest/2026-10-14.md` line 8: "We're actually clipping
/// a little bit through some of the ... things at the station"). A main-line facility has no stop, so nothing stood where
/// its trestle is drawn; now its faces and footings are walls (no ladder since note 869), the woods stand back from it, and it's
/// gone once Tower Jaw brings it down.
/// </summary>
public class CoalingTowerWallsTests
{
    static readonly PlayerTuning P = Tuning.Player;
    static readonly Route.Route Frontier = RouteGenerator.Generate(Tuning.Route, RouteTier.Frontier, 7);
    static readonly RouteFeature Tower = Frontier.Of(FeatureKind.Facility).First(f => f.Facility == FacilityKind.CoalingTower);

    static World Night(double front)
    {
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 4, 1)), Frontier.Build(), front, Tuning.Boiler);
        var world = new World(train, Tuning.Combat);
        world.EnableRun(Tuning.Run, Frontier, 600, authority: true);
        world.EnableEnemies(Tuning.Enemies, Frontier, 1, crew: 4, authority: true);
        return world;
    }

    /// <summary>The tower's middle on the line, the way out to it (its side), and the line's way along there.</summary>
    static (Double3 Mid, Double3 Out, Double3 Along) Where(TrainOnLine train)
    {
        var t = train.Line.Sample((Tower.Start + Tower.End) / 2);
        var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
        return (t.Position, right * (Tower.Side == 0 ? 1 : Tower.Side), new Double3(t.Tangent.X, 0, t.Tangent.Z).Normalized);
    }

    static double Lateral(PlayerState s, Double3 mid, Double3 outward) => Double3.Dot((s.Position - mid) with { Y = 0 }, outward);

    /// <summary>A crewmate on the ground <paramref name="from"/> m out from the line, <paramref name="along"/> m along from the tower's middle, walking straight out from the line for <paramref name="seconds"/>.</summary>
    static PlayerState WalkOut(World world, double from, double along, double seconds)
    {
        var train = world.Train;
        var (mid, outward, way) = Where(train);
        var s = PlayerMotor.SpawnOnGround(mid + outward * from + way * along, train.Line, Tower.Start, P);
        s.Yaw = DMath.Atan2(-outward.X, -outward.Z);
        for (int i = 0; i < seconds * SimConstants.TickRate; i++)
            PlayerMotor.Step(ref s, new PlayerIntent { MoveZ = 1, LookYaw = 0 }, train, P, Tuning.Train, SimConstants.TickSeconds, applyLook: false);
        return s;
    }

    [Fact]
    public void ACrewmateWalkingOutFromTheLineStopsAtTheTrestle()
    {
        var world = Night(Tower.Start - 2000);
        var (mid, outward, _) = Where(world.Train);
        // At its middle and at a corner: stopped at the near face (3.5 m out, a post's half), never inside it.
        foreach (double along in new[] { 0.0, 3.8 })
        {
            var s = WalkOut(world, 2.0, along, 4);
            Assert.InRange(Lateral(s, mid, outward), 2.2, 3.2);
        }
        // Past its end, the way is open: out beyond its far face (10.5 m).
        var clear = WalkOut(world, 2.0, 7.5, 4);
        Assert.True(Lateral(clear, mid, outward) > 11, $"walked out {Lateral(clear, mid, outward):F1} m past its end");
    }

    [Fact]
    public void NothingStandsOffItsBackButItsFootings()
    {
        // Note 869 (the director's playtest of 19 Oct line 8: "Ladder on the coaling tower but can't climb it?"): no ladder up
        // its back, drawn or stood; nothing of it reaches past its far face but a footing's corner.
        var world = Night(Tower.Start - 2000);
        var (mid, outward, _) = Where(world.Train);
        var walls = world.Train.Walls!.All.Where(w => w.Tower > 0 && ((w.Centre - mid) with { Y = 0 }).Length < 20).ToList();
        Assert.Equal(8, walls.Count); // its four faces and four footings
        foreach (var w in walls)
            Assert.True(Double3.Dot((w.Centre - mid) with { Y = 0 }, outward) <= 7 + 3.5 + 0.01, "a wall off the trestle's back");
    }

    [Fact]
    public void NoTreeStandsInTheTrestle()
    {
        var world = Night(Tower.Start - 2000);
        var (mid, outward, way) = Where(world.Train);
        // Every solid near the tower that isn't the tower's own is out of its footprint (and a little room round it).
        for (double a = -6; a <= 6; a += 1)
            for (double o = 2.5; o <= 12.5; o += 1)
            {
                var p = mid + way * a + outward * o + Double3.Up;
                foreach (var w in world.Train.Walls!.Near(p))
                    if (!w.Trestle && w.ToLocal(p) is var l && Math.Abs(l.X) <= w.HalfLength && Math.Abs(l.Z) <= w.HalfWidth)
                        Assert.Fail($"a wall (owner {w.Owner}) at {a:F0} m along, {o:F0} m out, in the tower's ground");
            }
    }

    [Fact]
    public void BroughtDownByTowerJawItStandsNoMore()
    {
        var world = Night(Tower.Start - 2000);
        var (mid, outward, _) = Where(world.Train);
        int facility = world.Run!.Facilities.ToList().IndexOf(Tower);
        var jaw = world.AddEnemy(id => TowerJaw.AtCoalingTower(id, world, facility, Tuning.Enemies.TowerJaw, gnawed: 0.95));
        // A creature isn't held out of the trestle: the beaver at its leg stays there.
        var leg = jaw.Local;
        world.Step(new TrainControls { Reverser = 1, Brake = 1 });
        Assert.True(((jaw.Local - leg) with { Y = 0 }).Length < 0.05, "Tower Jaw kept at its leg");
        // AtCoalingTower starts it no more than 0.95 through: about six seconds more.
        for (int i = 0; i < 10 * SimConstants.TickRate && jaw.Mode != TowerJawMode.Wreck; i++)
        {
            world.BeginTick();
            world.Step(new TrainControls { Reverser = 1, Brake = 1 });
        }
        Assert.Equal(TowerJawMode.Wreck, jaw.Mode);
        Assert.True(world.Train.Walls!.Felled(facility + 1));
        var s = WalkOut(world, 2.0, 0, 4);
        Assert.True(Lateral(s, mid, outward) > 11, "walks out through where it stood");
        // A client comes to the same from the wreck in its snapshot.
        var client = Night(Tower.Start - 2000);
        client.MirrorEnemies([jaw]);
        Assert.True(client.Train.Walls!.Felled(facility + 1));
    }
}
