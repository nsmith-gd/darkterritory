using Ballast;
using DarkTerritory.Sim.Campaign;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The silly upgrades (ARCHITECTURE §8 note 701; the director, 13 Oct 2026: approved, "hilarious", "super funny"): each a
/// thing on the train (composition.dress, for the art), the moose head's and the disco ball's prices in the night.
/// </summary>
public class DressTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly CampaignTuning C = DataFile.Load<CampaignTuning>(Path.Combine(Content, CampaignTuning.File));
    static readonly Loadout Base = new(Tuning.Train, Tuning.Boiler, Tuning.Combat, Tuning.Enemies);

    static Loadout With(params string[] upgrades) => Campaign.Campaign.Apply(C, upgrades, Base);

    static readonly string[] Sillies = ["fuzzyDice", "bumperStickers", "flames", "spinnerHubcaps", "tinyCaboose", "cucarachaHorn", "airFreshener",
        "participationRibbon", "monogrammedTowels", "mooseHead", "discoBall"];

    [Fact]
    public void EachIsOnTheTrainOnceBoughtAndNotBefore()
    {
        Assert.Empty(Base.Train.Composition.Dress);
        foreach (var id in Sillies)
        {
            var u = Assert.Single(C.Upgrades, u => u.Id == id);
            Assert.NotNull(u.Tally);
            Assert.NotNull(u.Line);
            Assert.Equal([id], With(id).Train.Composition.Dress);
        }
        // All at once, each the once.
        Assert.Equal(Sillies.Order(StringComparer.Ordinal), With([.. Sillies, "flames"]).Train.Composition.Dress.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheMooseTakesTheHeadPersonallyAndTheFliesComeToDance()
    {
        Assert.Equal(Tuning.Enemies.Moose.TrainPass * 2, With("mooseHead").Enemies!.Moose.TrainPass, 9);
        Assert.Equal(Tuning.Enemies.FireFlies.StoppedWeight * 1.5, With("discoBall").Enemies!.FireFlies.StoppedWeight, 9);
        // Nothing else of the night changed for the rest.
        foreach (var id in Sillies.Except(["mooseHead", "discoBall"]))
            Assert.Equal(Base.Enemies, With(id).Enemies);
    }

    [Fact]
    public void TheHornIsTheWhistleSoItGoesWithNoOtherWhistle()
    {
        var horn = C.Upgrades.Single(u => u.Id == "cucarachaHorn");
        Assert.Contains("theHonk", horn.Excludes);
        Assert.Contains("goldWhistle", horn.Excludes);
        Assert.Contains("cucarachaHorn", C.Upgrades.Single(u => u.Id == "theHonk").Excludes);
        Assert.Contains("cucarachaHorn", C.Upgrades.Single(u => u.Id == "goldWhistle").Excludes);
        // And the Choir hears it as the whistle it is.
        Assert.Equal(Base.Combat, With("cucarachaHorn").Combat);
    }

    [Fact]
    public void TheStickersArePunsThatLeaveTheChildrenAndThePassengerAlone()
    {
        // GDD §23.1: the Soot Children and the Passenger stay straight (docs/design/upgrades.md §8.6).
        string text = File.ReadAllText(Path.Combine(Content, "world", "dress.json"));
        var json = System.Text.Json.Nodes.JsonNode.Parse(text, documentOptions: new() { CommentHandling = System.Text.Json.JsonCommentHandling.Skip })!;
        var stickers = json["stickers"]!.AsArray().Select(n => (string)n!).ToList();
        Assert.True(stickers.Count >= 12);
        Assert.Equal(stickers.Count, stickers.Distinct().Count());
        Assert.All(stickers, s =>
        {
            Assert.DoesNotContain("CHILD", s, StringComparison.Ordinal);
            Assert.DoesNotContain("SOOT", s, StringComparison.Ordinal);
            Assert.DoesNotContain("PASSENGER", s, StringComparison.Ordinal);
            Assert.InRange(s.Length, 4, 48);
        });
        Assert.All(json["named"]!.AsArray(), n => Assert.Contains("{NAME}", (string)n!, StringComparison.Ordinal));
        Assert.NotEmpty(json["towels"]!.AsArray());
    }
}
