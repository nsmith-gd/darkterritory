using DarkTerritory.Sim.Enemies;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The one pacing model's clock (the spawn director overhaul's brief §3; note 908): λ owed a second unless the night's held for
/// a quiet it means, every placed encounter paying one off, what couldn't be placed going to the bounded debt.
/// </summary>
public class EncounterClockTests
{
    static readonly EncountersTuning T = Tuning.Enemies.Director.Encounters;

    [Fact]
    public void TheShippedTuningDrivesTheDirector()
    {
        Assert.True(T.Drive.On);
        Assert.True(T.Drive.DueCap >= 1, "the free channel's threshold must be reachable");
        Assert.True(T.Drive.DueFloor <= 0);
    }

    [Fact]
    public void ItOwesTheRateASecondUntilTheCap()
    {
        var c = EncounterClock.Start;
        for (int s = 0; s < 60; s++)
            c = c.Second(0.5 / 60, null, DebtCause.Ineligible, 4, T);
        Assert.Equal(0.5, c.Due, 9);
        Assert.False(c.Ready);
        for (int s = 0; s < 600; s++)
            c = c.Second(0.5 / 60, null, DebtCause.Ineligible, 4, T);
        Assert.Equal(T.Drive.DueCap, c.Due, 9);
        Assert.True(c.Ready);
    }

    [Theory]
    [InlineData(DebtCause.Grace)]
    [InlineData(DebtCause.SafeZone)]
    [InlineData(DebtCause.Terminus)]
    [InlineData(DebtCause.Recovery)]
    [InlineData(DebtCause.Capped)]
    public void AQuietTheGameMeansOwesNothing(DebtCause quiet)
    {
        var c = EncounterClock.Start;
        for (int s = 0; s < 3600; s++)
            c = c.Second(3.0 / 60, quiet, DebtCause.Ineligible, 4, T);
        Assert.Equal(0, c.Due);
        Assert.Equal(0, c.Debt.Owed);
    }

    /// <summary>The director wanting encounters and finding none it could place: past the cap it's remembered, to the debt's cap.</summary>
    [Fact]
    public void WhatCouldntBePlacedGoesToTheDebt()
    {
        var c = EncounterClock.Start;
        for (int s = 0; s < 1200; s++)
            c = c.Second(1.0 / 60, null, DebtCause.NoPlacement, 4, T);
        Assert.Equal(T.Drive.DueCap, c.Due, 9);
        Assert.True(c.Debt.Owed > 0.5, $"owed {c.Debt.Owed}");
        Assert.True(c.Debt.Owed <= T.Debt.Cap + 1e-9);
    }

    /// <summary>Paid off once a placement, a pack as one, to no lower than the floor; the debt flows back only into room under the cap.</summary>
    [Fact]
    public void APlacementPaysOneAndTheDebtComesBackIntoTheRoom()
    {
        var c = new EncounterClock(0.5, T.Drive.DueCap, 1, new EncounterDebt(T.Debt.Cap, T.Debt.ReleaseBurst));
        c = c.Paid(1, T);
        Assert.Equal(T.Drive.DueCap - 1, c.Due, 9);
        for (int i = 0; i < 10; i++)
            c = c.Paid(1, T);
        Assert.Equal(Math.Min(T.Drive.DueFloor, 0), c.Due, 9);
        // No headroom under the engaged caps: nothing of the debt comes back.
        var held = c.Second(0, null, DebtCause.Ineligible, 0, T);
        Assert.Equal(c.Due, held.Due, 9);
        // Room: it does, never past what the allowance holds.
        var back = c.Second(0, null, DebtCause.Ineligible, 4, T);
        Assert.True(back.Due > c.Due && back.Due - c.Due <= T.Debt.ReleaseBurst + 1e-9);
    }

    [Fact]
    public void TheThresholdIsAboutOne()
    {
        Assert.Equal(1 - T.Drive.Jitter, EncounterClock.NextThreshold(0, T), 9);
        Assert.Equal(1, EncounterClock.NextThreshold(0.5, T), 9);
        Assert.True(EncounterClock.NextThreshold(0.999999, T) <= T.Drive.DueCap, "a threshold past the cap would never fire");
    }

    /// <summary>
    /// The night's free encounters are the rate's: with nothing else placed, a 20-minute night at a steady rate sends about
    /// rate × 20 (the brief §3: "avoid increasing frequency merely through repeated random rolls with no guarantee").
    /// </summary>
    [Theory]
    [InlineData(0.3)]
    [InlineData(0.6)]
    [InlineData(1.2)]
    public void WithNothingElseTheFreeChannelMeetsTheRate(double perMinute)
    {
        var c = EncounterClock.Start;
        var rng = new Ballast.Pcg32(7, 0xD21E);
        int sent = 0;
        for (int s = 0; s < 1200; s++)
        {
            c = c.Second(perMinute / 60, null, DebtCause.Ineligible, 4, T);
            if (c.Ready)
            {
                sent++;
                c = c.Paid(EncounterClock.NextThreshold(rng.NextDouble(), T), T);
            }
        }
        Assert.InRange(sent, (int)Math.Floor(perMinute * 20 * 0.8), (int)Math.Ceiling(perMinute * 20 * 1.1));
    }
}
