using Ballast;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Pins content/tuning/holdouts.json to GDD App. D.13. If a designer retunes one of these, the test fails on purpose:
/// update it and the appendix together.
/// </summary>
public class HoldoutTuningTests
{
    static readonly HoldoutTuning T = Tuning.Holdouts;

    [Fact]
    public void DefaultsMatchD13()
    {
        var p = T.Placement;
        Assert.Equal([60, 200], p.FacilityFromConsistM);
        Assert.Equal(40, p.HaltFromMainM);
        Assert.Equal(80, p.TownFromMainM);
        Assert.Equal(200, p.SecondPadScaleM);
        Assert.Contains("switchyard", p.SecondAtKinds);
        Assert.Equal(5, T.SecondCrew);
        Assert.Equal(400, T.ReleaseM);
        Assert.Equal(200, T.CallOut.ActiveRadiusM);
        Assert.Equal(60, T.CallOut.AudibleM);
        Assert.Equal(7, T.CallOut.CooldownSeconds);
        Assert.Equal(new BreachStep(BreachTool.Melee, 3, "cannon"), T.Breach.Smash);
        Assert.Equal(new BreachStep(BreachTool.Melee, 6, "machinery"), T.Breach.Pry);
        Assert.Equal(new BreachStep(BreachTool.RepairKit, 6, "none"), T.Breach.Open);
        Assert.Equal(80, T.FreedHealth);
        Assert.Equal(0.5, T.FeeShare);
        Assert.Equal(0.75, T.RefundShare);
        Assert.Equal(0.4, T.SoloClimb);
        Assert.Equal(1.2, T.Vote.PerVote);
        Assert.Equal(1.5, T.Vote.Cap);
        Assert.Equal(1, T.Vote.PerRun);
        Assert.Equal(1, T.Commendations.PerPlayer);
        Assert.Equal(["Came Back For Me", "Held the Switch", "Kept the Fire", "Brought Them Home", "Last One Standing"], T.Commendations.Awards);
    }

    [Fact]
    public void EachTypeIsFreedTheD4Way()
    {
        Assert.Equal([BreachMethod.Smash, BreachMethod.Open], T.Methods[HoldoutType.PrisonCar]);
        Assert.Equal([BreachMethod.Pry], T.Methods[HoldoutType.BarricadedShelter]);
        Assert.Equal([BreachMethod.Smash], T.Methods[HoldoutType.HaltLockup]);
    }

    [Theory]
    [InlineData(450, 225, 169, -56)]
    [InlineData(700, 350, 263, -87)]
    [InlineData(1100, 550, 413, -137)]
    [InlineData(1700, 850, 638, -212)]
    public void FeesAndRefundsMatchTheD9Table(double perCar, double fee, double refund, double net)
    {
        var body = BodyRecord.For(T, perCar, 1, 1, dropOut: false, []);
        Assert.Equal(fee, body.Fee);
        Assert.Equal(refund, body.Refund);
        Assert.Equal(net, body.Refund - body.Fee);
        Assert.Equal(refund, body.LootValue);
        var dropped = BodyRecord.For(T, perCar, 2, 1, dropOut: true, []);
        Assert.Equal(0, dropped.Fee);
        Assert.Equal(0, dropped.Refund);
    }

    [Fact]
    public void VotesCompoundUpToTheCap()
    {
        Assert.Equal(1, T.Vote.Multiplier(0));
        Assert.Equal(1.2, T.Vote.Multiplier(1), 9);
        Assert.Equal(1.44, T.Vote.Multiplier(2), 9);
        Assert.Equal(1.5, T.Vote.Multiplier(3), 9);
        Assert.Equal(1.5, T.Vote.Multiplier(8), 9);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.2)]
    public void ARefundThatWouldPayForADeathIsRefused(double refund)
    {
        var e = Assert.Throws<InvalidDataException>(() => (T with { RefundShare = refund }).Validate());
        Assert.Contains("refundShare", e.Message);
    }

    [Fact]
    public void TheRefundNeverCoversTheWholeFeeEvenRounded()
    {
        var near = T with { RefundShare = 0.999 };
        foreach (double perCar in new[] { 2.0, 3, 10, 450, 1700 })
        {
            var b = BodyRecord.For(near.Validate(), perCar, 1, 1, false, []);
            Assert.True(b.Refund < b.Fee, $"{perCar}: {b.Refund} of {b.Fee}");
        }
    }

    [Theory]
    [InlineData("secondCrew")]
    [InlineData("releaseM")]
    [InlineData("callOut")]
    [InlineData("soloClimb")]
    public void D13RangesAreHeld(string what)
    {
        var bad = what switch
        {
            "secondCrew" => T with { SecondCrew = 7 },
            "releaseM" => T with { ReleaseM = 100 },
            "callOut" => T with { CallOut = T.CallOut with { CooldownSeconds = 2 } },
            _ => T with { SoloClimb = 1.6 },
        };
        Assert.Throws<InvalidDataException>(() => bad.Validate());
    }

    [Fact]
    public void OccupantsAreAlwaysAdults()
    {
        Assert.All(T.Survivors.VoiceSets, v => Assert.True(v.Adult));
        var child = T with { Survivors = T.Survivors with { VoiceSets = [.. T.Survivors.VoiceSets, new SurvivorVoiceSet("child", "wildlander", false, ["x"])] } };
        var e = Assert.Throws<InvalidDataException>(() => child.Validate());
        Assert.Contains("adult", e.Message);
    }

    [Fact]
    public void TheFileLoadsWithEveryValueGiven()
    {
        var loaded = DataFile.Load<HoldoutTuning>(Path.Combine(DataFile.FindContentRoot(), HoldoutTuning.File));
        Assert.Equal(T, loaded.Validate() with { Methods = T.Methods, Placement = T.Placement, Survivors = T.Survivors, Commendations = T.Commendations });
        Assert.Equal(3, loaded.Placement.Size[HoldoutType.PrisonCar].Length);
        Assert.True(loaded.Placement.Tries > 0 && loaded.Placement.WalkStepM > 0 && loaded.Placement.RouteClearanceM > 0 && loaded.Placement.BoardEyeM > 0);
        Assert.True(loaded.Breach.ReachM > 0);
    }
}
