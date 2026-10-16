using Ballast;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Enemies;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// A woken Gaunt is drawn beside its waker, not on them (note 823; the director, 15 Oct, on the Look Review: "it stands
/// right on the player when triggered. Needs to stand next to the player"). The sim keeps it at their back at
/// <c>followAt</c>; it's drawn stood back from there, where a swing and the guns still find the sim's point.
/// </summary>
public class GauntStoodBackTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly EnemyTuning Enemies = DataFile.Load<EnemyTuning>(Path.Combine(Content, EnemyTuning.File));

    static double Flat(Double3 a, Double3 b) => ((a - b) with { Y = 0 }).Length;

    [Fact]
    public void AtItsWakersBackItsDrawnAStepClearOfThemWhereTheirBlowsAndTheGunsStillFindIt()
    {
        var waker = new Double3(10, 0.4, -5);
        var at = waker + new Double3(0.6, 0, 0.8).Normalized * Enemies.Gaunt.FollowAt;
        var (drawn, _) = CreatureArt.GauntStoodBack(at, waker, null, 0);
        Assert.Equal(CreatureArt.GauntStandOff, Flat(drawn, waker), 6);
        // Its feet on the ground the sim stands it on.
        Assert.Equal(at.Y, drawn.Y, 9);
        // On the same bearing: the sim's point is straight ahead of anyone facing it, and in reach of their swing.
        var toDrawn = ((drawn - waker) with { Y = 0 }).Normalized;
        var toSim = ((at - waker) with { Y = 0 }).Normalized;
        Assert.True(Double3.Dot(toDrawn, toSim) > 0.999, "drawn off the sim's bearing");
        Assert.True(Flat(at, waker) <= Enemies.Melee.Reach + 0.8, "the sim's point out of a swing's reach");
        // And inside its chest as the guns find it (its body's widest sphere round the sim's point).
        double chest = Enemies.Bodies["gaunt"].Max(s => s[0]);
        Assert.True(Flat(drawn, at) <= chest + 1e-9, $"drawn {Flat(drawn, at):0.00} m off its body ({chest} m)");
    }

    [Fact]
    public void FurtherOffItsDrawnWhereItIs()
    {
        var waker = new Double3(0, 0, 0);
        var at = new Double3(-2.0, 0, -(CreatureArt.GauntStandOff + CreatureArt.GauntStandOffFades + 0.5));
        var (drawn, _) = CreatureArt.GauntStoodBack(at, waker, Math.PI * 0.5, 1.0 / 60);
        Assert.True(Flat(drawn, at) < 1e-9, $"drawn {Flat(drawn, at):0.000} m from where it is");
    }

    [Fact]
    public void WhenTheyTurnItGoesRoundThemNotThroughThem()
    {
        // The sim walks it at followSpeed straight through them to their new back.
        var waker = new Double3(0, 0, 0);
        var from = new Double3(0, 0, Enemies.Gaunt.FollowAt);
        var to = new Double3(0, 0, -Enemies.Gaunt.FollowAt);
        double step = 1.0 / 60, was = 0;
        bool first = true;
        double? bearing = null;
        for (double t = 0; t < 3; t += step)
        {
            double u = Math.Min(1, t * Enemies.Gaunt.FollowSpeed / (to - from).Length);
            var at = from + (to - from) * u;
            var (drawn, b) = CreatureArt.GauntStoodBack(at, waker, bearing, first ? 0 : step);
            Assert.True(Flat(drawn, waker) >= CreatureArt.GauntStandOff - 1e-9, $"at {t:0.00} s drawn {Flat(drawn, waker):0.00} m from them");
            if (!first)
                Assert.True(Math.Abs(Math.IEEERemainder(b - was, 2 * Math.PI)) <= CreatureArt.GauntGoesRound * step + 1e-9, $"at {t:0.00} s it jumped round them");
            (bearing, was, first) = (b, b, false);
        }
        // And it's come round to their new back.
        Assert.True(Math.Abs(Math.IEEERemainder(was - Math.PI, 2 * Math.PI)) < 1e-6, $"ended on {was:0.00}, not behind them");
    }
}
