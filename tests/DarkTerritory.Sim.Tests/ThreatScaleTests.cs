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
    public void OffItIsTheTiersAlone()
    {
        var off = P with { CrewScale = 0, LengthScale = 0 };
        Assert.Equal(1, off.Scale(1, 3), 9);
        Assert.Equal(1, off.Scale(8, 18), 9);
    }
}
