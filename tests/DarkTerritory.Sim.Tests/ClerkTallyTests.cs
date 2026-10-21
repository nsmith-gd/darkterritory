using Ballast;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 972 (upgrades.md §8.5: "the tally reads 'Bar: eight drinks'"): the yard clerk counts what the silly and whimsy
/// upgrades did tonight, in the lines campaign.json gives them: the bar's drinks, the deck's beers and grill, Gerald taken,
/// and so on, the night's counts read in at the gate, and the same on every client from the report.
/// </summary>
public class ClerkTallyTests
{
    static readonly CampaignTuning C = DataFile.Load<CampaignTuning>(Path.Combine(DataFile.FindContentRoot(), CampaignTuning.File));
    static readonly PlayerTuning P = Tuning.Player;

    [Fact]
    public void ACountIsReadInAndAReadingIsChosenByWhetherThereWasAny()
    {
        var counts = new Dictionary<string, int> { ["barDrinks"] = 8, ["gnomesTaken"] = 2 };
        Assert.Equal("Bar: 8 poured.", Radio.Fill("Bar: {barDrinks} poured.", counts));
        Assert.Equal("Gnomes: 2 taken.", Radio.Fill("{gnomesTaken?Gnomes: # taken.|Gnomes: bolted.}", counts));
        Assert.Equal("Gerald: aboard.", Radio.Fill("{geraldTaken?Gerald. Body not recovered. Fee 0.|Gerald: aboard.}", counts));
        // A name the night didn't count is nought; the engine's name is the session's, left for it; no braces, as it was.
        Assert.Equal("Balls: thrown, 0.", Radio.Fill("Balls: thrown, {ballsThrown}.", counts));
        Assert.Equal("Engine: {engine}.", Radio.Fill("Engine: {engine}.", counts));
        Assert.Equal("Horn: comedic.", Radio.Fill("Horn: comedic.", counts));
    }

    [Fact]
    public void TheClerkReadsTheNightsDrinksAndGeraldTakenFromTheWorld()
    {
        string[] owned = ["diningCar", "bbqDeck", "gerald", "canary"];
        var t = Campaign.Campaign.Apply(C, owned, new Loadout(Tuning.Train, Tuning.Boiler, Tuning.Combat, Tuning.Enemies)).Train;
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(t, 6, 1)), new RailLine(new LineDefinition("t", [new TrackSegment(20_000)])), 1_000);
        var w = new World(train, Tuning.Combat);
        w.EnableBodies();
        w.Stock();
        // Two drinks poured at the bar.
        int car = train.Vehicles.Single(v => v.Kind == VehicleKind.Dining).Id;
        var s = new PlayerState { Parent = car, Position = train.Frames[car].Shape.Dining!.Bar, Surface = Surface.Deck, Health = P.Health, Yaw = Math.PI, Kit = P.StartingKit };
        for (int n = 0; n < 2; n++)
        {
            Act(w, ref s, new PlayerIntent { Buttons = PlayerButtons.Use });
            w.Bodies.Remove(w.Bodies.CarriedBy(1)!);
            Act(w, ref s, default);
        }
        // Three beers out of the cooler; the Gannet's had Gerald.
        train.Vehicles[0].Beers -= 3;
        w.Whimsy = w.Whimsy with { Taken = 1 };
        var counts = w.UpgradeCounts();
        Assert.Equal(2, counts["barDrinks"]);
        Assert.Equal(3, counts["beers"]);
        Assert.Equal(1, counts["geraldTaken"]);
        var report = new RunReport(RunEnd.Delivered, 1000, 12, 3, 0, 2, 1240, 40, 12, 30, 1158, 3, 0) { Counts = counts };
        var lines = Radio.Tally(report, Campaign.Campaign.TallyLines(C, owned));
        Assert.Contains("Car: candles. Taken: 2.", lines);
        Assert.Contains("Deck: barbecue. Taken: 3. Food: 0.", lines);
        Assert.Contains("Gerald. Body not recovered. Fee 0.", lines);
        Assert.Contains("Canary: singing.", lines);
        // The report reaches the clients as JSON (Messages.ReportMessages): the counts with it, so they read the same.
        var copy = System.Text.Json.JsonSerializer.Deserialize<RunReport>(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(report))!;
        Assert.Equal(lines, Radio.Tally(copy, Campaign.Campaign.TallyLines(C, owned)));
    }

    static void Act(World w, ref PlayerState s, PlayerIntent intent)
    {
        w.BeginTick();
        w.CrewAct(ref s, intent, 1);
        w.Step(new TrainControls { Reverser = 1, Brake = 1 });
    }
}
