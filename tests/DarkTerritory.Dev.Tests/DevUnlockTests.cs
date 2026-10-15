using Ballast;
using DarkTerritory.Dev.Smithy;
using DarkTerritory.Sim.Campaign;

namespace DarkTerritory.Dev.Tests;

/// <summary>
/// The blacksmith's unlock (ARCHITECTURE §8 note 747; the director, 14 Oct 2026: "a dev unlock mode where I can just
/// infinitely buy whatever I want. From the Smith."): free, any tier, as often as you like, and off again.
/// </summary>
public class DevUnlockTests
{
    static readonly CampaignTuning C = DataFile.Load<CampaignTuning>(Path.Combine(DataFile.FindContentRoot(), CampaignTuning.File));

    [Fact]
    public void EverythingIsFreeAtAnyTierAndComesOffAgain()
    {
        // A crew with nothing and a two-car train: the tools for the deep territory aren't sold to it, but are given to it here.
        var broke = new CampaignState { Cars = C.StartingCars, Scrip = 0 };
        var locked = C.Upgrades.First(u => Campaign.Unavailable(C, broke, u) is not null);
        Assert.False(Campaign.BuyUpgrade(C, broke, locked.Id).Ok);
        var s = broke;
        foreach (var u in C.Upgrades)
        {
            var p = DevUnlock.Buy(C, s, u.Id);
            Assert.True(p.Ok, p.Refused);
            s = p.State;
            Assert.Contains(u.Id, s.Upgrades);
            Assert.Equal(0, s.Scrip);
        }
        // Owned, choosing it again takes it off; and on again.
        var off = DevUnlock.Buy(C, s, locked.Id).State;
        Assert.DoesNotContain(locked.Id, off.Upgrades);
        Assert.Contains(locked.Id, DevUnlock.Buy(C, off, locked.Id).State.Upgrades);
    }

    [Fact]
    public void WhatWontGoWithItComesOffForIt()
    {
        var s = DevUnlock.Buy(C, new CampaignState(), "theHonk").State;
        s = DevUnlock.Buy(C, s, "goldWhistle").State;
        Assert.Equal(["goldWhistle"], s.Upgrades);
        s = DevUnlock.Buy(C, s, "cucarachaHorn").State;
        Assert.Equal(["cucarachaHorn"], s.Upgrades);
    }

    [Fact]
    public void TheRowTurnsItOnAndOff()
    {
        var unlock = new DevUnlock();
        Assert.Contains("OFF", unlock.Row().Item.Label, StringComparison.Ordinal);
        unlock.Row().Choose();
        Assert.True(unlock.On);
        Assert.Contains("ON", unlock.Row().Item.Label, StringComparison.Ordinal);
    }
}
