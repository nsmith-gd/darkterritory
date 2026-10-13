using DarkTerritory.Sim.Enemies;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The threat scales by the crew and by the train (note 646; the director, the inbox, 9 Oct: "Scale by both, higher numbers
/// on both readings should increase threat level"): the pressure's rate, which is what sets how often things come on a 7 km
/// night, × a crew scale and a length scale, 1 at a crew of four and a Frontier train.
/// </summary>
public class ThreatScaleTests
{
    static readonly PressureTuning P = Tuning.Enemies.Director.Pressure;

    [Fact]
    public void HigherNumbersOnBothReadingsIncreaseTheThreat()
    {
        Assert.True(P.CrewScale > 0 && P.LengthScale > 0);
        Assert.Equal(1, P.Scale(P.ScaleCrew, P.ScaleCars), 9);
        for (int crew = 1; crew < 8; crew++)
            Assert.True(P.Scale(crew + 1, P.ScaleCars) > P.Scale(crew, P.ScaleCars), $"crew {crew} → {crew + 1}");
        for (int cars = 3; cars < 20; cars++)
            Assert.True(P.Scale(P.ScaleCrew, cars + 1) > P.Scale(P.ScaleCrew, cars), $"cars {cars} → {cars + 1}");
        // Both at once multiply; neither falls under the floor however small.
        Assert.Equal(P.Scale(8, P.ScaleCars) * P.Scale(P.ScaleCrew, 15), P.Scale(8, 15), 9);
        Assert.True(P.Scale(1, 1) >= P.ScaleFloor * P.ScaleFloor);
    }

    [Fact]
    public void ALongerTrainHasMoreOfTheCrewUnderAThreatAtOnceNeverMoreThanOneEach()
    {
        // The ramp below a long train (note 646), with the long train at note 647's 13 cars: the director's roster
        // (note 652) has it from 8, where the ramp has nothing left to do.
        var o = Tuning.Enemies.Director.Orchestrator with { LongTrainCars = 13 };
        Assert.Equal(o.EngagedPerActive, o.EngagedShare(P.ScaleCars, P.ScaleCars), 9);
        Assert.Equal(o.EngagedPerActive, o.EngagedShare(4, P.ScaleCars), 9);
        Assert.True(o.EngagedShare(P.ScaleCars + 3, P.ScaleCars) > o.EngagedPerActive);
        // A crew of four: three at once on a Frontier train, four short of a long one.
        Assert.Equal(3, (int)Math.Ceiling(4 * o.EngagedShare(8, P.ScaleCars) - 1e-9));
        Assert.Equal(4, (int)Math.Ceiling(4 * o.EngagedShare(o.LongTrainCars - 1, P.ScaleCars) - 1e-9));
        Assert.Equal(o.PerPlayer, o.PerPlayerFor(o.LongTrainCars - 1));
    }

    [Fact]
    public void ALongTrainCanPutTwoOnOneCrewmate()
    {
        // Note 647 (the director, the inbox, 12 Oct: "b"): from longTrainCars, two threats each. The director's roster on
        // the Creature Tweaker (note 652, 13 Oct) sets it at 8: a Frontier train and longer.
        var o = Tuning.Enemies.Director.Orchestrator;
        Assert.Equal(8, o.LongTrainCars);
        Assert.Equal(o.PerPlayer, o.PerPlayerFor(o.LongTrainCars - 1));
        Assert.Equal(2, o.PerPlayerFor(o.LongTrainCars));
        Assert.Equal(2, o.PerPlayerFor(18));
        Assert.Equal(8, (int)Math.Ceiling(4 * o.EngagedShare(18, P.ScaleCars) - 1e-9));
        Assert.Equal(1, (o with { LongTrainCars = 0 }).PerPlayerFor(40));
    }

    [Fact]
    public void OffItIsTheTiersAlone()
    {
        var off = P with { CrewScale = 0, LengthScale = 0 };
        Assert.Equal(1, off.Scale(1, 3), 9);
        Assert.Equal(1, off.Scale(8, 18), 9);
    }
}
