using Ballast;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 484 (queue #221): the bots against a pack that moves (D1's #208, note 472). The pack fight (Heed.Hounds) went at the
/// nearest hound within 20 m in a straight line, wherever it was: one dropped into the car under the walker (it can't bite
/// them there, note 471, nor they it), or one in the air over a gap, mid-leap (run at, the walker ran off the roof's end).
/// </summary>
public class PackFightTests
{
    static readonly PlayerTuning P = Tuning.Player;

    static (Night N, CinderHound Hound, PlayerState Walker) Staged(bool inside, HoundMode mode)
    {
        var n = new Night(4, speed: 10);
        const int car = 2;
        var f = n.Train.Frames[car];
        var room = f.Shape.Interior!.Value;
        var hound = n.World.AddEnemy(id => new CinderHound(id, 900));
        var local = inside ? new Double3(room.Centre.X, room.Min.Y, 2) : new Double3(0.6, f.Shape.RoofHeight, 2);
        // (Its mode rides in Lateral: mode × 4 + facing.)
        hound.Restore(SpinePhase.Commit, 0, Tuning.Enemies.CinderHounds.Health, car, local, 0, (int)mode * 4, 0.6, hound.Id, 0);
        var walker = PlayerMotor.SpawnOnRoof(n.Train, car, -2, P);
        return (n, hound, walker);
    }

    [Theory]
    [InlineData(true, HoundMode.Patrol, false)]  // dropped into the car under the walker: not one to fight from the roof
    [InlineData(false, HoundMode.Leap, false)]   // in the air, mid-leap: not one to run at
    [InlineData(false, HoundMode.Patrol, true)]  // on the roof with the walker: the pack fight, as ever
    public void TheFitGoAtAHoundTheyCanFightAndOnlyThat(bool inside, HoundMode mode, bool fights)
    {
        var (n, hound, walker) = Staged(inside, mode);
        Assert.Equal(fights, Heed.Fightable(hound, n.Train, walker));
        var idle = new PlayerIntent();
        var intent = Heed.Hounds(idle, walker, n.World, 1);
        // Going at it or swinging, or left as it was.
        bool acted = intent.MoveZ != 0 || intent.LookYaw != 0 || intent.Actions != idle.Actions;
        Assert.Equal(fights, acted);
    }
}
