using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The Freight Beetle (GDD §21, App. A.6, B.6; the director's brief of 8 Oct 2026; ARCHITECTURE §8 note 366). Rule: it
/// pushes away from whoever's nearest; stand where you want it not to go. Still with nobody about; it takes the nearest
/// loose freight and shoves it straight away from the nearest player; two players steer it; blows drive it off and kill
/// it; it never harms anyone.
/// </summary>
public class FreightBeetleTests
{
    static readonly FreightBeetleTuning B = Tuning.Enemies.FreightBeetle;
    static readonly PlayerTuning P = Tuning.Player;

    static Double3 Beside(Night n, int car, double lateral, double along = 0)
    {
        var at = n.Train.Frames[car].ToWorld(new Double3(lateral, 0, along));
        double hint = n.Train.Dynamics.Distance;
        return at with { Y = PlayerMotor.GroundAt(at, n.Train.Line, ref hint) };
    }

    static PlayerState Ground(Night n, Double3 at, Double3? toward = null)
    {
        var s = PlayerMotor.SpawnOnGround(at, n.Train.Line, n.Train.Dynamics.Distance, P);
        if (toward is { } to)
        {
            var d = to - at;
            s.Yaw = DMath.Atan2(-d.X, -d.Z);
        }
        return s;
    }

    /// <summary>The night with its bodies stepped too (the crate's own physics carries the push).</summary>
    static void Run(Night n, double seconds, Func<int, PlayerIntent>? intent = null)
    {
        for (int i = 0; i < seconds * SimConstants.TickRate; i++)
        {
            n.Run(SimConstants.TickSeconds, intent);
            n.World.StepBodies([.. n.Crew.Select(c => (c.Key, c.Value))]);
        }
    }

    static (FreightBeetle Beetle, Body Crate) Stage(Night n, double lateral = 14)
    {
        var at = Beside(n, 2, lateral);
        var crate = n.World.Bodies.SpawnCargo(at, n.Train.Dynamics.Distance);
        var beetle = n.World.AddEnemy(id => FreightBeetle.At(id, at + new Double3(2.5, 0, 0), n.Train.Dynamics.Distance, 0, B));
        return (beetle, crate);
    }

    static double Flat(Double3 v) => (v with { Y = 0 }).Length;

    [Fact]
    public void WithNobodyAboutItSitsStill()
    {
        var n = new Night(4, speed: 0);
        var (beetle, crate) = Stage(n);
        var was = crate.Centre;
        Run(n, 20);
        Assert.Equal(BeetleMode.Idle, beetle.Mode);
        Assert.True(Flat(crate.Centre - was) < 0.05);
    }

    [Fact]
    public void ItPushesTheNearestFreightStraightAwayFromTheNearestPlayer()
    {
        var n = new Night(4, speed: 0);
        var (beetle, crate) = Stage(n);
        var start = crate.Centre;
        // A crewmate 8 m off the crate, along the line from it.
        var them = start + new Double3(0, 0, 8);
        n.Crew[1] = Ground(n, them, toward: start);
        Run(n, 15);
        Assert.Equal(crate.Id, beetle.Load);
        var moved = (crate.Centre - start) with { Y = 0 };
        Assert.True(moved.Length > 4, $"moved {moved.Length:0.0} m");
        // Away from them: along −Z, within a few degrees.
        var away = (start - them) with { Y = 0 };
        Assert.True(Double3.Dot(moved.Normalized, away.Normalized) > 0.95, $"pushed {moved.Normalized}");
        Assert.Equal(P.Health, n.Crew[1].Health);
        n.AssertFair();
    }

    [Fact]
    public void TwoPlayersSteerIt()
    {
        var n = new Night(4, speed: 0);
        var (beetle, crate) = Stage(n);
        var start = crate.Centre;
        n.Crew[1] = Ground(n, start + new Double3(0, 0, 8), toward: start);
        Run(n, 10);
        var first = (crate.Centre - start) with { Y = 0 };
        // A second crewmate comes round in front of it: now they're nearest, and it turns away from them.
        var mid = crate.Centre;
        n.Crew[2] = Ground(n, mid + new Double3(-5, 0, -2.5), toward: mid);
        Run(n, 12);
        var second = (crate.Centre - mid) with { Y = 0 };
        Assert.True(first.Length > 2 && second.Length > 2);
        // It went one way, then turned with the nearest player: well off its first line.
        Assert.True(Double3.Dot(first.Normalized, second.Normalized) < 0.8, $"{first.Normalized} then {second.Normalized}");
    }

    [Fact]
    public void ThreeBlowsDriveItOffItsLoadAndSixKillIt()
    {
        var n = new Night(4, speed: 0);
        var (beetle, crate) = Stage(n);
        var start = crate.Centre;
        n.Crew[1] = Ground(n, start + new Double3(0, 0, 8), toward: start);
        Run(n, 6);
        Assert.Equal(BeetleMode.Push, beetle.Mode);
        // Up beside it (at its flank, out of its head's reach) with a crowbar.
        void Blow()
        {
            var at = beetle.Local;
            var side = new Double3(DMath.Cos(beetle.Lateral), 0, -DMath.Sin(beetle.Lateral));
            n.Crew[2] = Ground(n, at + side * 1.6, toward: at);
            Run(n, Tuning.Enemies.Melee.SwingSeconds + 0.1, id => id == 2 ? new PlayerIntent { Actions = PlayerActions.Swing } : default);
        }
        for (int i = 0; i < B.DriveOffBlows; i++)
            Blow();
        Assert.Equal(BeetleMode.Away, beetle.Mode);
        Assert.Null(beetle.Load);
        for (int i = 0; i < 6 && !beetle.Gone; i++)
            Blow();
        Assert.True(beetle.Gone);
        Assert.All(n.Crew.Values, s => Assert.Equal(P.Health, s.Health));
    }

    [Fact]
    public void ACrewmateAtItsHeadStartlesIt()
    {
        var n = new Night(4, speed: 0);
        var (beetle, crate) = Stage(n);
        var start = crate.Centre;
        n.Crew[1] = Ground(n, start + new Double3(0, 0, 8), toward: start);
        Run(n, 6);
        var head = beetle.Local + beetle.Facing * B.HeadAt;
        n.Crew[2] = Ground(n, head + beetle.Facing * 0.5, toward: beetle.Local);
        Run(n, 0.2);
        Assert.Equal(BeetleMode.Startle, beetle.Mode);
        Assert.Equal(P.Health, n.Crew[2].Health);
    }
}
