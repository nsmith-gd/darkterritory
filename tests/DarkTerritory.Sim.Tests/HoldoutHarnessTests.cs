using DarkTerritory.Sim.Net;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// GDD App. D.14, "what the harness verifies": every row, as `dt holdouts check` runs it, smaller (two sites on the
/// networked night, a few nights for the wallet). Each has to pass having tested something.
/// </summary>
[Collection(nameof(LineGenTests))]
public class HoldoutHarnessTests
{
    static readonly IReadOnlyList<HoldoutCheck> Checks = HoldoutChecks.Run(new HoldoutCheckOptions
    {
        Route = LineGen.Routes.Generate(Ballast.DataFile.FindContentRoot(), "frontier:7", 6),
        VoteRoute = LineGen.Routes.Generate(Ballast.DataFile.FindContentRoot(), "deadLines:3", 10),
        Train = Tuning.Train,
        Player = Tuning.Player,
        Boiler = Tuning.Boiler,
        Combat = Tuning.Combat,
        Enemies = Tuning.Enemies,
        Run = Tuning.Run,
        Holdouts = Tuning.Holdouts,
        Rescues = 2,
        Nights = 4,
        QueueOps = 2000,
    });

    public static TheoryData<string> Rows => new(
        "No open-world spawns", "Recoverability", "Queue integrity", "Release and reassign",
        "No farming", "Dead silence", "Vote bounds", "Channel isolation");

    [Theory]
    [MemberData(nameof(Rows))]
    public void EveryRowOfD14Passes(string row)
    {
        var check = Assert.Single(Checks, c => c.Name == row);
        Assert.True(check.Passed, $"{row}: " + string.Join("; ", check.Faults));
        Assert.Contains(check.Measured.Values, v => v > 0);
    }

    [Fact]
    public void TheNetworkedNightFreedSomeoneInsideAHoldoutAndCameBackForAnother()
    {
        var spawns = Assert.Single(Checks, c => c.Name == "No open-world spawns").Measured;
        Assert.True(spawns["inHoldouts"] >= 2);
        Assert.Equal(spawns["midRun"], spawns["inHoldouts"]);
        Assert.True(Assert.Single(Checks, c => c.Name == "Release and reassign").Measured["returns"] >= 1);
        Assert.True(Assert.Single(Checks, c => c.Name == "Channel isolation").Measured["liveMicFramesAtTheDoor"] > 0);
    }
}
