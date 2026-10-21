using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 945 (the director's playtest of 20 Oct evening, line 19: "I think I just saw a gaunt. Oh, but it just disappeared. Okay.
/// Why would it just disappear?"): the two ways a Gaunt was taken out of the world in front of the crew.
/// </summary>
public class GauntSightTests
{
    static readonly EnemyTuning E = Tuning.Enemies;
    static readonly PlayerTuning P = Tuning.Player;

    /// <summary>A crewmate on the ballast beside car 2 of a standing train, and a Gaunt leaving from beside them, empty-handed.</summary>
    static (Night N, Gaunt G) Leaving(EnemyTuning e)
    {
        var n = new Night(4, speed: 0, enemies: e);
        var frame = n.Train.Frames[2];
        var beside = frame.ToWorld(new Double3(frame.Shape.HalfWidth + 1.5, 0, 0));
        double hint = n.Train.Dynamics.Distance;
        var ground = beside with { Y = PlayerMotor.GroundAt(beside, n.Train.Line, ref hint) };
        n.Crew[1] = PlayerMotor.SpawnOnGround(ground, n.Train.Line, hint, P);
        var g = n.World.AddEnemy(id => Gaunt.Asleep(id, ground + frame.Right * 1.0, e.Gaunt));
        g.Restore(SpinePhase.BreakOff, 0, g.Health, Enemy.Loose, ground + frame.Right * 1.0, hint, 0, 0, -1, 0);
        return (n, g);
    }

    [Fact]
    public void LeavingItsGoneOnlyOutOfTheCrewsSight()
    {
        // Before: gone the moment it was clearedAt (30 m) off the train, with the crewmate who watched it go 30 m off.
        var (was, before) = Leaving(E with { Gaunt = E.Gaunt with { GoneUnseenAt = 0 } });
        was.Run(30);
        Assert.True(before.Gone);
        // Now: on across the ground while they're out there within goneUnseenAt, and gone once it's past it.
        var (n, g) = Leaving(E);
        n.Run(30);
        Assert.False(g.Gone, $"{g.Phase}");
        var at = g.WorldPosition(n.Train);
        Assert.InRange((at - PlayerMotor.WorldPosition(n.Crew[1], n.Train)).Length, E.Gaunt.ClearedAt, E.Gaunt.GoneUnseenAt);
        n.Run(E.Gaunt.GoneUnseenAt / E.Gaunt.LeaveSpeed);
        Assert.True(g.Gone, $"{g.Phase}, {(g.WorldPosition(n.Train) - PlayerMotor.WorldPosition(n.Crew[1], n.Train)).Length:0} m off");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FollowingItsWakerItIsntDismissedAsStuckWhenTheFlagSaysSo(bool spares)
    {
        // The director's stuck rule (World.Unmet, T114): telegraphing lingerSeconds × 1.5 and it's gone, wherever the crew is. A
        // Gaunt following its waker (kept from attacking here) is in its telegraph all that time, at their side.
        var quiet = HoundRunTests.Quiet;
        var e = quiet with
        {
            Gaunt = quiet.Gaunt with { AttackAt = 1000 },
            Director = quiet.Director with { StuckSparesTheMet = spares },
        };
        var n = new Night(4, speed: 0, enemies: e);
        var frame = n.Train.Frames[2];
        var beside = frame.ToWorld(new Double3(frame.Shape.HalfWidth + 1.5, 0, 0));
        double hint = n.Train.Dynamics.Distance;
        var ground = beside with { Y = PlayerMotor.GroundAt(beside, n.Train.Line, ref hint) };
        n.Crew[1] = PlayerMotor.SpawnOnGround(ground, n.Train.Line, hint, P);
        var g = n.World.AddEnemy(id => Gaunt.WokenBy(id, ground + frame.Right * 2.0, 1, e.Gaunt));
        n.Run(e.Director.LingerSeconds * 1.5 + 10);
        Assert.True((g.WorldPosition(n.Train) - PlayerMotor.WorldPosition(n.Crew[1], n.Train)).Length < e.Director.LingerRadius || g.Gone);
        Assert.Equal(!spares, g.Gone);
    }
}
