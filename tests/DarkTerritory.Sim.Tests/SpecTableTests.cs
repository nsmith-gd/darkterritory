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

    /// <summary>Spec B.12, the Moose (the director's decisions of 7 Oct 2026; note 339).</summary>
    [Fact]
    public void TheMooseMatchesB12()
    {
        var m = Tuning.Enemies.Moose;
        Assert.Equal((20.0, 50.0), (m.ListenAt, m.WarnAt));
        Assert.Equal((20.0, 25.0, 12.0, 70.0), (m.CrowdAt, m.CrowdPerSecond, m.CloseAt, m.ClosePerSecond));
        Assert.Equal((15.0, 40, 40.0), (m.HearVoice, m.TalkingAbove, m.VoicePerSecond));
        Assert.Equal((25.0, 25.0, 15.0), (m.TrainPassAt, m.TrainPass, m.CalmPerSecond));
        Assert.Equal((2.5, 30.0), (m.SquareUpSeconds, m.SquareUpAt[1]));
        Assert.Equal((11.0, 8.0, 2.5, 3.2, 4.0), (m.ChargeSpeed, m.Overrun, m.WheelSeconds, m.RackSpan, m.SnagSeconds));
        Assert.Equal((60, 40.0, 12.0), (m.ChargeDamage, m.GrabBelowHealth, m.PinSeconds));
        Assert.Equal((25.0, 2.5, 80.0), (m.SearchSeconds, m.SearchSpeed, m.LeashRadius));
        Assert.Equal((3.0, 15.0), (m.RamEvery, m.RamSeconds));
        Assert.Equal((3.2, 6.0), (m.TrackClearance, m.MovingClearance));
        Assert.Equal([1.0, 1.5, 2.0, 2.5], Enum.GetValues<DarkTerritory.Sim.Route.RouteTier>().Select(t => DarkTerritory.Sim.Enemies.MooseTuning.ByTier(m.TierWeights, t)));
        Assert.Equal([2.0, 3.0, 4.0, 5.0], Enum.GetValues<DarkTerritory.Sim.Route.RouteTier>().Select(t => DarkTerritory.Sim.Enemies.MooseTuning.ByTier(m.Lineside, t)));
    }

    /// <summary>Spec B.13, the Gannet (the director's decisions of 7 Oct 2026; note 340).</summary>
    [Fact]
    public void TheGannetMatchesB13()
    {
        var g = Tuning.Enemies.Gannet;
        Assert.Equal((18.0, 30.0, 12.0, 6.0, 60.0, 180.0), (g.ArriveAbove, g.ArriveSeconds, g.StallBelow, g.StallSeconds, g.QuietSeconds, g.ReturnSeconds));
        Assert.Equal([20.0, 35.0], g.SoarHeight);
        Assert.Equal((0.8, 2.0, 1.6), (g.PreyAbove, g.HangSeconds, g.FoldSeconds));
        Assert.Equal((0.9, 35, 4.0), (g.StrikeRadius, g.StabDamage, g.StuckSeconds));
        Assert.Equal([8.0, 12.0], g.DiveEvery);
        Assert.Equal((2.5, 200.0, 4, 3.0, 3), (g.BankSeconds, g.MarkReach, g.Pecks, g.PeckEvery, g.DriveOffBlows));
        Assert.Equal((12.0, 4.0), (g.Health, g.GiveUpBelow));
        Assert.Equal([1.0, 1.5, 2.0, 2.5], Enum.GetValues<DarkTerritory.Sim.Route.RouteTier>().Select(t => DarkTerritory.Sim.Enemies.MooseTuning.ByTier(g.TierWeights, t)));
    }
}
