using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 310 (queue #49), found sweeping question 12 (note 305): at crew 4 on deadLines:2 the Car Hugger ate a walker who
/// went down its car's end ladder to club it, and its friend on the roof above stood there the ten seconds it had to pull them
/// out (App. A.3). And a harness night started out on the line left every walker in the respawn queue (App. D.1).
/// </summary>
public class CarHuggerRescueTests
{
    [Fact]
    public void AHarnessNightStartedOnTheLineHasItsWholeCrewAboard()
    {
        // Past the gate nobody spawns aboard (D.1): the gunner and fireman were posted, the walkers never were.
        var report = CrewOfTwoTests.Night("deadLines:2", 6, 5, EnemyKind.CarHugger, start: CrewOfTwoTests.ShortOfTheTrestle(), bots: 4);
        Assert.Equal(4, report.Clients.Count);
        Assert.All(report.Clients, c => Assert.True(c.Alive, $"{c.Bot} never came aboard"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    public void AFriendOnTheRoofHaulsASwallowedCrewmateOutOfTheCarHuggersMouth(int seed)
    {
        // Each of these lost two walkers, one after the other, down the end ladder to it with the other on the roof above.
        // The mail cranes are down: once note 278 moved the line, an ammo crane fell 100 m past the trestle, and the walkers
        // were in car 4 at its side door for the bag when it took hold (seeds 1 and 3 were found with the cranes up; 1 and
        // 10 are the ones that eat two without the rescue now).
        var report = CrewOfTwoTests.Night("deadLines:2", 6, 90, EnemyKind.CarHugger, start: CrewOfTwoTests.ShortOfTheTrestle(), bots: 4, seed: seed,
            cranes: false);
        var threats = report.Threats!;
        Assert.False(threats.DeathsByCause.ContainsKey(nameof(DeathCause.Eaten)), string.Join(", ", threats.DeathsByCause));
        Assert.True(threats.Rescues.GetValueOrDefault(nameof(EnemyKind.CarHugger)) > 0, "nobody was pulled out");
    }
}
