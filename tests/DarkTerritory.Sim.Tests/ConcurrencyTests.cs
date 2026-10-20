using DarkTerritory.Sim.Enemies;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Concurrency and overlap by crew and train length (the director's brief §1 B–C, §6; note 906): the caps grow with the
/// living crew and the cars, gradually, and stay bounded; a crew of one is never set a pair only two can answer; a crew
/// that's losing gets relief. On the shipped tuning.
/// </summary>
public class ConcurrencyTests
{
    static readonly ConcurrencyTuning T = Tuning.Enemies.Director.Encounters.Concurrency;
    static readonly DirectorTuning D = Tuning.Enemies.Director;

    [Fact]
    public void TheEngagedCapGrowsWithTheLivingAndTheCarsAndIsBounded()
    {
        for (int cars = 1; cars <= 30; cars++)
            for (int p = 1; p <= 12; p++)
            {
                int cap = Concurrency.EngagedCap(p, cars, T);
                Assert.InRange(cap, 1, T.Max);
                Assert.True(Concurrency.EngagedCap(p + 1, cars, T) >= cap, $"crew {p + 1} on {cars} under crew {p}");
                Assert.True(Concurrency.EngagedCap(p, cars + 1, T) >= cap, $"{cars + 1} cars under {cars}, crew {p}");
                // Never more than perCrewmateMax each.
                Assert.True(cap <= p * T.PerCrewmateMax);
            }
        Assert.Equal(0, Concurrency.EngagedCap(0, 8, T));
    }

    [Fact]
    public void TheCapsAreTheOnesTheTuningSays()
    {
        // enemies.json director.encounters.concurrency's comment, so the comment and the numbers can't drift apart.
        Assert.Equal(1, Concurrency.EngagedCap(1, 2, T));
        Assert.Equal(1, Concurrency.EngagedCap(1, 6, T));
        Assert.Equal(2, Concurrency.EngagedCap(1, 7, T));
        Assert.Equal(2, Concurrency.EngagedCap(2, 4, T));
        Assert.Equal(3, Concurrency.EngagedCap(2, 6, T));
        Assert.Equal(4, Concurrency.EngagedCap(2, 8, T));
        Assert.Equal(4, Concurrency.EngagedCap(4, 4, T));
        Assert.Equal(7, Concurrency.EngagedCap(4, 8, T));
        Assert.Equal(11, Concurrency.EngagedCap(8, 6, T));
        Assert.Equal(12, Concurrency.EngagedCap(8, 7, T));
        Assert.Equal(1, Concurrency.PerCrewmate(6, T));
        Assert.Equal(2, Concurrency.PerCrewmate(7, T));
        Assert.Equal(1, Concurrency.AmbientRoom(12, T));
        Assert.Equal(2, Concurrency.AmbientRoom(13, T));
    }

    [Fact]
    public void ThereIsNoBreakpointAtEightCars()
    {
        // The share climbs the same amount for every car past carsFrom until it tops out: 7→8 and 8→9 are like 4→5.
        double step = Concurrency.Share(T.CarsFrom + 1, T) - Concurrency.Share(T.CarsFrom, T);
        for (int cars = T.CarsFrom + 1; Concurrency.Share(cars + 1, T) < T.ShareMax; cars++)
            Assert.Equal(step, Concurrency.Share(cars + 1, T) - Concurrency.Share(cars, T), 9);
        // And for a crew of four, the cap goes up by at most one a car either side of eight.
        for (int cars = 4; cars <= 12; cars++)
            Assert.InRange(Concurrency.EngagedCap(4, cars + 1, T) - Concurrency.EngagedCap(4, cars, T), 0, 1);
    }

    [Fact]
    public void ALongTrainHoldsMoreWithTheSameCrew()
    {
        foreach (int p in new[] { 1, 2, 4, 8 })
            Assert.True(Concurrency.EngagedCap(p, 18, T) > Concurrency.EngagedCap(p, 3, T), $"crew {p}");
        Assert.True(Concurrency.AmbientRoom(18, T) > Concurrency.AmbientRoom(3, T));
    }

    [Fact]
    public void ASoloCrewIsNeverSetAPairOnlyTwoCanAnswer()
    {
        Assert.NotEmpty(T.PairMinCrew);
        foreach (var (pair, need) in T.PairMinCrew)
        {
            // Every named pair is one of the conflict table's, so a typo can't silently protect nothing.
            var halves = pair.Split('+');
            Assert.Contains(D.Conflicts, c => c.Length == 2 && (c[0] == halves[0] && c[1] == halves[1] || c[0] == halves[1] && c[1] == halves[0]));
            Assert.False(Concurrency.PairAllowed(pair, need - 1, T), pair);
            Assert.True(Concurrency.PairAllowed(pair, need, T), pair);
            // Either order.
            Assert.False(Concurrency.PairAllowed($"{halves[1]}+{halves[0]}", need - 1, T), pair);
        }
        Assert.False(Concurrency.PairAllowed("cinderHounds+hotbox", 1, T));
        Assert.True(Concurrency.PairAllowed("cinderHounds+hotbox", 2, T));
        // A pair not named needs one: the Car Hugger on a grade is the solo's own problem.
        Assert.True(Concurrency.PairAllowed("carHugger+grade", 1, T));
    }

    [Fact]
    public void ABiggerCrewIsSetMorePairs()
    {
        Assert.Equal(1, Concurrency.PairsWanted(1, 1, T));
        Assert.Equal(1, Concurrency.PairsWanted(1, 2, T));
        Assert.Equal(2, Concurrency.PairsWanted(1, 4, T));
        Assert.Equal(3, Concurrency.PairsWanted(1, 8, T));
        Assert.Equal(0, Concurrency.PairsWanted(0, 8, T));
        for (int p = 1; p < 8; p++)
            Assert.True(Concurrency.PairsWanted(2, p + 1, T) >= Concurrency.PairsWanted(2, p, T));
    }

    [Fact]
    public void ACrewThatsLosingGetsRelief()
    {
        Assert.Equal(1, Concurrency.Relief(4, 4, 0, T));
        Assert.True(Concurrency.Relief(2, 4, 0, T) < Concurrency.Relief(3, 4, 0, T));
        Assert.True(Concurrency.Relief(3, 4, 0, T) < 1);
        // Hurt counts as well, and never takes it below zero.
        Assert.True(Concurrency.Relief(4, 4, 2, T) < 1);
        Assert.InRange(Concurrency.Relief(1, 8, 1, T), 0, 1);
        Assert.Equal(0, Concurrency.Relief(0, 4, 0, T));
        // Late joiners (alive over the count) aren't a bonus.
        Assert.Equal(1, Concurrency.Relief(5, 4, 0, T));
    }
}
