using DarkTerritory.Sim.Enemies;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Bounded encounter debt (the spawn director overhaul's brief §3; note 876): what the director couldn't place is remembered
/// in part, capped, forgotten over time, and paid back at a release rate, never as a burst after a safe zone, never past the
/// caller's headroom, and never for a quiet the game means.
/// </summary>
public class EncounterDebtTests
{
    static readonly EncounterDebtTuning T = Tuning.Enemies.Director.Encounters.Debt;

    [Fact]
    public void TheShippedTuningIsLoaded()
    {
        Assert.True(T.On);
        Assert.True(T.Cap > 0);
        Assert.True(T.MeanLifeSeconds > 0);
        Assert.True(T.ReleasePerMinute > 0);
        Assert.True(T.Share[EncounterDebt.Key(DebtCause.NoPlacement)] > 0);
    }

    [Fact]
    public void AnUnplacedEncounterIsOwedInPart()
    {
        var d = new EncounterDebt().Owe(1, DebtCause.NoPlacement, T);
        Assert.Equal(T.Share["noPlacement"], d.Owed, 12);
    }

    [Theory]
    [InlineData(DebtCause.SafeZone)]
    [InlineData(DebtCause.Grace)]
    [InlineData(DebtCause.Recovery)]
    [InlineData(DebtCause.Terminus)]
    [InlineData(DebtCause.Capped)]
    public void AnIntendedQuietOwesNothingWhateverTheTuningSays(DebtCause cause)
    {
        var greedy = T with { Share = new() { [EncounterDebt.Key(cause)] = 1 } };
        var d = new EncounterDebt();
        for (int i = 0; i < 100; i++) d = d.Owe(1, cause, greedy);
        Assert.Equal(0, d.Owed);
    }

    [Fact]
    public void TheDebtIsCapped()
    {
        var d = new EncounterDebt();
        for (int i = 0; i < 1000; i++) d = d.Owe(1, DebtCause.NoPlacement, T);
        Assert.Equal(T.Cap, d.Owed, 12);
    }

    [Fact]
    public void TheDebtDecaysAndNeverGoesNegative()
    {
        var d = new EncounterDebt(T.Cap, 0);
        double last = d.Owed;
        for (int s = 0; s < 3600; s++)
        {
            d = d.Decay(1, T);
            Assert.True(d.Owed <= last && d.Owed >= 0);
            last = d.Owed;
        }
        Assert.True(d.Owed < 0.01 * T.Cap, $"an hour on, still owed {d.Owed}");
        // About a mean life on, about 1/e of it is left (stepped linearly, so near it, not exactly).
        var m = new EncounterDebt(1, 0);
        for (int s = 0; s < (int)T.MeanLifeSeconds; s++) m = m.Decay(1, T);
        Assert.InRange(m.Owed, 0.3, 0.42);
    }

    [Fact]
    public void NothingIsRepaidPastWhatsOwedTheAllowanceOrTheHeadroom()
    {
        var d = new EncounterDebt(1.5, 0.8);
        d.Take(10, T, out double taken);
        Assert.Equal(0.8, taken, 12);
        d.Take(0.25, T, out taken);
        Assert.Equal(0.25, taken, 12);
        d.Take(0, T, out taken);
        Assert.Equal(0, taken);
        d.Take(-1, T, out taken);
        Assert.Equal(0, taken);
        var after = new EncounterDebt(0.3, 1).Take(10, T, out taken);
        Assert.Equal(0.3, taken, 12);
        Assert.Equal(0, after.Owed, 12);
        Assert.Equal(0.7, after.Allowance, 12);
    }

    /// <summary>
    /// A full backlog built while held in the yard comes back at the release rate once the train leaves: never more than the
    /// allowance's burst at once, and no more in any minute than the release rate allows on top of it.
    /// </summary>
    [Fact]
    public void NoBurstAfterTheYard()
    {
        var d = new EncounterDebt(T.Cap, T.ReleaseBurst);
        // Ten minutes held in a safe zone: nothing repaid, and the allowance kept empty.
        for (int s = 0; s < 600; s++)
        {
            d = d.Decay(1, T).Hold();
            d = d.Take(10, T, out double held);
            Assert.Equal(0, held);
        }
        // Topped up to the cap at the yard's gate (the worst case), then let go: repaid a second at a time with all the headroom.
        d = new EncounterDebt(T.Cap, d.Allowance);
        double[] perSecond = new double[600];
        for (int s = 0; s < perSecond.Length; s++)
        {
            d = d.Decay(1, T).Take(10, T, out perSecond[s]);
        }
        Assert.Equal(0, perSecond[0], 1e-9 + T.ReleasePerMinute / 60);
        for (int s = 0; s + 60 <= perSecond.Length; s++)
        {
            double minute = perSecond.AsSpan(s, 60).ToArray().Sum();
            Assert.True(minute <= T.ReleasePerMinute + 1e-9, $"{minute} repaid in the minute from {s} s");
        }
        Assert.True(perSecond.Sum() > 0, "the debt never came back");
    }

    [Fact]
    public void OffOwesAndRepaysNothing()
    {
        var off = T with { On = false };
        var d = new EncounterDebt().Owe(1, DebtCause.NoPlacement, off);
        Assert.Equal(0, d.Owed);
        new EncounterDebt(1, 1).Take(1, off, out double taken);
        Assert.Equal(0, taken);
    }

    /// <summary>The same calls give the same debt, bit for bit (replays and the host's night).</summary>
    [Fact]
    public void ItIsDeterministic()
    {
        static EncounterDebt Run()
        {
            var d = new EncounterDebt();
            double sum = 0;
            for (int s = 0; s < 1200; s++)
            {
                if (s % 7 == 0) d = d.Owe(0.37, DebtCause.NoPlacement, T);
                if (s % 11 == 0) d = d.Owe(0.5, DebtCause.Ineligible, T);
                d = s is > 300 and < 420 ? d.Decay(1, T).Hold() : d.Decay(1, T);
                d = d.Take(s % 3 == 0 ? 1 : 0.1, T, out double taken);
                sum += taken;
            }
            return d with { Allowance = d.Allowance + sum };
        }
        var a = Run();
        var b = Run();
        Assert.Equal(BitConverter.DoubleToInt64Bits(a.Owed), BitConverter.DoubleToInt64Bits(b.Owed));
        Assert.Equal(BitConverter.DoubleToInt64Bits(a.Allowance), BitConverter.DoubleToInt64Bits(b.Allowance));
    }
}
