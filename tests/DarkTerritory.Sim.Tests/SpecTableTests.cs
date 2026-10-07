using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Pins the sim to the systems spec (Part B). If a designer retunes content/tuning/train.json,
/// these fail on purpose: update the expected values here and in docs/design/systems-spec.md together.
/// </summary>
public class SpecTableTests
{
    static readonly TrainTuning T = Tuning.Train;

    [Theory]
    // T97 playtest: brakes twice the first pass's; T121: 0.7 of that.
    [InlineData(3, 10, 108)]
    [InlineData(6, 15, 164)]
    [InlineData(10, 22, 239)]
    [InlineData(15, 33, 361)]
    [InlineData(20, 45, 494)]
    public void StopFromMaxSpeedMatchesSpecB5(int cars, double seconds, double metres)
    {
        var r = TrainScenarios.StopFrom(T, cars, T.MaxSpeed);
        Assert.InRange(r.Seconds, seconds - 1, seconds + 1);
        Assert.InRange(r.Metres, metres * 0.98, metres * 1.02);
    }

    [Theory]
    [InlineData(3, 9.2)]
    [InlineData(10, 4.3)]
    [InlineData(15, 2.8)]
    [InlineData(20, 1.8)]
    public void MaxClimbableGradeMatchesSpecB5(int cars, double grade)
    {
        var dyn = new TrainDynamics(Consist.Uniform(T, cars, 1));
        Assert.InRange(dyn.MaxClimbableGradePercent(), grade - 0.1, grade + 0.1);
        Assert.True(TrainScenarios.Climb(T, cars, grade - 0.2).Holds);
        Assert.False(TrainScenarios.Climb(T, cars, grade + 0.2).Holds);
    }

    [Fact]
    public void ThreePercentGradeIsTrivialEarlyAndImpossibleAtTwentyCars()
    {
        Assert.True(TrainScenarios.Climb(T, 3, 3).Holds);
        Assert.False(TrainScenarios.Climb(T, 20, 3).Holds);
    }

    [Theory]
    [InlineData(3, 66)]
    [InlineData(10, 175)]
    [InlineData(20, 330)]
    public void ConsistLengthMatchesSpecB4(int cars, double metres) =>
        Assert.InRange(Consist.Uniform(T, cars, 1).LengthMetres, metres - 1, metres + 1);

    [Fact]
    public void TwentyCarRoofTraverseTakesAboutNinetyFourSeconds() =>
        Assert.InRange(Consist.Uniform(T, 20, 1).LengthMetres / Tuning.Player.RoofRun, 93, 95);

    /// <summary>
    /// Spec B.8 (the director's decision of 6 Oct 2026, note 270): one route length for every tier, 24 km, so one dawn timer:
    /// 24 km ÷ 11 m/s + 40% = 51 min. Transit at cruise (spec B.3's 14 m/s) is the total run's floor, 28.6 min.
    /// </summary>
    [Fact]
    public void RunLengthMatchesSpecB8()
    {
        var r = Tuning.Route;
        Assert.Equal(24, r.NightLengthKm);
        Assert.Equal(11, r.DawnAverageSpeed);
        Assert.Equal(0.4, r.DawnSlack, 6);
        Assert.InRange(r.NightLengthKm * 1000 / r.DawnAverageSpeed * (1 + r.DawnSlack) / 60, 50.5, 51.5);
        Assert.InRange(r.NightLengthKm * 1000 / 14 / 60, 28, 29);
    }

    /// <summary>GDD v1.4 App. D.13: auto-bookmarks per run, cap 12 (range 8-20); priority derailment cinematic > PUNISH > GRAB start.</summary>
    [Fact]
    public void AutoBookmarksMatchGddD13()
    {
        var b = Tuning.Run.Bookmarks;
        Assert.Equal(12, b.AutoCap);
        Assert.InRange(b.AutoCap, 8, 20);
        int Rank(DarkTerritory.Sim.Run.BookmarkKind k) => b.Priority.ToList().IndexOf(k);
        Assert.True(Rank(DarkTerritory.Sim.Run.BookmarkKind.Derail) < Rank(DarkTerritory.Sim.Run.BookmarkKind.Punish));
        Assert.True(Rank(DarkTerritory.Sim.Run.BookmarkKind.Punish) < Rank(DarkTerritory.Sim.Run.BookmarkKind.Grab));
        Assert.True(Rank(DarkTerritory.Sim.Run.BookmarkKind.Derail) >= 0);
    }
}
