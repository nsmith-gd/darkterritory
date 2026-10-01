using DarkTerritory.Sim.Bots;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// T81: the driver stopping for a lit Holdout (T96) slows to the speed that stands it 5 m short, and only alongside starts
/// the wait it gives up after. A train that came to a stand a hair further short was neither: it stood till the dawn.
/// </summary>
public class HoldoutStandTests
{
    [Theory]
    [InlineData(4.0, 3.0, true)]
    [InlineData(5.02, 0.0, true)]
    [InlineData(14.0, 0.0, true)]
    [InlineData(5.02, 0.4, false)]
    [InlineData(40.0, 0.0, false)]
    [InlineData(200.0, 12.0, false)]
    public void StandingJustShortOfTheHoldoutIsAlongsideIt(double ahead, double speed, bool alongside) =>
        Assert.Equal(alongside, ConductorBot.AlongsideTheHoldout(ahead, speed));
}
